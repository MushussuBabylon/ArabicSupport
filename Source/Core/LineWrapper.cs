using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Pixel-based RTL-aware line wrapper.
    ///
    /// The incoming Arabic text has already been reshaped and reordered by
    /// the translation pipeline. Because the word array is visually ordered
    /// opposite to normal reading order, wrapping scans backward.
    ///
    /// Rich-text state is therefore calculated BEFORE wrapping in the
    /// string's natural word-array order, then looked up by segment indices.
    /// It is never inferred from the order in which output lines are emitted.
    /// </summary>
    public static class LineWrapper
    {
        public struct WrapResult
        {
            public List<string> Lines;

            // Tags still logically open after the complete paragraph.
            public List<PlaceholderProtector.OpenTag> ExitTagState;
        }

        public static WrapResult Wrap(
            PlaceholderProtector.ProtectedText protectedResult,
            float maxWidth,
            List<PlaceholderProtector.OpenTag> enteringTagState)
        {
            enteringTagState =
                enteringTagState ??
                PlaceholderProtector.EmptyState();

            if (string.IsNullOrEmpty(protectedResult.Text))
            {
                return new WrapResult
                {
                    Lines = new List<string>
                    {
                        string.Empty
                    },
                    ExitTagState = enteringTagState
                };
            }

            var placeholders =
                protectedResult.Placeholders;

            string[] words =
                protectedResult.Text.Split(' ');

            /*
             * IMPORTANT:
             *
             * stateBefore[i] = tags logically open immediately before
             *                  word i.
             *
             * stateAfter[i]  = tags logically open immediately after
             *                  word i.
             *
             * These are calculated in normal array order BEFORE the RTL
             * backward wrapping loop.
             *
             * A colored span may be:
             *
             *   <color> word word word word </color>
             *
             * but the backward RTL wrapping loop can emit the line containing
             * </color> before the line containing <color>.
             *
             * Therefore a normal top-to-bottom stack would fail.
             * Index-based state avoids that completely.
             */
            var stateBefore =
                new List<PlaceholderProtector.OpenTag>[
                    words.Length
                ];

            var stateAfter =
                new List<PlaceholderProtector.OpenTag>[
                    words.Length
                ];

            var running = enteringTagState;

            for (int i = 0;
                 i < words.Length;
                 i++)
            {
                stateBefore[i] = running;

                running =
                    PlaceholderProtector.AdvanceTagState(
                        running,
                        words[i],
                        placeholders
                    );

                stateAfter[i] = running;
            }

            var exitState = running;

            if (maxWidth <= 0f)
            {
                string whole =
                    PlaceholderProtector.WrapLineWithTagState(
                        PlaceholderProtector.Restore(
                            protectedResult.Text,
                            placeholders
                        ),
                        enteringTagState,
                        exitState
                    );

                return new WrapResult
                {
                    Lines = new List<string>
                    {
                        whole
                    },
                    ExitTagState = exitState
                };
            }

            float spaceWidth =
                MeasureSpaceWidth();

            var wordWidths =
                new float[words.Length];

            for (int i = 0;
                 i < words.Length;
                 i++)
            {
                string measurable =
                    words[i].Length == 0
                        ? string.Empty
                        : TextMeasurer.RestorePlaceholders(
                            words[i],
                            placeholders
                        );

                wordWidths[i] =
                    string.IsNullOrEmpty(measurable)
                        ? 0f
                        : Text.CalcSize(measurable).x;
            }

            var lines = new List<string>();

            /*
             * RTL-aware wrapping:
             *
             * Start from the end of the array because the source has already
             * been visually reordered for Arabic before reaching this mod.
             */
            int i2 = words.Length - 1;

            while (i2 >= 0)
            {
                int segEnd = i2;
                int segStart = i2;

                float width = wordWidths[i2];

                i2--;

                while (i2 >= 0)
                {
                    float candidate =
                        width +
                        spaceWidth +
                        wordWidths[i2];

                    if (candidate > maxWidth)
                        break;

                    width = candidate;
                    segStart = i2;

                    i2--;
                }

                string protectedSegment =
                    JoinRange(
                        words,
                        segStart,
                        segEnd
                    );

                string restoredSegment =
                    PlaceholderProtector.Restore(
                        protectedSegment,
                        placeholders
                    );

                string balancedLine =
                    PlaceholderProtector.WrapLineWithTagState(
                        restoredSegment,
                        stateBefore[segStart],
                        stateAfter[segEnd]
                    );

                lines.Add(balancedLine);
            }

            if (lines.Count == 0)
            {
                lines.Add(
                    PlaceholderProtector.WrapLineWithTagState(
                        PlaceholderProtector.Restore(
                            protectedResult.Text,
                            placeholders
                        ),
                        enteringTagState,
                        exitState
                    )
                );
            }

            return new WrapResult
            {
                Lines = lines,
                ExitTagState = exitState
            };
        }

        private static readonly float[] SpaceWidths =
            new float[8];

        private static readonly bool[] SpaceWidthsCached =
            new bool[8];

        private static float MeasureSpaceWidth()
        {
            int fontIndex = (int)Text.Font;

            bool validIndex =
                fontIndex >= 0 &&
                fontIndex < SpaceWidths.Length;

            if (validIndex &&
                SpaceWidthsCached[fontIndex])
            {
                return SpaceWidths[fontIndex];
            }

            float indirect =
                Text.CalcSize("i i").x -
                2f * Text.CalcSize("i").x;

            float result;

            if (indirect > 0.01f)
            {
                result = indirect;
            }
            else
            {
                float direct =
                    Text.CalcSize(" ").x;

                result =
                    direct > 0.01f
                        ? direct
                        : 1f;
            }

            if (validIndex)
            {
                SpaceWidths[fontIndex] = result;
                SpaceWidthsCached[fontIndex] = true;
            }

            return result;
        }

        private static string JoinRange(
            string[] words,
            int start,
            int end)
        {
            if (start == end)
                return words[start];

            var sb = new StringBuilder();

            for (int i = start;
                 i <= end;
                 i++)
            {
                if (i > start)
                    sb.Append(' ');

                sb.Append(words[i]);
            }

            return sb.ToString();
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

                return Wrap(
                    protectedResult,
                    maxWidth,
                    enteringTagState
                );
            }
            finally
            {
                Text.Font = previous;
            }
        }
    }
}
