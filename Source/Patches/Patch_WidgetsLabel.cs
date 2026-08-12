using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
    public static class Patch_WidgetsLabel
    {
        public static void Prefix(Rect rect, ref string label, out TextAnchor __state)
        {
            __state = Text.Anchor;

            if (string.IsNullOrEmpty(label) || !ArabicDetector.ContainsArabic(label))
                return;

            label = FullPipeline.Process(label, rect.width, Text.Font);

            // SMART RTL ALIGNMENT: 
            // Only apply right-alignment to blocks of text that actually wrap.
            // Single-line elements (FloatMenus, stat lists, health bills) rely on 
            // strict X-coordinate layouts and must keep their original anchor.
            if (label.Contains("\n"))
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
