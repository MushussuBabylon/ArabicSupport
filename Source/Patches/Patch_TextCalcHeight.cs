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
    ///
    /// Runs for every Text.CalcHeight call in the game, so the cheap,
    /// thread-safe rejections come first and the main-thread check only
    /// runs for text that really contains Arabic.
    /// </summary>
    [HarmonyPatch(typeof(Text), nameof(Text.CalcHeight), new[] { typeof(string), typeof(float) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_TextCalcHeight
    {
        public static void Prefix(ref string text, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f || !ArabicDetector.ContainsArabic(text))
                return;

            if (!UnityData.IsInMainThread) return;

            try
            {
                // Text.WordWrap is off: the game wants ONE line here (buttons,
                // architect menu tabs...). Wrapping it anyway made a second
                // line that spilled over the text around it. Width 0 = no wrap.
                text = FullPipeline.ProcessKnownArabic(text, Text.WordWrap ? width : 0f, Text.Font);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Text.CalcHeight failed: {ex}", 102783453);
            }
        }
    }
}
