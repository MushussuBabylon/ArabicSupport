using System;
using ArabicSupport.Fonts;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Gives FontManager a safe moment to swap fonts: the start of every
    /// Root.OnGUI, on the main thread and inside OnGUI, before anything is
    /// drawn that frame. When nothing is pending this is one bool check plus
    /// one reference compare.
    /// </summary>
    [HarmonyPatch(typeof(Root), "OnGUI")]
    public static class Patch_RootOnGUI
    {
#if RW17
        // RimWorld 1.7: if this game version has no Root.OnGUI, skip the patch
        // quietly - Patch_TextStartOfOnGUI is the other way in.
        public static bool Prepare()
        {
            try
            {
                return AccessTools.Method(typeof(Root), "OnGUI") != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif

        public static void Prefix()
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
