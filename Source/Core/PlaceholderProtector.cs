using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Protects placeholders ({0}, {{0}}, tags, [brackets], -> prefixes) from
    /// being split apart mid-wrap: replace each match with a Private-Use-Area
    /// marker character, wrap the rest of the string, then restore afterward.
    /// </summary>
    public static class PlaceholderProtector
    {
        private static readonly Regex PlaceholderRegex = new Regex(
            @"<(\w+)[^>]*>.*?</\1>|\{+[^{}]*\}+|<.*?>|\(\*.*?\)|\(/.*?\)|->|\[.*?\]",
            RegexOptions.Compiled
        );

        private const char MarkerBase = '\uE000'; // start of Unicode Private Use Area

        // Every alternative in PlaceholderRegex requires at least one of
        // these characters to appear before it can possibly match. Most
        // ordinary game labels contain none of them, so checking for these
        // first lets us skip the regex engine entirely for the common case.
        private static readonly char[] TriggerChars = { '<', '{', '(', '[', '-' };

        public struct ProtectedText
        {
            public string Text;
            public List<string> Placeholders;
        }

        public static ProtectedText Protect(string line)
        {
            if (line.IndexOfAny(TriggerChars) == -1)
            {
                return new ProtectedText { Text = line, Placeholders = null };
            }

            var placeholders = new List<string>();
            int markerIndex = 0;

            string protectedLine = PlaceholderRegex.Replace(line, match =>
            {
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
            // Nothing to restore — either the fast path above skipped
            // protection entirely, or this line simply had no placeholders.
            if (placeholders == null || placeholders.Count == 0)
                return text;

            var sb = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                if (c >= MarkerBase && c < MarkerBase + placeholders.Count)
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
