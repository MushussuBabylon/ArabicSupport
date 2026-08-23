using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Protects placeholders ({0}, {{0}}, tags, [brackets], -> prefixes) from
    /// being split apart mid-wrap: replace each match with a Private-Use-Area
    /// marker character, wrap the rest of the string, then restore afterward.
    ///
    /// IMPORTANT: rich-text tags are protected INDIVIDUALLY — "<color=red>"
    /// and "</color>" each become their own marker — rather than protecting
    /// the whole "<color=red>...</color>" span (tag + content) as one
    /// marker. Protecting the whole span used to hide whatever Arabic text
    /// sat between the tags from ArabicDetector entirely (a marker
    /// character isn't Arabic), which silently skipped wrapping for any
    /// label whose Arabic content was entirely wrapped in one rich-text
    /// tag. Protecting each tag on its own keeps the tags themselves atomic
    /// (never split mid-tag, even across a later '\n' split) while leaving
    /// the Arabic content between them fully visible to detection and
    /// wrapping.
    /// </summary>
    public static class PlaceholderProtector
    {
        private static readonly Regex PlaceholderRegex = new Regex(
            @"\{+[^{}]*\}+|<[^>]+>|\(\*.*?\)|\(/.*?\)|->|\[.*?\]",
            RegexOptions.Compiled | RegexOptions.Singleline
        );

        private const char MarkerBase = '\uE000'; // start of Unicode Private Use Area
        private const char MarkerEnd = '\uF8FF';  // end of Unicode Private Use Area
        private const int MaxMarkerCount = MarkerEnd - MarkerBase + 1;

        // Every alternative in PlaceholderRegex requires at least one of
        // these characters to appear before it can possibly match. Most
        // ordinary game labels contain none of them, so checking for these
        // first lets us skip the regex engine entirely for the common
        // case. '-' is deliberately NOT in this list: it's common in
        // ordinary text (hyphenated words, dashes) and only matters here
        // as part of the literal "->" token, checked separately below.
        private static readonly char[] TriggerChars = { '<', '{', '(', '[' };

        // Shared, never-mutated stand-in for "no placeholders." Avoids a
        // fresh empty List<string> allocation on every plain label, and
        // means callers never have to null-check Placeholders.
        private static readonly List<string> EmptyPlaceholders = new List<string>(0);

        public struct ProtectedText
        {
            public string Text;
            public List<string> Placeholders;
        }

        public static ProtectedText Protect(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return new ProtectedText
                {
                    Text = line ?? string.Empty,
                    Placeholders = EmptyPlaceholders
                };
            }

            bool mayContainPlaceholder = line.IndexOfAny(TriggerChars) != -1 ||
                                          line.IndexOf("->", System.StringComparison.Ordinal) != -1;

            if (!mayContainPlaceholder)
            {
                return new ProtectedText { Text = line, Placeholders = EmptyPlaceholders };
            }

            var placeholders = new List<string>();
            int markerIndex = 0;

            string protectedLine = PlaceholderRegex.Replace(line, match =>
            {
                // Private Use Area has a fixed number of code points. In
                // the (essentially impossible for real game text) case
                // where a single string has more matches than that, stop
                // protecting further matches rather than silently letting
                // the marker char wrap into unrelated Unicode ranges.
                if (markerIndex >= MaxMarkerCount)
                    return match.Value;

                placeholders.Add(match.Value);
                char marker = (char)(MarkerBase + markerIndex);
                markerIndex++;
                return marker.ToString();
            });

            return new ProtectedText
            {
                Text = protectedLine,
                Placeholders = placeholders
            };
        }

        public static string Restore(string text, List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            if (placeholders == null || placeholders.Count == 0)
                return text;

            int maxMarkerExclusive = MarkerBase + placeholders.Count;

            // Even when this paragraph has placeholders somewhere, most
            // individual words/lines passed in here won't actually contain
            // a marker character. Scan first and bail out before paying for
            // a StringBuilder + full rebuild if there's nothing to replace.
            bool hasMarker = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= MarkerBase && c < maxMarkerExclusive)
                {
                    hasMarker = true;
                    break;
                }
            }

            if (!hasMarker)
                return text;

            var sb = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                if (c >= MarkerBase && c < maxMarkerExclusive)
                {
                    int index = c - MarkerBase;
                    sb.Append(placeholders[index]);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
