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
    /// That matters for *how* wrapping has to work: word[0] here is whatever
    /// ends up drawn first (leftmost) if nothing wraps, which for an RTL
    /// paragraph is actually the LAST word in reading order, not the first.
    ///
    /// Wrapping that left-to-right the ordinary way (accumulate words in
    /// array order, cut when a line is full) would put the END of the
    /// sentence on line 1 and push the opening words to a later line —
    /// correct for single-line text, silently scrambled for anything long
    /// enough to actually wrap. That's why short labels looked fine and
    /// longer ones came out in the wrong order/line.
    ///
    /// The fix: scan for line breaks from the END of the word array
    /// backward instead of from the start. Each line is still built from a
    /// contiguous, unmodified slice of the original array — nothing about
    /// word order within a line is touched, since that was already correct
    /// from the offline pass — this only changes where the cuts land and
    /// makes sure the resulting lines come out top-to-bottom in reading
    /// order.
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

            // maxWidth <= 0 means "no wrapping" — return as a single line
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
                string wordMeasurable = words[i].Length == 0
                    ? string.Empty
                    : TextMeasurer.RestorePlaceholders(words[i], placeholders);

                wordWidths[i] = string.IsNullOrEmpty(wordMeasurable)
                    ? 0f
                    : Text.CalcSize(wordMeasurable).x;
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

                    if (candidate > maxWidth)
                        break;

                    width = candidate;
                    segStart = i2;
                    i2--;
                }

                lines.Add(JoinRange(words, segStart, segEnd));
            }

            if (lines.Count == 0)
                lines.Add(protectedResult.Text);

            return lines;
        }

        /// <summary>
        /// Text.CalcSize(" ") can return 0 on backends that trim pure-
        /// whitespace content during layout measurement. If that happens,
        /// every word-gap in the width budget becomes "free," and the
        /// greedy wrap below packs extra words onto a line before the
        /// pixel check ever trips — silently reintroducing the exact
        /// overflow bug this mod exists to fix.
        ///
        /// Measuring the width *added* by a space between two real
        /// characters sidesteps that trimming, since the string as a whole
        /// is no longer pure whitespace. Falls back to the direct
        /// measurement, and then to a small nonzero constant, only if both
        /// come back non-positive.
        /// </summary>
        private static float MeasureSpaceWidth()
        {
            float indirect = Text.CalcSize("i i").x - 2f * Text.CalcSize("i").x;
            if (indirect > 0.01f)
                return indirect;

            float direct = Text.CalcSize(" ").x;
            if (direct > 0.01f)
                return direct;

            return 1f;
        }

        private static string JoinRange(string[] words, int start, int end)
        {
            if (start == end)
                return words[start];

            var sb = new StringBuilder();
            for (int i = start; i <= end; i++)
            {
                if (i > start)
                    sb.Append(' ');
                sb.Append(words[i]);
            }
            return sb.ToString();
        }

        public static List<string> Wrap(PlaceholderProtector.ProtectedText protectedResult, float maxWidth, GameFont font)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            var result = Wrap(protectedResult, maxWidth);
            Text.Font = previous;
            return result;
        }
    }
}
