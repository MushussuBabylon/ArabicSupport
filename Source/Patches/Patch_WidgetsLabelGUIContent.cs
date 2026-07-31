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
        public static void Prefix(Rect rect, GUIContent content)
        {
            if (content == null || string.IsNullOrEmpty(content.text) || !ArabicDetector.ContainsArabic(content.text))
                return;

            // Re-wrap this label to the Rect's actual pixel width instead of
            // RimWorld's estimated character-count wrap.
            content.text = FullPipeline.Process(content.text, rect.width, Text.Font);
        }
    }
}
