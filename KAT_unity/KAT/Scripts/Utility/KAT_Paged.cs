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
  - KatPagedMenu   : menu tool that adds the "KAT Text" submenu (Anchor radial, Text Style radial, Roll Speed radial) to an avatar's
                     expression menu and makes sure the required parameters exist.
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

	public static class KatPagedBuilder
	{
		private const string ShaderName = "Unlit/KF_VRChatAvatarTextShaderPaged";
		// Where the pages get copied to inside the project, so re-builds never need the OS folder panel again.
		private const string SourcePagesFolderName = "SourcePages";
		// Remembers the last *external* folder (e.g. wherever build_atlas.py -o wrote to), purely so that if the
		// person does need to browse again (new charset export, etc.) the dialog opens where they left off.
		private const string LastExternalFolderPrefKey = "KAT_Paged_LastSourceFolder";

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
			if (!CheckPageCount(files.Length, ReadMetaPageFormat(folder))) return;

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
			if (material.HasProperty("_OutlineWidth") && Mathf.Approximately(material.GetFloat("_OutlineWidth"), 0.12f)) {
				material.SetFloat("_OutlineWidth", 0.0f);
			}

			int builtFormat = ReadMetaPageFormat(folder);
			if (builtFormat == KatSettings.PageFormat3) material.EnableKeyword(KatSettings.PageFormat3Keyword);
			else if (builtFormat == KatSettings.PageFormat2) material.DisableKeyword(KatSettings.PageFormat3Keyword);

			EditorUtility.SetDirty(material);
			AssetDatabase.SaveAssets();

			DestroyPages(pages);
			Debug.Log("KAT Paged: " + files.Length + " pages of " + size + "x" + size + " -> " + arrayPath + " (" + array.format + ") and " + matPath);
		}

		[Serializable]
		private class AtlasMeta { public int page_format; public int format; }

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
			return int.TryParse(name.Substring("page_".Length), out number) ? number : int.MaxValue;
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
			return ValidateAtlasForSettings(settings, textMaterial);
		}

		public static bool ValidateAtlasForSettings(KatSettings settings, Material textMaterial)
		{
			Texture2DArray atlas = textMaterial != null && textMaterial.HasProperty("_GlyphArray") ? textMaterial.GetTexture("_GlyphArray") as Texture2DArray : null;
			if (atlas == null) return true; // not a paged material; nothing to check

			int depth = atlas.depth;
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
				array.SetPixels32(pages[p].GetPixels32(), p, 0);
			}
			array.Apply(true, false);
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
					src.SetPixels32(pages[p].GetPixels32(), 0);
					src.Apply(true, false);
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

			if (settings.InstallStyleSpeedControls) {
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
			}

			KatAvatarInstaller.CreateOutputFolder(settings);
			string folder = "Assets/" + settings.GeneratedOutputFolderName;

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

				targetMenu.controls.RemoveAt(i);
				removed = true;
				if (!string.IsNullOrEmpty(assetPath)) {
					AssetDatabase.DeleteAsset(assetPath);
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
