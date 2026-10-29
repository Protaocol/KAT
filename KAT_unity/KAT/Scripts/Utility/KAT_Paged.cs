/*
Paged glyph extension for KillFrenzyAvatarText.

Copyright 2023 KillFrenzy / Evan Tran
Copyright 2026 Protaocol (MIT) for the original project this builds on.

Adds:
  - KatPagedClips  : generates the Page<j> / PageEnd<j> / PageClear animation clips at install time
                     (64 material floats _Page0.._Page63, one per PAIR of text slots)
  - KatPagedBuilder: menu tool that turns the PNG pages from build_atlas.py into a Texture2DArray
                     and creates the KAT_Text_Paged material that uses the paged shader.
  - Anchor clips   : chest / left-head / right-head / top-head anchor clips for the KAT_HeadAnchor radial layer
  - KatPagedMenu   : menu tool that adds the "KAT Text" submenu (Anchor radial, Text Style radial, Roll Speed radial, and optionally an
                     "Advanced" submenu of material tweaks) to an avatar's expression menu and makes sure the required parameters exist.
  - Emoji pages    : color_page_*.png from build_atlas.py (emoji and emoji images) become a second Texture2DArray, KAT_ColorArray, compressed
                     to BC7 (or RGBA32 if the editor refuses) and hooked up to the shader through _ColorArray, _ColorPageStart and the
                     KAT_COLOR_GLYPHS keyword.
*/

#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

#if VRC_SDK_VRCSDK3
	using VRCAvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
	using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
	using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
	using ExpressionsMenu = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
#endif

