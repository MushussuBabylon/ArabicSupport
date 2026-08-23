using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace ArabicSupport.Caching
{
    public static class ProcessedTextCache
    {
        private static readonly object _lock = new object();

        private struct CacheKey : System.IEquatable<CacheKey>
        {
            public string Text;
            public int Width;
            public GameFont Font;

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

        private static readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> cache =
            new Dictionary<CacheKey, LinkedListNode<CacheEntry>>();
        private static readonly LinkedList<CacheEntry> lruOrder = new LinkedList<CacheEntry>();
        private static ConditionalWeakTable<string, LastResult> lastResultByText =
            new ConditionalWeakTable<string, LastResult>();

        private const int MaxCacheEntries = 5000;
        private const int WidthBucketPx = 4;

        /// <summary>
        /// Buckets a raw pixel width down to the nearest multiple of
        /// WidthBucketPx, FLOORING rather than rounding. Flooring
        /// guarantees a cached wrap was always computed at a width that is
        /// never LARGER than the real width of any rect mapping to the
        /// same bucket — rounding could serve a wrap computed for more
        /// horizontal space than a slightly narrower rect actually has,
        /// causing overflow. Callers MUST wrap using this same bucketed
        /// value (not the raw width) so the cache key and the actual
        /// computation always agree — see FullPipeline.Process.
        /// </summary>
        public static int BucketWidth(float width)
        {
            return Mathf.Max(0, Mathf.FloorToInt(width / WidthBucketPx) * WidthBucketPx);
        }

        public static string TryGet(string originalText, int bucketedWidth, GameFont font)
        {
            // Fast path: ConditionalWeakTable is thread-safe on its own, so
            // this identity-keyed check can run before taking the lock.
            // Safe to read without the lock because Store() always
            // publishes a brand-new, fully-initialized LastResult rather
            // than mutating an existing one.
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
                    lastResultByText.Add(originalText, new LastResult
                    {
                        Width = bucketedWidth,
                        Font = font,
                        Value = node.Value.Value
                    });
                    return node.Value.Value;
                }
                return null;
            }
        }

        public static void Store(string originalText, int bucketedWidth, GameFont font, string processedText)
        {
            lock (_lock)
            {
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
                lastResultByText.Add(originalText, new LastResult
                {
                    Width = bucketedWidth,
                    Font = font,
                    Value = processedText
                });
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
