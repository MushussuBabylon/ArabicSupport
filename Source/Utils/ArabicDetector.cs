using System.Runtime.CompilerServices;

namespace ArabicSupport.Utils
{
    /// <summary>
    /// Detects whether a string contains Arabic (or related RTL) characters.
    ///
    /// Detection is a linear scan with an early exit as soon as a match is
    /// found. That's already cheap enough that a locked Dictionary+LRU
    /// content cache in front of it doesn't pay for itself: computing a
    /// string's hash code (needed for any dictionary lookup) already
    /// requires touching every character, so it can't be cheaper than the
    /// scan for confirmed non-Arabic text, and it's strictly slower than
    /// the scan's early exit for confirmed Arabic text — plus it adds a lock.
    ///
    /// What IS worth keeping is a lock-free identity cache: many RimWorld
    /// labels are redrawn every frame from the exact same string instance,
    /// and ConditionalWeakTable answers that in O(1) without ever touching
    /// the string's contents and without pinning the string in memory.
    /// </summary>
    public static class ArabicDetector
    {
        private static readonly ConditionalWeakTable<string, DetectionResult> Cache =
            new ConditionalWeakTable<string, DetectionResult>();

        private static readonly ConditionalWeakTable<string, DetectionResult>.CreateValueCallback Factory =
            CreateResult;

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

            return Cache.GetValue(text, Factory).Value;
        }

        private static DetectionResult CreateResult(string text)
        {
            return Scan(text) ? TrueResult : FalseResult;
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
    }
}
