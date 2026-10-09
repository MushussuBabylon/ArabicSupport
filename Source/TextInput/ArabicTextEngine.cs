using System;
using System.Collections.Generic;
using System.Text;
using ArabicSupport.Caching;
using ArabicSupport.Utils;

namespace ArabicSupport.TextInput
{
    // =====================================================================
    //  What this file is for
    //
    //  The translation stores every Arabic string in VISUAL form: letters
    //  already replaced by their joined presentation forms (U+FE70-U+FEFC)
    //  and the characters already reordered for a left-to-right renderer.
    //
    //  When a player types, the keyboard produces LOGICAL text (base letters
    //  U+0621-U+064A, in reading order). To store what the player typed in
    //  the same form as the translation, it has to go through the same two
    //  steps:  shape (pick the joined form of each letter)  and  reorder
    //  (reverse right-to-left runs).  That is ToVisual().
    //
    //  To edit text that is already stored visually, the process has to be
    //  undone first:  ToLogical().
    //
    //  Assumptions (match these to your build-time reshaper if the results
    //  differ from your translation):
    //   - the paragraph direction is always right-to-left
    //   - lam+alef is merged into the lam-alef ligature (U+FEF5-U+FEFC)
    //   - diacritics (tashkeel) are not repositioned
    // =====================================================================

    /// <summary>
    /// Arabic contextual shaping: base letters to presentation forms, and back.
    /// </summary>
    public static class ArabicShaper
    {
        private const char Tatweel = '\u0640';
        private const char Lam = '\u0644';

        // { base letter, isolated, final, initial, medial }; 0 = form does not exist.
        private static readonly ushort[][] Rows =
        {
            new ushort[] { 0x0621, 0xFE80, 0,      0,      0      }, // hamza
            new ushort[] { 0x0622, 0xFE81, 0xFE82, 0,      0      }, // alef madda
            new ushort[] { 0x0623, 0xFE83, 0xFE84, 0,      0      }, // alef hamza above
            new ushort[] { 0x0624, 0xFE85, 0xFE86, 0,      0      }, // waw hamza
            new ushort[] { 0x0625, 0xFE87, 0xFE88, 0,      0      }, // alef hamza below
            new ushort[] { 0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C }, // yeh hamza
            new ushort[] { 0x0627, 0xFE8D, 0xFE8E, 0,      0      }, // alef
            new ushort[] { 0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92 }, // beh
            new ushort[] { 0x0629, 0xFE93, 0xFE94, 0,      0      }, // teh marbuta
            new ushort[] { 0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98 }, // teh
            new ushort[] { 0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C }, // theh
            new ushort[] { 0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0 }, // jeem
            new ushort[] { 0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4 }, // hah
            new ushort[] { 0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8 }, // khah
            new ushort[] { 0x062F, 0xFEA9, 0xFEAA, 0,      0      }, // dal
            new ushort[] { 0x0630, 0xFEAB, 0xFEAC, 0,      0      }, // thal
            new ushort[] { 0x0631, 0xFEAD, 0xFEAE, 0,      0      }, // reh
            new ushort[] { 0x0632, 0xFEAF, 0xFEB0, 0,      0      }, // zain
            new ushort[] { 0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4 }, // seen
            new ushort[] { 0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8 }, // sheen
            new ushort[] { 0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC }, // sad
            new ushort[] { 0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0 }, // dad
            new ushort[] { 0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4 }, // tah
            new ushort[] { 0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8 }, // zah
            new ushort[] { 0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC }, // ain
            new ushort[] { 0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0 }, // ghain
            new ushort[] { 0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4 }, // feh
            new ushort[] { 0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8 }, // qaf
            new ushort[] { 0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC }, // kaf
            new ushort[] { 0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0 }, // lam
            new ushort[] { 0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4 }, // meem
            new ushort[] { 0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8 }, // noon
            new ushort[] { 0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC }, // heh
            new ushort[] { 0x0648, 0xFEED, 0xFEEE, 0,      0      }, // waw
            new ushort[] { 0x0649, 0xFEEF, 0xFEF0, 0,      0      }, // alef maksura
            new ushort[] { 0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4 }, // yeh
        };

