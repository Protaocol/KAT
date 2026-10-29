/*
The shader's debug material fields are never meant to ship: whatever value is left in a material gets baked into
the upload. This covers the _DebugFill* controls used to inspect the atlas. Fields a given build of the shader does not
have are simply skipped. These helpers zero them whenever the shader is (re)compiled and whenever KAT installs.
*/

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatDebugFields
	{
		public const string ShaderName = "Unlit/KF_VRChatAvatarTextShaderPaged";
		public const string ShaderFileName = "KF_AvatarTextShaderPaged.shader";

		// Debug fields: Add new debug properties here and they are reset everywhere on compile.
		public static readonly string[] Fields = {
			"_DebugFillEnabled", "_DebugFillPage", "_DebugFillSlot", "_DebugFillColumns", "_DebugFillRows"
		};

		public static bool IsDebugField(string name)
		{
			return Array.IndexOf(Fields, name) >= 0;
		}

		// Returns true when a value had to be cleared.
		public static bool Reset(Material material)
		{
			if (material == null) return false;
			bool changed = false;
			foreach (string field in Fields) {
				if (!material.HasProperty(field)) continue;
				if (material.GetFloat(field) != 0f) {
					material.SetFloat(field, 0f);
					changed = true;
				}
			}
			if (changed) EditorUtility.SetDirty(material);
			return changed;
		}

		public static int ResetProjectMaterials(string shaderPath)
		{
			int cleared = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:Material")) {
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (Array.IndexOf(AssetDatabase.GetDependencies(path, false), shaderPath) < 0) continue;
				Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (material != null && material.shader != null && material.shader.name == ShaderName && Reset(material)) {
					cleared++;
					Debug.Log("KAT: reset the debug fields on " + path + ".");
				}
			}
			if (cleared > 0) AssetDatabase.SaveAssets();
			return cleared;
		}
	}

	internal class KatDebugFieldsPostprocessor : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			string shaderPath = null;
			foreach (string path in importedAssets) {
				if (path.EndsWith(KatDebugFields.ShaderFileName, StringComparison.OrdinalIgnoreCase)) {
					shaderPath = path;
					break;
				}
			}
			if (shaderPath == null) return;

			EditorApplication.delayCall += () => KatDebugFields.ResetProjectMaterials(shaderPath);
		}
	}
}

#endif
