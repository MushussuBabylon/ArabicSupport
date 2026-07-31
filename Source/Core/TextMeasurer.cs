using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    public static class TextMeasurer
    {
        public static float MeasureWidth(string text, List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            string restored = RestorePlaceholders(text, placeholders);
            if (string.IsNullOrEmpty(restored))
                return 0f;

            return Text.CalcSize(restored).x;
        }

        public static float MeasureWidth(string text, List<string> placeholders, GameFont font)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            float width = MeasureWidth(text, placeholders);
            Text.Font = previous;
            return width;
        }

        /// <summary>
        /// Restores placeholder markers to their original text but DOES NOT
        /// strip rich-text tags. This way font size, bold, italic and other
        /// styling that affect pixel width are correctly accounted for during
        /// measurement.
        /// </summary>
        public static string RestorePlaceholders(string text, List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            return PlaceholderProtector.Restore(text, placeholders);
        }
    }
}
