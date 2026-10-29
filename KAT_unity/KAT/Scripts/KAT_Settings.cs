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

namespace KillFrenzy.AvatarTextTools.Settings
{
	public class KatSettings
	{
		public readonly int SDK = KatSDK.VRChat;
		public readonly string ParamTextVisible = "KAT_Visible";
		public readonly string ParamTextPointer = "KAT_Pointer";
		public readonly string ParamTextSyncPrefix = "KAT_CharSync";
		public readonly string ParamTextPageSyncPrefix = "KAT_PageSync";
		public readonly string ParamTextWdOnMemoryCharPrefix = "KAT_WDOnCharMem";
		public readonly string ParamTextWdOnMemoryPagePrefix = "KAT_WDOnPageMem";
		public readonly string ParamTextWdOnAlwaysOne = "KAT_WDOnAlwaysOne";

		public readonly string ParamHeadAnchor = "KAT_HeadAnchor";
		public readonly string ParamSpeed = "KAT_Speed";
		public readonly string ParamStyle = "KAT_Style";

		// The Advanced menu (a submenu inside "KAT Text"). All of these are local-only, so they cost no synced bits, and each one has
		// its own tiny animator layer. That way they never fight the anchor or style layers over the same property.
		public readonly string ParamAdvPrefix = "KAT_Adv_";
		public readonly string ParamAdvBillboard = "KAT_Adv_Billboard";   // Int: 0 = auto (the head anchor decides), 1 = always face the viewer, 2 = never
		public readonly string ParamAdvGrow = "KAT_Adv_Grow";             // Bool, default on:  _GrowEnabled
		public readonly string ParamAdvBox = "KAT_Adv_Box";               // Bool, default on:  _BoxEnabled
		public readonly string ParamAdvShrink = "KAT_Adv_Shrink";         // Bool, default on:  _ShrinkToFit
		public readonly string ParamAdvBold = "KAT_Adv_Bold";             // Bool, default off: _Weight
		public readonly string ParamAdvSize = "KAT_Adv_Size";             // Float radial, 0.5 = normal size. Scales the whole board
		public const float AdvSizeMin = 0.35f;                           // radial 0.0
		public const float AdvSizeDefault = 1.0f;                        // radial 0.5
		public const float AdvSizeMax = 2.5f;                            // radial 1.0

		// Board controls: hold the board in a hand, drop it in the world, or drop it so it follows you. One synced int; KAT_Board.cs
		// explains how it all works.
		public readonly string ParamBoardPrefix = "KAT_Board_";
		public readonly string ParamBoardMode = "KAT_Board_Mode";         // Int, synced: 0 dock, 1 hold left, 2 hold right, 3 drop in world, 4 drop and follow
		public const int BoardModeDock = 0, BoardModeHoldLeft = 1, BoardModeHoldRight = 2, BoardModeWorld = 3, BoardModeFollow = 4;
		public readonly string BoardHandLeftName = "KAT_Board_HandL";
		public readonly string BoardHandRightName = "KAT_Board_HandR";
		public readonly string BoardFollowName = "KAT_Board_Follow";
		// The anchors, the follow target and their constraints live in this object, under KillFrenzyAvatarText (so they go away with it), not on the avatar root.
		public readonly string BoardTargetsName = "KAT_Board_Targets";
		// The hand targets are two objects each: a "point" just past the knuckles (under the hand bone) and an "anchor" under the avatar
		// root that a position constraint pulls to that point. The anchor keeps the root's facing, so the board never takes the wrist's twist.
		public readonly string BoardHandLeftPointName = "KAT_Board_HandL_Point";
		public readonly string BoardHandRightPointName = "KAT_Board_HandR_Point";
		public readonly string HeadAttachmentPointName = "KAT_Head_AttachmentPoint";
		public readonly string HeadTopPivotName = "KAT_Head_Top_Pivot";
		public readonly Vector3 HeadAnchorOffset = new Vector3(-0.4f, 0.15f, 0.15f);
		public readonly Vector3 RightHeadAnchorOffset = new Vector3(0.4f, 0.15f, 0.15f);
		public readonly Vector3 TopHeadAnchorOffset = new Vector3(0.0f, 0.45f, 0.15f);
		public readonly Vector3 ChestAnchorOffset = new Vector3(0.0f, 0.0f, 0.2f);
		public const int ConstraintSourceChest = 0;
		public const int ConstraintSourceHeadLeft = 1;
		public const int ConstraintSourceHeadRight = 2;
		public const int ConstraintSourceHeadTop = 3;

