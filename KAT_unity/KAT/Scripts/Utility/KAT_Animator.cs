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
		public static bool InstallToAnimator(AnimatorController controller, KatSettings settings, string headScalePivotPath = null, Transform avatarRoot = null)
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
			AddParameterIfMissing(controller, settings.ParamSpeed, AnimatorControllerParameterType.Float);   // only the OSC client reads this one

#if VRC_SDK_VRCSDK3
			if (settings.SDK == KatSDK.VRChat) {
				AddParameterIfMissing(controller, settings.ParamStyle, AnimatorControllerParameterType.Float);   // local presentation/style control
				if (settings.InstallAdvancedMenu) {
					AddParameterIfMissing(controller, settings.ParamAdvBillboard, AnimatorControllerParameterType.Int);
					AddParameterIfMissing(controller, settings.ParamAdvGrow, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamAdvBox, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamAdvShrink, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamAdvBold, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamAdvSize, AnimatorControllerParameterType.Float);
				}
				if (settings.InstallBoardControls) {
					AddParameterIfMissing(controller, settings.ParamBoardMode, AnimatorControllerParameterType.Int);
				}
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
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedSpace, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedPageUp, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPressedPageDown, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardMode, AnimatorControllerParameterType.Int);
					AddParameterIfMissing(controller, settings.ParamKeyboardOdd, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamKeyboardLastWide, AnimatorControllerParameterType.Bool);
					AddParameterIfMissing(controller, settings.ParamKeyboardPageValue, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardPageNorm, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardKeyDown, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardKeyCode, AnimatorControllerParameterType.Float);
					AddParameterIfMissing(controller, settings.ParamKeyboardKeyHighlight, AnimatorControllerParameterType.Float);

					for (int i = 0; i < settings.KeyboardKeysCount; i++) {
						AddParameterIfMissing(controller, settings.ParamKeyboardPressed + i.ToString(), AnimatorControllerParameterType.Float);
					}
				}

				controller.AddLayer(CreateToggleLayer(controller, settings, animations));

			if (settings.InstallKeyboard) {
				controller.AddLayer(CreateKeyboardKeyScanLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardProximityLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardShiftLayer(controller, settings, animations));
				controller.AddLayer(CreateKeyboardHighlightLayer(controller, settings, animations));
				int keyboardAtlasPages;
				KatPagedBuilder.DetectAtlasPageFormat(out keyboardAtlasPages);
				int keyboardPageCount = KatKeyboardPaging.KeyboardPageCount(keyboardAtlasPages);
				if (keyboardPageCount > 1) {
					controller.AddLayer(CreateKeyboardPageLayer(controller, settings, keyboardPageCount));
					AnimatorControllerLayer legendLayer = CreateKeyboardLegendLayer(controller, settings, keyboardPageCount);
					if (legendLayer != null) controller.AddLayer(legendLayer);
				}
			}
#endif

			controller.AddLayer(CreateAnchorLayer(controller, settings, animations));

#if VRC_SDK_VRCSDK3
			if (settings.InstallKeyboard && settings.SDK == KatSDK.VRChat) {
				AnimatorControllerLayer keyboardTextLayer = CreateKeyboardTextSourceLayer(controller, settings, animations, avatarRoot);
				if (keyboardTextLayer != null) controller.AddLayer(keyboardTextLayer);
			}
#endif

