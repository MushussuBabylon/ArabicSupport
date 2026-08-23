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
    /// Rich-text tag PAIRS (<color=...>...</color>, <size=...>...</size>,
    /// etc.) are protected as ONE atomic marker covering the tag and its
    /// content together, not as two separate tag markers. An earlier
    /// version of this class protected each tag individually, which let
    /// LineWrapper insert a '\n' between an opening tag and its matching
    /// closing tag whenever the content between them didn't fit on one
    /// line. That produced well-formed-looking tags that nonetheless
    /// rendered as literal text instead of applying their color/size —
    /// Verse's tooltip/label rendering does not reliably apply rich-text
    /// markup whose opening and closing tags land on different wrapped
    /// lines within the same Label call. Protecting the whole pair as one
    /// marker guarantees a tag's open and close can never be separated by
    /// any line break we insert.
    ///
    /// The trade-off: a paired tag is treated as ONE unsplittable "word" by
    /// LineWrapper, so a single tag-wrapped run of Arabic text that is
    /// wider than the available width on its own cannot wrap internally
    /// and will overflow rather than break onto multiple lines. This is a
    /// narrower, more acceptable limitation than tags rendering as literal
    /// text — see FullPipeline.ProcessParagraph for how Arabic content
    /// hidden inside a protected pair is still correctly detected.
    /// </summary>
    public static class PlaceholderProtector
    {
        private static readonly Regex PlaceholderRegex = new Regex(
            @"<(\w+)[^>]*>.*?</\1>|\{+[^{}]*\}+|<.*?>|\(\*.*?\)|\(/.*?\)|->|\[.*?\]",
            // Singleline lets '.' match '\n' too, so the paired-tag
            // alternative (<(\w+)...>...</\1>) can match an opening/closing
            // pair even if the ORIGINAL text already had a '\n' between
            // them (e.g. a <color=...>...</color> wrapped around a whole
            // multi-line tooltip blurb). Protect() runs on the FULL
            // original string before any '\n' splitting happens, so this
            // also reduces the pair to a single marker character before
            // line-wrapping ever runs — guaranteeing our own wrapping can
            // never insert a break between the open and close tags either.
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
