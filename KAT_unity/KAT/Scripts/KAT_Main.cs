/*
Copyright 2023 KillFrenzy / Evan Tran
Copyright 2026 Protaocol

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

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using AnimatorController = UnityEditor.Animations.AnimatorController;

#if VRC_SDK_VRCSDK3
	using ExpressionsMenu = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
	using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
	using VRCAvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
#endif

#if CVR_CCK_EXISTS
	using CVRAvatar = ABI.CCK.Components.CVRAvatar;
#endif

using KillFrenzy.AvatarTextTools.Utility;
using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools
{
	public class KillFrenzyAvatarText : EditorWindow
	{
		[SerializeField] private AnimatorController targetController = null;

		#if VRC_SDK_VRCSDK3
			[SerializeField] private ExpressionsMenu targetMenu = null;
			[SerializeField] private ExpressionParameters targetParameters = null;
			[SerializeField] private VRCAvatarDescriptor targetAvatar = null;
		#endif
		#if CVR_CCK_EXISTS
			[SerializeField] private CVRAvatar targetCVRAvatar = null;
		#endif
		[SerializeField] private int attachmentPoint = KatAttachmentPoint.Chest;
		[SerializeField] private int installKeyboard = 0; // the paged keyboard cannot set pages and costs 1 synced bit + 103 local floats
		[SerializeField] private int syncParamSize = 1; // popup index: 2^(index + 1) synced floats per group, so 1 = 4 (default)
		[SerializeField] private int pageFormatChoice = 0; // 0 = Auto (follow the built glyph atlas), 1 = format 2, 2 = format 3
		[SerializeField] private bool useWriteDefaultsOn = false;
		[SerializeField] private int slotCount = 128;
		[SerializeField] private bool installStyleSpeed = true;
		[SerializeField] private TextAsset configFile = null; // optional KAT_Config.txt, see KAT_Config.example.txt

		[SerializeField] private int tab = 0;
		[SerializeField] private bool optionsExpandVRC = false;
		[SerializeField] private bool optionsExpandCVR = false;

		// Rebuilt on demand cuz not serializable and cheap to rebuild anyways
		[NonSerialized] private GUIStyle titleStyle = null;
		[NonSerialized] private GUIStyle subtitleStyle = null;
		[NonSerialized] private GUIStyle dropdownStyle = null;
		[NonSerialized] private TextAsset parsedConfigSource = null;
		[NonSerialized] private string parsedConfigText = null;
		[NonSerialized] private KatConfig parsedConfig = null;

		[NonSerialized] private bool atlasChecked = false;
		[NonSerialized] private int atlasPages = 0;
		[NonSerialized] private int atlasFormat = 0;

		private static readonly string[] SyncChoices = {
			"2 (Average typing speed)",
			"4 (Default)",
			"8 (Speech to text speed)",
			"16 (Fastest)"
		};

		private static readonly string[] PageFormatChoices = {
			"Auto (follow the built glyph atlas)",
			"2 (default, two pages per synced float)",
			"3 (one page per synced float, very large charsets)"
		};

		[MenuItem("Tools/KAT/Install KAT")]
		public static void Open()
		{
			GetWindow<KillFrenzyAvatarText>("KAT");
		}

		private void OnFocus() { atlasChecked = false; }
		private void OnProjectChange() { atlasChecked = false; }

		private void EnsureStyles()
		{
			if (titleStyle != null) return;

			titleStyle = new GUIStyle() {
				fontSize = 14,
				fixedHeight = 28,
				fontStyle = FontStyle.Bold,
				normal = {
					textColor = Color.white
				}
			};

			subtitleStyle = new GUIStyle() {
				fontSize = 13,
				fixedHeight = 26,
				fontStyle = FontStyle.Bold,
				normal = {
					textColor = Color.white
				}
			};

			dropdownStyle = new GUIStyle("ToolbarDropDown") {
				fontSize = 12,
				fixedHeight = 22,
				fontStyle = FontStyle.Bold
			};
		}

		private void OnGUI()
		{
			EnsureStyles();
			EditorGUILayout.LabelField("KillFrenzy Avatar Text (KAT)", titleStyle);

			// One tab per available installer, then Advanced.
			List<string> labels = new List<string>();
			List<Action> pages = new List<Action>();
			#if VRC_SDK_VRCSDK3
				#if CVR_CCK_EXISTS
					labels.Add("VRChat Avatar");
				#else
					labels.Add("VRChat Avatar Installer");
				#endif
				pages.Add(DrawSimpleVRC);
			#endif
			#if CVR_CCK_EXISTS
				#if VRC_SDK_VRCSDK3
					labels.Add("ChilloutVR Avatar");
				#else
					labels.Add("ChilloutVR Avatar Installer");
				#endif
				pages.Add(DrawSimpleCVR);
			#endif

			if (pages.Count == 0) {
				EditorGUILayout.HelpBox("Neither the VRChat SDK (VRCSDK3) nor the ChilloutVR CCK was found in this project. " +
					"Import one of them first; the installer only appears once its scripting define is set.", MessageType.Warning);
				return;
			}

			labels.Add("Advanced");
			pages.Add(DrawAdvanced);

			tab = Mathf.Clamp(GUILayout.Toolbar(Mathf.Clamp(tab, 0, pages.Count - 1), labels.ToArray()), 0, pages.Count - 1);
			EditorGUILayout.Space();
			pages[tab]();
		}

		private void DrawInstallRemove(string buttonSuffix, string logName, UnityEngine.Object target, string missingMessage,
			Func<bool> preflight, Func<bool> remove, Func<bool> install)
		{
			string suffix = buttonSuffix.Length == 0 ? "" : " " + buttonSuffix;

			if (GUILayout.Button("Install/Update KAT" + suffix)) {
				if (target == null) {
					Debug.LogError("Failed: " + missingMessage);
				} else if (preflight != null && !preflight()) {
					Debug.LogError(logName + " install stopped before anything was changed.");
				} else {
					if (!remove()) {
						Debug.LogWarning("Warning: " + logName + " removal failed. This is done before installation.");
					}
					if (!install()) {
						Debug.LogError(logName + " install failed.");
					} else {
						Debug.Log(logName + " install complete.");
					}
				}
			}

			if (GUILayout.Button("Remove KAT" + suffix)) {
				if (target == null) {
					Debug.LogError("Failed: " + missingMessage);
				} else if (!remove()) {
					Debug.LogError(logName + " removal failed.");
				} else {
					Debug.Log(logName + " removal complete.");
				}
			}
		}

		#if VRC_SDK_VRCSDK3
			private bool InstallVRCAvatar()
			{
				KatSettings settings = GetKatSettings(KatSDK.VRChat);
				if (KatAvatarInstaller.InstallToAvatar(targetAvatar, settings)) return true;
				KatAvatarInstaller.RemoveFromAvatar(targetAvatar, settings);
				Debug.LogWarning("KAT VRChat install failed part-way and was rolled back; the avatar has no KAT parts left on it.");
				return false;
			}

			private void DrawSimpleVRC()
			{
				EditorGUILayout.LabelField("KAT VRChat Avatar Installer", subtitleStyle);

				targetAvatar = EditorGUILayout.ObjectField("VRChat Avatar", targetAvatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
				EditorGUILayout.Space();

				DrawOptions();

				EditorGUILayout.Space();

				DrawInstallRemove("", "KAT VRChat", targetAvatar, "No VRChat avatar has been selected.",
					() => KatPagedBuilder.PreflightInstall(GetKatSettings(KatSDK.VRChat)),
					() => KatAvatarInstaller.RemoveFromAvatar(targetAvatar, GetKatSettings(KatSDK.VRChat)),
					InstallVRCAvatar);
			}
		#endif

		#if CVR_CCK_EXISTS
			private bool InstallCVRAvatar()
			{
				KatSettings settings = GetKatSettings(KatSDK.ChilloutVR);
				if (KatAvatarInstaller.InstallToAvatar(targetCVRAvatar, settings)) return true;
				KatAvatarInstaller.RemoveFromAvatar(targetCVRAvatar, settings);
				Debug.LogWarning("KAT ChilloutVR install failed part-way and was rolled back; the avatar has no KAT parts left on it.");
				return false;
			}

			private void DrawSimpleCVR()
			{
				EditorGUILayout.LabelField("KAT ChilloutVR Avatar Installer (EXPERIMENTAL CUZ I DONT HAVE CVR CANT TEST)", subtitleStyle);

				targetCVRAvatar = EditorGUILayout.ObjectField("ChilloutVR Avatar", targetCVRAvatar, typeof(CVRAvatar), true) as CVRAvatar;
				EditorGUILayout.Space();

				DrawOptions(true);

				DrawInstallRemove("", "KAT ChilloutVR", targetCVRAvatar, "No ChilloutVR avatar has been selected.",
					() => KatPagedBuilder.PreflightInstall(GetKatSettings(KatSDK.ChilloutVR)),
					() => KatAvatarInstaller.RemoveFromAvatar(targetCVRAvatar, GetKatSettings(KatSDK.ChilloutVR)),
					InstallCVRAvatar);
			}
		#endif

		private void DrawAdvanced()
		{
			EditorGUILayout.LabelField("Options", subtitleStyle);
			DrawOptions();

			EditorGUILayout.Space();

			#if VRC_SDK_VRCSDK3
				#if CVR_CCK_EXISTS
					if (optionsExpandVRC = GUILayout.Toggle(optionsExpandVRC, "VRChat Installers", dropdownStyle)) {
						DrawAdvancedVRC();
					}
				#else
					DrawAdvancedVRC();
				#endif
			#endif

			#if CVR_CCK_EXISTS
				#if VRC_SDK_VRCSDK3
					if (optionsExpandCVR = GUILayout.Toggle(optionsExpandCVR, "ChilloutVR Installers", dropdownStyle)) {
						DrawAdvancedCVR();
					}
				#else
					DrawAdvancedCVR();
				#endif
			#endif
		}

		#if VRC_SDK_VRCSDK3
			private void DrawAdvancedVRC()
			{
				KatSettings Settings() { return GetKatSettings(KatSDK.VRChat); }

				// Animation controller installer
				EditorGUILayout.LabelField("Install to VRChat Animation Controller", subtitleStyle);
				targetController = EditorGUILayout.ObjectField("Animator Controller", targetController, typeof(AnimatorController), true) as AnimatorController;
				EditorGUILayout.Space();
				DrawInstallRemove("VRChat animations", "KAT VRChat animator", targetController, "No VRChat animator has been selected.", null,
					() => KatAnimatorInstaller.RemoveFromAnimator(targetController, Settings()),
					() => KatAnimatorInstaller.InstallToAnimator(targetController, Settings()));

				EditorGUILayout.Space();

				// Menu installer
				EditorGUILayout.LabelField("Install to VRChat Expressions Menu", subtitleStyle);
				targetMenu = EditorGUILayout.ObjectField("Expressions Menu", targetMenu, typeof(ExpressionsMenu), true) as ExpressionsMenu;
				EditorGUILayout.Space();
				DrawInstallRemove("VRChat expressions menu", "KAT VRChat expressions menu", targetMenu, "No VRChat expressions menu has been selected.", null,
					() => KatMenuInstaller.RemoveFromMenu(targetMenu, Settings()),
					() => KatMenuInstaller.InstallToMenu(targetMenu, Settings()));

				EditorGUILayout.Space();

				// Parameter installer
				EditorGUILayout.LabelField("Install to VRChat Expression Parameters", subtitleStyle);
				targetParameters = EditorGUILayout.ObjectField("Expression Parameters", targetParameters, typeof(ExpressionParameters), true) as ExpressionParameters;
				EditorGUILayout.Space();
				DrawInstallRemove("VRChat expression parameters", "KAT VRChat expression parameters", targetParameters, "No VRChat expression parameters has been selected.", null,
					() => KatParametersInstaller.RemoveFromParameters(targetParameters, Settings()),
					() => KatParametersInstaller.InstallToParameters(targetParameters, Settings()));

				EditorGUILayout.Space();

				// Objects installer
				EditorGUILayout.LabelField("Install VRChat Avatar Parts", subtitleStyle);
				targetAvatar = EditorGUILayout.ObjectField("Avatar Descriptor", targetAvatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
				EditorGUILayout.Space();
				DrawInstallRemove("VRChat avatar parts", "KAT VRChat avatar parts", targetAvatar, "No VRChat avatar descriptor has been selected.",
					() => KatPagedBuilder.PreflightInstall(Settings()),
					() => KatObjectsInstaller.RemoveObjectsFromAvatar(targetAvatar, Settings()),
					() => KatObjectsInstaller.InstallObjectsToAvatar(targetAvatar, Settings()));
			}
		#endif

		#if CVR_CCK_EXISTS
			private void DrawAdvancedCVR()
			{
				KatSettings Settings() { return GetKatSettings(KatSDK.ChilloutVR); }

				// Animation controller installer
				EditorGUILayout.LabelField("Install to ChilloutVR Animation Controller", subtitleStyle);
				targetController = EditorGUILayout.ObjectField("Animator Controller", targetController, typeof(AnimatorController), true) as AnimatorController;
				EditorGUILayout.Space();
				DrawInstallRemove("ChilloutVR animations", "KAT ChilloutVR animator", targetController, "No ChilloutVR animator has been selected.", null,
					() => KatAnimatorInstaller.RemoveFromAnimator(targetController, Settings()),
					() => KatAnimatorInstaller.InstallToAnimator(targetController, Settings()));

				EditorGUILayout.Space();

				// Objects installer
				EditorGUILayout.LabelField("Install ChilloutVR Avatar Parts", subtitleStyle);
				targetCVRAvatar = EditorGUILayout.ObjectField("Avatar Descriptor", targetCVRAvatar, typeof(CVRAvatar), true) as CVRAvatar;
				EditorGUILayout.Space();
				DrawInstallRemove("ChilloutVR avatar parts", "KAT ChilloutVR avatar parts", targetCVRAvatar, "No ChilloutVR avatar descriptor has been selected.",
					() => KatPagedBuilder.PreflightInstall(Settings()),
					() => KatObjectsInstaller.RemoveObjectsFromAvatar(targetCVRAvatar, Settings()),
					() => KatObjectsInstaller.InstallObjectsToAvatar(targetCVRAvatar, Settings()));
			}
		#endif

		private KatConfig GetConfig()
		{
			if (configFile == null) {
				parsedConfigSource = null;
				parsedConfigText = null;
				parsedConfig = null;
				return null;
			}
			if (parsedConfig == null || parsedConfigSource != configFile || parsedConfigText != configFile.text) {
				parsedConfigSource = configFile;
				parsedConfigText = configFile.text;
				parsedConfig = KatConfig.FromAsset(configFile);
				parsedConfig.LogWarnings();
			}
			return parsedConfig;
		}

		private void RefreshAtlasInfo()
		{
			if (atlasChecked) return;
			atlasFormat = KatPagedBuilder.DetectAtlasPageFormat(out atlasPages);
			atlasChecked = true;
		}

		// The page format actually used. Auto follows the built glyph atlas (its atlas_meta.json, or its page count)
		// Falls back to format 2 when there is no atlas yet.
		private int ResolvePageFormat()
		{
			if (pageFormatChoice == 1) return KatSettings.PageFormat2;
			if (pageFormatChoice == 2) return KatSettings.PageFormat3;
			RefreshAtlasInfo();
			return atlasFormat != 0 ? atlasFormat : KatSettings.PageFormat2;
		}

		private KatSettings GetKatSettings(int sdk) {
			int syncParams = 1 << (Mathf.Clamp(syncParamSize, 0, SyncChoices.Length - 1) + 1);
			return new KatSettings(sdk, installKeyboard != 0, attachmentPoint, syncParams, useWriteDefaultsOn: useWriteDefaultsOn, slotCount: slotCount, installStyleSpeedControls: installStyleSpeed, config: GetConfig(),
				pageFormat: ResolvePageFormat());
		}

		private void DrawAtlasStatus()
		{
			RefreshAtlasInfo();
			if (atlasPages == 0) {
				EditorGUILayout.HelpBox("No glyph atlas has been built in this project yet, so the avatar would get the stock (non-paged) text material. " +
					"Build one from the atlas/ folder that build_atlas.py or font2atlas.py wrote.", MessageType.Warning);
			} else {
				string format = atlasFormat != 0 ? "page format " + atlasFormat : "page format not recorded (no atlas_meta.json)";
				EditorGUILayout.HelpBox("Glyph atlas: " + atlasPages + " pages, " + format + ".", MessageType.Info);
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Build glyph array (pick atlas folder)...")) {
				KatPagedBuilder.BuildBC4();
				atlasChecked = false;
			}
			if (GUILayout.Button("Rebuild from last imported pages")) {
				KatPagedBuilder.RebuildLastBC4();
				atlasChecked = false;
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawOptions(bool hideVRCOnly = false)
		{
			if (!hideVRCOnly) {
				#if VRC_SDK_VRCSDK3
					EditorGUILayout.Space();

					#if CVR_CCK_EXISTS
						EditorGUILayout.LabelField("Include In-game keyboard (VRChat Only)");
					#else
						EditorGUILayout.LabelField("Include In-game keyboard");
					#endif

					installKeyboard = EditorGUILayout.Popup(installKeyboard, new string[2] {
						"No",
						"Yes"
					});
				#endif
			}

			EditorGUILayout.Space();

			DrawAtlasStatus();

			EditorGUILayout.Space();

			configFile = (TextAsset)EditorGUILayout.ObjectField(
				new GUIContent("Config file (optional)",
					"A KAT_Config.txt (see KAT_Config.example.txt): Sets default values for the installed parts' attributes like positions or colours etc. Leave empty for the stock defaults."),
				configFile, typeof(TextAsset), false);

			EditorGUILayout.LabelField("Default Attachment Point");
			attachmentPoint = EditorGUILayout.Popup(attachmentPoint, new string[3] {
				"None (no anchor, world-space object or manually attach)",
				"Head",
				"Chest"
			});

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Sync Parameters (Lower = Less Parameters, Higher = Faster Sync)");
			syncParamSize = EditorGUILayout.Popup(Mathf.Clamp(syncParamSize, 0, SyncChoices.Length - 1), SyncChoices);

			EditorGUILayout.Space();

			EditorGUILayout.LabelField(new GUIContent("Page format",
				"How each slot's glyph page travels to the shader. Auto (default) uses atlas_meta.json in your atlas folder, or its page count. " +
				"2: two pages packed into each synced float, up to 15 pages (14 wide pages), half as many page floats. " +
				"3: one page per synced float, up to 254 pages (253 wide pages) for very large charsets; costs more synced bits, animator layers and clips. " +
				"The OSC app needs no setting: it reads the format off the avatar."));
			pageFormatChoice = EditorGUILayout.Popup(Mathf.Clamp(pageFormatChoice, 0, PageFormatChoices.Length - 1), PageFormatChoices);
			int resolvedFormat = ResolvePageFormat();
			if (pageFormatChoice == 0) {
				EditorGUILayout.HelpBox("Using page format " + resolvedFormat + (atlasFormat != 0 ? " (from the built glyph atlas)." : " (no atlas information yet)."), MessageType.None);
			} else if (atlasFormat != 0 && atlasFormat != resolvedFormat) {
				EditorGUILayout.HelpBox("The built glyph atlas is page format " + atlasFormat + " but format " + resolvedFormat + " is selected. " +
					"The install will stop until they match: set Page format to Auto, or rebuild the atlas.", MessageType.Warning);
			}

			EditorGUILayout.Space();
			useWriteDefaultsOn = EditorGUILayout.Toggle(
				new GUIContent("WD-On",
					"Off (default): Avatar is WD-Off\nOn: Avatar is WD-On.\nIf WD-Off at WD-on avatar, text will disappear"),
				useWriteDefaultsOn);
			if (useWriteDefaultsOn) {
				EditorGUILayout.LabelField("Text slots (fewer = fewer local memory floats, doesn't affect synced bits)");
				int[] slotChoices = new int[4] { 32, 64, 96, 128 };
				int slotIndex = System.Array.IndexOf(slotChoices, slotCount);
				if (slotIndex < 0) slotIndex = 3;
				slotIndex = EditorGUILayout.Popup(slotIndex, new string[4] { "32", "64", "96", "128 (Default)" });
				slotCount = slotChoices[slotIndex];
				EditorGUILayout.HelpBox(
					"Slots must be larger than the character limit (default 128), or else slots above this number are never written.",
					MessageType.Info);
			}

			#if VRC_SDK_VRCSDK3
				EditorGUILayout.Space();
				installStyleSpeed = EditorGUILayout.Toggle(
					new GUIContent("Text style / roll speed menu controls",
						"KAT_Style and KAT_Speed are local floats (8 bits each). Untick to save 16 total expression bits; the style/head-scale layers are then skipped and the client's roll speed cannot be set from the menu."),
					installStyleSpeed);

				if (installKeyboard != 0) {
					EditorGUILayout.HelpBox(
						"The in-game keyboard adds 1 synced bit and 103 animator-only floats.",
						MessageType.Warning);
				}

				KatSettings budgetSettings = GetKatSettings(KatSDK.VRChat);
				EditorGUILayout.HelpBox(KatParameterBudget.Describe(budgetSettings), MessageType.None);
				foreach (string budgetWarning in KatParameterBudget.Warnings(budgetSettings, atlasPages > 0 ? atlasPages : -1)) {
					EditorGUILayout.HelpBox(budgetWarning, MessageType.Warning);
				}

				ExpressionParameters budgetParameters = targetParameters;
				if (budgetParameters == null && targetAvatar != null) budgetParameters = targetAvatar.expressionParameters;
				if (budgetParameters != null) {
					EditorGUILayout.HelpBox(KatParameterBudget.DescribeAgainst(budgetParameters, budgetSettings), MessageType.None);
				}
			#endif

			EditorGUILayout.Space();
		}
	}
}

#endif
