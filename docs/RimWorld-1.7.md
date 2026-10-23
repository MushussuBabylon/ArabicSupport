# RimWorld 1.7 support - maintainer notes

RimWorld 1.7 does not run on Unity any more. It runs on Ludeon's own **Waylith** engine, which
imitates the parts of Unity that mods use (`UnityStubs.dll` provides the `UnityEngine.*` types).
Ludeon says most 1.6 mods keep working without changes. This mod's text wrapping and harakat do, but its
font loading and its typing fix are built on Unity and cannot:

| What the mod does | On 1.5 / 1.6 | On 1.7 |
|---|---|---|
| Wraps Arabic text by pixel width (Harmony patches on `Widgets.Label`, `Text.CalcHeight`) | works | same code, unchanged |
| Puts harakat on their letter (pre-placed glyph copies, `HarakatMapper`) | works | same code, unchanged; needs the engine to draw the private-use glyphs (see "Not checked" below) |
| Custom fonts | Unity `AssetBundle` -> `Font` -> `GUIStyle.font` | **new font loader** that gives the font file to the engine (`Source/RW17`) |
| Arabic typing in text boxes and Arabic search | patches Unity's text editor | **not in the 1.7 build** - left to the game |

So the repository now makes two DLLs from one source tree.

## Folder layout

```
ArabicSupport/
  About/About.xml            supportedVersions 1.5, 1.6, 1.7 and a separate 1.7 description
  LoadFolders.xml            which folders RimWorld loads for which game version
  1.6/Assemblies/            ArabicSupport.dll for RimWorld 1.5 and 1.6 (Unity)
  1.7/Assemblies/            ArabicSupport.dll for RimWorld 1.7 (Waylith)
  Fonts/                     Cairo, shared by every version (.fontbundle, plus .ttf for 1.7)
  Source/                    one C# project, two build configurations
    RW17/                    files that are only part of the 1.7 build
  Tools/                     build_fonts.py (fonts), fontbundle_to_ttf.py (bundle -> .ttf)
  ExtraFonts/ FontSources/   as before
  .github/workflows/build.yml
```

