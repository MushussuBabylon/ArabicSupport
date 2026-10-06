using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ArabicSupport.Caching
{
    /// <summary>
    /// Cache of already-wrapped text, in two levels.
    ///
    /// Level 1 (fast path): a small fixed-size table looked up by the exact
    /// string instance, width and font. It serves the overwhelming majority
    /// of calls - the same widget redrawing the same text at the same width,
    /// many times per frame. A hit is a few array reads: no lock, no memory
    /// allocation, nothing for the garbage collector to track.
    ///
    /// Level 2 (slow path): a true-LRU dictionary keyed by text CONTENT. It
    /// catches text that is rebuilt into a new string instance every frame
    /// (formatted / interpolated labels), and refills level 1 when it hits.
    ///
    /// The previous version used a ConditionalWeakTable for level 1. That
    /// makes the garbage collector track every string instance it ever saw,
    /// which is expensive for text rebuilt every frame (one new tracked
    /// entry and one new object per label, per frame).
    ///
    /// MAIN THREAD ONLY. Level 1 is a plain array and is not safe against
    /// concurrent use. Every path into this class already passes a
    /// UnityData.IsInMainThread check (the three Harmony patches and
    /// FullPipeline.Process) - do not call it from a new place without the
    /// same guard.
    /// </summary>
    public static class ProcessedTextCache
    {
        private static readonly object _lock = new object();

        private struct CacheKey : IEquatable<CacheKey>
        {
            public string Text;
            public int Width;
            public GameFont Font;

            // IEquatable<CacheKey> matters here: without it, Dictionary
            // falls back to a boxing comparer and boxes this struct on
            // every TryGetValue/Remove/index-set.
            public bool Equals(CacheKey other)
            {
                return Text == other.Text && Width == other.Width && Font == other.Font;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (Text?.GetHashCode() ?? 0);
                    hash = hash * 31 + Width;
                    hash = hash * 31 + (int)Font;
                    return hash;
                }
            }
        }

        private class CacheEntry
        {
            public CacheKey Key;
            public string Value;
        }

        private static readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> cache =
            new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
        private static readonly LinkedList<CacheEntry> lruOrder = new LinkedList<CacheEntry>();

        private const int MaxCacheEntries = 5000;

        // DO NOT CHANGE - 4px bucketing is load-bearing for correct wrapping.
        private const int WidthBucketPx = 4;

        private static int BucketWidth(float width)
        {
            return Mathf.RoundToInt(width / WidthBucketPx) * WidthBucketPx;
        }

        // ---- Level 1: fast table, looked up by exact string instance ----

        private struct FastSlot
        {
            public string Original; // the exact string instance that was drawn
            public string Value;    // its wrapped result
            public int Width;       // bucketed width
            public GameFont Font;
        }

        // Must be a power of two (the index is masked, not divided).
        private const int FastSlotCount = 2048;
        private const int FastSlotMask = FastSlotCount - 1;

        private static readonly FastSlot[] fastSlots = new FastSlot[FastSlotCount];

        // Cheap, allocation-free slot choice from the string's length, four
        // sample characters, the bucketed width and the font. Two different
        // strings can land in the same slot; that only costs a level-2
        // lookup, because a slot is only trusted when it holds the exact
        // same string instance - a collision can never return wrong text.
        private static int FastIndex(string text, int bucketedWidth, GameFont font)
        {
            int len = text.Length; // callers guarantee len >= 1

            unchecked
            {
                int h = len;
                h = h * 31 + text[0];
                h = h * 31 + text[len >> 2];
                h = h * 31 + text[len >> 1];
                h = h * 31 + text[len - 1];
                h = h * 31 + bucketedWidth;
                h = h * 31 + (int)font;
                return (h ^ (h >> 16)) & FastSlotMask;
            }
        }

        private static void PutFast(int slotIndex, string originalText, int bucketedWidth, GameFont font, string value)
        {
            fastSlots[slotIndex] = new FastSlot
            {
                Original = originalText,
                Value = value,
                Width = bucketedWidth,
                Font = font
            };
        }

        public static string TryGet(string originalText, float width, GameFont font)
        {
            if (string.IsNullOrEmpty(originalText))
                return null;

            int bucketedWidth = BucketWidth(width);
            int slotIndex = FastIndex(originalText, bucketedWidth, font);

            // Level 1. The reference comparison is the whole point: the same
            // widget redrawing the same string instance is by far the most
            // common case.
            FastSlot slot = fastSlots[slotIndex];
            if ((object)slot.Original == (object)originalText &&
                slot.Width == bucketedWidth &&
                slot.Font == font)
            {
                return slot.Value;
            }

            // Level 2: by content, with true LRU ordering.
            lock (_lock)
            {
                var key = new CacheKey { Text = originalText, Width = bucketedWidth, Font = font };
                if (cache.TryGetValue(key, out LinkedListNode<CacheEntry> node))
                {
                    lruOrder.Remove(node);
                    lruOrder.AddFirst(node);

                    string value = node.Value.Value;
                    PutFast(slotIndex, originalText, bucketedWidth, font, value);
                    return value;
                }
                return null;
            }
        }

        public static void Store(string originalText, float width, GameFont font, string processedText)
        {
            lock (_lock)
            {
                int bucketedWidth = BucketWidth(width);
                var key = new CacheKey { Text = originalText, Width = bucketedWidth, Font = font };

                if (cache.TryGetValue(key, out LinkedListNode<CacheEntry> existingNode))
                {
                    existingNode.Value.Value = processedText;
                    lruOrder.Remove(existingNode);
                    lruOrder.AddFirst(existingNode);
                }
                else
                {
                    if (cache.Count >= MaxCacheEntries)
                    {
                        LinkedListNode<CacheEntry> coldest = lruOrder.Last;
                        if (coldest != null)
                        {
                            lruOrder.RemoveLast();
                            cache.Remove(coldest.Value.Key);
                        }
                    }
                    var newNode = new LinkedListNode<CacheEntry>(new CacheEntry { Key = key, Value = processedText });
                    lruOrder.AddFirst(newNode);
                    cache[key] = newNode;
                }

                if (!string.IsNullOrEmpty(originalText))
                    PutFast(FastIndex(originalText, bucketedWidth, font), originalText, bucketedWidth, font, processedText);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                cache.Clear();
                lruOrder.Clear();
                Array.Clear(fastSlots, 0, fastSlots.Length);
            }
        }
    }
}
