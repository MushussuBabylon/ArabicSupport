using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(GUIContent) })]
    public static class Patch_WidgetsLabelGUIContent
    {
        // Changed from private struct to public struct to match method accessibility
        public struct LabelState
        {
            public TextAnchor Anchor;
            public string OriginalText;
        }

        public static void Prefix(Rect rect, GUIContent content, out LabelState __state)
        {
            __state = new LabelState { Anchor = Text.Anchor, OriginalText = null };

            if (content == null || string.IsNullOrEmpty(content.text) || !ArabicDetector.ContainsArabic(content.text))
                return;

            __state.OriginalText = content.text;

            content.text = FullPipeline.Process(content.text, rect.width, Text.Font);

            if (content.text.Contains("\n"))
            {
                if (Text.Anchor == TextAnchor.UpperLeft)
                    Text.Anchor = TextAnchor.UpperRight;
                else if (Text.Anchor == TextAnchor.MiddleLeft)
                    Text.Anchor = TextAnchor.MiddleRight;
                else if (Text.Anchor == TextAnchor.LowerLeft)
                    Text.Anchor = TextAnchor.LowerRight;
            }
        }

        public static void Postfix(GUIContent content, LabelState __state)
        {
            Text.Anchor = __state.Anchor;

            if (__state.OriginalText != null && content != null)
            {
                content.text = __state.OriginalText;
            }
        }
    }
}
