using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArabicSupport.Fonts
{
    /// <summary>
    /// The few pieces of RimWorld 1.7's engine (Waylith) that the font code
    /// needs (RimWorld 1.7 build only).
    ///
    /// This mod is compiled against RimWorld 1.6's reference assemblies - there
    /// is no reference package for 1.7 - so everything that only exists in the
    /// new engine is reached by reflection, in this one file. If a name is not
    /// found (an older game, or a later 1.7 patch that renames something),
    /// <see cref="Problem"/> says what is missing and the mod keeps the game's
    /// own font instead of failing.
    ///
    /// What is used, and where it was seen working (see docs/RimWorld-1.7.md):
    ///   UnityStubs.Gui.GuiBackend.Current                    the GUI backend; null until the game draws its first frame
    ///   IGuiBackend.RegisterFont(name, bytes, size, mode)    -> font index, or below 0 when the bytes are not a font
    ///   UnityEngine.Font.GameFontIndex  (get/set)            which registered font a Font object draws with
    ///   UnityEngine.Font._material      (private field)      reset after the index changes
    /// RegisterFont remembers a font by its name and byte length only, so the
    /// callers put the font's hash and the pixel size into the name.
    /// </summary>
    internal static class WaylithFonts
    {
        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // The rendering mode that readable-fonts17 registers its fonts with (it names it "HintedSmooth").
        private const int SmoothHintedMode = 1;

        private static bool resolved;
        private static string problem;

        private static PropertyInfo backendProperty;
        private static FieldInfo backendField;
        private static Type backendType;          // declared type of GuiBackend.Current (the IGuiBackend interface)

        private static MethodInfo registerMethod;
        private static bool registerHasMode;

        private static PropertyInfo indexProperty;
        private static PropertyInfo sizeProperty;
        private static FieldInfo materialField;

        /// <summary>Why the engine's font system cannot be used, or null when it can.</summary>
        public static string Problem
        {
            get
            {
                Resolve();
                return problem;
            }
        }

        /// <summary>
        /// The engine's GUI backend, or null while the game has not started
        /// drawing yet (try again next frame) or when <see cref="Problem"/> is set.
        /// </summary>
        public static object GetBackend()
        {
            Resolve();
            if (problem != null)
                return null;

            try
            {
                return backendProperty != null
                    ? backendProperty.GetValue(null, null)
                    : backendField.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Registers font file bytes with the engine; returns the font index (below 0 = rejected).</summary>
        public static int Register(object backend, string name, byte[] data, float size)
        {
            MethodInfo method = FindRegisterMethod(backend);

            object[] args = registerHasMode
                ? new object[] { name, data, size, SmoothHintedMode }
                : new object[] { name, data, size };

            try
            {
                return Convert.ToInt32(method.Invoke(backend, args));
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException(ex.InnerException.Message, ex.InnerException);
            }
        }

        public static int GetIndex(Font font)
        {
            Resolve();
            if (indexProperty == null)
                throw new InvalidOperationException(problem ?? "Font.GameFontIndex is not available.");

            return Convert.ToInt32(indexProperty.GetValue(font, null));
        }

        public static void SetIndex(Font font, int index)
        {
            Resolve();
            if (indexProperty == null)
                throw new InvalidOperationException(problem ?? "Font.GameFontIndex is not available.");

            indexProperty.SetValue(font, index, null);
        }

        /// <summary>The font's pixel size as the engine reports it; 0 when unknown.</summary>
        public static int GetSize(Font font)
        {
            Resolve();
            if (sizeProperty == null)
                return 0;

            try
            {
                return Convert.ToInt32(sizeProperty.GetValue(font, null));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>The font's cached material (an engine object), or null.</summary>
        public static object GetMaterial(Font font)
        {
            Resolve();
            return materialField == null ? null : materialField.GetValue(font);
        }

        public static void SetMaterial(Font font, object material)
        {
            Resolve();
            if (materialField != null)
                materialField.SetValue(font, material);
        }

        // ------------------------------------------------------------------

        private static void Resolve()
        {
            if (resolved)
                return;
            resolved = true;

            try
            {
                Type font = typeof(Font);

                indexProperty = font.GetProperty("GameFontIndex", AnyMember);
                if (indexProperty == null || !indexProperty.CanRead || !indexProperty.CanWrite)
                {
                    indexProperty = null;
                    problem = "this game has no Font.GameFontIndex, so it is not RimWorld 1.7 or newer.";
                    return;
                }

                sizeProperty = font.GetProperty("fontSize", AnyMember);
                materialField = font.GetField("_material", AnyMember);

                // The engine's UnityEngine.* types and the GUI backend live in the same assembly.
                Type guiBackend = font.Assembly.GetType("UnityStubs.Gui.GuiBackend", false)
                                  ?? FindType("UnityStubs.Gui.GuiBackend");
                if (guiBackend == null)
                {
                    problem = "the engine's GUI backend (UnityStubs.Gui.GuiBackend) was not found.";
                    return;
                }

                backendProperty = guiBackend.GetProperty("Current", AnyMember);
                if (backendProperty != null)
                {
                    backendType = backendProperty.PropertyType;
                }
                else
                {
                    backendField = guiBackend.GetField("Current", AnyMember);
                    if (backendField != null)
                        backendType = backendField.FieldType;
                }

                if (backendType == null)
                    problem = "UnityStubs.Gui.GuiBackend.Current was not found.";
            }
            catch (Exception ex)
            {
                problem = "the engine's font API could not be inspected (" + ex.Message + ").";
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Some assemblies (dynamic ones, for example) cannot be searched. Skip them.
                }
            }

            return null;
        }

        /// <summary>RegisterFont(string, byte[], float, int) of the backend - or the 3-argument form.</summary>
        private static MethodInfo FindRegisterMethod(object backend)
        {
            if (registerMethod != null)
                return registerMethod;

            if (backend == null)
                throw new ArgumentNullException("backend");

            var types = new List<Type>();
            if (backendType != null)
                types.Add(backendType);            // the interface method is invoked on the object, so it is tried first
            types.Add(backend.GetType());

            MethodInfo withoutMode = null;

            foreach (Type type in types)
            {
                foreach (MethodInfo method in type.GetMethods(AnyMember))
                {
                    if (method.Name != "RegisterFont" || method.ReturnType != typeof(int))
                        continue;

                    ParameterInfo[] p = method.GetParameters();
                    if (p.Length < 3 || p.Length > 4 ||
                        p[0].ParameterType != typeof(string) ||
                        p[1].ParameterType != typeof(byte[]) ||
                        p[2].ParameterType != typeof(float))
                        continue;

                    if (p.Length == 4)
                    {
                        if (p[3].ParameterType != typeof(int))
                            continue;

                        registerMethod = method;
                        registerHasMode = true;
                        return method;
                    }

                    if (withoutMode == null)
                        withoutMode = method;
                }
            }

            if (withoutMode != null)
            {
                registerMethod = withoutMode;
                registerHasMode = false;
                return withoutMode;
            }

            throw new MissingMethodException("The engine's GUI backend has no RegisterFont(string, byte[], float, int).");
        }
    }
}
