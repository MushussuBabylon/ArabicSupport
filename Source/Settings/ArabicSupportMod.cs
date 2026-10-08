using System.Collections.Generic;
using ArabicSupport.Fonts;
using UnityEngine;
using Verse;

namespace ArabicSupport.Settings
{
    public class ArabicSupportMod : Mod
    {
        public static ArabicSupportMod Instance { get; private set; }
        public static ArabicSupportSettings Settings { get; private set; }

        private static readonly string[] SlotLabels = { "Tiny", "Small", "Medium" };
        private static readonly GameFont[] SlotFonts = { GameFont.Tiny, GameFont.Small, GameFont.Medium };

        // "This is a test text to preview the Arabic font in RimWorld 123",
        // already reshaped and in visual order, like the translation files.
        private const string ArabicSample =
            "\u0661\u0662\u0663 \uFEAA\uFEDF\uFEAD\uFEED \uFEE2\uFEF3\uFEAD \uFEF2\uFED3 " +
            "\uFEF2\uFE91\uFEAE\uFECC\uFEDF\uFE8D \uFEC2\uFEA8\uFEDF\uFE8D \uFE94\uFEE8\uFEF3\uFE8E\uFECC\uFEE4\uFEDF " +
            "\uFEF2\uFE92\uFEF3\uFEAE\uFEA0\uFE97 \uFEBA\uFEE7 \uFE8D\uFEAC\uFEEB";

        // Harakat preview: "haqqan, shukran jazeelan! ar-rajulu qawiyyun jiddan",
        // reshaped and in visual order. Shown so you can judge how harakat look
        // with the chosen font and the "Place harakat" option.
        private const string HarakatSample =
            "\u064B\uFE8D\uFEAA\uFE9F \u0651\u064C\uFEF1\uFEEE\uFED7 \u064F\uFEDE\u064F\uFE9F\u0651\u064E\uFEAE\uFEDF\uFE8D " +
            "!\u064B\uFEFC\uFEF3\uFEB0\uFE9F \u064B\uFE8D\uFEAE\uFEDC\uFEB7 \u060C\u064B\uFE8E\uFED8\uFEA3";

        private const string LatinSample = "The quick brown fox jumps over the lazy dog 0123456789";

        private Vector2 scroll;
        private string typingTest = "";
        private float contentHeight = 900f;
        private int seenApplyCount = -1;
        private bool catalogRefreshed;

        public ArabicSupportMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ArabicSupportSettings>();
            FontManager.RequestApply();
        }

        public override string SettingsCategory() => "Arabic Support";

