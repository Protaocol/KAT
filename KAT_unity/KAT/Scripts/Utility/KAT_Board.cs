/*
Board controls for KillFrenzyAvatarText: hold the text board in your hand, drop it in the world, or drop it so it follows you.

I wrote this from scratch for this fork. It isn't based on any other avatar text board project.

How it works
  A VRChat Parent Constraint (VRCParentConstraint, SDK 3.5 or newer) sits on the "Text" object. While the board is docked the
  constraint is switched off, so the normal head and chest anchors do exactly what they always did. One synced int,
  KAT_Board_Mode, drives it from the expression menu:

    0  Dock          constraint off; the board follows the usual anchor
    1  Hold left     constraint on, pointed at a spot just in front of the left hand
    2  Hold right    same, for the right hand
    3  Drop world    constraint on with "Freeze To World", so the board stays right where it was when you let go
    4  Drop follow   constraint on, pointed at the avatar root, and its offsets get rebaked when it un-freezes, so the board stays
                     where you let go of it but travels with you. It passes through a short frozen "prep" state first, because
                     the rebake only happens when the constraint goes from frozen to not frozen.

  Hands: the hand sources are NOT the bones' own targets any more. Each hand gets an anchor object under the avatar root, pulled to a
  point past the knuckles by a VRCPositionConstraint. The anchor therefore has the hand's position but the avatar root's facing,
  so the board keeps its heading instead of picking up whatever twist the wrist has (the old hand targets turned with the hand
  bone, and the board only takes the Y turn of its source). Follow: the follow source floats in front of the chest, so if a
  constraint rebake ever doesn't happen the board still lands somewhere you can see instead of at your feet.

  I couldn't check the exact VRC constraint API from outside Unity, so the types are found by name (reflection) and the animation
  property names are looked up with AnimationUtility.GetAnimatableBindings. If your SDK names something differently, the installer
  tells you what it found instead of failing to compile and taking the whole tool down with it.
*/

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public class KatBoardClips
	{
		public AnimationClip dock, holdLeft, holdRight, world, prep, follow;
		public AnimationClip worldLeft, worldRight, prepLeft, prepRight, unfreeze;   // see CreateBoardLayer: freezing keeps the weights of the mode it came from
	}

	public static class KatBoard
	{
		private const string ConstraintTypeName = "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint";
		private const string PositionConstraintTypeName = "VRC.SDK3.Dynamics.Constraint.Components.VRCPositionConstraint";
		private const string SourceTypeName = "VRC.Dynamics.VRCConstraintSource";
				private const string TextRelativePath = "KillFrenzyAvatarText/Constraint/Text";
		private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

		// -------------------------------------------------------------------------------------------- availability

		private static Type FindType(string fullName)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				Type type = null;
				try { type = assembly.GetType(fullName, false); } catch (Exception) { }
				if (type != null) return type;
			}
			return null;
		}

		public static Type ConstraintType()
		{
			return FindType(ConstraintTypeName);
		}

		// True if this project's VRChat SDK has VRC constraints (SDK 3.5 or newer, Unity 2022).
		public static bool IsAvailable()
		{
			return ConstraintType() != null && FindSourceType() != null;
		}

		// -------------------------------------------------------------------------------------------- reflection helpers

		private static bool SetMember(object target, string name, object value, List<string> missing)
		{
			Type type = target.GetType();
			PropertyInfo property = type.GetProperty(name, Any);
			if (property != null && property.CanWrite) {
				property.SetValue(target, Convert.ChangeType(value, property.PropertyType));
				return true;
			}
			FieldInfo field = type.GetField(name, Any);
			if (field != null) {
				field.SetValue(target, Convert.ChangeType(value, field.FieldType));
				return true;
			}
			if (missing != null) missing.Add(name);
			return false;
		}

		private static bool SetObjectMember(object target, string name, object value)
		{
			Type type = target.GetType();
			PropertyInfo property = type.GetProperty(name, Any);
			if (property != null && property.CanWrite) { property.SetValue(target, value); return true; }
			FieldInfo field = type.GetField(name, Any);
			if (field != null) { field.SetValue(target, value); return true; }
			return false;
		}

		private static object GetMember(object target, string name)
		{
			Type type = target.GetType();
			PropertyInfo property = type.GetProperty(name, Any);
			if (property != null) return property.GetValue(target);
			FieldInfo field = type.GetField(name, Any);
			return field != null ? field.GetValue(target) : null;
		}

		// Calls a public method with no arguments, if there is one. Returns whether it was there.
		private static bool InvokeMethod(object target, string name)
		{
			MethodInfo method = target.GetType().GetMethod(name, Any, null, Type.EmptyTypes, null);
			if (method == null) return false;
			method.Invoke(target, null);
			return true;
		}

		private static Type FindSourceType()
		{
			Type type = FindType(SourceTypeName);
			if (type != null) return type;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				Type[] types;
				try { types = assembly.GetTypes(); } catch (Exception) { continue; }
				foreach (Type candidate in types) {
					if (candidate.Name == "VRCConstraintSource") return candidate;
				}
			}
			return null;
		}

		// The docs say ApplyModifiedChanges(); a VRChat bug report talks about ApplyConfigurationChanges(). Whichever of them this SDK has gets called.
		private static void Apply(Component constraint)
		{
			bool any = false;
			foreach (string name in new[] { "ApplyModifiedChanges", "ApplyConfigurationChanges" }) {
				if (InvokeMethod(constraint, name)) any = true;
			}
			if (!any) Debug.Log("KAT board controls: this SDK's constraint has no ApplyModifiedChanges()/ApplyConfigurationChanges(); relying on the serialized data instead.");
			EditorUtility.SetDirty(constraint);
			PrefabUtility.RecordPrefabInstancePropertyModifications(constraint);
		}

		// -------------------------------------------------------------------------------------------- constraint sources
		//
		// Why this is more than constraint.Sources.Add(...): VRChat's own bug tracker has reports that Add / RemoveAt / SetLength on the
		// Sources list do nothing in some situations, and that is the same thing that left the Text constraint with 0 sources here.
		// So the sources are written through SerializedObject first (the same data the inspector edits, which always sticks in the Editor),
		// the API is only the fallback, and either way the result is READ BACK from the component instead of trusting that Add worked.

		private static IEnumerable<SerializedProperty> Children(SerializedProperty parent)
		{
			SerializedProperty it = parent.Copy();
			SerializedProperty end = parent.GetEndProperty();
			bool enter = true;
			while (it.Next(enter) && !SerializedProperty.EqualContents(it, end)) {
				enter = false;
				yield return it.Copy();
			}
		}

		private static SerializedProperty FindSourcesProperty(SerializedObject so)
		{
			foreach (string name in new[] { "Sources", "sources", "_sources", "m_Sources" }) {
				SerializedProperty p = so.FindProperty(name);
				if (p != null) return p;
			}
			return null;
		}

		// The list is a fixed set of slots (source0, source1, ...) plus a length, or an ordinary serialized array. This copes with both.
		private static void ReadSlots(SerializedProperty sources, out List<SerializedProperty> slots, out SerializedProperty length)
		{
			slots = new List<SerializedProperty>();
			length = null;
			SortedDictionary<int, SerializedProperty> numbered = new SortedDictionary<int, SerializedProperty>();
			foreach (SerializedProperty child in Children(sources)) {
				Match m = Regex.Match(child.name, @"^_?source(\d+)$", RegexOptions.IgnoreCase);
				if (m.Success && child.propertyType == SerializedPropertyType.Generic) numbered[int.Parse(m.Groups[1].Value)] = child;
				else if (child.propertyType == SerializedPropertyType.Integer && Regex.IsMatch(child.name, "length|count|size", RegexOptions.IgnoreCase)) length = child;
			}
			slots.AddRange(numbered.Values);
		}

		private static bool WriteSlot(SerializedProperty slot, Transform transform, float weight)
		{
			bool gotTransform = false, gotWeight = false;
			foreach (SerializedProperty f in Children(slot)) {
				string n = f.name.ToLowerInvariant();
				if (f.propertyType == SerializedPropertyType.ObjectReference && n.Contains("transform")) { f.objectReferenceValue = transform; gotTransform = true; }
				else if (f.propertyType == SerializedPropertyType.Float && n == "weight") { f.floatValue = weight; gotWeight = true; }
				else if (n.Contains("offset") && f.propertyType == SerializedPropertyType.Vector3) f.vector3Value = Vector3.zero;
				else if (n.Contains("offset") && f.propertyType == SerializedPropertyType.Quaternion) f.quaternionValue = Quaternion.identity;
			}
			return gotTransform && gotWeight;
		}

		private static Transform SlotTransform(SerializedProperty slot)
		{
			foreach (SerializedProperty f in Children(slot)) {
				if (f.propertyType == SerializedPropertyType.ObjectReference && f.name.ToLowerInvariant().Contains("transform")) return f.objectReferenceValue as Transform;
			}
			return null;
		}

		private static bool SetSourcesSerialized(Component constraint, Transform[] transforms, float[] weights)
		{
			try {
				SerializedObject so = new SerializedObject(constraint);
				SerializedProperty sources = FindSourcesProperty(so);
				if (sources == null) return false;

				if (sources.isArray && sources.propertyType != SerializedPropertyType.String) {
					sources.arraySize = transforms.Length;
					for (int i = 0; i < transforms.Length; i++) {
						if (!WriteSlot(sources.GetArrayElementAtIndex(i), transforms[i], weights[i])) return false;
					}
				} else {
					List<SerializedProperty> slots;
					SerializedProperty length;
					ReadSlots(sources, out slots, out length);
					if (slots.Count < transforms.Length) return false;
					for (int i = 0; i < slots.Count; i++) {
						if (i < transforms.Length) { if (!WriteSlot(slots[i], transforms[i], weights[i])) return false; }
						else WriteSlot(slots[i], null, 0f);
					}
					if (length != null) length.intValue = transforms.Length;
				}
				so.ApplyModifiedPropertiesWithoutUndo();
				return true;
			} catch (Exception e) {
				Debug.LogWarning("KAT board controls: writing the sources through SerializedObject failed (" + e.GetType().Name + ": " + e.Message + ").");
				return false;
			}
		}

		// The documented way, as a fallback: constraint.Sources.Add(new VRCConstraintSource(transform, weight)).
		private static bool SetSourcesApi(Component constraint, Transform[] transforms, float[] weights)
		{
			try {
				Type sourceType = FindSourceType();
				object list = GetMember(constraint, "Sources");
				if (list == null || sourceType == null) return false;
				InvokeMethod(list, "Clear");

				MethodInfo add = list.GetType().GetMethod("Add", new Type[] { sourceType });
				if (add == null) {   // an explicit interface implementation isn't found by name
					Type collection = typeof(ICollection<>).MakeGenericType(sourceType);
					if (collection.IsInstanceOfType(list)) add = collection.GetMethod("Add");
				}
				if (add == null) return false;

				for (int i = 0; i < transforms.Length; i++) {
					object source;
					try {
						source = Activator.CreateInstance(sourceType, new object[] { transforms[i], weights[i] });   // new VRCConstraintSource(Transform, float)
					} catch (MissingMethodException) {
						source = Activator.CreateInstance(sourceType);
						SetObjectMember(source, "SourceTransform", transforms[i]);
						SetObjectMember(source, "Weight", weights[i]);
					}
					add.Invoke(list, new object[] { source });
				}
				if (list.GetType().IsValueType) SetObjectMember(constraint, "Sources", list);   // a boxed struct is a copy: put it back
				Apply(constraint);
				return true;
			} catch (Exception e) {
				Debug.LogWarning("KAT board controls: Sources.Add failed (" + e.GetType().Name + ": " + e.Message + ").");
				return false;
			}
		}

		private static int SourceCount(object list)
		{
			object count = GetMember(list, "Count");
			return count is int ? (int)count : -1;
		}

		// What the component really holds right now (a fresh read, not the list object we wrote to). Null if the layout isn't recognised.
		private static List<Transform> ReadSources(Component constraint)
		{
			try {
				SerializedObject so = new SerializedObject(constraint);
				SerializedProperty sources = FindSourcesProperty(so);
				if (sources == null) return null;
				List<Transform> result = new List<Transform>();
				if (sources.isArray && sources.propertyType != SerializedPropertyType.String) {
					for (int i = 0; i < sources.arraySize; i++) result.Add(SlotTransform(sources.GetArrayElementAtIndex(i)));
					return result;
				}
				List<SerializedProperty> slots;
				SerializedProperty length;
				ReadSlots(sources, out slots, out length);
				if (slots.Count == 0) return null;
				int count = length != null ? Mathf.Min(length.intValue, slots.Count) : slots.Count;
				for (int i = 0; i < count; i++) result.Add(SlotTransform(slots[i]));
				if (length == null) { while (result.Count > 0 && result[result.Count - 1] == null) result.RemoveAt(result.Count - 1); }
				return result;
			} catch (Exception) {
				return null;
			}
		}

		private static bool VerifySources(Component constraint, Transform[] transforms)
		{
			List<Transform> read = ReadSources(constraint);
			if (read != null) {
				if (read.Count != transforms.Length) return false;
				for (int i = 0; i < transforms.Length; i++) { if (read[i] != transforms[i]) return false; }
				return true;
			}
			object list = GetMember(constraint, "Sources");
			return list != null && SourceCount(list) == transforms.Length;
		}

		// Replaces the constraint's sources with these, in this order, and checks that they are really there.
		private static bool SetSources(Component constraint, Transform[] transforms, float[] weights)
		{
			if (SetSourcesSerialized(constraint, transforms, weights)) {
				Apply(constraint);
				if (VerifySources(constraint, transforms)) return true;
				Debug.LogWarning("KAT board controls: the sources were written through SerializedObject but didn't read back; trying Sources.Add.");
			}
			if (SetSourcesApi(constraint, transforms, weights) && VerifySources(constraint, transforms)) return true;
			DumpSourceDiagnostics(constraint);
			return false;
		}

		// If it still doesn't work, this is what I need to see (it is only printed on failure).
		private static void DumpSourceDiagnostics(Component constraint)
		{
			StringBuilder sb = new StringBuilder();
			try {
				sb.Append("KAT board controls: couldn't get sources into ").Append(constraint.GetType().FullName).Append(". Please send this whole message back.\n");
				object list = GetMember(constraint, "Sources");
				if (list == null) {
					sb.Append("  Sources: (no property or field named Sources)\n");
				} else {
					sb.Append("  Sources type: ").Append(list.GetType().FullName).Append(list.GetType().IsValueType ? " (struct)" : " (class)").Append(", Count=").Append(SourceCount(list)).Append('\n');
					foreach (MethodInfo m in list.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) sb.Append("    method ").Append(m).Append('\n');
				}
				foreach (MethodInfo m in constraint.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
					if (Regex.IsMatch(m.Name, "Apply|Zero|Lock|Activate|Source|Bake", RegexOptions.IgnoreCase)) sb.Append("  constraint method ").Append(m).Append('\n');
				}
				SerializedObject so = new SerializedObject(constraint);
				SerializedProperty sources = FindSourcesProperty(so);
				if (sources == null) {
					sb.Append("  no serialized property called Sources; top-level serialized properties: ");
					SerializedProperty it = so.GetIterator();
					bool enter = true;
					while (it.Next(enter)) { enter = false; sb.Append(it.propertyPath).Append(' ').Append(" "); }
					sb.Append('\n');
				} else {
					sb.Append("  serialized Sources (").Append(sources.propertyType).Append(", isArray=").Append(sources.isArray).Append("):\n");
					SerializedProperty it = sources.Copy();
					SerializedProperty end = sources.GetEndProperty();
					int shown = 0;
					bool enter = true;
					while (it.Next(enter) && !SerializedProperty.EqualContents(it, end) && shown++ < 60) {
						enter = true;
						sb.Append("    ").Append(it.propertyPath).Append(" : ").Append(it.propertyType).Append('\n');
					}
				}
			} catch (Exception e) {
				sb.Append("  (diagnostics failed: ").Append(e.Message).Append(')');
			}
			Debug.LogError(sb.ToString());
		}

		// -------------------------------------------------------------------------------------------- scene objects

		private static Transform CreateHandPoint(Transform avatarRoot, Animator animator, HumanBodyBones hand, HumanBodyBones middleFinger, string name)
		{
			Transform bone = animator.GetBoneTransform(hand);
			if (bone == null) return null;

			GameObject target = new GameObject(name);
			target.transform.SetParent(bone, false);

			// A little past the knuckles, so the board floats in front of the fingers. This is done in world space because the bone axes
			// are different on every rig.
			Transform finger = animator.GetBoneTransform(middleFinger);
			Vector3 position = finger != null
				? bone.position + (finger.position - bone.position) * 1.8f
				: bone.position + (bone.position - bone.parent.position).normalized * 0.12f;
			target.transform.position = position;
			return target.transform;
		}

		// An object under the avatar root that sits on the hand point. It is moved there by a position constraint and never rotates by
		// itself, so it keeps the root's facing. Faces the same way as the docked board (the Constraint object is turned 180 degrees
		// about Y). If this SDK has no VRCPositionConstraint, the point itself is used, which is how it used to work.
		private static Transform CreateHandAnchor(Transform container, Transform avatarRoot, Transform point, string name)
		{
			Type positionType = FindType(PositionConstraintTypeName);
			if (positionType == null || point == null) {
				Debug.LogWarning("KAT board controls: no VRCPositionConstraint in this SDK, so the hand uses the bone's own target (it will turn with the wrist).");
				return point;
			}

			GameObject anchor = new GameObject(name);
			anchor.transform.SetParent(container, false);
			anchor.transform.position = point.position;
			anchor.transform.rotation = Quaternion.LookRotation(-avatarRoot.forward, Vector3.up);

			Component pc = anchor.AddComponent(positionType);
			List<string> missing = new List<string>();
			SetMember(pc, "TargetTransform", anchor.transform, missing);
			SetMember(pc, "SolveInLocalSpace", false, null);
			SetMember(pc, "GlobalWeight", 1f, missing);
			SetMember(pc, "FreezeToWorld", false, null);
			SetMember(pc, "AffectsPositionX", true, null);
			SetMember(pc, "AffectsPositionY", true, null);
			SetMember(pc, "AffectsPositionZ", true, null);
			if (!SetSources(pc, new Transform[] { point }, new float[] { 1f })) {
				Debug.LogWarning("KAT board controls: couldn't add the hand point as a source of the position constraint, so the bone's own target is used.");
				GameObject.DestroyImmediate(anchor);
				return point;
			}
			SetMember(pc, "IsActive", true, missing);
			Apply(pc);
			if (!InvokeMethod(pc, "ZeroConstraint")) SetMember(pc, "Locked", true, null);   // activates and locks it with zero offsets, once the source is in
			Apply(pc);
			if (missing.Count > 0) Debug.LogWarning("KAT board controls: these VRC position constraint members were not found: " + string.Join(", ", missing));
			EditorUtility.SetDirty(pc);
			return anchor.transform;
		}

		// Checks the avatar before anything is installed, so we can say no up front instead of leaving a Board menu behind that does nothing.
		public static bool CanInstall(Transform avatarRoot, out string reason)
		{
			reason = null;
			if (!IsAvailable()) {
				reason = "Board controls need VRC constraints (VRCParentConstraint), which this VRChat SDK doesn't have. Update the SDK to 3.5 or newer, or untick \"Install board controls\".";
				return false;
			}
			Animator animator = avatarRoot.GetComponent<Animator>();
			if (animator == null || !animator.isHuman) {
				reason = "Board controls need a humanoid avatar (to find the hands). Untick \"Install board controls\", or set the avatar's rig to Humanoid.";
				return false;
			}
			if (animator.GetBoneTransform(HumanBodyBones.LeftHand) == null || animator.GetBoneTransform(HumanBodyBones.RightHand) == null) {
				reason = "Board controls need both hand bones mapped in the avatar's Humanoid rig.";
				return false;
			}
			return true;
		}

		// Reflection can throw in ways I can't predict from here (an SDK that names things differently, say). If it does, the install carries on
		// without the board instead of dying halfway through.
		public static bool InstallConstraint(Transform avatarRoot, Transform text, KatSettings settings)
		{
			try {
				return InstallConstraintUnchecked(avatarRoot, text, settings);
			} catch (Exception e) {
				Debug.LogWarning("KAT board controls: couldn't set up the VRC constraint (" + e.GetType().Name + ": " + e.Message + "). The rest of the install carries on without it.");
				try { RemoveTargets(avatarRoot, settings); } catch (Exception) { }
				return false;
			}
		}

		private static bool InstallConstraintUnchecked(Transform avatarRoot, Transform text, KatSettings settings)
		{
			Type constraintType = ConstraintType();
			if (constraintType == null || FindSourceType() == null) {
				Debug.LogWarning("KAT board controls: this VRChat SDK has no VRC constraints (VRCParentConstraint). Update the SDK (3.5+, Unity 2022) to use them.");
				return false;
			}

			Animator animator = avatarRoot.GetComponent<Animator>();
			if (animator == null || !animator.isHuman) {
				Debug.LogWarning("KAT board controls: the avatar needs a humanoid Animator to find the hands.");
				return false;
			}

			RemoveTargets(avatarRoot, settings);

			Transform leftPoint = CreateHandPoint(avatarRoot, animator, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, settings.BoardHandLeftPointName);
			Transform rightPoint = CreateHandPoint(avatarRoot, animator, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, settings.BoardHandRightPointName);
			if (leftPoint == null || rightPoint == null) {
				Debug.LogWarning("KAT board controls: couldn't find both hand bones.");
				return false;
			}
			// The anchors and the follow target go in one container under KillFrenzyAvatarText, not on the avatar root. It sits next to the Constraint object,
			// so it has the same parent (the avatar root's pose) and goes away with the text.
			Transform katRoot = text.parent != null && text.parent.parent != null ? text.parent.parent : avatarRoot;
			GameObject container = new GameObject(settings.BoardTargetsName);
			container.transform.SetParent(katRoot, false);
			Transform left = CreateHandAnchor(container.transform, avatarRoot, leftPoint, settings.BoardHandLeftName);
			Transform right = CreateHandAnchor(container.transform, avatarRoot, rightPoint, settings.BoardHandRightName);

			// In front of the chest, not at the feet: if a constraint rebake ever doesn't happen, this is where the board lands.
			Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ??
				animator.GetBoneTransform(HumanBodyBones.Spine) ?? animator.GetBoneTransform(HumanBodyBones.Head);
			float height = chest != null ? Mathf.Max(0.3f, chest.position.y - avatarRoot.position.y) : 1.0f;
			GameObject follow = new GameObject(settings.BoardFollowName);
			follow.transform.SetParent(container.transform, false);
			follow.transform.position = avatarRoot.position + Vector3.up * height + avatarRoot.forward * (0.3f * height);
			follow.transform.rotation = Quaternion.LookRotation(-avatarRoot.forward, Vector3.up);

			// Remember where the Text object sits in the docked board right now. Zeroing/locking a constraint can move its target in the Editor,
			// and the Dock animation also puts the board back to exactly this pose.
			Vector3 restPosition = text.localPosition;
			Quaternion restRotation = text.localRotation;

			Component constraint = text.gameObject.GetComponent(constraintType);
			if (constraint != null) GameObject.DestroyImmediate(constraint);   // start clean, so an old install can't leave stale sources behind
			constraint = text.gameObject.AddComponent(constraintType);

			List<string> missing = new List<string>();
			SetMember(constraint, "TargetTransform", text, missing);
			SetMember(constraint, "SolveInLocalSpace", false, null);
			SetMember(constraint, "GlobalWeight", 1f, missing);
			SetMember(constraint, "FreezeToWorld", false, missing);
			SetMember(constraint, "RebakeOffsetsWhenUnfrozen", true, missing);   // this is what keeps a follow drop where you let go
			SetMember(constraint, "AffectsPositionX", true, null);
			SetMember(constraint, "AffectsPositionY", true, null);
			SetMember(constraint, "AffectsPositionZ", true, null);
			SetMember(constraint, "AffectsRotationX", false, null);              // like the docked board: stays upright, only turns about Y
			SetMember(constraint, "AffectsRotationY", true, null);
			SetMember(constraint, "AffectsRotationZ", false, null);
			SetMember(constraint, "IsActive", false, missing);                   // starts docked, until the menu says otherwise

			// The order matters, because the animator clips refer to the sources by number (0 left hand, 1 right hand, 2 follow)
			bool sources = SetSources(constraint, new Transform[] { left, right, follow.transform }, new float[] { 0f, 0f, 0f });
			if (!sources) {
				Debug.LogWarning("KAT board controls: couldn't add the constraint sources (this SDK's VRCConstraintSource doesn't look the way I expected).");
				GameObject.DestroyImmediate(constraint);
				RemoveTargets(avatarRoot, settings);
				return false;
			}
			Apply(constraint);
			if (!InvokeMethod(constraint, "ZeroConstraint")) SetMember(constraint, "Locked", true, null);   // locks it with zero offsets, so the board sits right on its target
			SetMember(constraint, "IsActive", false, null);                      // ZeroConstraint activates it; the board starts docked
			Apply(constraint);
			text.localPosition = restPosition;                                    // undo anything the zeroing did to the board in the Editor
			text.localRotation = restRotation;
			if (!VerifySources(constraint, new Transform[] { left, right, follow.transform })) {
				Debug.LogError("KAT board controls: the constraint was created but its sources did not stick (see Check Board Controls).");
				DumpSourceDiagnostics(constraint);
			}
			Debug.Log("KAT board controls: " + Describe(constraint));
			if (missing.Count > 0) {
				Debug.LogWarning("KAT board controls: these VRC constraint members were not found and were skipped: " + string.Join(", ", missing) +
					". Drop-and-follow in particular needs RebakeOffsetsWhenUnfrozen and FreezeToWorld.");
			}

			EditorUtility.SetDirty(constraint);
			return true;
		}

		public static void RemoveTargets(Transform avatarRoot, KatSettings settings)
		{
			string[] names = { settings.BoardTargetsName, settings.BoardHandLeftName, settings.BoardHandRightName, settings.BoardFollowName,
				settings.BoardHandLeftPointName, settings.BoardHandRightPointName };
			List<GameObject> found = new List<GameObject>();
			foreach (Transform t in avatarRoot.GetComponentsInChildren<Transform>(true)) {
				if (Array.IndexOf(names, t.name) >= 0) found.Add(t.gameObject);
			}
			foreach (GameObject go in found) {
				if (go != null) GameObject.DestroyImmediate(go);
			}
		}

		// -------------------------------------------------------------------------------------------- self check

		// One line saying what the constraint on the Text object looks like, so a board that doesn't move can be told apart from one whose
		// sources never made it in.
		public static string Describe(Component constraint)
		{
			StringBuilder sb = new StringBuilder();
			try {
				foreach (string n in new[] { "IsActive", "Locked", "FreezeToWorld", "RebakeOffsetsWhenUnfrozen", "SolveInLocalSpace", "GlobalWeight" }) {
					object v = GetMember(constraint, n);
					sb.Append(n).Append('=').Append(v == null ? "(missing)" : v.ToString()).Append("; ");
				}
				object list = GetMember(constraint, "Sources");
				int count = list != null ? SourceCount(list) : 0;
				sb.Append("sources(").Append(count).Append("): ");
				PropertyInfo indexer = list != null ? list.GetType().GetProperty("Item", Any) : null;
				for (int i = 0; indexer != null && i < count; i++) {
					object source = indexer.GetValue(list, new object[] { i });
					Transform t = GetMember(source, "SourceTransform") as Transform;
					object w = GetMember(source, "Weight");
					sb.Append('[').Append(i).Append(' ').Append(t != null ? t.name : "NULL").Append(" w=").Append(w).Append("] ");
				}
				List<Transform> fresh = ReadSources(constraint);
				if (fresh != null) {
					sb.Append(" | serialized: ");
					for (int i = 0; i < fresh.Count; i++) sb.Append('[').Append(i).Append(' ').Append(fresh[i] != null ? fresh[i].name : "NULL").Append("] ");
					count = Mathf.Max(count, fresh.Count);
				}
				if (count != 3) sb.Append("<-- expected 3 sources (left hand, right hand, follow)");
			} catch (Exception e) {
				sb.Append("(couldn't read it: ").Append(e.Message).Append(')');
			}
			return sb.ToString();
		}

		[MenuItem("Tools/KAT/Check Board Controls (selected avatar)")]
		public static void CheckSelected()
		{
			Transform t = Selection.activeTransform;
			while (t != null && t.Find(TextRelativePath) == null) t = t.parent;
			if (t == null) { Debug.LogWarning("KAT board check: select the avatar (or anything inside it) first."); return; }
			Transform text = t.Find(TextRelativePath);
			Type constraintType = ConstraintType();
			Component constraint = constraintType != null ? text.GetComponent(constraintType) : null;
			if (constraint == null) { Debug.LogWarning("KAT board check: there is no VRC constraint on " + text.name + ", so board controls aren't installed on this avatar."); return; }
			Debug.Log("KAT board check (" + t.name + "): " + Describe(constraint));
		}

		// -------------------------------------------------------------------------------------------- animation clips

		private sealed class Bindings
		{
			public EditorCurveBinding isActive, freeze;
			public EditorCurveBinding[] weights = new EditorCurveBinding[3];
		}

		private static Bindings FindBindings(Transform avatarRoot, Transform text, Type constraintType)
		{
			EditorCurveBinding[] all = AnimationUtility.GetAnimatableBindings(text.gameObject, avatarRoot.gameObject);
			List<EditorCurveBinding> mine = new List<EditorCurveBinding>();
			foreach (EditorCurveBinding b in all) {
				if (b.type == constraintType) mine.Add(b);
			}

			Bindings result = new Bindings();
			bool haveActive = false, haveFreeze = false;
			bool[] haveWeight = new bool[3];
			foreach (EditorCurveBinding b in mine) {
				string n = b.propertyName;
				if (!haveActive && n.EndsWith("IsActive", StringComparison.OrdinalIgnoreCase)) { result.isActive = b; haveActive = true; continue; }
				if (!haveFreeze && n.IndexOf("FreezeToWorld", StringComparison.OrdinalIgnoreCase) >= 0) { result.freeze = b; haveFreeze = true; continue; }
				if (n.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith("Weight", StringComparison.OrdinalIgnoreCase) &&
					n.IndexOf("Global", StringComparison.OrdinalIgnoreCase) < 0) {
					Match m = Regex.Match(n, @"(\d+)");
					if (m.Success) {
						int index = int.Parse(m.Groups[1].Value);
						if (index >= 0 && index < 3 && !haveWeight[index]) { result.weights[index] = b; haveWeight[index] = true; }
					}
				}
			}

			if (haveActive && haveFreeze && haveWeight[0] && haveWeight[1] && haveWeight[2]) return result;

			StringBuilder sb = new StringBuilder();
			foreach (EditorCurveBinding b in mine) sb.Append(b.propertyName).Append("; ");
			Debug.LogError("KAT board controls: couldn't find the animation properties of the VRC constraint (IsActive: " + haveActive +
				", FreezeToWorld: " + haveFreeze + ", source weights 0/1/2: " + haveWeight[0] + "/" + haveWeight[1] + "/" + haveWeight[2] +
				"). What the constraint offers: " + sb);
			return null;
		}

		private static AnimationClip Make(KatSettings settings, string name, float length, Bindings b, bool active, bool freeze, float w0, float w1, float w2)
		{
			AnimationClip clip = KatPagedClips.LoadOrCreateClip(settings, name);
			Action<EditorCurveBinding, float> set = (binding, value) =>
				AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, length, value));
			set(b.isActive, active ? 1f : 0f);
			set(b.freeze, freeze ? 1f : 0f);
			set(b.weights[0], w0);
			set(b.weights[1], w1);
			set(b.weights[2], w2);
			EditorUtility.SetDirty(clip);
			return clip;
		}

		// An inactive VRC constraint leaves its target wherever it last put it. After "drop in world" that is far from the avatar, and
		// the board used to stay there when you went back to the dock. So the Dock clip also writes the Text object's docked local pose.
		// Only the Dock clip has these curves. The layer's states all have write defaults off (KatAnimator.CreateState), so no other state
		// touches the Text transform, and the constraint is what moves it in the other modes.
		private static void AddRestPose(AnimationClip clip, Transform avatarRoot, Transform text, float length)
		{
			string path = AnimationUtility.CalculateTransformPath(text, avatarRoot);
			Vector3 p = text.localPosition;
			Quaternion q = text.localRotation;
			string[] names = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
			float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
			for (int i = 0; i < names.Length; i++) {
				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), names[i]), AnimationCurve.Constant(0f, length, values[i]));
			}
			EditorUtility.SetDirty(clip);
		}

		// Every clip sets every property, so it comes out the same with write defaults on or off, whatever state came before.
		public static KatBoardClips BuildClips(Transform avatarRoot, KatSettings settings)
		{
			Transform text = avatarRoot.Find(TextRelativePath);
			Type constraintType = ConstraintType();
			if (text == null || constraintType == null || text.GetComponent(constraintType) == null) {
				Debug.LogWarning("KAT board controls: the VRC constraint on the Text object wasn't found, so the board layer is skipped.");
				return null;
			}

			Bindings b = FindBindings(avatarRoot, text, constraintType);
			if (b == null) return null;

			const float one = 1f / 60f;
			KatBoardClips clips = new KatBoardClips();
			clips.dock = Make(settings, "BoardDock", one, b, false, false, 0f, 0f, 0f);
			AddRestPose(clips.dock, avatarRoot, text, one);
			clips.holdLeft = Make(settings, "BoardHoldLeft", one, b, true, false, 1f, 0f, 0f);
			clips.holdRight = Make(settings, "BoardHoldRight", one, b, true, false, 0f, 1f, 0f);
			clips.world = Make(settings, "BoardWorld", one, b, true, true, 0f, 0f, 1f);
			clips.prep = Make(settings, "BoardFollowPrep", 0.25f, b, true, true, 0f, 0f, 1f);    // frozen for a moment, then it moves on to Follow
			clips.follow = Make(settings, "BoardFollow", one, b, true, false, 0f, 0f, 1f);
			// Freezing from a hand must keep that hand's weight. Freeze To World holds whatever pose the constraint solves to in the frame it
			// turns on, and the old clips changed the weights in that same frame, so the board was frozen at the follow source instead of at the hand.
			clips.worldLeft = Make(settings, "BoardWorldLeft", one, b, true, true, 1f, 0f, 0f);
			clips.worldRight = Make(settings, "BoardWorldRight", one, b, true, true, 0f, 1f, 0f);
			clips.prepLeft = Make(settings, "BoardFollowPrepLeft", 0.25f, b, true, true, 1f, 0f, 0f);
			clips.prepRight = Make(settings, "BoardFollowPrepRight", 0.25f, b, true, true, 0f, 1f, 0f);
			// Leaving a frozen state: the constraint rebakes offsets when it un-freezes, into whichever sources have weight at that moment. Doing that
			// with a hand weighted left the hand with an offset, so the board stayed put instead of going to the hand. Un-freeze here, with only the
			// follow source weighted (it rebakes again whenever Follow is entered), and only then move on.
			clips.unfreeze = Make(settings, "BoardUnfreeze", one, b, true, false, 0f, 0f, 1f);
			AssetDatabase.SaveAssets();
			return clips;
		}
	}
}

#endif
