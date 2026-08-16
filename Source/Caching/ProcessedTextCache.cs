using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace ArabicSupport.Caching
{
    public static class ProcessedTextCache
    {
        private struct CacheKey
        {
            public string Text;
            public int Width;
            public GameFont Font;

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

            public override bool Equals(object obj)
            {
                if (!(obj is CacheKey other))
                    return false;

                return Text == other.Text
                    && Width == other.Width
                    && Font == other.Font;
            }
        }

        private class LastResult
        {
            public int Width;
            public GameFont Font;
            public string Value;
        }

        private class CacheEntry
        {
            public CacheKey Key;
            public string Value;
        }

        // True LRU: Dictionary gives O(1) lookup, LinkedList gives O(1)
        // "move to front on use" and O(1) "evict the coldest entry when
        // full". Each node carries its own key so eviction from the tail
        // can also remove the matching Dictionary entry directly.
        private static readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> cache =
            new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
        private static readonly LinkedList<CacheEntry> lruOrder = new LinkedList<CacheEntry>();

        // Fast path: ConditionalWeakTable keyed by string identity. Lives as
        // long as the string instance itself, self-evicting via GC.
        private static ConditionalWeakTable<string, LastResult> lastResultByText =
            new ConditionalWeakTable<string, LastResult>();

        private const int MaxCacheEntries = 5000;
        private const int WidthBucketPx = 4;

        private static int BucketWidth(float width)
        {
            return Mathf.RoundToInt(width / WidthBucketPx) * WidthBucketPx;
        }

        public static string TryGet(string originalText, float width, GameFont font)
        {
            int bucketedWidth = BucketWidth(width);

            if (lastResultByText.TryGetValue(originalText, out LastResult last)
                && last.Width == bucketedWidth && last.Font == font)
            {
                return last.Value;
            }

            var key = new CacheKey { Text = originalText, Width = bucketedWidth, Font = font };

            if (cache.TryGetValue(key, out LinkedListNode<CacheEntry> node))
            {
                // Hit: this entry is "hot" again — move it to the front so
                // a future trim only ever drops the truly cold tail.
                lruOrder.Remove(node);
                lruOrder.AddFirst(node);

                // Also refresh the identity fast-path cache for this exact
                // string instance, so a repeat lookup of the same instance
                // (e.g. next frame) hits the O(1) identity path instead of
                // coming back through this dictionary lookup every time.
                lastResultByText.Remove(originalText);
                lastResultByText.Add(originalText, new LastResult { Width = bucketedWidth, Font = font, Value = node.Value.Value });

                return node.Value.Value;
            }

            return null;
        }

        public static void Store(string originalText, float width, GameFont font, string processedText)
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

            lastResultByText.Remove(originalText);
            lastResultByText.Add(originalText, new LastResult { Width = bucketedWidth, Font = font, Value = processedText });
        }

        public static void Clear()
        {
            cache.Clear();
            lruOrder.Clear();
            lastResultByText = new ConditionalWeakTable<string, LastResult>();
        }
    }
}
