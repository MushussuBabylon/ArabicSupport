using System.Collections.Generic;
using ArabicSupport.Caching;
using ArabicSupport.Utils;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Entry point for the mod's one remaining job: wrapping Arabic labels
    /// by their real pixel width instead of RimWorld's default estimated
    /// character-count wrap. No reshaping or bidi/RTL reordering happens
    /// here anymore — this only re-wraps.
    /// </summary>
    public static class FullPipeline
    {
        public static string Process(string original, float maxWidth, GameFont font)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            if (!ArabicDetector.ContainsArabic(original))
                return original;

            string cached = ProcessedTextCache.TryGet(original, maxWidth, font);
            if (cached != null)
                return cached;

            string result = ProcessUncached(original, maxWidth, font);
            ProcessedTextCache.Store(original, maxWidth, font, result);
            return result;
        }

        private static string ProcessUncached(string original, float maxWidth, GameFont font)
        {
            // Most labels are a single line. Skip the array/list allocation
            // and Join call entirely when there's nothing to split on.
            if (original.IndexOf('\n') == -1)
            {
                return ProcessParagraph(original, maxWidth, font);
            }

            string[] paragraphs = original.Split('\n');
            var processedParagraphs = new List<string>(paragraphs.Length);

            foreach (string paragraph in paragraphs)
            {
                processedParagraphs.Add(ProcessParagraph(paragraph, maxWidth, font));
            }

            return string.Join("\n", processedParagraphs);
        }

        private static string ProcessParagraph(string paragraph, float maxWidth, GameFont font)
        {
            if (string.IsNullOrEmpty(paragraph))
                return paragraph;

            if (!ArabicDetector.ContainsArabic(paragraph))
                return paragraph;

            var protectedResult = PlaceholderProtector.Protect(paragraph);

            List<string> wrappedLines = LineWrapper.Wrap(protectedResult, maxWidth, font);

            var finalLines = new List<string>(wrappedLines.Count);
            foreach (string line in wrappedLines)
            {
                finalLines.Add(PlaceholderProtector.Restore(line, protectedResult.Placeholders));
            }

            return string.Join("\n", finalLines);
        }
    }
}
