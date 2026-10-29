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

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;

#if VRC_SDK_VRCSDK3
	using VRCAvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
#endif

#if CVR_CCK_EXISTS
	using CVRAvatar = ABI.CCK.Components.CVRAvatar;
#endif

using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatObjectsInstaller
	{
		#if VRC_SDK_VRCSDK3
			public static bool InstallObjectsToAvatar(VRCAvatarDescriptor avatarDescriptor, KatSettings settings)
			{
				Transform transform = avatarDescriptor.gameObject.transform;
				if (!InstallObjectsToAvatar(transform, settings)) {
					return false;
				}

				return InstallPagedMenuToAvatar(avatarDescriptor, settings);
			}

			public static bool InstallPagedMenuToAvatar(VRCAvatarDescriptor avatarDescriptor, KatSettings settings)
			{
				return avatarDescriptor.expressionsMenu == null || KatPagedMenu.InstallToMenu(avatarDescriptor.expressionsMenu, settings);
			}

			public static bool RemoveObjectsFromAvatar(VRCAvatarDescriptor avatarDescriptor, KatSettings settings)
			{
				Transform transform = avatarDescriptor.gameObject.transform;
				bool result = RemoveObjectsFromAvatar(transform, settings);
				result = KatPagedMenu.RemoveFromMenu(avatarDescriptor.expressionsMenu, settings) && result;
				return result;
			}
		#endif

		#if CVR_CCK_EXISTS
			public static bool InstallObjectsToAvatar(CVRAvatar avatarDescriptor, KatSettings settings)
			{
				Transform transform = avatarDescriptor.gameObject.transform;
				return InstallObjectsToAvatar(transform, settings);
			}

			public static bool RemoveObjectsFromAvatar(CVRAvatar avatarDescriptor, KatSettings settings)
			{
				Transform transform = avatarDescriptor.gameObject.transform;
				return RemoveObjectsFromAvatar(transform, settings);
			}
		#endif

		public static bool InstallObjectsToAvatar(Transform avatarRootTransform, KatSettings settings)
		{
			return InstallObjectsToAvatar(avatarRootTransform, settings, out string _);
		}

		public static bool InstallObjectsToAvatar(Transform avatarRootTransform, KatSettings settings, out string headScalePivotPath)
		{
			headScalePivotPath = null;

			Material atlasCheckMaterial = Resources.Load<Material>("KAT_Misc/KAT_Text_Paged");
			if (atlasCheckMaterial != null && !KatPagedBuilder.ValidateAtlasForSettings(settings, atlasCheckMaterial)) return false;

			if (avatarRootTransform.Find("KillFrenzyAvatarText") == null) {
				RemoveStaleAttachmentPoints(avatarRootTransform, settings);
			}

			Dictionary<string, Transform> created = new Dictionary<string, Transform>();

			Material textMaterial = Resources.Load<Material>("KAT_Misc/KAT_Text_Paged");
			if (textMaterial == null) textMaterial = Resources.Load<Material>("KAT_Misc/KAT_Text");
			if (textMaterial != null) textMaterial = PrepareTextMaterial(textMaterial, avatarRootTransform, settings);
			Transform avatarAttachmentTransform = null;
			Vector3 avatarAttachmentOffset = settings.ChestAnchorOffset;
			if (settings.AttachmentPoint != KatAttachmentPoint.None) {
				avatarAttachmentTransform = FindAvatarChest(avatarRootTransform);
				if (avatarAttachmentTransform == null) {
					Debug.LogWarning("Warning: Avatar chest not found. The 0% chest anchor will be unavailable.");
				}
			}

			if (textMaterial == null) {
				Debug.LogError("Error: Material for Text not found.");
				return false;
			}

			GameObject keyboardObject = null;
			if (settings.InstallKeyboard) {
				Object keyboardPrefab = Resources.Load<GameObject>("KAT_Misc/KillFrenzyAvatarKeyboard");
				if (keyboardPrefab == null) {
					Debug.LogError("Error: Prefab for keyboard not found.");
					return false;
				}

				keyboardObject = (GameObject)PrefabUtility.InstantiatePrefab(keyboardPrefab, avatarRootTransform);
				keyboardObject.SetActive(false);

				try {
					Transform avatarChest = FindAvatarChest(avatarRootTransform);

					GameObject attachmentPoint = new GameObject(settings.KeyboardAttachmentPointName);
					attachmentPoint.transform.SetParent(avatarChest);
					attachmentPoint.transform.localPosition = new Vector3(0, 0, 0);
					created[settings.KeyboardAttachmentPointName] = attachmentPoint.transform;

					RotationConstraint constraintAvatar = attachmentPoint.AddComponent<RotationConstraint>();
					constraintAvatar.locked = true;
					constraintAvatar.rotationAxis = Axis.X | Axis.Z;

					ConstraintSource constraintAvatarSource = new ConstraintSource();
					constraintAvatarSource.sourceTransform = keyboardObject.transform;
					constraintAvatarSource.weight = 1f;
					constraintAvatar.AddSource(constraintAvatarSource);
					constraintAvatar.constraintActive = true;

					GameObject attachmentTarget = new GameObject(settings.KeyboardAttachmentPointName + "_Target");
					attachmentTarget.transform.SetParent(attachmentPoint.transform);
					attachmentTarget.transform.localPosition = new Vector3(0.0f, 0.0f, 0.4f);
					created[settings.KeyboardAttachmentPointName + "_Target"] = attachmentTarget.transform;

					Transform keyboardConstraintTransform = FindTransformRecursive(keyboardObject.transform, "ConstraintChild");
					ParentConstraint parentConstraint = keyboardConstraintTransform.gameObject.GetComponent<ParentConstraint>();
					ConstraintSource constraintSource = parentConstraint.GetSource(0);
					constraintSource.sourceTransform = attachmentTarget.transform;
					parentConstraint.SetSource(0, constraintSource);

				} catch {
					Debug.LogWarning("Warning: Could not attach keyboard 'ConstraintChild' to '" + settings.KeyboardAttachmentPointName + "'.");
				}
			}

			GameObject katObject = new GameObject("KillFrenzyAvatarText");
			katObject.transform.SetParent(avatarRootTransform);
			katObject.SetActive(false);

			GameObject constraintObject = new GameObject("Constraint");
			constraintObject.transform.SetParent(katObject.transform);
			constraintObject.transform.localRotation = new Quaternion(0,1,0,0);

			if (avatarAttachmentTransform != null) {
				GameObject attachmentPoint = new GameObject(settings.TextAttachmentPointName);
				attachmentPoint.transform.SetParent(avatarAttachmentTransform);
				attachmentPoint.transform.localPosition = new Vector3(0, 0, 0);
				created[settings.TextAttachmentPointName] = attachmentPoint.transform;

				RotationConstraint constraintAvatar = attachmentPoint.AddComponent<RotationConstraint>();
				constraintAvatar.locked = true;
				constraintAvatar.rotationAxis = Axis.X | Axis.Z;

				ConstraintSource constraintAvatarSource = new ConstraintSource();
				constraintAvatarSource.sourceTransform = katObject.transform;
				constraintAvatarSource.weight = 1f;
				constraintAvatar.AddSource(constraintAvatarSource);
				constraintAvatar.constraintActive = true;

				GameObject attachmentTarget = new GameObject(settings.TextAttachmentPointName + "_Target");
				attachmentTarget.transform.SetParent(attachmentPoint.transform);
				attachmentTarget.transform.localPosition = avatarAttachmentOffset;
				created[settings.TextAttachmentPointName + "_Target"] = attachmentTarget.transform;

				ParentConstraint constraintText = constraintObject.AddComponent<ParentConstraint>();
				constraintText.locked = true;

				ConstraintSource constraintTextSource = new ConstraintSource();
				constraintTextSource.sourceTransform = attachmentTarget.transform;
				constraintTextSource.weight = 1f;
				constraintText.AddSource(constraintTextSource);

				constraintText.translationAxis = Axis.X | Axis.Y | Axis.Z;
				constraintText.rotationAxis = Axis.Y;

				Transform avatarHead = FindAvatarHead(avatarRootTransform);
				if (avatarHead == null) {
					Debug.LogWarning("Warning: Avatar head not found. The head radial positions will be unavailable.");
				} else {
					GameObject headPoint = new GameObject(settings.HeadAttachmentPointName);
					headPoint.transform.SetParent(avatarHead);
					headPoint.transform.localPosition = Vector3.zero;
					created[settings.HeadAttachmentPointName] = headPoint.transform;

					GameObject headScalePivot = new GameObject(settings.HeadAttachmentPointName + "_ScalePivot");
					headScalePivot.transform.SetParent(headPoint.transform);
					headScalePivot.transform.localPosition = Vector3.zero;
					headScalePivot.transform.localScale = Vector3.one;
					created[settings.HeadAttachmentPointName + "_ScalePivot"] = headScalePivot.transform;
					headScalePivotPath = AnimationUtility.CalculateTransformPath(headScalePivot.transform, avatarRootTransform);

					GameObject headLeftTarget = new GameObject(settings.HeadAttachmentPointName + "_Target");
					headLeftTarget.transform.SetParent(headScalePivot.transform);
					headLeftTarget.transform.localPosition = settings.HeadAnchorOffset;
					created[settings.HeadAttachmentPointName + "_Target"] = headLeftTarget.transform;

					ConstraintSource headLeftSource = new ConstraintSource();
					headLeftSource.sourceTransform = headLeftTarget.transform;
					headLeftSource.weight = 0f;
					constraintText.AddSource(headLeftSource); // source 1

					GameObject headRightTarget = new GameObject(settings.HeadAttachmentPointName + "_RightTarget");
					headRightTarget.transform.SetParent(headScalePivot.transform);
					headRightTarget.transform.localPosition = settings.RightHeadAnchorOffset;
					created[settings.HeadAttachmentPointName + "_RightTarget"] = headRightTarget.transform;

					ConstraintSource headRightSource = new ConstraintSource();
					headRightSource.sourceTransform = headRightTarget.transform;
					headRightSource.weight = 0f;
					constraintText.AddSource(headRightSource); // source 2

					GameObject headTopPivot = new GameObject(settings.HeadTopPivotName);
					headTopPivot.transform.SetParent(headScalePivot.transform);
					headTopPivot.transform.localPosition = settings.TopHeadAnchorOffset;
					headTopPivot.transform.localScale = Vector3.one;
					created[settings.HeadTopPivotName] = headTopPivot.transform;

					ConstraintSource headTopSource = new ConstraintSource();
					headTopSource.sourceTransform = headTopPivot.transform;
					headTopSource.weight = 0f;
					constraintText.AddSource(headTopSource); // source 3
				}

				if (settings.InstallKeyboard) {
					Transform avatarChest = FindAvatarChest(avatarRootTransform);

					if (avatarChest == null) {
						Debug.LogWarning("Warning: Avatar chest not found.");
					} else {
						Transform constraintKeyboardAttachment = FindTransformRecursive(keyboardObject.transform, "KeyboardText_AttachmentPoint");
						if (constraintKeyboardAttachment == null) {
							Debug.Log(keyboardObject);
							Debug.LogWarning("Warning: Prefab for keyboard does not contain 'KeyboardText_AttachmentPoint'.");
						} else {
							ConstraintSource constraintSource2 = new ConstraintSource();
							constraintSource2.sourceTransform = constraintKeyboardAttachment;
							constraintSource2.weight = 0f;
							constraintText.AddSource(constraintSource2);
						}
					}
				}

				constraintText.constraintActive = true;
			}

			GameObject textObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			textObject.name = "Text";
			textObject.transform.SetParent(constraintObject.transform);
			textObject.transform.localScale = new Vector3(0.5f, 1f / 6f, 1.0f);
			created["Text"] = textObject.transform;

			GameObject.DestroyImmediate(textObject.GetComponent<MeshCollider>());

			if (settings.InstallBoardControls) {
				KatBoard.InstallConstraint(avatarRootTransform, textObject.transform, settings);
			}

			MeshRenderer textRenderer = textObject.GetComponent<MeshRenderer>();
			textRenderer.material = textMaterial;

			if (settings.Config != null && settings.Config.Transforms.Count > 0) {
				settings.Config.ApplyTransforms(created);
			}

			// EditorSceneManager.MarkSceneDirty(avatarDescriptor.gameObject.scene);
			EditorSceneManager.MarkSceneDirty(avatarRootTransform.gameObject.scene);
			Debug.Log("Installation of objects to avatar completed.");
			return true;
		}

		public static bool RemoveObjectsFromAvatar(Transform avatarRootTransform, KatSettings settings)
		{
			Transform katObjectTransform = avatarRootTransform.transform.Find("KillFrenzyAvatarText");
			if (katObjectTransform) {
				try {
					Transform katConstraintTransform = katObjectTransform.transform.Find("Constraint");
					ParentConstraint constraint = katConstraintTransform.GetComponent<ParentConstraint>();
					ConstraintSource constraintSource = constraint.GetSource(0);
					if (constraintSource.sourceTransform.parent.gameObject.name == settings.TextAttachmentPointName) {
						GameObject.DestroyImmediate(constraintSource.sourceTransform.parent.gameObject);
					}
				} catch {}

				try {
					Transform katConstraintTransform2 = katObjectTransform.transform.Find("Constraint");
					ParentConstraint constraint2 = katConstraintTransform2.GetComponent<ParentConstraint>();
					if (constraint2.sourceCount > 1) {
						Transform node = constraint2.GetSource(1).sourceTransform;
						while (node != null && node.name != settings.HeadAttachmentPointName) {
							node = node.parent;
						}
						if (node != null) {
							GameObject.DestroyImmediate(node.gameObject);
						}
					}
				} catch {}

				// Remove parent object
				try {
					GameObject.DestroyImmediate(katObjectTransform.gameObject);
				} catch {
					Debug.LogWarning("Warning: Unable to destroy the KillFrenzyAvatarText object.");
					return false;
				}
			}

			Transform keyboardObjectTransform = avatarRootTransform.transform.Find("KillFrenzyAvatarKeyboard");
			if (keyboardObjectTransform) {
				// Remove constraint target
				try {
					Transform keyboardConstraintTransform = FindTransformRecursive(keyboardObjectTransform, "ConstraintChild");
					ParentConstraint constraint = keyboardConstraintTransform.GetComponent<ParentConstraint>();
					ConstraintSource constraintSource = constraint.GetSource(0);
					if (constraintSource.sourceTransform.parent.gameObject.name == settings.KeyboardAttachmentPointName) {
						GameObject.DestroyImmediate(constraintSource.sourceTransform.parent.gameObject);
					}
				} catch {}

				// Remove parent object
				try {
					GameObject.DestroyImmediate(keyboardObjectTransform.gameObject);
				} catch {
					Debug.LogWarning("Warning: Unable to destroy the KillFrenzyAvatarKeyboard object.");
					EditorSceneManager.MarkSceneDirty(avatarRootTransform.gameObject.scene);
					return false;
				}
			}

			RemoveStaleAttachmentPoints(avatarRootTransform, settings);
			KatBoard.RemoveTargets(avatarRootTransform, settings);   // the hand and follow targets the board controls added

			EditorSceneManager.MarkSceneDirty(avatarRootTransform.gameObject.scene);
			Debug.Log("Removal of objects from avatar completed.");
			return true;
		}

		private static int RemoveStaleAttachmentPoints(Transform avatarRootTransform, KatSettings settings)
		{
			string[] names = new string[] {
				settings.HeadAttachmentPointName,
				settings.HeadTopPivotName,
				settings.TextAttachmentPointName,
				settings.KeyboardAttachmentPointName
			};

			int removed = 0;
			foreach (Transform t in avatarRootTransform.GetComponentsInChildren<Transform>(true)) {
				if (t == null) continue; // already destroyed together with its parent
				if (System.Array.IndexOf(names, t.name) < 0) continue;
				try {
					GameObject.DestroyImmediate(t.gameObject);
					removed++;
				} catch (System.Exception e) {
					Debug.LogWarning("Warning: Could not remove leftover '" + t.name + "': " + e.Message);
				}
			}
			if (removed > 0) {
				Debug.Log("Removed " + removed + " leftover KAT attachment point(s).");
			}
			return removed;
		}

		private static Material PrepareTextMaterial(Material source, Transform avatarRootTransform, KatSettings settings)
		{
			KatConfig config = settings.Config;
			bool hasConfigValues = config != null && config.HasMaterialValues;
			bool isPagedShader = source.shader != null && source.shader.name == KatDebugFields.ShaderName;
			bool format3 = settings.PageFormat == KatSettings.PageFormat3 && isPagedShader;
			if (!hasConfigValues && !format3) {
				KatDebugFields.Reset(source);
				if (isPagedShader && source.IsKeywordEnabled(KatSettings.PageFormat3Keyword)) source.DisableKeyword(KatSettings.PageFormat3Keyword);
				return source;
			}

			KatAvatarInstaller.CreateOutputFolder(settings);
			string folder = "Assets/" + settings.GeneratedOutputFolderName;

			string safeName = avatarRootTransform.name;
			foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) {
				safeName = safeName.Replace(bad, '_');
			}
			string assetName = "KAT_Text_" + safeName;
			string path = folder + "/" + assetName + ".mat";

			Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (copy == null) {
				copy = new Material(source);
				AssetDatabase.CreateAsset(copy, path);
			} else {
				EditorUtility.CopySerialized(source, copy);
				copy.name = assetName;
			}

			if (hasConfigValues) config.ApplyToMaterial(copy);
			else KatDebugFields.Reset(copy);
			if (isPagedShader) {
				if (format3) copy.EnableKeyword(KatSettings.PageFormat3Keyword);
				else copy.DisableKeyword(KatSettings.PageFormat3Keyword);
			}
			EditorUtility.SetDirty(copy);
			AssetDatabase.SaveAssets();
			Debug.Log("KAT: text material written to " + path + (hasConfigValues ? " (with your config defaults)" : "") + (format3 ? " (page format 3)" : ""));
			return copy;
		}

		private static Transform FindAvatarChest(Transform transform)
		{
			Transform chest = null;

			// Get chest from animator bone
			Animator animator = transform.GetComponent<Animator>();
			if (animator != null) {
				chest = animator.GetBoneTransform(HumanBodyBones.Chest);
				if (chest != null) {
					return chest;
				}
			}

			// Otherwise check bone transform names to find the chest
			chest = FindTransformRecursive(transform, "chest");
			if (chest != null) {
				Transform neck = FindTransformRecursive(transform, "neck");
				if (neck != null) {
					return chest;
				}
			}

			return null;
		}

		private static Transform FindAvatarHead(Transform transform)
		{
			Transform head = null;

			// Get neck from animator bone
			Animator animator = transform.GetComponent<Animator>();
			if (animator != null) {
				head = animator.GetBoneTransform(HumanBodyBones.Head);
				if (head != null) {
					return head;
				}
			}

			// Otherwise check bone transform names to find the head
			Transform neck = FindTransformRecursive(transform, "neck");
			if (neck != null) {
				head = FindTransformRecursive(transform, "head");
				return head;
			}

			return null;
		}

		private static Transform FindTransformRecursive(Transform transform, string name)
		{
			foreach (Transform child in transform) {
				if (child.name.StartsWith("KAT_", System.StringComparison.Ordinal)) continue;
				if (child.name.ToLower().Contains(name.ToLower())) {
					return child;
				} else {
					Transform found = FindTransformRecursive(child, name);
					if (found != null) {
						return found;
					}
				}
			}
			return null;
		}
	}
}

#endif
