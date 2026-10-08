using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArabicSupport.Caching;
using ArabicSupport.Core;
using ArabicSupport.Settings;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Fonts
{
    /// <summary>
    /// Replaces RimWorld's UI font with one of the fonts in the mod's Fonts
    /// folder.
    ///
    /// Every "&lt;Name&gt;.fontbundle" file in Fonts/ is one font (built by
    /// Tools/build_fonts.py from FontSources/). The font's own Latin letters
    /// are used for English text. Players can drop extra .fontbundle files
    /// (for example from the ExtraFonts folder of the GitHub repository)
    /// into Fonts/ and they show up in the settings automatically.
    ///
    /// Older builds named their files "&lt;Name&gt;__Same.fontbundle"; those
    /// are still accepted (shown as "&lt;Name&gt;"). Old pair files with a
    /// separate English font ("&lt;Name&gt;__Roboto" etc.) are ignored.
    ///
    /// RimWorld draws all IMGUI text through the GUIStyle arrays on
    /// Verse.Text (fontStyles, textFieldStyles, textAreaStyles,
    /// textAreaReadOnlyStyles), one style per GameFont. Changing the font on
    /// those styles in place changes it everywhere, including Text.CalcSize /
    /// CalcHeight, so this mod's wrapping measures with the new font.
    /// Text's private line-height cache is recomputed after each change.
    ///
    /// Changes are applied from Root.OnGUI (Patch_RootOnGUI), main thread,
    /// and only at the start of a frame (the Layout event) so that a window's
    /// layout and repaint passes never see two different fonts.
    /// </summary>
    public static class FontManager
    {
        private struct OriginalStyle
        {
            public Font Font;
            public int FontSize;       // the raw value stored on the style (may be 0)
            public int EffectiveSize;  // what that 0 actually meant in pixels
            public Vector2 ContentOffset;
        }

        private static readonly string[] StyleFieldNames =
        {
            "fontStyles", "textFieldStyles", "textAreaStyles", "textAreaReadOnlyStyles"
        };

        // Fallback sizes only used if neither the style nor its font report one.
        private static readonly int[] DefaultSizes = { 11, 14, 20 };

        public const string BundleExtension = ".fontbundle";

        // Old file names: "<Name>__Same.fontbundle" (accepted) and
        // "<Name>__<EnglishFont>.fontbundle" (ignored).
        private const string LegacySeparator = "__";
        private const string LegacySameSuffix = "__Same";

        private static GUIStyle[][] styleGroups;
        private static OriginalStyle[][] originals;
        private static float[] lineHeights;
        private static float[] spaceBetweenLines;
        private static float[] originalLineHeights;
        private static float[] originalSpaceBetweenLines;
        private static bool reflectionDone;

        // Fonts (and their bundles) stay loaded for the session; each font is
        // loaded at most once.
        private static readonly Dictionary<string, Font> loadedFonts = new Dictionary<string, Font>();
        private static readonly List<AssetBundle> loadedBundles = new List<AssetBundle>();

        private static bool pending = true;
        private static Font appliedFont;
        private static int driftReapplies;
        private const int MaxDriftReapplies = 3;

        public static string Status { get; private set; } = "";
        public static bool IsCustomFontActive => appliedFont != null;
        public static Font AppliedFont => appliedFont;

        /// <summary>Bumped after every apply; the settings window uses it to reset its layout.</summary>
        public static int ApplyCount { get; private set; }

        public static void RequestApply()
        {
            pending = true;
            driftReapplies = 0;
        }

        /// <summary>Called at the start of every Root.OnGUI.</summary>
        public static void OnGUITick()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Layout)
                return;

            if (pending)
            {
                // Don't rebuild fonts every frame while a slider is dragged.
                if (Input.GetMouseButton(0) && appliedFont != null)
                    return;

                pending = false;
                ApplyNow();
                return;
            }

            // Something (a language reload, another mod) replaced our font on
            // the styles. Re-capture what is there now as the new "original"
            // and apply again - but give up after a few tries so two font
            // mods can't fight each other every frame.
            if (appliedFont != null && styleGroups != null && styleGroups[0] != null &&
                styleGroups[0].Length > 1 && styleGroups[0][1] != null &&
                styleGroups[0][1].font != appliedFont)
            {
                if (driftReapplies >= MaxDriftReapplies)
                {
                    Log.WarningOnce("[Arabic Support] Another mod keeps replacing the UI font; custom font disabled for this session.", 102783460);
                    appliedFont = null;
                    ClearTextCaches();
                    return;
                }

                driftReapplies++;
                originals = null;
                appliedFont = null;
                ApplyNow();
            }
        }

        // ------------------------------------------------------------------

        private static void EnsureReflection()
        {
            if (reflectionDone)
                return;
            reflectionDone = true;

            styleGroups = new GUIStyle[StyleFieldNames.Length][];
            for (int g = 0; g < StyleFieldNames.Length; g++)
            {
                styleGroups[g] = AccessTools.Field(typeof(Text), StyleFieldNames[g])?.GetValue(null) as GUIStyle[];
                if (styleGroups[g] == null)
                    Log.Warning($"[Arabic Support] Text.{StyleFieldNames[g]} not found; that style group won't use the custom font.");
            }

            lineHeights = AccessTools.Field(typeof(Text), "lineHeights")?.GetValue(null) as float[];
            spaceBetweenLines = AccessTools.Field(typeof(Text), "spaceBetweenLines")?.GetValue(null) as float[];

            if (lineHeights == null || spaceBetweenLines == null)
                Log.Warning("[Arabic Support] Text line-height cache not found; line heights will keep the game font's values.");
        }

        private static void CaptureOriginalMetrics()
        {
            if (originalLineHeights != null || lineHeights == null || spaceBetweenLines == null)
                return;

            originalLineHeights = (float[])lineHeights.Clone();
            originalSpaceBetweenLines = (float[])spaceBetweenLines.Clone();
        }

        private static void CaptureOriginals()
        {
            if (originals != null)
                return;

            originals = new OriginalStyle[styleGroups.Length][];

            for (int g = 0; g < styleGroups.Length; g++)
            {
                GUIStyle[] group = styleGroups[g];
                if (group == null)
                    continue;

                originals[g] = new OriginalStyle[group.Length];

                for (int i = 0; i < group.Length; i++)
                {
                    GUIStyle s = group[i];
                    if (s == null)
                        continue;

                    int effective = s.fontSize > 0
                        ? s.fontSize
                        : (s.font != null && s.font.fontSize > 0 ? s.font.fontSize : DefaultSizes[Mathf.Clamp(i, 0, DefaultSizes.Length - 1)]);

                    originals[g][i] = new OriginalStyle
                    {
                        Font = s.font,
                        FontSize = s.fontSize,
                        EffectiveSize = effective,
                        ContentOffset = s.contentOffset
                    };
                }
            }
        }

        /// <summary>Game's own pixel size for a GameFont slot (for the settings UI).</summary>
        public static int GameSize(int index)
        {
            if (originals != null && originals[0] != null && index < originals[0].Length)
                return originals[0][index].EffectiveSize;
            return DefaultSizes[Mathf.Clamp(index, 0, DefaultSizes.Length - 1)];
        }

        private static void ApplyNow()
        {
            try
            {
                EnsureReflection();
                if (styleGroups[0] == null)
                {
                    Status = "Text.fontStyles not found - custom fonts unavailable on this game version.";
                    return;
                }

                CaptureOriginals();
                CaptureOriginalMetrics();

                ArabicSupportSettings settings = ArabicSupportMod.Settings;
                Font font = null;
                string fontId = null;

                if (settings != null && settings.customFontEnabled)
                {
                    font = ResolveFont(settings, out fontId, out string error);
                    if (font == null)
                    {
                        Status = error;
                        Log.Warning("[Arabic Support] " + error + " Using the game font.");
                    }
                }

                if (font == null)
                {
                    RestoreOriginals();
                    if (settings == null || !settings.customFontEnabled)
                        Status = "Using the game font.";
                }
                else
                {
                    ApplyFont(font, settings);
                    Status = "Using " + Pretty(fontId) + ".";
                }

                RecomputeLineMetrics();

                if (appliedFont != null && settings.prewarmGlyphs)
                    Prewarm();

                HarakatMapper.Refresh(appliedFont, settings != null && settings.harakatPlacement);
                ClearTextCaches();
            }
            catch (Exception ex)
            {
                Status = "Failed to apply font: " + ex.Message;
                Log.Error("[Arabic Support] Failed to apply custom font: " + ex);
                try { HarakatMapper.Disable(); RestoreOriginals(); RecomputeLineMetrics(); ClearTextCaches(); } catch { }
            }
            finally
            {
                ApplyCount++;
            }
        }

        private static void ApplyFont(Font font, ArabicSupportSettings settings)
        {
            for (int g = 0; g < styleGroups.Length; g++)
            {
                GUIStyle[] group = styleGroups[g];
                if (group == null || originals[g] == null)
                    continue;

                bool isLabelGroup = g == 0;
                bool use = isLabelGroup || settings.applyToTextFields;

                for (int i = 0; i < group.Length; i++)
                {
                    GUIStyle s = group[i];
                    if (s == null)
                        continue;

                    OriginalStyle o = originals[g][i];

                    if (!use)
                    {
                        s.font = o.Font;
                        s.fontSize = o.FontSize;
                        s.contentOffset = o.ContentOffset;
                        continue;
                    }

                    s.font = font;
                    s.fontSize = Mathf.Max(4, o.EffectiveSize + settings.GetSizeOffset(i));

                    // Vertical nudge only for plain labels: offsetting text
                    // fields would misplace the caret relative to the text.
                    s.contentOffset = isLabelGroup
                        ? o.ContentOffset + new Vector2(0f, settings.GetVerticalOffset(i))
                        : o.ContentOffset;
                }
            }

            appliedFont = font;
        }

        private static void RestoreOriginals()
        {
            if (originals != null)
            {
                for (int g = 0; g < styleGroups.Length; g++)
                {
                    GUIStyle[] group = styleGroups[g];
                    if (group == null || originals[g] == null)
                        continue;

                    for (int i = 0; i < group.Length; i++)
                    {
                        if (group[i] == null)
                            continue;
                        group[i].font = originals[g][i].Font;
                        group[i].fontSize = originals[g][i].FontSize;
                        group[i].contentOffset = originals[g][i].ContentOffset;
                    }
                }
            }

            appliedFont = null;
        }

        // Rasterise the common glyphs now, once, instead of one by one in the
        // middle of the game (each new glyph can make Unity rebuild the font
        // texture, which is a visible hitch on slow devices). Covers every
        // size the custom font is used at: labels AND text fields (the base
        // Arabic letters typed into text boxes are included).
        private static string prewarmText;

        private static void Prewarm()
        {
            try
            {
                if (prewarmText == null)
                {
                    var sb = new StringBuilder(400);
                    for (char c = ' '; c <= '~'; c++) sb.Append(c);
                    for (char c = '\u0621'; c <= '\u064A'; c++) sb.Append(c);
                    for (char c = '\u0660'; c <= '\u0669'; c++) sb.Append(c);
                    sb.Append('\u060C').Append('\u061B').Append('\u061F');
                    for (char c = '\uFE80'; c <= '\uFEFC'; c++) sb.Append(c);
                    prewarmText = sb.ToString();
                }

                var sizes = new HashSet<int>();
                for (int g = 0; g < styleGroups.Length; g++)
                {
                    GUIStyle[] group = styleGroups[g];
                    if (group == null)
                        continue;
                    for (int i = 0; i < group.Length; i++)
                        if (group[i] != null && group[i].font == appliedFont)
                            sizes.Add(group[i].fontSize);
                }

                foreach (int size in sizes)
                    appliedFont.RequestCharactersInTexture(prewarmText, size, FontStyle.Normal);
            }
            catch (Exception ex)
            {
                Log.Warning("[Arabic Support] Glyph prewarm failed: " + ex.Message);
            }
        }

        // Same measurement Verse.Text's static constructor does - with the
        // game font active, the original values are restored instead. Every
        // new value is sanity-checked: a font that reports a zero / absurd
        // line height would otherwise collapse every row in the UI.
        private static void RecomputeLineMetrics()
        {
            if (lineHeights == null || spaceBetweenLines == null || originalLineHeights == null)
                return;

            int n = Mathf.Min(lineHeights.Length, spaceBetweenLines.Length);

            if (appliedFont == null)
            {
                for (int i = 0; i < n; i++)
                {
                    lineHeights[i] = originalLineHeights[i];
                    spaceBetweenLines[i] = originalSpaceBetweenLines[i];
                }
                return;
            }

            GUIStyle[] labels = styleGroups[0];
            var content = new GUIContent();
            bool bad = false;
            var newHeights = new float[n];
            var newGaps = new float[n];

            for (int i = 0; i < n; i++)
            {
                newHeights[i] = originalLineHeights[i];
                newGaps[i] = originalSpaceBetweenLines[i];

                if (i >= labels.Length || labels[i] == null)
                    continue;

                content.text = "W";
                float one = labels[i].CalcHeight(content, 999f);
                content.text = "W\nW";
                float two = labels[i].CalcHeight(content, 999f);
                float gap = two - one * 2f;

                float orig = originalLineHeights[i];
                if (float.IsNaN(one) || float.IsInfinity(one) || one < orig * 0.6f || one > orig * 2.5f ||
                    float.IsNaN(gap) || float.IsInfinity(gap) || Mathf.Abs(gap) > orig)
                {
                    bad = true;
                    break;
                }

                newHeights[i] = one;
                newGaps[i] = gap;
            }

            if (bad)
            {
                string fontName = appliedFont.name;
                RestoreOriginals();
                for (int i = 0; i < n; i++)
                {
                    lineHeights[i] = originalLineHeights[i];
                    spaceBetweenLines[i] = originalSpaceBetweenLines[i];
                }
                Status = $"'{fontName}' reports broken line heights and can't be used. Using the game font.";
                Log.Warning("[Arabic Support] " + Status);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                lineHeights[i] = newHeights[i];
                spaceBetweenLines[i] = newGaps[i];
            }
        }

        private static void ClearTextCaches()
        {
            ProcessedTextCache.Clear();
            TextMeasurer.ClearCache();
            LineWrapper.ClearCache();
        }

        // ------------------------------------------------------------------
        // Font catalog

        public static string FontsDirectory
        {
            get
            {
                string root = ArabicSupportMod.Instance?.Content?.RootDir;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Fonts");
            }
        }

        // Font id -> bundle file path.
        private static Dictionary<string, string> catalog;
        private static List<string> catalogIds;

        /// <summary>Available font ids, sorted by family and then from light to heavy.</summary>
        public static List<string> Catalog
        {
            get
            {
                if (catalog == null)
                    RefreshCatalog();
                return catalogIds;
            }
        }

        public static bool HasFont(string id)
        {
            if (catalog == null)
                RefreshCatalog();
            return id != null && catalog.ContainsKey(id);
        }

        public static void RefreshCatalog()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string dir = FontsDirectory;

            try
            {
                if (dir != null && Directory.Exists(dir))
                {
                    foreach (string path in Directory.GetFiles(dir, "*" + BundleExtension))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        string id = name;

                        if (name.IndexOf(LegacySeparator, StringComparison.Ordinal) >= 0)
                        {
                            // Old build: keep "<Name>__Same", skip separate-English pairs.
                            if (!name.EndsWith(LegacySameSuffix, StringComparison.OrdinalIgnoreCase))
                                continue;
                            id = name.Substring(0, name.Length - LegacySameSuffix.Length);
                            if (id.Length == 0 || result.ContainsKey(id))
                                continue; // a new-style "<Name>.fontbundle" wins
                        }

                        if (id.Length > 0)
                            result[id] = path;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Arabic Support] Could not list Fonts folder: " + ex.Message);
            }

            var ids = new List<string>(result.Keys);
            ids.Sort(CompareFontIds);

            catalog = result;
            catalogIds = ids;
        }

        // "Cairo-Regular" < "Cairo-Medium" < "Cairo-Bold" < "Tajawal-Bold" ...
        private static int CompareFontIds(string a, string b)
        {
            SplitId(a, out string famA, out string styleA);
            SplitId(b, out string famB, out string styleB);

            int c = string.Compare(famA, famB, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
                return c;

            c = WeightRank(styleA).CompareTo(WeightRank(styleB));
            return c != 0 ? c : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static void SplitId(string id, out string family, out string style)
        {
            int dash = id.LastIndexOf('-');
            if (dash <= 0)
            {
                family = id;
                style = "";
                return;
            }
            family = id.Substring(0, dash);
            style = id.Substring(dash + 1);
        }

        private static int WeightRank(string style)
        {
            switch (style.ToLowerInvariant())
            {
                case "thin": return 100;
                case "extralight": return 200;
                case "light": return 300;
                case "":
                case "regular": return 400;
                case "medium": return 500;
                case "semibold": return 600;
                case "bold": return 700;
                case "extrabold": return 800;
                case "black": return 900;
                default: return 1000;
            }
        }

        /// <summary>"IBMSansArabic-SemiBold" -> "IBM Sans Arabic SemiBold".</summary>
        public static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";

            var sb = new StringBuilder(id.Length + 8);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '-' || c == '_')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ' &&
                    (char.IsLower(id[i - 1]) || (i + 1 < id.Length && char.IsLower(id[i + 1]) && char.IsUpper(id[i - 1]))))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Replace("Semi Bold", "SemiBold").Replace("Extra Bold", "ExtraBold")
                                .Replace("Extra Light", "ExtraLight");
        }

        // ------------------------------------------------------------------
        // Loading

        private static Font ResolveFont(ArabicSupportSettings settings, out string fontId, out string error)
        {
            error = null;
            fontId = null;

            string dir = FontsDirectory;
            if (dir == null)
            {
                error = "Mod folder not found.";
                return null;
            }

            List<string> ids = Catalog;
            if (ids.Count == 0)
            {
                error = "No .fontbundle files in " + dir + ". Run the 'Build Mod' GitHub action with 'build fonts' ticked (or Tools/build_fonts.py).";
                return null;
            }

            string id = settings.arabicFont;
            if (!catalog.ContainsKey(id))
            {
                string fallback = catalog.ContainsKey(ArabicSupportSettings.DefaultArabicFont)
                    ? ArabicSupportSettings.DefaultArabicFont
                    : ids[0];
                Log.Message($"[Arabic Support] Font '{id}' not found, using '{fallback}'.");
                id = settings.arabicFont = fallback;
            }

            fontId = id;
            if (loadedFonts.TryGetValue(id, out Font cached) && cached != null)
                return cached;

            string bundlePath = catalog[id];
            Font font = LoadFontFromBundle(bundlePath, out error);
            if (font == null)
                return null;

            if (!LooksValid(font))
            {
                error = $"Font in '{Path.GetFileName(bundlePath)}' has no usable glyphs.";
                return null;
            }

            font.name = id;
            loadedFonts[id] = font;
            return font;
        }

        private static Font LoadFontFromBundle(string bundlePath, out string error)
        {
            error = null;
            AssetBundle bundle = null;

            try
            {
                if (!File.Exists(bundlePath))
                {
                    error = $"'{Path.GetFileName(bundlePath)}' is missing.";
                    return null;
                }

                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    error = $"Unity could not open '{Path.GetFileName(bundlePath)}'.";
                    return null;
                }

                // Kept loaded for the session: the Font object lives in it.
                loadedBundles.Add(bundle);

                Font[] fonts = bundle.LoadAllAssets<Font>();
                foreach (Font f in fonts)
                {
                    if (f != null && f.dynamic)
                    {
                        Log.Message($"[Arabic Support] Loaded font {Path.GetFileName(bundlePath)}.");
                        return f;
                    }
                }

                error = $"'{Path.GetFileName(bundlePath)}' contains no dynamic font.";
                return null;
            }
            catch (Exception ex)
            {
                error = $"Failed to load '{Path.GetFileName(bundlePath)}': {ex.Message}";
                return null;
            }
        }

        private static bool LooksValid(Font font)
        {
            if (font == null)
                return false;

            try
            {
                if (font.HasCharacter('A') || font.HasCharacter('\u0627'))
                    return true;

                font.RequestCharactersInTexture("A\u0627", 16);
                return font.GetCharacterInfo('A', out _, 16) || font.GetCharacterInfo('\u0627', out _, 16);
            }
            catch
            {
                return false;
            }
        }
    }
}
