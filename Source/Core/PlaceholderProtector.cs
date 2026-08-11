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
        // Matches, in priority order:
        //   <tag ...>...</tag>  (paired tags, captured whole so open/close
        //                        can never land on different wrapped lines
        //                        and get emitted out of order)
        //   {...} (including nested {{0}})
        //   <...> (unpaired/self-contained tag, fallback)
        //   (*tags), (/tags), ->, [brackets]
        private static readonly Regex PlaceholderRegex = new Regex(
            @"<(\w+)[^>]*>.*?</\1>|\{+[^{}]*\}+|<.*?>|\(\*.*?\)|\(/.*?\)|->|\[.*?\]",
            RegexOptions.Compiled
        );

        private const char MarkerBase = '\uE000'; // start of Unicode Private Use Area

        public struct ProtectedText
        {
            public string Text;
            public List<string> Placeholders;
        }

        public static ProtectedText Protect(string line)
        {
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
