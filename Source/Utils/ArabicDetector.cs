using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArabicSupport.Utils
{
    /// <summary>
    /// Detects whether a string contains Arabic (or related RTL) characters.
    ///
    /// The Dictionary/LinkedList-based content cache below is NOT
    /// thread-safe on its own. If another mod (e.g. Map Preview)
    /// generates content off the main Unity thread and that path
    /// happens to call into a Verse text API that we also patch,
    /// concurrent access could corrupt the cache or throw. The lock
    /// makes this safe regardless of what thread calls in, at the cost
    /// of a small amount of contention if that ever actually happens
    /// (uncontended locks are cheap in practice).
    /// </summary>
    public static class ArabicDetector
    {
        private static readonly object _lock = new object();

        private static readonly ConditionalWeakTable<string, object> instanceCache =
            new ConditionalWeakTable<string, object>();

        private class ContentEntry
        {
            public string Text;
            public bool Value;
        }

        private static readonly Dictionary<string, LinkedListNode<ContentEntry>> contentCache =
            new Dictionary<string, LinkedListNode<ContentEntry>>();
        private static readonly LinkedList<ContentEntry> lruOrder = new LinkedList<ContentEntry>();
        private const int MaxContentCacheEntries = 5000;
        private static readonly object BoxedTrue = true;
        private static readonly object BoxedFalse = false;

        public static bool ContainsArabic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            // ConditionalWeakTable itself is thread-safe, so the identity
            // fast-path can run without the lock.
            if (instanceCache.TryGetValue(text, out object cachedByInstance))
                return (bool)cachedByInstance;

            // Scan speculatively BEFORE taking the lock, so the lock only
            // ever guards the O(1) dictionary/LRU bookkeeping below, not
            // the O(length) scan itself. On the rare race where two
            // threads scan the same new string at once, one scan's result
            // is simply discarded in favor of whichever thread's entry
            // lands in contentCache first — both scans agree either way.
            bool scanned = Scan(text);

            lock (_lock)
            {
                bool result;
                if (contentCache.TryGetValue(text, out LinkedListNode<ContentEntry> node))
                {
                    result = node.Value.Value;
                    lruOrder.Remove(node);
                    lruOrder.AddFirst(node);
                }
                else
                {
                    result = scanned;
                    StoreContent(text, result);
                }

                // Remove-then-Add rather than a bare Add: if this exact
                // string somehow already has an entry (e.g. a prior call
                // raced in just before the lock was acquired), a bare Add
                // would throw "key already exists." Remove is always safe
                // even when there's nothing to remove.
                instanceCache.Remove(text);
                instanceCache.Add(text, result ? BoxedTrue : BoxedFalse);
                return result;
            }
        }

        private static void StoreContent(string text, bool value)
        {
            if (contentCache.Count >= MaxContentCacheEntries)
            {
                LinkedListNode<ContentEntry> coldest = lruOrder.Last;
                if (coldest != null)
                {
                    lruOrder.RemoveLast();
                    contentCache.Remove(coldest.Value.Text);
                }
            }
            var newNode = new LinkedListNode<ContentEntry>(new ContentEntry { Text = text, Value = value });
            lruOrder.AddFirst(newNode);
            contentCache[text] = newNode;
        }

        private static bool Scan(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // Arabic, Arabic Supplement, Arabic Extended-A, Arabic
                // Extended-B, Arabic Presentation Forms A/B, and Hebrew.
                if ((c >= '\u0600' && c <= '\u06FF') ||
                    (c >= '\u0750' && c <= '\u077F') ||
                    (c >= '\u08A0' && c <= '\u08FF') ||
                    (c >= '\u0870' && c <= '\u089F') ||
                    (c >= '\u0590' && c <= '\u05FF') ||
                    (c >= '\uFE70' && c <= '\uFEFF') ||
                    (c >= '\uFB50' && c <= '\uFDFF'))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
