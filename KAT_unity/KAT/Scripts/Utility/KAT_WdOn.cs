/*
Copyright 2023 KillFrenzy / Evan Tran
Copyright 2026 Protaocol

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

using KillFrenzy.AvatarTextTools.Settings;

namespace KillFrenzy.AvatarTextTools.Utility
{
    /*
    WD-On persistence implementation alongside KAT's WD-Off material persistence.
    
    The WD-Off controller uses the material properties themselves as its persistence cells:
    each pointer state writes one slot and WD-Off lets that slot remain at its
    last value after the pointer moves away.
    
    WD-On cannot use that implicit persistence. This implementation therefore
    moves the RAM into local Animated Animator Parameters (AAPs). Character and
    page persistence each use ONE dedicated WD-On layer that owns the complete
    RAM/output set for that path. This is because with multiple WD-On override
    layers, disjoint property ownership would otherwise cause higher layers to
    write defaults into cells owned by lower layers.
    
    Every normal state uses a Direct Blend Tree with Normalized Blend Values
    disabled. Every child has KAT_WDOnAlwaysOne as its direct weight. Normally
    each child is a self-feedback 1D tree driven by its local AAP; the cells
    being written in the active pointer group are replaced with separate 1D
    trees driven by the existing shared KAT sync parameters.
    
    The local memory parameters are not VRCExpressionParameters and therefore
    are not part of KAT's synced parameter budget.
    */
    public static class KatWdOnPersistence
    {
        public const string LayerPrefix = "KAT_WDOn_";
        private const string ClipPrefix = "KAT_WDOn_";
        private const string TreePrefix = "KAT_WDOn_";

        public static bool Install(
            AnimatorController controller,
            KatSettings settings,
            KatAnimations animations
        )
        {
            if (settings == null || controller == null || animations == null)
            {
                Debug.LogError("KAT WD-On: controller, settings, and animations are required.");
                return false;
            }

            if (!IsSupportedSyncSize(settings.SyncParamsSize))
            {
                Debug.LogError(
                    "KAT WD-On: SyncParamsSize=" + settings.SyncParamsSize +
                    " is unsupported. Use 2, 4, 8, or 16 (normalized), which divide the 128 character slots exactly."
                );
                return false;
            }

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath))
            {
                Debug.LogError("KAT WD-On: controller must be an asset before installing WD-On persistence.");
                return false;
            }

            // Local AAP memory. These are NOT VRCExpressionParameters and won't yeet your parameter budget.
            for (int i = 0; i < settings.SlotCount; i++)
                AddFloatParameterIfMissing(controller, settings.ParamTextWdOnMemoryCharPrefix + i);

            // One memory cell per PAIR of slots (format 2) or per SLOT (format 3), matching the _Page<index> the animator writes.
            int pageCount = settings.GetPageCellCount(settings.SlotCount);
            for (int i = 0; i < pageCount; i++)
                AddFloatParameterIfMissing(controller, settings.ParamTextWdOnMemoryPagePrefix + i);

            // Direct Blend Trees need a constant child weight. This parameter is local, default 1, and is never modified by KAT.
            AddFloatParameterIfMissing(controller, settings.ParamTextWdOnAlwaysOne, 1f);

            AnimationClip[] charStart = new AnimationClip[settings.SlotCount];
            AnimationClip[] charEnd = new AnimationClip[settings.SlotCount];
            AnimationClip[] pageStart = new AnimationClip[pageCount];
            AnimationClip[] pageEnd = new AnimationClip[pageCount];

            for (int i = 0; i < settings.SlotCount; i++)
            {
                string memoryParam = settings.ParamTextWdOnMemoryCharPrefix + i;
                charStart[i] = CloneWithMemoryCurve(
                    animations.charsStart[i],
                    controllerPath,
                    ClipPrefix + "CharStart" + i,
                    memoryParam,
                    -1f
                );
                charEnd[i] = CloneWithMemoryCurve(
                    animations.charsEnd[i],
                    controllerPath,
                    ClipPrefix + "CharEnd" + i,
                    memoryParam,
                    1f
                );
            }

            for (int i = 0; i < pageCount; i++)
            {
                string memoryParam = settings.ParamTextWdOnMemoryPagePrefix + i;
                pageStart[i] = CloneWithMemoryCurve(
                    animations.pageStart[i],
                    controllerPath,
                    ClipPrefix + "PageStart" + i,
                    memoryParam,
                    -1f
                );
                pageEnd[i] = CloneWithMemoryCurve(
                    animations.pageEnd[i],
                    controllerPath,
                    ClipPrefix + "PageEnd" + i,
                    memoryParam,
                    1f
                );
            }

            // There is exactly one WD-On character layer and one WD-On page layer.
            // Therefore the complete clear clip is scoped to exactly one layer and cannot wipe properties belonging to another persistence layer.
            AnimationClip charClear = CloneWithMemoryCurves(
                animations.clear,
                controllerPath,
                ClipPrefix + "CharClear",
                settings.ParamTextWdOnMemoryCharPrefix,
                settings.SlotCount,
                0f
            );
            AnimationClip pageClear = CloneWithMemoryCurves(
                animations.pageClear,
                controllerPath,
                ClipPrefix + "PageClear",
                settings.ParamTextWdOnMemoryPagePrefix,
                pageCount,
                0f
            );

            controller.AddLayer(CreatePersistentCharLayer(
                controller,
                settings,
                charStart,
                charEnd,
                charClear
            ));

            controller.AddLayer(CreatePersistentPageLayer(
                controller,
                settings,
                pageStart,
                pageEnd,
                pageClear
            ));

            AssetDatabase.SaveAssets();
            return true;
        }

        private static AnimatorControllerLayer CreatePersistentCharLayer(
            AnimatorController controller,
            KatSettings settings,
            AnimationClip[] starts,
            AnimationClip[] ends,
            AnimationClip clearClip
        )
        {
            AnimatorControllerLayer layer = MakeLayer(controller, LayerPrefix + "CharPersistence");
            AnimatorStateMachine sm = layer.stateMachine;

            int charCount = settings.SlotCount;

            BlendTree[] selfTrees = new BlendTree[charCount];
            BlendTree[] writeTrees = new BlendTree[charCount];
            for (int cellIndex = 0; cellIndex < charCount; cellIndex++)
            {
                string syncParam = settings.ParamTextSyncPrefix + (cellIndex % settings.SyncParamsSize);
                selfTrees[cellIndex] = Create1DTree(
                    controller,
                    TreePrefix + "CharSelfTree" + cellIndex,
                    settings.ParamTextWdOnMemoryCharPrefix + cellIndex,
                    starts[cellIndex],
                    ends[cellIndex]
                );
                writeTrees[cellIndex] = Create1DTree(
                    controller,
                    TreePrefix + "CharWriteTree" + cellIndex,
                    syncParam,
                    starts[cellIndex],
                    ends[cellIndex]
                );
            }

            // Pointer 0: memory decoder only; no new write.
            AnimatorState idle = KatAnimatorInstaller.CreateState(
                sm,
                "Idle / Remember",
                new Vector3(300f, 250f, 0f),
                CreateDirectMemoryTree(
                    controller,
                    TreePrefix + "CharIdleTree",
                    selfTrees,
                    settings.ParamTextWdOnAlwaysOne
                )
            );
            idle.writeDefaultValues = true;
            sm.defaultState = idle;

            int groups = settings.GetGroupCount();
            for (int group = 0; group < groups; group++)
            {
                BlendTree pointerTree = CreateDirectMemoryTree(
                    controller,
                    TreePrefix + "CharPointerTree_" + group,
                    selfTrees,
                    settings.ParamTextWdOnAlwaysOne
                );

                for (int logicId = 0; logicId < settings.SyncParamsSize; logicId++)
                {
                    int cellIndex = settings.SyncParamsSize * group + logicId;
                    ReplaceMotion(pointerTree, cellIndex, writeTrees[cellIndex], settings.ParamTextWdOnAlwaysOne);
                }

                AnimatorState state = KatAnimatorInstaller.CreateState(
                    sm,
                    "Pointer" + (group + 1) + " Chars",
                    new Vector3(700f, 100f + 45f * group, 0f),
                    pointerTree
                );
                state.writeDefaultValues = true;
                AddAnyStateExactTransition(sm, state, group + 1, settings.ParamTextPointer);
            }

            if (settings.InstallKeyboard && settings.PointerAltSyncOffset != 0)
            {
                for (int charIndex = 0; charIndex < charCount; charIndex++)
                {
                    BlendTree alternateTree = CreateDirectMemoryTree(
                        controller,
                        TreePrefix + "CharAlternateTree_" + charIndex,
                        selfTrees,
                        settings.ParamTextWdOnAlwaysOne
                    );
                    ReplaceMotion(
                        alternateTree,
                        charIndex,
                        writeTrees[charIndex],
                        settings.ParamTextWdOnAlwaysOne
                    );

                    int altPointer = settings.PointerAltSyncOffset + charIndex + 1;
                    AnimatorState state = KatAnimatorInstaller.CreateState(
                        sm,
                        "Alternate " + altPointer + " Char" + charIndex,
                        new Vector3(1050f, 100f + 18f * charIndex, 0f),
                        alternateTree
                    );
                    state.writeDefaultValues = true;
                    AddAlternateHoldTransition(sm, idle, altPointer, settings.ParamTextSyncPrefix + (charIndex % settings.SyncParamsSize), settings.KeyboardBackspaceMode, settings);
                    AddAnyStateExactTransition(sm, state, altPointer, settings.ParamTextPointer);
                }

                AddAnyStateEqualsTransition(sm, idle, settings.PointerAltSyncOffset, settings.ParamTextPointer);
            }

            // Clear explicitly zeroes all 128 material outputs and all 128 memory AAPs.
            AnimatorState clear = KatAnimatorInstaller.CreateState(
                sm,
                "Clear Text",
                new Vector3(700f, 100f + 45f * groups, 0f),
                CreateDirectClipTree(controller, TreePrefix + "CharClearTree", clearClip, settings.ParamTextWdOnAlwaysOne)
            );
            clear.writeDefaultValues = true;
            AddAnyStateExactTransition(sm, clear, 255, settings.ParamTextPointer);

            // Pointer 0 always returns to the decoder state so stored RAM remains visible.
            AddAnyStateEqualsTransition(sm, idle, 0, settings.ParamTextPointer);

            // Recover invalid pointers rather than leaving the controller stuck in a write/clear state. 255 is excluded because it has the dedicated Clear state.
            AddInvalidPointerRecoveryTransitions(sm, idle, settings, settings.InstallKeyboard && settings.PointerAltSyncOffset != 0);

            #if CVR_CCK_EXISTS
                if (settings.SDK == KatSDK.ChilloutVR)
                    layer.avatarMask = animations.avatarMask;
            #endif

            return layer;
        }

        private static AnimatorControllerLayer CreatePersistentPageLayer(
            AnimatorController controller,
            KatSettings settings,
            AnimationClip[] starts,
            AnimationClip[] ends,
            AnimationClip clearClip
        )
        {
            AnimatorControllerLayer layer = MakeLayer(controller, LayerPrefix + "PagePersistence");
            AnimatorStateMachine sm = layer.stateMachine;
            int pageCount = settings.GetPageCellCount(settings.SlotCount);

            // Page sync floats per pointer group: SyncParamsSize / 2 in format 2, SyncParamsSize in format 3.
            int stride = settings.GetPageSyncCount();
            BlendTree[] selfTrees = new BlendTree[pageCount];
            BlendTree[] writeTrees = new BlendTree[pageCount];
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                string syncParam = settings.ParamTextPageSyncPrefix + (pageIndex % stride);
                selfTrees[pageIndex] = Create1DTree(
                    controller,
                    TreePrefix + "PageSelfTree" + pageIndex,
                    settings.ParamTextWdOnMemoryPagePrefix + pageIndex,
                    starts[pageIndex],
                    ends[pageIndex]
                );
                writeTrees[pageIndex] = Create1DTree(
                    controller,
                    TreePrefix + "PageWriteTree" + pageIndex,
                    syncParam,
                    starts[pageIndex],
                    ends[pageIndex]
                );
            }

            AnimatorState idle = KatAnimatorInstaller.CreateState(
                sm,
                "Idle / Remember",
                new Vector3(300f, 250f, 0f),
                CreateDirectMemoryTree(
                    controller,
                    TreePrefix + "PageIdleTree",
                    selfTrees,
                    settings.ParamTextWdOnAlwaysOne
                )
            );
            idle.writeDefaultValues = true;
            sm.defaultState = idle;

            // Each pointer group writes `stride` page cells in one state (S/2 in format 2, S in format 3).
            int groups = settings.GetGroupCount();
            for (int group = 0; group < groups; group++)
            {
                BlendTree pointerTree = CreateDirectMemoryTree(
                    controller,
                    TreePrefix + "PagePointerTree_" + group,
                    selfTrees,
                    settings.ParamTextWdOnAlwaysOne
                );

                for (int logicId = 0; logicId < stride; logicId++)
                {
                    int pageIndex = stride * group + logicId;
                    ReplaceMotion(pointerTree, pageIndex, writeTrees[pageIndex], settings.ParamTextWdOnAlwaysOne);
                }

                AnimatorState state = KatAnimatorInstaller.CreateState(
                    sm,
                    "Pointer" + (group + 1) + " Pages",
                    new Vector3(700f, 100f + 45f * group, 0f),
                    pointerTree
                );
                state.writeDefaultValues = true;
                AddAnyStateExactTransition(sm, state, group + 1, settings.ParamTextPointer);
            }

            bool keyboardAlt = settings.InstallKeyboard && settings.PointerAltSyncOffset != 0;
            if (keyboardAlt)
            {
                // The in-game keyboard writes one slot at a time, so the page cell of that slot (a pair of slots in format 2) needs alternate pointer states too.
                bool cellPerSlot = settings.PageFormat == KatSettings.PageFormat3;
                for (int slot = 0; slot < settings.SlotCount; slot++)
                {
                    int pageIndex = cellPerSlot ? slot : slot / 2;
                    if (pageIndex >= pageCount) continue;

                    BlendTree alternateTree = CreateDirectMemoryTree(
                        controller,
                        TreePrefix + "PageAlternateTree_" + slot,
                        selfTrees,
                        settings.ParamTextWdOnAlwaysOne
                    );
                    ReplaceMotion(alternateTree, pageIndex, writeTrees[pageIndex], settings.ParamTextWdOnAlwaysOne);

                    int altPointer = settings.PointerAltSyncOffset + slot + 1;
                    AnimatorState state = KatAnimatorInstaller.CreateState(
                        sm,
                        "Alternate " + altPointer + " Page" + pageIndex,
                        new Vector3(1050f, 100f + 18f * slot, 0f),
                        alternateTree
                    );
                    state.writeDefaultValues = true;
                    AddAlternateHoldTransition(sm, idle, altPointer, settings.ParamTextPageSyncPrefix + (pageIndex % stride), settings.KeyboardPageHold, settings);
                    AddAnyStateExactTransition(sm, state, altPointer, settings.ParamTextPointer);
                }

                AddAnyStateEqualsTransition(sm, idle, settings.PointerAltSyncOffset, settings.ParamTextPointer);
            }

            AnimatorState clear = KatAnimatorInstaller.CreateState(
                sm,
                "Clear Pages",
                new Vector3(700f, 100f + 45f * groups, 0f),
                CreateDirectClipTree(controller, TreePrefix + "PageClearTree", clearClip, settings.ParamTextWdOnAlwaysOne)
            );
            clear.writeDefaultValues = true;
            AddAnyStateExactTransition(sm, clear, 255, settings.ParamTextPointer);

            AddAnyStateEqualsTransition(sm, idle, 0, settings.ParamTextPointer);
            AddInvalidPointerRecoveryTransitions(sm, idle, settings, keyboardAlt);

            return layer;
        }

        private static void AddInvalidPointerRecoveryTransitions(
            AnimatorStateMachine sm,
            AnimatorState idle,
            KatSettings settings,
            bool allowAlternatePointers
        )
        {
            // Valid values are 0, 1..groupCount, 255, and optionally the alternate character-pointer range [offset+1, offset+128]. Any other value returns to idle. The page layer allows them too, so the in-game keyboard can set a slot's page.
            int groups = settings.GetGroupCount();
            int altEnd = settings.PointerAltSyncOffset + settings.SlotCount;

            AnimatorStateTransition negative = sm.AddAnyStateTransition(idle);
            negative.duration = 0f;
            negative.canTransitionToSelf = false;
            negative.AddCondition(AnimatorConditionMode.Less, 0f, settings.ParamTextPointer);

            AnimatorStateTransition highOrGap = sm.AddAnyStateTransition(idle);
            highOrGap.duration = 0f;
            highOrGap.canTransitionToSelf = false;
            highOrGap.AddCondition(AnimatorConditionMode.Greater, groups, settings.ParamTextPointer);

            if (allowAlternatePointers)
            {
                highOrGap.AddCondition(AnimatorConditionMode.Less, settings.PointerAltSyncOffset, settings.ParamTextPointer);

                AnimatorStateTransition aboveAlt = sm.AddAnyStateTransition(idle);
                aboveAlt.duration = 0f;
                aboveAlt.canTransitionToSelf = false;
                aboveAlt.AddCondition(AnimatorConditionMode.Greater, altEnd, settings.ParamTextPointer);
                aboveAlt.AddCondition(AnimatorConditionMode.NotEqual, 255, settings.ParamTextPointer);
            }
            else
            {
                highOrGap.AddCondition(AnimatorConditionMode.NotEqual, 255, settings.ParamTextPointer);
            }
        }

        private static bool IsSupportedSyncSize(int size)
        {
            return size == 2 || size == 4 || size == 8 || size == 16;
        }

        private static BlendTree CreateDirectMemoryTree(
            AnimatorController controller,
            string name,
            BlendTree[] selfTrees,
            string alwaysOneParam
        )
        {
            BlendTree direct = new BlendTree();
            direct.name = name;
            direct.blendType = BlendTreeType.Direct;
            AssetDatabase.AddObjectToAsset(direct, AssetDatabase.GetAssetPath(controller));
            SetNormalizedBlendValues(direct, false);

            for (int i = 0; i < selfTrees.Length; i++)
                AddDirectChild(direct, selfTrees[i], alwaysOneParam);

            return direct;
        }

        private static BlendTree CreateDirectClipTree(AnimatorController controller, string name, AnimationClip clip, string alwaysOneParam)
        {
            BlendTree direct = new BlendTree();
            direct.name = name;
            direct.blendType = BlendTreeType.Direct;
            AssetDatabase.AddObjectToAsset(direct, AssetDatabase.GetAssetPath(controller));
            SetNormalizedBlendValues(direct, false);
            AddDirectChild(direct, clip, alwaysOneParam);
            return direct;
        }

        private static void ReplaceMotion(BlendTree direct, int index, Motion motion, string alwaysOneParam)
        {
            ChildMotion[] children = direct.children;
            if (index < 0 || index >= children.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            children[index].motion = motion;
            children[index].directBlendParameter = alwaysOneParam;
            children[index].timeScale = 1f;
            direct.children = children;
        }

        private static void AddDirectChild(BlendTree direct, Motion motion, string weightParameter)
        {
            direct.AddChild(motion, 1f);
            ChildMotion[] children = direct.children;
            int index = children.Length - 1;
            children[index].directBlendParameter = weightParameter;
            children[index].timeScale = 1f;
            direct.children = children;
        }

        private static AnimatorControllerLayer MakeLayer(AnimatorController controller, string name)
        {
            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine()
            };
            layer.stateMachine.name = name;
            AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));
            return layer;
        }

        private static BlendTree Create1DTree(
            AnimatorController controller,
            string name,
            string parameter,
            AnimationClip start,
            AnimationClip end
        )
        {
            BlendTree tree = new BlendTree
            {
                name = name,
                blendParameter = parameter,
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false
            };
            tree.AddChild(start, -1f);
            tree.AddChild(end, 1f);
            AssetDatabase.AddObjectToAsset(tree, AssetDatabase.GetAssetPath(controller));
            return tree;
        }

        private static AnimationClip CloneWithMemoryCurve(
            AnimationClip source,
            string controllerPath,
            string name,
            string memoryParameter,
            float value
        )
        {
            AnimationClip clip = new AnimationClip();
            EditorUtility.CopySerialized(source, clip);
            clip.name = name;
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                "",
                typeof(Animator),
                memoryParameter
            );
            AnimationCurve curve = AnimationCurve.Constant(0f, 1f, value);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            AssetDatabase.AddObjectToAsset(clip, controllerPath);
            return clip;
        }

        private static AnimationClip CloneWithMemoryCurves(
            AnimationClip source,
            string controllerPath,
            string name,
            string memoryPrefix,
            int count,
            float value
        )
        {
            AnimationClip clip = new AnimationClip();
            EditorUtility.CopySerialized(source, clip);
            clip.name = name;
            for (int i = 0; i < count; i++)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    "",
                    typeof(Animator),
                    memoryPrefix + i
                );
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, value));
            }
            AssetDatabase.AddObjectToAsset(clip, controllerPath);
            return clip;
        }

        // The in-game keyboard's Backspace puts a "hold" value on a sync channel to mean "don't write this slot". With the pointer on that slot and the
        // channel at the hold value, stay on the memory decoder instead of writing.
        private static void AddAlternateHoldTransition(
            AnimatorStateMachine sm,
            AnimatorState idle,
            int altPointer,
            string syncParam,
            float holdValue,
            KatSettings settings
        )
        {
            AnimatorStateTransition transition = sm.AddAnyStateTransition(idle);
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.Equals, altPointer, settings.ParamTextPointer);
            transition.AddCondition(AnimatorConditionMode.Greater, holdValue - 0.005f, syncParam);
            transition.AddCondition(AnimatorConditionMode.Less, holdValue + 0.005f, syncParam);
        }

        private static void AddAnyStateExactTransition(
            AnimatorStateMachine sm,
            AnimatorState destination,
            int value,
            string parameter
        )
        {
            AddAnyStateEqualsTransition(sm, destination, value, parameter);
        }

        private static void AddAnyStateEqualsTransition(
            AnimatorStateMachine sm,
            AnimatorState destination,
            int value,
            string parameter
        )
        {
            AnimatorStateTransition transition = sm.AddAnyStateTransition(destination);
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
        }

        private static void AddFloatParameterIfMissing(AnimatorController controller, string name, float defaultValue = 0f)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != name) continue;
                if (parameters[i].type != AnimatorControllerParameterType.Float)
                {
                    throw new InvalidOperationException(
                        "KAT WD-On parameter name collision: '" + name + "' exists but is not a Float."
                    );
                }

                if (!Mathf.Approximately(parameters[i].defaultFloat, defaultValue))
                {
                    parameters[i].defaultFloat = defaultValue;
                    controller.parameters = parameters;
                }
                return;
            }

            AnimatorControllerParameter added = new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            };
            controller.AddParameter(added);
        }

        private static void SetNormalizedBlendValues(BlendTree tree, bool enabled)
        {
            SerializedObject serialized = new SerializedObject(tree);
            SerializedProperty property = serialized.FindProperty("m_NormalizedBlendValues");
            if (property == null)
            {
                throw new InvalidOperationException(
                    "KAT WD-On could not find Unity's m_NormalizedBlendValues field on a Direct Blend Tree."
                );
            }
            property.boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

#endif
