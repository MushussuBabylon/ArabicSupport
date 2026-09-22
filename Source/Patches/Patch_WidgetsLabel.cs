using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Uses a Finalizer (not just Postfix) to restore Text.Anchor. A
    /// Postfix is skipped if another mod's earlier patch on this method
    /// throws unhandled — a Finalizer runs regardless.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_WidgetsLabel
    {
        public struct LabelState
        {
            public TextAnchor OriginalAnchor;
            public bool AnchorChanged;
        }

        public static void Prefix(Rect rect, ref string label, out LabelState __state)
        {
            __state = new LabelState { OriginalAnchor = Text.Anchor, AnchorChanged = false };

            if (!UnityData.IsInMainThread) return;

            try
            {
                if (string.IsNullOrEmpty(label) || rect.width <= 0f || !ArabicDetector.ContainsArabic(label))
                    return;

                string processed = FullPipeline.ProcessKnownArabic(label, rect.width, Text.Font);
                if (processed == null) return;

                label = processed;

                if (!processed.Contains("\n")) return;

                switch (Text.Anchor)
                {
                    case TextAnchor.UpperLeft:
                        Text.Anchor = TextAnchor.UpperRight;
                        __state.AnchorChanged = true;
                        break;
                    case TextAnchor.MiddleLeft:
                        Text.Anchor = TextAnchor.MiddleRight;
                        __state.AnchorChanged = true;
                        break;
                    case TextAnchor.LowerLeft:
                        Text.Anchor = TextAnchor.LowerRight;
                        __state.AnchorChanged = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Widgets.Label failed: {ex}", 102783451);
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(LabelState __state)
        {
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
        }

        [HarmonyPriority(Priority.Last)]
        public static Exception Finalizer(Exception __exception, LabelState __state)
        {
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
            return __exception;
        }
    }
}
