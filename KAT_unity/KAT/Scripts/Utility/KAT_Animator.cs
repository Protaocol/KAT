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

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

using System.Collections.Generic;

using AnimatorControllerLayer = UnityEditor.Animations.AnimatorControllerLayer;
using AnimatorController = UnityEditor.Animations.AnimatorController;

#if VRC_SDK_VRCSDK3
	using Driver = VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver;
	using DriverParameter = VRC.SDKBase.VRC_AvatarParameterDriver.Parameter;
	using LocomotionControl = VRC.SDK3.Avatars.Components.VRCAnimatorLocomotionControl;
#endif

using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatAnimatorInstaller
	{
		public static bool InstallToAnimator(AnimatorController controller, KatSettings settings, string headScalePivotPath = null)
		{
			KatAnimations animations = new KatAnimations(settings, headScalePivotPath);

			if (animations.valid == false) {
				Debug.LogError("Failed: Resources/" + settings.CharacterAnimationFolder + " is missing some animations.");
				return false;
			}

			// Add parameters
			// AddParameterIfMissing(controller, "IsLocal", AnimatorControllerParameterType.Bool);
			// AddParameterIfMissing(controller, "VRMode", AnimatorControllerParameterType.Int);
			AddParameterIfMissing(controller, settings.ParamTextVisible, AnimatorControllerParameterType.Bool);
			AddParameterIfMissing(controller, settings.ParamTextPointer, AnimatorControllerParameterType.Int);
			AddParameterIfMissing(controller, settings.ParamHeadAnchor, AnimatorControllerParameterType.Float);
			if (settings.InstallStyleSpeedControls) {
				AddParameterIfMissing(controller, settings.ParamSpeed, AnimatorControllerParameterType.Float);   // read by the OSC client only
			}

#if VRC_SDK_VRCSDK3
			if (settings.SDK == KatSDK.VRChat && settings.InstallStyleSpeedControls) {
				AddParameterIfMissing(controller, settings.ParamStyle, AnimatorControllerParameterType.Float);   // local presentation/style control
			}
#endif

			for (int i = 0; i < settings.SyncParamsSize; i++) {
				AddParameterIfMissing(controller, settings.ParamTextSyncPrefix + i.ToString(), AnimatorControllerParameterType.Float);
			}

			for (int i = 0; i < settings.GetPageSyncCount(); i++) {
				AddParameterIfMissing(controller, settings.ParamTextPageSyncPrefix + i.ToString(), AnimatorControllerParameterType.Float);
			}

#if VRC_SDK_VRCSDK3
				if (settings.InstallKeyboard) {
					bool hasIsLocal = false, hasVRMode = false;
					foreach (AnimatorControllerParameter existing in controller.parameters) {
						if (existing.name == "IsLocal") hasIsLocal = true;
						if (existing.name == "VRMode") hasVRMode = true;
					}
					if (!hasIsLocal) controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
					if (!hasVRMode) controller.AddParameter("VRMode", AnimatorControllerParameterType.Int);
					AddParameterIfMissing(controller, settings.ParamKeyboardPrefix, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamKeyboardProximity, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardProximityExit, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardHighlight, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedClear, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedBackspace, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedShiftL, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedShiftR, AnimatorControllerParameterType.Float);

					for (int i = 0; i < settings.KeyboardKeysCount; i++) {
						AddParameterIfMissing(controller, settings.ParamKeyboardPressed + i.ToString(), AnimatorControllerParameterType.Float);
					}
				}

				controller.AddLayer(CreateToggleLayer(controller, settings, animations));

			if (settings.InstallKeyboard) {
				controller.AddLayer(CreateKeyboardLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardProximityLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardShiftLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardHighlightLayer(controller, settings, animations));
			}
#endif

			controller.AddLayer(CreateAnchorLayer(controller, settings, animations));

#if VRC_SDK_VRCSDK3
			if (settings.SDK == KatSDK.VRChat && settings.InstallStyleSpeedControls) {
				controller.AddLayer(CreateStyleLayer(controller, settings, animations));
				if (animations.headScaleClean != null) {
					controller.AddLayer(CreateHeadScaleLayer(controller, settings, animations));
				}
			}
#endif

			if (settings.UseWriteDefaultsOn) {
				if (!KatWdOnPersistence.Install(controller, settings, animations)) {
					Debug.LogError("KAT WD-On persistent-memory installation failed.");
					return false;
				}
				ForceWriteDefaultsOnForKatLayers(controller, settings);
			} else {
				for (int i = 0; i < settings.SyncParamsSize; i++) {
					controller.AddLayer(CreateSyncLayer(controller, settings, animations, i));
				}

				for (int i = 0; i < settings.GetPageSyncCount(); i++) {
					controller.AddLayer(CreatePageSyncLayer(controller, settings, animations, i));
				}
			}

			EditorUtility.SetDirty(controller);
			Debug.Log("Installation to animator completed.");
			return true;
		}

		public static bool RemoveFromAnimator(AnimatorController controller, KatSettings settings) {
			// Remove Blend Trees
			// AssetDatabase.RemoveObjectFromAsset();
			// BlendTree[] blendTrees = controller.GetStateEffectiveMotion();

			// Remove Layers
			for (int i = controller.layers.Length - 1; i >= 0; i--) {
				AnimatorControllerLayer layer = controller.layers[i];
				if (
					layer.name == settings.ParamTextVisible ||
					layer.name.StartsWith(settings.ParamKeyboardPrefix) ||
					layer.name.StartsWith(settings.ParamTextSyncPrefix) ||
					layer.name.StartsWith(settings.ParamTextPageSyncPrefix) ||
					layer.name.StartsWith(KatWdOnPersistence.LayerPrefix) ||
					layer.name == settings.ParamHeadAnchor ||
					layer.name == settings.ParamStyle ||
					layer.name == settings.ParamStyle + "_HeadScale"
				) {
					// Non-WD-On layers own their top-level trees; WD-On layers are swept by name prefix below.
					if (!layer.name.StartsWith(KatWdOnPersistence.LayerPrefix)) {
						foreach (ChildAnimatorState childState in layer.stateMachine.states) {
							if (childState.state.motion != null && childState.state.motion is BlendTree) {
								AssetDatabase.RemoveObjectFromAsset(childState.state.motion);
							}
						}
					}
					controller.RemoveLayer(i);
				}
			}

			// Remove Parameters
			for (int i = controller.parameters.Length - 1; i >= 0; i--) {
				AnimatorControllerParameter parameter = controller.parameters[i];
				if (
					parameter.name == settings.ParamTextVisible ||
					parameter.name.StartsWith(settings.ParamKeyboardPrefix) ||
					parameter.name == settings.ParamKeyboardPressed ||
					parameter.name == settings.ParamTextPointer ||
					parameter.name.StartsWith(settings.ParamTextSyncPrefix) ||
					parameter.name.StartsWith(settings.ParamTextPageSyncPrefix) ||
					parameter.name.StartsWith(settings.ParamTextWdOnMemoryCharPrefix) ||
					parameter.name.StartsWith(settings.ParamTextWdOnMemoryPagePrefix) ||
					parameter.name == settings.ParamTextWdOnAlwaysOne ||
					parameter.name == settings.ParamHeadAnchor ||
					parameter.name == settings.ParamSpeed ||
					parameter.name == settings.ParamStyle
				) {
					controller.RemoveParameter(i);
				}
			}

			string controllerPath = AssetDatabase.GetAssetPath(controller);
			if (!string.IsNullOrEmpty(controllerPath)) {
				UnityEngine.Object[] embedded = AssetDatabase.LoadAllAssetsAtPath(controllerPath);
				foreach (UnityEngine.Object obj in embedded) {
					if (obj == null || obj == controller || !obj.name.StartsWith(KatWdOnPersistence.LayerPrefix)) continue;
					if (obj is AnimationClip || obj is BlendTree || obj is AnimatorStateMachine) {
						AssetDatabase.RemoveObjectFromAsset(obj);
					}
				}
			}

			EditorUtility.SetDirty(controller);
			AssetDatabase.SaveAssets();
			Debug.Log("Removal from animator completed.");
			return true;
		}

		//[MenuItem("Tools/KAT/KAT Paged/Audit Write Defaults")]
		public static void AuditWriteDefaultsMenuItem()
		{
			GameObject selected = Selection.activeGameObject;
			if (selected == null) {
				Debug.LogWarning("KAT: select an avatar (or anything with an Animator) in the Hierarchy first.");
				return;
			}
			Animator animator = selected.GetComponentInParent<Animator>();
			AnimatorController controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
			if (controller == null) {
				Debug.LogWarning("KAT: '" + selected.name + "' has no AnimatorController to check.");
				return;
			}

			List<string> statesWithWdOn = AuditWriteDefaults(controller, fix: false);
			if (statesWithWdOn.Count == 0) {
				Debug.Log("KAT: no Write Defaults ON states found in '" + controller.name + "'.");
			} else {
				Debug.LogWarning("KAT audit: found Write Defaults ON on " + statesWithWdOn.Count +
					" state(s) in '" + controller.name + "'. Report only; no states were modified.\n" + string.Join("\n", statesWithWdOn));
			}
		}

		public static List<string> AuditWriteDefaults(AnimatorController controller, bool fix)
		{
			List<string> problems = new List<string>();
			foreach (AnimatorControllerLayer layer in controller.layers) {
				AuditStateMachine(layer.stateMachine, layer.name, fix, problems);
			}
			return problems;
		}

		private static void AuditStateMachine(AnimatorStateMachine stateMachine, string path, bool fix, List<string> problems)
		{
			if (stateMachine == null) return;
			foreach (ChildAnimatorState child in stateMachine.states) {
				if (child.state.writeDefaultValues) {
					problems.Add(path + " / " + child.state.name);
					if (fix) child.state.writeDefaultValues = false;
				}
			}
			foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines) {
				AuditStateMachine(child.stateMachine, path + " / " + child.stateMachine.name, fix, problems);
			}
		}

		private static AnimatorControllerLayer CreateToggleLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			AnimatorStateMachine stateMachine;

			layer.name = settings.ParamTextVisible;
			layer.defaultWeight = 1f;
			layer.stateMachine = stateMachine = new AnimatorStateMachine();
			layer.stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

			AnimatorState stateDisabled = CreateState(stateMachine, "Disabled", new Vector3(300f, 200f, 0f), animations.disable);

			AnimatorState stateEnabled = CreateState(stateMachine, "Enabled", new Vector3(300f, 300f, 0f), animations.enable);
			AnimatorStateTransition transitionEnable = CreateTransition(stateDisabled, stateEnabled);
			transitionEnable.AddCondition(AnimatorConditionMode.If, 0, settings.ParamTextVisible);
			AnimatorStateTransition transitionDisable = CreateTransition(stateEnabled, stateDisabled);
			transitionDisable.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamTextVisible);

			if (settings.InstallKeyboard) {
				transitionDisable.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);

				AnimatorStateTransition transitionEnable2 = CreateTransition(stateDisabled, stateEnabled);
				transitionEnable2.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
			}

