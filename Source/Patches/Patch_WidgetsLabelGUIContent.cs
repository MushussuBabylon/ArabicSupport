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
        public static void Prefix(Rect rect, GUIContent content, out TextAnchor __state)
        {
            __state = Text.Anchor;

            if (content == null || string.IsNullOrEmpty(content.text) || !ArabicDetector.ContainsArabic(content.text))
                return;

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

        public static void Postfix(TextAnchor __state)
        {
            Text.Anchor = __state;
        }
    }
}
