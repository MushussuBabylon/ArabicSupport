using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ArabicSupport.TextInput;
using ArabicSupport.Utils;
using HarmonyLib;
using Verse;

namespace ArabicSupport.Settings
{
    /// <summary>
    /// The text of the settings window.
    ///
    /// Every string has a key in Languages/*/Keyed/ArabicSupport.xml and an English
    /// fallback written next to it in the code, so the window reads the same in a
    /// language that has no file for it and nothing is ever blank.
    ///
    /// The Arabic file holds plain Arabic in reading order (no joined forms, no
    /// reordering). The window is drawn with the same visual text the translation
    /// uses, so Arabic text is run through the mod's own shaper once and the result
    /// is kept (this is the only place the mod shapes anything at draw time, and it
    /// is a few dozen short strings, not the game's text).
    ///
    /// The game's translation lookup is called by name: if the game ever renames it,
    /// the English text is used instead of a failure.
    /// </summary>
    internal static class UiText
    {
        private const int MaxShown = 512;

        private static readonly Dictionary<string, string> shown = new Dictionary<string, string>();

        private static MethodInfo tryTranslate;
        private static bool lookupResolved;

        /// <summary>
        /// The text of <paramref name="key"/> in the active language, as plain logical text
        /// (Arabic not yet shaped). Use it for the pieces that go into <see cref="Get"/>.
        /// </summary>
        public static string Raw(string key, string english)
        {
            return TryLookup(key, out string text) ? text : english;
        }

        /// <summary>
        /// The text ready to draw: looked up, filled in with <paramref name="args"/>
        /// ({0}, {1}...), and, for Arabic, shaped and put in visual order.
        /// </summary>
        public static string Get(string key, string english, params object[] args)
        {
            return Show(Format(Raw(key, english), english, args));
        }

        /// <summary>Logical text to drawable text. English (and anything without Arabic) is returned as it is.</summary>
        public static string Show(string logical)
        {
            if (string.IsNullOrEmpty(logical) || !ArabicDetector.ContainsArabic(logical))
                return logical;

            if (shown.TryGetValue(logical, out string cached))
                return cached;

            string visual;
            try
            {
                visual = ArabicText.ToVisual(logical, out _);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Arabic Support] Could not shape a settings text: " + ex.Message, 102783481);
                visual = logical;
            }

            if (shown.Count >= MaxShown)
                shown.Clear();

            shown[logical] = visual;
            return visual;
        }

        private static string Format(string pattern, string english, object[] args)
        {
            if (args == null || args.Length == 0)
                return pattern;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                // A translation lost or misspelt a {0}: use the English pattern instead.
                try
                {
                    return string.Format(CultureInfo.InvariantCulture, english, args);
                }
                catch (FormatException)
                {
                    return english;
                }
            }
        }

        private static bool TryLookup(string key, out string text)
        {
            text = null;

            try
            {
                if (!lookupResolved)
                {
                    lookupResolved = true;

                    Type translator = AccessTools.TypeByName("Verse.Translator");
                    Type tagged = AccessTools.TypeByName("Verse.TaggedString");

                    if (translator != null && tagged != null)
                    {
                        tryTranslate = translator.GetMethod(
                            "TryTranslate",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(string), tagged.MakeByRefType() },
                            null);
                    }
                }

                if (tryTranslate == null)
                    return false;

                var args = new object[] { key, null };
                if (!(bool)tryTranslate.Invoke(null, args) || args[1] == null)
                    return false;

                text = args[1].ToString();
                return !string.IsNullOrEmpty(text);
            }
            catch (Exception ex)
            {
                tryTranslate = null;    // stop asking: the English text is used from now on
                Log.ErrorOnce("[Arabic Support] Translation lookup failed, using English: " + ex.Message, 102783482);
                return false;
            }
        }
    }
}
