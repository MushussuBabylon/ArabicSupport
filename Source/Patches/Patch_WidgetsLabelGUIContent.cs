using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Same approach as Patch_WidgetsLabel: cheap thread-safe rejections
    /// first, main-thread check only for Arabic text. A Finalizer alone
    /// (no Postfix) restores the original label text, since GUIContent is
    /// a shared object that must not keep the wrapped version.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(GUIContent) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_WidgetsLabelGUIContent
    {
        // __state holds the original text if we changed it, otherwise null.
        public static void Prefix(Rect rect, GUIContent content, out string __state)
        {
            __state = null;

            if (content == null || rect.width <= 0f)
                return;

            string originalText = content.text;

            if (string.IsNullOrEmpty(originalText) || !ArabicDetector.ContainsArabic(originalText))
                return;

            if (!UnityData.IsInMainThread) return;

            try
            {
                string processed = FullPipeline.ProcessKnownArabic(originalText, rect.width, Text.Font);

                if (processed == null || processed == originalText) return;

                __state = originalText;
                content.text = processed;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Widgets.Label(GUIContent) failed: {ex}", 102783452);
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static Exception Finalizer(Exception __exception, GUIContent content, string __state)
        {
            if (__state != null && content != null)
                content.text = __state;

            return __exception;
        }
    }
}
