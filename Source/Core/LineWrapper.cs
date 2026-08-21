using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Greedy word-wrapping based on real pixel width (via TextMeasurer),
    /// replacing RimWorld's default fixed character-count wrap guess, which
    /// under- or over-estimates line breaks for Arabic glyph widths.
    ///
    /// The incoming text has already been reshaped AND bidi-reordered
    /// offline (by the translator's own tooling), as if the whole paragraph
    /// were one unbroken line — this class never reorders anything itself.
    /// word[0] here is whatever ends up drawn first (leftmost) if nothing
    /// wraps, which for an RTL paragraph is actually the LAST word in
    /// reading order, not the first.
    ///
    /// The fix: scan for line breaks from the END of the word array
    /// backward instead of from the start, so the resulting lines come out
    /// top-to-bottom in reading order.
    /// </summary>
    public static class LineWrapper
    {
        public static List<string> Wrap(PlaceholderProtector.ProtectedText protectedResult, float maxWidth)
        {
            var lines = new List<string>();

            if (string.IsNullOrEmpty(protectedResult.Text))
            {
                lines.Add(protectedResult.Text ?? string.Empty);
                return lines;
            }
            if (maxWidth <= 0f)
            {
                lines.Add(protectedResult.Text);
                return lines;
            }

            var placeholders = protectedResult.Placeholders;
            string[] words = protectedResult.Text.Split(' ');
            float spaceWidth = MeasureSpaceWidth();
            float[] wordWidths = new float[words.Length];

            for (int i = 0; i < words.Length; i++)
            {
                string wordMeasurable = words[i].Length == 0 ? string.Empty : TextMeasurer.RestorePlaceholders(words[i], placeholders);
                wordWidths[i] = string.IsNullOrEmpty(wordMeasurable) ? 0f : Text.CalcSize(wordMeasurable).x;
            }

            int i2 = words.Length - 1;
            while (i2 >= 0)
            {
                int segEnd = i2;
                int segStart = i2;
                float width = wordWidths[i2];
                i2--;
                while (i2 >= 0)
                {
                    float candidate = width + spaceWidth + wordWidths[i2];
                    if (candidate > maxWidth) break;
                    width = candidate;
                    segStart = i2;
                    i2--;
                }
                lines.Add(JoinRange(words, segStart, segEnd));
            }
            if (lines.Count == 0) lines.Add(protectedResult.Text);
            return lines;
        }

        // RimWorld's GameFont enum only has a handful of values (Tiny,
        // Small, Medium, ...). Indexing a small fixed array by (int)Text.Font
        // is cheaper than a Dictionary<GameFont, float> lookup. 8 slots gives
        // headroom beyond the 3 commonly-used fonts; anything out of range
        // just falls back to computing fresh, so it can never break.
        private static readonly float[] SpaceWidths = new float[8];
        private static readonly bool[] SpaceWidthsCached = new bool[8];

        /// <summary>
        /// Text.CalcSize(" ") can return 0 on backends that trim pure-
        /// whitespace content during layout measurement. If that happens,
        /// every word-gap in the width budget becomes "free," and the
        /// greedy wrap above packs extra words onto a line before the
        /// pixel check ever trips — silently reintroducing the exact
        /// overflow bug this mod exists to fix.
        ///
        /// Measuring the width *added* by a space between two real
        /// characters sidesteps that trimming. Falls back to the direct
        /// measurement, then to a small nonzero constant, only if both come
        /// back non-positive. Cached per font since the result never
        /// changes for a given font.
        /// </summary>
        private static float MeasureSpaceWidth()
        {
            int fontIndex = (int)Text.Font;
            bool validIndex = fontIndex >= 0 && fontIndex < SpaceWidths.Length;
            if (validIndex && SpaceWidthsCached[fontIndex]) return SpaceWidths[fontIndex];

            float indirect = Text.CalcSize("i i").x - 2f * Text.CalcSize("i").x;
            float result;
            if (indirect > 0.01f)
            {
                result = indirect;
            }
            else
            {
                float direct = Text.CalcSize(" ").x;
                result = direct > 0.01f ? direct : 1f;
            }

            if (validIndex)
            {
                SpaceWidths[fontIndex] = result;
                SpaceWidthsCached[fontIndex] = true;
            }
            return result;
        }

        private static string JoinRange(string[] words, int start, int end)
        {
            if (start == end) return words[start];
            var sb = new StringBuilder();
            for (int i = start; i <= end; i++)
            {
                if (i > start) sb.Append(' ');
                sb.Append(words[i]);
            }
            return sb.ToString();
        }

        public static List<string> Wrap(PlaceholderProtector.ProtectedText protectedResult, float maxWidth, GameFont font)
        {
            // try/finally instead of a bare assignment-after-call: if Wrap()
            // throws mid-measurement, Text.Font must still be put back, or
            // every subsequent label drawn this frame renders in the wrong
            // font size until something else happens to reset it.
            GameFont previous = Text.Font;
            try
            {
                Text.Font = font;
                return Wrap(protectedResult, maxWidth);
            }
            finally
            {
                Text.Font = previous;
            }
        }
    }
}
