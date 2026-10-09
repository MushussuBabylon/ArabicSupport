using System.Collections.Generic;

namespace ArabicSupport.Caching
{
    /// <summary>
    /// Bounded cache made of two dictionaries ("hot" and "cold") instead of a
    /// dictionary plus an LRU linked list.
    ///
    /// New entries go into hot. When hot is full, the two swap roles: hot
    /// becomes cold, and the old cold generation is cleared and reused as
    /// the new hot one. A hit in cold copies the entry back into hot, so
    /// anything used since the last swap survives the next one - the same
    /// "no mass eviction hitch" property as a true LRU, but:
    ///   * a hit in hot is a single dictionary lookup, with no list re-linking,
    ///   * no LinkedListNode is allocated per entry (no garbage),
    ///   * the dictionaries are reused, so steady state allocates nothing.
    ///
    /// Not thread-safe. Every caller in this mod is main-thread only (guarded
    /// by UnityData.IsInMainThread in the Harmony patches), so the locks the
    /// old caches took on every lookup were pure overhead.
    /// </summary>
    internal sealed class GenerationalCache<TKey, TValue>
    {
        private Dictionary<TKey, TValue> hot;
        private Dictionary<TKey, TValue> cold;
        private readonly int generationSize;

        public GenerationalCache(int maxEntries, IEqualityComparer<TKey> comparer = null)
        {
            generationSize = maxEntries / 2 < 16 ? 16 : maxEntries / 2;
            hot = new Dictionary<TKey, TValue>(generationSize, comparer);
            cold = new Dictionary<TKey, TValue>(generationSize, comparer);
        }

        public int Count => hot.Count + cold.Count;

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (hot.TryGetValue(key, out value))
                return true;

            if (cold.TryGetValue(key, out value))
            {
                Promote(key, value);
                return true;
            }

            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (hot.ContainsKey(key))
            {
                hot[key] = value;
                return;
            }

            cold.Remove(key);
            Promote(key, value);
        }

        private void Promote(TKey key, TValue value)
        {
            if (hot.Count >= generationSize)
            {
                Dictionary<TKey, TValue> t = cold;
                cold = hot;
                hot = t;
                hot.Clear();
            }

            hot[key] = value;
        }

        public void Clear()
        {
            hot.Clear();
            cold.Clear();
        }
    }
}
