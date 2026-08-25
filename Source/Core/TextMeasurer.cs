using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    public static class TextMeasurer
    {
        public static float MeasureWidth(
            string text,
            List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            string restored =
                RestorePlaceholders(
                    text,
                    placeholders
                );

            if (string.IsNullOrEmpty(restored))
                return 0f;

            return Text.CalcSize(restored).x;
        }

        public static float MeasureWidth(
            string text,
            List<string> placeholders,
            GameFont font)
        {
            GameFont previous = Text.Font;

            try
            {
                Text.Font = font;

                return MeasureWidth(
                    text,
                    placeholders
                );
            }
            finally
            {
                Text.Font = previous;
            }
        }

        /// <summary>
        /// Restores visible placeholders for measurement but removes rich-text
        /// tags themselves because markup has no visible pixel width.
        /// </summary>
        public static string RestorePlaceholders(
            string text,
            List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            return PlaceholderProtector.RestoreForMeasurement(
                text,
                placeholders
            );
        }
    }
}