        // alef variant -> { isolated lam-alef ligature, final lam-alef ligature }
        private static readonly Dictionary<char, ushort[]> LamAlef = new Dictionary<char, ushort[]>
        {
            { '\u0622', new ushort[] { 0xFEF5, 0xFEF6 } },
            { '\u0623', new ushort[] { 0xFEF7, 0xFEF8 } },
            { '\u0625', new ushort[] { 0xFEF9, 0xFEFA } },
            { '\u0627', new ushort[] { 0xFEFB, 0xFEFC } },
        };

        private static readonly Dictionary<char, ushort[]> ByBase = new Dictionary<char, ushort[]>();
        private static readonly Dictionary<char, char> FormToBase = new Dictionary<char, char>();
        private static readonly Dictionary<char, string> LigatureToBase = new Dictionary<char, string>();

        static ArabicShaper()
        {
            foreach (ushort[] row in Rows)
            {
                char baseLetter = (char)row[0];
                ByBase[baseLetter] = row;

                for (int i = 1; i < row.Length; i++)
                {
                    if (row[i] != 0)
                        FormToBase[(char)row[i]] = baseLetter;
                }
            }

            foreach (KeyValuePair<char, ushort[]> pair in LamAlef)
            {
                string expanded = Lam.ToString() + pair.Key;
                LigatureToBase[(char)pair.Value[0]] = expanded;
                LigatureToBase[(char)pair.Value[1]] = expanded;
            }
        }

        /// <summary>True for a base letter that has joined forms (what a keyboard produces).</summary>
        public static bool IsShapeableBase(char c)
        {
            return ByBase.ContainsKey(c);
        }

        /// <summary>The isolated presentation form of a base letter (a glyph every Arabic font has).</summary>
        public static bool TryGetIsolated(char c, out char isolated)
        {
            if (ByBase.TryGetValue(c, out ushort[] row))
            {
                isolated = (char)row[1];
                return true;
            }

            isolated = c;
            return false;
        }

        /// <summary>Combining marks (tashkeel etc.) - they never affect joining.</summary>
        public static bool IsMark(char c)
        {
            return (c >= '\u064B' && c <= '\u065F') ||
                   c == '\u0670' ||
                   (c >= '\u0610' && c <= '\u061A') ||
                   (c >= '\u06D6' && c <= '\u06DC') ||
                   (c >= '\u06DF' && c <= '\u06E4') ||
                   c == '\u06E7' || c == '\u06E8' ||
                   (c >= '\u06EA' && c <= '\u06ED');
        }

        private static bool PrevJoinsForward(string s, int i)
        {
            int k = i - 1;
            while (k >= 0 && IsMark(s[k])) k--;
            if (k < 0) return false;

            char p = s[k];
            if (p == Tatweel) return true;
            return ByBase.TryGetValue(p, out ushort[] row) && row[3] != 0;
        }

        private static bool NextJoinsBackward(string s, int i)
        {
            int k = i + 1;
            while (k < s.Length && IsMark(s[k])) k++;
            if (k >= s.Length) return false;

            char p = s[k];
            if (p == Tatweel) return true;
            return ByBase.TryGetValue(p, out ushort[] row) && row[2] != 0;
        }