		public readonly string ParamKeyboardPrefix = "KAT_Keyboard";
		public readonly string ParamKeyboardPressed = "KAT_KeyboardPressed";
		public readonly string ParamKeyboardShift = "KAT_KeyboardShift";
		public readonly string ParamKeyboardProximity = "KAT_KeyboardProximity";
		public readonly string ParamKeyboardProximityExit = "KAT_KeyboardProximityExit";
		public readonly string ParamKeyboardHighlight = "KAT_KeyboardHighlight";
		public readonly string ParamKeyboardPressedClear = "KAT_KeyboardPressedClear";
		public readonly string ParamKeyboardPressedBackspace = "KAT_KeyboardPressedBksp";
		public readonly string ParamKeyboardPressedShiftL = "KAT_KeyboardPressedShiftL";
		public readonly string ParamKeyboardPressedShiftR = "KAT_KeyboardPressedShiftR";

		public readonly string ParamKeyboardPressedSpace = "KAT_KeyboardPressedSpace";
		public readonly string ParamKeyboardPressedPageUp = "KAT_KeyboardPressedPgUp";
		public readonly string ParamKeyboardPressedPageDown = "KAT_KeyboardPressedPgDn";

		// Local helper parameters of the keyboard. None of them is synced.
		// Key scan (animated animator parameters, none of them synced). A Direct blend tree over the 128 KAT_KeyboardPressed{n} floats writes these, so the typing
		// states don't need one copy per key: KeyDown = how many glyph keys are down, KeyCode = the slot of the key that is down (0..127),
		// KeyHighlight = what KAT_KeyboardHighlight should show for it.
		public readonly string ParamKeyboardKeyDown = "KAT_KeyboardKeyDown";
		public readonly string ParamKeyboardKeyCode = "KAT_KeyboardKeyCode";
		public readonly string ParamKeyboardKeyHighlight = "KAT_KeyboardKeyHl";
		public readonly string ParamKeyboardMode = "KAT_KeyboardMode";             // int: 0 = page 0 (narrow), 1 = wide page slots 0..127, 2 = wide page slots 128..254
		public readonly string ParamKeyboardOdd = "KAT_KeyboardOdd";               // bool: the next slot to write is odd (wide glyphs must start on an even one)
		public readonly string ParamKeyboardLastWide = "KAT_KeyboardLastWide";     // bool: the last thing typed was a wide glyph, so Backspace removes two slots
		public readonly string ParamKeyboardPageValue = "KAT_KeyboardPageValue";   // float: what to write into the page cell for the lead half of a wide glyph
		public readonly string ParamKeyboardPageNorm = "KAT_KeyboardPageNorm";     // float: keyboard page / last keyboard page, drives the live key legends
		public readonly string KeyboardLayoutPath = "KillFrenzyAvatarKeyboard/ConstraintWorld/ConstraintChild/Keyboard/Layout";

		// Ids the highlight map uses (the glyph keys are 0..63). Must match tools/gen_keyboard.py.
		public readonly int KeyboardKeyClear = 64;
		public readonly int KeyboardKeyBackspace = 65;
		public readonly int KeyboardKeyShiftL = 66;
		public readonly int KeyboardKeyShiftR = 67;
		public readonly int KeyboardKeySpace = 68;
		public readonly int KeyboardKeyPageUp = 69;
		public readonly int KeyboardKeyPageDown = 70;

		// The keyboard types the 128 slots of one "keyboard page" at a time: the lower half (slots 64..127) by default, the top half (0..63) while Shift is
		// held. Slot 253 is never typed: it is the value Backspace puts on the sync channels to mean "leave this slot alone" (the old 97 clashed
		// with a real glyph). Slot 254 is the FOLLOW marker, which the keyboard writes itself after a wide glyph.
		public const int KeyboardHoldSlot = 253;
		public const int KeyboardFollowSlot = 254;
		public const int KeyboardPageHoldSymbol = 254;      // page channels: no real page or page pair has this value (format 2 tops out at 224, format 3 at 253)
		public readonly float KeyboardBackspaceMode = (KeyboardHoldSlot - 255f) / 127f;
		public readonly float KeyboardPageHold = (KeyboardPageHoldSymbol - 255f) / 127f;

		// Two contact sets of 64 receivers each share the same 64 physical keys: parameter KAT_KeyboardPressed{n} is "glyph slot n of the keyboard page".
		public readonly int KeyboardKeysCount = 128;

