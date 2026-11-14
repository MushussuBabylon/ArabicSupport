using System.Collections.Generic;
using ArabicSupport.Utils;

namespace ArabicSupport.Core
{
    /// <summary>
    /// Keeps an English phrase (or a number next to one) in one piece while an
    /// Arabic line is wrapped.
    ///
    /// The wrapper scans the already-reordered words from the end, because the
    /// first line of an Arabic paragraph is at the right end of the string.
    /// That is right for Arabic words, but an English run such as a colonist's
    /// name ("Randall Elliott") is NOT reversed by the reordering: its words
    /// stay in their own left-to-right order. Scanned from the end, its words
    /// would come out in the wrong order the moment a line breaks inside it.
    ///
    /// The fix needs no reshaping and no reordering of anything: a run of two or
    /// more such words is handed to the wrapper as ONE word, so a line can only
    /// break before or after it. A run wider than a whole line is left on a line
    /// of its own; the game then wraps it left to right, which is its natural
    /// order. Text that has no such run comes back untouched (same array).
    ///
    /// A number next to an English word is kept with it (the Unicode bidi rules
    /// treat it as part of the run). Punctuation between two English words is too.
    /// Either way the characters only get a smaller choice of break points; their
    /// order on screen never changes.
    /// </summary>
    internal static class LatinRunGlue
    {
        private enum Kind : byte { Rtl, Latin, Number, Neutral }

        /// <summary>The word has at least one letter of a right-to-left script.</summary>
        public static bool HasRtlLetter(string word)
        {
            if (word == null)
                return false;

            for (int i = 0; i < word.Length; i++)
                if (ScriptMix.IsRtlLetter(word[i]))
                    return true;

            return false;
        }

        private static Kind KindOf(string word)
        {
            bool latin = false;
            bool number = false;

            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];

                if (ScriptMix.IsRtlLetter(c))
                    return Kind.Rtl;

                if (char.IsLetter(c))
                    latin = true;
                else if (char.IsDigit(c))
                    number = true;
            }

            return latin ? Kind.Latin : number ? Kind.Number : Kind.Neutral;
        }

        /// <summary>
        /// Returns the words with every run of two or more English words (and the
        /// numbers or punctuation that belong to it) joined into one entry.
        /// Returns <paramref name="words"/> itself when there is nothing to join.
        /// </summary>
        public static string[] Glue(string[] words)
        {
            int n = words.Length;
            if (n < 2)
                return words;

            var kinds = new Kind[n];
            bool anyLatin = false;

            for (int i = 0; i < n; i++)
            {
                kinds[i] = KindOf(words[i]);
                if (kinds[i] == Kind.Latin)
                    anyLatin = true;
            }

            if (!anyLatin)
                return words;       // pure Arabic (the common case): nothing to do

            var member = new bool[n];
            for (int i = 0; i < n; i++)
                member[i] = kinds[i] == Kind.Latin;

            // Numbers and punctuation sitting between / next to English words.
            for (int i = 0; i < n;)
            {
                if (kinds[i] != Kind.Number && kinds[i] != Kind.Neutral)
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < n && (kinds[i] == Kind.Number || kinds[i] == Kind.Neutral))
                    i++;
                int end = i;        // exclusive

                bool latinBefore = start > 0 && kinds[start - 1] == Kind.Latin;
                bool latinAfter = end < n && kinds[end] == Kind.Latin;

                int from = start;
                int to = end - 1;

                if (latinBefore && latinAfter)
                {
                    // between two English words: all of it belongs to the run
                }
                else if (latinBefore)
                {
                    to = LastNumber(kinds, start, end);     // trailing punctuation does not
                    if (to < 0) continue;
                }
                else if (latinAfter)
                {
                    from = FirstNumber(kinds, start, end);  // neither does leading punctuation
                    if (from < 0) continue;
                }
                else
                {
                    continue;
                }

                for (int k = from; k <= to; k++)
                    member[k] = true;
            }

            bool hasRun = false;
            for (int i = 0; i + 1 < n; i++)
            {
                if (member[i] && member[i + 1])
                {
                    hasRun = true;
                    break;
                }
            }

            if (!hasRun)
                return words;

            var glued = new List<string>(n);

            for (int i = 0; i < n;)
            {
                if (!member[i])
                {
                    glued.Add(words[i]);
                    i++;
                    continue;
                }

                int j = i + 1;
                while (j < n && member[j])
                    j++;

                glued.Add(j - i == 1 ? words[i] : string.Join(" ", words, i, j - i));
                i = j;
            }

            return glued.ToArray();
        }

        private static int FirstNumber(Kind[] kinds, int start, int end)
        {
            for (int k = start; k < end; k++)
                if (kinds[k] == Kind.Number)
                    return k;
            return -1;
        }

        private static int LastNumber(Kind[] kinds, int start, int end)
        {
            for (int k = end - 1; k >= start; k--)
                if (kinds[k] == Kind.Number)
                    return k;
            return -1;
        }
    }
}