#if CVR_CCK_EXISTS
				if (settings.SDK == KatSDK.ChilloutVR) {
					layer.avatarMask = animations.avatarMask;
				}
#endif

			return layer;
		}

		private static AnimatorControllerLayer CreateAnchorLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			AnimatorStateMachine stateMachine = new AnimatorStateMachine();

			layer.name = settings.ParamHeadAnchor;
			layer.defaultWeight = 1f;
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			BlendTree blendTree = new BlendTree();
			blendTree.name = "KAT Anchor BlendTree";
			blendTree.blendParameter = settings.ParamHeadAnchor;
			blendTree.blendType = BlendTreeType.Simple1D;
			blendTree.useAutomaticThresholds = false;
			blendTree.AddChild(animations.anchorChest, 0.0f);
			blendTree.AddChild(animations.anchorHeadLeft, 0.2f);
			blendTree.AddChild(animations.anchorHeadLeft, 0.3f);
			blendTree.AddChild(animations.anchorHeadRight, 0.6f);
			blendTree.AddChild(animations.anchorHeadRight, 0.7f);
			blendTree.AddChild(animations.anchorHeadTop, 1.0f);
			AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));

			CreateState(stateMachine, "Anchor", new Vector3(300f, 250f, 0f), blendTree);
			return layer;
		}

		private static AnimatorControllerLayer CreateStyleLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = settings.ParamStyle;
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			BlendTree blendTree = new BlendTree();
			blendTree.name = "KAT Style BlendTree";
			blendTree.blendParameter = settings.ParamStyle;
			blendTree.blendType = BlendTreeType.Simple1D;
			blendTree.useAutomaticThresholds = false;
			blendTree.AddChild(animations.styleClean, 0.0f);
			blendTree.AddChild(animations.styleDefault, 0.5f);
			blendTree.AddChild(animations.styleRoomy, 1.0f);
			AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));

			CreateState(stateMachine, "Style", new Vector3(300f, 200f, 0f), blendTree);
			return layer;
		}

        // Only call this when animations.headScaleClean != null (i.e. an avatar head was actually found at install time)
		private static AnimatorControllerLayer CreateHeadScaleLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = settings.ParamStyle + "_HeadScale";
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			BlendTree blendTree = new BlendTree();
			blendTree.name = "KAT Head Scale BlendTree";
			blendTree.blendParameter = settings.ParamStyle;
			blendTree.blendType = BlendTreeType.Simple1D;
			blendTree.useAutomaticThresholds = false;
			blendTree.AddChild(animations.headScaleClean, 0.0f);
			blendTree.AddChild(animations.headScaleDefault, 0.5f);
			blendTree.AddChild(animations.headScaleRoomy, 1.0f);
			AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));
			AssetDatabase.AddObjectToAsset(animations.headScaleClean, AssetDatabase.GetAssetPath(controller));
			AssetDatabase.AddObjectToAsset(animations.headScaleDefault, AssetDatabase.GetAssetPath(controller));
			AssetDatabase.AddObjectToAsset(animations.headScaleRoomy, AssetDatabase.GetAssetPath(controller));

			CreateState(stateMachine, "Head Scale", new Vector3(300f, 260f, 0f), blendTree);
			return layer;
		}

		private static AnimatorControllerLayer CreateSyncLayer(AnimatorController controller, KatSettings settings, KatAnimations animations, int logicId)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			AnimatorStateMachine stateMachine;

			layer.name = settings.ParamTextSyncPrefix + logicId.ToString();
			layer.defaultWeight = 1f;
			layer.stateMachine = stateMachine = new AnimatorStateMachine();
			layer.stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

			// Default state
			AnimatorState stateDisabled = CreateState(stateMachine, "Disabled", new Vector3(300f, 200f, 0f), animations.nothing);
			AnimatorStateTransition transitionDisabled = CreateTransition(stateMachine, stateDisabled);
			transitionDisabled.AddCondition(AnimatorConditionMode.Equals, 0, settings.ParamTextPointer);

			// Standby state - waits for updates to the pointer
			AnimatorState stateStandby = CreateState(stateMachine, "StandBy", new Vector3(300f, 500f, 0f), animations.nothing);
			AnimatorStateTransition transitionStandby = CreateTransition(stateDisabled, stateStandby);
			transitionStandby.AddCondition(AnimatorConditionMode.NotEqual, 0, settings.ParamTextPointer);

			if (settings.InstallKeyboard) {
				string backspaceChannel = settings.ParamTextSyncPrefix + logicId.ToString();
				AnimatorStateTransition transitionDisabled2 = CreateTransition(stateMachine, stateDisabled);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Greater, settings.KeyboardBackspaceMode - 0.005f, backspaceChannel);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Less, settings.KeyboardBackspaceMode + 0.005f, backspaceChannel);

				transitionStandby.AddCondition(AnimatorConditionMode.Less, settings.KeyboardBackspaceMode - 0.005f, backspaceChannel);

				AnimatorStateTransition transitionStandby2 = CreateTransition(stateDisabled, stateStandby);
				transitionStandby2.AddCondition(AnimatorConditionMode.NotEqual, 0, settings.ParamTextPointer);
				transitionStandby2.AddCondition(AnimatorConditionMode.Greater, settings.KeyboardBackspaceMode + 0.005f, backspaceChannel);
			}

			for (int i = 0; i < settings.PointerCount; i++) {
				int pointerIndex = i + 1;
				int charIndex = settings.SyncParamsSize * i + logicId;

				float pointerPosOffsetY = 500f + 50f * i;

				BlendTree blendTree = new BlendTree();
				// controller.CreateBlendTreeInController("Char" + charIndex + " BlendTree", out blendTree);
				blendTree.name = "Char" + charIndex + " BlendTree";
				blendTree.blendParameter = settings.ParamTextSyncPrefix + logicId.ToString();
				blendTree.blendType = BlendTreeType.Simple1D;
				blendTree.useAutomaticThresholds = false;
				blendTree.AddChild(animations.charsStart[charIndex], -1.0f);
				blendTree.AddChild(animations.charsEnd[charIndex], 1.0f);
				AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));

				AnimatorState statePointerChar = CreateState(stateMachine, "Pointer" + pointerIndex.ToString() + " Char" + charIndex, new Vector3(800f, pointerPosOffsetY, 0f), blendTree);
				statePointerChar.timeParameter = settings.ParamTextSyncPrefix + logicId.ToString();
				statePointerChar.timeParameterActive = true;

				AnimatorStateTransition transitionPointerStart = stateMachine.AddAnyStateTransition(statePointerChar);
				transitionPointerStart.duration = 0f;
				transitionPointerStart.canTransitionToSelf = false;
				transitionPointerStart.AddCondition(AnimatorConditionMode.Equals, pointerIndex, settings.ParamTextPointer);

				// Create alternate transitions to point to a single character at a time (for in-game keyboard)
				if (settings.InstallKeyboard && settings.PointerAltSyncOffset != 0) {
					AnimatorStateTransition transitionAltStart = stateMachine.AddAnyStateTransition(statePointerChar);
					transitionAltStart.duration = 0f;
					transitionAltStart.canTransitionToSelf = false;
					transitionAltStart.AddCondition(AnimatorConditionMode.Equals, settings.PointerAltSyncOffset + charIndex + 1, settings.ParamTextPointer);
				}
			}

			AnimatorState stateClear = CreateState(stateMachine, "Clear Text", new Vector3(800f, 500f - 50f, 0f), animations.clear);
			stateClear.timeParameter = settings.ParamTextSyncPrefix + logicId.ToString();
			stateClear.timeParameterActive = true;

			AnimatorStateTransition clearStartTransition = stateMachine.AddAnyStateTransition(stateClear);
			clearStartTransition.duration = 0f;
			clearStartTransition.canTransitionToSelf = false;
			clearStartTransition.AddCondition(AnimatorConditionMode.Equals, 255, settings.ParamTextPointer);
			AnimatorStateTransition clearReturnTransition = CreateTransition(stateClear, stateStandby);
			clearReturnTransition.AddCondition(AnimatorConditionMode.NotEqual, 255, settings.ParamTextPointer);

