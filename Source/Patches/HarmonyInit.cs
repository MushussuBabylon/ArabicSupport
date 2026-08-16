using System;
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

            // Patch each class individually, inside its own try/catch, instead
            // of calling harmony.PatchAll() directly. PatchAll() applies every
            // [HarmonyPatch] class in one pass; if even one class targets a
            // method that doesn't exist (or fails for any other reason), the
            // whole pass can throw and silently cancel every patch that would
            // have come after it. Isolating each class means one bad patch
            // only disables that one patch instead of the whole mod.
            foreach (var type in typeof(HarmonyInit).Assembly.GetTypes())
            {
                try
                {
                    harmony.CreateClassProcessor(type)?.Patch();
                }
                catch (Exception ex)
                {
                    Log.Error($"[Arabic Support] Failed to apply patch in {type.Name}: {ex}");
                }
            }

            Log.Message("[Arabic Support] Harmony patches applied.");
        }
    }
}
