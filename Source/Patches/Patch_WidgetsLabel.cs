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
        public static void Prefix(Rect rect, ref string label)
        {
            if (string.IsNullOrEmpty(label) || !ArabicDetector.ContainsArabic(label))
                return;

            // Re-wrap this label to the Rect's actual pixel width instead of
            // RimWorld's estimated character-count wrap.
            label = FullPipeline.Process(label, rect.width, Text.Font);
        }
    }
}
