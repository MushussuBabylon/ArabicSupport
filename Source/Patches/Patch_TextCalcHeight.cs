using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Patches Text.CalcHeight so that it measures the height of the
    /// *already wrapped* text, matching what Widgets.Label will draw.
    /// Without this, every scroll view, dialog, and list row would be
    /// sized based on RimWorld's default (broken for Arabic) wrapping,
    /// causing overflow or huge gaps.
    /// </summary>
    [HarmonyPatch(typeof(Text), nameof(Text.CalcHeight), new[] { typeof(string), typeof(float) })]
    public static class Patch_TextCalcHeight
    {
        public static void Prefix(ref string text, float width)
        {
            if (string.IsNullOrEmpty(text) || !ArabicDetector.ContainsArabic(text))
                return;

            // Wrap the text exactly as it will be drawn, then let the
            // original CalcHeight measure the pre-wrapped lines.
            text = FullPipeline.Process(text, width, Text.Font);
        }
    }
}
