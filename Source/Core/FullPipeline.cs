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

            int bucketedWidth = ProcessedTextCache.BucketWidth(maxWidth);

            string cached = ProcessedTextCache.TryGet(original, bucketedWidth, font);
            if (cached != null)
                return cached;

            string result = ProcessUncached(original, bucketedWidth, font);
            ProcessedTextCache.Store(original, bucketedWidth, font, result);
            return result;
        }

        private static string ProcessUncached(string original, float maxWidth, GameFont font)
        {
            if (original.IndexOf('\r') != -1)
            {
                original = original.Replace("\r\n", "\n").Replace('\r', '\n');
            }

            var protectedResult = PlaceholderProtector.Protect(original);

            if (protectedResult.Text.IndexOf('\n') == -1)
            {
                return ProcessParagraph(protectedResult.Text, protectedResult.Placeholders, maxWidth, font);
            }

            string[] paragraphs = protectedResult.Text.Split('\n');
            var processedParagraphs = new List<string>(paragraphs.Length);

            foreach (string paragraph in paragraphs)
            {
                processedParagraphs.Add(ProcessParagraph(paragraph, protectedResult.Placeholders, maxWidth, font));
            }

            return string.Join("\n", processedParagraphs);
        }

        private static string ProcessParagraph(string protectedParagraph, List<string> placeholders, float maxWidth, GameFont font)
        {
            if (string.IsNullOrEmpty(protectedParagraph))
                return protectedParagraph;

            // Decide whether this paragraph needs RTL-aware wrapping based
            // on what it will actually DISPLAY (the restored text), not on
            // its marker-substituted form. Paired tags (see
            // PlaceholderProtector) are protected as ONE marker covering
            // the tag AND its content, so a paragraph whose only Arabic
            // sits entirely inside a <color>/<size>/etc. pair would look
            // Arabic-free if we checked the protected text directly —
            // silently skipping the RTL wrap it actually needs.
            string restored = PlaceholderProtector.Restore(protectedParagraph, placeholders);
            if (!ArabicDetector.ContainsArabic(restored))
            {
                return restored;
            }

            var protectedText = new PlaceholderProtector.ProtectedText
            {
                Text = protectedParagraph,
                Placeholders = placeholders
            };

            List<string> wrappedLines = LineWrapper.Wrap(protectedText, maxWidth, font);

            var finalLines = new List<string>(wrappedLines.Count);
            foreach (string line in wrappedLines)
            {
                finalLines.Add(PlaceholderProtector.Restore(line, placeholders));
            }

            return string.Join("\n", finalLines);
        }
    }
}