using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatPagedClips
	{
		public const string TextPath = "KillFrenzyAvatarText/Constraint/Text";
		public const string ConstraintPath = "KillFrenzyAvatarText/Constraint";
		private const string ClipFolderName = "PageAnimations";

		public static AnimationClip[] GetAnchorClips(KatSettings settings)
		{
			string stalePath = ClipPath(settings, "AnchorHead");
			if (AssetDatabase.LoadAssetAtPath<AnimationClip>(stalePath) != null) {
				AssetDatabase.DeleteAsset(stalePath);
			}
			return new AnimationClip[] {
				GetAnchorClip(settings, "AnchorChest", 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
				GetAnchorClip(settings, "AnchorHeadLeft", 0f, 1f, 0f, 0f, 1f, 0f, 1f, 0f),
				GetAnchorClip(settings, "AnchorHeadRight", 0f, 0f, 1f, 0f, 1f, 1f, 1f, 0f),
				GetAnchorClip(settings, "AnchorHeadTop", 0f, 0f, 0f, 1f, 0f, 0f, 1f, 1f)
			};
		}

		private static AnimationClip GetAnchorClip(KatSettings settings, string name, float chest, float left, float right, float top, float tail, float tailSide, float anchorY, float billboard)
		{
			string path = ClipPath(settings, name);
			AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (clip == null) {
				clip = new AnimationClip();
				clip.name = name;
				AssetDatabase.CreateAsset(clip, path);
			}

			Type constraintType = typeof(UnityEngine.Animations.ParentConstraint);
			SetCurve(clip, EditorCurveBinding.FloatCurve(ConstraintPath, constraintType, "m_Sources.Array.data[" + KatSettings.ConstraintSourceChest + "].weight"), chest);
			SetCurve(clip, EditorCurveBinding.FloatCurve(ConstraintPath, constraintType, "m_Sources.Array.data[" + KatSettings.ConstraintSourceHeadLeft + "].weight"), left);
			SetCurve(clip, EditorCurveBinding.FloatCurve(ConstraintPath, constraintType, "m_Sources.Array.data[" + KatSettings.ConstraintSourceHeadRight + "].weight"), right);
			SetCurve(clip, EditorCurveBinding.FloatCurve(ConstraintPath, constraintType, "m_Sources.Array.data[" + KatSettings.ConstraintSourceHeadTop + "].weight"), top);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._Tail"), tail);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._TailSide"), tailSide);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._AnchorY"), anchorY);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._FaceViewer"), billboard);
			EditorUtility.SetDirty(clip);
			AssetDatabase.SaveAssets();
			return clip;
		}

		// Clips for the advanced menu. Each sets exactly one value on the text, so a layer built from them doesn't depend on any other
		// KAT layer, and it behaves the same with write defaults on or off.
		public static KatAdvancedClips GetAdvancedClips(KatSettings settings)
		{
			KatAdvancedClips c = new KatAdvancedClips();
			c.billboardAuto = GetValueClip(settings, "AdvBillboardAuto", "_FaceViewerMode", 0f);
			c.billboardAlways = GetValueClip(settings, "AdvBillboardAlways", "_FaceViewerMode", 1f);
			c.billboardNever = GetValueClip(settings, "AdvBillboardNever", "_FaceViewerMode", 2f);
			c.growOn = GetValueClip(settings, "AdvGrowOn", "_GrowEnabled", 1f);
			c.growOff = GetValueClip(settings, "AdvGrowOff", "_GrowEnabled", 0f);
			c.boxOn = GetValueClip(settings, "AdvBoxOn", "_BoxEnabled", 1f);
			c.boxOff = GetValueClip(settings, "AdvBoxOff", "_BoxEnabled", 0f);
			c.shrinkOn = GetValueClip(settings, "AdvShrinkOn", "_ShrinkToFit", 1f);
			c.shrinkOff = GetValueClip(settings, "AdvShrinkOff", "_ShrinkToFit", 0f);
			c.boldOn = GetValueClip(settings, "AdvBoldOn", "_Weight", 0.05f);
			c.boldOff = GetValueClip(settings, "AdvBoldOff", "_Weight", 0f);
			// Board size scales the Constraint object (Text's parent), not the Text quad. The installer gives the quad a lopsided scale
			// (0.5 x 1/6), and a uniform scale clip would flatten that into a square.
			c.sizeSmall = GetHeadScaleClip("AdvSizeSmall", KatSettings.AdvSizeMin, ConstraintPath);
			c.sizeDefault = GetHeadScaleClip("AdvSizeDefault", KatSettings.AdvSizeDefault, ConstraintPath);
			c.sizeLarge = GetHeadScaleClip("AdvSizeLarge", KatSettings.AdvSizeMax, ConstraintPath);
			return c;
		}

		// Gives back an empty clip asset in the clips folder (an existing one is wiped and reused).
		public static AnimationClip LoadOrCreateClip(KatSettings settings, string name)
		{
			string path = ClipPath(settings, name);
			AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (clip == null) {
				clip = new AnimationClip();
				clip.name = name;
				AssetDatabase.CreateAsset(clip, path);
			} else {
				clip.ClearCurves();
			}
			return clip;
		}

		private static AnimationClip GetValueClip(KatSettings settings, string name, string materialProperty, float value)
		{
			string path = ClipPath(settings, name);
			AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (clip == null) {
				clip = new AnimationClip();
				clip.name = name;
				AssetDatabase.CreateAsset(clip, path);
			}
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material." + materialProperty), value);
			EditorUtility.SetDirty(clip);
			AssetDatabase.SaveAssets();
			return clip;
		}

		private static void SetCurve(AnimationClip clip, EditorCurveBinding binding, float value)
		{
			AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, value)));
		}

		public static AnimationClip[] GetStyleClips(KatSettings settings)
		{
			return new AnimationClip[] {
				GetStyleClip(settings, "StyleClean", 0.85f, 0.25f, 0.25f, 0.08f, 0.0f),
				GetStyleClip(settings, "StyleDefault", 1.00f, 0.50f, 0.50f, 0.14f, 0.0f),
				GetStyleClip(settings, "StyleRoomy", 1.00f, 0.90f, 0.80f, 0.20f, 0.0f)
			};
		}

		private static AnimationClip GetStyleClip(KatSettings settings, string name, float scale, float padding, float radius, float border, float outline)
		{
			string path = ClipPath(settings, name);
			AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (existing != null) {
				float previousDefaultOutline = name == "StyleClean" ? 0.08f : name == "StyleDefault" ? 0.12f : 0.16f;
				EditorCurveBinding outlineBinding = EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._OutlineWidth");
				AnimationCurve existingOutline = AnimationUtility.GetEditorCurve(existing, outlineBinding);
				if (existingOutline != null && Mathf.Approximately(existingOutline.Evaluate(0f), previousDefaultOutline)) {
					SetCurve(existing, outlineBinding, outline);
					EditorUtility.SetDirty(existing);
					AssetDatabase.SaveAssets();
				}
				return existing;
			}

			AnimationClip clip = new AnimationClip();
			clip.name = name;
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._Scale"), scale);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._Padding"), padding);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._CornerRadius"), radius);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._BorderWidth"), border);
			SetCurve(clip, EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._OutlineWidth"), outline);
			AssetDatabase.CreateAsset(clip, path);
			return clip;
		}

		public static AnimationClip[] GetHeadScaleClips(KatSettings settings, string headScalePivotPath)
		{
			return new AnimationClip[] {
				GetHeadScaleClip("HeadScaleClean", 0.85f, headScalePivotPath),
				GetHeadScaleClip("HeadScaleDefault", 1.00f, headScalePivotPath),
				GetHeadScaleClip("HeadScaleRoomy", 1.00f, headScalePivotPath)
			};
		}

		private static AnimationClip GetHeadScaleClip(string name, float scale, string headScalePivotPath)
		{
			AnimationClip clip = new AnimationClip();
			clip.name = name;
			EditorCurveBinding xBinding = EditorCurveBinding.FloatCurve(headScalePivotPath, typeof(Transform), "m_LocalScale.x");
			EditorCurveBinding yBinding = EditorCurveBinding.FloatCurve(headScalePivotPath, typeof(Transform), "m_LocalScale.y");
			EditorCurveBinding zBinding = EditorCurveBinding.FloatCurve(headScalePivotPath, typeof(Transform), "m_LocalScale.z");
			SetCurve(clip, xBinding, scale);
			SetCurve(clip, yBinding, scale);
			SetCurve(clip, zBinding, scale);
			return clip;
		}

		public static AnimationClip[] GetPageClips(KatSettings settings, bool end)
		{
			int pairs = settings.GetPageCellCount(settings.TextLength);
			AnimationClip[] clips = new AnimationClip[pairs];
			AssetDatabase.StartAssetEditing();
			try {
				for (int j = 0; j < pairs; j++) {
					clips[j] = GetOrCreate(settings, (end ? "PageEnd" : "Page") + j.ToString(), new int[] { j }, end ? 127f : -127f);
				}
			} finally {
				AssetDatabase.StopAssetEditing();
			}
			AssetDatabase.SaveAssets();
			// Re-load so the references point at the imported assets
			for (int j = 0; j < pairs; j++) {
				clips[j] = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(settings, (end ? "PageEnd" : "Page") + j.ToString()));
			}
			return clips;
		}

		public static AnimationClip GetPageClear(KatSettings settings)
		{
			int pairs = settings.GetPageCellCount(settings.TextLength);
			int[] all = new int[pairs];
			for (int j = 0; j < pairs; j++) all[j] = j;
			// Format 2 uses PageClear; format 3 uses PageClear3 because the page binding counts differ.
			string clearName = settings.PageFormat == KatSettings.PageFormat3 ? "PageClear3" : "PageClear";
			AnimationClip clip = GetOrCreate(settings, clearName, all, 0f);
			AssetDatabase.SaveAssets();
			return AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(settings, clearName));
		}

		private static string ClipFolder(KatSettings settings)
		{
			KatAvatarInstaller.CreateOutputFolder(settings);
			string root = "Assets/" + settings.GeneratedOutputFolderName;
			string sub = root + "/" + ClipFolderName;
			if (!AssetDatabase.IsValidFolder(sub)) AssetDatabase.CreateFolder(root, ClipFolderName);
			return sub;
		}

		private static string ClipPath(KatSettings settings, string name)
		{
			return ClipFolder(settings) + "/KAT_" + name + ".anim";
		}

		// True when the clip animates exactly material._Page<j> for every j in `pairs`, each held at `value`.
		private static bool ClipMatches(AnimationClip clip, int[] pairs, float value)
		{
			EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
			if (bindings.Length != pairs.Length) return false;
			foreach (int j in pairs) {
				EditorCurveBinding expected = EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._Page" + j.ToString());
				AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, expected);
				if (curve == null || curve.length != 1 || !Mathf.Approximately(curve[0].value, value)) return false;
			}
			return true;
		}

		private static AnimationClip GetOrCreate(KatSettings settings, string name, int[] pairs, float value)
		{
			string path = ClipPath(settings, name);
			AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (existing != null) {
				if (ClipMatches(existing, pairs, value)) return existing;
				Debug.LogWarning("KAT Paged: " + path + " does not match the current page format; regenerating it.");
				AssetDatabase.DeleteAsset(path);
			}

			AnimationClip clip = new AnimationClip();
			clip.name = name;
			foreach (int j in pairs) {
				EditorCurveBinding binding = EditorCurveBinding.FloatCurve(TextPath, typeof(MeshRenderer), "material._Page" + j.ToString());
				AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, value)));
			}
			AssetDatabase.CreateAsset(clip, path);
			return clip;
		}
	}

	// Makes the mipmaps with a Kaiser filter. When you build a texture from script, Unity always makes box-filtered mips, and the
	// importer's Kaiser option only applies to textures it imports itself, so for these arrays we do it by hand: each mip level is
	// a 2x downsample with a Kaiser-windowed sinc (6 taps, horizontal then vertical), and the texture is applied with
	// Apply(false) so Unity doesn't redo them. Emoji pages are filtered premultiplied, so their transparent pixels can't leak
	// dark edges into the smaller mips.
	internal static class KatMips
	{
		public static bool UseKaiser = true;   // set to false to go back to Unity's own box mips
		private const float Beta = 4f;
		private static readonly float[] Weights = BuildWeights();

		private static float Bessel0(float x)
		{
			float sum = 1f, term = 1f;
			for (int k = 1; k < 30; k++) {
				float h = x / (2f * k);
				term *= h * h;
				sum += term;
			}
			return sum;
		}

		private static float Sinc(float x)
		{
			if (Mathf.Abs(x) < 1e-6f) return 1f;
			float px = Mathf.PI * x;
			return Mathf.Sin(px) / px;
		}

		// Each output pixel sits between input pixels 2x and 2x+1, so the six taps are at -2.5 ... +2.5 pixels from it.
		private static float[] BuildWeights()
		{
			float[] w = new float[6];
			float sum = 0f;
			for (int i = 0; i < 6; i++) {
				float d = i - 2.5f;
				float t = d / 3f;
				float window = Bessel0(Beta * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t))) / Bessel0(Beta);
				w[i] = Sinc(d * 0.5f) * window;
				sum += w[i];
			}
			for (int i = 0; i < 6; i++) w[i] /= sum;
			return w;
		}

		private static float[] Down(float[] src, int w, int h)
		{
			int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
			float[] tmp = new float[nw * h * 4];
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < nw; x++) {
					for (int c = 0; c < 4; c++) {
						float acc = 0f;
						for (int i = 0; i < 6; i++) {
							int sx = Mathf.Clamp(2 * x - 2 + i, 0, w - 1);
							acc += Weights[i] * src[(y * w + sx) * 4 + c];
						}
						tmp[(y * nw + x) * 4 + c] = acc;
					}
				}
			}
			float[] dst = new float[nw * nh * 4];
			for (int y = 0; y < nh; y++) {
				for (int x = 0; x < nw; x++) {
					for (int c = 0; c < 4; c++) {
						float acc = 0f;
						for (int i = 0; i < 6; i++) {
							int sy = Mathf.Clamp(2 * y - 2 + i, 0, h - 1);
							acc += Weights[i] * tmp[(sy * nw + x) * 4 + c];
						}
						dst[(y * nw + x) * 4 + c] = acc;
					}
				}
			}
			return dst;
		}

		private static byte ToByte(float v)
		{
			return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
		}

		// Takes level 0 (size x size) and builds every mip down to 1x1. singleChannel: only .r counts (SDF pages). premultiply: for RGBA emoji pages.
		public static Color32[][] Chain(Color32[] level0, int size, bool singleChannel, bool premultiply)
		{
			int levels = 1;
			for (int n = size; n > 1; n /= 2) levels++;
			Color32[][] chain = new Color32[levels][];
			chain[0] = level0;

			float[] cur = new float[size * size * 4];
			for (int i = 0; i < level0.Length; i++) {
				Color32 c = level0[i];
				float a = singleChannel ? 255f : c.a;
				float k = premultiply ? a / 255f : 1f;
				cur[i * 4 + 0] = c.r * k;
				cur[i * 4 + 1] = singleChannel ? 0f : c.g * k;
				cur[i * 4 + 2] = singleChannel ? 0f : c.b * k;
				cur[i * 4 + 3] = a;
			}

			int w = size;
			for (int level = 1; level < levels; level++) {
				cur = Down(cur, w, w);
				w = Mathf.Max(1, w / 2);
				Color32[] px = new Color32[w * w];
				for (int i = 0; i < px.Length; i++) {
					float a = Mathf.Clamp(cur[i * 4 + 3], 0f, 255f);
					float inv = premultiply ? (a > 0.5f ? 255f / a : 0f) : 1f;
					px[i] = new Color32(
						ToByte(cur[i * 4 + 0] * inv),
						singleChannel ? (byte)0 : ToByte(cur[i * 4 + 1] * inv),
						singleChannel ? (byte)0 : ToByte(cur[i * 4 + 2] * inv),
						singleChannel ? (byte)255 : ToByte(a));
				}
				chain[level] = px;
			}
			return chain;
		}

		// Fills a texture (made with a mip chain) with the Kaiser mips.
		public static void Fill(Texture2D target, Color32[] level0, int size, bool singleChannel, bool premultiply)
		{
			Color32[][] chain = Chain(level0, size, singleChannel, premultiply);
			for (int level = 0; level < chain.Length && level < target.mipmapCount; level++) {
				target.SetPixels32(chain[level], level);
			}
			target.Apply(false, false);
		}
	}

	public class KatAdvancedClips
	{
		public AnimationClip billboardAuto, billboardAlways, billboardNever;
		public AnimationClip growOn, growOff;
		public AnimationClip boxOn, boxOff;
		public AnimationClip shrinkOn, shrinkOff;
		public AnimationClip boldOn, boldOff;
		public AnimationClip sizeSmall, sizeDefault, sizeLarge;
	}

	public static class KatPagedBuilder
	{
		private const string ShaderName = "Unlit/KF_VRChatAvatarTextShaderPaged";
		// Where the pages get copied to inside the project, so re-builds never need the OS folder panel again.
		private const string SourcePagesFolderName = "SourcePages";
		// Remembers the last *external* folder (e.g. wherever build_atlas.py -o wrote to), purely so that if the
		// person does need to browse again (new charset export, etc.) the dialog opens where they left off.
		private const string LastExternalFolderPrefKey = "KAT_Paged_LastSourceFolder";
		// The emoji pages build_atlas.py writes: RGBA PNGs, one slice of the emoji array each.
		private const string ColorPagePattern = "color_page_*.png";
		private const string ColorArrayAssetName = "KAT_ColorArray.asset";
		public const string ColorGlyphsKeyword = "KAT_COLOR_GLYPHS";

		[MenuItem("Tools/KAT/KAT Paged/Build Glyph Array + Material (R8)")]
		public static void BuildR8() { BuildFromDialog(false); }

		[MenuItem("Tools/KAT/KAT Paged/Build Glyph Array + Material (BC4)")]
		public static void BuildBC4() { BuildFromDialog(true); }

		[MenuItem("Tools/KAT/KAT Paged/Rebuild From Last Imported Pages (R8)")]
		public static void RebuildLastR8() { RebuildFromImportedCopy(false); }

		[MenuItem("Tools/KAT/KAT Paged/Rebuild From Last Imported Pages (BC4)")]
		public static void RebuildLastBC4() { RebuildFromImportedCopy(true); }

		private static void BuildFromDialog(bool bc4)
		{
			string startIn = EditorPrefs.GetString(LastExternalFolderPrefKey, "");
			string folder = EditorUtility.OpenFolderPanel("Folder with page_00.png ... (from build_atlas.py)", startIn, "");
			if (string.IsNullOrEmpty(folder)) return;
			EditorPrefs.SetString(LastExternalFolderPrefKey, folder);
			Build(bc4, folder);
		}

		private static void RebuildFromImportedCopy(bool bc4)
		{
			string folder = ImportedPagesFolder();
			if (!Directory.Exists(folder) || Directory.GetFiles(folder, "page_*.png").Length == 0) {
				EditorUtility.DisplayDialog("KAT Paged", "No pages have been imported into the project yet. Use \"Build Glyph Array + Material\" once first.", "OK");
				return;
			}
			Build(bc4, folder);
		}

		private static string ImportedPagesFolder()
		{
			return "Assets/" + new KatSettings().GeneratedOutputFolderName + "/" + SourcePagesFolderName;
		}

		private static string CopyPagesIntoProject(string folder)
		{
			string dest = ImportedPagesFolder();
			string destAbsolute = Path.GetFullPath(dest);
			string sourceAbsolute = Path.GetFullPath(folder);

			if (string.Equals(destAbsolute.TrimEnd('/', '\\'), sourceAbsolute.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
				return folder;

			if (Directory.Exists(destAbsolute)) {
				if (Directory.GetFiles(destAbsolute).Length > 0)
					Debug.LogWarning("KAT Paged: overwriting existing pages in " + dest + " with the new source from " + folder + ".");
				FileUtil.DeleteFileOrDirectory(destAbsolute);
			}

			if (!Directory.Exists(destAbsolute))
			{
				Directory.CreateDirectory(destAbsolute);
			}

			foreach (string src in Directory.GetFiles(folder, "page_*.png"))
			{
				File.Copy(src, Path.Combine(destAbsolute, Path.GetFileName(src)), true);
			}
			foreach (string src in Directory.GetFiles(folder, ColorPagePattern))
			{
				File.Copy(src, Path.Combine(destAbsolute, Path.GetFileName(src)), true);
			}

			string meta = Path.Combine(folder, "atlas_meta.json");
			if (File.Exists(meta))
			{
				File.Copy(meta, Path.Combine(destAbsolute, "atlas_meta.json"), true);
			}

			AssetDatabase.Refresh();
			Debug.Log("KAT Paged: copied pages into " + dest + " -- use \"Rebuild From Last Imported Pages\" next time to skip the folder picker.");
			return dest;
		}

		private static void Build(bool bc4, string folder)
		{
			folder = CopyPagesIntoProject(folder);

			string[] files = Directory.GetFiles(folder, "page_*.png");
			Array.Sort(files, ComparePageFiles);
			if (files.Length == 0) {
				Debug.LogError("KAT Paged: no page_*.png in " + folder);
				return;
			}
			string[] colorFiles = Directory.GetFiles(folder, ColorPagePattern);
			Array.Sort(colorFiles, ComparePageFiles);
			// As far as the wire format goes, emoji pages are ordinary pages: they get the page numbers right after the SDF pages.
			if (!CheckPageCount(files.Length + colorFiles.Length, ReadMetaPageFormat(folder))) return;
			int metaColorStart = ReadMetaColorPageStart(folder);
			if (colorFiles.Length > 0 && metaColorStart != 0 && metaColorStart != files.Length) {
				string problem = "atlas_meta.json says the emoji pages start at page " + metaColorStart + ", but there are " + files.Length +
					" SDF pages (page_*.png), so the emoji would be looked up in the wrong place. Rebuild the atlas with build_atlas.py and import it again.";
				Debug.LogError("KAT Paged: " + problem);
				EditorUtility.DisplayDialog("KAT Paged", problem, "OK");
				return;
			}

			Texture2D[] pages = new Texture2D[files.Length];
			for (int p = 0; p < files.Length; p++) {
				pages[p] = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
				pages[p].LoadImage(File.ReadAllBytes(files[p]), false);
			}
			int size = pages[0].width;
			for (int p = 0; p < pages.Length; p++) {
				if (pages[p].width != size || pages[p].height != size) {
					Debug.LogError("KAT Paged: every page must be " + size + "x" + size + " (" + files[p] + ")");
					DestroyPages(pages);
					return;
				}
			}

			string[] found = AssetDatabase.FindAssets("KAT_Text t:Material");
			if (found.Length == 0) {
				Debug.LogError("KAT Paged: could not find the stock KAT_Text material to locate Resources/KAT_Misc.");
				DestroyPages(pages);
				return;
			}
			string stockPath = AssetDatabase.GUIDToAssetPath(found[0]);
			string outDir = Path.GetDirectoryName(stockPath).Replace('\\', '/');

			Texture2DArray array = bc4 ? BuildBC4Array(pages, size) : null;
			if (array == null) array = BuildR8Array(pages, size);

			// The emoji array, if there is one (BC7, or plain RGBA32 if that fails)
			Texture2DArray colorArray = null;
			Texture2D[] colorPages = null;
			if (colorFiles.Length > 0) {
				colorPages = new Texture2D[colorFiles.Length];
				for (int c = 0; c < colorFiles.Length; c++) {
					colorPages[c] = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
					colorPages[c].LoadImage(File.ReadAllBytes(colorFiles[c]), false);
				}
				int colorSize = colorPages[0].width;
				bool sizesOk = colorPages[0].height == colorSize;
				for (int c = 1; c < colorPages.Length && sizesOk; c++) sizesOk = colorPages[c].width == colorSize && colorPages[c].height == colorSize;
				if (!sizesOk) {
					Debug.LogError("KAT Paged: all the emoji pages (" + ColorPagePattern + ") have to be the same square size.");
					DestroyPages(colorPages);
					DestroyPages(pages);
					UnityEngine.Object.DestroyImmediate(array);
					return;
				}
				colorArray = BuildBC7Array(colorPages, colorSize);
				if (colorArray == null) colorArray = BuildRgba32Array(colorPages, colorSize);
			}

			string arrayPath = outDir + "/KAT_GlyphArray.asset";
			if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(arrayPath) != null) AssetDatabase.DeleteAsset(arrayPath);
			AssetDatabase.CreateAsset(array, arrayPath);

			Shader shader = Shader.Find(ShaderName);
			if (shader == null) {
				Debug.LogError("KAT Paged: shader '" + ShaderName + "' not found. Import KF_AvatarTextShaderPaged.shader first.");
				DestroyPages(pages);
				return;
			}
			string matPath = outDir + "/KAT_Text_Paged.mat";
			Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
			if (material == null) {
				material = new Material(shader);
				Material stock = AssetDatabase.LoadAssetAtPath<Material>(stockPath);
				CopyIfPresent(stock, material, "_MainColor", "_ShadowColor", "_RowLength", "_RowColumns", "_GrowEnabled");
				AssetDatabase.CreateAsset(material, matPath);
			} else {
				material.shader = shader;
			}
			material.SetTexture("_GlyphArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(arrayPath));

			string colorPath = outDir + "/" + ColorArrayAssetName;
			if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(colorPath) != null) AssetDatabase.DeleteAsset(colorPath);
			if (colorArray != null) {
				AssetDatabase.CreateAsset(colorArray, colorPath);
				material.SetTexture("_ColorArray", AssetDatabase.LoadAssetAtPath<Texture2DArray>(colorPath));
				material.SetFloat("_ColorPageStart", files.Length);
				material.EnableKeyword(ColorGlyphsKeyword);
			} else {
				// No emoji in this atlas: make sure nothing left over from an earlier build stays on the material.
				if (material.HasProperty("_ColorArray")) material.SetTexture("_ColorArray", null);
				material.SetFloat("_ColorPageStart", 255f);
				material.DisableKeyword(ColorGlyphsKeyword);
			}
			if (material.HasProperty("_OutlineWidth") && Mathf.Approximately(material.GetFloat("_OutlineWidth"), 0.12f)) {
				material.SetFloat("_OutlineWidth", 0.0f);
			}

			int builtFormat = ReadMetaPageFormat(folder);
			if (builtFormat == KatSettings.PageFormat3) material.EnableKeyword(KatSettings.PageFormat3Keyword);
			else if (builtFormat == KatSettings.PageFormat2) material.DisableKeyword(KatSettings.PageFormat3Keyword);

			EditorUtility.SetDirty(material);
			AssetDatabase.SaveAssets();
			KatKeyboardPaging.SyncMaterial(material);   // the in-game keyboard draws its key legends out of this atlas

			DestroyPages(pages);
			if (colorPages != null) DestroyPages(colorPages);
			Debug.Log("KAT Paged: " + files.Length + " pages of " + size + "x" + size + " -> " + arrayPath + " (" + array.format + ") and " + matPath +
				(colorArray != null ? "; " + colorFiles.Length + " emoji pages -> " + colorPath + " (" + colorArray.format + "), emoji pages start at page " + files.Length : ""));
		}

		[Serializable]
		private class AtlasMeta { public int page_format; public int format; public int color_page_start; public int color_pages; }

		// The first emoji page number that build_atlas.py wrote down (0 if it didn't, or there are no emoji).
		private static int ReadMetaColorPageStart(string folder)
		{
			string meta = Path.Combine(folder, "atlas_meta.json");
			if (!File.Exists(meta)) return 0;
			try {
				AtlasMeta parsed = JsonUtility.FromJson<AtlasMeta>(File.ReadAllText(meta));
				return parsed == null ? 0 : parsed.color_page_start;
			} catch (Exception) {
				return 0;
			}
		}

		// How many emoji pages the imported atlas has (the array's depth), or 0.
		private static int ColorPageCountInProject()
		{
			string[] guids = AssetDatabase.FindAssets("KAT_ColorArray t:Texture2DArray");
			if (guids.Length == 0) return 0;
			Texture2DArray array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AssetDatabase.GUIDToAssetPath(guids[0]));
			return array != null ? array.depth : 0;
		}

		private static void DestroyPages(Texture2D[] pages)
		{
			foreach (Texture2D page in pages) {
				if (page != null) UnityEngine.Object.DestroyImmediate(page);
			}
		}

		private static int ReadMetaPageFormat(string folder)
		{
			string meta = Path.Combine(folder, "atlas_meta.json");
			if (!File.Exists(meta)) return 0;
			try {
				AtlasMeta parsed = JsonUtility.FromJson<AtlasMeta>(File.ReadAllText(meta));
				int recorded = parsed == null ? 0 : (parsed.page_format != 0 ? parsed.page_format : parsed.format);
				return recorded == KatSettings.PageFormat3 ? KatSettings.PageFormat3 : KatSettings.PageFormat2;
			} catch (Exception e) {
				Debug.LogWarning("KAT Paged: could not read " + meta + " (" + e.Message + ").");
				return 0;
			}
		}

		private static int PageNumber(string file)
		{
			string name = Path.GetFileNameWithoutExtension(file);
			int number;
			// "page_07" and "color_page_07" both sort by the number
			int at = name.LastIndexOf("page_", StringComparison.Ordinal);
			return at >= 0 && int.TryParse(name.Substring(at + "page_".Length), out number) ? number : int.MaxValue;
		}

		private static int ComparePageFiles(string a, string b)
		{
			int byNumber = PageNumber(a).CompareTo(PageNumber(b));
			return byNumber != 0 ? byNumber : string.CompareOrdinal(a, b);
		}

		private static bool CheckPageCount(int pageCount, int metaFormat)
		{
			int format2Pages = KatSettings.MaxPage2 + 1;
			int format3Pages = KatSettings.MaxPage3 + 1;
			string problem = null;
			if (pageCount > format3Pages) {
				problem = "The atlas has " + pageCount + " pages but even page format 3 addresses at most " + format3Pages + ". Build a smaller charset.";
			} else if (pageCount > format2Pages && metaFormat != KatSettings.PageFormat3) {
				problem = "The atlas has " + pageCount + " pages but page format 2 (the default) addresses at most " + format2Pages + ". " +
					"Rebuild the charset and atlas with --format 3 (the installer's Page format setting follows the atlas on Auto), or use a smaller charset.";
			}
			if (problem != null) {
				Debug.LogError("KAT Paged: " + problem);
				EditorUtility.DisplayDialog("KAT Paged", problem, "OK");
				return false;
			}
			if (metaFormat == KatSettings.PageFormat3 && pageCount <= format2Pages) {
				Debug.LogWarning("KAT Paged: atlas is marked page format 3 but has only " + pageCount + " pages; format 2 would be enough and uses fewer parameter bits.");
			}
			return true;
		}

		public static int DetectAtlasPageFormat(out int pages)
		{
			pages = 0;
			string[] guids = AssetDatabase.FindAssets("KAT_GlyphArray t:Texture2DArray");
			if (guids.Length > 0) {
				Texture2DArray array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AssetDatabase.GUIDToAssetPath(guids[0]));
				if (array != null) pages = array.depth;
			}
			pages += ColorPageCountInProject();
			int fromMeta = ReadMetaPageFormat(ImportedPagesFolder());
			if (fromMeta != 0) return fromMeta;
			return pages > KatSettings.MaxPage2 + 1 ? KatSettings.PageFormat3 : 0;
		}

		public static bool PreflightInstall(KatSettings settings)
		{
			Material textMaterial = Resources.Load<Material>("KAT_Misc/KAT_Text_Paged");
			if (textMaterial == null) {
				Debug.LogWarning("KAT: no paged glyph atlas has been built in this project yet, so the stock text material will be installed. " +
					"Run Tools > KillFrenzy > KAT Paged > Build Glyph Array + Material first for the paged text.");
				return true;
			}
			if (settings.InstallKeyboard) KatKeyboardPaging.SyncMaterial(textMaterial);   // an atlas built before the keyboard got live legends still works
			return ValidateAtlasForSettings(settings, textMaterial);
		}

		public static bool ValidateAtlasForSettings(KatSettings settings, Material textMaterial)
		{
			Texture2DArray atlas = textMaterial != null && textMaterial.HasProperty("_GlyphArray") ? textMaterial.GetTexture("_GlyphArray") as Texture2DArray : null;
			if (atlas == null) return true; // not a paged material; nothing to check

			Texture2DArray colorAtlas = textMaterial.HasProperty("_ColorArray") ? textMaterial.GetTexture("_ColorArray") as Texture2DArray : null;
			int depth = atlas.depth + (colorAtlas != null ? colorAtlas.depth : 0); // emoji pages use up page numbers too
			int metaFormat = ReadMetaPageFormat(ImportedPagesFolder());
			string problem = null;
			if (depth > settings.GetMaxPage() + 1) {
				problem = "The glyph atlas has " + depth + " pages but page format " + settings.PageFormat + " addresses at most " + (settings.GetMaxPage() + 1) +
					". " + (settings.PageFormat == KatSettings.PageFormat2 ? "Set the installer's Page format to Auto (or 3) and use an atlas built with --format 3, or rebuild a smaller atlas." : "Rebuild a smaller atlas.");
			} else if (metaFormat != 0 && metaFormat != settings.PageFormat) {
				problem = "The imported atlas was built for page format " + metaFormat + " but the installer is set to page format " + settings.PageFormat +
					". Set the installer's Page format to Auto so it follows the atlas (or rebuild the atlas/charset with the --format you want and build the glyph array again).";
			}
			if (problem != null) {
				Debug.LogError("KAT Paged: " + problem);
				EditorUtility.DisplayDialog("KAT install stopped", problem, "OK");
				return false;
			}
			foreach (string warning in KatParameterBudget.Warnings(settings, depth)) Debug.LogWarning("KAT: " + warning);
			return true;
		}

		private static Texture2DArray BuildR8Array(Texture2D[] pages, int size)
		{
			Texture2DArray array = new Texture2DArray(size, size, pages.Length, TextureFormat.R8, true, true);
			array.wrapMode = TextureWrapMode.Clamp;
			array.filterMode = FilterMode.Bilinear;
			for (int p = 0; p < pages.Length; p++) {
				if (KatMips.UseKaiser) {
					Color32[][] chain = KatMips.Chain(pages[p].GetPixels32(), size, true, false);
					for (int level = 0; level < chain.Length; level++) array.SetPixels32(chain[level], p, level);
				} else {
					array.SetPixels32(pages[p].GetPixels32(), p, 0);
				}
			}
			array.Apply(!KatMips.UseKaiser, false);
			return array;
		}

		// Compresses each page to BC4 (1 channel, 0.5 byte/texel) and copies every mip into a BC4 array.
		// Returns null if this Unity version / platform refuses, so the caller can fall back to R8.
		private static Texture2DArray BuildBC4Array(Texture2D[] pages, int size)
		{
			try {
				Texture2DArray array = new Texture2DArray(size, size, pages.Length, TextureFormat.BC4, true, true);
				array.wrapMode = TextureWrapMode.Clamp;
				array.filterMode = FilterMode.Bilinear;
				for (int p = 0; p < pages.Length; p++) {
					Texture2D src = new Texture2D(size, size, TextureFormat.R8, true, true);
					if (KatMips.UseKaiser) {
						KatMips.Fill(src, pages[p].GetPixels32(), size, true, false);
					} else {
						src.SetPixels32(pages[p].GetPixels32(), 0);
						src.Apply(true, false);
					}
					EditorUtility.CompressTexture(src, TextureFormat.BC4, TextureCompressionQuality.Best);
					for (int mip = 0; mip < src.mipmapCount; mip++) {
						Graphics.CopyTexture(src, 0, mip, array, p, mip);
					}
					UnityEngine.Object.DestroyImmediate(src);
				}
				return array;
			} catch (Exception e) {
				Debug.LogWarning("KAT Paged: BC4 array failed (" + e.Message + "), falling back to R8.");
				return null;
			}
		}

		// Packs the emoji pages as BC7 (RGBA, 1 byte per pixel, sRGB). Returns null if this Unity or platform won't do it, so the
		// caller can fall back to uncompressed RGBA32.
		private static Texture2DArray BuildBC7Array(Texture2D[] pages, int size)
		{
			try {
				Texture2DArray array = new Texture2DArray(size, size, pages.Length, TextureFormat.BC7, true, false);
				array.wrapMode = TextureWrapMode.Clamp;
				array.filterMode = FilterMode.Bilinear;
				for (int p = 0; p < pages.Length; p++) {
					Texture2D src = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
					if (KatMips.UseKaiser) {
						KatMips.Fill(src, pages[p].GetPixels32(), size, false, true);
					} else {
						src.SetPixels32(pages[p].GetPixels32(), 0);
						src.Apply(true, false);
					}
					EditorUtility.CompressTexture(src, TextureFormat.BC7, TextureCompressionQuality.Best);
					for (int mip = 0; mip < src.mipmapCount; mip++) {
						Graphics.CopyTexture(src, 0, mip, array, p, mip);
					}
					UnityEngine.Object.DestroyImmediate(src);
				}
				return array;
			} catch (Exception e) {
				Debug.LogWarning("KAT Paged: BC7 emoji array failed (" + e.Message + "), falling back to uncompressed RGBA32.");
				return null;
			}
		}

		private static Texture2DArray BuildRgba32Array(Texture2D[] pages, int size)
		{
			Texture2DArray array = new Texture2DArray(size, size, pages.Length, TextureFormat.RGBA32, true, false);
			array.wrapMode = TextureWrapMode.Clamp;
			array.filterMode = FilterMode.Bilinear;
			for (int p = 0; p < pages.Length; p++) {
				if (KatMips.UseKaiser) {
					Color32[][] chain = KatMips.Chain(pages[p].GetPixels32(), size, false, true);
					for (int level = 0; level < chain.Length; level++) array.SetPixels32(chain[level], p, level);
				} else {
					array.SetPixels32(pages[p].GetPixels32(), p, 0);
				}
			}
			array.Apply(!KatMips.UseKaiser, false);
			return array;
		}

		private static void CopyIfPresent(Material from, Material to, params string[] names)
		{
			if (from == null) return;
			foreach (string n in names) {
				if (!from.HasProperty(n) || !to.HasProperty(n)) continue;
				if (n == "_MainColor" || n == "_ShadowColor") to.SetColor(n, from.GetColor(n));
				else to.SetFloat(n, from.GetFloat(n));
			}
		}
	}
	public static class KatPagedMenu
	{
		private const string SubMenuName = "KAT Text";
		private const string SubMenuAssetName = "KAT_Text_Menu";
		private const string LegacySubMenuAssetName = "KAT_Menu";
		private const string AdvancedSubMenuName = "Advanced";
		private const string AdvancedSubMenuAssetName = "KAT_Text_Advanced_Menu";
		private const string BoardSubMenuName = "Board";
		private const string BoardSubMenuAssetName = "KAT_Text_Board_Menu";

#if VRC_SDK_VRCSDK3
		public static bool InstallToMenu(ExpressionsMenu targetMenu, KatSettings settings)
		{
			if (targetMenu == null) {
				Debug.LogError("KAT Paged: cannot install expression controls without a target menu.");
				return false;
			}

			RemoveOwnedSubMenu(targetMenu, settings, false);

			if (targetMenu.controls.Count >= 8) {
				Debug.LogError("KAT Paged: expression menu is full; one main-menu slot is required for the \"" + SubMenuName + "\" submenu.");
				return false;
			}

			ExpressionsMenu subMenu = ScriptableObject.CreateInstance<ExpressionsMenu>();
			subMenu.name = SubMenuAssetName;
			subMenu.controls = new System.Collections.Generic.List<ExpressionsMenu.Control>();

			ExpressionsMenu.Control anchor = new ExpressionsMenu.Control();
			anchor.name = "Anchor Position";
			anchor.type = ExpressionsMenu.Control.ControlType.RadialPuppet;
			anchor.subParameters = new ExpressionsMenu.Control.Parameter[] {
				new ExpressionsMenu.Control.Parameter { name = settings.ParamHeadAnchor }
			};
			subMenu.controls.Add(anchor);

			ExpressionsMenu.Control style = new ExpressionsMenu.Control();
			style.name = "Text Style";
			style.type = ExpressionsMenu.Control.ControlType.RadialPuppet;
			style.subParameters = new ExpressionsMenu.Control.Parameter[] {
				new ExpressionsMenu.Control.Parameter { name = settings.ParamStyle }
			};
			subMenu.controls.Add(style);

			ExpressionsMenu.Control speed = new ExpressionsMenu.Control();
			speed.name = "Roll Speed";
			speed.type = ExpressionsMenu.Control.ControlType.RadialPuppet;
			speed.subParameters = new ExpressionsMenu.Control.Parameter[] {
				new ExpressionsMenu.Control.Parameter { name = settings.ParamSpeed }
			};
			subMenu.controls.Add(speed);

			KatAvatarInstaller.CreateOutputFolder(settings);
			string folder = "Assets/" + settings.GeneratedOutputFolderName;

			if (settings.InstallBoardControls) {
				ExpressionsMenu board = ScriptableObject.CreateInstance<ExpressionsMenu>();
				board.name = BoardSubMenuAssetName;
				board.controls = new System.Collections.Generic.List<ExpressionsMenu.Control>();
				// One int, five toggles with different values, so they act like radio buttons. The first one (value 0) is the reset: it is ticked
				// while the board is docked, and pressing it sends the board back to the head / chest and hands it back to the KAT Text anchor radial.
				board.controls.Add(MakeToggle("Reset: Back To Body", settings.ParamBoardMode, KatSettings.BoardModeDock));
				board.controls.Add(MakeToggle("Hold: Left Hand", settings.ParamBoardMode, KatSettings.BoardModeHoldLeft));
				board.controls.Add(MakeToggle("Hold: Right Hand", settings.ParamBoardMode, KatSettings.BoardModeHoldRight));
				board.controls.Add(MakeToggle("Drop: In World", settings.ParamBoardMode, KatSettings.BoardModeWorld));
				board.controls.Add(MakeToggle("Drop: Follow Me", settings.ParamBoardMode, KatSettings.BoardModeFollow));

				string boardPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + BoardSubMenuAssetName + ".asset");
				AssetDatabase.CreateAsset(board, boardPath);
				EditorUtility.SetDirty(board);

				ExpressionsMenu.Control boardEntry = new ExpressionsMenu.Control();
				boardEntry.name = BoardSubMenuName;
				boardEntry.type = ExpressionsMenu.Control.ControlType.SubMenu;
				boardEntry.subMenu = board;
				subMenu.controls.Add(boardEntry);
			}

			if (settings.InstallAdvancedMenu) {
				ExpressionsMenu advanced = ScriptableObject.CreateInstance<ExpressionsMenu>();
				advanced.name = AdvancedSubMenuAssetName;
				advanced.controls = new System.Collections.Generic.List<ExpressionsMenu.Control>();
				// Billboard is one int shared by two toggles (1 and 2): ticking one unticks the other, and neither means Auto.
				advanced.controls.Add(MakeToggle("Billboard: Always", settings.ParamAdvBillboard, 1f));
				advanced.controls.Add(MakeToggle("Billboard: Never", settings.ParamAdvBillboard, 2f));
				advanced.controls.Add(MakeToggle("Grow With Distance", settings.ParamAdvGrow, 1f));
				advanced.controls.Add(MakeToggle("Show Box", settings.ParamAdvBox, 1f));
				advanced.controls.Add(MakeToggle("Shrink Box To Text", settings.ParamAdvShrink, 1f));
				advanced.controls.Add(MakeToggle("Bold Text", settings.ParamAdvBold, 1f));

				ExpressionsMenu.Control size = new ExpressionsMenu.Control();
				size.name = "Board Size";
				size.type = ExpressionsMenu.Control.ControlType.RadialPuppet;
				size.subParameters = new ExpressionsMenu.Control.Parameter[] {
					new ExpressionsMenu.Control.Parameter { name = settings.ParamAdvSize }
				};
				advanced.controls.Add(size);

				string advancedPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AdvancedSubMenuAssetName + ".asset");
				AssetDatabase.CreateAsset(advanced, advancedPath);
				EditorUtility.SetDirty(advanced);

				ExpressionsMenu.Control advancedEntry = new ExpressionsMenu.Control();
				advancedEntry.name = AdvancedSubMenuName;
				advancedEntry.type = ExpressionsMenu.Control.ControlType.SubMenu;
				advancedEntry.subMenu = advanced;
				subMenu.controls.Add(advancedEntry);
			}

			string subPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SubMenuAssetName + ".asset");
			AssetDatabase.CreateAsset(subMenu, subPath);

			ExpressionsMenu.Control entry = new ExpressionsMenu.Control();
			entry.name = SubMenuName;
			entry.type = ExpressionsMenu.Control.ControlType.SubMenu;
			entry.subMenu = subMenu;
			targetMenu.controls.Add(entry);

			EditorUtility.SetDirty(subMenu);
			EditorUtility.SetDirty(targetMenu);
			AssetDatabase.SaveAssets();
			Debug.Log("KAT Paged: added the \"" + SubMenuName + "\" submenu.");
			return true;
		}

		private static ExpressionsMenu.Control MakeToggle(string name, string parameter, float value)
		{
			ExpressionsMenu.Control control = new ExpressionsMenu.Control();
			control.name = name;
			control.type = ExpressionsMenu.Control.ControlType.Toggle;
			control.parameter = new ExpressionsMenu.Control.Parameter { name = parameter };
			control.value = value;
			return control;
		}

		public static bool RemoveFromMenu(ExpressionsMenu targetMenu, KatSettings settings)
		{
			if (targetMenu == null) return true;
			return RemoveOwnedSubMenu(targetMenu, settings, true);
		}

		private static bool RemoveOwnedSubMenu(ExpressionsMenu targetMenu, KatSettings settings, bool markDirty)
		{
			bool removed = false;
			for (int i = targetMenu.controls.Count - 1; i >= 0; i--) {
				ExpressionsMenu.Control control = targetMenu.controls[i];
				if (control == null || control.name != SubMenuName || control.subMenu == null) continue;

				string assetPath = AssetDatabase.GetAssetPath(control.subMenu);
				bool ours = (control.subMenu.name == SubMenuAssetName || control.subMenu.name == LegacySubMenuAssetName) &&
					assetPath.StartsWith("Assets/" + settings.GeneratedOutputFolderName + "/", StringComparison.Ordinal);
				if (!ours) continue;

				// The Advanced and Board submenus live inside ours. Note their assets first, because deleting the parent breaks the reference.
				System.Collections.Generic.List<string> nestedOwned = new System.Collections.Generic.List<string>();
				if (control.subMenu.controls != null) {
					foreach (ExpressionsMenu.Control inner in control.subMenu.controls) {
						if (inner == null || inner.type != ExpressionsMenu.Control.ControlType.SubMenu || inner.subMenu == null) continue;
						string innerPath = AssetDatabase.GetAssetPath(inner.subMenu);
						if ((inner.subMenu.name == AdvancedSubMenuAssetName || inner.subMenu.name == BoardSubMenuAssetName) &&
							innerPath.StartsWith("Assets/" + settings.GeneratedOutputFolderName + "/", StringComparison.Ordinal)) {
							nestedOwned.Add(innerPath);
						}
					}
				}

				targetMenu.controls.RemoveAt(i);
				removed = true;
				if (!string.IsNullOrEmpty(assetPath)) {
					AssetDatabase.DeleteAsset(assetPath);
				}
				foreach (string nested in nestedOwned) {
					AssetDatabase.DeleteAsset(nested);
				}
			}

			if (removed && markDirty) {
				EditorUtility.SetDirty(targetMenu);
				AssetDatabase.SaveAssets();
				Debug.Log("KAT Paged: removed the \"" + SubMenuName + "\" submenu.");
			}
			return true;
		}
#endif
	}

}

#endif