        public override void WriteSettings()
        {
            base.WriteSettings();
            catalogRefreshed = false;
            FontManager.RequestApply();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ArabicSupportSettings s = Settings;
            GameFont previousFont = Text.Font;
            bool scrollOpen = false;
            bool listOpen = false;
            var list = new Listing_Standard();

            try
            {
                if (!catalogRefreshed)
                {
                    FontManager.RefreshCatalog();
                    catalogRefreshed = true;
                }

                // Font changed since last frame: row heights changed, so the
                // old content height / scroll position are meaningless.
                if (seenApplyCount != FontManager.ApplyCount)
                {
                    seenApplyCount = FontManager.ApplyCount;
                    contentHeight = Mathf.Max(contentHeight, inRect.height + 400f);
                    scroll.y = Mathf.Min(scroll.y, Mathf.Max(0f, contentHeight - inRect.height));
                }

                string before = Snapshot(s);

                // Always-visible way out, outside the scroll view.
                Text.Font = GameFont.Small;
                Rect resetRect = new Rect(inRect.x, inRect.y, Mathf.Min(260f, inRect.width), 30f);
                if (Widgets.ButtonText(resetRect, "Use the game font (reset)"))
                    s.ResetToGameFont();

                Rect scrollRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f);
                Rect view = new Rect(0f, 0f, scrollRect.width - 20f, Mathf.Max(contentHeight, 100f));
                Widgets.BeginScrollView(scrollRect, ref scroll, view);
                scrollOpen = true;

                // One column only: by default Listing_Standard starts a new
                // column when the content is taller than the view, which put
                // the size/position settings outside the window.
                list.maxOneColumn = true;
                list.Begin(view);
                listOpen = true;

                DrawContents(list, s);

                if (Snapshot(s) != before)
                    FontManager.RequestApply();
            }
            catch (System.Exception ex)
            {
                // Logged, and the window keeps drawing next frame.
                Log.ErrorOnce("[Arabic Support] Settings window failed: " + ex, 102783462);
            }
            finally
            {
                Text.Font = previousFont;
                if (listOpen)
                {
                    list.End();
                    contentHeight = Mathf.Max(list.CurHeight + 20f, 100f);
                }
                if (scrollOpen)
                    Widgets.EndScrollView();
            }
        }

        private void DrawContents(Listing_Standard list, ArabicSupportSettings s)
        {
            Header(list, "Custom font");

            list.CheckboxLabeled("Use a custom font", ref s.customFontEnabled,
                "Replace RimWorld's UI font with the font chosen below.");

            if (s.customFontEnabled)
            {
                var catalog = FontManager.Catalog;

                if (catalog.Count == 0)
                {
                    Note(list, "No fonts found in " + (FontManager.FontsDirectory ?? "<mod>/Fonts") +
                               ". Run the 'Build Mod' GitHub action (or Tools/build_fonts.py) to create them.");
                }
                else
                {
                    if (list.ButtonTextLabeled("Font", FontManager.Pretty(s.arabicFont)))
                        Find.WindowStack.Add(new FloatMenu(FontMenu(s)));

                    Note(list, "Fonts live in: " + (FontManager.FontsDirectory ?? "<mod>/Fonts") +
                               ". To add another font, copy its .fontbundle file into that folder " +
                               "(more fonts are in the ExtraFonts folder of the mod's GitHub page).");
                }

                list.CheckboxLabeled("Also use it in text fields", ref s.applyToTextFields,
                    "Use the custom font for typing boxes (search fields, renaming, etc.) too.");

                list.CheckboxLabeled("Place harakat on their letter", ref s.harakatPlacement,
                    "Draws fatha, tanween, shadda... on top of the letter they belong to " +
                    "(for example tanween on a final alef). Works with fonts made for this mod.");

                list.CheckboxLabeled("Prepare letters in advance (smoother)", ref s.prewarmGlyphs,
                    "Draws all Arabic and English letters once when the font is applied, so the game " +
                    "doesn't stutter the first time a new letter appears.");

                list.Gap(6f);
                Header(list, "Size and position");

                for (int i = 0; i < ArabicSupportSettings.SlotCount; i++)
                {
                    int gameSize = FontManager.GameSize(i);
                    Note(list, $"{SlotLabels[i]}: {gameSize + s.sizeOffsets[i]}px  (game: {gameSize}px, offset {Signed(s.sizeOffsets[i])})");
                    s.sizeOffsets[i] = Mathf.RoundToInt(list.Slider(s.sizeOffsets[i], -6f, 12f));

                    Note(list, $"{SlotLabels[i]} vertical shift: {Signed(s.verticalOffsets[i])}px");
                    s.verticalOffsets[i] = Mathf.RoundToInt(list.Slider(s.verticalOffsets[i], -8f, 8f));
                }

                if (list.ButtonText("Reset font settings"))
                    s.ResetFontSettings();
            }

            list.Gap(6f);
            Header(list, "Typing");
            list.CheckboxLabeled("Arabic typing in text boxes", ref s.textInputFix,
                "Lets you type Arabic in names, search boxes and other text boxes: letters are joined " +
                "and ordered like the translation. Search boxes also find Arabic words while you type.");

            Note(list, "Try typing here:");
            typingTest = Widgets.TextField(list.GetRect(Text.LineHeightOf(GameFont.Small) + 8f), typingTest ?? "");
            list.Gap(2f);

            list.Gap(6f);
            Header(list, "Preview");
            Note(list, FontManager.Status);

            GameFont previous = Text.Font;
            for (int i = 0; i < SlotFonts.Length; i++)
            {
                Text.Font = SlotFonts[i];
                LabelLine(list, ArabicSample);
                LabelLine(list, HarakatSample);
                LabelLine(list, LatinSample);
            }
            Text.Font = previous;
        }

        private static List<FloatMenuOption> FontMenu(ArabicSupportSettings s)
        {
            var options = new List<FloatMenuOption>();
            foreach (string font in FontManager.Catalog)
            {
                string id = font;
                options.Add(new FloatMenuOption(FontManager.Pretty(id), () =>
                {
                    s.arabicFont = id;
                    FontManager.RequestApply();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No fonts found", null));
            return options;
        }

        private static string Snapshot(ArabicSupportSettings s)
        {
            return string.Concat(
                s.customFontEnabled ? "1" : "0", s.applyToTextFields ? "1" : "0",
                s.prewarmGlyphs ? "1" : "0", s.harakatPlacement ? "1" : "0",
                "|",
                s.arabicFont, "|",
                string.Join(",", s.sizeOffsets), "|", string.Join(",", s.verticalOffsets));
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        // Labels drawn through Widgets.Label (and so through this mod's
        // wrapping) without depending on Listing_Standard.Label, whose
        // signature differs between RimWorld 1.5 and 1.6.
        private static void LabelLine(Listing_Standard list, string text)
        {
            float h = Text.CalcHeight(text, list.ColumnWidth);
            Widgets.Label(list.GetRect(h), text);
            list.Gap(2f);
        }

        private static void Note(Listing_Standard list, string text)
        {
            GameFont previous = Text.Font;
            Text.Font = GameFont.Small;
            LabelLine(list, text);
            Text.Font = previous;
        }

        private static void Header(Listing_Standard list, string text)
        {
            GameFont previous = Text.Font;
            Text.Font = GameFont.Medium;
            LabelLine(list, text);
            Text.Font = previous;
            list.GapLine(6f);
        }
    }
}
