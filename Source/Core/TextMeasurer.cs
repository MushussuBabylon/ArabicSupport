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

        private sealed class WidthEntry
        {
            public WidthKey Key;
            public float Width;
        }

        private static readonly object _lock = new object();

        // Text.CalcSize is by far the most expensive operation in this
        // pipeline. Words repeat heavily across RimWorld's UI, so this is
        // what pays that cost once instead of on every wrap pass.
        //
        // True LRU rather than a full reset-on-cap: unlike ArabicDetector's
        // content cache (where a miss just re-runs a cheap linear scan), a
        // miss here means an actual Text.CalcSize call. Clearing everything
        // at once would mean every visible word gets remeasured in the same
        // frame — a visible hitch. Evicting one coldest entry at a time
        // spreads that cost out instead.
        private static readonly Dictionary<WidthKey, LinkedListNode<WidthEntry>> widthCache =
            new Dictionary<WidthKey, LinkedListNode<WidthEntry>>();
        private static readonly LinkedList<WidthEntry> lruOrder = new LinkedList<WidthEntry>();

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

        // The lock below only protects widthCache/lruOrder — it does NOT
        // make Text.CalcSize or the global Text.Font it reads thread-safe,
        // and it isn't meant to. This class relies on every call path into
        // it already having passed a UnityData.IsInMainThread check
        // upstream, in the three Harmony patches — this class doesn't
        // enforce that itself, so don't call it from a new site without the
        // same guard.
        private static float MeasureRestored(string text)
        {
            var key = new WidthKey { Text = text, Font = Text.Font };

            lock (_lock)
            {
                if (widthCache.TryGetValue(key, out LinkedListNode<WidthEntry> node))
                {
                    lruOrder.Remove(node);
                    lruOrder.AddFirst(node);
                    return node.Value.Width;
                }
            }

            // Don't hold the lock across the Unity call.
            float width = Text.CalcSize(text).x;

            lock (_lock)
            {
                // Another thread may have computed and inserted this exact
                // key while we didn't hold the lock — check again rather
                // than inserting a second, orphaned LRU node for it.
                if (widthCache.TryGetValue(key, out LinkedListNode<WidthEntry> existing))
                {
                    lruOrder.Remove(existing);
                    lruOrder.AddFirst(existing);
                    return existing.Value.Width;
                }

                if (widthCache.Count >= MaxWidthCacheEntries)
                {
                    LinkedListNode<WidthEntry> coldest = lruOrder.Last;
                    if (coldest != null)
                    {
                        lruOrder.RemoveLast();
                        widthCache.Remove(coldest.Value.Key);
                    }
                }

                var newNode = new LinkedListNode<WidthEntry>(new WidthEntry { Key = key, Width = width });
                lruOrder.AddFirst(newNode);
                widthCache[key] = newNode;

                return width;
            }
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
                lruOrder.Clear();
            }
        }
    }
}
