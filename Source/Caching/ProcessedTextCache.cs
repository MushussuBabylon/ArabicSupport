using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace ArabicSupport.Caching
{
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

        // Immutable by design. Readers reach this through the lock-free
        // path below, so once published it must never be edited in place —
        // otherwise a concurrent reader could see a torn mix of an old
        // Value with a new Width/Font. "Updating" means building a new
        // instance and swapping it in.
        private sealed class LastResult
        {
            public readonly int Width;
            public readonly GameFont Font;
            public readonly string Value;

            public LastResult(int width, GameFont font, string value)
            {
                Width = width;
                Font = font;
                Value = value;
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
        private static ConditionalWeakTable<string, LastResult> lastResultByText =
            new ConditionalWeakTable<string, LastResult>();

        private const int MaxCacheEntries = 5000;

        // DO NOT CHANGE — 4px bucketing is load-bearing for correct wrapping.
        private const int WidthBucketPx = 4;

        private static int BucketWidth(float width)
        {
            return Mathf.RoundToInt(width / WidthBucketPx) * WidthBucketPx;
        }

        public static string TryGet(string originalText, float width, GameFont font)
        {
            int bucketedWidth = BucketWidth(width);

            // Lock-free fast path: ConditionalWeakTable is thread-safe on
            // its own, and LastResult is immutable once published, so this
            // is safe without the lock. This is the path taken by the
            // overwhelming majority of calls — the same widget redrawing
            // the same text at the same width, many times per frame.
            if (lastResultByText.TryGetValue(originalText, out LastResult last) &&
                last.Width == bucketedWidth && last.Font == font)
            {
                return last.Value;
            }

            lock (_lock)
            {
                var key = new CacheKey { Text = originalText, Width = bucketedWidth, Font = font };
                if (cache.TryGetValue(key, out LinkedListNode<CacheEntry> node))
                {
                    lruOrder.Remove(node);
                    lruOrder.AddFirst(node);

                    lastResultByText.Remove(originalText);
                    lastResultByText.Add(originalText, new LastResult(bucketedWidth, font, node.Value.Value));
                    return node.Value.Value;
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

                lastResultByText.Remove(originalText);
                lastResultByText.Add(originalText, new LastResult(bucketedWidth, font, processedText));
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                cache.Clear();
                lruOrder.Clear();
                lastResultByText = new ConditionalWeakTable<string, LastResult>();
            }
        }
    }
}
