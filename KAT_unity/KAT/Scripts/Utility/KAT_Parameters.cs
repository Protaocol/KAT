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

using UnityEditor;
using UnityEngine;

using System.Collections.Generic;

#if VRC_SDK_VRCSDK3
	using ExpressionParameters = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
	using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
#endif

using KillFrenzy.AvatarTextTools.Settings;


namespace KillFrenzy.AvatarTextTools.Utility
{
	public static class KatParametersInstaller
	{
		#if VRC_SDK_VRCSDK3
			public static bool IsKatParameter(string name, KatSettings settings)
			{
				if (string.IsNullOrEmpty(name)) return false;
				return name == settings.ParamTextVisible ||
					name.StartsWith(settings.ParamKeyboardPrefix, System.StringComparison.Ordinal) ||
					name == settings.ParamTextPointer ||
					name.StartsWith(settings.ParamTextSyncPrefix, System.StringComparison.Ordinal) ||
					name.StartsWith(settings.ParamTextPageSyncPrefix, System.StringComparison.Ordinal) ||
					name == settings.ParamHeadAnchor ||
					name == settings.ParamSpeed ||
					name == settings.ParamStyle ||
					name.StartsWith(settings.ParamAdvPrefix, System.StringComparison.Ordinal) ||
					name.StartsWith(settings.ParamBoardPrefix, System.StringComparison.Ordinal);
			}

			private static ExpressionParameter Make(string name, ExpressionParameters.ValueType type, float defaultValue, bool saved, bool synced = true)
			{
				ExpressionParameter parameter = new ExpressionParameter() {
					name = name,
					valueType = type,
					defaultValue = defaultValue,
					saved = saved
				};
				if (!synced) parameter.networkSynced = false;
				return parameter;
			}

			private static List<ExpressionParameter> WithoutKat(ExpressionParameters targetParameters, KatSettings settings)
			{
				List<ExpressionParameter> kept = new List<ExpressionParameter>();
				if (targetParameters.parameters == null) return kept;
				foreach (ExpressionParameter parameter in targetParameters.parameters) {
					if (parameter == null || IsKatParameter(parameter.name, settings)) continue;
					kept.Add(parameter);
				}
				return kept;
			}

			public static bool InstallToParameters(ExpressionParameters targetParameters, KatSettings settings)
			{
				List<ExpressionParameter> parameters = WithoutKat(targetParameters, settings);

				parameters.Add(Make(settings.ParamTextVisible, ExpressionParameters.ValueType.Bool, 0, false));

				if (settings.InstallKeyboard) {
					parameters.Add(Make(settings.ParamKeyboardPrefix, ExpressionParameters.ValueType.Bool, 0, false));
				}

				parameters.Add(Make(settings.ParamTextPointer, ExpressionParameters.ValueType.Int, 0, false));

				for (int i = 0; i < settings.SyncParamsSize; i++) {
					parameters.Add(Make(settings.ParamTextSyncPrefix + i.ToString(), ExpressionParameters.ValueType.Float, 0, false));
				}

				for (int i = 0; i < settings.GetPageSyncCount(); i++) {
					parameters.Add(Make(settings.ParamTextPageSyncPrefix + i.ToString(), ExpressionParameters.ValueType.Float, 0, false));
				}

				parameters.Add(Make(settings.ParamHeadAnchor, ExpressionParameters.ValueType.Float,
					settings.AttachmentPoint == KatAttachmentPoint.Head ? 0.2f : 0f, true));

				parameters.Add(Make(settings.ParamSpeed, ExpressionParameters.ValueType.Float, 0.5f, true, false));
				parameters.Add(Make(settings.ParamStyle, ExpressionParameters.ValueType.Float, 0.5f, true, false));

				if (settings.InstallBoardControls) {
					// A synced int (8 bits), so everyone sees where the board is held or dropped. Not saved, so it starts docked every time.
					parameters.Add(Make(settings.ParamBoardMode, ExpressionParameters.ValueType.Int, 0f, false, true));
				}

				if (settings.InstallAdvancedMenu) {
					// Local only: these use up expression entries but no synced bits.
					parameters.Add(Make(settings.ParamAdvBillboard, ExpressionParameters.ValueType.Int, 0f, true, false));
					parameters.Add(Make(settings.ParamAdvGrow, ExpressionParameters.ValueType.Bool, 1f, true, false));
					parameters.Add(Make(settings.ParamAdvBox, ExpressionParameters.ValueType.Bool, 1f, true, false));
					parameters.Add(Make(settings.ParamAdvShrink, ExpressionParameters.ValueType.Bool, 1f, true, false));
					parameters.Add(Make(settings.ParamAdvBold, ExpressionParameters.ValueType.Bool, 0f, true, false));
					parameters.Add(Make(settings.ParamAdvSize, ExpressionParameters.ValueType.Float, 0.5f, true, false));
				}

				targetParameters.parameters = parameters.ToArray();
				EditorUtility.SetDirty(targetParameters);
				Debug.Log("Installation to parameters completed.");
				return true;
			}

			public static bool RemoveFromParameters(ExpressionParameters targetParameters, KatSettings settings)
			{
				targetParameters.parameters = WithoutKat(targetParameters, settings).ToArray();
				EditorUtility.SetDirty(targetParameters);
				Debug.Log("Removal from parameters completed.");
				return true;
			}
		#endif
	}
}

#endif
