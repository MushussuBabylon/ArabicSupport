using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        public const string HarmonyId = "mushussubabylon.arabicsupport";

        static HarmonyInit()
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                var patchTypes = typeof(HarmonyInit).Assembly
                    .GetTypes()
                    .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0);

                foreach (var type in patchTypes)
                {
                    try
                    {
                        harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Arabic Support] Failed to apply {type.FullName}: {ex}");
                    }
                }
                Log.Message("[Arabic Support] Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Arabic Support] Harmony initialization failed: {ex}");
            }
        }
    }
}
