namespace ArabicSupport.Utils
{
    /// <summary>
    /// Detects whether a string contains Arabic (or related RTL) characters.
    ///
    /// This runs on every Widgets.Label and Text.CalcHeight call in the whole
    /// game - thousands of times per frame - so it is kept as cheap as
    /// possible: a plain scan with no cache, no lock and no allocation.
    ///
    /// Why there is no cache: ordinary text (Latin letters, digits,
    /// punctuation) is rejected with a single comparison per character, and
    /// Arabic text is accepted at its first Arabic character. That is about
    /// as cheap as any cache lookup, and it avoids the memory and
    /// garbage-collector cost of remembering every string instance the game
    /// creates (many labels are rebuilt as brand-new strings every frame,
    /// which made the old two-level cache miss and grow constantly).
    ///
    /// Thread-safe, because it touches no shared state.
    /// </summary>
    public static class ArabicDetector
    {
        public static bool ContainsArabic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Every range below starts at U+0590, so ordinary characters
                // are rejected right here with one comparison.
                if (c < '\u0590')
                    continue;

                if (c <= '\u06FF' ||                    // Hebrew + Arabic (U+0590-U+06FF)
                    (c >= '\u0750' && c <= '\u077F') || // Arabic Supplement
                    (c >= '\u0870' && c <= '\u08FF') || // Arabic Extended-B + Extended-A
                    (c >= '\uFB50' && c <= '\uFDFF') || // Arabic Presentation Forms-A
                    (c >= '\uFE70' && c <= '\uFEFF'))   // Arabic Presentation Forms-B
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Kept so any existing caller still compiles. There are no caches
        /// left to clear.
        /// </summary>
        public static void ClearCaches()
        {
        }
    }
}
