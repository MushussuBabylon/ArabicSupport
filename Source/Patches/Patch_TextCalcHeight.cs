using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Patches Text.CalcHeight so it measures the already-wrapped text,
    /// matching what Widgets.Label will draw. Lets the ORIGINAL
    /// Text.CalcHeight run on the pre-wrapped result rather than bypassing
    /// it, since Verse's own measurement is font/size-specific in ways
    /// GUI.skin.label.CalcHeight is not.
    /// </summary>
    [HarmonyPatch(typeof(Text), nameof(Text.CalcHeight), new[] { typeof(string), typeof(float) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_TextCalcHeight
    {
        public static void Prefix(ref string text, float width)
        {
            if (!UnityData.IsInMainThread) return;

            try
            {
                if (string.IsNullOrEmpty(text) || width <= 0f || !ArabicDetector.ContainsArabic(text))
                    return;

                text = FullPipeline.ProcessKnownArabic(text, width, Text.Font);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Text.CalcHeight failed: {ex}", 102783453);
            }
        }
    }
}