#if VRC_SDK_VRCSDK3
			if (settings.SDK == KatSDK.VRChat) {
				controller.AddLayer(CreateStyleLayer(controller, settings, animations));
				if (animations.headScaleClean != null) {
					controller.AddLayer(CreateHeadScaleLayer(controller, settings, animations));
				}
				// These go after the anchor and style layers on purpose: when two layers write the same property, the later one wins.
				if (settings.InstallAdvancedMenu) {
					KatAdvancedClips advanced = KatPagedClips.GetAdvancedClips(settings);
					controller.AddLayer(CreateAdvancedIntLayer(controller, settings, settings.ParamAdvBillboard,
						new AnimationClip[] { advanced.billboardAuto, advanced.billboardAlways, advanced.billboardNever }));
					controller.AddLayer(CreateAdvancedBoolLayer(controller, settings, settings.ParamAdvGrow, advanced.growOff, advanced.growOn, true));
					controller.AddLayer(CreateAdvancedBoolLayer(controller, settings, settings.ParamAdvBox, advanced.boxOff, advanced.boxOn, true));
					controller.AddLayer(CreateAdvancedBoolLayer(controller, settings, settings.ParamAdvShrink, advanced.shrinkOff, advanced.shrinkOn, true));
					controller.AddLayer(CreateAdvancedBoolLayer(controller, settings, settings.ParamAdvBold, advanced.boldOff, advanced.boldOn, false));
					controller.AddLayer(CreateAdvancedSizeLayer(controller, settings, advanced));
				}
				if (settings.InstallBoardControls) {
					KatBoardClips board = avatarRoot != null ? KatBoard.BuildClips(avatarRoot, settings) : null;
					if (board != null) {
						controller.AddLayer(CreateBoardLayer(controller, settings, board));
					} else {
						Debug.LogWarning("KAT board controls: the board layer was not added (see the message above). The Board menu and its synced parameter are installed but do nothing.");
					}
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
					layer.name == KeyboardTextLayerName ||
					layer.name == settings.ParamStyle ||
					layer.name == settings.ParamStyle + "_HeadScale" ||
					layer.name.StartsWith(settings.ParamAdvPrefix) ||
					layer.name.StartsWith(settings.ParamBoardPrefix)
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
					parameter.name == settings.ParamStyle ||
					parameter.name.StartsWith(settings.ParamAdvPrefix) ||
					parameter.name.StartsWith(settings.ParamBoardPrefix)
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

		private const string KeyboardTextLayerName = "KAT_TextOnKeyboard";   // deliberately not a KAT_Keyboard* name: it must stay write defaults off, see below

		// The Text constraint has one source per anchor and the keyboard's attachment point is the LAST one: index 4 with the head anchors, 1 without. The keyboard clips
		// (KAT_Keyboard_Enable and friends) were made for the old two-source layout and set sources 0 and 1, so with the head anchors they pulled the text towards the
		// head-left anchor and the keyboard source was never given any weight; the anchor layer overrides them anyway. This layer goes after the anchor layer, finds the
		// keyboard source by name and, while the keyboard is on, puts all the weight on it. It leaves the other weights alone when the keyboard is off (write defaults
		// off on purpose, even in WD-on builds), so the anchor layer's weights stand.
		private static AnimatorControllerLayer CreateKeyboardTextSourceLayer(AnimatorController controller, KatSettings settings, KatAnimations animations, Transform avatarRoot)
		{
			Transform constraintTransform = avatarRoot != null ? avatarRoot.Find(KatPagedClips.ConstraintPath) : null;
			UnityEngine.Animations.ParentConstraint parentConstraint = constraintTransform != null ? constraintTransform.GetComponent<UnityEngine.Animations.ParentConstraint>() : null;
			if (parentConstraint == null) return null;
			int keyboardSource = -1;
			for (int i = 0; i < parentConstraint.sourceCount; i++) {
				Transform source = parentConstraint.GetSource(i).sourceTransform;
				if (source != null && source.name == "KeyboardText_AttachmentPoint") keyboardSource = i;
			}
			if (keyboardSource < 0) {
				Debug.LogWarning("KAT keyboard: the text constraint has no keyboard source, so the text won't move onto the keyboard.");
				return null;
			}

			System.Type constraintType = typeof(UnityEngine.Animations.ParentConstraint);
			AnimationClip clipOff = KatPagedClips.LoadOrCreateClip(settings, "KeyboardTextOff");
			AnimationClip clipOn = KatPagedClips.LoadOrCreateClip(settings, "KeyboardTextOn");
			for (int i = 0; i <= keyboardSource; i++) {
				EditorCurveBinding binding = EditorCurveBinding.FloatCurve(KatPagedClips.ConstraintPath, constraintType, "m_Sources.Array.data[" + i.ToString() + "].weight");
				if (i == keyboardSource) AnimationUtility.SetEditorCurve(clipOff, binding, AnimationCurve.Constant(0f, 1f / 60f, 0f));
				AnimationUtility.SetEditorCurve(clipOn, binding, AnimationCurve.Constant(0f, 1f / 60f, i == keyboardSource ? 1f : 0f));
			}
			EditorUtility.SetDirty(clipOff);
			EditorUtility.SetDirty(clipOn);

			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = KeyboardTextLayerName;
			layer.defaultWeight = 1f;
			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			AnimatorState stateOff = CreateState(stateMachine, "Keyboard Off", new Vector3(300f, 200f, 0f), clipOff);
			AnimatorState stateOn = CreateState(stateMachine, "Keyboard On", new Vector3(300f, 300f, 0f), clipOn);
			CreateTransition(stateOff, stateOn).AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
			CreateTransition(stateOn, stateOff).AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);
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

		// Advanced menu, on/off toggle: two states, and each one holds a clip that sets the value outright. That way it works the
		// same with write defaults on or off, and never depends on what some other layer did. It only touches properties that no
		// other KAT layer animates.
		private static AnimatorControllerLayer CreateAdvancedBoolLayer(AnimatorController controller, KatSettings settings, string parameter,
			AnimationClip offClip, AnimationClip onClip, bool defaultOn)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = parameter;
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			// The first state added becomes the default state, so the one that matches the shader's default goes first.
			AnimatorState first = CreateState(stateMachine, defaultOn ? "On" : "Off", new Vector3(300f, 200f, 0f), defaultOn ? onClip : offClip);
			AnimatorState second = CreateState(stateMachine, defaultOn ? "Off" : "On", new Vector3(300f, 300f, 0f), defaultOn ? offClip : onClip);
			AnimatorState onState = defaultOn ? first : second;
			AnimatorState offState = defaultOn ? second : first;

			AnimatorStateTransition toOn = CreateTransition(offState, onState);
			toOn.AddCondition(AnimatorConditionMode.If, 0, parameter);
			AnimatorStateTransition toOff = CreateTransition(onState, offState);
			toOff.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
			return layer;
		}

		// Advanced menu, pick-one selector: one state per value (0, 1, 2...), each with its own clip.
		private static AnimatorControllerLayer CreateAdvancedIntLayer(AnimatorController controller, KatSettings settings, string parameter, AnimationClip[] clipsByValue)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = parameter;
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			for (int value = 0; value < clipsByValue.Length; value++) {
				AnimatorState state = CreateState(stateMachine, "Value " + value.ToString(), new Vector3(300f, 200f + 100f * value, 0f), clipsByValue[value]);
				// From Any State, so every value can be reached from anywhere. The first state added (value 0) is the default.
				AnimatorStateTransition transition = CreateTransition(stateMachine, state);
				transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
			}
			return layer;
		}

		// Advanced menu, board size radial: a 1D blend tree over KAT_Adv_Size (0 to 1) between three scale clips. The head-scale layer
		// animates a different transform (the head pivot), so the two never write the same property.
		private static AnimatorControllerLayer CreateAdvancedSizeLayer(AnimatorController controller, KatSettings settings, KatAdvancedClips advanced)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = settings.ParamAdvSize;
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			BlendTree blendTree = new BlendTree();
			blendTree.name = "KAT Board Size BlendTree";
			blendTree.blendParameter = settings.ParamAdvSize;
			blendTree.blendType = BlendTreeType.Simple1D;
			blendTree.useAutomaticThresholds = false;
			blendTree.AddChild(advanced.sizeSmall, 0.0f);
			blendTree.AddChild(advanced.sizeDefault, 0.5f);
			blendTree.AddChild(advanced.sizeLarge, 1.0f);
			AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));

			CreateState(stateMachine, "Board Size", new Vector3(300f, 200f, 0f), blendTree);
			return layer;
		}

		// Board controls: one state per KAT_Board_Mode value. The transitions are spelled out one by one instead of using Any State,
		// because an Any State transition would fire again every frame. Switching to "drop and follow" always goes through a short
		// frozen prep state first: the VRC constraint only rebakes its offsets when it un-freezes, and that's what lets the board stay
		// where you let go of it while still following you.
		private static AnimatorControllerLayer CreateBoardLayer(AnimatorController controller, KatSettings settings, KatBoardClips clips)
		{
			AnimatorControllerLayer layer = new AnimatorControllerLayer();
			layer.name = settings.ParamBoardMode;
			layer.defaultWeight = 1f;

			AnimatorStateMachine stateMachine = new AnimatorStateMachine();
			layer.stateMachine = stateMachine;
			stateMachine.name = layer.name;
			AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

			string parameter = settings.ParamBoardMode;
			// Dock is created first, so it's the default state. Frozen states come in three kinds, by where the board was when it was frozen, because Freeze To World
			// holds the pose of the frame it turns on in and the weights must not change in that frame (see KatBoard.BuildClips).
			AnimatorState dock = CreateState(stateMachine, "Dock", new Vector3(300f, 200f, 0f), clips.dock);
			AnimatorState holdL = CreateState(stateMachine, "Hold Left", new Vector3(300f, 300f, 0f), clips.holdLeft);
			AnimatorState holdR = CreateState(stateMachine, "Hold Right", new Vector3(300f, 400f, 0f), clips.holdRight);
			AnimatorState world = CreateState(stateMachine, "Drop World", new Vector3(300f, 500f, 0f), clips.world);
			AnimatorState worldL = CreateState(stateMachine, "Drop World (from left hand)", new Vector3(600f, 500f, 0f), clips.worldLeft);
			AnimatorState worldR = CreateState(stateMachine, "Drop World (from right hand)", new Vector3(900f, 500f, 0f), clips.worldRight);
			AnimatorState follow = CreateState(stateMachine, "Drop Follow", new Vector3(300f, 600f, 0f), clips.follow);
			AnimatorState prep = CreateState(stateMachine, "Follow Prep (frozen)", new Vector3(600f, 600f, 0f), clips.prep);
			AnimatorState prepL = CreateState(stateMachine, "Follow Prep (frozen, from left hand)", new Vector3(600f, 650f, 0f), clips.prepLeft);
			AnimatorState prepR = CreateState(stateMachine, "Follow Prep (frozen, from right hand)", new Vector3(600f, 700f, 0f), clips.prepRight);
			AnimatorState unfreeze = CreateState(stateMachine, "Unfreeze", new Vector3(900f, 300f, 0f), clips.unfreeze);
			prep.speed = 1f;   // the preps run for as long as their clip, then move on to Drop Follow
			prepL.speed = 1f;
			prepR.speed = 1f;
			unfreeze.speed = 1f;   // one frame

			void Go(AnimatorState src, int mode, AnimatorState dst)
			{
				CreateTransition(src, dst).AddCondition(AnimatorConditionMode.Equals, mode, parameter);
			}

			// Not frozen: straight to where it was asked
			Go(dock, KatSettings.BoardModeHoldLeft, holdL); Go(dock, KatSettings.BoardModeHoldRight, holdR);
			Go(dock, KatSettings.BoardModeWorld, world); Go(dock, KatSettings.BoardModeFollow, prep);
			Go(holdL, KatSettings.BoardModeDock, dock); Go(holdL, KatSettings.BoardModeHoldRight, holdR);
			Go(holdL, KatSettings.BoardModeWorld, worldL); Go(holdL, KatSettings.BoardModeFollow, prepL);
			Go(holdR, KatSettings.BoardModeDock, dock); Go(holdR, KatSettings.BoardModeHoldLeft, holdL);
			Go(holdR, KatSettings.BoardModeWorld, worldR); Go(holdR, KatSettings.BoardModeFollow, prepR);
			Go(follow, KatSettings.BoardModeDock, dock); Go(follow, KatSettings.BoardModeHoldLeft, holdL);
			Go(follow, KatSettings.BoardModeHoldRight, holdR); Go(follow, KatSettings.BoardModeWorld, world);

			// Frozen: to Follow it just goes on being frozen (the prep un-freezes into Follow); to anything that isn't frozen it un-freezes first
			foreach (AnimatorState frozen in new[] { world, worldL, worldR }) {
				Go(frozen, KatSettings.BoardModeDock, unfreeze); Go(frozen, KatSettings.BoardModeHoldLeft, unfreeze);
				Go(frozen, KatSettings.BoardModeHoldRight, unfreeze); Go(frozen, KatSettings.BoardModeFollow, prep);
			}
			foreach (AnimatorState frozen in new[] { prep, prepL, prepR }) {
				Go(frozen, KatSettings.BoardModeDock, unfreeze); Go(frozen, KatSettings.BoardModeHoldLeft, unfreeze);
				Go(frozen, KatSettings.BoardModeHoldRight, unfreeze); Go(frozen, KatSettings.BoardModeWorld, world);
				AnimatorStateTransition finish = CreateTransition(frozen, follow);   // if the selection is still Follow, the prep finishes into Drop Follow
				finish.hasExitTime = true;
				finish.exitTime = 1f;
				finish.duration = 0f;
				finish.AddCondition(AnimatorConditionMode.Equals, KatSettings.BoardModeFollow, parameter);
			}

			// Un-frozen with nothing but the follow source weighted, one frame, then wherever it was going
			AnimatorState[] targets = { dock, holdL, holdR, world, prep };   // indexed by mode
			for (int to = 0; to < targets.Length; to++) {
				AnimatorStateTransition leave = CreateTransition(unfreeze, targets[to]);
				leave.hasExitTime = true;
				leave.exitTime = 1f;
				leave.duration = 0f;
				leave.AddCondition(AnimatorConditionMode.Equals, to, parameter);
			}
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
				// Only the keyboard's own pointers (101..228) use the hold value. Without this, OSC text could never write the glyph that shares the value.
				transitionDisabled2.AddCondition(AnimatorConditionMode.Greater, settings.PointerAltSyncOffset, settings.ParamTextPointer);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Less, settings.PointerAltSyncOffset + settings.TextLength + 1, settings.ParamTextPointer);

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

			bool keyboardAlt = settings.InstallKeyboard && settings.PointerAltSyncOffset != 0;
			if (keyboardAlt) {
				// The keyboard's Backspace puts a "hold" value on the page channel, meaning: don't touch this slot's page (same idea as the character layers)
				AnimatorStateTransition transitionDisabled2 = CreateTransition(stateMachine, stateDisabled);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Greater, settings.KeyboardPageHold - 0.005f, syncParam);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Less, settings.KeyboardPageHold + 0.005f, syncParam);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Greater, settings.PointerAltSyncOffset, settings.ParamTextPointer);
				transitionDisabled2.AddCondition(AnimatorConditionMode.Less, settings.PointerAltSyncOffset + settings.TextLength + 1, settings.ParamTextPointer);

				transitionStandby.AddCondition(AnimatorConditionMode.Less, settings.KeyboardPageHold - 0.005f, syncParam);
				AnimatorStateTransition transitionStandby2 = CreateTransition(stateDisabled, stateStandby);
				transitionStandby2.AddCondition(AnimatorConditionMode.NotEqual, 0, settings.ParamTextPointer);
				transitionStandby2.AddCondition(AnimatorConditionMode.Greater, settings.KeyboardPageHold + 0.005f, syncParam);
			}

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

				// The in-game keyboard writes one slot at a time, and the page of that slot lives in this cell (a pair of slots in format 2)
				if (keyboardAlt) {
					bool format3Cells = settings.PageFormat == KatSettings.PageFormat3;
					int firstSlot = format3Cells ? pairIndex : pairIndex * 2;
					int slotCount = format3Cells ? 1 : 2;
					for (int k = 0; k < slotCount; k++) {
						if (firstSlot + k >= settings.TextLength) continue;
						AnimatorStateTransition transitionAltStart = stateMachine.AddAnyStateTransition(statePointerPage);
						transitionAltStart.duration = 0f;
						transitionAltStart.canTransitionToSelf = false;
						transitionAltStart.AddCondition(AnimatorConditionMode.Equals, settings.PointerAltSyncOffset + firstSlot + k + 1, settings.ParamTextPointer);
					}
				}
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
			private static void KbSet(Driver driver, string name, float value)
			{
				DriverParameter p = new DriverParameter();
				p.name = name;
				p.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
				p.value = value;
				driver.parameters.Add(p);
			}

			private static void KbAdd(Driver driver, string name, float value)
			{
				DriverParameter p = new DriverParameter();
				p.name = name;
				p.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add;
				p.value = value;
				driver.parameters.Add(p);
			}

			private static void KbCopy(Driver driver, string name, string source)
			{
				DriverParameter p = new DriverParameter();
				p.name = name;
				p.source = source;
				p.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy;
				driver.parameters.Add(p);
			}

			// the same value on every character channel / page channel (only the slot the pointer is on takes it)
			private static void KbChars(Driver driver, KatSettings settings, float value)
			{
				for (int j = 0; j < settings.SyncParamsSize; j++) KbSet(driver, settings.ParamTextSyncPrefix + j.ToString(), value);
			}

			private static void KbPagesSet(Driver driver, KatSettings settings, float value)
			{
				for (int j = 0; j < settings.GetPageSyncCount(); j++) KbSet(driver, settings.ParamTextPageSyncPrefix + j.ToString(), value);
			}

			private static void KbPagesCopy(Driver driver, KatSettings settings, string source)
			{
				for (int j = 0; j < settings.GetPageSyncCount(); j++) KbCopy(driver, settings.ParamTextPageSyncPrefix + j.ToString(), source);
			}

			// The 128 glyph keys, folded into three numbers: KeyDown (1 while a key is held), KeyCode (which one) and KeyHighlight (which key to light up). The typing states
			// read KeyCode instead of there being a copy of them for every key.
			//
			// This used to be a Direct blend tree animating those three parameters (animated animator parameters). That cannot work for KeyCode: a parameter driver's Copy
			// reads the parameter's expression value, and an animated animator parameter only changes what the Animator sees (Lyuma's Av3 emulator lists them as separate
			// values). The typing states' conditions saw the key go down, but their Copy of KeyCode into the character channels read a value that was never set, which is
			// why only Space (constants, no Copy) ever typed. So here every key has one small state whose driver Sets the three numbers, and drivers do write the real value.
			// It is the first key held that counts; a second key held at the same time is ignored until the first is let go.
			private static AnimatorControllerLayer CreateKeyboardKeyScanLayer(AnimatorController controller, KatSettings settings, KatAnimations animations)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				layer.name = settings.ParamKeyboardPrefix + "Keys";
				layer.defaultWeight = 1f;
				AnimatorStateMachine stateMachine = new AnimatorStateMachine();
				layer.stateMachine = stateMachine;
				stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

				AnimatorState idle = CreateState(stateMachine, "Idle", new Vector3(300f, 200f, 0f), animations.nothing);
				Driver driverIdle = idle.AddStateMachineBehaviour<Driver>();
				driverIdle.localOnly = true;
				driverIdle.parameters = new List<DriverParameter>();
				KbSet(driverIdle, settings.ParamKeyboardKeyDown, 0f);

				for (int i = 0; i < settings.KeyboardKeysCount; i++) {
					int physicalKey = i < 64 ? i : (i - 64) ^ 32;
					AnimatorState key = CreateState(stateMachine, "Key " + i.ToString(), new Vector3(600f + 250f * (i / 32), 100f + 40f * (i % 32), 0f), animations.nothing);
					Driver driverKey = key.AddStateMachineBehaviour<Driver>();
					driverKey.localOnly = true;
					driverKey.parameters = new List<DriverParameter>();
					KbSet(driverKey, settings.ParamKeyboardKeyCode, (float)i);
					KbSet(driverKey, settings.ParamKeyboardKeyHighlight, ConvertKeyToFloat(physicalKey));
					KbSet(driverKey, settings.ParamKeyboardKeyDown, 1f);

					AnimatorStateTransition press = CreateTransition(idle, key);
					press.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
					press.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressed + i.ToString());

					AnimatorStateTransition release = CreateTransition(key, idle);
					release.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressed + i.ToString());
					AnimatorStateTransition off = CreateTransition(key, idle);
					off.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardPrefix);
				}
				return layer;
			}

			// KeyCode (0..127) into the character channels: a Copy that remaps the range. ConvertKeyToFloat is k/127 for k 0..127 and (k-255)/127 for 128..254,
			// so bank 0 (slots 0..127) is 0..1 and bank 1 (slots 128..255) is -1..0, both linear in the key code.
			private static void KbCharsFromKey(Driver driver, KatSettings settings, int bank)
			{
				for (int j = 0; j < settings.SyncParamsSize; j++) {
					DriverParameter p = new DriverParameter();
					p.name = settings.ParamTextSyncPrefix + j.ToString();
					p.source = settings.ParamKeyboardKeyCode;
					p.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy;
					p.convertRange = true;
					p.sourceMin = 0f;
					p.sourceMax = 127f;
					p.destMin = bank == 0 ? 0f : -1f;
					p.destMax = bank == 0 ? 1f : 0f;
					driver.parameters.Add(p);
				}
			}

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
				KbSet(driverInit, settings.ParamKeyboardOdd, 0f);
				KbSet(driverInit, settings.ParamKeyboardLastWide, 0f);

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
				KbSet(driverFix, settings.ParamKeyboardOdd, 0f);          // the next slot is slot 0 again
				KbSet(driverFix, settings.ParamKeyboardLastWide, 0f);

				// ------------------------------------------------------------------------------------------------------------------
				// Keys. One contact set of 64 per half; parameter KAT_KeyboardPressed{i} is glyph slot i of the keyboard page (i < 64: the top half,
				// shown while Shift is held; i >= 64: the lower half). The physical key is K = i for the top half and (i - 64) ^ 32 for the lower half,
				// and K is what the highlight map and the live legends use. See KatKeyboardPaging / tools/gen_keyboard.py.
				// ------------------------------------------------------------------------------------------------------------------
				int atlasPages;
				KatPagedBuilder.DetectAtlasPageFormat(out atlasPages);
				int kpCount = KatKeyboardPaging.KeyboardPageCount(atlasPages);
				bool paged = kpCount > 1;                                   // more than page 0: wide glyphs, FOLLOW slots, even alignment
				bool format3 = settings.PageFormat == KatSettings.PageFormat3;
				float followValue = ConvertKeyToFloat(KatSettings.KeyboardFollowSlot);

				AnimatorState NewState(string name, float x, float y, out Driver driver)
				{
					AnimatorState state = CreateState(stateMachine, name, new Vector3(x, y, 0f), animations.nothing);
					driver = state.AddStateMachineBehaviour<Driver>();
					driver.localOnly = true;
					driver.parameters = new List<DriverParameter>();
					return state;
				}

				AnimatorStateTransition Press(AnimatorState to, string pressParam)
				{
					AnimatorStateTransition t = CreateTransition(stateStandby, to);
					t.AddCondition(AnimatorConditionMode.Greater, 0.5f, pressParam);
					t.duration = 0.2f;
					t.hasFixedDuration = true;
					return t;
				}

				void Release(AnimatorState source, string pressParam)
				{
					AnimatorStateTransition t = CreateTransition(source, stateStandby);
					t.AddCondition(AnimatorConditionMode.Less, 0.5f, pressParam);
				}

				// Goes on by itself after a fraction of the state's one second clip. States are created with speed 0, so this one has to be given a speed.
				void Next(AnimatorState source, AnimatorState target, float exitTime)
				{
					source.speed = 1f;
					AnimatorStateTransition t = CreateTransition(source, target);
					t.hasExitTime = true;
					t.exitTime = exitTime;
					t.duration = 0f;
				}

				// The glyph keys go through the key scan layer (CreateKeyboardKeyScanLayer): KeyDown says a key is down (the first one held counts; a second one is ignored until it's let go) and KeyCode
				// says which. So there is one state of each kind below, not one per key.
				AnimatorStateTransition PressKey(AnimatorState to)
				{
					AnimatorStateTransition t = CreateTransition(stateStandby, to);
					t.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardKeyDown);
					t.AddCondition(AnimatorConditionMode.Less, 1.5f, settings.ParamKeyboardKeyDown);
					t.duration = 0.2f;
					t.hasFixedDuration = true;
					return t;
				}

				void ReleaseKey(AnimatorState source)
				{
					CreateTransition(source, stateStandby).AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardKeyDown);
				}

				// Wide glyphs: the lead half is typed by the key, the FOLLOW half by this shared state; after that it waits for the key to come up.
				AnimatorState stateFollow = null;
				AnimatorState statePad = null;
				if (paged) {
					Driver driverFollow;
					stateFollow = NewState("WideFollow", 900f, 400f, out driverFollow);
					KbAdd(driverFollow, settings.ParamTextPointer, 1f);
					KbChars(driverFollow, settings, followValue);
					if (format3) KbPagesSet(driverFollow, settings, 0f);        // the FOLLOW slot is page 0 slot 254
					else KbPagesCopy(driverFollow, settings, settings.ParamKeyboardPageValue);   // format 2: the pair stays (wide page, page 0)

					Driver driverPad;
					statePad = NewState("PadToEven", 900f, 300f, out driverPad);
					KbAdd(driverPad, settings.ParamTextPointer, 1f);
					KbChars(driverPad, settings, 0f);
					KbPagesSet(driverPad, settings, 0f);
					KbSet(driverPad, settings.ParamKeyboardOdd, 0f);
					KbSet(driverPad, settings.ParamKeyboardLastWide, 0f);
					Next(statePad, stateStandby, 0.05f);                           // the key is probably still down: StandBy picks it up again, now aligned

					AnimatorState stateWait = CreateState(stateMachine, "WaitRelease", new Vector3(1200f, 400f, 0f), animations.nothing);
					CreateTransition(stateFollow, stateWait).AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardKeyDown);
					ReleaseKey(stateWait);
					Next(stateFollow, stateStandby, 0.2f);                         // nothing is held any more
				}

				if (!paged) {
					Driver d;
					AnimatorState s = NewState("Key", 600f, 100f, out d);
					KbAdd(d, settings.ParamTextPointer, 1f);
					KbCopy(d, settings.ParamKeyboardHighlight, settings.ParamKeyboardKeyHighlight);
					KbCharsFromKey(d, settings, 0);
					KbPagesSet(d, settings, 0f);
					PressKey(s);
					ReleaseKey(s);
				} else {
					// page 0: a narrow glyph. One state per parity so the keyboard knows whether the next slot is even or odd.
					for (int parity = 0; parity < 2; parity++) {
						Driver d;
						AnimatorState s = NewState("Narrow" + (parity == 0 ? "E" : "O"), 600f, 100f + 60f * parity, out d);
						KbAdd(d, settings.ParamTextPointer, 1f);
						KbCopy(d, settings.ParamKeyboardHighlight, settings.ParamKeyboardKeyHighlight);
						KbCharsFromKey(d, settings, 0);
						KbPagesSet(d, settings, 0f);
						KbSet(d, settings.ParamKeyboardOdd, parity == 0 ? 1f : 0f);
						KbSet(d, settings.ParamKeyboardLastWide, 0f);
						AnimatorStateTransition t = PressKey(s);
						t.AddCondition(AnimatorConditionMode.Equals, 0, settings.ParamKeyboardMode);
						t.AddCondition(parity == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, settings.ParamKeyboardOdd);
						ReleaseKey(s);
					}

					// a wide page: slots 0..127 (mode 1) or 128..254 (mode 2). Slots 253 and 254 are never typed: those are key codes 125 and 126 of bank 1.
					for (int bank = 0; bank < 2; bank++) {
						Driver d;
						AnimatorState s = NewState("Wide" + bank, 700f + 150f * bank, 100f, out d);
						KbAdd(d, settings.ParamTextPointer, 1f);
						KbCopy(d, settings.ParamKeyboardHighlight, settings.ParamKeyboardKeyHighlight);
						KbCharsFromKey(d, settings, bank);
						KbPagesCopy(d, settings, settings.ParamKeyboardPageValue);
						KbSet(d, settings.ParamKeyboardLastWide, 1f);
						AnimatorStateTransition t = PressKey(s);
						t.AddCondition(AnimatorConditionMode.Equals, 1 + bank, settings.ParamKeyboardMode);
						t.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardOdd);
						if (bank == 1) t.AddCondition(AnimatorConditionMode.Less, KatSettings.KeyboardHoldSlot - 128 - 0.5f, settings.ParamKeyboardKeyCode);
						Next(s, stateFollow, 0.05f);
					}

					// a wide glyph on an odd slot: put a blank in the odd slot first, then try again
					AnimatorStateTransition pad = PressKey(statePad);
					pad.AddCondition(AnimatorConditionMode.Greater, 0, settings.ParamKeyboardMode);
					pad.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardOdd);
				}

				// Space is always the blank glyph of page 0, whatever page the keyboard is on
				for (int parity = 0; parity < (paged ? 2 : 1); parity++) {
					Driver d;
					AnimatorState s = NewState("Space" + (paged ? (parity == 0 ? "E" : "O") : ""), 300f, 650f + 50f * parity, out d);
					KbAdd(d, settings.ParamTextPointer, 1f);
					KbSet(d, settings.ParamKeyboardHighlight, ConvertKeyToFloat(settings.KeyboardKeySpace));
					KbChars(d, settings, 0f);
					KbPagesSet(d, settings, 0f);
					if (paged) {
						KbSet(d, settings.ParamKeyboardOdd, parity == 0 ? 1f : 0f);
						KbSet(d, settings.ParamKeyboardLastWide, 0f);
					}
					AnimatorStateTransition t = Press(s, settings.ParamKeyboardPressedSpace);
					if (paged) t.AddCondition(parity == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, settings.ParamKeyboardOdd);
					Release(s, settings.ParamKeyboardPressedSpace);
				}

				// Clear
				{
					Driver d;
					AnimatorState s = NewState("KeyClear", 0f, 550f, out d);
					KbSet(d, settings.ParamTextPointer, 255f);
					KbSet(d, settings.ParamKeyboardHighlight, ConvertKeyToFloat(settings.KeyboardKeyClear));
					KbChars(d, settings, 0f);
					KbSet(d, settings.ParamKeyboardOdd, 0f);
					KbSet(d, settings.ParamKeyboardLastWide, 0f);
					Press(s, settings.ParamKeyboardPressedClear);
					Release(s, settings.ParamKeyboardPressedClear);
				}

				// Backspace. It removes one slot, or two if the last thing typed was a wide glyph (the keyboard only remembers that for one glyph; after a
				// Backspace it guesses from the page it is on). The slot's page is left alone while it is erased (the page channels carry a "hold" value).
				{
					Driver dStartN, dEndN;
					AnimatorState startN = NewState("KeyBackspaceStart", 0f, 600f, out dStartN);
					KbSet(dStartN, settings.ParamKeyboardHighlight, ConvertKeyToFloat(settings.KeyboardKeyBackspace));
					KbChars(dStartN, settings, 0f);
					KbPagesSet(dStartN, settings, settings.KeyboardPageHold);
					AnimatorStateTransition downN = Press(startN, settings.ParamKeyboardPressedBackspace);
					if (paged) downN.AddCondition(AnimatorConditionMode.IfNot, 0, settings.ParamKeyboardLastWide);

					int endVariants = paged ? 2 : 1;
					for (int parity = 0; parity < endVariants; parity++) {
						AnimatorState endN = NewState("KeyBackspaceEnd" + (paged ? (parity == 0 ? "E" : "O") : ""), 0f, 650f + 50f * parity, out dEndN);
						KbAdd(dEndN, settings.ParamTextPointer, -1f);
						KbChars(dEndN, settings, settings.KeyboardBackspaceMode);
						KbPagesSet(dEndN, settings, settings.KeyboardPageHold);
						if (paged) {
							KbSet(dEndN, settings.ParamKeyboardOdd, parity == 0 ? 1f : 0f);
							KbCopy(dEndN, settings.ParamKeyboardLastWide, settings.ParamKeyboardMode);
						}
						AnimatorStateTransition up = CreateTransition(startN, endN);
						up.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
						if (paged) up.AddCondition(parity == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, settings.ParamKeyboardOdd);
						up.duration = 0.2f;
						up.hasFixedDuration = true;
						Release(endN, settings.ParamKeyboardPressedBackspace);
					}

					if (paged) {
						Driver dStartW, dMidW, dEndW;
						AnimatorState startW = NewState("KeyBackspaceWideStart", 150f, 600f, out dStartW);
						KbSet(dStartW, settings.ParamKeyboardHighlight, ConvertKeyToFloat(settings.KeyboardKeyBackspace));
						KbChars(dStartW, settings, 0f);                                      // the FOLLOW half
						KbPagesSet(dStartW, settings, settings.KeyboardPageHold);
						AnimatorStateTransition downW = Press(startW, settings.ParamKeyboardPressedBackspace);
						downW.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardLastWide);

						AnimatorState midW = NewState("KeyBackspaceWideMid", 150f, 650f, out dMidW);
						KbAdd(dMidW, settings.ParamTextPointer, -1f);
						KbChars(dMidW, settings, 0f);                                        // the lead half
						KbPagesSet(dMidW, settings, 0f);                                     // and its page goes back to page 0
						AnimatorStateTransition toMid = CreateTransition(startW, midW);
						toMid.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
						toMid.duration = 0.2f;
						toMid.hasFixedDuration = true;

						AnimatorState endW = NewState("KeyBackspaceWideEnd", 150f, 700f, out dEndW);
						KbAdd(dEndW, settings.ParamTextPointer, -1f);
						KbChars(dEndW, settings, settings.KeyboardBackspaceMode);
						KbPagesSet(dEndW, settings, settings.KeyboardPageHold);
						KbCopy(dEndW, settings.ParamKeyboardLastWide, settings.ParamKeyboardMode);
						AnimatorStateTransition toEnd = CreateTransition(midW, endW);
						toEnd.AddCondition(AnimatorConditionMode.If, 0, settings.ParamKeyboardPrefix);
						toEnd.duration = 0.2f;
						toEnd.hasFixedDuration = true;
						Release(endW, settings.ParamKeyboardPressedBackspace);
					}
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

			// The page arrows. The keyboard is on "keyboard page" kp: 0 is page 0 (narrow); after that every wide atlas page is two keyboard pages of 128
			// slots (kp 1 = page 1 slots 0..127, kp 2 = page 1 slots 128..254, kp 3 = page 2 ...). Up goes to the previous one, Down to the next, and both wrap.
			private static AnimatorControllerLayer CreateKeyboardPageLayer(AnimatorController controller, KatSettings settings, int kpCount)
			{
				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				layer.name = settings.ParamKeyboardPrefix + "_Page";
				layer.defaultWeight = 1f;
				AnimatorStateMachine stateMachine = new AnimatorStateMachine();
				layer.stateMachine = stateMachine;
				stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

				bool format3 = settings.PageFormat == KatSettings.PageFormat3;
				AnimatorState[] idle = new AnimatorState[kpCount];
				AnimatorState[] arrivedDown = new AnimatorState[kpCount];
				AnimatorState[] arrivedUp = new AnimatorState[kpCount];

				for (int kp = 0; kp < kpCount; kp++) {
					int atlasPage = kp == 0 ? 0 : (kp - 1) / 2 + 1;
					int mode = kp == 0 ? 0 : 1 + (kp - 1) % 2;
					float pageValue = kp == 0 ? 0f : ConvertKeyToFloat(format3 ? atlasPage : atlasPage * 15);   // format 2: the pair (wide page, page 0)
					float norm = kpCount > 1 ? (float)kp / (kpCount - 1) : 0f;

					idle[kp] = CreateState(stateMachine, "Page" + kp, new Vector3(300f, 100f + 40f * kp, 0f), null);
					Driver driverIdle = idle[kp].AddStateMachineBehaviour<Driver>();
					driverIdle.localOnly = true;
					driverIdle.parameters = new List<DriverParameter>();
					KbSet(driverIdle, settings.ParamKeyboardHighlight, ConvertKeyToFloat(100));

					for (int dir = 0; dir < 2; dir++) {
						AnimatorState state = CreateState(stateMachine, (dir == 0 ? "Down" : "Up") + " to Page" + kp, new Vector3(600f + 300f * dir, 100f + 40f * kp, 0f), null);
						Driver driver = state.AddStateMachineBehaviour<Driver>();
						driver.localOnly = true;
						driver.parameters = new List<DriverParameter>();
						KbSet(driver, settings.ParamKeyboardHighlight, ConvertKeyToFloat(dir == 0 ? settings.KeyboardKeyPageDown : settings.KeyboardKeyPageUp));
						KbSet(driver, settings.ParamKeyboardMode, mode);
						KbSet(driver, settings.ParamKeyboardPageValue, pageValue);
						KbSet(driver, settings.ParamKeyboardPageNorm, norm);
						if (dir == 0) arrivedDown[kp] = state; else arrivedUp[kp] = state;

						AnimatorStateTransition back = CreateTransition(state, idle[kp]);
						back.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedPageUp);
						back.AddCondition(AnimatorConditionMode.Less, 0.5f, settings.ParamKeyboardPressedPageDown);
					}
				}

				for (int kp = 0; kp < kpCount; kp++) {
					AnimatorStateTransition down = CreateTransition(idle[kp], arrivedDown[(kp + 1) % kpCount]);
					down.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedPageDown);
					down.duration = 0.2f;
					down.hasFixedDuration = true;
					AnimatorStateTransition up = CreateTransition(idle[kp], arrivedUp[(kp + kpCount - 1) % kpCount]);
					up.AddCondition(AnimatorConditionMode.Greater, 0.5f, settings.ParamKeyboardPressedPageUp);
					up.duration = 0.2f;
					up.hasFixedDuration = true;
				}
				stateMachine.defaultState = idle[0];
				return layer;
			}

			// Tells the keyboard shader which page to draw on the keys (material._KeyKp), from the page layer's normalised page number.
			private static AnimatorControllerLayer CreateKeyboardLegendLayer(AnimatorController controller, KatSettings settings, int kpCount)
			{
				AnimationClip[] clips = KatKeyboardPaging.BuildLegendClips(settings, kpCount);
				if (clips == null) return null;

				AnimatorControllerLayer layer = new AnimatorControllerLayer();
				layer.name = settings.ParamKeyboardPrefix + "_Legend";
				layer.defaultWeight = 1f;
				AnimatorStateMachine stateMachine = new AnimatorStateMachine();
				layer.stateMachine = stateMachine;
				stateMachine.name = layer.name;
				AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));

				BlendTree blendTree = new BlendTree();
				blendTree.name = "KAT Keyboard Legend BlendTree";
				blendTree.blendParameter = settings.ParamKeyboardPageNorm;
				blendTree.blendType = BlendTreeType.Simple1D;
				blendTree.useAutomaticThresholds = false;
				blendTree.AddChild(clips[0], 0f);
				blendTree.AddChild(clips[1], 1f);
				AssetDatabase.AddObjectToAsset(blendTree, AssetDatabase.GetAssetPath(controller));
				CreateState(stateMachine, "Legend Page", new Vector3(300f, 200f, 0f), blendTree);
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
				name == settings.ParamStyle + "_HeadScale" ||
				name.StartsWith(settings.ParamAdvPrefix) ||
				name.StartsWith(settings.ParamBoardPrefix);
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
