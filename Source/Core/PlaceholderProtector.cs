using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Protects placeholders and rich-text tags from being split apart by
    /// word wrapping.
    ///
    /// Rich-text tags are protected INDIVIDUALLY rather than as one complete
    /// <color>...</color> span. This is necessary because a colored span can
    /// itself wrap across multiple output lines.
    /// </summary>
    public static class PlaceholderProtector
    {
        private static readonly Regex PlaceholderRegex = new Regex(
            @"\{+[^{}]*\}+|<.*?>|\(\*.*?\)|\(/.*?\)|->|\[.*?\]",
            RegexOptions.Compiled
        );

        private const char MarkerBase = '\uE000';
        private const char MarkerEnd = '\uF8FF';

        private const int MaxMarkerCount =
            MarkerEnd - MarkerBase + 1;

        // '-' is intentionally NOT included. The only supported '-' based
        // placeholder is ->, checked explicitly below.
        private static readonly char[] TriggerChars =
        {
            '<',
            '{',
            '(',
            '['
        };

        private static readonly List<string> EmptyPlaceholders =
            new List<string>(0);

        private static readonly List<OpenTag> EmptyTagState =
            new List<OpenTag>(0);

        public struct ProtectedText
        {
            public string Text;
            public List<string> Placeholders;
        }

        /// <summary>
        /// Represents a rich-text tag that is currently open.
        /// </summary>
        public struct OpenTag
        {
            public string Name;
            public string OpenText;
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

            bool mayContainPlaceholder =
                line.IndexOfAny(TriggerChars) != -1 ||
                line.IndexOf("->", StringComparison.Ordinal) != -1;

            if (!mayContainPlaceholder)
            {
                return new ProtectedText
                {
                    Text = line,
                    Placeholders = EmptyPlaceholders
                };
            }

            var placeholders = new List<string>();
            int markerIndex = 0;

            string protectedLine = PlaceholderRegex.Replace(
                line,
                match =>
                {
                    // Do not enter the Unicode range outside our marker
                    // area if an extremely large string contains more
                    // placeholders than available PUA marker characters.
                    if (markerIndex >= MaxMarkerCount)
                        return match.Value;

                    placeholders.Add(match.Value);

                    char marker =
                        (char)(MarkerBase + markerIndex);

                    markerIndex++;

                    return marker.ToString();
                }
            );

            return new ProtectedText
            {
                Text = protectedLine,
                Placeholders = placeholders
            };
        }

        /// <summary>
        /// Restores every placeholder exactly as it originally appeared.
        /// </summary>
        public static string Restore(
            string text,
            List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            if (placeholders == null ||
                placeholders.Count == 0)
            {
                return text;
            }

            int maxMarkerExclusive =
                MarkerBase + placeholders.Count;

            bool hasMarker = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c >= MarkerBase &&
                    c < maxMarkerExclusive)
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
                if (c >= MarkerBase &&
                    c < maxMarkerExclusive)
                {
                    sb.Append(
                        placeholders[c - MarkerBase]
                    );
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Restores placeholders for width measurement, but rich-text tags
        /// themselves contribute zero visible width.
        ///
        /// Other placeholders such as {0} remain visible because their text
        /// can affect the width.
        /// </summary>
        public static string RestoreForMeasurement(
            string text,
            List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            if (placeholders == null ||
                placeholders.Count == 0)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                int index = c - MarkerBase;

                if (index < 0 ||
                    index >= placeholders.Count)
                {
                    sb.Append(c);
                    continue;
                }

                string original = placeholders[index];

                // Rich-text markup is not rendered as visible text.
                if (!IsTag(original))
                {
                    sb.Append(original);
                }
            }

            return sb.ToString();
        }

        public static bool IsTag(string token)
        {
            return token != null &&
                   token.Length > 1 &&
                   token[0] == '<';
        }

        public static bool IsClosingTag(string token)
        {
            return token != null &&
                   token.Length > 2 &&
                   token[0] == '<' &&
                   token[1] == '/';
        }

        public static string GetTagName(
            string token,
            bool closing)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            int i = closing ? 2 : 1;
            int start = i;

            while (i < token.Length &&
                   char.IsLetterOrDigit(token[i]))
            {
                i++;
            }

            return i > start
                ? token.Substring(start, i - start)
                : string.Empty;
        }

        public static List<OpenTag> EmptyState()
        {
            return EmptyTagState;
        }

        /// <summary>
        /// Advances the currently-open tag state through one protected chunk
        /// of text.
        ///
        /// The chunk is scanned in its NATURAL string order, independent of
        /// the later RTL line ordering used by LineWrapper.
        /// </summary>
        public static List<OpenTag> AdvanceTagState(
            List<OpenTag> stack,
            string protectedChunk,
            List<string> placeholders)
        {
            if (stack == null)
                stack = EmptyTagState;

            if (string.IsNullOrEmpty(protectedChunk) ||
                placeholders == null ||
                placeholders.Count == 0)
            {
                return stack;
            }

            List<OpenTag> next = null;

            foreach (char c in protectedChunk)
            {
                int index = c - MarkerBase;

                if (index < 0 ||
                    index >= placeholders.Count)
                {
                    continue;
                }

                string original = placeholders[index];

                if (!IsTag(original))
                    continue;

                if (next == null)
                {
                    next = new List<OpenTag>(stack);
                }

                if (IsClosingTag(original))
                {
                    string name =
                        GetTagName(original, true);

                    // Remove the nearest matching open tag.
                    for (int i = next.Count - 1;
                         i >= 0;
                         i--)
                    {
                        if (next[i].Name == name)
                        {
                            next.RemoveAt(i);
                            break;
                        }
                    }
                }
                else
                {
                    string name =
                        GetTagName(original, false);

                    if (!string.IsNullOrEmpty(name))
                    {
                        next.Add(
                            new OpenTag
                            {
                                Name = name,
                                OpenText = original
                            }
                        );
                    }
                }
            }

            return next ?? stack;
        }

        /// <summary>
        /// Makes one wrapped output line self-contained.
        ///
        /// entering = tags already open before this line's word range.
        /// exiting  = tags still open after this line's word range.
        ///
        /// The line therefore starts by reopening entering tags and ends by
        /// closing the tags that remain active after the line.
        /// </summary>
        public static string WrapLineWithTagState(
            string restoredLine,
            List<OpenTag> entering,
            List<OpenTag> exiting)
        {
            entering = entering ?? EmptyTagState;
            exiting = exiting ?? EmptyTagState;

            if (entering.Count == 0 &&
                exiting.Count == 0)
            {
                return restoredLine;
            }

            var sb = new StringBuilder(
                (restoredLine?.Length ?? 0) + 64
            );

            for (int i = 0;
                 i < entering.Count;
                 i++)
            {
                sb.Append(entering[i].OpenText);
            }

            sb.Append(restoredLine);

            // Close in reverse order so nested tags remain valid.
            for (int i = exiting.Count - 1;
                 i >= 0;
                 i--)
            {
                sb.Append("</");
                sb.Append(exiting[i].Name);
                sb.Append('>');
            }

            return sb.ToString();
        }
    }
}
