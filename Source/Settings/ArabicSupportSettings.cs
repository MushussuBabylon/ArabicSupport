using Verse;

namespace ArabicSupport.Settings
{
    public class ArabicSupportSettings : ModSettings
    {
        // The font the mod starts with: the id of a file in the Fonts folder (Config/fonts.json lists it as "defaultFont").
        public const string DefaultArabicFont = "Cairo-Bold";
        public const int SlotCount = 3;                   // GameFont.Tiny, Small, Medium

        // Built-in vertical shift in pixels for Tiny, Small and Medium text. It is always applied and is not
        // saved: it keeps backstories, traits and other labels on their line. The sliders in the Size tab
        // are relative to it, so they start at 0 and only hold the player's own extra change.
        public static readonly int[] BaseVerticalOffsets = { -3, -2, -6 };

        // Widest margin the settings window offers (see wrapMargin).
        public const int MaxWrapMargin = 16;

        // The default before settings version 12. Saved settings that still hold it follow the new default once.
        private const string PreviousDefaultFont = "ElMessiri-Bold";

        // Bumped when a new version needs to change saved values once.
        // 10 = V10: the default font was Cairo Bold, separate English fonts removed.
        // 11 = V11: the default font became El Messiri Bold.
        // 12 = V12: the default font became Cairo Bold again.
        // 13 = V13: the vertical shifts -3/-2/-6 became built in; the sliders start at 0.
        private const int CurrentSettingsVersion = 13;
        private int settingsVersion = CurrentSettingsVersion;

        public bool customFontEnabled = true;
        public string arabicFont = DefaultArabicFont;

        // Pixel offsets relative to the game's own size for each GameFont.
        public int[] sizeOffsets = new int[SlotCount];

        // Extra vertical nudge (pixels) for labels, per GameFont, on top of BaseVerticalOffsets.
        public int[] verticalOffsets = new int[SlotCount];

        public bool applyToTextFields = true;
        public bool prewarmGlyphs = true;

        // Arabic typing in text boxes (names, search boxes...) and Arabic
        // matching in the game's quick-search boxes.
        public bool textInputFix = true;

        // Put harakat on top of their letter (needs a font built by this mod).
        public bool harakatPlacement = true;

        // ---- Line wrapping --------------------------------------------------

        // Wrap Arabic labels by their real pixel width. Off = the game wraps them itself
        // (which cuts lines in the wrong order for right-to-left text).
        public bool wrapByPixels = true;

        // An English phrase or a number inside Arabic text stays on one line together.
        public bool keepLatinRuns = true;

        // A word wider than the whole line is cut in reading order instead of
        // being left to the game.
        public bool splitWideWords = true;

        // Pixels taken off the width the wrapper may use (for fonts whose letters
        // reach past their advance width, or boxes that clip a pixel too early).
        public int wrapMargin = 0;

        // ---- Label height ---------------------------------------------------------

        // Give one-line Arabic labels room for the dots and tails below the baseline.
        public bool growLabelHeight = true;

        public int GetSizeOffset(int slot) => slot >= 0 && slot < SlotCount ? sizeOffsets[slot] : 0;
        // What is really applied: the built-in base plus the player's extra shift from the slider.
        public int GetVerticalOffset(int slot) => slot >= 0 && slot < SlotCount ? BaseVerticalOffsets[slot] + verticalOffsets[slot] : 0;

        public void ResetFontSettings()
        {
            customFontEnabled = true;
            arabicFont = DefaultArabicFont;
            sizeOffsets = new int[SlotCount];
            verticalOffsets = new int[SlotCount];
            applyToTextFields = true;
            prewarmGlyphs = true;
        }

        // The Layout tab: line wrapping and label height.
        public void ResetLayoutSettings()
        {
            wrapByPixels = true;
            keepLatinRuns = true;
            splitWideWords = true;
            wrapMargin = 0;
            growLabelHeight = true;
        }

        public void ResetToGameFont()
        {
            ResetFontSettings();
            customFontEnabled = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // Settings saved before V10 have no version (reads as 0).
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", 0);

            Scribe_Values.Look(ref customFontEnabled, "customFontEnabled", true);
            Scribe_Values.Look(ref arabicFont, "arabicFont", DefaultArabicFont);
            Scribe_Values.Look(ref applyToTextFields, "applyToTextFields", true);
            Scribe_Values.Look(ref prewarmGlyphs, "prewarmGlyphs", true);
            Scribe_Values.Look(ref textInputFix, "textInputFix", true);
            Scribe_Values.Look(ref harakatPlacement, "harakatPlacement", true);

            Scribe_Values.Look(ref wrapByPixels, "wrapByPixels", true);
            Scribe_Values.Look(ref keepLatinRuns, "keepLatinRuns", true);
            Scribe_Values.Look(ref splitWideWords, "splitWideWords", true);
            Scribe_Values.Look(ref wrapMargin, "wrapMargin", 0);
            Scribe_Values.Look(ref growLabelHeight, "growLabelHeight", true);

            if (sizeOffsets == null || sizeOffsets.Length != SlotCount) sizeOffsets = new int[SlotCount];
            if (verticalOffsets == null || verticalOffsets.Length != SlotCount) verticalOffsets = new int[SlotCount];

            for (int i = 0; i < SlotCount; i++)
            {
                Scribe_Values.Look(ref sizeOffsets[i], "sizeOffset" + i, 0);
                Scribe_Values.Look(ref verticalOffsets[i], "verticalOffset" + i, 0);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && settingsVersion < CurrentSettingsVersion)
            {
                if (settingsVersion < 10)
                {
                    // One-time upgrade from V9 or older: switch to the default
                    // font (most V9 fonts are no longer shipped with the mod).
                    arabicFont = DefaultArabicFont;
                    customFontEnabled = true;
                }
                else if (settingsVersion == 11 && arabicFont == PreviousDefaultFont)
                {
                    // V11 -> V12: the default changed from El Messiri Bold back to Cairo Bold.
                    // Settings that still use the old default follow it once; any
                    // other font the player picked stays as it is.
                    arabicFont = DefaultArabicFont;
                }

                if (settingsVersion < 13 && verticalOffsets[0] == -3 && verticalOffsets[1] == -2 && verticalOffsets[2] == -6)
                {
                    // V12 -> V13: those exact values were typed in by hand to get what is now built in.
                    // Keeping them would shift the text twice, so the sliders go back to 0.
                    // Any other value the player chose stays as it is.
                    verticalOffsets = new int[SlotCount];
                }
                settingsVersion = CurrentSettingsVersion;
            }

            if (string.IsNullOrEmpty(arabicFont)) arabicFont = DefaultArabicFont;

            // A hand-edited settings file must not be able to break the wrapper.
            if (wrapMargin < 0) wrapMargin = 0;
            if (wrapMargin > MaxWrapMargin) wrapMargin = MaxWrapMargin;
        }
    }
}
