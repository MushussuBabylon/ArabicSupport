using System;
using System.Text.RegularExpressions;
using ArabicSupport.Settings;
using ArabicSupport.TextInput;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    /// <summary>
    /// Makes typing Arabic work in RimWorld text fields (pawn and animal
    /// names, search boxes, rename dialogs, notes...).
    ///
    /// Why nothing appears without it: Unity's text field only inserts a
    /// typed character if the field's font has a glyph for it
    /// (Font.HasCharacter). Fonts made for pre-shaped text often have the
    /// joined presentation forms (U+FE70-U+FEFC) but not the base letters
    /// (U+0621-U+064A) a keyboard produces, so every keystroke is silently
    /// dropped. And even when the font does have them, Unity inserts them
    /// unjoined and left-to-right.
    ///
    /// What this does, for Widgets.TextField / Widgets.TextArea:
    ///  - typing at the "end" of the text (where the next letter is the last
    ///    one in reading order) is handled here: the typed letter is added to
    ///    the logical text, which is re-shaped and re-ordered into the same
    ///    visual form the translation uses, and handed back to the field.
    ///  - typing anywhere else: the letter is swapped for its isolated form
    ///    (a glyph the font has) so Unity accepts it, then the edited line is
    ///    re-shaped afterwards.
    ///  - Backspace / Delete follow reading order inside Arabic text.
    ///  - pasted Arabic is re-shaped after the edit.
    ///
    /// The stored text is visual, exactly like the translation, so it shows
    /// correctly everywhere the game draws it (nameplates, labels, saves).
    ///
    /// Three small patch classes (one per Widgets overload) share this core.
    /// Typed parameters are used instead of Harmony's object[] __args, so a
    /// text field that is merely drawn (every frame) costs one int compare
    /// and allocates nothing.
    ///
    /// Only the outermost patched call does any work (depth), so wrappers
    /// such as TextField(rect, text, max, regex) -> TextField(rect, text)
    /// are not processed twice.
    /// </summary>
    public static class TextInputCore
    {
        /// <summary>
        /// Arabic-Indic digits typed on some Arabic layouts (U+0660-U+0669,
        /// U+06F0-U+06F9) are converted to 0-9. The game's numbers are 0-9,
        /// and the font may not have the Arabic-Indic digit glyphs.
        /// </summary>
        private const bool ConvertArabicIndicDigits = true;

        public struct InputState
        {
            public bool Outer;          // this call is the outermost patched call
            public string Original;     // text as the caller passed it in (set on key events only)
            public bool Handled;        // we produced the final text ourselves
            public bool Changed;        // ... and it differs from Original
            public bool HasAnchor;      // visual index of the edit is known
            public int Anchor;
            public bool Inserted;       // a character was inserted by Unity
        }

        private static int depth;
        private static bool loggedFirstKey;
        private static bool loggedRenormalizeFailure;

        // ------------------------------------------------------------------
        // Entry points used by the three patch classes

        public static void Prefix(Rect rect, ref string text, int maxLength, Regex validator, bool readOnly,
            out InputState state)
        {
            state = default(InputState);
            state.Outer = depth == 0;
            depth++;

            if (!state.Outer || readOnly) return;

            // Cheapest rejection first: this runs for every text field on
            // every GUI event (layout, repaint, mouse...), not just typing.
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            ArabicSupportSettings settings = ArabicSupportMod.Settings;
            if (settings != null && !settings.textInputFix) return;

            try
            {
                PrefixImpl(e, rect, ref text, maxLength, validator, ref state);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Text input (prefix) failed: {ex}", 102783470);
            }
        }

        public static void Postfix(Rect rect, ref string result, InputState state)
        {
            if (!state.Outer || state.Original == null) return;

            try
            {
                PostfixImpl(rect, ref result, state);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Arabic Support] Text input (postfix) failed: {ex}", 102783472);
            }
        }

        public static void Finalizer()
        {
            if (depth > 0) depth--;
        }

        // ------------------------------------------------------------------

        private static void PostfixImpl(Rect rect, ref string result, InputState state)
        {
            if (state.Handled)
            {
                // Our own edit: the event was consumed, so Unity did not
                // raise GUI.changed for it.
                if (state.Changed) GUI.changed = true;
                return;
            }

            string original = state.Original;

            if (result == null || result == original) return;
            if (!ArabicDetector.ContainsArabic(result) && !ArabicDetector.ContainsArabic(original)) return;

            TextEditor ed = FindEditor(rect, result);

            int anchor;
            if (state.HasAnchor) anchor = state.Anchor;
            else if (ed != null && result.Length == original.Length + 1) anchor = ed.cursorIndex - 1;
            else anchor = ArabicText.CommonPrefixLength(original, result);

            bool inserted = state.Inserted || result.Length > original.Length;

            if (!ArabicText.TryRenormalize(original, result, anchor, inserted, out string fixedText, out int caret))
            {
                if (!loggedRenormalizeFailure)
                {
                    loggedRenormalizeFailure = true;
                    Log.Warning("[Arabic Support] Text input: could not safely re-shape an edit " +
                                "(unusual mix of Arabic, digits and Latin). The text was left as typed.");
                }
                return;
            }

            result = fixedText;

            if (ed != null)
            {
                ed.text = fixedText;
                ed.cursorIndex = caret;
                ed.selectIndex = caret;
            }
        }

        private static void PrefixImpl(Event e, Rect rect, ref string text, int maxLength, Regex validator,
            ref InputState state)
        {
            string current = text ?? string.Empty;

            // Lets the postfix re-shape edits Unity makes itself (paste, cut...).
            state.Original = current;

            char ch = e.character;
            bool typedChar = ch >= ' ' && ch != '\u007F' && !e.command && !(e.control && !e.alt);
            bool deleteKey = !typedChar &&
                             (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete) &&
                             !e.control && !e.alt && !e.command;

            if (!typedChar && !deleteKey) return;

            TextEditor ed = FindEditor(rect, current);

            if (typedChar)
            {
                HandleTypedChar(e, ed, ref text, current, ch, maxLength, validator, ref state);
                return;
            }

            HandleDelete(e, ed, ref text, current, ref state);
        }

        private static void HandleTypedChar(Event e, TextEditor ed, ref string textArg, string text, char ch,
            int maxLength, Regex validator, ref InputState state)
        {
            if (ConvertArabicIndicDigits && IsArabicIndicDigit(ch))
            {
                ch = ToAsciiDigit(ch);
                e.character = ch;
            }

            bool arabicLetter = ArabicShaper.IsShapeableBase(ch);

            // Plain English typing into an English field: leave it to Unity.
            if (!arabicLetter && !ArabicDetector.ContainsArabic(text))
                return;

            if (arabicLetter && !loggedFirstKey)
            {
                loggedFirstKey = true;
                Log.Message($"[Arabic Support] Arabic key input detected (U+{(int)ch:X4}); text input handling is active.");
            }

            // English/digits typed so far were inserted by Unity itself, in
            // normal left-to-right order - that text IS the logical text. The
            // first Arabic letter typed at its end continues it ("Bob" then
            // Arabic gives "Bob <arabic>", not the reverse).
            bool latinPrefix = arabicLetter && ed != null &&
                               ed.cursorIndex == text.Length &&
                               !ArabicDetector.ContainsArabic(text);

            if (ed != null &&
                !ed.hasSelection &&
                text.IndexOf('\n') < 0 &&
                (latinPrefix || ArabicText.IsAtLogicalEnd(text, ed.cursorIndex)) &&
                CanRender(ed, ch) &&
                TryAppend(e, ed, ref textArg, text, ch, latinPrefix, maxLength, validator, ref state))
            {
                return;
            }

            // Anywhere else: give Unity a glyph it will accept; the postfix fixes the shaping.
            if (arabicLetter && ArabicShaper.TryGetIsolated(ch, out char isolated))
                e.character = isolated;

            state.Inserted = true;
        }

        /// <summary>Adds one character at the logical end of the text. Returns true if the key was consumed.</summary>
        private static bool TryAppend(Event e, TextEditor ed, ref string textArg, string text, char ch,
            bool textIsLogical, int maxLength, Regex validator, ref InputState state)
        {
            string logical;
            if (textIsLogical)
            {
                logical = text;
            }
            else
            {
                if (!ArabicText.IsOrderConsistent(text)) return false;
                logical = ArabicText.ToLogical(text);
            }

            if (!ArabicText.TryBuildVisual(logical + ch, out string visual, out int caret))
                return false;

            // Same limits the game's own field would enforce.
            if ((maxLength > 0 && visual.Length > maxLength) ||
                (validator != null && !validator.IsMatch(visual)))
            {
                e.Use();
                state.Handled = true;
                return true;
            }

            Apply(e, ed, ref textArg, visual, caret, ref state);
            return true;
        }

        private static void HandleDelete(Event e, TextEditor ed, ref string textArg, string text,
            ref InputState state)
        {
            if (ed == null || ed.hasSelection) return;
            if (text.IndexOf('\n') >= 0 || !ArabicDetector.ContainsArabic(text)) return;

            int caret = Math.Max(0, Math.Min(ed.cursorIndex, text.Length));
            bool backspace = e.keyCode == KeyCode.Backspace;

            if (ArabicText.IsAtLogicalEnd(text, caret))
            {
                if (!ArabicText.IsOrderConsistent(text)) return;

                // Nothing comes after the end of the text.
                if (!backspace)
                {
                    e.Use();
                    state.Handled = true;
                    return;
                }

                string logical = ArabicText.ToLogical(text);
                if (logical.Length == 0)
                {
                    e.Use();
                    state.Handled = true;
                    return;
                }

                if (ArabicText.TryBuildVisual(logical.Substring(0, logical.Length - 1), out string shorterVisual, out int newCaret))
                    Apply(e, ed, ref textArg, shorterVisual, newCaret, ref state);

                return;
            }

            // Elsewhere in right-to-left text, the character that comes just
            // before the caret in reading order is on its RIGHT (Backspace),
            // the one just after it on its LEFT (Delete).
            int removeAt = backspace ? caret : caret - 1;
            if (removeAt < 0 || removeAt >= text.Length || !Bidi.IsStrongRtl(text[removeAt])) return;

            string shorter = text.Remove(removeAt, 1);

            textArg = shorter;
            ed.text = shorter;
            ed.cursorIndex = removeAt;
            ed.selectIndex = removeAt;
            e.Use();

            // Not "Handled": the postfix still re-shapes the neighbours.
            state.HasAnchor = true;
            state.Anchor = removeAt;
        }

        private static void Apply(Event e, TextEditor ed, ref string textArg, string visual, int caret,
            ref InputState state)
        {
            textArg = visual;

            ed.text = visual;
            ed.cursorIndex = caret;
            ed.selectIndex = caret;

            e.Use();

            state.Handled = true;
            state.Changed = true;
        }

        // ------------------------------------------------------------------

        private static bool IsArabicIndicDigit(char c)
        {
            return (c >= '\u0660' && c <= '\u0669') || (c >= '\u06F0' && c <= '\u06F9');
        }

        private static char ToAsciiDigit(char c)
        {
            return (char)('0' + (c >= '\u06F0' ? c - '\u06F0' : c - '\u0660'));
        }

        /// <summary>Unity would silently drop a character the font lacks; do the same instead of inserting a box.</summary>
        private static bool CanRender(TextEditor ed, char ch)
        {
            if (ch < 128 || ArabicShaper.IsShapeableBase(ch)) return true;

            Font font = ed.style != null ? ed.style.font : null;
            return font == null || font.HasCharacter(ch);
        }

        /// <summary>
        /// The TextEditor Unity keeps for the field that has keyboard focus,
        /// if it belongs to THIS call (same text, same place on screen).
        ///
        /// QueryStateObject (not GetStateObject): it never creates or replaces
        /// a state object, so it cannot disturb a non-text control that
        /// happens to have keyboard focus.
        /// </summary>
        private static TextEditor FindEditor(Rect rect, string text)
        {
            int id = GUIUtility.keyboardControl;
            if (id == 0) return null;

            if (!(GUIUtility.QueryStateObject(typeof(TextEditor), id) is TextEditor ed)) return null;
            if (ed.text != text) return null;
            if (ed.controlID != 0 && ed.controlID != id) return null;

            // Same field = same place. The editor's rect can be slightly
            // inset from the one passed to Widgets, so compare by its center.
            Rect p = ed.position;
            if (p.width > 0f && p.height > 0f)
            {
                Vector2 c = p.center;
                if (c.x < rect.xMin - 1f || c.x > rect.xMax + 1f || c.y < rect.yMin - 1f || c.y > rect.yMax + 1f)
                    return null;
            }

            return ed;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), new[] { typeof(Rect), typeof(string) })]
    public static class Patch_TextField
    {
        public static void Prefix(Rect rect, ref string text, out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, 0, null, false, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), new[] { typeof(Rect), typeof(string), typeof(int), typeof(Regex) })]
    public static class Patch_TextFieldLimited
    {
        public static void Prefix(Rect rect, ref string text, int maxLength, Regex inputValidator,
            out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, maxLength, inputValidator, false, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextArea), new[] { typeof(Rect), typeof(string), typeof(bool) })]
    public static class Patch_TextArea
    {
        public static void Prefix(Rect rect, ref string text, bool readOnly, out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, 0, null, readOnly, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }
}
