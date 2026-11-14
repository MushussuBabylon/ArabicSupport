using System.Collections.Generic;
using ArabicSupport.TextInput;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Pixel-based RTL-aware line wrapper.
    ///
    /// The incoming Arabic text has already been reshaped and reordered by
    /// the translation pipeline, so wrapping scans backward. Rich-text state
    /// is calculated BEFORE wrapping in the string's natural word-array
    /// order, then looked up by segment index - never inferred from output
    /// line order.
    ///
    /// Two optional refinements (both on by default, both switchable in the
    /// settings window; with both off the result is exactly what this class
    /// produced before they existed):
    ///  - an English phrase inside Arabic text is one unit, so a line never
    ///    breaks in the middle of it (see LatinRunGlue);
    ///  - an Arabic word wider than the whole line is cut in reading order
    ///    (its right-hand piece first) instead of being left to the game,
    ///    which would cut it from the left and show the pieces in the wrong order.
    /// </summary>
    public static class LineWrapper
    {
        public struct WrapResult
        {
            public List<string> Lines;
            public List<PlaceholderProtector.OpenTag> ExitTagState;
        }

        public static WrapResult Wrap(
            PlaceholderProtector.ProtectedText protectedResult,
            float maxWidth,
            List<PlaceholderProtector.OpenTag> enteringTagState)
        {
            return Wrap(protectedResult, maxWidth, enteringTagState, WrapRules.KeepLatinRuns, WrapRules.SplitWideWords);
        }

        public static WrapResult Wrap(
            PlaceholderProtector.ProtectedText protectedResult,
            float maxWidth,
            List<PlaceholderProtector.OpenTag> enteringTagState,
            bool keepLatinRuns,
            bool splitWideWords)
        {
            enteringTagState = enteringTagState ?? PlaceholderProtector.EmptyState();

            if (string.IsNullOrEmpty(protectedResult.Text))
            {
                return new WrapResult
                {
                    Lines = new List<string> { string.Empty },
                    ExitTagState = enteringTagState
                };
            }

            var placeholders = protectedResult.Placeholders;

            // Already fits on one line (measured whole, exactly like Unity
            // does): don't break it. Adding up word widths can come out a
            // pixel wider than the real line, which split short labels that
            // the game sized to fit them (trait boxes, tabs) into two lines.
            if (maxWidth > 0f && TextMeasurer.MeasureWidth(protectedResult.Text, placeholders) <= maxWidth)
                maxWidth = 0f;

            if (maxWidth <= 0f)
            {
                var exit = PlaceholderProtector.AdvanceTagState(enteringTagState, protectedResult.Text, placeholders);

                string whole = PlaceholderProtector.WrapLineWithTagState(
                    PlaceholderProtector.Restore(protectedResult.Text, placeholders),
                    enteringTagState,
                    exit
                );

                return new WrapResult
                {
                    Lines = new List<string> { whole },
                    ExitTagState = exit
                };
            }

            string[] words = protectedResult.Text.Split(' ');

            if (keepLatinRuns)
                words = LatinRunGlue.Glue(words);

            var stateBefore = new List<PlaceholderProtector.OpenTag>[words.Length];
            var stateAfter = new List<PlaceholderProtector.OpenTag>[words.Length];

            var running = enteringTagState;

            for (int i = 0; i < words.Length; i++)
            {
                stateBefore[i] = running;
                running = PlaceholderProtector.AdvanceTagState(running, words[i], placeholders);
                stateAfter[i] = running;
            }

            var exitState = running;

            float spaceWidth = MeasureSpaceWidth();

            var wordWidths = new float[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                wordWidths[i] = TextMeasurer.MeasureWidth(words[i], placeholders);
            }

            var lines = new List<string>();

            int i2 = words.Length - 1;

            while (i2 >= 0)
            {
                // Only the first word of a line can be wider than the line: every
                // other word was accepted because it fit next to the ones before it.
                if (splitWideWords && wordWidths[i2] > maxWidth)
                    SplitWideWord(words, wordWidths, stateBefore, stateAfter, i2, maxWidth, placeholders, lines);

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

                string protectedSegment = segStart == segEnd
                    ? words[segStart]
                    : string.Join(" ", words, segStart, segEnd - segStart + 1);

                string restoredSegment = PlaceholderProtector.Restore(protectedSegment, placeholders);

                string balancedLine = PlaceholderProtector.WrapLineWithTagState(
                    restoredSegment,
                    stateBefore[segStart],
                    stateAfter[segEnd]
                );

                lines.Add(balancedLine);
            }

            if (lines.Count == 0)
            {
                lines.Add(PlaceholderProtector.WrapLineWithTagState(
                    PlaceholderProtector.Restore(protectedResult.Text, placeholders),
                    enteringTagState,
                    exitState
                ));
            }

            return new WrapResult
            {
                Lines = lines,
                ExitTagState = exitState
            };
        }

        /// <summary>
        /// Cuts an Arabic word that is wider than a whole line.
        ///
        /// The word is already in visual order, so its first letters (the ones the
        /// reader meets first) are at its RIGHT end. The right-hand pieces are
        /// therefore written out as lines first; what is left on the left is put
        /// back into the arrays as the word, and the normal loop goes on with it
        /// (it may even share its line with the words that come before it).
        ///
        /// Nothing is cut when the word is an English run, has no Arabic letter,
        /// or no safe place to cut is found: the game then handles it as before.
        /// The rich-text state of every piece is worked out from the tags inside
        /// the word, so each line opens and closes its own tags.
        /// </summary>
        private static void SplitWideWord(
            string[] words,
            float[] widths,
            List<PlaceholderProtector.OpenTag>[] before,
            List<PlaceholderProtector.OpenTag>[] after,
            int index,
            float maxWidth,
            List<string> placeholders,
            List<string> lines)
        {
            string word = words[index];

            if (word.Length < 2 || word.IndexOf(' ') >= 0 || !LatinRunGlue.HasRtlLetter(word))
                return;

            var startState = before[index];
            int end = word.Length;      // everything from here to the right is already written out
            bool cutAny = false;

            while (end > 1)
            {
                float headWidth = cutAny
                    ? TextMeasurer.MeasureWidth(word.Substring(0, end), placeholders)
                    : widths[index];

                if (headWidth <= maxWidth)
                    break;

                int cut = FindCut(word, end, maxWidth, placeholders);
                if (cut <= 0)
                    break;

                var pieceBefore = PlaceholderProtector.AdvanceTagState(startState, word.Substring(0, cut), placeholders);
                var pieceAfter = PlaceholderProtector.AdvanceTagState(startState, word.Substring(0, end), placeholders);

                lines.Add(PlaceholderProtector.WrapLineWithTagState(
                    PlaceholderProtector.Restore(word.Substring(cut, end - cut), placeholders),
                    pieceBefore,
                    pieceAfter));

                end = cut;
                cutAny = true;
            }

            if (!cutAny)
                return;

            string head = word.Substring(0, end);
            words[index] = head;
            widths[index] = TextMeasurer.MeasureWidth(head, placeholders);
            after[index] = PlaceholderProtector.AdvanceTagState(startState, head, placeholders);
        }

        /// <summary>
        /// Where the right-hand piece of word[0..end) starts: the longest tail that fits
        /// in <paramref name="maxWidth"/> (a tail is never shorter than one character, so
        /// the loop in SplitWideWord always makes progress). Returns -1 when no safe
        /// place exists. Width only grows with length, so a binary search finds it.
        /// </summary>
        private static int FindCut(string word, int end, float maxWidth, List<string> placeholders)
        {
            int cut = end - 1;      // fallback: a single character

            if (TailFits(word, end - 1, end, maxWidth, placeholders))
            {
                int low = 1;
                int high = end - 1;     // the tail word[high..end) is known to fit

                while (low < high)
                {
                    int mid = (low + high) / 2;
                    if (TailFits(word, mid, end, maxWidth, placeholders))
                        high = mid;
                    else
                        low = mid + 1;
                }

                cut = low;
            }

            // Moving the cut to the right only shortens the tail, so it still fits.
            while (cut < end && !CanCutAt(word, cut))
                cut++;

            return cut < end ? cut : -1;
        }

        private static bool TailFits(string word, int start, int end, float maxWidth, List<string> placeholders)
        {
            return TextMeasurer.MeasureWidth(word.Substring(start, end - start), placeholders) <= maxWidth;
        }

        /// <summary>
        /// A cut between word[at - 1] and word[at] is not allowed when it would separate a
        /// haraka from its letter (in visual order the mark comes right BEFORE its letter),
        /// or the two halves of a surrogate pair.
        /// </summary>
        private static bool CanCutAt(string word, int at)
        {
            if (ArabicShaper.IsMark(word[at - 1]))
                return false;

            if (char.IsHighSurrogate(word[at - 1]) && char.IsLowSurrogate(word[at]))
                return false;

            return true;
        }

        private static readonly float[] SpaceWidths = new float[8];
        private static readonly bool[] SpaceWidthsCached = new bool[8];

        /// <summary>Called when the UI font changes - cached space widths belong to the old font.</summary>
        public static void ClearCache()
        {
            System.Array.Clear(SpaceWidthsCached, 0, SpaceWidthsCached.Length);
        }

        private static float MeasureSpaceWidth()
        {
            int fontIndex = (int)Text.Font;
            bool validIndex = fontIndex >= 0 && fontIndex < SpaceWidths.Length;

            if (validIndex && SpaceWidthsCached[fontIndex])
                return SpaceWidths[fontIndex];

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

        public static WrapResult Wrap(
            PlaceholderProtector.ProtectedText protectedResult,
            float maxWidth,
            GameFont font,
            List<PlaceholderProtector.OpenTag> enteringTagState)
        {
            GameFont previous = Text.Font;

            try
            {
                Text.Font = font;
                return Wrap(protectedResult, maxWidth, enteringTagState);
            }
            finally
            {
                Text.Font = previous;
            }
        }
    }
}
