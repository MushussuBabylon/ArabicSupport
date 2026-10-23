using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ArabicSupport.Caching;
using ArabicSupport.Core;
using ArabicSupport.Settings;
using UnityEngine;
using Verse;

namespace ArabicSupport.Fonts
{
    /// <summary>
    /// RimWorld 1.7 version of FontManager. Fonts/FontManager.cs is the
    /// RimWorld 1.5 / 1.6 version; exactly one of the two is compiled into a
    /// given DLL (see ArabicSupport.csproj), and both offer the same members to
    /// the rest of the mod.
    ///
    /// 1.7 does not run on Unity, so the Unity way of changing fonts
    /// (AssetBundle -> Font -> GUIStyle.font) does not exist. The new engine
    /// takes the bytes of a TrueType / OpenType file and gives back a font
    /// index, and the three Font objects behind Verse.Text (Tiny, Small,
    /// Medium) are pointed at the new index. Every GUIStyle that uses them -
    /// labels, buttons, text boxes - follows. This is the same approach the
    /// community mod "Readable Fonts 1.7" uses; see WaylithFonts for the calls.
    ///
    /// Fonts come from the Fonts folder:
    ///     Name.ttf / Name.otf   used as they are
    ///     Name.fontbundle       the Unity bundle the 1.5 / 1.6 build uses; the
    ///                           font inside is cut out of it (UnityFsFont)
    /// If both a .ttf and a .fontbundle with the same name exist the .ttf is used.
    ///
    /// Changes are applied from the start of Root.OnGUI / Text.StartOfOnGUI on
    /// the main thread (Patch_RootOnGUI, Patch_TextStartOfOnGUI).
    /// </summary>
    public static class FontManager
    {
        public const string BundleExtension = ".fontbundle";

        private const string TrueTypeExtension = ".ttf";
        private const string OpenTypeExtension = ".otf";

        // Old file names: "<Name>__Same.fontbundle" (accepted) and
        // "<Name>__<EnglishFont>.fontbundle" (ignored).
        private const string LegacySeparator = "__";
        private const string LegacySameSuffix = "__Same";

        private const int MaxFontFileBytes = 64 * 1024 * 1024;
        private const int DriftCheckIntervalMs = 500;
        private const int MaxDriftReapplies = 3;

        // Pixel sizes of Tiny, Small and Medium text in RimWorld 1.7. Only used
        // when the game's own Font objects do not report a size.
        private static readonly int[] DefaultSizes = { 12, 14, 21 };

        private sealed class LoadedFont
        {
            public string Id;
            public byte[] Data;
            public string Hash;
            public SfntCoverage Coverage;
        }

        private sealed class FontFile
        {
            public string Path;
            public int Rank;       // when several files share a name, the highest rank is used
        }

        // The game's own state, captured before the first change.
        private static bool reflectionDone;
        private static Font[] gameFonts;             // Verse.Text.fonts: Tiny, Small, Medium
        private static GUIStyle[] labelStyles;       // Verse.Text.fontStyles
        private static float[] lineHeights;
        private static float[] spaceBetweenLines;
        private static int[] originalIndices;
        private static int[] originalSizes;
        private static object[] originalMaterials;
        private static float[] originalLineHeights;
        private static float[] originalSpaceBetweenLines;

        // What this mod has applied. null = the game's own fonts are in use.
        private static int[] appliedIndices;
        private static string appliedFontId;

        private static bool pending = true;
        private static int driftReapplies;
        private static int lastDriftCheck;

        // Loaded font files stay in memory for the session; each is read at most once.
        private static readonly Dictionary<string, LoadedFont> loadedFonts =
            new Dictionary<string, LoadedFont>(StringComparer.OrdinalIgnoreCase);

        public static string Status { get; private set; } = "Waiting for the game to start drawing...";
        public static bool IsCustomFontActive => appliedIndices != null;

        /// <summary>Bumped after every apply; the settings window uses it to reset its layout.</summary>
        public static int ApplyCount { get; private set; }

        public static void RequestApply()
        {
            pending = true;
            driftReapplies = 0;
        }

