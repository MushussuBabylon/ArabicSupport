using ArabicSupport.Core;

namespace ArabicSupport.Caching
{
    /// <summary>
    /// One place to empty the text caches. The font manager has its own private
    /// copy of this for font changes; the settings window uses these for the
    /// wrapping options, whose results the processed-text cache would otherwise
    /// keep serving.
    ///
    /// MAIN THREAD ONLY, like the caches themselves.
    /// </summary>
    public static class TextCaches
    {
        /// <summary>Forget every wrapped label (a wrapping option changed). Measured widths stay valid.</summary>
        public static void ClearProcessed()
        {
            ProcessedTextCache.Clear();
        }

        /// <summary>Forget everything: wrapped labels, measured widths and space widths.</summary>
        public static void ClearAll()
        {
            ProcessedTextCache.Clear();
            TextMeasurer.ClearCache();
            LineWrapper.ClearCache();
        }
    }
}
