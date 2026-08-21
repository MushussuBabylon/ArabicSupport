using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Patches Text.CalcHeight so that it measures the height of the
    /// already-wrapped text, matching what Widgets.Label will draw.
    /// Without this, every scroll view, dialog, and list row would be
    /// sized based on RimWorld's default (broken for Arabic) wrapping,
    /// causing overflow or huge gaps.
    ///
    /// IMPORTANT: this mutates `text` via ref and lets the ORIGINAL
    /// Text.CalcHeight run on the pre-wrapped result, rather than
    /// bypassing it with GUI.skin.label.CalcHeight(...). GUI.skin.label
    /// is Unity's default IMGUI style, not the font-specific style Verse
    /// actually draws with, so replacing the original method with it can
    /// return the wrong height for non-default fonts/sizes. Letting the
    /// real Text.CalcHeight run keeps this consistent with however Verse
    /// itself measures text.
    /// </summary>
    [HarmonyPatch(typeof(Text), nameof(Text.CalcHeight), new[] { typeof(string), typeof(float) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_TextCalcHeight
    {
        public static void Prefix(ref string text, float width)
        {
            // Unity/Verse text APIs are not safe to touch off the main
            // thread. If another mod (e.g. Map Preview) is generating a
            // preview on a background thread and happens to call into
            // Text.CalcHeight, skip processing rather than risk a crash.
            if (!UnityData.IsInMainThread) return;

            try
            {
                if (string.IsNullOrEmpty(text) || width <= 0f || !ArabicDetector.ContainsArabic(text))
                    return;

                // Wrap the text exactly as it will be drawn, then let the
                // original CalcHeight measure the pre-wrapped lines.
                text = FullPipeline.Process(text, width, Text.Font);
            }
            catch (Exception ex)
            {
                // Never let a bad label crash the whole Harmony patch
                // chain for this method — just skip our processing for
                // this one call and let RimWorld's default height stand.
                Log.ErrorOnce($"[Arabic Support] Text.CalcHeight failed: {ex}", 102783453);
            }
        }
    }
}