		public readonly string CharacterAnimationFolder = "KAT_CharAnimations/";
		public readonly string CharacterAnimationClipNamePrefix = "Char";
		public readonly int TextLength = 128;
		public const int SyncParamsMin = 2;
		public const int SyncParamsMax = 16;
		public const int SyncParamsDefault = 4;
		public const int PageFormat2 = 2;
		public const int PageFormat3 = 3;
		public const int MaxPage2 = 14;
		public const int MaxPage3 = 253;
		public const string PageFormat3Keyword = "KAT_PAGE_FORMAT_3";
		public readonly int SyncParamsSize;
		public readonly int PageFormat = PageFormat2;
		public readonly int PointerCount;
		public readonly int SlotCount;
		// The KAT Text menu (anchor, style, roll speed) is always installed now. The field stays so older code that reads it still compiles.
		public readonly bool InstallStyleSpeedControls = true;
		public readonly bool InstallAdvancedMenu;
		public readonly bool InstallBoardControls;   // VRChat only, and it needs VRC constraints (SDK 3.5+)
		public readonly int PointerAltSyncOffset = 100;
		public readonly bool InstallKeyboard = true;
		public readonly bool UseWriteDefaultsOn;
		public readonly KatConfig Config;

		public readonly string GeneratedOutputFolderName = "KAT_GeneratedOutput";

		public readonly string TextAttachmentPointName = "KAT_Text_AttachmentPoint";
		public readonly string KeyboardAttachmentPointName = "KAT_Keyboard_AttachmentPoint";
		public readonly int AttachmentPoint = KatAttachmentPoint.Chest;

		public KatSettings(int sdk = KatSDK.VRChat, bool installKeyboard = true, int attachmentPoint = KatAttachmentPoint.Chest, int syncParamsSize = 4,
			Vector3? headAnchorOffset = null, Vector3? chestAnchorOffset = null, bool useWriteDefaultsOn = false, int slotCount = 128, bool installAdvancedMenu = false, bool installBoardControls = false, KatConfig config = null, int pageFormat = PageFormat2)
		{
			SDK = sdk;
			if (pageFormat != PageFormat2 && pageFormat != PageFormat3)
				throw new System.ArgumentOutOfRangeException(nameof(pageFormat), pageFormat, "pageFormat must be 2 (default, packed pairs) or 3 (one page per synced float).");
			PageFormat = pageFormat;
			InstallKeyboard = installKeyboard;
			int normalizedSyncParams = System.Math.Max(SyncParamsMin, System.Math.Min(SyncParamsMax, syncParamsSize));
			if ((normalizedSyncParams & 1) != 0) normalizedSyncParams++;
			if (TextLength % normalizedSyncParams != 0)
				throw new System.ArgumentOutOfRangeException(nameof(syncParamsSize), syncParamsSize,
					"The installer supports only sync sizes that divide the 128 character slots exactly: 2, 4, 8 or 16");
			SyncParamsSize = normalizedSyncParams;
			UseWriteDefaultsOn = useWriteDefaultsOn;
			PointerCount = (TextLength + SyncParamsSize - 1) / SyncParamsSize;
			int slots = System.Math.Max(SyncParamsSize, System.Math.Min(TextLength, slotCount));
			SlotCount = slots - (slots % SyncParamsSize);
			InstallAdvancedMenu = installAdvancedMenu;
			InstallBoardControls = installBoardControls && sdk == KatSDK.VRChat;
			AttachmentPoint = attachmentPoint;
			Config = config;
			if (config != null) {
				if (config.HeadAnchorOffset.HasValue) HeadAnchorOffset = config.HeadAnchorOffset.Value;
				if (config.ChestAnchorOffset.HasValue) ChestAnchorOffset = config.ChestAnchorOffset.Value;
				if (config.RightHeadAnchorOffset.HasValue) RightHeadAnchorOffset = config.RightHeadAnchorOffset.Value;
				if (config.TopHeadAnchorOffset.HasValue) TopHeadAnchorOffset = config.TopHeadAnchorOffset.Value;
			}
			if (headAnchorOffset.HasValue) HeadAnchorOffset = headAnchorOffset.Value;
			if (chestAnchorOffset.HasValue) ChestAnchorOffset = chestAnchorOffset.Value;
		}

		public int GetPointerCount()
		{
			return (TextLength + SyncParamsSize - 1) / SyncParamsSize;
		}

		public int GetGroupCount()
		{
			return SlotCount / SyncParamsSize;
		}

		public int GetPageSyncCount()
		{
			return PageFormat == PageFormat3 ? SyncParamsSize : SyncParamsSize / 2;
		}

		public int GetPageCellCount(int slots)
		{
			return PageFormat == PageFormat3 ? slots : slots / 2;
		}

		public int GetMaxPage()
		{
			return PageFormat == PageFormat3 ? MaxPage3 : MaxPage2;
		}
	}

	public static class KatAttachmentPoint
	{
		public const int None = 0;
		public const int Head = 1;
		public const int Chest = 2;
	}

	public static class KatSDK
	{
		public const int VRChat = 0;
		public const int ChilloutVR = 1;
	}
}

#endif