        /// <summary>
        /// Logical text to joined presentation forms, still in logical order.
        /// logToShaped[i] is the index in the result of the character that
        /// logical character i became (a lam-alef ligature covers two).
        /// </summary>
        public static string Shape(string logical, out int[] logToShaped)
        {
            int n = logical.Length;
            logToShaped = new int[n];
            var sb = new StringBuilder(n);

            for (int i = 0; i < n; i++)
            {
                char c = logical[i];

                if (!ByBase.TryGetValue(c, out ushort[] row))
                {
                    logToShaped[i] = sb.Length;
                    sb.Append(c);
                    continue;
                }

                bool joinPrev = PrevJoinsForward(logical, i);

                if (c == Lam && i + 1 < n && LamAlef.TryGetValue(logical[i + 1], out ushort[] ligature))
                {
                    sb.Append((char)(joinPrev ? ligature[1] : ligature[0]));
                    logToShaped[i] = sb.Length - 1;
                    logToShaped[i + 1] = sb.Length - 1;
                    i++;
                    continue;
                }

                bool joinNext = row[3] != 0 && NextJoinsBackward(logical, i);

                ushort form;
                if (joinPrev && row[2] != 0)
                    form = joinNext ? row[4] : row[2];
                else
                    form = joinNext ? row[3] : row[1];

                logToShaped[i] = sb.Length;
                sb.Append((char)form);
            }

            return sb.ToString();
        }

        /// <summary>Appends the base letter(s) for a presentation form. Returns how many characters were added.</summary>
        public static int AppendUnshaped(char c, StringBuilder sb)
        {
            if (FormToBase.TryGetValue(c, out char baseLetter))
            {
                sb.Append(baseLetter);
                return 1;
            }

            if (LigatureToBase.TryGetValue(c, out string expanded))
            {
                sb.Append(expanded);
                return expanded.Length;
            }

            sb.Append(c);
            return 1;
        }

        /// <summary>Presentation forms to base letters, character by character (no reordering).</summary>
        public static string UnshapeAll(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            for (int i = 0; i < s.Length; i++)
                AppendUnshaped(s[i], sb);
            return sb.ToString();
        }
    }

    /// <summary>
    /// A deliberately small version of the Unicode bidirectional algorithm,
    /// for a right-to-left paragraph (base level 1). Handles Arabic letters,
    /// numbers, Latin words, neutrals (spaces, punctuation) and bracket
    /// mirroring. It does not handle explicit embeddings/isolates.
    /// </summary>
    internal static class Bidi
    {
        private enum CharType : byte { R, L, EN, AN, ES, ET, CS, ON }

        /// <summary>
        /// order[k] = index in <paramref name="s"/> of the character that goes
        /// at position k after reordering. levels[i] is 1 (right-to-left) or
        /// 2 (left-to-right run inside it) for character i of the input.
        /// The operation is its own inverse, which is what lets ToLogical reuse it.
        /// </summary>
        public static int[] VisualOrder(string s, out byte[] levels)
        {
            int n = s.Length;
            levels = new byte[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            if (n == 0) return order;

            var types = new CharType[n];
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (ArabicShaper.IsMark(c))
                    types[i] = i == 0 ? CharType.R : types[i - 1];
                else
                    types[i] = Classify(c);
            }

            // W4: a single separator between two numbers belongs to the number.
            for (int i = 1; i < n - 1; i++)
            {
                if (types[i] != CharType.CS && types[i] != CharType.ES) continue;

                if (types[i - 1] == CharType.EN && types[i + 1] == CharType.EN)
                    types[i] = CharType.EN;
                else if (types[i] == CharType.CS && types[i - 1] == CharType.AN && types[i + 1] == CharType.AN)
                    types[i] = CharType.AN;
            }

            // W5: terminators (%, $ ...) next to a number belong to the number.
            for (int i = 0; i < n;)
            {
                if (types[i] != CharType.ET) { i++; continue; }

                int j = i;
                while (j < n && types[j] == CharType.ET) j++;

                bool nextToNumber = (i > 0 && types[i - 1] == CharType.EN) ||
                                    (j < n && types[j] == CharType.EN);
                if (nextToNumber)
                    for (int k = i; k < j; k++) types[k] = CharType.EN;

                i = j;
            }

            // W6: any separator/terminator still left is a plain neutral.
            for (int i = 0; i < n; i++)
            {
                if (types[i] == CharType.ES || types[i] == CharType.ET || types[i] == CharType.CS)
                    types[i] = CharType.ON;
            }

            // W7: a number after Latin text is part of that Latin text.
            CharType lastStrong = CharType.R;
            for (int i = 0; i < n; i++)
            {
                if (types[i] == CharType.R || types[i] == CharType.L)
                    lastStrong = types[i];
                else if (types[i] == CharType.EN && lastStrong == CharType.L)
                    types[i] = CharType.L;
            }

            // N1/N2: neutrals take the direction of their neighbours when both
            // agree on Latin; in every other case the paragraph direction (R).
            for (int i = 0; i < n;)
            {
                if (types[i] != CharType.ON) { i++; continue; }

                int j = i;
                while (j < n && types[j] == CharType.ON) j++;

                CharType left = i == 0 ? CharType.R : Direction(types[i - 1]);
                CharType right = j == n ? CharType.R : Direction(types[j]);
                CharType resolved = (left == CharType.L && right == CharType.L) ? CharType.L : CharType.R;

                for (int k = i; k < j; k++) types[k] = resolved;
                i = j;
            }

            for (int i = 0; i < n; i++)
                levels[i] = types[i] == CharType.R ? (byte)1 : (byte)2;

            // L2: reverse the level-2 runs, then reverse everything.
            for (int i = 0; i < n;)
            {
                if (levels[i] != 2) { i++; continue; }

                int j = i;
                while (j < n && levels[j] == 2) j++;

                Array.Reverse(order, i, j - i);
                i = j;
            }

            Array.Reverse(order);
            return order;
        }

