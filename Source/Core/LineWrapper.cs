using System.Collections.Generic;
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
    /// order, then looked up by segment index — never inferred from output
    /// line order.
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

        private static readonly float[] SpaceWidths = new float[8];
        private static readonly bool[] SpaceWidthsCached = new bool[8];

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
