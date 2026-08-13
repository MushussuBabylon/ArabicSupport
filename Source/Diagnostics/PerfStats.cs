using System.Diagnostics;
using Verse;

namespace ArabicSupport.Diagnostics
{
    /// <summary>
    /// Lightweight, always-cheap counters for measuring how much work
    /// FullPipeline is actually doing, so the effect of caching can be
    /// checked as real numbers in Player.log instead of guessed at.
    ///
    /// Cache hits only increment a counter (no timing at all). Only the
    /// much rarer cache-miss path is timed with a Stopwatch. A summary
    /// line is written to the log at most once every 10 seconds, never
    /// every frame, so this is safe to leave running.
    /// </summary>
    public static class PerfStats
    {
        // Set to false to fully silence this without deleting the file.
        public const bool Enabled = true;

        private const double LogIntervalSeconds = 10.0;

        private static readonly Stopwatch UncachedTimer = new Stopwatch();
        private static readonly Stopwatch SinceLastLog = Stopwatch.StartNew();

        private static long totalCalls;
        private static long cacheHits;
        private static long cacheMisses;
        private static long uncachedTicksAccumulated;

        public static void RecordHit()
        {
            if (!Enabled) return;
            totalCalls++;
            cacheHits++;
            MaybeLog();
        }

        public static void RecordMissStart()
        {
            if (!Enabled) return;
            UncachedTimer.Restart();
        }

        public static void RecordMissEnd()
        {
            if (!Enabled) return;
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

            double hitRate = cacheHits * 100.0 / totalCalls;
            double totalUncachedMs = uncachedTicksAccumulated / (double)Stopwatch.Frequency * 1000.0;
            double avgUncachedMs = cacheMisses == 0 ? 0 : totalUncachedMs / cacheMisses;

            Log.Message(
                $"[Arabic Support] Perf: {totalCalls} label calls | " +
                $"{cacheHits} cache hits ({hitRate:F1}%) | " +
                $"{cacheMisses} cache misses | " +
                $"avg {avgUncachedMs:F3} ms/miss | " +
                $"{totalUncachedMs:F1} ms total spent wrapping this session"
            );

            SinceLastLog.Restart();
        }
    }
}