#if CVR_CCK_EXISTS
				if (settings.SDK == KatSDK.ChilloutVR) {
					layer.avatarMask = animations.avatarMask;
				}
#endif

			return layer;
		}


		private static AnimatorControllerLayer CreatePageSyncLayer(AnimatorController controller, KatSettings settings, KatAnimations animations, int logicId)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			AnimatorStateMachine stateMachine;
			string syncParam = settings.ParamTextPageSyncPrefix + logicId.ToString();

			layer.name = syncParam;
			layer.defaultWeight = 1f;
			layer.stateMachine = stateMachine = new AnimatorStateMachine();
			layer.stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

			AnimatorState stateDisabled = CreateState(stateMachine, "Disabled", new Vector3(300f, 200f, 0f), animations.nothing);
			AnimatorStateTransition transitionDisabled = CreateTransition(stateMachine, stateDisabled);
			transitionDisabled.AddCondition(AnimatorConditionMode.Equals, 0, settings.ParamTextPointer);

			AnimatorState stateStandby = CreateState(stateMachine, "StandBy", new Vector3(300f, 500f, 0f), animations.nothing);
			AnimatorStateTransition transitionStandby = CreateTransition(stateDisabled, stateStandby);
			transitionStandby.AddCondition(AnimatorConditionMode.NotEqual, 0, settings.ParamTextPointer);

			for (int i = 0; i < settings.PointerCount; i++) {
				int pointerIndex = i + 1;
				int pairIndex = settings.GetPageSyncCount() * i + logicId; // a pair in format 2, a slot in format 3
				float pointerPosOffsetY = 500f + 50f * i;

				BlendTree blendTree = new BlendTree();
				blendTree.name = "Page" + pairIndex + " BlendTree";
				blendTree.blendParameter = syncParam;
				blendTree.blendType = BlendTreeType.Simple1D;
				blendTree.useAutomaticThresholds = false;
				blendTree.AddChild(animations.pageStart[pairIndex], -1.0f);
				blendTree.AddChild(animations.pageEnd[pairIndex], 1.0f);
				AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));

				AnimatorState statePointerPage = CreateState(stateMachine, "Pointer" + pointerIndex.ToString() + " Page" + pairIndex, new Vector3(800f, pointerPosOffsetY, 0f), blendTree);
				statePointerPage.timeParameter = syncParam;
				statePointerPage.timeParameterActive = true;

				AnimatorStateTransition transitionPointerStart = stateMachine.AddAnyStateTransition(statePointerPage);
				transitionPointerStart.duration = 0f;
				transitionPointerStart.canTransitionToSelf = false;
				transitionPointerStart.AddCondition(AnimatorConditionMode.Equals, pointerIndex, settings.ParamTextPointer);
			}

			AnimatorState stateClear = CreateState(stateMachine, "Clear Pages", new Vector3(800f, 450f, 0f), animations.pageClear);
			stateClear.timeParameter = syncParam;
			stateClear.timeParameterActive = true;

			AnimatorStateTransition clearStartTransition = stateMachine.AddAnyStateTransition(stateClear);
			clearStartTransition.duration = 0f;
			clearStartTransition.canTransitionToSelf = false;
			clearStartTransition.AddCondition(AnimatorConditionMode.Equals, 255, settings.ParamTextPointer);
			AnimatorStateTransition clearReturnTransition = CreateTransition(stateClear, stateStandby);
			clearReturnTransition.AddCondition(AnimatorConditionMode.NotEqual, 255, settings.ParamTextPointer);

			return layer;
		}

