using System;
using System.Collections.Generic;
using ArabicSupport.Caching;
using UnityEngine;
using Verse;

namespace ArabicSupport.Core
{
    public static class TextMeasurer
    {
        private struct WidthKey : IEquatable<WidthKey>
        {
            public string Text;
            public GameFont Font;

            public bool Equals(WidthKey other)
            {
                return Text == other.Text && Font == other.Font;
            }

            public override bool Equals(object obj)
            {
                return obj is WidthKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (Text?.GetHashCode() ?? 0);
                    hash = hash * 31 + (int)Font;
                    return hash;
                }
            }
        }

        // Text.CalcSize is by far the most expensive operation in this
        // pipeline. Words repeat heavily across RimWorld's UI, so this is
        // what pays that cost once instead of on every wrap pass.
        //
        // Generational rather than reset-on-cap: a miss here means an actual
        // Text.CalcSize call, and clearing everything at once would make
        // every visible word get remeasured in the same frame (a hitch).
        // See GenerationalCache for why this replaced the locked LRU list.
        private const int MaxWidthCacheEntries = 8192;

        private static readonly GenerationalCache<WidthKey, float> widthCache =
            new GenerationalCache<WidthKey, float>(MaxWidthCacheEntries);

        public static float MeasureWidth(string text, List<string> placeholders)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            string restored = RestorePlaceholders(text, placeholders);

            if (string.IsNullOrEmpty(restored))
                return 0f;

            return MeasureRestored(restored);
        }

        public static float MeasureWidth(string text, List<string> placeholders, GameFont font)
        {
            GameFont previous = Text.Font;

            try
            {
                Text.Font = font;
                return MeasureWidth(text, placeholders);
            }
            finally
            {
                Text.Font = previous;
            }
        }

        // MAIN THREAD ONLY: every call path into this class has passed a
        // UnityData.IsInMainThread check upstream (the Harmony patches).
        // Don't call it from a new site without the same guard.
        private static float MeasureRestored(string text)
        {
            var key = new WidthKey { Text = text, Font = Text.Font };

            if (widthCache.TryGetValue(key, out float cached))
                return cached;

            float width = Text.CalcSize(text).x;
            widthCache.Set(key, width);
            return width;
        }

        /// <summary>
        /// Restores visible placeholders for measurement but removes rich-text
        /// tags because markup itself has zero visible width.
        /// </summary>
        public static string RestorePlaceholders(string text, List<string> placeholders)
        {
            return PlaceholderProtector.RestoreForMeasurement(text, placeholders);
        }

        public static void ClearCache()
        {
            widthCache.Clear();
        }
    }
}
