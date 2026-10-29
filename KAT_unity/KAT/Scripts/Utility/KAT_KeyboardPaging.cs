/*
In-game keyboard: pages, halves and live key legends.

The keyboard shows 64 glyph keys. One "keyboard page" is 128 glyph slots, split in two halves of 64: the lower half (slots 64..127) is on the keys by
default and the top half (slots 0..63) is on them while Shift is held. Page 0 (the narrow page) is one keyboard page. Every wide atlas page has up to 255 slots, so
it is two keyboard pages: slots 0..127 and slots 128..254. The page arrows walk through them: 0, 1a, 1b, 2a, 2b, ...

The key map is the same on every page (tools/gen_keyboard.py has the table): the letters sit where QWERTY has them, and the other keys follow in reading order.
Key K (0..63) types slot K in the top half and slot 64 + (K ^ 32) in the lower half. On page 0 that gives A..Z on the letter keys with Shift and a..z without.

This file only has the pieces that need the Unity editor APIs: how many keyboard pages there are, the two clips that tell the shader which page to draw, and
copying the glyph atlas onto the keyboard's material (the shader draws each key's glyph out of the same atlas as the text).
*/

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

using KillFrenzy.AvatarTextTools.Settings;

namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatKeyboardPaging
	{
		private const string KeyboardMaterialPath = "KAT_Misc/KAT_Keyboard";

		// atlasPages = every page of the glyph atlas, emoji pages included. 0 or 1 means there is only page 0.
		public static int KeyboardPageCount(int atlasPages)
		{
			return atlasPages > 1 ? 1 + 2 * (atlasPages - 1) : 1;
		}

		// Two clips that set material._KeyKp to 0 and to the last keyboard page. A 1D blend tree between them, driven by page / last page, gives every page in between.
		public static AnimationClip[] BuildLegendClips(KatSettings settings, int keyboardPages)
		{
			if (keyboardPages < 2) return null;
			EditorCurveBinding binding = EditorCurveBinding.FloatCurve(settings.KeyboardLayoutPath, typeof(MeshRenderer), "material._KeyKp");
			AnimationClip[] clips = new AnimationClip[2];
			for (int i = 0; i < 2; i++) {
				clips[i] = KatPagedClips.LoadOrCreateClip(settings, i == 0 ? "KeyboardLegendFirstPage" : "KeyboardLegendLastPage");
				AnimationUtility.SetEditorCurve(clips[i], binding, AnimationCurve.Constant(0f, 1f / 60f, i == 0 ? 0f : keyboardPages - 1));
				EditorUtility.SetDirty(clips[i]);
			}
			AssetDatabase.SaveAssets();
			return clips;
		}

		// Gives the keyboard's material the same glyph atlas as the text material, and turns the live legends on. Without an atlas the keys stay blank.
		public static void SyncMaterial(Material textMaterial)
		{
			Material keyboard = Resources.Load<Material>(KeyboardMaterialPath);
			if (keyboard == null || !keyboard.HasProperty("_GlyphArray")) return;

			Texture atlas = textMaterial != null && textMaterial.HasProperty("_GlyphArray") ? textMaterial.GetTexture("_GlyphArray") : null;
			keyboard.SetTexture("_GlyphArray", atlas);
			keyboard.SetFloat("_KeyLegend", atlas != null ? 1f : 0f);
			float colorStart = 255f;
			Texture colorArray = textMaterial != null && textMaterial.HasProperty("_ColorArray") ? textMaterial.GetTexture("_ColorArray") : null;
			if (colorArray != null && textMaterial.HasProperty("_ColorPageStart")) colorStart = textMaterial.GetFloat("_ColorPageStart");
			keyboard.SetFloat("_KeyColorStart", colorStart);
			if (keyboard.HasProperty("_ColorArray")) keyboard.SetTexture("_ColorArray", colorArray);
			if (colorArray != null) keyboard.EnableKeyword("KAT_KEY_COLOR"); else keyboard.DisableKeyword("KAT_KEY_COLOR");
			EditorUtility.SetDirty(keyboard);
			AssetDatabase.SaveAssets();
		}
	}
}

#endif
