using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
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
            __state = new LabelState
            {
                OriginalAnchor = Text.Anchor,
                OriginalText = null,
                TextChanged = false,
                AnchorChanged = false
            };

            if (!UnityData.IsInMainThread) return;

            try
            {
                if (content == null || string.IsNullOrEmpty(content.text) ||
                    !ArabicDetector.ContainsArabic(content.text) || rect.width <= 0f)
                    return;

                string originalText = content.text;
                string processed = FullPipeline.Process(originalText, rect.width, Text.Font);

                if (processed == null || processed == originalText) return;

                __state.OriginalText = originalText;
                __state.TextChanged = true;
                content.text = processed;

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
                Log.ErrorOnce($"[Arabic Support] Widgets.Label(GUIContent) failed: {ex}", 102783452);
            }
        }

        [HarmonyPriority(Priority.First)]
        public static void Postfix(GUIContent content, LabelState __state)
        {
            if (__state.TextChanged && content != null) content.text = __state.OriginalText;
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
        }

        // Guarantees content.text and Text.Anchor are restored even if a
        // DIFFERENT mod's patch on this same method throws.
        [HarmonyPriority(Priority.First)]
        public static Exception Finalizer(Exception __exception, GUIContent content, LabelState __state)
        {
            if (__state.TextChanged && content != null) content.text = __state.OriginalText;
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
            return __exception;
        }
    }
}
