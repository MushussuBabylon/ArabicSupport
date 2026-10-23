[🇮🇶 العربية](README.md) | **🇬🇧 English** | [🇷🇺 Русский](README.ru.md) | [🇫🇷 Français](README.fr.md)

# ArabicSupport Mod

A lightweight RimWorld mod that fixes the rendering of Arabic text requiring right-to-left (RTL) direction, enables Arabic text input, and supports custom fonts.

## Features

- **Arabic Text Input** – Type Arabic characters directly into the game (RimWorld 1.5 and 1.6, see [RimWorld versions](#rimworld-versions)).
- **Accurate RTL Rendering** – Arabic characters are displayed in the correct order, with proper character shaping and letter joining.
- **Custom Font Support** – Change the game's font to any Arabic font you prefer. The default font is **Cairo Bold**.
- **Lightweight and Performance-Optimized** – Uses Harmony patches with minimal resource consumption.
- **Translation Included** – The translation is included as a `Data` folder alongside the mod in the downloaded package.

## RimWorld versions

| RimWorld | What works |
|---|---|
| 1.5 and 1.6 | Everything: Arabic typing, RTL rendering, harakat, custom fonts |
| 1.7 (new Waylith engine) – experimental | RTL rendering and text wrapping, harakat placement, custom fonts |

**What the 1.7 version does not include:** the Arabic typing fix for text boxes and Arabic search matching. RimWorld 1.7 handles typing itself, so typed Arabic may show up with separated letters or in reversed order, depending on what the new engine does.

The mod loads the files for your game version automatically (`LoadFolders.xml`). Developer notes: [docs/RimWorld-1.7.md](docs/RimWorld-1.7.md).

## Installation

**Manual Installation**:

1. Download the latest release from [GitHub](https://github.com/MushussuBabylon/RimWorld-ar) (the `Data-ArabicSupport` version).
2. Extract the archive into your RimWorld game directory. The mod will be placed in the `Mods` folder, and the translation will be placed in the `Data` folder automatically.
3. Enable the mod from the in-game Mods menu.

## Custom Fonts

**Cairo Bold** is the mod's default font. If you want to use a different font, follow these steps:

1. Navigate to `ArabicSupport/ExtraFonts/Fonts` in this repository.
2. Download the font file (`.fontbundle`) you need. (On RimWorld 1.7 your own `.ttf` or `.otf` font files work too.)
3. Copy the downloaded font file into `ArabicSupport/Fonts`, replacing the existing files or adding the new font alongside them.

## Compatibility

- If you encounter any issues, try loading this mod **after** other UI or translation mods.
- **Known Conflicts** – If you encounter a conflict with another mod, please open an issue in this repository.

## Credits

- **Author**: Mushussu Babylon
- **Special Thanks**: The Harmony library for game modding and the RimWorld modding community.

## License

This mod is released under the [MIT License](LICENSE). You are free to use, modify, and distribute it, provided that proper attribution is given.

---

*Dedicated to all Arabic-speaking RimWorld players. Enjoy the game!*
