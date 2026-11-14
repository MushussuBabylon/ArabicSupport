using System.Collections.Generic;
using ArabicSupport.Caching;
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
        private static readonly string[] SlotKeys =
        {
            "ArabicSupport_FontSize_Tiny", "ArabicSupport_FontSize_Small", "ArabicSupport_FontSize_Medium"
        };
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

        // "Randall Elliott saved the colony from the big attack in the desert, then went home and
        // ate with his friends." Already reshaped and in visual order, with an English name inside,
        // so the preview box below shows the wrapping and the English-name handling.
        private const string ParagraphSample =
            ".\uFEEA\uFE8B\uFE8E\uFED7\uFEAA\uFEBB\uFE83 \uFECA\uFEE3 \uFEE1\uFE8E\uFECC\uFEC4\uFEDF\uFE8D " +
            "\uFEDD\uFEED\uFE8E\uFEE8\uFE97\uFEED \uFEDD\uFEB0\uFEE8\uFEE4\uFEDF\uFE8D \uFEF0\uFEDF\uFE87 " +
            "\uFEA9\uFE8E\uFECB \uFEE2\uFE9B \u060C\uFE80\uFE8D\uFEAE\uFEA4\uFEBC\uFEDF\uFE8D \uFEF2\uFED3 " +
            "\uFEAE\uFEF4\uFE92\uFEDC\uFEDF\uFE8D \uFEE1\uFEEE\uFEA0\uFEEC\uFEDF\uFE8D \uFEE6\uFEE3 \uFE93\uFEAE\uFEE4\uFECC\uFE98\uFEB4\uFEE4\uFEDF\uFE8D " +
            "Randall Elliott \uFEAC\uFED8\uFEE7\uFE83";

        // The window is split into tabs, drawn with the game's own tab bar.
        private enum Tab { Font, Size, Layout, Typing, Preview, Tools }

        private const int TabCount = 6;

        private Tab tab = Tab.Font;
        private readonly Vector2[] scrolls = new Vector2[TabCount];
        private readonly float[] contentHeights = { 900f, 900f, 900f, 900f, 900f, 900f };
        private string typingTest = "";
        private int seenApplyCount = -1;
        private bool catalogRefreshed;

        // Width of the preview box (not saved).
        private int previewWidth = 260;

        public ArabicSupportMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ArabicSupportSettings>();
            FontManager.RequestApply();
        }

        public override string SettingsCategory() => UiText.Get("ArabicSupport_SettingsCategory", "Arabic Support");

        public override void WriteSettings()
        {
            base.WriteSettings();
            catalogRefreshed = false;
            TextCaches.ClearProcessed();
            FontManager.RequestApply();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ArabicSupportSettings s = Settings;
            GameFont previousFont = Text.Font;
            bool scrollOpen = false;
            bool listOpen = false;
            var list = new Listing_Standard();

            // The tab shown in this frame. A click on the tab bar changes `tab` for the
            // next frame; everything below keeps using this one, so the scroll position
            // and height always belong to the content that is really drawn.
            Tab shown = tab;
            int index = (int)shown;

            try
            {
                if (!catalogRefreshed)
                {
                    FontManager.RefreshCatalog();
                    catalogRefreshed = true;
                }

                // Font changed since last frame: row heights changed, so the
                // old content heights / scroll positions are meaningless.
                if (seenApplyCount != FontManager.ApplyCount)
                {
                    seenApplyCount = FontManager.ApplyCount;
                    for (int i = 0; i < TabCount; i++)
                    {
                        contentHeights[i] = Mathf.Max(contentHeights[i], inRect.height + 400f);
                        scrolls[i].y = Mathf.Min(scrolls[i].y, Mathf.Max(0f, contentHeights[i] - inRect.height));
                    }
                }

                string before = Snapshot(s);
                string wrapBefore = WrapSnapshot(s);

                // Always-visible way out, above the tabs.
                Text.Font = GameFont.Small;
                Rect resetRect = new Rect(inRect.x, inRect.y, Mathf.Min(260f, inRect.width), 30f);
                if (Widgets.ButtonText(resetRect, UiText.Get("ArabicSupport_ResetToGameFont", "Use the game font (reset)")))
                    s.ResetToGameFont();

                // The tab bar is drawn by the game just above the rectangle it is given.
                const float tabBarHeight = 32f;
                float top = inRect.y + 36f + tabBarHeight;
                Rect menu = new Rect(inRect.x, top, inRect.width, Mathf.Max(60f, inRect.yMax - top));
                Widgets.DrawMenuSection(menu);
                DrawTabBar(menu);

                Rect scrollRect = new Rect(menu.x + 6f, menu.y + 6f, menu.width - 12f, menu.height - 12f);
                Rect view = new Rect(0f, 0f, scrollRect.width - 20f, Mathf.Max(contentHeights[index], 100f));
                Widgets.BeginScrollView(scrollRect, ref scrolls[index], view);
                scrollOpen = true;

                // One column only: by default Listing_Standard starts a new
                // column when the content is taller than the view, which put
                // the size/position settings outside the window.
                list.maxOneColumn = true;
                list.Begin(view);
                listOpen = true;

                DrawTab(list, s, shown);

                if (Snapshot(s) != before)
                    FontManager.RequestApply();

                // A wrapping option changed: wrapped labels in the cache are stale.
                if (WrapSnapshot(s) != wrapBefore)
                    TextCaches.ClearProcessed();
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
                    contentHeights[index] = Mathf.Max(list.CurHeight + 20f, 100f);
                }
                if (scrollOpen)
                    Widgets.EndScrollView();
            }
        }

        // ---- Tabs ------------------------------------------------------------------------------

        private void DrawTabBar(Rect menu)
        {
            var tabs = new List<TabRecord>
            {
                MakeTab(Tab.Font, UiText.Get("ArabicSupport_Header_CustomFont", "Custom font")),
                MakeTab(Tab.Size, UiText.Get("ArabicSupport_Tab_Size", "Size")),
                MakeTab(Tab.Layout, UiText.Get("ArabicSupport_Tab_Layout", "Layout")),
                MakeTab(Tab.Typing, UiText.Get("ArabicSupport_Header_Typing", "Typing")),
                MakeTab(Tab.Preview, UiText.Get("ArabicSupport_Header_Preview", "Preview")),
                MakeTab(Tab.Tools, UiText.Get("ArabicSupport_Tab_Tools", "Tools"))
            };

            TabDrawer.DrawTabs(menu, tabs);
        }

        private TabRecord MakeTab(Tab id, string label)
        {
            return new TabRecord(label, () => tab = id, tab == id);
        }

        private void DrawTab(Listing_Standard list, ArabicSupportSettings s, Tab shown)
        {
            switch (shown)
            {
                case Tab.Font: DrawFontTab(list, s); break;
                case Tab.Size: DrawSizeTab(list, s); break;
                case Tab.Layout: DrawLayoutTab(list, s); break;
                case Tab.Typing: DrawTypingTab(list, s); break;
                case Tab.Preview: DrawPreviewTab(list); break;
                case Tab.Tools: DrawToolsTab(list); break;
            }
        }

        // ---- Font ------------------------------------------------------------------------------

        private void DrawFontTab(Listing_Standard list, ArabicSupportSettings s)
        {
            list.CheckboxLabeled(UiText.Get("ArabicSupport_UseCustomFont", "Use a custom font"), ref s.customFontEnabled,
                UiText.Get("ArabicSupport_UseCustomFont_Tip", "Replace RimWorld's UI font with the font chosen below."));

            if (!s.customFontEnabled)
                return;

            var catalog = FontManager.Catalog;

            if (catalog.Count == 0)
            {
                Note(list, UiText.Get("ArabicSupport_NoFontsFoundIn",
                    "No fonts found in {0}. Run the 'Build Mod' GitHub action (or Tools/build_fonts.py) to create them.",
                    FontManager.FontsDirectory ?? "<mod>/Fonts"));
            }
            else
            {
                if (list.ButtonTextLabeled(UiText.Get("ArabicSupport_Font", "Font"), FontLabel(s.arabicFont)))
                    Find.WindowStack.Add(new FloatMenu(FontMenu(s)));

#if RW17
                Note(list, UiText.Get("ArabicSupport_FontsFolderHelp_RW17",
                    "Fonts live in: {0}. To add another font, copy its .fontbundle, .ttf or .otf file into that folder " +
                    "(more fonts are in the FontLibrary folder of the mod's GitHub page).",
                    FontManager.FontsDirectory ?? "<mod>/Fonts"));
#else
                Note(list, UiText.Get("ArabicSupport_FontsFolderHelp_Unity",
                    "Fonts live in: {0}. To add another font, copy its .fontbundle file into that folder " +
                    "(more fonts are in the FontLibrary folder of the mod's GitHub page).",
                    FontManager.FontsDirectory ?? "<mod>/Fonts"));
#endif
            }

#if !RW17
            // RimWorld 1.7 draws text boxes with the same font objects as labels,
            // so there is no separate choice to make there.
            list.CheckboxLabeled(UiText.Get("ArabicSupport_ApplyToTextFields", "Also use it in text fields"), ref s.applyToTextFields,
                UiText.Get("ArabicSupport_ApplyToTextFields_Tip", "Use the custom font for typing boxes (search fields, renaming, etc.) too."));
#endif

            list.CheckboxLabeled(UiText.Get("ArabicSupport_HarakatPlacement", "Place harakat on their letter"), ref s.harakatPlacement,
                UiText.Get("ArabicSupport_HarakatPlacement_Tip",
                    "Draws fatha, tanween, shadda... on top of the letter they belong to " +
                    "(for example tanween on a final alef). Works with fonts made for this mod."));

#if !RW17
            // Unity-only feature (Font.RequestCharactersInTexture); the 1.7 build does not use it.
            list.CheckboxLabeled(UiText.Get("ArabicSupport_PrewarmGlyphs", "Prepare letters in advance (smoother)"), ref s.prewarmGlyphs,
                UiText.Get("ArabicSupport_PrewarmGlyphs_Tip",
                    "Draws all Arabic and English letters once when the font is applied, so the game " +
                    "doesn't stutter the first time a new letter appears."));
#endif
        }

        // ---- Size and position -----------------------------------------------------------------

        private void DrawSizeTab(Listing_Standard list, ArabicSupportSettings s)
        {
            if (!s.customFontEnabled)
            {
                Note(list, UiText.Get("ArabicSupport_SizeNeedsFont", "Turn on the custom font to change sizes and positions."));
                return;
            }

            Header(list, UiText.Get("ArabicSupport_Header_SizePosition", "Size and position"));

            bool verticalShift = true;
#if RW17
            // Some game versions cannot move text up or down; then the sliders are not shown.
            verticalShift = FontManager.SupportsVerticalOffset;
#endif

            for (int i = 0; i < ArabicSupportSettings.SlotCount; i++)
            {
                string slotName = UiText.Raw(SlotKeys[i], SlotLabels[i]);
                int gameSize = FontManager.GameSize(i);
                Note(list, UiText.Get("ArabicSupport_SizeLine", "{0}: {1}px  (game: {2}px, offset {3})",
                    slotName, gameSize + s.sizeOffsets[i], gameSize, Signed(s.sizeOffsets[i])));
                s.sizeOffsets[i] = Mathf.RoundToInt(list.Slider(s.sizeOffsets[i], -6f, 12f));

                if (verticalShift)
                {
                    Note(list, UiText.Get("ArabicSupport_VerticalShiftLine", "{0} vertical shift: {1}px",
                        slotName, Signed(s.verticalOffsets[i])));
                    s.verticalOffsets[i] = Mathf.RoundToInt(list.Slider(s.verticalOffsets[i], -8f, 8f));
                }
            }

            if (list.ButtonText(UiText.Get("ArabicSupport_ResetFontSettings", "Reset font settings")))
                s.ResetFontSettings();
        }

        // ---- Layout: line wrapping and label height --------------------------------------------

        private void DrawLayoutTab(Listing_Standard list, ArabicSupportSettings s)
        {
            Header(list, UiText.Get("ArabicSupport_Header_Wrapping", "Line wrapping"));

            list.CheckboxLabeled(UiText.Get("ArabicSupport_WrapByPixels", "Wrap Arabic text by its real width"), ref s.wrapByPixels,
                UiText.Get("ArabicSupport_WrapByPixels_Tip",
                    "Breaks Arabic lines by measuring the real width of the text, so the line you read first comes first. " +
                    "Turn this off to let the game wrap Arabic text by itself."));

            if (s.wrapByPixels)
            {
                list.CheckboxLabeled(UiText.Get("ArabicSupport_KeepLatinRuns", "Keep English words together"), ref s.keepLatinRuns,
                    UiText.Get("ArabicSupport_KeepLatinRuns_Tip",
                        "A name or phrase in English, such as a colonist's name, never splits across two lines. " +
                        "Numbers next to it stay with it."));

                list.CheckboxLabeled(UiText.Get("ArabicSupport_SplitWideWords", "Cut words wider than the line"), ref s.splitWideWords,
                    UiText.Get("ArabicSupport_SplitWideWords_Tip",
                        "When a single word is wider than its whole box, cut it in reading order. " +
                        "Without this option the game cuts it from the wrong side."));

                Note(list, UiText.Get("ArabicSupport_WrapMarginLine", "Safety margin at the end of a line: {0}px", s.wrapMargin));
                s.wrapMargin = Mathf.RoundToInt(list.Slider(s.wrapMargin, 0f, ArabicSupportSettings.MaxWrapMargin));
                Note(list, UiText.Get("ArabicSupport_WrapMargin_Help",
                    "Lines break this many pixels earlier. Raise it if the last letter of a line is cut off."));
            }

            list.Gap(6f);

            list.CheckboxLabeled(UiText.Get("ArabicSupport_GrowLabelHeight", "Leave room for dots and tails"), ref s.growLabelHeight,
                UiText.Get("ArabicSupport_GrowLabelHeight_Tip",
                    "Makes a one-line label a few pixels taller when it needs it, so the dots under a final yeh " +
                    "and the tails of reh, waw and qaf are not cut off."));

            if (list.ButtonText(UiText.Get("ArabicSupport_ResetLayoutSettings", "Reset layout options")))
                s.ResetLayoutSettings();
        }

        // ---- Typing ----------------------------------------------------------------------------

        private void DrawTypingTab(Listing_Standard list, ArabicSupportSettings s)
        {
            list.CheckboxLabeled(UiText.Get("ArabicSupport_TextInputFix", "Arabic typing in text boxes"), ref s.textInputFix,
                UiText.Get("ArabicSupport_TextInputFix_Tip",
                    "Lets you type Arabic in names, search boxes and other text boxes: letters are joined " +
                    "and ordered like the translation. Search boxes also find Arabic words while you type."));

            Note(list, UiText.Get("ArabicSupport_TypingTest", "Try typing here:"));
            typingTest = Widgets.TextField(list.GetRect(Text.LineHeightOf(GameFont.Small) + 8f), typingTest ?? "");
        }

        // ---- Preview ---------------------------------------------------------------------------

        private void DrawPreviewTab(Listing_Standard list)
        {
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

            // A paragraph in a box of adjustable width: shows the wrapping options at work,
            // drawn through the same code as the game's own text.
            list.Gap(6f);
            float maxBox = Mathf.Max(140f, list.ColumnWidth - 8f);
            previewWidth = Mathf.Clamp(previewWidth, 120, Mathf.RoundToInt(maxBox));
            Note(list, UiText.Get("ArabicSupport_PreviewWidthLine", "Preview box width: {0}px", previewWidth));
            previewWidth = Mathf.RoundToInt(list.Slider(previewWidth, 120f, maxBox));

            GameFont keep = Text.Font;
            Text.Font = GameFont.Small;

            const float pad = 6f;
            float textHeight = Text.CalcHeight(ParagraphSample, previewWidth - 2f * pad);
            Rect row = list.GetRect(textHeight + 2f * pad);
            Rect box = new Rect(row.xMax - previewWidth, row.y, previewWidth, row.height);
            Widgets.DrawBox(box);
            Widgets.Label(new Rect(box.x + pad, box.y + pad, box.width - 2f * pad, box.height - 2f * pad), ParagraphSample);

            Text.Font = keep;
        }

        // ---- Tools -----------------------------------------------------------------------------

        private void DrawToolsTab(Listing_Standard list)
        {
            Note(list, UiText.Get("ArabicSupport_ClearCaches_Help",
                "Forgets every wrapped and measured text; the game rebuilds them as it draws. " +
                "Use it if some text still looks wrong after you changed an option."));

            if (list.ButtonText(UiText.Get("ArabicSupport_ClearCaches", "Clear text caches")))
                TextCaches.ClearAll();
        }

        // "El Messiri Bold", or "Cairo Bold (default)" for the font the mod starts with.
        private static string FontLabel(string id)
        {
            string label = FontManager.Pretty(id);
            return string.Equals(id, ArabicSupportSettings.DefaultArabicFont, System.StringComparison.OrdinalIgnoreCase)
                ? UiText.Get("ArabicSupport_FontDefault", "{0} (default)", label)
                : label;
        }

        private static List<FloatMenuOption> FontMenu(ArabicSupportSettings s)
        {
            var options = new List<FloatMenuOption>();
            foreach (string font in FontManager.Catalog)
            {
                string id = font;
                options.Add(new FloatMenuOption(FontLabel(id), () =>
                {
                    s.arabicFont = id;
                    FontManager.RequestApply();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption(UiText.Get("ArabicSupport_NoFontsFound", "No fonts found"), null));
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

        // The options that change what the wrapper produces (the label height is applied
        // while drawing and never enters the cache).
        private static string WrapSnapshot(ArabicSupportSettings s)
        {
            return string.Concat(
                s.wrapByPixels ? "1" : "0", s.keepLatinRuns ? "1" : "0", s.splitWideWords ? "1" : "0",
                "|", s.wrapMargin);
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