        /// <summary>Called at the start of every Root.OnGUI (and Text.StartOfOnGUI).</summary>
        public static void OnGUITick()
        {
            if (pending)
            {
                string problem = WaylithFonts.Problem;
                if (problem != null)
                {
                    pending = false;
                    Status = "Custom fonts are not available: " + problem;
                    Log.Warning("[Arabic Support] " + Status + " Using the game font.");
                    HarakatMapper.Disable();
                    ApplyCount++;
                    return;
                }

                object backend = WaylithFonts.GetBackend();
                if (backend == null)
                    return;                    // the game has not started drawing yet; try again next frame

                // Don't register fonts on every frame while a slider is dragged.
                if (appliedIndices != null && MouseIsHeld())
                    return;

                pending = false;
                ApplyNow(backend);
                return;
            }

            if (appliedIndices != null)
                CheckForDrift();
        }

        // ------------------------------------------------------------------

        // The mouse test lives in two small methods so that a game version
        // without Input.GetMouseButton only loses the "wait while dragging"
        // nicety instead of the whole font feature.
        private static bool MouseIsHeld()
        {
            try
            {
                return MouseIsHeldCore();
            }
            catch
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool MouseIsHeldCore()
        {
            return Input.GetMouseButton(0);
        }

        /// <summary>
        /// Something (a language change, another font mod) pointed the game's
        /// fonts somewhere else. Take what is there now as the new "original"
        /// and apply again - but give up after a few tries so two font mods
        /// can't fight each other forever.
        /// </summary>
        private static void CheckForDrift()
        {
            int now = System.Environment.TickCount;
            if (unchecked(now - lastDriftCheck) < DriftCheckIntervalMs)
                return;
            lastDriftCheck = now;

            if (gameFonts == null || originalIndices == null || appliedIndices.Length != gameFonts.Length)
                return;

            bool drifted = false;
            for (int i = 0; i < gameFonts.Length; i++)
            {
                if (gameFonts[i] != null && WaylithFonts.GetIndex(gameFonts[i]) != appliedIndices[i])
                {
                    drifted = true;
                    break;
                }
            }

            if (!drifted)
                return;

            if (driftReapplies >= MaxDriftReapplies)
            {
                Log.WarningOnce("[Arabic Support] Another mod keeps replacing the UI font; custom font disabled for this session.", 102783460);
                appliedIndices = null;
                HarakatMapper.Disable();
                ClearTextCaches();
                return;
            }

            driftReapplies++;

            // Only the fonts that were replaced get a new "original": the others still show ours.
            for (int i = 0; i < gameFonts.Length; i++)
            {
                if (gameFonts[i] == null || WaylithFonts.GetIndex(gameFonts[i]) == appliedIndices[i])
                    continue;

                originalIndices[i] = WaylithFonts.GetIndex(gameFonts[i]);
                originalSizes[i] = SizeOrDefault(gameFonts[i], i);
                originalMaterials[i] = WaylithFonts.GetMaterial(gameFonts[i]);
            }

            pending = true;
        }

        // ------------------------------------------------------------------

        private static bool EnsureReflection()
        {
            if (reflectionDone)
                return gameFonts != null;
            reflectionDone = true;

            gameFonts = TextStaticField("fonts") as Font[];
            labelStyles = TextStaticField("fontStyles") as GUIStyle[];
            lineHeights = TextStaticField("lineHeights") as float[];
            spaceBetweenLines = TextStaticField("spaceBetweenLines") as float[];

            if (gameFonts == null || gameFonts.Length == 0)
            {
                gameFonts = null;
                Log.Warning("[Arabic Support] Text.fonts not found; the custom font cannot be used on this game version.");
                return false;
            }

            if (labelStyles == null)
                Log.Warning("[Arabic Support] Text.fontStyles not found; line heights will keep the game font's values.");

            if (lineHeights == null || spaceBetweenLines == null)
                Log.Warning("[Arabic Support] Text line-height cache not found; line heights will keep the game font's values.");

            return true;
        }

        private static object TextStaticField(string name)
        {
            FieldInfo field = typeof(Text).GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(null);
        }

        private static int SizeOrDefault(Font font, int slot)
        {
            int size = WaylithFonts.GetSize(font);
            return size > 0 ? size : DefaultSizes[Math.Max(0, Math.Min(slot, DefaultSizes.Length - 1))];
        }

        /// <summary>Remembers what the game's three fonts look like, once.</summary>
        private static bool CaptureOriginals()
        {
            if (originalIndices != null)
                return true;

            int count = gameFonts.Length;
            var indices = new int[count];
            var sizes = new int[count];
            var materials = new object[count];

            for (int i = 0; i < count; i++)
            {
                if (gameFonts[i] == null)
                {
                    Status = "RimWorld's own fonts are not ready; the custom font is unavailable.";
                    return false;
                }

                indices[i] = WaylithFonts.GetIndex(gameFonts[i]);
                sizes[i] = SizeOrDefault(gameFonts[i], i);
                materials[i] = WaylithFonts.GetMaterial(gameFonts[i]);
            }

            originalIndices = indices;
            originalSizes = sizes;
            originalMaterials = materials;
            return true;
        }

        private static void CaptureOriginalMetrics()
        {
            if (originalLineHeights != null || lineHeights == null || spaceBetweenLines == null)
                return;

            originalLineHeights = (float[])lineHeights.Clone();
            originalSpaceBetweenLines = (float[])spaceBetweenLines.Clone();
        }

        /// <summary>Game's own pixel size for a GameFont slot (for the settings UI).</summary>
        public static int GameSize(int index)
        {
            if (originalSizes != null && index >= 0 && index < originalSizes.Length)
                return originalSizes[index];
            return DefaultSizes[Math.Max(0, Math.Min(index, DefaultSizes.Length - 1))];
        }

        private static void ApplyNow(object backend)
        {
            try
            {
                if (!EnsureReflection())
                {
                    Status = "Text.fonts not found - custom fonts unavailable on this game version.";
                    HarakatMapper.Disable();
                    return;
                }

                if (!CaptureOriginals())
                {
                    HarakatMapper.Disable();
                    return;
                }

                CaptureOriginalMetrics();

                ArabicSupportSettings settings = ArabicSupportMod.Settings;
                LoadedFont font = null;
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
                    ApplyFont(backend, font, settings);
                    Status = "Using " + Pretty(fontId) + ".";
                }

                RecomputeLineMetrics();

                // Harakat placement needs the pre-placed harakat glyphs that fonts built by this mod contain.
                bool hasHarakatGlyphs = appliedIndices != null && font != null && font.Coverage.Has(HarakatMapper.ProbeCodePoint);
                HarakatMapper.Refresh(hasHarakatGlyphs, settings != null && settings.harakatPlacement);
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

        private static void ApplyFont(object backend, LoadedFont font, ArabicSupportSettings settings)
        {
            int count = gameFonts.Length;
            var indices = new int[count];

            // Register every size first: if the engine refuses one of them the
            // game's fonts have not been touched yet.
            for (int i = 0; i < count; i++)
            {
                int size = Math.Max(4, originalSizes[i] + settings.GetSizeOffset(i));

                // The engine remembers a font by name and byte length only, so the hash and size are part of the name.
                string name = "ArabicSupport/" + font.Hash + "/" + i + "/" + size;

                indices[i] = WaylithFonts.Register(backend, name, font.Data, size);
                if (indices[i] < 0)
                    throw new InvalidDataException("the game's font system rejected '" + font.Id + "'.");
            }

            for (int i = 0; i < count; i++)
            {
                WaylithFonts.SetIndex(gameFonts[i], indices[i]);
                WaylithFonts.SetMaterial(gameFonts[i], null);
            }

            appliedIndices = indices;
            appliedFontId = font.Id;
            ApplyContentOffsets(settings);
        }

        private static void RestoreOriginals()
        {
            if (appliedIndices != null && originalIndices != null && gameFonts != null)
            {
                int count = Math.Min(gameFonts.Length, Math.Min(appliedIndices.Length, originalIndices.Length));
                for (int i = 0; i < count; i++)
                {
                    if (gameFonts[i] == null)
                        continue;

                    // Another mod changed this font after us: leave its choice alone.
                    if (WaylithFonts.GetIndex(gameFonts[i]) != appliedIndices[i])
                        continue;

                    WaylithFonts.SetIndex(gameFonts[i], originalIndices[i]);
                    WaylithFonts.SetMaterial(gameFonts[i], originalMaterials[i]);
                }
            }

            RestoreContentOffsets();
            appliedIndices = null;
            appliedFontId = null;
        }

        // ------------------------------------------------------------------
        // Moving text up or down ("vertical shift"): the content offset of
        // RimWorld's label styles. Reached by reflection so that a game
        // version without it only loses this one setting.

        private static PropertyInfo contentOffsetProperty;
        private static bool contentOffsetResolved;
        private static Vector2[] originalContentOffsets;

        private static bool ResolveContentOffsets()
        {
            if (!contentOffsetResolved)
            {
                contentOffsetResolved = true;
                try
                {
                    contentOffsetProperty = typeof(GUIStyle).GetProperty("contentOffset", BindingFlags.Public | BindingFlags.Instance);
                }
                catch
                {
                    contentOffsetProperty = null;
                }
            }

            return contentOffsetProperty != null && labelStyles != null;
        }

        /// <summary>False when this game version cannot move text up or down (the settings hide the sliders then).</summary>
        public static bool SupportsVerticalOffset
        {
            get
            {
                EnsureReflection();
                return ResolveContentOffsets();
            }
        }

        private static void ApplyContentOffsets(ArabicSupportSettings settings)
        {
            if (!ResolveContentOffsets())
                return;

            try
            {
                if (originalContentOffsets == null)
                {
                    originalContentOffsets = new Vector2[labelStyles.Length];
                    for (int i = 0; i < labelStyles.Length; i++)
                    {
                        originalContentOffsets[i] = labelStyles[i] == null
                            ? Vector2.zero
                            : (Vector2)contentOffsetProperty.GetValue(labelStyles[i], null);
                    }
                }

                for (int i = 0; i < labelStyles.Length; i++)
                {
                    if (labelStyles[i] == null)
                        continue;

                    Vector2 original = originalContentOffsets[i];
                    var shifted = new Vector2(original.x, original.y + settings.GetVerticalOffset(i));
                    contentOffsetProperty.SetValue(labelStyles[i], shifted, null);
                }
            }
            catch (Exception ex)
            {
                contentOffsetProperty = null;
                Log.Warning("[Arabic Support] Could not move text up or down on this game version: " + ex.Message);
            }
        }

        private static void RestoreContentOffsets()
        {
            if (originalContentOffsets == null || labelStyles == null || contentOffsetProperty == null)
                return;

            try
            {
                for (int i = 0; i < labelStyles.Length && i < originalContentOffsets.Length; i++)
                {
                    if (labelStyles[i] != null)
                        contentOffsetProperty.SetValue(labelStyles[i], originalContentOffsets[i], null);
                }
            }
            catch
            {
                // Nothing more can be done; the next full apply tries again.
            }
        }

        // ------------------------------------------------------------------

        // Same measurement Verse.Text's static constructor does - with the
        // game font active, the original values are restored instead. Every
        // new value is sanity-checked: a font that reports a zero / absurd
        // line height would otherwise collapse every row in the UI.
        private static void RecomputeLineMetrics()
        {
            if (lineHeights == null || spaceBetweenLines == null || originalLineHeights == null)
                return;

            int n = Math.Min(lineHeights.Length, spaceBetweenLines.Length);
            n = Math.Min(n, Math.Min(originalLineHeights.Length, originalSpaceBetweenLines.Length));

            if (appliedIndices == null || labelStyles == null)
            {
                for (int i = 0; i < n; i++)
                {
                    lineHeights[i] = originalLineHeights[i];
                    spaceBetweenLines[i] = originalSpaceBetweenLines[i];
                }
                return;
            }

            var content = new GUIContent();
            bool bad = false;
            var newHeights = new float[n];
            var newGaps = new float[n];

            for (int i = 0; i < n; i++)
            {
                newHeights[i] = originalLineHeights[i];
                newGaps[i] = originalSpaceBetweenLines[i];

                if (i >= labelStyles.Length || labelStyles[i] == null)
                    continue;

                content.text = "W";
                float one = labelStyles[i].CalcHeight(content, 999f);
                content.text = "W\nW";
                float two = labelStyles[i].CalcHeight(content, 999f);
                float gap = two - one * 2f;

                float orig = originalLineHeights[i];
                if (float.IsNaN(one) || float.IsInfinity(one) || one < orig * 0.6f || one > orig * 2.5f ||
                    float.IsNaN(gap) || float.IsInfinity(gap) || Math.Abs(gap) > orig)
                {
                    bad = true;
                    break;
                }

                newHeights[i] = one;
                newGaps[i] = gap;
            }

            if (bad)
            {
                string fontName = appliedFontId ?? "The font";
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

        private static MethodInfo clearLabelCache;
        private static bool clearLabelCacheResolved;

        private static void ClearTextCaches()
        {
            ProcessedTextCache.Clear();
            TextMeasurer.ClearCache();
            LineWrapper.ClearCache();

            // RimWorld also remembers the height of recently drawn labels, measured with the old font.
            if (!clearLabelCacheResolved)
            {
                clearLabelCacheResolved = true;
                clearLabelCache = typeof(Widgets).GetMethod(
                    "ClearLabelCache",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
            }

            if (clearLabelCache != null)
            {
                try
                {
                    clearLabelCache.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[Arabic Support] Widgets.ClearLabelCache failed: " + ex.Message, 102783464);
                }
            }
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

        // Font id -> font file.
        private static Dictionary<string, FontFile> catalog;
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
            var result = new Dictionary<string, FontFile>(StringComparer.OrdinalIgnoreCase);
            string dir = FontsDirectory;

            try
            {
                if (dir != null && Directory.Exists(dir))
                {
                    foreach (string path in Directory.GetFiles(dir))
                    {
                        string extension = Path.GetExtension(path);
                        string name = Path.GetFileNameWithoutExtension(path);
                        string id = name;
                        int rank;

                        if (extension.Equals(TrueTypeExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            rank = 4;
                        }
                        else if (extension.Equals(OpenTypeExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            rank = 3;
                        }
                        else if (extension.Equals(BundleExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            rank = 2;

                            if (name.IndexOf(LegacySeparator, StringComparison.Ordinal) >= 0)
                            {
                                // Old build: keep "<Name>__Same", skip separate-English pairs.
                                if (!name.EndsWith(LegacySameSuffix, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                id = name.Substring(0, name.Length - LegacySameSuffix.Length);
                                rank = 1;
                            }
                        }
                        else
                        {
                            continue;
                        }

                        if (id.Length == 0)
                            continue;

                        FontFile existing;
                        if (result.TryGetValue(id, out existing) && existing.Rank >= rank)
                            continue;

                        result[id] = new FontFile { Path = path, Rank = rank };
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

        private static LoadedFont ResolveFont(ArabicSupportSettings settings, out string fontId, out string error)
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
                error = "No font files (.ttf, .otf or .fontbundle) in " + dir + ". Run the 'Build Mod' GitHub action with 'build fonts' ticked (or Tools/build_fonts.py).";
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

            LoadedFont cached;
            if (loadedFonts.TryGetValue(id, out cached))
                return cached;

            string path = catalog[id].Path;
            byte[] data = LoadFontFile(path, out error);
            if (data == null)
                return null;

            SfntCoverage coverage;
            try
            {
                coverage = SfntCoverage.Read(data);
            }
            catch (Exception ex)
            {
                error = $"'{Path.GetFileName(path)}' is not a usable font ({ex.Message}).";
                return null;
            }

            if (!coverage.Has('A') && !coverage.Has(0x0627))
            {
                error = $"Font in '{Path.GetFileName(path)}' has no usable glyphs.";
                return null;
            }

            var loaded = new LoadedFont
            {
                Id = id,
                Data = data,
                Hash = Sha256Hex(data),
                Coverage = coverage
            };

            loadedFonts[id] = loaded;
            return loaded;
        }

        /// <summary>The bytes of the TrueType / OpenType font in a .ttf, .otf or .fontbundle file.</summary>
        private static byte[] LoadFontFile(string path, out string error)
        {
            error = null;
            string fileName = Path.GetFileName(path);

            try
            {
                if (!File.Exists(path))
                {
                    error = $"'{fileName}' is missing.";
                    return null;
                }

                var info = new FileInfo(path);
                if (info.Length < 12 || info.Length > MaxFontFileBytes)
                {
                    error = $"'{fileName}' is not a usable font file.";
                    return null;
                }

                byte[] file = File.ReadAllBytes(path);
                byte[] font;

                if (path.EndsWith(BundleExtension, StringComparison.OrdinalIgnoreCase))
                {
                    string reason;
                    font = UnityFsFont.Extract(file, out reason);
                    if (font == null)
                    {
                        error = $"'{fileName}': {reason}";
                        return null;
                    }
                }
                else
                {
                    font = file;
                }

                if (!SfntFile.LooksLikeFont(font))
                {
                    error = $"'{fileName}' is not a TrueType or OpenType font.";
                    return null;
                }

                Log.Message($"[Arabic Support] Loaded font {fileName}.");
                return font;
            }
            catch (Exception ex)
            {
                error = $"Failed to load '{fileName}': {ex.Message}";
                return null;
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            const string digits = "0123456789abcdef";

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var chars = new char[hash.Length * 2];

                for (int i = 0; i < hash.Length; i++)
                {
                    chars[2 * i] = digits[hash[i] >> 4];
                    chars[2 * i + 1] = digits[hash[i] & 15];
                }

                return new string(chars);
            }
        }
    }
}
