using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Wraps Arabic label text by pixel width before Widgets.Label draws it.
    ///
    /// This Prefix runs for EVERY label in the game, so the order of checks
    /// matters: the cheap, thread-safe rejections come first, and the
    /// main-thread check only runs for labels that really contain Arabic.
    /// There is no Finalizer: only the label string is changed, so there is
    /// nothing to restore afterwards.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_WidgetsLabel
    {
        public static void Prefix(Rect rect, ref string label)
        {
            if (string.IsNullOrEmpty(label) || rect.width <= 0f || !ArabicDetector.ContainsArabic(label))
                return;

            if (!UnityData.IsInMainThread) return;

            try
            {
                string processed = FullPipeline.ProcessKnownArabic(label, rect.width, Text.Font);
                if (processed != null)
                    label = processed;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Widgets.Label failed: {ex}", 102783451);
            }
        }
    }
}