        private static CharType Direction(CharType t)
        {
            return t == CharType.L ? CharType.L : CharType.R;
        }

        private static CharType Classify(char c)
        {
            if (c >= '0' && c <= '9') return CharType.EN;
            if (c >= '\u0660' && c <= '\u0669') return CharType.AN;
            if (c >= '\u06F0' && c <= '\u06F9') return CharType.EN;

            switch (c)
            {
                case '+':
                case '-':
                case '\u2212':
                    return CharType.ES;

                case '#':
                case '$':
                case '%':
                case '\u00A2':
                case '\u00A3':
                case '\u00A4':
                case '\u00A5':
                case '\u00B0':
                case '\u20AC':
                case '\u066A':
                    return CharType.ET;

                case ',':
                case '.':
                case '/':
                case ':':
                case '\u060C':
                    return CharType.CS;
            }

            if ((c >= '\u0590' && c <= '\u08FF') ||
                (c >= '\uFB1D' && c <= '\uFDFF') ||
                (c >= '\uFE70' && c <= '\uFEFF') ||
                c == '\u200F')
                return CharType.R;

            if (char.IsLetter(c)) return CharType.L;

            return CharType.ON;
        }

        /// <summary>A strong right-to-left letter (Arabic/Hebrew), not a digit, mark or neutral.</summary>
        public static bool IsStrongRtl(char c)
        {
            return !ArabicShaper.IsMark(c) && Classify(c) == CharType.R;
        }

