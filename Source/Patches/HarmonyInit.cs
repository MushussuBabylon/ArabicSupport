using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Entry point. RimWorld calls any [StaticConstructorOnStartup] class's
    /// static constructor once, automatically, after defs are loaded but
    /// before the main menu appears. This is where Harmony patches get applied.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        public const string HarmonyId = "mushussubabylon.arabicsupport";

        static HarmonyInit()
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();

            Log.Message("[Arabic Support] Harmony patches applied.");
        }
    }
}
