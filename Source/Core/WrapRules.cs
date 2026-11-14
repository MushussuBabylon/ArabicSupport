using ArabicSupport.Settings;
using UnityEngine;

namespace ArabicSupport.Core
{
    /// <summary>
    /// The wrapping choices from the settings window, read at the moment the
    /// wrapper needs them. Everything falls back to the defaults while the
    /// settings are not loaded yet (a label can be drawn before the mod object
    /// exists), so a missing settings object never changes how text is wrapped.
    ///
    /// Changing any of these invalidates the processed-text cache; the settings
    /// window does that through TextCaches.ClearAll().
    /// </summary>
    internal static class WrapRules
    {
        private static ArabicSupportSettings Current => ArabicSupportMod.Settings;

        /// <summary>English words and numbers inside Arabic text stay on one line together.</summary>
        public static bool KeepLatinRuns
        {
            get { var s = Current; return s == null || s.keepLatinRuns; }
        }

        /// <summary>A word wider than the whole line is cut, in reading order, instead of being left to the game.</summary>
        public static bool SplitWideWords
        {
            get { var s = Current; return s == null || s.splitWideWords; }
        }

        /// <summary>
        /// The width the wrapper really uses for a label that is <paramref name="width"/> pixels wide.
        /// 0 means "do not wrap" (the same value the patches already pass for one-line labels).
        /// </summary>
        public static float EffectiveWidth(float width)
        {
            if (width <= 0f)
                return 0f;

            var s = Current;
            if (s == null)
                return width;

            if (!s.wrapByPixels)
                return 0f;

            if (s.wrapMargin > 0)
                width = Mathf.Max(1f, width - s.wrapMargin);

            return width;
        }
    }
}
