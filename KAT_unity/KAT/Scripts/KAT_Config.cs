/*
Optional install-time configuration for the paged KAT build (MIT, same terms as the rest of the project).

A plain text file (import it in Unity as a TextAsset, e.g. KAT_Config.txt) that is dropped into the "Config file"
slot of the KAT window before pressing Install/Update. Everything in it is optional; an empty slot installs the
the built-in defaults. See KAT_Config.example.txt for the format and README section 3.8.1.
*/

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

using KillFrenzy.AvatarTextTools.Utility;

namespace KillFrenzy.AvatarTextTools.Settings
{
	public class KatConfig
	{
		public struct TransformOverride
		{
			public Vector3? Position;
			public Vector3? Rotation;
			public Vector3? Scale;
		}

		public Vector3? HeadAnchorOffset;
		public Vector3? RightHeadAnchorOffset;
		public Vector3? TopHeadAnchorOffset;
		public Vector3? ChestAnchorOffset;

		public readonly List<KeyValuePair<string, float[]>> MaterialValues = new List<KeyValuePair<string, float[]>>();
		public readonly Dictionary<string, TransformOverride> Transforms = new Dictionary<string, TransformOverride>();
		public readonly List<string> Warnings = new List<string>();

		public bool HasMaterialValues { get { return MaterialValues.Count > 0; } }

		public static KatConfig FromAsset(TextAsset asset)
		{
			if (asset == null) return null;
			return Parse(asset.text, asset.name);
		}

		public static KatConfig Parse(string text, string sourceName)
		{
			KatConfig config = new KatConfig();
			if (string.IsNullOrEmpty(text)) return config;

			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			string section = "";

			for (int i = 0; i < lines.Length; i++) {
				string line = lines[i];
				int comment = line.IndexOf('#');
				if (comment >= 0) line = line.Substring(0, comment);
				line = line.Trim();
				if (line.Length == 0) continue;

				string loc = sourceName + " line " + (i + 1).ToString();

				if (line[0] == '[' && line[line.Length - 1] == ']') {
					section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
					if (section != "settings" && section != "material" && section != "transforms") {
						config.Warnings.Add(loc + ": unknown section [" + section + "] (expected settings, material or transforms)");
					}
					continue;
				}

				int eq = line.IndexOf('=');
				if (eq <= 0) {
					config.Warnings.Add(loc + ": expected 'name = value', got '" + line + "'");
					continue;
				}

				string key = line.Substring(0, eq).Trim();
				float[] numbers;
				if (!TryParseNumbers(line.Substring(eq + 1), out numbers)) {
					config.Warnings.Add(loc + ": could not read numbers for '" + key + "'");
					continue;
				}

				switch (section) {
					case "settings": config.ReadSetting(key, numbers, loc); break;
					case "material": config.MaterialValues.Add(new KeyValuePair<string, float[]>(key, numbers)); break;
					case "transforms": config.ReadTransform(key, numbers, loc); break;
					default: config.Warnings.Add(loc + ": '" + key + "' is outside any [section]"); break;
				}
			}
			return config;
		}

