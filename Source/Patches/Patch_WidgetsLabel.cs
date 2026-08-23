using System;
using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Uses only a Finalizer (not a Postfix) to restore Text.Anchor.
    /// Harmony always runs the Finalizer after Postfix on the success
    /// path, so keeping both meant every successful label draw restored
    /// the anchor twice. The Finalizer alone already covers both the
    /// normal case AND the case where some OTHER mod's patch earlier in
    /// the chain on this same method throws before a Postfix would run —
    /// which is exactly the scenario that motivated using a Finalizer
    /// here (it previously left Text.Anchor permanently corrupted and
    /// produced order-dependent breakage/crashes with mods like Map
    /// Preview).
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_WidgetsLabel
    {
        public struct LabelState
        {
            public TextAnchor OriginalAnchor;
            public bool AnchorChanged;
        }

        public static void Prefix(Rect rect, ref string label, out LabelState __state)
        {
            __state = new LabelState { OriginalAnchor = Text.Anchor, AnchorChanged = false };

            if (!UnityData.IsInMainThread) return;

            try
            {
                if (string.IsNullOrEmpty(label) || !ArabicDetector.ContainsArabic(label) || rect.width <= 0f)
                    return;

                string processed = FullPipeline.Process(label, rect.width, Text.Font);
                if (processed == null) return;

                label = processed;

                // SMART RTL ALIGNMENT: only right-align blocks of text
                // that actually wrap. Single-line elements (FloatMenus,
                // stat lists, health bills) rely on strict X-coordinate
                // layouts and must keep their original anchor.
                if (!processed.Contains("\n")) return;

                switch (Text.Anchor)
                {
                    case TextAnchor.UpperLeft:
                        Text.Anchor = TextAnchor.UpperRight;
                        __state.AnchorChanged = true;
                        break;
                    case TextAnchor.MiddleLeft:
                        Text.Anchor = TextAnchor.MiddleRight;
                        __state.AnchorChanged = true;
                        break;
                    case TextAnchor.LowerLeft:
                        Text.Anchor = TextAnchor.LowerRight;
                        __state.AnchorChanged = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Widgets.Label failed: {ex}", 102783451);
            }
        }

        // Guarantees restoration exactly once, whether the original
        // method (and any other mod's patch on it) succeeded or threw.
        [HarmonyPriority(Priority.Last)]
        public static Exception Finalizer(Exception __exception, LabelState __state)
        {
            if (__state.AnchorChanged) Text.Anchor = __state.OriginalAnchor;
            return __exception;
        }
    }
}
