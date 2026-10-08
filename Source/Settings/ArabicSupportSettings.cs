using Verse;

namespace ArabicSupport.Settings
{
    public class ArabicSupportSettings : ModSettings
    {
        public const string DefaultArabicFont = "Cairo-Bold";
        public const int SlotCount = 3;                   // GameFont.Tiny, Small, Medium

        // Bumped when a new version needs to change saved values once.
        // 10 = V10: default font is Cairo Bold, separate English fonts removed.
        private const int CurrentSettingsVersion = 10;
        private int settingsVersion = CurrentSettingsVersion;

        public bool customFontEnabled = true;
        public string arabicFont = DefaultArabicFont;

        // Pixel offsets relative to the game's own size for each GameFont.
        public int[] sizeOffsets = new int[SlotCount];

        // Vertical nudge (pixels) for labels, per GameFont.
        public int[] verticalOffsets = new int[SlotCount];

        public bool applyToTextFields = true;
        public bool prewarmGlyphs = true;

        // Arabic typing in text boxes (names, search boxes...) and Arabic
        // matching in the game's quick-search boxes.
        public bool textInputFix = true;

        // Put harakat on top of their letter (needs a font built by this mod).
        public bool harakatPlacement = true;

        public int GetSizeOffset(int slot) => slot >= 0 && slot < SlotCount ? sizeOffsets[slot] : 0;
        public int GetVerticalOffset(int slot) => slot >= 0 && slot < SlotCount ? verticalOffsets[slot] : 0;

        public void ResetFontSettings()
        {
            customFontEnabled = true;
            arabicFont = DefaultArabicFont;
            sizeOffsets = new int[SlotCount];
            verticalOffsets = new int[SlotCount];
            applyToTextFields = true;
            prewarmGlyphs = true;
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

            if (sizeOffsets == null || sizeOffsets.Length != SlotCount) sizeOffsets = new int[SlotCount];
            if (verticalOffsets == null || verticalOffsets.Length != SlotCount) verticalOffsets = new int[SlotCount];

            for (int i = 0; i < SlotCount; i++)
            {
                Scribe_Values.Look(ref sizeOffsets[i], "sizeOffset" + i, 0);
                Scribe_Values.Look(ref verticalOffsets[i], "verticalOffset" + i, 0);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && settingsVersion < CurrentSettingsVersion)
            {
                // One-time upgrade from V9 or older: switch to the new default
                // font (most V9 fonts are no longer shipped with the mod).
                arabicFont = DefaultArabicFont;
                customFontEnabled = true;
                settingsVersion = CurrentSettingsVersion;
            }

            if (string.IsNullOrEmpty(arabicFont)) arabicFont = DefaultArabicFont;
        }
    }
}
