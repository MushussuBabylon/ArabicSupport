using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArabicSupport.Utils
{
    /// <summary>
    /// Detects whether a string contains Arabic (or related RTL) characters.
    /// Shared by every patch so detection logic stays consistent everywhere.
    ///
    /// Widgets.Label is patched globally, so this runs on every single label
    /// drawn every frame — including plain English/number labels that have
    /// nothing to do with this mod, making it the hottest path in the whole
    /// mod. Two caching layers are used:
    ///
    /// 1. Identity cache (ConditionalWeakTable): if the exact same string
    ///    *instance* is seen again — true for anything RimWorld doesn't
    ///    rebuild every frame (translated strings, def labels, tab names) —
    ///    this is an O(1) identity lookup with no scan at all. Entries are
    ///    dropped automatically once the string itself is garbage
    ///    collected, so this layer can't leak or need manual eviction.
    ///
    /// 2. Content cache (bounded, true LRU): a lot of UI code instead
    ///    builds a *brand-new* string instance every single frame with
    ///    *identical content* — string.Format/interpolation on a value that
    ///    isn't actually changing, a ToString() in a draw loop, etc. Those
    ///    always miss layer 1, which used to force a full O(length)
    ///    character-by-character scan every frame for every such label —
    ///    this was the actual source of the mod feeling heavy, since it's
    ///    running on essentially every label onscreen once the game is in
    ///    Arabic. Keying on string *content* instead catches these: any
    ///    string whose text has been seen before, regardless of instance,
    ///    hits in O(1).
    ///
    ///    Eviction is genuine LRU (Dictionary + LinkedList), not
    ///    insertion-order FIFO: every hit moves its entry to the front, so
    ///    a trim always drops whichever content has gone the longest
    ///    without being seen again, rather than whichever happened to be
    ///    cached first. That matters here specifically because this cache
    ///    is meant to survive "runs every frame" — a genuinely hot label
    ///    should never lose its slot to a label that was only ever drawn
    ///    once.
    ///
    /// Only text whose exact content has never been seen by either layer
    /// pays for an actual character scan.
    /// </summary>
    public static class ArabicDetector
    {
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

            if (instanceCache.TryGetValue(text, out object cachedByInstance))
                return (bool)cachedByInstance;

            bool result;
            if (contentCache.TryGetValue(text, out LinkedListNode<ContentEntry> node))
            {
                result = node.Value.Value;
                lruOrder.Remove(node);
                lruOrder.AddFirst(node);
            }
            else
            {
                result = Scan(text);
                StoreContent(text, result);
            }

            instanceCache.Add(text, result ? BoxedTrue : BoxedFalse);
            return result;
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

                // Arabic, Arabic Supplement, Arabic Extended-A, Arabic Extended-B,
                // Arabic Presentation Forms A/B, and Hebrew.
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
