using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Uses a Finalizer (not a Postfix) to restore Text.Anchor. A Postfix is
    /// skipped if another mod's earlier patch on this method throws
    /// unhandled - a Finalizer runs regardless, so the Finalizer alone covers
    /// every case (a second, Postfix copy would only add work to every label
    /// call in the game).
    ///
    /// This Prefix runs for EVERY label in the game, so the order of checks
    /// matters: the cheap, thread-safe rejections come first, and the
    /// main-thread check only runs for labels that really contain Arabic.
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
            __state = default(LabelState);

            if (string.IsNullOrEmpty(label) || rect.width <= 0f || !ArabicDetector.ContainsArabic(label))
                return;

            if (!UnityData.IsInMainThread) return;

            try
            {
                string processed = FullPipeline.ProcessKnownArabic(label, rect.width, Text.Font);
                if (processed == null) return;

                label = processed;

                if (processed.IndexOf('\n') < 0) return;

                TextAnchor current = Text.Anchor;
                TextAnchor flipped;

                switch (current)
                {
                    case TextAnchor.UpperLeft:
                        flipped = TextAnchor.UpperRight;
                        break;
                    case TextAnchor.MiddleLeft:
                        flipped = TextAnchor.MiddleRight;
                        break;
                    case TextAnchor.LowerLeft:
                        flipped = TextAnchor.LowerRight;
                        break;
                    default:
                        return;
                }

                __state.OriginalAnchor = current;
                __state.AnchorChanged = true;
                Text.Anchor = flipped;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Widgets.Label failed: {ex}", 102783451);
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static Exception Finalizer(Exception __exception, LabelState __state)
        {
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
            return __exception;
        }
    }
}
