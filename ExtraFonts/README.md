# Arabic Support - Extra fonts

77 extra font styles (42 families), ready to use. They are NOT part of the mod, so the
mod stays small. The mod itself ships Cairo only (Regular, Medium, SemiBold, Bold,
ExtraBold, Black; Cairo Bold is the default).

## How to use an extra font (players)

1. Download the `.fontbundle` file you want from the `ExtraFonts/Fonts` folder
   of this GitHub repository.
2. Put it in the mod's `Fonts` folder, next to the Cairo files:
   - Steam: `steamapps/workshop/content/294100/<mod id>/Fonts/`
   - Manual install: `RimWorld/Mods/ArabicSupport/Fonts/`
3. Start the game: Options > Mod settings > Arabic Support > Font, and pick it.

Tip: copy only the fonts you want. Each one is small (40 to 240 KB), but the font menu gets long with all of them.

## Available fonts

### UI / clean (good for menus and long text)

| Font | Files |
|---|---|
| Rubik | `Rubik-Medium.fontbundle`, `Rubik-Bold.fontbundle` |
| Tajawal | `Tajawal-Medium.fontbundle`, `Tajawal-Bold.fontbundle` |
| Noto Sans Arabic | `NotoSansArabic-Medium.fontbundle`, `NotoSansArabic-SemiBold.fontbundle`, `NotoSansArabic-Bold.fontbundle` |
| Almarai | `Almarai-Regular.fontbundle`, `Almarai-Bold.fontbundle` |
| IBM Plex Sans Arabic | `IBMPlexSansArabic-Medium.fontbundle`, `IBMPlexSansArabic-SemiBold.fontbundle`, `IBMPlexSansArabic-Bold.fontbundle` |
| Noto Kufi Arabic | `NotoKufiArabic-Medium.fontbundle`, `NotoKufiArabic-Bold.fontbundle` |
| Readex Pro | `ReadexPro-Medium.fontbundle`, `ReadexPro-SemiBold.fontbundle` |
| Changa | `Changa-Medium.fontbundle`, `Changa-Bold.fontbundle` |
| Alexandria | `Alexandria-Medium.fontbundle`, `Alexandria-Bold.fontbundle` |
| Vazirmatn | `Vazirmatn-Medium.fontbundle`, `Vazirmatn-Bold.fontbundle` |
| El Messiri | `ElMessiri-Medium.fontbundle`, `ElMessiri-Bold.fontbundle` |
| Fustat | `Fustat-Medium.fontbundle`, `Fustat-Bold.fontbundle` |
| Mada | `Mada-Medium.fontbundle`, `Mada-Bold.fontbundle` |
| Cairo Play | `CairoPlay-Medium.fontbundle`, `CairoPlay-Bold.fontbundle` |
| Zain | `Zain-Regular.fontbundle`, `Zain-Bold.fontbundle` |
| Kufam | `Kufam-Medium.fontbundle`, `Kufam-Bold.fontbundle` |
| Harmattan | `Harmattan-Medium.fontbundle`, `Harmattan-Bold.fontbundle` |
| Beiruti | `Beiruti-Medium.fontbundle`, `Beiruti-Bold.fontbundle` |
| Estedad | `Estedad-Medium.fontbundle`, `Estedad-Bold.fontbundle` |
| Alan Sans | `AlanSans-Medium.fontbundle`, `AlanSans-Bold.fontbundle` |

### Traditional (Naskh, Ruqaa)

| Font | Files |
|---|---|
| Amiri | `Amiri-Regular.fontbundle`, `Amiri-Bold.fontbundle` |
| Noto Naskh Arabic | `NotoNaskhArabic-Medium.fontbundle`, `NotoNaskhArabic-Bold.fontbundle` |
| Lateef | `Lateef-Regular.fontbundle`, `Lateef-Bold.fontbundle` |
| Markazi Text | `MarkaziText-Medium.fontbundle`, `MarkaziText-Bold.fontbundle` |
| Aref Ruqaa | `ArefRuqaa-Regular.fontbundle`, `ArefRuqaa-Bold.fontbundle` |
| Scheherazade New | `ScheherazadeNew-Regular.fontbundle`, `ScheherazadeNew-Bold.fontbundle` |
| Mirza | `Mirza-Medium.fontbundle`, `Mirza-Bold.fontbundle` |
| Alyamama | `Alyamama-Medium.fontbundle`, `Alyamama-Bold.fontbundle` |
| Ruwudu | `Ruwudu-Regular.fontbundle`, `Ruwudu-Bold.fontbundle` |

### Display / titles (stylised, best for big text)

| Font | Files |
|---|---|
| Lalezar | `Lalezar-Regular.fontbundle` |
| Reem Kufi | `ReemKufi-Medium.fontbundle`, `ReemKufi-Bold.fontbundle` |
| Lemonada | `Lemonada-Medium.fontbundle`, `Lemonada-SemiBold.fontbundle` |
| Rakkas | `Rakkas-Regular.fontbundle` |
| Baloo Bhaijaan 2 | `BalooBhaijaan2-Medium.fontbundle`, `BalooBhaijaan2-Bold.fontbundle` |
| Jomhuria | `Jomhuria-Regular.fontbundle` |
| Marhey | `Marhey-Medium.fontbundle`, `Marhey-Bold.fontbundle` |
| Katibeh | `Katibeh-Regular.fontbundle` |
| Handjet | `Handjet-Medium.fontbundle` |
| Reem Kufi Fun | `ReemKufiFun-Medium.fontbundle` |
| Blaka | `Blaka-Regular.fontbundle` |
| Badeen Display | `BadeenDisplay-Regular.fontbundle` |
| Playpen Sans Arabic | `PlaypenSansArabic-Medium.fontbundle` |

All fonts are from Google Fonts and under the SIL Open Font License 1.1
(one license file per family in `ExtraFonts/Licenses`).

## Building them (mod author)

Source `.ttf` files are in `ExtraFonts/FontSources/Arabic/`, configured in
`ExtraFonts/FontSources/fonts.json`. Run the "Build Mod" GitHub action with
"build extra fonts" ticked (download the "ArabicSupport-extra-fonts" artifact
and put its files in `ExtraFonts/Fonts/`), or locally:

    pip install fonttools UnityPy ttfautohint-py
    python Tools/build_fonts.py --extra
