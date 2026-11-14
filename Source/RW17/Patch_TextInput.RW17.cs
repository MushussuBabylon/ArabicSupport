using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// RimWorld 1.7 build only: Arabic typing in text fields (pawn and animal
    /// names, search boxes, rename dialogs, notes...). It does the same job as
    /// Patches/Patch_TextInput.cs does for 1.5 and 1.6 (read that file for the
    /// full story: why nothing appears without it, and how a typed letter is
    /// added to the logical text and handed back as the same visual, shaped
    /// text the translation uses).
    ///
    /// This copy exists because the Waylith engine's text field is not
    /// documented, so every access to the game's text editor object
    /// (UnityEngine.TextEditor) is optional. For each text field that is
    /// drawn while a key goes down, one of two things happens:
    ///
    ///  1. The editor object of THIS field is found (the engine keeps one like
    ///     Unity does): exactly what 1.6 does. The letter is added at the logical
    ///     end, the caret is moved to where the next letter belongs, selections
    ///     and the caret position are respected.
    ///
    ///  2. No editor object for this field: the mod cannot tell whether this field
    ///     has the keyboard focus (several fields can be drawn during one key
    ///     press), so it never consumes the key and never touches a field the game
    ///     did not edit. It only swaps the typed letter for its isolated form (a
    ///     glyph the font has) so the game accepts it. After the game has drawn the
    ///     field, the postfix looks at what the game really did: if exactly that
    ///     one character was inserted (or, for Backspace, exactly one removed), the
    ///     field's text is rebuilt as if the letter had been typed at the END of the
    ///     text (the usual way of typing). Anything else (paste, a selection that
    ///     was replaced, a letter typed in the middle of a line) goes through the
    ///     same re-shaping the 1.6 version uses for such edits. In this mode the
    ///     caret is not moved (it stays where the game left it).
    ///
    /// Every call into the game's input API is wrapped, so a member that does
    /// not exist in this engine switches only that part off. After three
    /// failures the whole fix switches itself off and says so in the log; the
    /// game's own text fields are never left broken. StatusLine() summarises
    /// what happened (a debugging aid: the settings window does not show it).
    ///
    /// Same parameter-name guard as the fonts: if Widgets.TextField / TextArea
    /// have other parameter names in this game version, the patch is skipped
    /// with a log warning instead of throwing.
    /// </summary>
    public static class TextInputCore
    {
        /// <summary>
        /// Arabic-Indic digits typed on some Arabic layouts (U+0660-U+0669,
        /// U+06F0-U+06F9) are converted to 0-9. The game's numbers are 0-9,
        /// and the font may not have the Arabic-Indic digit glyphs.
        /// </summary>
        private const bool ConvertArabicIndicDigits = true;

        private const int MaxFailures = 3;

        public struct InputState
        {
            public bool Outer;          // this call is the outermost patched call
            public string Original;     // text as the caller passed it in (set on key events only)
            public bool Handled;        // we produced the final text ourselves
            public bool Changed;        // ... and it differs from Original
            public bool HasAnchor;      // visual index of the edit is known
            public int Anchor;
            public bool Inserted;       // a character was inserted by the game
            public int MaxLength;       // limit of the field (0 = none)

            // Only when this call found no editor object (mode 2 in the class comment):
            public char Typed;          // the character as the key event carried it
            public char Pending;        // the letter or digit to add at the logical end (0 = none)
            public char Substituted;    // what the game was given instead (isolated form), or the same character
            public bool PendingBackspace;
        }

        private static int depth;
        private static bool loggedFirstKey;
        private static bool loggedRenormalizeFailure;
        private static bool disabled;
        private static bool editorBroken;
        private static bool editorByGetState;
        private static int failures;
        private static int editorFound;
        private static int editorMissing;
        private static string lastError = "";
        private static readonly HashSet<string> warned = new HashSet<string>();

        // Counters behind StatusLine().
        public static int KeysSeen { get; private set; }
        public static int ArabicKeysSeen { get; private set; }
        public static int KeysHandled { get; private set; }
        public static int EditsReshaped { get; private set; }

        /// <summary>One line for the settings window: did keys reach the mod, did the game take them, and is the engine's text editor object there.</summary>
        public static string StatusLine()
        {
            if (disabled)
                return "Typing check: the Arabic typing fix switched itself off after errors (see the log). " + lastError;

            if (KeysSeen == 0)
                return "Typing check: no key has reached the mod yet. Click the box above and type something.";

            string editor;
            if (editorFound > 0) editor = "found";
            else if (editorBroken || editorMissing > 0) editor = "not found (letters are added at the end of the text)";
            else editor = "not checked yet";

            string line = "Typing check: " + KeysSeen + " keys seen (" + ArabicKeysSeen + " Arabic), " +
                          KeysHandled + " completed by the mod, " + EditsReshaped + " other edits re-shaped. " +
                          "Text editor object: " + editor + ".";

            if (ArabicKeysSeen > 0 && KeysHandled == 0 && EditsReshaped == 0)
                line += " Arabic keys arrive, but the text box did not take them.";

            return line;
        }

        /// <summary>Used by the patch classes: false (with one log warning) when the target is missing or its parameters have other names.</summary>
        public static bool CanPatch(string label, MethodBase method, string[] parameterNames)
        {
            string problem = null;
            if (method == null)
            {
                problem = "was not found in this game version";
            }
            else
            {
                ParameterInfo[] parameters = method.GetParameters();
                for (int i = 0; i < parameterNames.Length; i++)
                {
                    if (i >= parameters.Length || parameters[i].Name != parameterNames[i])
                    {
                        problem = "has different parameter names in this game version";
                        break;
                    }
                }
            }

            if (problem == null) return true;

            if (warned.Add(label))
                Log.Warning("[Arabic Support] " + label + " " + problem + ": Arabic typing is not available for it.");
            return false;
        }

        // ------------------------------------------------------------------
        // Entry points used by the three patch classes

        public static void Prefix(Rect rect, ref string text, int maxLength, Regex validator, bool readOnly,
            out InputState state)
        {
            state = default(InputState);
            state.Outer = depth == 0;
            depth++;

            if (!state.Outer || readOnly || disabled) return;

            // Everything that touches the game's input API is in PrefixGate and below, never
            // in this method: if this engine lacks a member, the exception is thrown (and
            // caught) here, and not into the game's own text field.
            try
            {
                PrefixGate(rect, ref text, maxLength, validator, ref state);
            }
            catch (Exception ex)
            {
                Fail("prefix", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PrefixGate(Rect rect, ref string text, int maxLength, Regex validator,
            ref InputState state)
        {
            // Cheapest rejection first: this runs for every text field on
            // every GUI event (layout, repaint, mouse...), not just typing.
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            ArabicSupportSettings settings = ArabicSupportMod.Settings;
            if (settings != null && !settings.textInputFix) return;

            state.MaxLength = maxLength;
            PrefixImpl(e, rect, ref text, maxLength, validator, ref state);
        }

        public static void Postfix(Rect rect, ref string result, InputState state)
        {
            if (!state.Outer || state.Original == null || disabled) return;

            try
            {
                PostfixImpl(rect, ref result, state);
            }
            catch (Exception ex)
            {
                Fail("postfix", ex);
            }
        }

        public static void Finalizer()
        {
            if (depth > 0) depth--;
        }

        private static void Fail(string where, Exception ex)
        {
            failures++;
            lastError = where + ": " + ex.GetType().Name + ": " + ex.Message;
            Log.ErrorOnce("[Arabic Support] Text input (" + where + ") failed: " + ex, 102783470 + (where == "prefix" ? 0 : 2));

            if (failures >= MaxFailures && !disabled)
            {
                disabled = true;
                Log.Warning("[Arabic Support] Text input handling failed " + failures + " times and is switched off for this session. " +
                            "The game's own text fields keep working. Please report the first error above.");
            }
        }

        // ------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PostfixImpl(Rect rect, ref string result, InputState state)
        {
            if (state.Handled)
            {
                // Our own edit: the event was consumed, so the game did not
                // raise GUI.changed for it.
                if (state.Changed) MarkChanged();
                return;
            }

            string original = state.Original;

            // Only a field the game really edited gets here with a different text.
            if (result == null || result == original) return;
            if (!ArabicDetector.ContainsArabic(result) && !ArabicDetector.ContainsArabic(original)) return;

            object ed = FindEditor(rect, result);
            int editorCaret = 0;
            bool editorSelection = false;
            bool hasEditor = ed != null && ReadEditor(ed, out editorCaret, out editorSelection);

            // Mode 2: complete the one-character edit the game made (see the class comment).
            if (state.Pending != '\0' || state.PendingBackspace)
            {
                string completed;
                int completedCaret;
                if (TryComplete(original, result, state, out completed, out completedCaret))
                {
                    if (hasEditor) WriteEditor(ed, completed, completedCaret);
                    result = completed;
                    KeysHandled++;
                    return;
                }
            }

            int anchor;
            if (state.HasAnchor) anchor = state.Anchor;
            else if (hasEditor && result.Length == original.Length + 1) anchor = editorCaret - 1;
            else anchor = ArabicText.CommonPrefixLength(original, result);

            bool inserted = state.Inserted || result.Length > original.Length;

            string fixedText;
            int caret;
            if (!ArabicText.TryRenormalize(original, result, anchor, inserted, out fixedText, out caret))
            {
                if (!loggedRenormalizeFailure)
                {
                    loggedRenormalizeFailure = true;
                    Log.Warning("[Arabic Support] Text input: could not safely re-shape an edit " +
                                "(unusual mix of Arabic, digits and Latin). The text was left as typed.");
                }
                return;
            }

            EditsReshaped++;
            result = fixedText;
            if (hasEditor) WriteEditor(ed, fixedText, caret);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PrefixImpl(Event e, Rect rect, ref string text, int maxLength, Regex validator,
            ref InputState state)
        {
            string current = text ?? string.Empty;

            // Lets the postfix re-shape edits the game makes itself (paste, cut...).
            state.Original = current;

            bool command, control, alt;
            ReadModifiers(e, out command, out control, out alt);

            char ch = e.character;
            bool typedChar = ch >= ' ' && ch != (char)127 && !command && !(control && !alt);
            bool deleteKey = !typedChar &&
                             (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete) &&
                             !control && !alt && !command;

            if (!typedChar && !deleteKey) return;

            KeysSeen++;
            if (typedChar && ArabicShaper.IsShapeableBase(ch)) ArabicKeysSeen++;

            object ed = FindEditor(rect, current);
            int caret = 0;
            bool selection = false;
            if (ed != null && !ReadEditor(ed, out caret, out selection)) ed = null;

            if (ed == null)
            {
                HandleWithoutEditor(e, current, ch, typedChar, ref state);
                return;
            }

            if (typedChar)
            {
                HandleTypedChar(e, ed, caret, selection, ref text, current, ch, maxLength, validator, ref state);
                return;
            }

            HandleDelete(e, ed, caret, selection, ref text, current, ref state);
        }

        // ------------------------------------------------------------------
        // Mode 2: no editor object for this field

        private static void HandleWithoutEditor(Event e, string text, char ch, bool typedChar, ref InputState state)
        {
            bool textHasArabic = ArabicDetector.ContainsArabic(text);

            if (!typedChar)
            {
                // Backspace: remember it; the postfix redoes it in reading order if the game removed one character.
                if (e.keyCode == KeyCode.Backspace && textHasArabic && text.IndexOf('\n') < 0)
                    state.PendingBackspace = true;
                return;
            }

            state.Typed = ch;

            if (ConvertArabicIndicDigits && IsArabicIndicDigit(ch))
            {
                ch = ToAsciiDigit(ch);
                SetCharacter(e, ch);
            }

            bool arabicLetter = ArabicShaper.IsShapeableBase(ch);

            // Plain English typing into an English field: leave it to the game.
            if (!arabicLetter && !textHasArabic)
                return;

            if (arabicLetter) LogFirstArabicKey(ch);

            state.Pending = ch;
            state.Substituted = ch;

            char isolated;
            if (arabicLetter && ArabicShaper.TryGetIsolated(ch, out isolated))
            {
                SetCharacter(e, isolated);
                state.Substituted = isolated;
            }

            state.Inserted = true;
        }

        /// <summary>
        /// Mode 2, after the game has drawn the field: if it inserted exactly the one character we
        /// gave it (or removed exactly one for Backspace), rebuild the text as if the letter had
        /// been typed at the logical end. False = not that simple an edit; the caller re-shapes it.
        /// </summary>
        private static bool TryComplete(string original, string result, InputState state,
            out string completed, out int caret)
        {
            completed = null;
            caret = 0;

            if (original.IndexOf('\n') >= 0 || result.IndexOf('\n') >= 0) return false;

            bool originalHasArabic = ArabicDetector.ContainsArabic(original);

            if (state.Pending != '\0')
            {
                if (!IsSingleInsertion(original, result, state)) return false;

                string logical;
                if (!originalHasArabic)
                {
                    logical = original;     // typed left to right so far: that IS the logical text
                }
                else
                {
                    if (!ArabicText.IsOrderConsistent(original)) return false;
                    logical = ArabicText.ToLogical(original);
                }

                string visual;
                int visualCaret;
                if (!ArabicText.TryBuildVisual(logical + state.Pending, out visual, out visualCaret)) return false;
                if (state.MaxLength > 0 && visual.Length > state.MaxLength) return false;

                completed = visual;
                caret = visualCaret;
                return true;
            }

            // Backspace
            if (!originalHasArabic || !IsSingleDeletion(original, result)) return false;
            if (!ArabicText.IsOrderConsistent(original)) return false;

            string all = ArabicText.ToLogical(original);
            if (all.Length == 0) return false;

            string shorter;
            int shorterCaret;
            if (!ArabicText.TryBuildVisual(all.Substring(0, all.Length - 1), out shorter, out shorterCaret)) return false;

            completed = shorter;
            caret = shorterCaret;
            return true;
        }

        /// <summary>result is original with ONE character added somewhere, and that character is one we expected.</summary>
        private static bool IsSingleInsertion(string original, string result, InputState state)
        {
            if (result.Length != original.Length + 1) return false;

            for (int k = 0; k <= original.Length; k++)
            {
                // result[0..k) equals original[0..k) here.
                char c = result[k];
                if ((c == state.Substituted || c == state.Pending || c == state.Typed) &&
                    string.CompareOrdinal(result, k + 1, original, k, original.Length - k) == 0)
                    return true;

                if (k < original.Length && result[k] != original[k]) break;
            }

            return false;
        }

        /// <summary>result is original with ONE character removed.</summary>
        private static bool IsSingleDeletion(string original, string result)
        {
            if (result.Length != original.Length - 1) return false;

            for (int k = 0; k < original.Length; k++)
            {
                // result[0..k) equals original[0..k) here.
                if (string.CompareOrdinal(original, k + 1, result, k, result.Length - k) == 0)
                    return true;

                if (k < result.Length && original[k] != result[k]) break;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Mode 1: the editor object of this field is known (same behaviour as 1.6)

        private static void HandleTypedChar(Event e, object ed, int caret, bool selection, ref string textArg,
            string text, char ch, int maxLength, Regex validator, ref InputState state)
        {
            if (ConvertArabicIndicDigits && IsArabicIndicDigit(ch))
            {
                ch = ToAsciiDigit(ch);
                SetCharacter(e, ch);
            }

            bool arabicLetter = ArabicShaper.IsShapeableBase(ch);

            // Plain English typing into an English field: leave it to the game.
            if (!arabicLetter && !ArabicDetector.ContainsArabic(text))
                return;

            if (arabicLetter) LogFirstArabicKey(ch);

            // English/digits typed so far were inserted by the game itself, in
            // normal left-to-right order - that text IS the logical text. The
            // first Arabic letter typed at its end continues it ("Bob" then
            // Arabic gives "Bob <arabic>", not the reverse).
            bool latinPrefix = arabicLetter &&
                               caret == text.Length &&
                               !ArabicDetector.ContainsArabic(text);

            if (!selection &&
                text.IndexOf('\n') < 0 &&
                (latinPrefix || ArabicText.IsAtLogicalEnd(text, caret)) &&
                CanRender(ed, ch) &&
                TryAppend(e, ed, ref textArg, text, ch, latinPrefix, maxLength, validator, ref state))
            {
                return;
            }

            // Anywhere else: give the game a glyph it will accept; the postfix fixes the shaping.
            char isolated;
            if (arabicLetter && ArabicShaper.TryGetIsolated(ch, out isolated))
                SetCharacter(e, isolated);

            state.Inserted = true;
        }

        /// <summary>Adds one character at the logical end of the text. Returns true if the key was consumed.</summary>
        private static bool TryAppend(Event e, object ed, ref string textArg, string text, char ch,
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

            string visual;
            int caret;
            if (!ArabicText.TryBuildVisual(logical + ch, out visual, out caret))
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

        private static void HandleDelete(Event e, object ed, int caret, bool selection, ref string textArg,
            string text, ref InputState state)
        {
            if (selection) return;
            if (text.IndexOf('\n') >= 0 || !ArabicDetector.ContainsArabic(text)) return;

            caret = Math.Max(0, Math.Min(caret, text.Length));
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

                string shorterVisual;
                int newCaret;
                if (ArabicText.TryBuildVisual(logical.Substring(0, logical.Length - 1), out shorterVisual, out newCaret))
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
            WriteEditor(ed, shorter, removeAt);
            e.Use();

            // Not "Handled": the postfix still re-shapes the neighbours.
            state.HasAnchor = true;
            state.Anchor = removeAt;
        }

        private static void Apply(Event e, object ed, ref string textArg, string visual, int caret,
            ref InputState state)
        {
            textArg = visual;
            WriteEditor(ed, visual, caret);

            e.Use();

            state.Handled = true;
            state.Changed = true;
            KeysHandled++;
        }

        // ------------------------------------------------------------------

        private static void LogFirstArabicKey(char ch)
        {
            if (loggedFirstKey) return;

            loggedFirstKey = true;
            Log.Message("[Arabic Support] Arabic key input detected (U+" + ((int)ch).ToString("X4") + "); text input handling is active.");
        }

        private static bool IsArabicIndicDigit(char c)
        {
            return (c >= (char)0x0660 && c <= (char)0x0669) || (c >= (char)0x06F0 && c <= (char)0x06F9);
        }

        private static char ToAsciiDigit(char c)
        {
            return (char)('0' + (c >= (char)0x06F0 ? c - 0x06F0 : c - 0x0660));
        }

        /// <summary>The game would silently drop a character the font lacks; do the same instead of inserting a box.</summary>
        private static bool CanRender(object ed, char ch)
        {
            if (ed == null || ch < 128 || ArabicShaper.IsShapeableBase(ch)) return true;

            try
            {
                return EditorAccess.CanRender(ed, ch);
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Changing the character of the key event is only a convenience; if this engine does not allow it, the key is used as it is.</summary>
        private static void SetCharacter(Event e, char c)
        {
            try
            {
                EventAccess.SetCharacter(e, c);
            }
            catch (Exception)
            {
                // read-only or missing in this engine: nothing to do
            }
        }

        /// <summary>Ctrl / Alt / Command state of the key event. If this engine cannot tell, no modifier is assumed.</summary>
        private static void ReadModifiers(Event e, out bool command, out bool control, out bool alt)
        {
            command = false;
            control = false;
            alt = false;

            try
            {
                EventAccess.ReadModifiers(e, out command, out control, out alt);
            }
            catch (Exception)
            {
                command = false;
                control = false;
                alt = false;
            }
        }

        /// <summary>Tells the game's own code that a field changed (our edit consumed the key event, so the game did not).</summary>
        private static void MarkChanged()
        {
            try
            {
                EventAccess.MarkChanged();
            }
            catch (Exception)
            {
                // not available in this engine: the text is still returned and stored
            }
        }

        /// <summary>
        /// The text editor object the engine keeps for the field that has keyboard
        /// focus, if it belongs to THIS call (same text, same place on screen).
        /// Null when there is none, or when this engine has no such object.
        /// </summary>
        private static object FindEditor(Rect rect, string text)
        {
            if (editorBroken) return null;

            try
            {
                object ed = EditorAccess.Find(rect, text, editorByGetState);
                if (ed != null) editorFound++; else editorMissing++;
                return ed;
            }
            catch (MissingMemberException)
            {
                // QueryStateObject is the careful lookup (it never creates anything). If the engine
                // does not have it, GetStateObject is the one Unity's own text field uses.
                if (!editorByGetState)
                {
                    editorByGetState = true;
                    return FindEditor(rect, text);
                }

                editorBroken = true;
                return null;
            }
            catch (Exception)
            {
                editorBroken = true;
                return null;
            }
        }

        private static bool ReadEditor(object ed, out int caret, out bool selection)
        {
            caret = 0;
            selection = false;
            try
            {
                EditorAccess.Read(ed, out caret, out selection);
                return true;
            }
            catch (Exception)
            {
                editorBroken = true;
                return false;
            }
        }

        private static void WriteEditor(object ed, string text, int caret)
        {
            if (ed == null) return;

            try
            {
                EditorAccess.Write(ed, text, caret);
            }
            catch (Exception)
            {
                editorBroken = true;
            }
        }
    }

    /// <summary>
    /// The few members of the key event that are only conveniences. Each is in its own
    /// method that is never inlined, so a member this engine does not have makes that one
    /// call throw (TextInputCore catches it) instead of making the whole typing code fail.
    /// </summary>
    internal static class EventAccess
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetCharacter(Event e, char c)
        {
            e.character = c;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ReadModifiers(Event e, out bool command, out bool control, out bool alt)
        {
            command = e.command;
            control = e.control;
            alt = e.alt;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void MarkChanged()
        {
            GUI.changed = true;
        }
    }

    /// <summary>
    /// Every use of UnityEngine.TextEditor, GUIUtility's state objects and the font of the
    /// field lives here, in small methods that are never inlined, and the rest of the mod
    /// only sees "object". If this engine lacks one of these members, the exception comes
    /// out of the call into one of these methods (where TextInputCore catches it) and not
    /// out of the compilation of the caller.
    /// </summary>
    internal static class EditorAccess
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object Find(Rect rect, string text, bool create)
        {
            int id = GUIUtility.keyboardControl;
            if (id == 0) return null;

            TextEditor ed = (create ? GetState(id) : QueryState(id)) as TextEditor;
            if (ed == null) return null;
            if (ed.text != text) return null;

            bool samePlace;
            try
            {
                samePlace = PlacementMatches(ed, rect, id);
            }
            catch (Exception)
            {
                samePlace = true;      // cannot tell: the same text and focus have to be enough
            }

            return samePlace ? ed : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object QueryState(int id)
        {
            return GUIUtility.QueryStateObject(typeof(TextEditor), id);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object GetState(int id)
        {
            return GUIUtility.GetStateObject(typeof(TextEditor), id);
        }

        /// <summary>Same field = same place. The editor's rect can be slightly inset from the one passed to Widgets, so compare by its center.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool PlacementMatches(TextEditor ed, Rect rect, int id)
        {
            if (ed.controlID != 0 && ed.controlID != id) return false;

            Rect p = ed.position;
            if (p.width > 0f && p.height > 0f)
            {
                Vector2 c = p.center;
                if (c.x < rect.xMin - 1f || c.x > rect.xMax + 1f || c.y < rect.yMin - 1f || c.y > rect.yMax + 1f)
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Read(object editor, out int caret, out bool selection)
        {
            TextEditor ed = (TextEditor)editor;
            caret = ed.cursorIndex;
            selection = ed.hasSelection;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Write(object editor, string text, int caret)
        {
            TextEditor ed = (TextEditor)editor;
            ed.text = text;
            ed.cursorIndex = caret;
            ed.selectIndex = caret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool CanRender(object editor, char ch)
        {
            TextEditor ed = (TextEditor)editor;
            Font font = ed.style != null ? ed.style.font : null;
            return font == null || font.HasCharacter(ch);
        }
    }

    [HarmonyPatch]
    public static class Patch_TextField
    {
        private static readonly Type[] Signature = { typeof(Rect), typeof(string) };

        public static MethodBase TargetMethod() => AccessTools.Method(typeof(Widgets), "TextField", Signature);

        public static bool Prepare() =>
            TextInputCore.CanPatch("Widgets.TextField(Rect, string)", TargetMethod(), new[] { "rect", "text" });

        public static void Prefix(Rect rect, ref string text, out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, 0, null, false, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }

    [HarmonyPatch]
    public static class Patch_TextFieldLimited
    {
        private static readonly Type[] Signature = { typeof(Rect), typeof(string), typeof(int), typeof(Regex) };

        public static MethodBase TargetMethod() => AccessTools.Method(typeof(Widgets), "TextField", Signature);

        public static bool Prepare() =>
            TextInputCore.CanPatch("Widgets.TextField(Rect, string, int, Regex)", TargetMethod(),
                new[] { "rect", "text", "maxLength", "inputValidator" });

        public static void Prefix(Rect rect, ref string text, int maxLength, Regex inputValidator,
            out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, maxLength, inputValidator, false, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }

    [HarmonyPatch]
    public static class Patch_TextArea
    {
        private static readonly Type[] Signature = { typeof(Rect), typeof(string), typeof(bool) };

        public static MethodBase TargetMethod() => AccessTools.Method(typeof(Widgets), "TextArea", Signature);

        public static bool Prepare() =>
            TextInputCore.CanPatch("Widgets.TextArea(Rect, string, bool)", TargetMethod(), new[] { "rect", "text", "readOnly" });

        public static void Prefix(Rect rect, ref string text, bool readOnly, out TextInputCore.InputState __state)
            => TextInputCore.Prefix(rect, ref text, 0, null, readOnly, out __state);

        public static void Postfix(Rect rect, ref string __result, TextInputCore.InputState __state)
            => TextInputCore.Postfix(rect, ref __result, __state);

        public static void Finalizer() => TextInputCore.Finalizer();
    }
}
