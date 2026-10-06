using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Same approach as Patch_WidgetsLabel: cheap thread-safe rejections
    /// first, main-thread check only for Arabic text, and a Finalizer alone
    /// (no Postfix) to restore the label text and Text.Anchor.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(GUIContent) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_WidgetsLabelGUIContent
    {
        public struct LabelState
        {
            public TextAnchor OriginalAnchor;
            public string OriginalText;
            public bool TextChanged;
            public bool AnchorChanged;
        }

        public static void Prefix(Rect rect, GUIContent content, out LabelState __state)
        {
            __state = default(LabelState);

            if (content == null || rect.width <= 0f)
                return;

            string originalText = content.text;

            if (string.IsNullOrEmpty(originalText) || !ArabicDetector.ContainsArabic(originalText))
                return;

            if (!UnityData.IsInMainThread) return;

            try
            {
                string processed = FullPipeline.ProcessKnownArabic(originalText, rect.width, Text.Font);

                if (processed == null || processed == originalText) return;

                __state.OriginalText = originalText;
                __state.TextChanged = true;
                content.text = processed;

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
                Log.ErrorOnce($"[Arabic Support] Widgets.Label(GUIContent) failed: {ex}", 102783452);
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static Exception Finalizer(Exception __exception, GUIContent content, LabelState __state)
        {
            if (__state.TextChanged && content != null) content.text = __state.OriginalText;
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
            return __exception;
        }
    }
}
