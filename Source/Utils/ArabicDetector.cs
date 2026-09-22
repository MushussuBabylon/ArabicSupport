using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ArabicSupport.Utils
{
    /// <summary>
    /// Detects whether a string contains Arabic (or related RTL) characters.
    ///
    /// Two-level cache:
    ///
    /// 1. Identity cache (ConditionalWeakTable) — O(1), lock-free, never
    ///    pins the string in memory. Covers the overwhelming majority of
    ///    calls: the same widget redrawing the exact same string instance
    ///    every frame.
    ///
    /// 2. Content cache (ConcurrentDictionary) — catches the remaining case
    ///    where the same TEXT is rebuilt into a NEW string instance every
    ///    frame (e.g. some interpolated/formatted labels), which the
    ///    identity cache alone would miss every single time. Lock-free via
    ///    ConcurrentDictionary rather than a manually locked Dictionary+LRU,
    ///    since a Scan() miss is cheap enough that a full reset past a
    ///    generous cap is a fine substitute for real eviction — unlike
    ///    TextMeasurer's width cache, where each entry is expensive
    ///    (Text.CalcSize) to recompute.
    /// </summary>
    public static class ArabicDetector
    {
        private static readonly ConditionalWeakTable<string, DetectionResult> IdentityCache =
            new ConditionalWeakTable<string, DetectionResult>();

        private static readonly ConditionalWeakTable<string, DetectionResult>.CreateValueCallback Factory =
            CreateResult;

        private static readonly ConcurrentDictionary<string, bool> ContentCache =
            new ConcurrentDictionary<string, bool>();

        // Word/line vocabulary is small relative to the number of distinct
        // string instances seen, so this rarely fills — but if it does, a
        // full reset is cheap because a Scan() miss is cheap.
        private const int MaxContentCacheEntries = 4096;

        private sealed class DetectionResult
        {
            public readonly bool Value;

            public DetectionResult(bool value)
            {
                Value = value;
            }
        }

        private static readonly DetectionResult TrueResult = new DetectionResult(true);
        private static readonly DetectionResult FalseResult = new DetectionResult(false);

        public static bool ContainsArabic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return IdentityCache.GetValue(text, Factory).Value;
        }

        private static DetectionResult CreateResult(string text)
        {
            if (ContentCache.TryGetValue(text, out bool cached))
                return cached ? TrueResult : FalseResult;

            bool result = Scan(text);

            if (ContentCache.Count >= MaxContentCacheEntries)
                ContentCache.Clear();

            ContentCache[text] = result;

            return result ? TrueResult : FalseResult;
        }

        private static bool Scan(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if ((c >= '\u0600' && c <= '\u06FF') || // Arabic
                    (c >= '\u0750' && c <= '\u077F') || // Arabic Supplement
                    (c >= '\u0870' && c <= '\u089F') || // Arabic Extended-B
                    (c >= '\u08A0' && c <= '\u08FF') || // Arabic Extended-A
                    (c >= '\u0590' && c <= '\u05FF') || // Hebrew
                    (c >= '\uFE70' && c <= '\uFEFF') || // Arabic Presentation Forms-B
                    (c >= '\uFB50' && c <= '\uFDFF'))   // Arabic Presentation Forms-A
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Clears the content cache. The identity cache is left alone — its
        /// entries are scoped to live string instances via
        /// ConditionalWeakTable and pose no memory-growth risk on their own.
        /// </summary>
        public static void ClearCaches()
        {
            ContentCache.Clear();
        }
    }
}
