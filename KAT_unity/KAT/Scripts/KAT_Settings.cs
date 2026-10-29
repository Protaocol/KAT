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

		public readonly int KeyboardKeyBackspace = 97;
		public readonly int KeyboardKeyClear = 96;
		public readonly int KeyboardKeyShiftL = 98;
		public readonly int KeyboardKeyShiftR = 99;
		public readonly float KeyboardBackspaceMode = 97f / 127f;

		public readonly int KeyboardKeysCount = 96;

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
		public readonly bool InstallStyleSpeedControls;
		public readonly int PointerAltSyncOffset = 100;
		public readonly bool InstallKeyboard = true;
		public readonly bool UseWriteDefaultsOn;
		public readonly KatConfig Config;

		public readonly string GeneratedOutputFolderName = "KAT_GeneratedOutput";

		public readonly string TextAttachmentPointName = "KAT_Text_AttachmentPoint";
		public readonly string KeyboardAttachmentPointName = "KAT_Keyboard_AttachmentPoint";
		public readonly int AttachmentPoint = KatAttachmentPoint.Chest;

		public KatSettings(int sdk = KatSDK.VRChat, bool installKeyboard = true, int attachmentPoint = KatAttachmentPoint.Chest, int syncParamsSize = 4,
			Vector3? headAnchorOffset = null, Vector3? chestAnchorOffset = null, bool useWriteDefaultsOn = false, int slotCount = 128, bool installStyleSpeedControls = true, KatConfig config = null, int pageFormat = PageFormat2)
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
			InstallStyleSpeedControls = installStyleSpeedControls;
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