#if VRC_SDK_VRCSDK3
			private static AnimatorControllerLayer CreateKeyboardLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				AnimatorStateMachine stateMachine;

				layer.name = settings.ParamKeyboardPrefix;
				layer.defaultWeight = 1f;
				layer.stateMachine = stateMachine = new AnimatorStateMachine();
				layer.stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

				// Default state
				AnimatorState stateDisabled = CreateState(stateMachine, "Disabled", new Vector3(300f, 200f, 0f), animations.keyboardDisable);
				AnimatorStateTransition transitionDisabled = CreateTransition(stateMachine, stateDisabled);
				transitionDisabled.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);

				Driver driverDisabled = stateDisabled.AddStateMachineBehaviour<Driver>();
				driverDisabled.localOnly = true;
				driverDisabled.parameters = new List<DriverParameter>();

				// Init state
				AnimatorState stateInit = CreateState(stateMachine, "Init", new Vector3(300f, 400f, 0f), animations.keyboardInit);
				stateInit.speed = 1.0f;
				AnimatorStateTransition transitionInit = CreateTransition(stateDisabled, stateInit);
				transitionInit.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transitionInit.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamTextVisible);

				Driver driverInit = stateInit.AddStateMachineBehaviour<Driver>();
				driverInit.localOnly = true;
				driverInit.parameters = new List<DriverParameter>();

				DriverParameter driverStateInit1 = new DriverParameter();
				driverStateInit1.name = settings.ParamTextPointer;
				driverStateInit1.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverStateInit1.value = 255;
				driverInit.parameters.Add(driverStateInit1);

				DriverParameter driverStateInit2 = new DriverParameter();
				driverStateInit2.name = settings.ParamKeyboardHighlight;
				driverStateInit2.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverStateInit2.value = ConvertKeyToFloat(100);
				driverInit.parameters.Add(driverStateInit2);

				for (int i = 0; i < settings.SyncParamsSize; i++) {
					DriverParameter driverParameterChar = new DriverParameter();
					driverParameterChar.name = settings.ParamTextSyncPrefix + i.ToString();
					driverParameterChar.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
					driverParameterChar.value = 0.0f;
					driverInit.parameters.Add(driverParameterChar);
				}

				// Alternate init state
				AnimatorState stateInitAlt = CreateState(stateMachine, "Init App Active", new Vector3(600f, 300f, 0f), animations.keyboardInit);
				stateInitAlt.speed = 1.0f;
				AnimatorStateTransition transitionInitAlt = CreateTransition(stateDisabled, stateInitAlt);
				transitionInitAlt.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transitionInitAlt.AddCondition(AnimatorConditionMode.If, 0, settings.ParamTextVisible);

				Driver driverInitAlt = stateInitAlt.AddStateMachineBehaviour<Driver>();
				driverInitAlt.localOnly = true;
				driverInitAlt.parameters = new List<DriverParameter>();

				DriverParameter driverStateInitAlt = new DriverParameter();
				driverStateInitAlt.name = settings.ParamKeyboardHighlight;
				driverStateInitAlt.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverStateInitAlt.value = ConvertKeyToFloat(100);
				driverInitAlt.parameters.Add(driverStateInitAlt);

				// Standby state
				AnimatorState stateStandby = CreateState(stateMachine, "StandBy", new Vector3(300f, 500f, 0f), animations.keyboardEnable);
				AnimatorStateTransition transitionEnabled = CreateTransition(stateInit, stateStandby);
				transitionEnabled.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transitionEnabled.hasExitTime = true;
				transitionEnabled.exitTime = 0.2f;

				Driver driverStandBy = stateStandby.AddStateMachineBehaviour<Driver>();
				driverStandBy.localOnly = true;
				driverStandBy.parameters = new List<DriverParameter>();

				DriverParameter driverResetHighlight = new DriverParameter();
				driverResetHighlight.name = settings.ParamKeyboardHighlight;
				driverResetHighlight.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverResetHighlight.value = ConvertKeyToFloat(100);
				driverStandBy.parameters.Add(driverResetHighlight);

				// Active state
				AnimatorState stateAppActive = CreateState(stateMachine, "App Active", new Vector3(600f, 400f, 0f), animations.keyboardEnable);
				AnimatorStateTransition transitionAppActiveEnter = CreateTransition(stateStandby, stateAppActive);
				transitionAppActiveEnter.AddCondition(AnimatorConditionMode.If, 0, settings.ParamTextVisible);
				AnimatorStateTransition transitionAppActiveEnter2 = CreateTransition(stateStandby, stateAppActive);
				transitionAppActiveEnter2.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLocal");
				AnimatorStateTransition transitionAppActiveExit = CreateTransition(stateAppActive, stateStandby);
				transitionAppActiveExit.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamTextVisible);

				AnimatorStateTransition transitionEnabled2 = CreateTransition(stateInitAlt, stateAppActive);
				transitionEnabled2.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transitionEnabled2.hasExitTime = true;
				transitionEnabled2.exitTime = 0.2f;

				// Fix Pointer
				AnimatorState stateFix = CreateState(stateMachine, "Fix Pointer", new Vector3(0f, 500f, 0f), animations.nothing);
				AnimatorStateTransition transitionFixEnter1 = CreateTransition(stateStandby, stateFix);
				transitionFixEnter1.AddCondition(AnimatorConditionMode.Greater, settings.PointerAltSyncOffset + settings.TextLength, settings.ParamTextPointer);
				transitionFixEnter1.duration = 0.2f;
				transitionFixEnter1.hasFixedDuration = true;
				AnimatorStateTransition transitionFixEnter2 = CreateTransition(stateStandby, stateFix);
				transitionFixEnter2.AddCondition(AnimatorConditionMode.Less, settings.PointerAltSyncOffset, settings.ParamTextPointer);
				transitionFixEnter2.duration = 0.2f;
				transitionFixEnter2.hasFixedDuration = true;
				AnimatorStateTransition transitionFixExit = CreateTransition(stateFix, stateStandby);
				transitionFixExit.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);

				Driver driverFix = stateFix.AddStateMachineBehaviour<Driver>();
				driverFix.localOnly = true;
				driverFix.parameters = new List<DriverParameter>();

				DriverParameter driverParameterFix = new DriverParameter();
				driverParameterFix.name = settings.ParamTextPointer;
				driverParameterFix.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverParameterFix.value = settings.PointerAltSyncOffset;
				driverFix.parameters.Add(driverParameterFix);

				// Create keyboard presses
				for (int i = 0; i < settings.KeyboardKeysCount; i++) {
					int key = i;
					string keyString = key.ToString();
					float positionY = 500f + 50f * i;

					AnimatorState stateKeyPress = CreateState(stateMachine, "Key" + keyString, new Vector3(600f, positionY, 0f), animations.nothing);
					AnimatorStateTransition transitionKeyDown = CreateTransition(stateStandby, stateKeyPress);
					transitionKeyDown.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressed + keyString);
					transitionKeyDown.duration = 0.2f;
					transitionKeyDown.hasFixedDuration = true;
					AnimatorStateTransition transitionKeyUp = CreateTransition(stateKeyPress, stateStandby);
					transitionKeyUp.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressed + keyString);

					Driver driver = stateKeyPress.AddStateMachineBehaviour<Driver>();
					driver.localOnly = true;
					driver.parameters = new List<DriverParameter>();

					DriverParameter driverParameterPointer = new DriverParameter();
					driverParameterPointer.name = settings.ParamTextPointer;
					driverParameterPointer.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add;
					driverParameterPointer.value = 1;
					driver.parameters.Add(driverParameterPointer);

					DriverParameter driverHighlight = new DriverParameter();
					driverHighlight.name = settings.ParamKeyboardHighlight;
					driverHighlight.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
					driverHighlight.value = ConvertKeyToFloat(key);
					driver.parameters.Add(driverHighlight);

					for (int j = 0; j < settings.SyncParamsSize; j++) {
						DriverParameter driverParameterChar = new DriverParameter();
						driverParameterChar.name = settings.ParamTextSyncPrefix + j.ToString();
						driverParameterChar.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
						driverParameterChar.value = ConvertKeyToFloat(key);
						driver.parameters.Add(driverParameterChar);
					}
				}

				// Create clear key
				AnimatorState stateKeyClear = CreateState(stateMachine, "KeyClear", new Vector3(0f, 550f, 0f), animations.nothing);
				AnimatorStateTransition transitionKeyClearDown = CreateTransition(stateStandby, stateKeyClear);
				transitionKeyClearDown.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedClear);
				transitionKeyClearDown.duration = 0.2f;
				transitionKeyClearDown.hasFixedDuration = true;
				AnimatorStateTransition transitionKeyClearUp = CreateTransition(stateKeyClear, stateStandby);
				transitionKeyClearUp.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedClear);

				Driver driveKeyClear = stateKeyClear.AddStateMachineBehaviour<Driver>();
				driveKeyClear.localOnly = true;
				driveKeyClear.parameters = new List<DriverParameter>();

				DriverParameter driverKeyClearParameter = new DriverParameter();
				driverKeyClearParameter.name = settings.ParamTextPointer;
				driverKeyClearParameter.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverKeyClearParameter.value = 255;
				driveKeyClear.parameters.Add(driverKeyClearParameter);

				DriverParameter driverHighlightClear = new DriverParameter();
				driverHighlightClear.name = settings.ParamKeyboardHighlight;
				driverHighlightClear.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverHighlightClear.value = ConvertKeyToFloat(settings.KeyboardKeyClear);
				driveKeyClear.parameters.Add(driverHighlightClear);

				for (int i = 0; i < settings.SyncParamsSize; i++) {
					DriverParameter driverParameterChar = new DriverParameter();
					driverParameterChar.name = settings.ParamTextSyncPrefix + i.ToString();
					driverParameterChar.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
					driverParameterChar.value = 0.0f;
					driveKeyClear.parameters.Add(driverParameterChar);
				}

				// Create backspace start key
				AnimatorState stateKeyBkspStart = CreateState(stateMachine, "KeyBackspaceStart", new Vector3(0f, 600f, 0f), animations.nothing);
				AnimatorStateTransition transitionKeyBkspDownStart = CreateTransition(stateStandby, stateKeyBkspStart);
				transitionKeyBkspDownStart.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedBackspace);
				transitionKeyBkspDownStart.duration = 0.2f;
				transitionKeyBkspDownStart.hasFixedDuration = true;

				Driver driverBkspStart = stateKeyBkspStart.AddStateMachineBehaviour<Driver>();
				driverBkspStart.localOnly = true;
				driverBkspStart.parameters = new List<DriverParameter>();

				DriverParameter driverHighlightBksp = new DriverParameter();
				driverHighlightBksp.name = settings.ParamKeyboardHighlight;
				driverHighlightBksp.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				driverHighlightBksp.value = ConvertKeyToFloat(settings.KeyboardKeyBackspace);
				driverBkspStart.parameters.Add(driverHighlightBksp);

				for (int i = 0; i < settings.SyncParamsSize; i++) {
					DriverParameter driverParameterChar = new DriverParameter();
					driverParameterChar.name = settings.ParamTextSyncPrefix + i.ToString();
					driverParameterChar.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
					driverParameterChar.value = 0.0f;
					driverBkspStart.parameters.Add(driverParameterChar);
				}

				// Create backspace end key
				AnimatorState stateKeyBkspEnd = CreateState(stateMachine, "KeyBackspaceEnd", new Vector3(0f, 650f, 0f), animations.nothing);
				AnimatorStateTransition transitionKeyBkspEnd = CreateTransition(stateKeyBkspEnd, stateStandby);
				transitionKeyBkspEnd.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedBackspace);

				AnimatorStateTransition transitionKeyBkspUpFirst = CreateTransition(stateKeyBkspStart, stateKeyBkspEnd);
				transitionKeyBkspUpFirst.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transitionKeyBkspUpFirst.duration = 0.2f;
				transitionKeyBkspUpFirst.hasFixedDuration = true;

				Driver driverBksp = stateKeyBkspEnd.AddStateMachineBehaviour<Driver>();
				driverBksp.localOnly = true;
				driverBksp.parameters = new List<DriverParameter>();

				DriverParameter driverParameterBksp = new DriverParameter();
				driverParameterBksp.name = settings.ParamTextPointer;
				driverParameterBksp.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add;
				driverParameterBksp.value = -1;
				driverBksp.parameters.Add(driverParameterBksp);

				for (int j = 0; j < settings.SyncParamsSize; j++) {
					DriverParameter driverParameterChar = new DriverParameter();
					driverParameterChar.name = settings.ParamTextSyncPrefix + j.ToString();
					driverParameterChar.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
					driverParameterChar.value = settings.KeyboardBackspaceMode;
					driverBksp.parameters.Add(driverParameterChar);
				}

				return layer;
			}

			private static AnimatorControllerLayer CreateKeyboardShiftLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				AnimatorStateMachine stateMachine;

				layer.name = settings.ParamKeyboardShift;
				layer.defaultWeight = 1f;
				layer.stateMachine = stateMachine = new AnimatorStateMachine();
				layer.stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

				// Create states
				AnimatorState stateDisabledStart = CreateState(stateMachine, "Disabled Start", new Vector3(300f, 200f, 0f), animations.keyboardShiftRelease);
				AnimatorState stateDisabledEnd = CreateState(stateMachine, "Disabled End", new Vector3(600f, 200f, 0f), animations.keyboardShiftRelease);
				AnimatorState stateEnabledStart = CreateState(stateMachine, "Enabled Start", new Vector3(600f, 300f, 0f), animations.keyboardShiftPress);
				AnimatorState stateEnabledEnd = CreateState(stateMachine, "Enabled End", new Vector3(300f, 300f, 0f), animations.keyboardShiftPress);

				// Create transitions
				AnimatorStateTransition transition;

				transition = CreateTransition(stateDisabledStart, stateDisabledEnd);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedShiftL);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedShiftR);
				transition.AddCondition(AnimatorConditionMode.If, 0, "IsLocal");

				transition = CreateTransition(stateDisabledEnd, stateEnabledStart);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedShiftL);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;
				transition = CreateTransition(stateDisabledEnd, stateEnabledStart);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedShiftR);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateEnabledStart, stateEnabledEnd);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedShiftL);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedShiftR);

				transition = CreateTransition(stateEnabledEnd, stateDisabledStart);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedShiftL);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;
				transition = CreateTransition(stateEnabledEnd, stateDisabledStart);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedShiftR);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				return layer;
			}

			private static AnimatorControllerLayer CreateKeyboardProximityLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				AnimatorStateMachine stateMachine;

				layer.name = settings.ParamKeyboardProximity;
				layer.defaultWeight = 1f;
				layer.stateMachine = stateMachine = new AnimatorStateMachine();
				layer.stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

				// Create states
				AnimatorState stateProximityFar = CreateState(stateMachine, "Proximity Far", new Vector3(300f, 200f, 0f), animations.keyboardProximityFar);
				AnimatorState stateProximityClose = CreateState(stateMachine, "Proximity Close", new Vector3(600f, 200f, 0f), animations.keyboardProximityClose);
				AnimatorState stateProximityCloseDesktop = CreateState(stateMachine, "Proximity Close Desktop", new Vector3(300f, 300f, 0f), animations.keyboardProximityClose);

				// Create transitions
				AnimatorStateTransition transition;

				transition = CreateTransition(stateProximityClose, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardProximity);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardProximityExit);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityClose, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.If, 0, settings.ParamTextVisible);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityClose, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityFar, stateProximityClose);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardProximity);
				transition.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamTextVisible);
				transition.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transition.AddCondition(AnimatorConditionMode.Equals, 1, "VRMode");
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				// Creat desktop mode transitions (currently just visual)
				transition = CreateTransition(stateProximityCloseDesktop, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardProximity);
				transition.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardProximityExit);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityCloseDesktop, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.If, 0, settings.ParamTextVisible);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityCloseDesktop, stateProximityFar);
				transition.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				transition = CreateTransition(stateProximityFar, stateProximityCloseDesktop);
				transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardProximity);
				transition.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamTextVisible);
				transition.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
				transition.AddCondition(AnimatorConditionMode.Equals, 0, "VRMode");
				transition.hasFixedDuration = true;
				transition.duration = 0.1f;

				// Create locomotion lock
				LocomotionControl locomotionControlClose = stateProximityClose.AddStateMachineBehaviour<LocomotionControl>();
				locomotionControlClose.disableLocomotion = true;

				LocomotionControl locomotionControlFar = stateProximityFar.AddStateMachineBehaviour<LocomotionControl>();
				locomotionControlFar.disableLocomotion = false;

				return layer;
			}

			private static AnimatorControllerLayer CreateKeyboardHighlightLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				AnimatorStateMachine stateMachine;

				layer.name = settings.ParamKeyboardHighlight;
				layer.defaultWeight = 1f;
				layer.stateMachine = stateMachine = new AnimatorStateMachine();
				layer.stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

				AnimatorState stateHighlight = CreateState(stateMachine, "Highlight", new Vector3(300f, 200f, 0f), animations.keyboardHighlightOn);
				stateHighlight.timeParameter = settings.ParamKeyboardHighlight;
				stateHighlight.timeParameterActive = true;

			return layer;
		}
