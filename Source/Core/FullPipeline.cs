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
            // Protect placeholders against the WHOLE original string before
            // splitting into paragraphs. A paired tag like <color=...>...
            // </color> can legitimately wrap around several lines of a
            // tooltip/hediff blurb. Protecting per-paragraph (after
            // splitting on '\n' first) let a tag's opening half land in one
            // paragraph and its closing half in another, so neither half
            // matched the paired-tag alternative in PlaceholderRegex and
            // each got treated as an unpaired fragment instead — which is
            // what let raw "<color=...>" / stray "</color>" leak into
            // rendered tooltips. Protecting once, up front (combined with
            // Singleline in PlaceholderProtector), lets a matched pair
            // swallow any '\n' between its halves, so the whole pair
            // becomes one atomic marker no matter how many display lines
            // it originally spanned.
            var protectedResult = PlaceholderProtector.Protect(original);

            // Placeholder markers are single Private-Use-Area characters
            // and never themselves contain '\n', so splitting the
            // already-protected text on '\n' is always safe here.
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

            // A paragraph with no *unprotected* Arabic left in it doesn't
            // need — and must not get — the RTL-aware backward wrap below:
            // LineWrapper's backward word scan assumes RTL reading order,
            // so running it on a plain LTR paragraph would reorder its
            // words. But this paragraph may still contain placeholder
            // markers substituted during the whole-string Protect() pass
            // above, so it must always be restored before returning —
            // never handed back untouched.
            if (!ArabicDetector.ContainsArabic(protectedParagraph))
            {
                return PlaceholderProtector.Restore(protectedParagraph, placeholders);
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
