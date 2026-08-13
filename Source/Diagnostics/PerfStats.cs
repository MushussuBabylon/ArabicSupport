using System.Diagnostics;
using UnityEngine;

namespace ArabicSupport.Diagnostics
{
    /// <summary>
    /// Lightweight counters for measuring FullPipeline performance.
    /// Safe from recursion and compile warnings.
    /// </summary>
    public static class PerfStats
    {
        // Changed from 'const' to 'static' to fix CS0162 compiler warnings
        public static bool Enabled = true;

        private const double LogIntervalSeconds = 10.0;

        private static readonly Stopwatch UncachedTimer = new Stopwatch();
        private static readonly Stopwatch SinceLastLog = Stopwatch.StartNew();

        private static long totalCalls;
        private static long cacheHits;
        private static long cacheMisses;
        private static long uncachedTicksAccumulated;

        // Re-entrancy guard to prevent infinite recursion stack overflow crashes
        private static bool isLogging = false;

        public static void RecordHit()
        {
            if (!Enabled || isLogging) return;
            totalCalls++;
            cacheHits++;
            MaybeLog();
        }

        public static void RecordMissStart()
        {
            if (!Enabled || isLogging) return;
            UncachedTimer.Restart();
        }

        public static void RecordMissEnd()
        {
            if (!Enabled || isLogging) return;
            UncachedTimer.Stop();
            totalCalls++;
            cacheMisses++;
            uncachedTicksAccumulated += UncachedTimer.ElapsedTicks;
            MaybeLog();
        }

        private static void MaybeLog()
        {
            if (SinceLastLog.Elapsed.TotalSeconds < LogIntervalSeconds)
                return;

            if (totalCalls == 0)
                return;

            isLogging = true;
            try
            {
                double hitRate = cacheHits * 100.0 / totalCalls;
                double totalUncachedMs = uncachedTicksAccumulated / (double)Stopwatch.Frequency * 1000.0;
                double avgUncachedMs = cacheMisses == 0 ? 0 : totalUncachedMs / cacheMisses;

                // UnityEngine.Debug.Log writes directly to Player.log without 
                // routing through RimWorld's Harmony-patched Verse.Log hooks.
                Debug.Log(
                    $"[Arabic Support] Perf: {totalCalls} label calls | " +
                    $"{cacheHits} cache hits ({hitRate:F1}%) | " +
                    $"{cacheMisses} cache misses | " +
                    $"avg {avgUncachedMs:F3} ms/miss | " +
                    $"{totalUncachedMs:F1} ms total spent wrapping this session"
                );

                SinceLastLog.Restart();
            }
            finally
            {
                isLogging = false;
            }
        }
    }
}
