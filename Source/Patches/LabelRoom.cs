using System;
using ArabicSupport.Settings;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Gives a one-line Arabic label enough height for the dots and tails that
    /// hang below the baseline.
    ///
    /// The game cuts a label's text off at the bottom edge of the rectangle the
    /// label was given, and many of those rectangles were sized for the game's
    /// own font: a title can be a few pixels shorter than one line of an Arabic
    /// font. The two dots under a final ي are the first thing to go (then the
    /// tails of ر و ق ...).
    ///
    /// The text does not move. It is anchored to the top, middle or bottom of the
    /// rectangle, and the rectangle only grows away from that anchor, so only the
    /// cut-off line moves out. The caller keeps its own rectangle: the change is
    /// made on the copy that is handed to the label.
    /// </summary>
    internal static class LabelRoom
    {
        // The rectangle is grown to one line of the active font plus this
        // fraction of it: room for the rounding of the font's line box.
        private const float Margin = 0.12f;

        private static bool broken;

        public static void Grow(ref Rect rect, string drawnText)
        {
            if (broken)
                return;

            // The "room for dots and tails" switch of the settings window.
            var settings = ArabicSupportMod.Settings;
            if (settings != null && !settings.growLabelHeight)
                return;

            try
            {
                GrowCore(ref rect, drawnText);
            }
            catch (Exception ex)
            {
                // Switched off after the first failure: a label is drawn many
                // times per frame, and the game itself keeps working without this.
                broken = true;
                Log.Warning("[Arabic Support] Label height adjustment turned off: " + ex.Message);
            }
        }

        private static void GrowCore(ref Rect rect, string drawnText)
        {
            // Several lines: the caller sized the box for them (and may want the rest cut off).
            if (string.IsNullOrEmpty(drawnText) || drawnText.IndexOf('\n') >= 0)
                return;

            float line = Text.LineHeightOf(Text.Font);
            if (line <= 0f || rect.height < line * 0.5f)
                return;

            float extra = line + Mathf.Ceil(line * Margin) - rect.height;
            if (extra <= 0f)
                return;

            switch (Text.Anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    rect.height += extra;           // text hangs from the top: grow downwards
                    break;

                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    rect.y -= extra;                // text sits on the bottom: grow upwards
                    rect.height += extra;
                    break;

                default:
                    rect.y -= extra * 0.5f;         // centred: grow both ways
                    rect.height += extra;
                    break;
            }
        }
    }
}
