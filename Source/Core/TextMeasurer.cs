using System;
using System.Collections.Generic;
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

        private static readonly object _lock = new object();

        // Text.CalcSize is by far the most expensive operation in this
        // pipeline. Words repeat heavily across RimWorld's UI, so this is
        // what pays that cost once instead of on every wrap pass.
        //
        // Locked rather than lock-free: unlike the identity caches
        // elsewhere, this is keyed by content, so there's no lock-free
        // option — kept consistent with this codebase's existing
        // defensive locking around shared mutable caches.
        private static readonly Dictionary<WidthKey, float> widthCache =
            new Dictionary<WidthKey, float>();

        // Word vocabulary is small relative to full sentences, and a miss
        // is cheap to recompute, so a full reset past a generous cap is
        // used instead of a proper LRU.
        private const int MaxWidthCacheEntries = 8192;

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

        // The lock below only protects widthCache itself — it does NOT make
        // Text.CalcSize or the global Text.Font it reads thread-safe, and
        // it isn't meant to. This class relies on every call path into it
        // already having passed a UnityData.IsInMainThread check upstream,
        // in the three Harmony patches — this class doesn't enforce that
        // itself, so don't call it from a new site without the same guard.
        private static float MeasureRestored(string text)
        {
            var key = new WidthKey { Text = text, Font = Text.Font };

            lock (_lock)
            {
                if (widthCache.TryGetValue(key, out float cached))
                    return cached;
            }

            // Don't hold the lock across the Unity call.
            float width = Text.CalcSize(text).x;

            lock (_lock)
            {
                if (widthCache.Count >= MaxWidthCacheEntries)
                    widthCache.Clear();

                widthCache[key] = width;
            }

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
            lock (_lock)
            {
                widthCache.Clear();
            }
        }
    }
}
