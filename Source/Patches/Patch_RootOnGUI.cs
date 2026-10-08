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