#endif

		private static void ForceWriteDefaultsOnForKatLayers(AnimatorController controller, KatSettings settings) {
			foreach (AnimatorControllerLayer layer in controller.layers) {
				if (!IsKatLayerName(layer.name, settings)) continue;
				SetWriteDefaults(layer.stateMachine, true);
			}
		}

		private static bool IsKatLayerName(string name, KatSettings settings) {
			return name == settings.ParamTextVisible ||
				name.StartsWith(settings.ParamKeyboardPrefix) ||
				name.StartsWith(KatWdOnPersistence.LayerPrefix) ||
				name.StartsWith(settings.ParamTextSyncPrefix) ||
				name.StartsWith(settings.ParamTextPageSyncPrefix) ||
				name == settings.ParamHeadAnchor ||
				name == settings.ParamStyle ||
				name == settings.ParamStyle + "_HeadScale";
		}

		private static void SetWriteDefaults(AnimatorStateMachine stateMachine, bool value) {
			foreach (ChildAnimatorState child in stateMachine.states) {
				child.state.writeDefaultValues = value;
			}
			foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines) {
				SetWriteDefaults(child.stateMachine, value);
			}
		}

		public static AnimatorState CreateState(AnimatorStateMachine stateMachine, string name, Vector3 position, Motion animation)
		{
			AnimatorState state = stateMachine.AddState(name, position);
			state.motion = animation;
			state.writeDefaultValues = false;
			state.speed = 0f;
			return state;
		}

		public static AnimatorStateTransition CreateTransition(AnimatorState source, AnimatorState destination)
		{
			AnimatorStateTransition transition = source.AddExitTransition(false);
			transition.destinationState = destination;
			transition.duration = 0;
			return transition;
		}

		public static AnimatorStateTransition CreateTransition(AnimatorStateMachine source, AnimatorState destination)
		{
			AnimatorStateTransition transition = source.AddAnyStateTransition(destination);
			transition.duration = 0;
			transition.canTransitionToSelf = false;
			return transition;
		}

		private static bool AddParameterIfMissing(AnimatorController controller, string name, AnimatorControllerParameterType type)
		{
			foreach (AnimatorControllerParameter parameter in controller.parameters) {
				if (parameter.name == name) {
					if (parameter.type == type) {
						return true;
					} else {
						return false;
					}
				}
			}
			controller.AddParameter(name, type);
			return true;
		}

		private static float ConvertKeyToFloat(int key)
		{
			// Must match symbol_to_float in kat_codec.py: symbols 0..255 map to k/127 with k in -127..127.
			float value = (float)key;
			if (value > 127f) {
				value = value - 255.0f;
			}
			value = value / 127.0f;
			return value;
		}
	}

	public class KatAnimations
	{
		public readonly AnimationClip[] charsStart;
		public readonly AnimationClip[] charsEnd;
		public readonly AnimationClip clear;
		public readonly AnimationClip[] pageStart;
		public readonly AnimationClip[] pageEnd;
		public readonly AnimationClip pageClear;
		public readonly AnimationClip anchorChest;
		public readonly AnimationClip anchorHeadLeft;
		public readonly AnimationClip anchorHeadRight;
		public readonly AnimationClip anchorHeadTop;
		public readonly AnimationClip styleClean;
		public readonly AnimationClip styleDefault;
		public readonly AnimationClip styleRoomy;
		public readonly AnimationClip headScaleClean;
		public readonly AnimationClip headScaleDefault;
		public readonly AnimationClip headScaleRoomy;
		public readonly AnimationClip disable;
		public readonly AnimationClip enable;
		public readonly AnimationClip nothing;

		public readonly AnimationClip keyboardDisable;
		public readonly AnimationClip keyboardEnable;
		public readonly AnimationClip keyboardInit;
		public readonly AnimationClip keyboardShiftPress;
		public readonly AnimationClip keyboardShiftRelease;
		public readonly AnimationClip keyboardProximityClose;
		public readonly AnimationClip keyboardProximityFar;
		public readonly AnimationClip keyboardHighlightOn;
		public readonly AnimationClip keyboardHighlightOff;

#if CVR_CCK_EXISTS
			public readonly AvatarMask avatarMask;
#endif


		public readonly bool valid;

		public KatAnimations(KatSettings settings, string headScalePivotPath = null)
		{
			valid = true;

			charsStart = GetCharAnimations(settings, false);
			charsEnd = GetCharAnimations(settings, true);
			pageStart = KatPagedClips.GetPageClips(settings, false);
			pageEnd = KatPagedClips.GetPageClips(settings, true);
			pageClear = KatPagedClips.GetPageClear(settings);
			AnimationClip[] anchors = KatPagedClips.GetAnchorClips(settings);
			anchorChest = anchors[0];
			anchorHeadLeft = anchors[1];
			anchorHeadRight = anchors[2];
			anchorHeadTop = anchors[3];
			AnimationClip[] styles = KatPagedClips.GetStyleClips(settings);
			styleClean = styles[0];
			styleDefault = styles[1];
			styleRoomy = styles[2];
			if (!string.IsNullOrEmpty(headScalePivotPath)) {
				AnimationClip[] headScales = KatPagedClips.GetHeadScaleClips(settings, headScalePivotPath);
				headScaleClean = headScales[0];
				headScaleDefault = headScales[1];
				headScaleRoomy = headScales[2];
			}
			disable = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Disable");
			enable = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Enable");
			nothing = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Nothing");
			clear = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + settings.CharacterAnimationClipNamePrefix + "Clear");

			if (
				charsStart == null ||
				charsEnd == null ||
				disable == null ||
				enable == null ||
				nothing == null ||
				styleClean == null ||
				styleDefault == null ||
				styleRoomy == null
			) {
				valid = false;
			}

			if (settings.InstallKeyboard) {
				keyboardDisable = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Disable");
				keyboardEnable = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Enable");
				keyboardInit = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Init");
				keyboardShiftPress = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Shift_Press");
				keyboardShiftRelease = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Shift_Release");
				keyboardProximityClose = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Proximity_Close");
				keyboardProximityFar = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Proximity_Far");
				keyboardHighlightOn = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Highlight_On");
				keyboardHighlightOff = Resources.Load<AnimationClip>(settings.CharacterAnimationFolder + "KAT_Keyboard_Highlight_Off");

				if (
					keyboardDisable == null ||
					keyboardEnable == null ||
					keyboardInit == null ||
					keyboardShiftPress == null ||
					keyboardShiftRelease == null ||
					keyboardProximityClose == null ||
					keyboardProximityFar == null ||
					keyboardHighlightOn == null ||
					keyboardHighlightOff == null
				) {
					valid = false;
				}
			}

#if CVR_CCK_EXISTS
				if (settings.SDK == KatSDK.ChilloutVR) {
					avatarMask = (AvatarMask)AssetDatabase.LoadAssetAtPath("Assets/ABI.CCK/Animations/Toggles.mask", typeof(AvatarMask));
				}
#endif
		}

		private static AnimationClip[] GetCharAnimations(KatSettings settings, bool end = false)
		{
			AnimationClip[] animationClips = Resources.LoadAll<AnimationClip>(settings.CharacterAnimationFolder);
			AnimationClip[] orderedAnimationClips = new AnimationClip[settings.TextLength];

			for (int i = 0; i < settings.TextLength; i++) {
				orderedAnimationClips[i] = null;
				foreach (var animationClip in animationClips) {
					string name;
					if (end) {
						name = settings.CharacterAnimationClipNamePrefix + "End" + i.ToString();
					} else {
						name = settings.CharacterAnimationClipNamePrefix + i.ToString();
					}
					if (animationClip.name == name) {
						orderedAnimationClips[i] = animationClip;
						break;
					}
				}
				if (orderedAnimationClips[i] == null) {
					return null;
				}
			}

			return orderedAnimationClips;
		}
	}
}

#endif
