using UnityEngine;

namespace ArabicSupport.Fonts
{
    /// <summary>
    /// Puts harakat (fatha, tanween, shadda...) on top of their letter.
    ///
    /// Unity's IMGUI cannot position combining marks: it draws a haraka
    /// wherever the pen is, so in the translation's visual-order text (where a
    /// haraka comes right BEFORE its letter) it lands at the letter's edge,
    /// e.g. the tanween of "دائماً" sat next to the alef instead of above it.
    ///
    /// Fonts built by Tools/build_fonts.py contain a copy of every haraka
    /// already moved onto every letter (placed with the font's own anchors),
    /// at fixed code points - no extra file is needed:
    ///     U+E400 + slot * 16 + k
    ///     slot: 0-124 = U+FE80-U+FEFC, 125-166 = U+0621-U+064A
    ///     k:    0-11  = the haraka (order of Marks below)
    ///           12-15 = fathatan/dammatan/fatha/damma drawn on top of a shadda
    /// Must match HARAKAT_* in Tools/build_fonts.py.
    ///
    /// Apply() runs inside the wrapping pipeline, i.e. only on a cache miss.
    /// Fonts without the copies (game font, older bundles) are detected in
    /// Refresh() and left alone.
    /// </summary>
    public static class HarakatMapper
    {
        private const int Pua = 0xE400;
        private const char Shadda = '\u0651';

        public static bool Active { get; private set; }

        private static bool IsMark(char c) => (c >= '\u064B' && c <= '\u0655') || c == '\u0670';

        private static int MarkIndex(char c)
        {
            if (c >= '\u064B' && c <= '\u0652') return c - '\u064B';
            switch (c)
            {
                case '\u0670': return 8;
                case '\u0653': return 9;
                case '\u0654': return 10;
                case '\u0655': return 11;
                default: return -1;
            }
        }

        // Fathatan, dammatan, fatha, damma stack on top of a shadda.
        private static int OnShaddaIndex(char c)
        {
            switch (c)
            {
                case '\u064B': return 12;
                case '\u064C': return 13;
                case '\u064E': return 14;
                case '\u064F': return 15;
                default: return -1;
            }
        }

        private static int BaseSlot(char c)
        {
            if (c >= '\uFE80' && c <= '\uFEFC') return c - 0xFE80;
            if (c >= '\u0621' && c <= '\u064A') return 125 + (c - 0x0621);
            return -1;
        }

        /// <summary>Called after the font changes (main thread).</summary>
        public static void Refresh(Font font, bool enabled)
        {
            // Probe: tanween fath on a final alef, the most common pair.
            char probe = (char)(Pua + BaseSlot('\uFE8E') * 16 + 0);
            bool has = false;
            try { has = font != null && font.HasCharacter(probe); }
            catch { has = false; }
            Active = enabled && has;
        }

        public static void Disable() => Active = false;

        /// <summary>
        /// Replaces each haraka with the copy made for the letter after it
        /// (visual order). Returns the same instance when nothing changes.
        /// </summary>
        public static string Apply(string text)
        {
            if (!Active || string.IsNullOrEmpty(text))
                return text;

            int n = text.Length;
            int i = 0;
            while (i < n && !IsMark(text[i]))
                i++;
            if (i == n)
                return text;

            char[] buffer = null;

            while (i < n)
            {
                if (!IsMark(text[i]))
                {
                    i++;
                    continue;
                }

                // A run of harakat (e.g. fatha + shadda) belongs to the letter after it.
                int j = i;
                bool hasShadda = false;
                while (j < n && IsMark(text[j]))
                {
                    if (text[j] == Shadda)
                        hasShadda = true;
                    j++;
                }

                int slot = j < n ? BaseSlot(text[j]) : -1;
                if (slot >= 0)
                {
                    for (int k = i; k < j; k++)
                    {
                        char c = text[k];
                        int idx = hasShadda ? OnShaddaIndex(c) : -1;
                        if (idx < 0)
                            idx = MarkIndex(c);
                        if (idx < 0)
                            continue;

                        if (buffer == null)
                            buffer = text.ToCharArray();
                        buffer[k] = (char)(Pua + slot * 16 + idx);
                    }
                }

                i = j;
            }

            return buffer == null ? text : new string(buffer);
        }
    }
}
