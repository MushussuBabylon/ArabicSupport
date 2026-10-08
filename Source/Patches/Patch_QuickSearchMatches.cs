using System;
using System.Reflection;
using ArabicSupport.Settings;
using ArabicSupport.TextInput;
using ArabicSupport.Utils;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Makes RimWorld's quick-search boxes match Arabic.
    ///
    /// The search text is stored in shaped, visual form. A half-typed word is
    /// shaped differently from the same letters inside the full word (its last
    /// letter gets the final form instead of the medial one), so the game's
    /// plain IndexOf never finds it. When the query contains Arabic, both the
    /// query and the label are converted back to base letters and compared
    /// there, ignoring diacritics and a few common spelling variants
    /// (alef forms, alef maksura/yeh, teh marbuta/heh).
    ///
    /// Target: RimWorld.QuickSearchFilter.MatchImpl(string) - the private
    /// method the game calls only when its own per-label result cache
    /// (QuickSearchFilter.cachedMatches) misses. So the Arabic comparison
    /// runs once per label per query, not every frame. If MatchImpl does not
    /// exist (other game version), Matches(string) is patched instead.
    /// If neither exists the patch switches itself off with a log warning.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_QuickSearchMatches
    {
        public static bool Prepare()
        {
            if (TargetMethod() != null)
                return true;

            Log.Warning("[Arabic Support] QuickSearchFilter.MatchImpl/Matches(string) not found; Arabic search matching is disabled.");
            return false;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(QuickSearchFilter), "MatchImpl", new[] { typeof(string) })
                ?? AccessTools.Method(typeof(QuickSearchFilter), nameof(QuickSearchFilter.Matches), new[] { typeof(string) });
        }

        // __0 is the (string) label being tested.
        public static bool Prefix(QuickSearchFilter __instance, string __0, ref bool __result)
        {
            // Cheap rejections first: this can run for every entry of a list.
            if (string.IsNullOrEmpty(__0) || __instance == null)
                return true;

            ArabicSupportSettings settings = ArabicSupportMod.Settings;
            if (settings != null && !settings.textInputFix)
                return true;

            try
            {
                string query = __instance.Text;

                if (string.IsNullOrEmpty(query) || !ArabicDetector.ContainsArabic(query))
                    return true;

                // The matching caches are main-thread only.
                if (!UnityData.IsInMainThread)
                    return true;

                __result = ArabicSearch.Matches(query, __0);
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Arabic search matching failed: {ex}", 102783471);
                return true;
            }
        }
    }
}
