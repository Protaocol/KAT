/*
Copyright 2023 KillFrenzy / Evan Tran

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/


Shader "Unlit/KF_KeyboardShader"
{
	Properties
	{
		[Enum(Off,0,Front,1,Back,2)] _Culling ("Culling Mode", Int) = 2
		_MainTex ("Texture Main", 2D) = "white" {}
		_MainTex2 ("Texture 2", 2D) = "white" {}
		_TextureBlend ("Texture Blend", Range(0,1)) = 0
		_Cutoff ("Alpha cutoff", Range(0,1)) = 0.1
		_Dither ("Dither", Range(0,1)) = 0
		_HighlightMap ("Highlight Map", 2D) = "white" {}
		_HighlightSelect ("Highlight Selection", Float) = 0
		_HighlightBrightness ("Highlight Brightness", Float) = 0

		// Live key legends. The glyph on every key is read from the same atlas the text uses, so it is right on any page.
		// The installer fills these in. _KeyKp is animated by the keyboard (0 = page 0, then two keyboard pages per wide atlas page).
		_GlyphArray ("Glyph SDF Array (the text material's)", 2DArray) = "" {}
		_KeyLegend ("Live legends on (0/1)", Float) = 0
		_KeyKp ("Keyboard page (animated)", Float) = 0
		_KeyColorStart ("First emoji page (255 = none)", Float) = 255
		_ColorArray ("Emoji Array (the text material's; optional)", 2DArray) = "" {}
	}
	SubShader
	{
		Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest+100" "IgnoreProjector"="True" }
		LOD 100
		AlphaToMask On
		Cull [_Culling]

		Pass
		{
			CGPROGRAM
			#pragma target 3.5
			#pragma shader_feature_local KAT_KEY_COLOR
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
			#pragma multi_compile_fog

			#include "UnityCG.cginc"
			#include "KAT_KeyboardKeyRects.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				// float2 uv2 : TEXCOORD1;
				// float2 uv3 : TEXCOORD2;
				float4 spos : TEXCOORD3;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;

			sampler2D _MainTex2;
			float4 _MainTex2_ST;

			float _TextureBlend;
			fixed _Cutoff;
			float _Dither;

			sampler2D _HighlightMap;
			float4 _HighlightMap_ST;
			float _HighlightSelect;
			float _HighlightBrightness;

			UNITY_DECLARE_TEX2DARRAY(_GlyphArray);
#ifdef KAT_KEY_COLOR
			UNITY_DECLARE_TEX2DARRAY(_ColorArray);
#endif
			float _KeyLegend;
			float _KeyKp;
			float _KeyColorStart;

			float Seg(float2 p, float2 lo, float2 hi)
			{
				return step(lo.x, p.x) * step(p.x, hi.x) * step(lo.y, p.y) * step(p.y, hi.y);
			}

			// One seven-segment digit; p is 0..1 inside the digit, y up.
			float SevenSeg(int d, float2 p)
			{
				int m = 0;
				if (d == 0) m = 0x3F; else if (d == 1) m = 0x06; else if (d == 2) m = 0x5B; else if (d == 3) m = 0x4F; else if (d == 4) m = 0x66;
				else if (d == 5) m = 0x6D; else if (d == 6) m = 0x7D; else if (d == 7) m = 0x07; else if (d == 8) m = 0x7F; else if (d == 9) m = 0x6F;
				float on = 0.0;
				on = max(on, ((m & 1)  != 0 ? 1.0 : 0.0) * Seg(p, float2(0.12, 0.86), float2(0.88, 1.00)));
				on = max(on, ((m & 2)  != 0 ? 1.0 : 0.0) * Seg(p, float2(0.86, 0.54), float2(1.00, 0.92)));
				on = max(on, ((m & 4)  != 0 ? 1.0 : 0.0) * Seg(p, float2(0.86, 0.08), float2(1.00, 0.46)));
				on = max(on, ((m & 8)  != 0 ? 1.0 : 0.0) * Seg(p, float2(0.12, 0.00), float2(0.88, 0.14)));
				on = max(on, ((m & 16) != 0 ? 1.0 : 0.0) * Seg(p, float2(0.00, 0.08), float2(0.14, 0.46)));
				on = max(on, ((m & 32) != 0 ? 1.0 : 0.0) * Seg(p, float2(0.00, 0.54), float2(0.14, 0.92)));
				on = max(on, ((m & 64) != 0 ? 1.0 : 0.0) * Seg(p, float2(0.12, 0.43), float2(0.88, 0.57)));
				return on;
			}

			// Which atlas page and which half-bank the keyboard is on. kp 0 is page 0 (narrow, 128 slots). After that every wide page is two
			// keyboard pages of 128 slots each: kp 1 = page 1 slots 0..127, kp 2 = page 1 slots 128..254, kp 3 = page 2 slots 0..127, ...
			void KeyPage(out int atlasPage, out int bank)
			{
				int kp = (int)(_KeyKp + 0.5);
				atlasPage = kp == 0 ? 0 : ((kp - 1) >> 1) + 1;
				bank = kp == 0 ? 0 : ((kp - 1) & 1);
			}

			// The legend on glyph key k at this uv: rgb and coverage. Text pages are white SDF glyphs; emoji pages are their own colours.
			float4 KeyGlyph(int k, float2 uv)
			{
				int atlasPage, bank;
				KeyPage(atlasPage, bank);

				// Shift held (texture blend 1) shows the top half of the page; otherwise the lower half. Must match KatKeyboardLayout in the installer.
				int slotInHalf = _TextureBlend > 0.5 ? k : 64 + (k ^ 32);
				int slot = slotInHalf + 128 * bank;

				float4 rect = KAT_KEY_RECT[k];
				float2 local = (uv - rect.xy) / rect.zw;
				float2 g = float2((local.x - 0.12) / 0.76, ((1.0 - local.y) - 0.12) / 0.76);   // glyph box inside the key, y down

				bool narrow = atlasPage == 0;
				float2 cell = g;
				if (narrow) cell.x = (g.x - 0.25) / 0.5;           // narrow glyphs are half as wide as they are tall

				float2 inBox = step(0.0, cell) * step(cell, 1.0);
				float visible = inBox.x * inBox.y;
				// the last two slots of a wide page are reserved for the keyboard's own use, and emoji pages are not drawn
				if (bank == 1 && slot >= 253) visible = 0.0;
				bool isColor = (float)atlasPage >= _KeyColorStart;
#ifndef KAT_KEY_COLOR
				if (isColor) return float4(0.6, 0.6, 0.6, visible * 0.35);   // no emoji array on this material: a grey box
#endif

				float2 uvAtlasDown = float2(((float)(slot & 15) + cell.x) / 16.0, ((float)(slot >> 4) + cell.y) / (narrow ? 8.0 : 16.0));
				float3 uvw = float3(uvAtlasDown.x, 1.0 - uvAtlasDown.y, (float)atlasPage);
				float2 dX = ddx(uvAtlasDown) * float2(1.0, -1.0);
				float2 dY = ddy(uvAtlasDown) * float2(1.0, -1.0);
				if (isColor) uvw = float3(0.001, 0.999, 0.0);                          // the SDF sample below is unused for emoji; keep it in range
				float sd = _GlyphArray.SampleGrad(sampler_GlyphArray, uvw, dX, dY).r;   // always sampled, so fwidth below is well defined
				float aa = max(fwidth(sd), 1e-3);
				float cover = visible * saturate((sd - 0.5) / aa + 0.5);
				float4 legend = float4(0.93, 0.93, 0.93, isColor ? 0.0 : cover);
#ifdef KAT_KEY_COLOR
				float3 cuvw = float3(uvAtlasDown.x, 1.0 - uvAtlasDown.y, max(0.0, (float)(atlasPage - (int)(_KeyColorStart + 0.5))));
				float4 emoji = _ColorArray.SampleGrad(sampler_ColorArray, cuvw, dX, dY);
				if (isColor) legend = float4(emoji.rgb, emoji.a * visible);
#endif
				return legend;
			}

			// The page number display: the atlas page in seven-segment digits, and two pips for which half of the page's slots are on the keys.
			float3 PageDisplay(float2 uv, float3 background)
			{
				int atlasPage, bank;
				KeyPage(atlasPage, bank);
				float4 rect = KAT_KEY_RECT[71];
				float2 local = (uv - rect.xy) / rect.zw;
				float3 green = float3(0.25, 1.0, 0.35);
				float3 col = background;
				float digits[3] = { (float)(atlasPage / 100), (float)((atlasPage / 10) % 10), (float)(atlasPage % 10) };
				[unroll]
				for (int d = 0; d < 3; d++) {
					float x0 = 0.05 + 0.24 * d;
					float2 p = float2((local.x - x0) / 0.20, (local.y - 0.14) / 0.72);
					bool blankDigit = (d == 0 && atlasPage < 100) || (d == 1 && atlasPage < 10);
					float inside = step(0.0, p.x) * step(p.x, 1.0) * step(0.0, p.y) * step(p.y, 1.0);
					float on = blankDigit ? 0.0 : SevenSeg((int)digits[d], p) * inside;
					col = lerp(col, green, on);
				}
				// pips: left = slots 0..127 of the page, right = slots 128..254
				[unroll]
				for (int b = 0; b < 2; b++) {
					float2 c = float2(0.84 + 0.10 * b, 0.5);
					float2 q = abs(local - c) / float2(0.035, 0.16);
					float inPip = step(max(q.x, q.y), 1.0);
					col = lerp(col, (b == bank) ? green : float3(0.08, 0.2, 0.1), inPip);
				}
				return col;
			}

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				// o.uv2 = TRANSFORM_TEX(v.uv, _MainTex2);
				// o.uv3 = TRANSFORM_TEX(v.uv, _HighlightMap);
				o.spos = ComputeScreenPos(o.vertex);
				UNITY_TRANSFER_FOG(o,o.vertex);
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				// sample the texture
				fixed4 col = lerp(tex2D(_MainTex, i.uv), tex2D(_MainTex2, i.uv), _TextureBlend);
				float4 highlightMap = tex2D(_HighlightMap, i.uv);

				// live key legends (see tools/gen_keyboard.py for how the ids are laid out)
				if (_KeyLegend > 0.5) {
					int keyId = (int)round(highlightMap.r * 255.0);
					bool onGlyphKey = highlightMap.b > 0.5 && keyId < 64;
					bool onDisplay = keyId == 71 && highlightMap.b < 0.5;
					// both are evaluated for every pixel (and only used where they apply) so the derivatives inside stay in uniform control flow
					float4 glyph = KeyGlyph(clamp(keyId, 0, 63), i.uv);
					float3 displayCol = PageDisplay(i.uv, col.rgb);
					if (onGlyphKey) col.rgb = lerp(col.rgb, glyph.rgb, glyph.a);
					else if (onDisplay) col.rgb = displayCol;
				}

				// highlight keyboard presses (8-bit texel b is stored as b/255, so invert with *255)
				float highlight1 = round(highlightMap.r * 255.0);
				float highlight2 = round(highlightMap.g * 255.0);
				_HighlightSelect = round(_HighlightSelect);
				if (_HighlightSelect == highlight1 || _HighlightSelect == highlight2) {
					col = float4(clamp(col.rgb * (1.0 + _HighlightBrightness * highlightMap.b), 0.0, 1.0), col.a);
				}

				// dither clip the texture
				float DITHER_THRESHOLDS[16] =
				{
					1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
					13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
					4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
					16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
				};

				float2 pos = i.spos.xy / i.spos.w;
				pos *= _ScreenParams.xy;
				uint index = (uint(pos.x) % 4) * 4 + uint(pos.y) % 4;
				clip((round(col.a) * 1.0 - _Dither) - DITHER_THRESHOLDS[index]);

				// apply fog
				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}
	}
}
