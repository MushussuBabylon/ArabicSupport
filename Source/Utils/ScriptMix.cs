using ArabicSupport.TextInput;

namespace ArabicSupport.Utils
{
    /// <summary>
    /// Tells right-to-left letters from the rest. Used by the line wrapper to decide
    /// how to treat a word (see LatinRunGlue).
    /// </summary>
    internal static class ScriptMix
    {
        /// <summary>A letter of a right-to-left script (Arabic, Hebrew...), joined or not.</summary>
        public static bool IsRtlLetter(char c)
        {
            if (Bidi.IsStrongRtl(c))
                return true;

            // Letters of the Hebrew / Arabic blocks that the bidi table does not call strong.
            bool block = (c >= '\u0590' && c <= '\u08FF') ||
                         (c >= '\uFB1D' && c <= '\uFDFF') ||
                         (c >= '\uFE70' && c <= '\uFEFF');
            return block && char.IsLetter(c);
        }
    }
}
