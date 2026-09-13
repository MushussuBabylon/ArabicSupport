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
    /// state is carried across paragraphs, so tags can start on one wrapped
    /// line and close on another, even across paragraph boundaries.
    /// </summary>
    public static class FullPipeline
    {
        public static string Process(string original, float maxWidth, GameFont font)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            if (!ArabicDetector.ContainsArabic(original))
                return original;

            return ProcessKnownArabic(original, maxWidth, font);
        }

        /// <summary>
        /// Same as Process, but for callers that already confirmed the text
        /// contains Arabic (the Harmony patches need that check anyway to
        /// decide whether to touch the label at all) — skips the redundant
        /// second scan.
        /// </summary>
        internal static string ProcessKnownArabic(string original, float maxWidth, GameFont font)
        {
            string cached = ProcessedTextCache.TryGet(original, maxWidth, font);

            if (cached != null)
                return cached;

            string result = ProcessUncached(original, maxWidth, font);

            ProcessedTextCache.Store(original, maxWidth, font, result);

            return result;
        }

        private static string ProcessUncached(string original, float maxWidth, GameFont font)
        {
            var protectedResult = PlaceholderProtector.Protect(original);
            var placeholders = protectedResult.Placeholders;

            string[] paragraphs =
                protectedResult.Text.IndexOf('\n') == -1
                    ? new[] { protectedResult.Text }
                    : protectedResult.Text.Split('\n');

            var allLines = new List<string>();
            var carryState = PlaceholderProtector.EmptyState();

            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    allLines.Add(string.Empty);
                    continue;
                }

                if (!ArabicDetector.ContainsArabic(paragraph))
                {
                    string restored = PlaceholderProtector.Restore(paragraph, placeholders);

                    var exitState = PlaceholderProtector.AdvanceTagState(carryState, paragraph, placeholders);

                    allLines.Add(PlaceholderProtector.WrapLineWithTagState(restored, carryState, exitState));

                    carryState = exitState;
                    continue;
                }

                var protectedParagraph = new PlaceholderProtector.ProtectedText
                {
                    Text = paragraph,
                    Placeholders = placeholders
                };

                LineWrapper.WrapResult wrapResult = LineWrapper.Wrap(protectedParagraph, maxWidth, font, carryState);

                allLines.AddRange(wrapResult.Lines);

                carryState = wrapResult.ExitTagState;
            }

            return string.Join("\n", allLines);
        }
    }
}
