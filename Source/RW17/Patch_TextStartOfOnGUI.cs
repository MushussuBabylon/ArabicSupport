using System;
using System.Reflection;
using ArabicSupport.Fonts;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// RimWorld 1.7 build only. A second place, next to Patch_RootOnGUI, that
    /// gives FontManager its chance to swap fonts: the start of Text.StartOfOnGUI,
    /// which is where the community mod "Readable Fonts 1.7" changes the game's
    /// fonts and where the engine's GUI backend is known to be ready.
    ///
    /// Either hook alone is enough; FontManager.OnGUITick does nothing when
    /// there is nothing to do, so running from both costs nothing. A hook whose
    /// target does not exist in a game version is skipped without an error
    /// (Prepare), so whichever one exists does the work.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_TextStartOfOnGUI
    {
        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static MethodBase TargetMethod()
        {
            try
            {
                return AccessTools.Method(typeof(Text), "StartOfOnGUI");
            }
            catch (Exception)
            {
                return null;                       // not found (or ambiguous): skip this hook
            }
        }

        public static void Postfix()
        {
            try
            {
                FontManager.OnGUITick();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Font update failed: {ex}", 102783461);
            }
        }
    }
}