`LoadFolders.xml` replaces the game's default folder rules: only the folders it lists for the running game
version are loaded, and `/` is the mod's own root. It is needed here because RimWorld 1.5 has to use the `1.6`
folder (the same DLL, as before), and the default rules would not find it: the wiki's example shows an older
version folder being used for a newer game, not the reverse. When a new game version comes out, add a `<v1.x>`
entry for it; the wiki does not say what happens for a version that is not listed
([Mod folder structure](https://rimworldwiki.com/wiki/Modding_Tutorials/Mod_folder_structure)).

## Building

`Source/ArabicSupport.csproj` has two configurations:

| Command | Result | Contents |
|---|---|---|
| `dotnet build Source/ArabicSupport.csproj -c Release` | `1.6/Assemblies/ArabicSupport.dll` | the original code, unchanged |
| `dotnet build Source/ArabicSupport.csproj -c Release17` | `1.7/Assemblies/ArabicSupport.dll` | defines `RW17`, leaves out the typing engine and the Unity font loader, adds `Source/RW17` |

The **Build Mod** workflow (Actions tab -> Run workflow) runs both and uploads:

* `ArabicSupport-mod` - the finished mod folder (About, Fonts, LoadFolders.xml, LICENSE, font licenses and both DLLs),
* `ArabicSupport-dll` - the two DLLs in their `1.6/Assemblies` and `1.7/Assemblies` folders,
* `ArabicSupport-fonts` / `ArabicSupport-extra-fonts` - only when the font options are ticked.

After a run, copy the two DLLs from the artifact into the repository (`1.6/Assemblies`,
`1.7/Assemblies`) and commit them, like before.

There is no reference package for RimWorld 1.7 (see
[krafs/RimRef#106](https://github.com/krafs/RimRef/issues/106)), so both configurations compile
against the 1.6 reference assemblies (`Krafs.Rimworld.Ref 1.6.*`, `Lib.Harmony 2.3.*`, `net472`).
That is the same situation as every 1.6 mod that runs unchanged on 1.7: the engine maps
`UnityEngine*` assemblies to `UnityStubs`. Everything that exists only in 1.7 is reached through
reflection, in one file (`Source/RW17/WaylithFonts.cs`). If you want a native 1.7 build later,
make `Release17` target `net10.0` and reference `Assembly-CSharp.dll`, `UnityStubs.dll` and
`Waylith.dll` from a RimWorld 1.7 install instead.

## How fonts work on 1.7

Files in `Fonts/` (`Source/RW17/FontManager.RW17.cs`):

* `Name.ttf` / `Name.otf` - used as they are,
* `Name.fontbundle` - the Unity bundle used by 1.5 / 1.6. `UnityFsFont.cs` opens it (UnityFS container,
  LZ4) and cuts the TrueType font out, so the same bundles, including everything in `ExtraFonts`,
  work on every version,
* if a `.ttf` and a `.fontbundle` have the same name the `.ttf` is used. Cairo ships both, so
  the default font does not depend on the bundle reader. Rebuild with
  `python Tools/build_fonts.py --ttf` so both files stay identical (the workflow does this).
  Delete the six `Cairo-*.ttf` files if you prefer a smaller mod; 1.7 then reads the bundles.

Applying a font (same sequence as the community mod
[Readable Fonts 1.7](https://github.com/basalt991/readable-fonts17), the only example found; the names,
parameter types and property kinds used here match its source and its `Validation/EngineProbe.cs`):

1. wait until `UnityStubs.Gui.GuiBackend.Current` exists (the first drawn frame),
2. read the font bytes, check the character map (`SfntFont.cs`: Latin or Arabic present),
3. `RegisterFont(name, bytes, pixelSize, 1)` once for each of Tiny / Small / Medium. The engine remembers a
   font by name and byte length only, so the name contains the file's SHA-256 and the size,
4. point the three `Verse.Text.fonts` objects at the returned indexes (`Font.GameFontIndex`) and reset their
   `_material`. Every `GUIStyle` that uses them - labels, buttons, text boxes - follows,
5. recompute `Text.lineHeights` / `spaceBetweenLines` from `GUIStyle.CalcHeight` (with the same sanity check
   the 1.6 code has) and clear this mod's caches and `Widgets.ClearLabelCache`.

The font swap is triggered from `Root.OnGUI` (`Patch_RootOnGUI`) and from `Text.StartOfOnGUI`
(`Patch_TextStartOfOnGUI`, 1.7 only). Either hook is enough; each is skipped without an error when its
target does not exist. If another mod changes the fonts afterwards, the code adopts its choice as the new
"original" and applies again (at most three times, then it gives up, as in 1.6).

Settings that changed on 1.7: "Also use it in text fields" is hidden because text boxes draw with the same three
font objects as labels, so there is no separate choice. "Prepare letters in advance" is hidden because it relies on a
Unity-only call (`Font.RequestCharactersInTexture`) that the 1.7 build does not use. The vertical-shift sliders are
hidden if `GUIStyle.contentOffset` does not exist. Size sliders change the registered pixel size.

## Arabic typing and shaping on 1.7

* **Joining and order of the translation.** The translation files are already in visual order with
  presentation forms (U+FE70-U+FEFC), and the mod's fonts have a glyph for each of them. A renderer only
  has to draw glyphs left to right. A glyph-by-glyph FreeType rendering of the preview strings (no shaping,
  no bidi) reads correctly, so no shaping by the engine is needed for this. It would only break if the engine
  applied the Unicode bidi algorithm on top, which would reverse the text a second time.
* **Typing.** Nothing from Ludeon (the 1.7 announcement, the wiki's 1.7 mod-update page) says that Waylith
  joins or reorders Arabic. What was found: the Rimworld-KRInput README says version 1.7.4980 rev60 added
  native Windows IME support, which is about entering text, not about joining letters. Typed Arabic arrives
  as logical-order base letters, so without the typing engine it will most likely show as separate letters,
  left to right. The fonts made by
  `build_fonts.py` also have their GSUB/GPOS tables removed, so even an engine with its own shaper would
  find nothing to shape with. This is why the 1.7 build does not claim typing support: look at the
  "Try typing here" box in the mod settings to see what the game does by itself.
* **Search.** Quick-search boxes compare against translation text that is stored shaped and reversed,
  so searching with typed Arabic will not find those words on 1.7 (the 1.5 / 1.6 build has a patch for exactly this).

## What has been checked, and what has not

Checked while preparing this change (no RimWorld and no C# compiler were available):

* `About.xml`, `LoadFolders.xml` and the workflow file parse; every folder named in `LoadFolders.xml` exists.
* Both configurations were preprocessed and scanned: balanced brackets and `#if` blocks, and every call
  from one mod class into another resolves to a declared member in that configuration.
* The bundle reader's steps (a Python port, `Tools/fontbundle_to_ttf.py`) opened all 83 bundles in this
  repository (6 mod fonts + 77 extra fonts), and the character-map reader gave the same result as fontTools
  for all 83 fonts. The C# files follow the same code line by line.
* The fonts rendered correctly through FreeType, glyph by glyph, including the pre-placed harakat glyphs.

**Not checked - please test:**

* The 1.7 DLL was never compiled here. Run **Build Mod**; if it reports an error, it will name the line.
* Everything that needs the real game. The engine names used (`GuiBackend.Current`, `RegisterFont`,
  `Font.GameFontIndex`, `Font._material`, `Text.fonts`, `Text.fontStyles`) are the ones the Readable Fonts 1.7
  mod uses, and that mod's own README says its in-game checks were still pending.
* That the engine draws the private-use glyphs (U+E400...) that harakat placement uses. If marks disappear,
  untick "Place harakat on their letter".
* That a DLL compiled against Harmony 2.3 runs on the Harmony 3 that 1.7 requires. Ludeon says 1.6 mods
  run unchanged; Harmony 3 additionally rejects patches whose parameter types do not match the target.
  Every patch here only declares parameters with the same name and type as the original method's
  (or none at all), so it should pass.
* `GUIStyle.contentOffset` (vertical shift) and `Widgets.ClearLabelCache` exist and have an effect in 1.7.

## Test list (in game, RimWorld 1.7)

1. Enable Harmony, then Arabic Support (and the Arabic translation). In the log: `Harmony patches applied
   successfully (RimWorld 1.7 build ...)` and `Loaded font Cairo-Bold.ttf.`
2. Options > Mod settings > Arabic Support: the status line says `Using Cairo Bold.`
3. Preview: line 1 reads joined, right to left; line 2 has harakat above their letters; line 3 is normal English.
4. Move a size slider and let go: Tiny / Small / Medium preview lines change. "Reset font settings" and
   "Use the game font (reset)" go back.
5. Pick another font in the menu (copy one from `ExtraFonts/Fonts`), also a `.fontbundle`.
6. Play: Arabic labels wrap inside their boxes; buttons and the architect menu do not overflow.
7. Change the game language and back: the font is applied again.
8. "Try typing here": write something Arabic and note what the game does.

## If it does not work

The status line in the mod settings, and the log, give the reason:

| Message | Meaning |
|---|---|
| `Waiting for the game to start drawing...` stays | neither `Root.OnGUI` nor `Text.StartOfOnGUI` exists or runs in this version |
| `Custom fonts are not available: this game has no Font.GameFontIndex ...` | the 1.7 DLL is running on a game version without the new engine, or Ludeon renamed it |
| `... GuiBackend ... was not found` / `... GuiBackend.Current was not found` | a name in `WaylithFonts.cs` changed; fix it there |
| `Failed to apply font: The engine's GUI backend has no RegisterFont ...` | the same, for the registration call |
| `'<file>': the bundle uses LZMA ...` / `... is not a Unity bundle` | rebuild the bundle with `Tools/build_fonts.py`, or put a `.ttf` next to it |
| `Failed to apply font: ...` | the full exception is in the log |

Sources: [Ludeon: Announcing the Waylith Engine and Update 1.7](https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/),
[RimWorld Wiki: RimWorld 1.7 Mod Updates](https://rimworldwiki.com/wiki/Modding_Tutorials/RimWorld_1.7_Mod_Updates),
[RimWorld Wiki: Mod folder structure](https://rimworldwiki.com/wiki/Modding_Tutorials/Mod_folder_structure),
[krafs/RimRef#106](https://github.com/krafs/RimRef/issues/106),
[Readable Fonts 1.7](https://github.com/basalt991/readable-fonts17),
[Rimworld-KRInput](https://github.com/craftingmod/Rimworld-KRInput).