        public static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '{': return '}';
                case '}': return '{';
                case '<': return '>';
                case '>': return '<';
                case '\u00AB': return '\u00BB';
                case '\u00BB': return '\u00AB';
                default: return c;
            }
        }
    }

    /// <summary>
    /// Conversion between logical text (what the keyboard produces) and the
    /// visual text stored in the translation and drawn by the game.
    /// </summary>
    public static class ArabicText
    {
        /// <summary>Logical to visual. logToVisual[i] = index in the result of logical character i.</summary>
        public static string ToVisual(string logical, out int[] logToVisual)
        {
            string shaped = ArabicShaper.Shape(logical, out int[] logToShaped);
            int n = shaped.Length;

            int[] order = Bidi.VisualOrder(shaped, out byte[] levels);

            var chars = new char[n];
            var shapedToVisual = new int[n];

            for (int k = 0; k < n; k++)
            {
                int src = order[k];
                char ch = shaped[src];
                if (levels[src] == 1) ch = Bidi.Mirror(ch);

                chars[k] = ch;
                shapedToVisual[src] = k;
            }

            logToVisual = new int[logical.Length];
            for (int i = 0; i < logical.Length; i++)
                logToVisual[i] = shapedToVisual[logToShaped[i]];

            return new string(chars);
        }

        /// <summary>Visual to logical. logToVisual[i] = index in <paramref name="visual"/> that logical character i came from.</summary>
        public static string ToLogical(string visual, out int[] logToVisual)
        {
            int[] order = Bidi.VisualOrder(visual, out byte[] levels);

            var sb = new StringBuilder(visual.Length + 2);
            var map = new List<int>(visual.Length + 2);

            for (int k = 0; k < order.Length; k++)
            {
                int src = order[k];
                char ch = visual[src];
                if (levels[src] == 1) ch = Bidi.Mirror(ch);

                int added = ArabicShaper.AppendUnshaped(ch, sb);
                for (int a = 0; a < added; a++) map.Add(src);
            }

            logToVisual = map.ToArray();
            return sb.ToString();
        }

        public static string ToLogical(string visual)
        {
            return ToLogical(visual, out _);
        }

        /// <summary>
        /// True when the model can represent this stored text without moving
        /// anything (only the joined forms are allowed to differ). Text that
        /// fails this is left alone rather than risk scrambling it.
        /// </summary>
        public static bool IsOrderConsistent(string visual)
        {
            if (string.IsNullOrEmpty(visual)) return true;

            string logical = ToLogical(visual, out _);
            string rebuilt = ToVisual(logical, out _);
            return ArabicShaper.UnshapeAll(rebuilt) == ArabicShaper.UnshapeAll(visual);
        }

        /// <summary>
        /// Builds the visual text for <paramref name="logical"/> and the caret
        /// position that continues typing after its last character. Fails if
        /// the result would not decode back to the same logical text.
        /// </summary>
        public static bool TryBuildVisual(string logical, out string visual, out int caret)
        {
            visual = ToVisual(logical, out int[] map);
            caret = 0;

            if (ToLogical(visual, out _) != logical) return false;
            if (logical.Length == 0) return true;

            int index = map[logical.Length - 1];
            Bidi.VisualOrder(visual, out byte[] levels);

            // A right-to-left character continues to its left, a left-to-right one to its right.
            caret = levels[index] == 1 ? index : index + 1;
            return true;
        }

        /// <summary>
        /// True when the caret is where the next typed character becomes the
        /// LAST character in reading order: at the left edge of the text, or
        /// at the end of a left-to-right run (digits/Latin) at the left edge.
        /// </summary>
        public static bool IsAtLogicalEnd(string visual, int caret)
        {
            if (caret <= 0) return true;
            if (string.IsNullOrEmpty(visual)) return true;

            Bidi.VisualOrder(visual, out byte[] levels);

            int run = 0;
            while (run < levels.Length && levels[run] == 2) run++;

            return caret == run && run < levels.Length;
        }

        public static int CommonPrefixLength(string a, string b)
        {
            int max = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < max && a[i] == b[i]) i++;
            return i;
        }

        /// <summary>
        /// Re-shapes and re-orders the line of <paramref name="newText"/> that
        /// contains <paramref name="anchor"/> after an edit made by the game's
        /// text field (which only knows visual order). Only that line is
        /// touched. caret is the new caret index for the whole text.
        /// </summary>
        public static bool TryRenormalize(string oldText, string newText, int anchor, bool inserted,
            out string result, out int caret)
        {
            result = newText;
            caret = anchor;

            if (string.IsNullOrEmpty(newText)) return false;

            if (anchor < 0) anchor = 0;
            if (anchor > newText.Length) anchor = newText.Length;

            int lineStart = anchor > 0 ? newText.LastIndexOf('\n', anchor - 1) + 1 : 0;
            int lineEnd = newText.IndexOf('\n', anchor);
            if (lineEnd < 0) lineEnd = newText.Length;

            string line = newText.Substring(lineStart, lineEnd - lineStart);
            int local = anchor - lineStart;

            if (!ArabicDetector.ContainsArabic(line)) return false;

            if (oldText != null && CountNewlines(oldText, oldText.Length) == CountNewlines(newText, newText.Length))
            {
                string oldLine = LineAt(oldText, CountNewlines(newText, lineStart));
                if (!IsOrderConsistent(oldLine)) return false;
            }

            string logical = ToLogical(line, out int[] logToOld);

            int k = logical.Length;
            if (local < line.Length)
            {
                for (int i = 0; i < logToOld.Length; i++)
                {
                    if (logToOld[i] == local) { k = i; break; }
                }
            }

            string visual = ToVisual(logical, out int[] logToNew);
            if (ToLogical(visual, out _) != logical) return false;

            int newLocal = k < logical.Length ? logToNew[k] : visual.Length;

            if (inserted && newLocal < visual.Length)
            {
                Bidi.VisualOrder(visual, out byte[] levels);
                if (levels[newLocal] == 2) newLocal++;
            }

            result = newText.Substring(0, lineStart) + visual + newText.Substring(lineEnd);
            caret = lineStart + newLocal;
            return true;
        }

        private static int CountNewlines(string s, int length)
        {
            int count = 0;
            for (int i = 0; i < length && i < s.Length; i++)
                if (s[i] == '\n') count++;
            return count;
        }

        private static string LineAt(string text, int lineIndex)
        {
            int start = 0;
            for (int i = 0; i < lineIndex; i++)
            {
                int nl = text.IndexOf('\n', start);
                if (nl < 0) return string.Empty;
                start = nl + 1;
            }

            int end = text.IndexOf('\n', start);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }

    /// <summary>
    /// Form-insensitive matching for search boxes. A half-typed word shapes
    /// differently from the same letters inside a longer word (the last
    /// letter gets its final form instead of its medial one), so shaped
    /// strings cannot be compared with IndexOf. Both sides are brought back to
    /// logical base letters, and spelling variants are folded together.
    ///
    /// Performance: the query's words are computed once per query (not once
    /// per list entry), and label skeletons are kept in a bounded
    /// generational cache (no mass-clear hitch). The game's own
    /// QuickSearchFilter also caches each label's result, so this only runs
    /// once per label per query.
    ///
    /// MAIN THREAD ONLY (shared caches).
    /// </summary>
    public static class ArabicSearch
    {
        private const int MaxCacheEntries = 8192;
        private static readonly GenerationalCache<string, string> Cache =
            new GenerationalCache<string, string>(MaxCacheEntries, StringComparer.Ordinal);
        private static readonly char[] Separators = { ' ', '\t', '\n' };

        private static string lastQuery;
        private static string[] lastQueryWords;

        public static bool Matches(string queryVisual, string textVisual)
        {
            string[] words = QueryWords(queryVisual);
            if (words.Length == 0)
                return true;

            string text = Skeleton(textVisual);

            for (int i = 0; i < words.Length; i++)
            {
                if (text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        private static string[] QueryWords(string queryVisual)
        {
            if (!ReferenceEquals(queryVisual, lastQuery) && queryVisual != lastQuery)
            {
                lastQueryWords = Skeleton(queryVisual).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                lastQuery = queryVisual;
            }
            return lastQueryWords;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            lastQuery = null;
            lastQueryWords = null;
        }

        private static string Skeleton(string visual)
        {
            if (string.IsNullOrEmpty(visual))
                return string.Empty;

            if (Cache.TryGetValue(visual, out string cached))
                return cached;

            string logical = ArabicDetector.ContainsArabic(visual) ? ArabicText.ToLogical(visual, out _) : visual;
            var sb = new StringBuilder(logical.Length);

            foreach (char c in logical)
            {
                if (ArabicShaper.IsMark(c) || c == '\u0640') continue; // diacritics, tatweel

                switch (c)
                {
                    case '\u0622': // alef madda / hamza above / hamza below -> alef
                    case '\u0623':
                    case '\u0625':
                        sb.Append('\u0627');
                        break;
                    case '\u0649': // alef maksura -> yeh
                        sb.Append('\u064A');
                        break;
                    case '\u0629': // teh marbuta -> heh
                        sb.Append('\u0647');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            string skeleton = sb.ToString();
            Cache.Set(visual, skeleton);
            return skeleton;
        }
    }
}
