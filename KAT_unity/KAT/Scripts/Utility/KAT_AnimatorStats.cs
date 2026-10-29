/*
Prints how big an animator controller is, layer by layer, so a slow avatar build can be traced to the layers that cause it.

Select an Animator Controller asset in the Project window (the avatar's FX controller) and run Tools > KAT > Animator Stats (selected controller).
The Console gets one line per layer, biggest first, and a total. Run it on a build with the keyboard and one without and compare.
*/

#if UNITY_EDITOR

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatAnimatorStats
	{
		private sealed class Row
		{
			public string name;
			public int states, transitions, conditions, behaviours, clips, bindings, blendTrees, blendChildren;
		}

		private static void CollectMotion(Motion motion, Row row, HashSet<AnimationClip> seen)
		{
			if (motion == null) return;
			AnimationClip clip = motion as AnimationClip;
			if (clip != null) {
				if (seen.Add(clip)) {
					row.clips++;
					row.bindings += AnimationUtility.GetCurveBindings(clip).Length + AnimationUtility.GetObjectReferenceCurveBindings(clip).Length;
				}
				return;
			}
			BlendTree tree = motion as BlendTree;
			if (tree == null) return;
			row.blendTrees++;
			foreach (ChildMotion child in tree.children) {
				row.blendChildren++;
				CollectMotion(child.motion, row, seen);
			}
		}

		private static void CollectMachine(AnimatorStateMachine machine, Row row, HashSet<AnimationClip> seen)
		{
			row.transitions += machine.anyStateTransitions.Length + machine.entryTransitions.Length;
			foreach (AnimatorStateTransition t in machine.anyStateTransitions) row.conditions += t.conditions.Length;
			foreach (ChildAnimatorState child in machine.states) {
				row.states++;
				row.transitions += child.state.transitions.Length;
				foreach (AnimatorStateTransition t in child.state.transitions) row.conditions += t.conditions.Length;
				row.behaviours += child.state.behaviours.Length;
				CollectMotion(child.state.motion, row, seen);
			}
			foreach (ChildAnimatorStateMachine child in machine.stateMachines) CollectMachine(child.stateMachine, row, seen);
		}

		[MenuItem("Tools/KAT/Animator Stats (selected controller)")]
		public static void PrintSelected()
		{
			AnimatorController controller = Selection.activeObject as AnimatorController;
			if (controller == null) {
				Debug.LogWarning("KAT animator stats: select an Animator Controller asset in the Project window first.");
				return;
			}

			List<Row> rows = new List<Row>();
			Row total = new Row();
			total.name = "TOTAL";
			foreach (AnimatorControllerLayer layer in controller.layers) {
				Row row = new Row();
				row.name = layer.name;
				CollectMachine(layer.stateMachine, row, new HashSet<AnimationClip>());
				rows.Add(row);
				total.states += row.states;
				total.transitions += row.transitions;
				total.conditions += row.conditions;
				total.behaviours += row.behaviours;
				total.clips += row.clips;
				total.bindings += row.bindings;
				total.blendTrees += row.blendTrees;
				total.blendChildren += row.blendChildren;
			}
			rows.Sort((a, b) => (b.transitions + b.conditions + b.bindings).CompareTo(a.transitions + a.conditions + a.bindings));

			StringBuilder sb = new StringBuilder();
			sb.AppendLine("KAT animator stats for " + controller.name + " (" + controller.layers.Length + " layers, " + controller.parameters.Length + " parameters), biggest first:");
			foreach (Row r in rows) sb.AppendLine(Format(r));
			sb.Append(Format(total));
			Debug.Log(sb.ToString());
		}

		private static string Format(Row r)
		{
			return string.Format("  {0}: {1} states, {2} transitions, {3} conditions, {4} state behaviours, {5} blend trees ({6} children), {7} clips, {8} curve bindings",
				r.name, r.states, r.transitions, r.conditions, r.behaviours, r.blendTrees, r.blendChildren, r.clips, r.bindings);
		}
	}
}

#endif
