/*
Parameter budget calculator.

VRChat's documented limit is 256 bits of SYNCED expression parameters (bool = 1 bit, int/float = 8 bits).
There is also a VRChat bug report where the avatar failed to load when the total bits of all expression
parameters, synced or not, exceeded 256, so this reports both numbers. Animator-only parameters (the WD-On
memory, keyboard contacts) are not expression parameters and are documented as local, so they are listed
separately together with a worst case in case a client ever counts them.
*/

#if UNITY_EDITOR

using System.Text;
using UnityEngine;

#if VRC_SDK_VRCSDK3
	using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
#endif

using KillFrenzy.AvatarTextTools.Settings;

namespace KillFrenzy.AvatarTextTools.Utility
{
	public struct KatBudget
	{
		public int syncedBits;            // synced expression parameters
		public int localExpressionBits;   // unsynced expression parameters (Speed, Style)
		public int expressionEntries;
		public int animatorOnlyFloats;    // WD-On memory + keyboard contacts; not expression parameters
		public int animatorTotal;         // expressionEntries + animatorOnlyFloats
		public int worstCaseBits;         // everything counted, animator-only floats at 8 bits each

		public int TotalExpressionBits { get { return syncedBits + localExpressionBits; } }
	}

	public static class KatParameterBudget
	{
		public const int BitLimit = 256;

		public static KatBudget Compute(KatSettings s, bool writeDefaultsOn)
		{
			int sync = s.SyncParamsSize;
			int pages = s.GetPageSyncCount();
			bool keyboard = s.InstallKeyboard && s.SDK == KatSDK.VRChat;
			bool styleSpeed = s.InstallStyleSpeedControls;

			KatBudget b = new KatBudget();
			// Visible (bool 1) + Pointer (int 8) + CharSync floats + PageSync floats + HeadAnchor radial (float 8) [+ keyboard toggle 1]
			b.syncedBits = 1 + 8 + 8 * sync + 8 * pages + 8 + (keyboard ? 1 : 0);
			b.localExpressionBits = styleSpeed ? 16 : 0; // KAT_Speed + KAT_Style, float, not synced
			b.expressionEntries = 3 + sync + pages + (keyboard ? 1 : 0) + (styleSpeed ? 2 : 0);

			int keyboardLocal = keyboard ? 7 + s.KeyboardKeysCount : 0; // contacts + pressed-key floats written by the keyboard prefab
			int memory = writeDefaultsOn ? s.SlotCount + s.GetPageCellCount(s.SlotCount) + 1 : 0;
			b.animatorOnlyFloats = keyboardLocal + memory;
			b.animatorTotal = b.expressionEntries + b.animatorOnlyFloats;
			b.worstCaseBits = b.syncedBits + b.localExpressionBits + 8 * b.animatorOnlyFloats;
			return b;
		}

		// Non-blocking warnings for the selected options. atlasDepth = page count of the imported atlas, or -1 when unknown.
		public static System.Collections.Generic.List<string> Warnings(KatSettings s, int atlasDepth = -1)
		{
			System.Collections.Generic.List<string> warnings = new System.Collections.Generic.List<string>();
			KatBudget b = Compute(s, s.UseWriteDefaultsOn);
			if (b.syncedBits > BitLimit) {
				warnings.Add("Synced parameters use " + b.syncedBits + " bits, over VRChat's " + BitLimit + "-bit limit. " +
					(s.PageFormat == KatSettings.PageFormat3 ? "Page format 3 spends one float per slot; lower the sync parameter count or use format 2. " : "") +
					"KAT alone will not upload on an avatar with this many synced bits.");
			} else if (b.syncedBits > BitLimit - 32) {
				warnings.Add("Synced parameters use " + b.syncedBits + " of " + BitLimit + " bits, leaving almost nothing for the rest of the avatar.");
			}
			if (b.TotalExpressionBits > BitLimit && b.syncedBits <= BitLimit) {
				warnings.Add("Synced + local expression parameters total " + b.TotalExpressionBits + " bits; over " + BitLimit + " if VRChat counts local parameters too.");
			}
			int pageLayers = s.GetPageSyncCount();
			if (s.PageFormat == KatSettings.PageFormat3 && pageLayers >= 8) {
				warnings.Add("Page format 3 adds one animator layer per page float (" + pageLayers + " instead of " + (s.SyncParamsSize / 2) + ") and one clip per slot.");
			}
			if (s.PageFormat == KatSettings.PageFormat3 && atlasDepth > 0 && atlasDepth <= KatSettings.MaxPage2 + 1) {
				warnings.Add("Page format 3 is selected but the atlas has only " + atlasDepth + " pages; format 2 (default) already addresses up to " +
					(KatSettings.MaxPage2 + 1) + " and costs fewer parameter bits.");
			}
			if (s.PageFormat != KatSettings.PageFormat3 && atlasDepth > KatSettings.MaxPage2 + 1) {
				warnings.Add("The atlas has " + atlasDepth + " pages but page format 2 addresses at most " + (KatSettings.MaxPage2 + 1) + ". Set the installer's Page format to 3 (or Auto) or rebuild a smaller atlas.");
			}
			return warnings;
		}

		// Both modes, one line each; the selected one is marked.
		public static string Describe(KatSettings s)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine(Line("WD Off (default)", Compute(s, false), !s.UseWriteDefaultsOn));
			sb.AppendLine(Line("WD On, " + s.SlotCount + " slots", Compute(s, true), s.UseWriteDefaultsOn));
			sb.Append("Limit: " + BitLimit + " synced bits; total expression bits (synced + local) are kept under " + BitLimit + " too.");
			return sb.ToString();
		}

		private static string Line(string label, KatBudget b, bool selected)
		{
			return (selected ? "> " : "   ") + label + ": synced " + b.syncedBits + " bits, total expression " + b.TotalExpressionBits +
				" bits, animator params " + b.animatorTotal + " (" + b.animatorOnlyFloats + " animator-only)";
		}

		#if VRC_SDK_VRCSDK3
			// Existing avatar usage (KAT's own entries excluded) plus what this install would add.
			public static string DescribeAgainst(ExpressionParameters existing, KatSettings s)
			{
				if (existing == null || existing.parameters == null) return "No expression parameters asset selected.";

				int synced = 0, total = 0;
				foreach (ExpressionParameters.Parameter p in existing.parameters) {
					if (p == null || string.IsNullOrEmpty(p.name) || KatParametersInstaller.IsKatParameter(p.name, s)) continue;
					int bits = p.valueType == ExpressionParameters.ValueType.Bool ? 1 : 8;
					total += bits;
					if (p.networkSynced) synced += bits;
				}

				KatBudget k = Compute(s, s.UseWriteDefaultsOn);
				int afterSynced = synced + k.syncedBits;
				int afterTotal = total + k.TotalExpressionBits;

				StringBuilder sb = new StringBuilder();
				sb.AppendLine("Avatar without KAT: " + synced + " synced bits, " + total + " total expression bits.");
				sb.AppendLine("With KAT: " + afterSynced + " / " + BitLimit + " synced" + (afterSynced > BitLimit ? "  (OVER)" : "") +
					", " + afterTotal + " / " + BitLimit + " total" + (afterTotal > BitLimit ? "  (OVER if totals are enforced)" : "") + ".");
				if (afterTotal > BitLimit && afterSynced <= BitLimit) {
					sb.Append("Untick the Speed/Style menu controls (saves 16 total bits) or lower the sync parameter count to get under.");
				}
				return sb.ToString().TrimEnd();
			}
		#endif
	}
}

#endif
