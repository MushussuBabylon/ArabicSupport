using System.Collections.Generic;
using ArabicSupport.Caching;
using ArabicSupport.Utils;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Main entry point for Arabic pixel-based text wrapping.
    ///
    /// Rich-text tags are protected individually and their logical open/close
    /// state is carried across paragraphs. This allows tags to:
    ///
    /// - start on one wrapped line and end on another;
    /// - start in one original paragraph and close in a later paragraph;
    /// - remain valid despite RTL wrapping emitting segments in reverse order.
    /// </summary>
    public static class FullPipeline
    {
        public static string Process(
            string original,
            float maxWidth,
            GameFont font)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            if (!ArabicDetector.ContainsArabic(original))
                return original;

            string cached =
                ProcessedTextCache.TryGet(
                    original,
                    maxWidth,
                    font
                );

            if (cached != null)
                return cached;

            string result =
                ProcessUncached(
                    original,
                    maxWidth,
                    font
                );

            ProcessedTextCache.Store(
                original,
                maxWidth,
                font,
                result
            );

            return result;
        }

        private static string ProcessUncached(
            string original,
            float maxWidth,
            GameFont font)
        {
            // Protect the entire string once so every tag has a stable marker
            // index shared across all paragraphs.
            var protectedResult =
                PlaceholderProtector.Protect(original);

            var placeholders =
                protectedResult.Placeholders;

            string[] paragraphs =
                protectedResult.Text.IndexOf('\n') == -1
                    ? new[]
                    {
                        protectedResult.Text
                    }
                    : protectedResult.Text.Split('\n');

            var allLines =
                new List<string>();

            /*
             * Paragraph order itself remains normal top-to-bottom order.
             *
             * Only the words WITHIN an Arabic paragraph are processed by the
             * RTL backward wrapper.
             *
             * Therefore tag state is carried forward between paragraphs.
             */
            var carryState =
                PlaceholderProtector.EmptyState();

            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    allLines.Add(string.Empty);
                    continue;
                }

                /*
                 * Paragraphs containing no unprotected Arabic are not passed
                 * to the RTL backward wrapper, because that could reorder
                 * plain LTR content.
                 *
                 * However, tags still need to affect the carried tag state.
                 */
                if (!ArabicDetector.ContainsArabic(paragraph))
                {
                    string restored =
                        PlaceholderProtector.Restore(
                            paragraph,
                            placeholders
                        );

                    var exitState =
                        PlaceholderProtector.AdvanceTagState(
                            carryState,
                            paragraph,
                            placeholders
                        );

                    allLines.Add(
                        PlaceholderProtector.WrapLineWithTagState(
                            restored,
                            carryState,
                            exitState
                        )
                    );

                    carryState = exitState;
                    continue;
                }

                var protectedParagraph =
                    new PlaceholderProtector.ProtectedText
                    {
                        Text = paragraph,
                        Placeholders = placeholders
                    };

                LineWrapper.WrapResult wrapResult =
                    LineWrapper.Wrap(
                        protectedParagraph,
                        maxWidth,
                        font,
                        carryState
                    );

                allLines.AddRange(
                    wrapResult.Lines
                );

                // A rich-text tag may remain open at the end of this
                // paragraph and close in a later paragraph.
                carryState =
                    wrapResult.ExitTagState;
            }

            return string.Join(
                "\n",
                allLines
            );
        }
    }
}