		private static bool TryParseNumbers(string value, out float[] numbers)
		{
			string[] parts = value.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
			numbers = new float[parts.Length];
			if (parts.Length == 0 || parts.Length > 4) return false;
			for (int i = 0; i < parts.Length; i++) {
				if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])) return false;
			}
			return true;
		}

		private void ReadSetting(string key, float[] n, string loc)
		{
			if (n.Length != 3) {
				Warnings.Add(loc + ": '" + key + "' needs three numbers (x y z)");
				return;
			}
			Vector3 v = new Vector3(n[0], n[1], n[2]);
			if (string.Equals(key, "headAnchorOffset", StringComparison.OrdinalIgnoreCase)) HeadAnchorOffset = v;
			else if (string.Equals(key, "rightHeadAnchorOffset", StringComparison.OrdinalIgnoreCase)) RightHeadAnchorOffset = v;
			else if (string.Equals(key, "topHeadAnchorOffset", StringComparison.OrdinalIgnoreCase)) TopHeadAnchorOffset = v;
			else if (string.Equals(key, "chestAnchorOffset", StringComparison.OrdinalIgnoreCase)) ChestAnchorOffset = v;
			else Warnings.Add(loc + ": unknown setting '" + key + "' (expected headAnchorOffset, rightHeadAnchorOffset, topHeadAnchorOffset or chestAnchorOffset)");
		}

		private void ReadTransform(string key, float[] n, string loc)
		{
			int dot = key.LastIndexOf('.');
			if (dot <= 0 || dot == key.Length - 1) {
				Warnings.Add(loc + ": expected 'ObjectName.position|rotation|scale', got '" + key + "'");
				return;
			}
			string objectName = key.Substring(0, dot).Trim();
			string field = key.Substring(dot + 1).Trim().ToLowerInvariant();
			if (n.Length != 3) {
				Warnings.Add(loc + ": '" + key + "' needs three numbers (x y z)");
				return;
			}

			TransformOverride t;
			Transforms.TryGetValue(objectName, out t);
			Vector3 v = new Vector3(n[0], n[1], n[2]);
			switch (field) {
				case "position": t.Position = v; break;
				case "rotation": t.Rotation = v; break;
				case "scale": t.Scale = v; break;
				default:
					Warnings.Add(loc + ": unknown transform field '" + field + "' (expected position, rotation or scale)");
					return;
			}
			Transforms[objectName] = t;
		}

		public void ApplyToMaterial(Material material)
		{
			Shader shader = material.shader;
			foreach (KeyValuePair<string, float[]> entry in MaterialValues) {
				string name = entry.Key;
				float[] n = entry.Value;

				if (KatDebugFields.IsDebugField(name)) {
					Debug.LogWarning("KAT config: '" + name + "' is a debug field and is always reset to 0. Use a [settings] anchor offset or a [transforms] entry for a real offset.");
					continue;
				}

				int index = FindPropertyIndex(shader, name);
				if (index < 0) {
					Debug.LogWarning("KAT config: the material's shader has no property '" + name + "'.");
					continue;
				}

				switch (ShaderUtil.GetPropertyType(shader, index)) {
					case ShaderUtil.ShaderPropertyType.Float:
					case ShaderUtil.ShaderPropertyType.Range:
						if (n.Length != 1) { Debug.LogWarning("KAT config: '" + name + "' is a float and needs exactly one number."); break; }
						material.SetFloat(name, n[0]);
						break;
					case ShaderUtil.ShaderPropertyType.Color:
						if (n.Length < 3) { Debug.LogWarning("KAT config: '" + name + "' is a colour and needs 3 or 4 numbers (r g b [a], 0..1)."); break; }
						material.SetColor(name, new Color(n[0], n[1], n[2], n.Length > 3 ? n[3] : 1f));
						break;
					case ShaderUtil.ShaderPropertyType.Vector:
						material.SetVector(name, new Vector4(n[0], n.Length > 1 ? n[1] : 0f, n.Length > 2 ? n[2] : 0f, n.Length > 3 ? n[3] : 0f));
						break;
					default:
						Debug.LogWarning("KAT config: '" + name + "' is a texture; textures cannot be set from the config file.");
						break;
				}
			}
			KatDebugFields.Reset(material);
		}

		private static int FindPropertyIndex(Shader shader, string name)
		{
			int count = ShaderUtil.GetPropertyCount(shader);
			for (int i = 0; i < count; i++) {
				if (ShaderUtil.GetPropertyName(shader, i) == name) return i;
			}
			return -1;
		}

		public void ApplyTransforms(Dictionary<string, Transform> created)
		{
			foreach (KeyValuePair<string, TransformOverride> entry in Transforms) {
				Transform target;
				if (!created.TryGetValue(entry.Key, out target) || target == null) {
					Debug.LogWarning("KAT config: no installed object named '" + entry.Key + "' (it is only created for some options, e.g. the head objects need a head bone). Known names: " + string.Join(", ", new List<string>(created.Keys).ToArray()));
					continue;
				}
				if (entry.Value.Position.HasValue) target.localPosition = entry.Value.Position.Value;
				if (entry.Value.Rotation.HasValue) target.localEulerAngles = entry.Value.Rotation.Value;
				if (entry.Value.Scale.HasValue) target.localScale = entry.Value.Scale.Value;
			}
		}

		public void LogWarnings()
		{
			foreach (string w in Warnings) Debug.LogWarning("KAT config: " + w);
		}
	}
}

#endif
