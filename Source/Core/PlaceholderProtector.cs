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

            if (line.IndexOfAny(TriggerChars) == -1)
            {
                return new ProtectedText { Text = line, Placeholders = EmptyPlaceholders };
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
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            if (placeholders == null || placeholders.Count == 0)
                return text;

            // Even when this paragraph has placeholders somewhere, most
            // individual words/lines passed in here won't actually contain
            // a marker character. Scan first and bail out before paying for
            // a StringBuilder + full rebuild if there's nothing to replace.
            bool hasMarker = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= MarkerBase && c < MarkerBase + placeholders.Count)
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
