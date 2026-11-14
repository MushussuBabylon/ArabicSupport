#!/usr/bin/env python3
"""Arabic Support - project self check. Run it after changing fonts, configuration, folders or documents.

    python Tools/verify_fonts.py                    everything (about one minute)
    python Tools/verify_fonts.py --quick            skip the FreeType clipping simulation
    python Tools/verify_fonts.py --skip-originals   skip the comparison with FontSources/Originals (after a font rebuild)
    python Tools/verify_fonts.py --json FILE        also write every number to FILE

It needs fontTools and Pillow (pip install fonttools pillow). The exit code is 1 when anything FAILs, 0 otherwise.

What it checks (FAIL = something is broken, WARN = look at it, ok = fine):

  configuration   Config/fonts.json and fonts.library.json agree with the files in FontSources/, Licenses/,
                  Fonts/ and FontLibrary/; the default font is in the list, it is the first entry, and it is the
                  default that the C# code (ArabicSupportSettings.DefaultArabicFont) and the compiled DLL use
  shipped fonts   Fonts/ holds exactly <id>.fontbundle + <id>.ttf for every configured font; every bundle is read
                  back the way the game reads it (LZ4 blocks checked with the strict decoder of Unity's loader);
                  the .ttf is the font inside the bundle; hhea, OS/2 typo and win metrics agree and the three
                  numbers Unity keeps next to the font match them; the metrics are the ones Tools/fontmetrics.py fits
  against the    FontSources/Originals (as shipped before the metric fix) differs from Fonts/ only in the
  originals       metric fields; glyphs, hinting programs, character maps and names are byte for byte the same;
                  Config/metrics.json (the record of the fit) describes the files that are in Fonts/
  characters      every character the mod draws has ink in every shipped font
  clipping        FreeType simulation, 8-32 px: before (originals) and after (Fonts/); nothing may be cut off at
                  11, 14 and 20 px under the FreeType rule or under the strictest rounding of the line box
  library         every optional font opens and has Latin and Arabic
  references      no stale path or default in text files, every Markdown link and workflow path exists, XML and JSON parse,
                  every folder in LoadFolders.xml exists, C# files have balanced brackets
  C#              for each build configuration of Source/ArabicSupport.csproj (Release = 1.5/1.6, Release17 = 1.7): the right
                  files are compiled, no type is defined twice, every Type.Member used on the mod's own types is
                  declared, Harmony patch classes are complete (a structural check: no C# compiler was available)

The simulation uses FreeType (through Pillow): the same rasteriser family and hinting bytecode Unity uses, glyph by glyph
and left to right like the mod's text. It is NOT RimWorld: nothing here replaces looking at the fonts in the game.
"""

import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from io import BytesIO
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "Tools"))

import fontmetrics as fm     # noqa: E402
import unityfs as ufs        # noqa: E402
from fontTools.ttLib import TTFont  # noqa: E402

RESULTS = {"ok": 0, "warn": 0, "fail": 0}
DATA = {}


def say(level: str, title: str, detail: str = "") -> None:
    RESULTS[level] += 1
    tag = {"ok": "ok  ", "warn": "WARN", "fail": "FAIL"}[level]
    print(f"{tag} {title}" + (f"  {detail}" if detail else ""))


def check(cond: bool, title: str, detail: str = "", warn_only: bool = False) -> bool:
    say("ok" if cond else ("warn" if warn_only else "fail"), title, "" if cond else detail)
    return cond


def section(name: str) -> None:
    print(f"\n== {name}")


def sha256(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def read_json(p: Path):
    return json.loads(p.read_text(encoding="utf-8"))


# --------------------------------------------------------------------------------------------------------------------
def configuration():
    section("configuration")
    cfg = read_json(ROOT / "Config" / "fonts.json")
    lib = read_json(ROOT / "Config" / "fonts.library.json")
    fonts = cfg["fonts"]
    ids = [f["id"] for f in fonts]
    DATA["shipped_ids"] = ids
    check(len(ids) == len(set(ids)), "shipped font ids are unique")
    check(cfg["defaultFont"] in ids, f"default font {cfg['defaultFont']} is one of the shipped fonts")
    check(ids and ids[0] == cfg["defaultFont"], "the default font is the first entry", f"first is {ids[0] if ids else None}")
    rest = [f["family"].lower() for f in fonts[1:]]
    check(rest == sorted(rest), "the other shipped fonts are sorted A-Z by family")
    check(all(f["weight"] >= 700 and f["style"] in ("Bold", "ExtraBold", "Black") for f in fonts), "every shipped font is bold (weight 700 or more)")
    for f in fonts:
        check((ROOT / cfg["sourceDir"] / f["file"]).exists(), f"source of {f['id']}", str(f["file"]))
        check((ROOT / f["license"]).exists(), f"license of {f['id']}", str(f["license"]))
    check(len(ids) == 10, f"ten fonts ship with the mod (found {len(ids)})")

    # library
    lib_ids = [f["id"] for f in lib["fonts"]]
    check(len(lib_ids) == len(set(lib_ids)) and not set(lib_ids) & set(ids), "library ids are unique and not shipped twice")
    missing = [f["id"] for f in lib["fonts"] if not (ROOT / lib["sourceDir"] / f["file"]).exists()]
    check(not missing, "every library source file exists", ", ".join(missing))
    missing = [f["id"] for f in lib["fonts"] if not (ROOT / f["license"]).exists()]
    check(not missing, "every library license exists", ", ".join(missing))
    built = {p.stem for p in (ROOT / "FontLibrary" / "Fonts").glob("*.fontbundle")}
    check(built == set(lib_ids), "FontLibrary/Fonts has a bundle for every library entry and nothing else",
          f"missing {sorted(set(lib_ids) - built)} extra {sorted(built - set(lib_ids))}")
    used = {f["file"].split("/", 1)[1] for f in fonts + lib["fonts"]}
    present = {p.name for p in (ROOT / "FontSources" / "Arabic").iterdir()}
    check(used == present, "FontSources/Arabic holds exactly the sources the two configs use",
          f"unused {sorted(present - used)} missing {sorted(used - present)}")
    lic_used = {Path(f["license"]).name for f in fonts + lib["fonts"]}
    lic_present = {p.name for p in list((ROOT / "Licenses").glob("*.txt")) + list((ROOT / "FontLibrary" / "Licenses").glob("*.txt"))}
    check(lic_used == lic_present, "every license file belongs to a configured font", f"unused {sorted(lic_present - lic_used)}")
    DATA["library_ids"] = lib_ids

    # the C# code
    settings = (ROOT / "Source" / "Settings" / "ArabicSupportSettings.cs").read_text(encoding="utf-8")
    m = re.search(r'DefaultArabicFont\s*=\s*"([^"]+)"', settings)
    check(bool(m) and m.group(1) == cfg["defaultFont"], "ArabicSupportSettings.DefaultArabicFont equals defaultFont in Config/fonts.json",
          f"code has {m.group(1) if m else None}")
    prev = re.search(r'PreviousDefaultFont\s*=\s*"([^"]+)"', settings)
    ver = re.search(r"CurrentSettingsVersion\s*=\s*(\d+)", settings)
    check(bool(prev) and bool(ver) and int(ver.group(1)) >= 11 and prev.group(1) != cfg["defaultFont"],
          "settings migration moves old default saves to the new default", f"previous={prev and prev.group(1)} version={ver and ver.group(1)}")
    ids_in_code = set()
    for p in (ROOT / "Source").rglob("*.cs"):
        code = "\n".join(line for line in p.read_text(encoding="utf-8").splitlines() if not line.lstrip().startswith("//"))
        ids_in_code |= set(re.findall(r'"([A-Za-z]+-(?:Regular|Medium|SemiBold|Bold|ExtraBold|Black))"', code))
    check(ids_in_code <= {cfg["defaultFont"], prev.group(1) if prev else ""}, "no other font id is written into the C# code", str(sorted(ids_in_code)))

    # the compiled DLLs (a string constant is stored in the DLL as UTF-16)
    DATA["dll_default_current"] = {}
    for version in ("1.6", "1.7"):
        dll = ROOT / version / "Assemblies" / "ArabicSupport.dll"
        rel = dll.relative_to(ROOT).as_posix()
        if not dll.exists():
            say("warn", f"{rel} does not exist", "run the Build Mod workflow (dotnet build -c Release / Release17) and commit the DLL")
            DATA["dll_default_current"][version] = None
            continue
        blob = dll.read_bytes()
        has_new = cfg["defaultFont"].encode("utf-16le") in blob
        has_old = bool(prev) and prev.group(1).encode("utf-16le") in blob
        if has_new:
            say("ok", f"{rel} contains the default {cfg['defaultFont']}")
        else:
            say("warn", f"{rel} is OLDER than the source",
                f"it still has {prev.group(1) if has_old else 'another default font'}: run the Build Mod workflow and commit the DLLs")
        DATA["dll_default_current"][version] = bool(has_new)
    return cfg


# --------------------------------------------------------------------------------------------------------------------
def tables(data: bytes) -> dict:
    d = fm._table_directory(data)
    return {tag: bytes(data[off:off + length]) for tag, (_, off, length) in d.items()}


def integrity(font: TTFont, name: str) -> None:
    cmap = font.getBestCmap()
    glyf = font["glyf"]

    def ink(cp):
        g = cmap.get(cp)
        if g is None:
            return False
        gl = glyf[g]
        if gl.isComposite():
            return any(glyf[c.glyphName].numberOfContours != 0 for c in gl.components)
        return gl.numberOfContours > 0

    sets = {
        "ASCII": range(0x21, 0x7F),
        "Latin-1 letters": [c for c in range(0xC0, 0x100) if c not in (0xD7, 0xF7)],
        "Arabic letters": list(range(0x0621, 0x063B)) + list(range(0x0641, 0x064B)),
        "Arabic shaped forms U+FE80-FEFC": range(0xFE80, 0xFEFD),
        "Arabic-Indic digits": range(0x0660, 0x066A),
        "Arabic punctuation": [0x060C, 0x061B, 0x061F],
        "harakat": list(range(0x064B, 0x0653)) + [0x0670],
        "placed harakat U+E400-EE6F": range(0xE400, 0xEE70),
    }
    bad = {k: [f"{c:04X}" for c in v if not ink(c)] for k, v in sets.items()}
    bad = {k: v for k, v in bad.items() if v}
    check(not bad, f"{name}: every character the mod draws has ink", "; ".join(f"{k}: {' '.join(v[:6])}" for k, v in bad.items()))


def shipped_fonts(cfg, quick: bool):
    section("shipped fonts (Fonts/)")
    ids = [f["id"] for f in cfg["fonts"]]
    expected = {f"{i}.fontbundle" for i in ids} | {f"{i}.ttf" for i in ids}
    present = {p.name for p in (ROOT / "Fonts").iterdir()}
    check(present == expected, "Fonts/ holds a .fontbundle and a .ttf for every configured font and nothing else",
          f"missing {sorted(expected - present)} extra {sorted(present - expected)}")
    records = {}
    for font_id in ids:
        bpath, tpath = ROOT / "Fonts" / f"{font_id}.fontbundle", ROOT / "Fonts" / f"{font_id}.ttf"
        if not (bpath.exists() and tpath.exists()):
            continue
        raw, loose = bpath.read_bytes(), tpath.read_bytes()
        rec = {"bundleBytes": len(raw), "ttfBytes": len(loose)}
        try:
            b = ufs.read_bundle(raw)
            strict = all(ufs.lz4_decompress(blob, size, strict=True) for blob, size, fl in zip(b.packed_blocks, b.block_sizes, b.block_flags) if fl & 0x3F in (2, 3))
            strict = strict and bool(ufs.lz4_decompress(b.info_packed, len(b.info_raw), strict=True))
            off, length = ufs.find_sfnt(b.data)
            inside = b.data[off:off + length]
            check(strict, f"{font_id}: bundle is valid UnityFS v{b.version}, {len(b.block_sizes)} LZ4 block(s) pass the strict decoder")
            check(inside == loose, f"{font_id}: the .ttf is the font inside the bundle")
            floats = ufs.read_unity_floats(b.data, off, length)
            m = fm.read_vertical_metrics(loose)
            want = ufs.metrics_floats(m["hhea_ascent"], -m["hhea_descent"], m["upm"])
            check(all(abs(floats[k] - v) < 1e-3 for k, v in want.items()) and abs(floats["m_FontSize"] - 16.0) < 1e-3,
                  f"{font_id}: the three metric numbers Unity keeps next to the font match hhea", str(floats))
            check(fm.metrics_are_consistent(m) and m["fs_selection"] & 0x80, f"{font_id}: hhea, OS/2 typo and win metrics agree, line gap 0, USE_TYPO_METRICS on", str(m))
            font = TTFont(BytesIO(loose), checkChecksums=2)
            check(True, f"{font_id}: table checksums and head.checkSumAdjustment are valid")
            fit = fm.fit_line_metrics(loose)
            check((fit.ascent, fit.descent) == (m["hhea_ascent"], -m["hhea_descent"]),
                  f"{font_id}: the stored metrics {m['hhea_ascent']}/{-m['hhea_descent']} are what the fit chooses",
                  f"fit gives {fit.ascent}/{fit.descent}")
            integrity(font, font_id)
            rec.update({"upm": m["upm"], "ascent": m["hhea_ascent"], "descent": -m["hhea_descent"], "fitNotes": fit.notes,
                        "mustHave": list(fit.must), "setBy": fit.drivers, "ftRounds": fit.ft_round})
        except (ufs.BundleError, AssertionError, Exception) as e:     # a broken file must not stop the other checks
            say("fail", f"{font_id}: {type(e).__name__}", str(e)[:200])
        records[font_id] = rec
    DATA["fonts"] = records
    return records


def against_originals(cfg):
    section("against the originals (FontSources/Originals)")
    folder = ROOT / "FontSources" / "Originals"
    sums = {}
    for line in (folder / "SHA256SUMS.txt").read_text(encoding="utf-8").splitlines():
        h, _, n = line.partition("  ")
        sums[n] = h
    bad = [n for n, h in sums.items() if sha256((folder / n).read_bytes()) != h]
    check(not bad and len(sums) >= 10, f"the {len(sums)} original files match SHA256SUMS.txt", ", ".join(bad))
    for f in cfg["fonts"]:
        fid = f["id"]
        o = ufs.read_bundle((folder / f"{fid}.fontbundle").read_bytes())
        off, length = ufs.find_sfnt(o.data)
        old = o.data[off:off + length]
        new = (ROOT / "Fonts" / f"{fid}.ttf").read_bytes()
        if len(old) != len(new):
            say("fail", f"{fid}: the font changed size", f"{len(old)} -> {len(new)}")
            continue
        to, tn = tables(old), tables(new)
        same_set = set(to) == set(tn)
        changed = sorted(t for t in to if to[t] != tn.get(t))
        # the three patched tables may differ; inside them only the metric fields and the checksum field
        allowed = {"head", "hhea", "OS/2"}
        check(same_set and set(changed) <= allowed, f"{fid}: only {', '.join(changed)} changed; every glyph, hinting, cmap and name table is byte-identical",
              f"changed tables: {changed}")
        diff_bytes = sum(1 for t in changed for a, b in zip(to[t], tn[t]) if a != b)
        DATA.setdefault("fonts", {}).setdefault(fid, {})["bytesChangedInTables"] = diff_bytes
        # the container
        raw_new = ufs.read_bundle((ROOT / "Fonts" / f"{fid}.fontbundle").read_bytes())
        same_head = (o.version, o.player_version, o.engine_version, o.flags, o.info_hash, o.block_sizes, o.block_flags,
                     [(n.offset, n.size, n.flags, n.path) for n in o.nodes]) == \
                    (raw_new.version, raw_new.player_version, raw_new.engine_version, raw_new.flags, raw_new.info_hash, raw_new.block_sizes,
                     raw_new.block_flags, [(n.offset, n.size, n.flags, n.path) for n in raw_new.nodes])
        check(same_head, f"{fid}: bundle header, block layout and node table are unchanged")
        d = sum(1 for a, b in zip(o.data, raw_new.data) if a != b)
        check(d <= 64, f"{fid}: {d} bytes differ inside the bundle (font metric fields, table checksums, three floats)", warn_only=False)

    # Config/metrics.json (written by Tools/retune_font_metrics.py --json) is the record of this comparison
    problems = []
    try:
        entries = {e["id"]: e for e in read_json(ROOT / "Config" / "metrics.json")}
    except (OSError, ValueError, KeyError, TypeError) as e:
        entries = {}
        problems.append(f"Config/metrics.json cannot be read: {e}")
    ids = [f["id"] for f in cfg["fonts"]]
    if entries and set(entries) != set(ids):
        problems.append(f"it lists {sorted(set(entries) ^ set(ids))} differently from Config/fonts.json")
    for fid in ids:
        e, tpath, bpath = entries.get(fid), ROOT / "Fonts" / f"{fid}.ttf", ROOT / "Fonts" / f"{fid}.fontbundle"
        if not e or not (tpath.exists() and bpath.exists()):
            continue
        m = fm.read_vertical_metrics(tpath.read_bytes())
        if (e["after"]["ascent"], e["after"]["descent"]) != (m["hhea_ascent"], -m["hhea_descent"]):
            problems.append(f"{fid}: it says {e['after']['ascent']}/{e['after']['descent']}, the font has {m['hhea_ascent']}/{-m['hhea_descent']}")
        elif e.get("ttfSha256") != sha256(tpath.read_bytes()) or e.get("bundleSha256") != sha256(bpath.read_bytes()):
            problems.append(f"{fid}: the SHA-256 of a file differs from the record")
    check(not problems, "Config/metrics.json describes the fonts in Fonts/ (ascent, descent and SHA-256 of both files of each font)",
          "; ".join(problems[:6]) + ("  -> python Tools/retune_font_metrics.py FontSources/Originals Fonts --json Config/metrics.json" if problems else ""))


def clipping(cfg, quick: bool):
    section("clipping simulation (FreeType, hinted, Latin + Arabic letters, digits, punctuation, shaped forms)")
    if quick:
        print("skipped (--quick)")
        return
    sizes = fm.REPORT_SIZES
    table = {}
    for f in cfg["fonts"]:
        fid = f["id"]
        after = (ROOT / "Fonts" / f"{fid}.ttf").read_bytes()
        o = ufs.read_bundle((ROOT / "FontSources" / "Originals" / f"{fid}.fontbundle").read_bytes())
        off, length = ufs.find_sfnt(o.data)
        before = o.data[off:off + length]
        rb = fm.clipping_report(before, sizes, only_groups=fm.REQUIRED_RANGES)
        ra = fm.clipping_report(after, sizes, only_groups=fm.REQUIRED_RANGES)

        def count(rep, model, size):
            tot_t = tot_b = rows_t = rows_b = 0
            for g in fm.REQUIRED_RANGES:
                r = rep["sizes"][size]["groups"].get(g)
                if r:
                    tot_t += r[model][0]; rows_t = max(rows_t, r[model][1])
                    tot_b += r[model][2]; rows_b = max(rows_b, r[model][3])
            return tot_t, tot_b, rows_t, rows_b

        entry = {"before": {}, "after": {}}
        for s in sizes:
            entry["before"][s] = count(rb, "must", s)
            entry["after"][s] = count(ra, "must", s)
        entry["after_strict"] = {s: count(ra, "strict", s) for s in sizes}
        entry["box_before"] = {s: rb["sizes"][s]["must"] for s in fm.PRIMARY_SIZES}
        entry["box_after"] = {s: ra["sizes"][s]["must"] for s in fm.PRIMARY_SIZES}
        table[fid] = entry
        b_cut = sum(sum(entry["before"][s][:2]) for s in fm.PRIMARY_SIZES)
        a_cut = sum(sum(entry["after"][s][:2]) for s in fm.PRIMARY_SIZES)
        s_cut = sum(sum(entry["after_strict"][s][:2]) for s in fm.PRIMARY_SIZES)
        check(a_cut == 0 and s_cut == 0,
              f"{fid}: no letter, digit or sign is cut off at 11, 14 and 20 px, under the FreeType rule or the strictest rounding (before the fix: {b_cut} glyph cut-offs)",
              f"{a_cut} (FreeType rule) and {s_cut} (strictest rounding) glyph cut-offs remain")
        b_all = sum(sum(entry["before"][s][:2]) for s in sizes)
        a_all = sum(sum(entry["after"][s][:2]) for s in sizes)
        check(a_all <= b_all, f"{fid}: over 8-32 px the cut-offs go from {b_all} to {a_all}", warn_only=True)
    DATA["clipping"] = table
    # harakat, for the record
    har = {}
    for f in cfg["fonts"]:
        fid = f["id"]
        o = ufs.read_bundle((ROOT / "FontSources" / "Originals" / f"{fid}.fontbundle").read_bytes())
        off, length = ufs.find_sfnt(o.data)
        before = fm.harakat_fit(o.data[off:off + length])
        after = fm.harakat_fit((ROOT / "Fonts" / f"{fid}.ttf").read_bytes())
        har[fid] = {"before": before, "after": after}
    DATA["harakat"] = har


def library():
    section("library (FontLibrary/Fonts)")
    bad, n = [], 0
    for p in sorted((ROOT / "FontLibrary" / "Fonts").iterdir()):
        if p.suffix != ".fontbundle":
            continue
        n += 1
        try:
            b = ufs.read_bundle(p.read_bytes())
            off, length = ufs.find_sfnt(b.data)
            cm = TTFont(BytesIO(b.data[off:off + length])).getBestCmap()
            if not (ord("a") in cm and 0xFEE3 in cm and 0xFE8E in cm):
                bad.append(p.name + " (no Latin or Arabic)")
        except Exception as e:
            bad.append(f"{p.name}: {e}")
    check(not bad and n > 0, f"all {n} optional bundles open and have Latin and Arabic letters", "; ".join(bad))


# --------------------------------------------------------------------------------------------------------------------
TEXT_SUFFIXES = {".md", ".xml", ".json", ".yml", ".yaml", ".py", ".cs", ".csproj", ".txt", ".gitignore"}
STALE = [
    (re.compile(r"ExtraFonts"), "the folder is now FontLibrary"),
    (re.compile(r"FontSources/Licenses|FontSources\\Licenses"), "licenses moved to Licenses/ and FontLibrary/Licenses/"),
    (re.compile(r"(?<![A-Za-z/])docs[/\\]RimWorld"), "docs/ is now Docs/"),
    (re.compile(r"Fonts/fonts\.json|FontSources/fonts\.json"), "configuration moved to Config/"),
    # present-tense claims only: "the default font is El Messiri Bold", "El Messiri Bold is the default"; "was the default" is history
    (re.compile(r"default(?: font)?\s*(?:is|=|:)\s*[*\"]*El[ -]?Messiri|El[ -]?Messiri[ -]Bold\s+(?:is|stays|remains)\s+(?:still\s+)?(?:the\s+|its\s+|used\s+by\s+)?(?:mod's\s+)?default|الافتراضي\s*[:=]?\s*(?:هو\s*)?[*\"]*El[ -]?Messiri|El Messiri Bold \(?used by default|El Messiri Bold \(default\)", re.I), "the default font is Cairo Bold"),
]
SKIP_STALE = {"Tools/verify_fonts.py"}


def references():
    section("references")
    files = [p for p in ROOT.rglob("*") if p.is_file() and p.suffix in TEXT_SUFFIXES and ".git" not in p.parts
             and "FontLibrary" not in p.parts[:1] or (p.name == "README.md" and p.parent.name == "FontLibrary")]
    files = [p for p in files if "__pycache__" not in p.parts and p.suffix != ".txt" or p.name in ("LICENSE-fontbundle-tools.txt",)]
    hits = []
    for p in files:
        rel = p.relative_to(ROOT).as_posix()
        if rel in SKIP_STALE or rel.startswith("Licenses/"):
            continue
        text = p.read_text(encoding="utf-8", errors="replace")
        for rx, why in STALE:
            for m in rx.finditer(text):
                line = text.count("\n", 0, m.start()) + 1
                hits.append(f"{rel}:{line} {m.group(0)!r} ({why})")
    check(not hits, "no stale path or default is left in any text file", "\n     ".join(hits[:20]))

    # markdown links, including #anchors that point into another (or the same) Markdown file
    def slug(heading: str) -> str:
        """GitHub's anchor for a heading: lower case, punctuation dropped, spaces become hyphens."""
        h = re.sub(r"[`*~]", "", heading.strip().lower())
        return re.sub(r"[^\w\- ]", "", h).replace(" ", "-")

    anchors = {}

    def anchors_of(path: Path) -> set:
        if path not in anchors:
            text = re.sub(r"```.*?```", "", path.read_text(encoding="utf-8"), flags=re.S)
            anchors[path] = {slug(m.group(1)) for m in re.finditer(r"^#{1,6}\s+(.+?)\s*$", text, re.M)}
        return anchors[path]

    broken = []
    pages = list(ROOT.glob("*.md")) + list((ROOT / "Docs").glob("*.md")) + [ROOT / "FontLibrary" / "README.md"]
    for p in pages:
        text = re.sub(r"```.*?```", "", p.read_text(encoding="utf-8"), flags=re.S)
        for m in re.finditer(r"\]\(([^)#\s]*)(?:#([^)\s]*))?\)", text):
            target, frag = m.group(1), m.group(2)
            if re.match(r"[a-z]+:", target):
                continue
            dest = (p.parent / target).resolve() if target else p
            where = f"{p.relative_to(ROOT).as_posix()} -> {target}{'#' + frag if frag else ''}"
            if not dest.exists():
                broken.append(where)
            elif frag and dest.suffix == ".md" and frag.lower() not in anchors_of(dest):
                broken.append(where + " (no such heading)")
    check(not broken, "every relative Markdown link and #anchor resolves", "; ".join(broken))

    # xml, json
    for p in [ROOT / "About" / "About.xml", ROOT / "LoadFolders.xml"]:
        try:
            ET.parse(p)
            say("ok", f"{p.relative_to(ROOT).as_posix()} is well-formed XML")
        except ET.ParseError as e:
            say("fail", f"{p.relative_to(ROOT).as_posix()} does not parse", str(e))
    for p in sorted((ROOT / "Config").glob("*.json")):
        try:
            read_json(p)
            say("ok", f"{p.relative_to(ROOT).as_posix()} is valid JSON")
        except ValueError as e:
            say("fail", f"{p.relative_to(ROOT).as_posix()} does not parse", str(e))
    tree = ET.parse(ROOT / "LoadFolders.xml").getroot()
    folders = {li.text.strip() for li in tree.iter("li")}
    gone = [f for f in folders if f != "/" and not (ROOT / f).is_dir()]
    check(not gone, "every folder named in LoadFolders.xml exists", ", ".join(gone))
    about = (ROOT / "About" / "About.xml").read_text(encoding="utf-8")
    cfg = read_json(ROOT / "Config" / "fonts.json")
    default_pretty = next(f for f in cfg["fonts"] if f["id"] == cfg["defaultFont"])
    check(f"{default_pretty['family']} {default_pretty['style']}" in about, "About.xml names the default font", warn_only=False)

    # workflow
    wf = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
    missing = []
    for tok in sorted(set(re.findall(r"(?<![\w./-])((?:Config|Fonts|Licenses|FontSources|FontLibrary|Tools|Source|About|Docs)/[\w./*\[\]-]*)", wf))):
        probe = tok.rstrip("/.")
        if "*" in probe:
            if not list(ROOT.glob(probe)):
                missing.append(tok)
        elif not (ROOT / probe).exists():
            missing.append(tok)
    check(not missing, "every repository path the workflow mentions exists", ", ".join(missing))

    # C# brackets
    bad = []
    for p in sorted((ROOT / "Source").rglob("*.cs")):
        if not balanced(p.read_text(encoding="utf-8")):
            bad.append(p.relative_to(ROOT).as_posix())
    check(not bad, f"all {len(list((ROOT / 'Source').rglob('*.cs')))} C# files have balanced brackets and #if blocks (a compiler was not available)", ", ".join(bad), warn_only=False)


def strip_cs(src: str) -> str:
    """The source with comments and the inside of strings and characters blanked out (line breaks are kept)."""
    out, i, n = [], 0, len(src)

    def blank(piece: str) -> str:
        return re.sub(r"[^\n]", " ", piece)

    while i < n:
        c = src[i]
        if src.startswith("//", i):
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(blank(src[i:j]))
            i = j
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(blank(src[i:j]))
            i = j
        elif src.startswith(('@"', '$@"', '@$"'), i):
            j = src.index('"', i) + 1
            while j < n:
                if src[j] == '"':
                    if src.startswith('""', j):
                        j += 2
                        continue
                    break
                j += 1
            out.append(blank(src[i:j + 1]))
            i = j + 1
        elif src.startswith('$"', i):
            j, depth = i + 2, 0
            while j < n:
                if src[j] == "\\":
                    j += 2
                    continue
                if src[j] == "{":
                    if src.startswith("{{", j):
                        j += 2
                        continue
                    depth += 1
                elif src[j] == "}":
                    if src.startswith("}}", j):
                        j += 2
                        continue
                    depth -= 1
                elif src[j] == '"' and depth <= 0:
                    break
                j += 1
            out.append(blank(src[i:j + 1]))
            i = j + 1
        elif c in "\"'":
            j = i + 1
            while j < n:
                if src[j] == "\\":
                    j += 2
                    continue
                if src[j] == c:
                    break
                j += 1
            out.append(blank(src[i:j + 1]))
            i = j + 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def balanced(src: str) -> bool:
    """Brackets and #if/#endif balance, ignoring strings, characters and comments (a sanity check, not a parser)."""
    pairs, stack = {"(": ")", "{": "}", "[": "]"}, []
    for ch in strip_cs(src):
        if ch in pairs:
            stack.append(ch)
        elif ch in pairs.values():
            if not stack or pairs[stack.pop()] != ch:
                return False
    ifs = len(re.findall(r"^\s*#if", src, re.M))
    ends = len(re.findall(r"^\s*#endif", src, re.M))
    return not stack and ifs == ends


# --------------------------------------------------------------------------------------------------------------------
# C#: what each build configuration compiles. No C# compiler was available, so this is a structural check, not a build.
SRC = ROOT / "Source"
CS_CONFIGS = {"Release": set(), "Release17": {"RW17"}}      # configuration -> conditional compilation symbols
TYPE_RX = re.compile(r"\b((?:(?:public|internal|private|protected|static|sealed|abstract|partial)\s+)*)"
                     r"(class|struct|enum|interface)\s+(\w+)")


def preprocess(src: str, symbols: set) -> str:
    """Resolves #if SYMBOL / #if !SYMBOL / #else / #endif, the only forms the project uses (anything else raises)."""
    out, stack = [], []         # stack of {"parent": bool, "cond": bool, "active": bool}

    def active() -> bool:
        return stack[-1]["active"] if stack else True

    for line in src.split("\n"):
        m = re.match(r"\s*#\s*(\w+)\s*(.*)$", line)
        if m and m.group(1) in ("if", "else", "endif", "elif", "define", "undef", "region", "endregion", "pragma"):
            kind, rest = m.group(1), m.group(2).split("//")[0].strip()
            if kind == "if":
                m2 = re.fullmatch(r"(!?)(\w+)", rest)
                if not m2:
                    raise ValueError(f"unsupported #if expression {rest!r}")
                cond = (m2.group(2) in symbols) != bool(m2.group(1))
                parent = active()
                stack.append({"parent": parent, "cond": cond, "active": parent and cond})
            elif kind == "else":
                top = stack[-1]
                top["active"] = top["parent"] and not top["cond"]
            elif kind == "endif":
                stack.pop()
            elif kind in ("elif", "define", "undef"):
                raise ValueError(f"unsupported directive #{kind}")
            out.append("")
        else:
            out.append(line if active() else "")
    if stack:
        raise ValueError("#if without #endif")
    return "\n".join(out)


def csproj_removed(config: str) -> list:
    """The <Compile Remove="..."> patterns of Source/ArabicSupport.csproj that apply to one build configuration."""
    root = ET.parse(SRC / "ArabicSupport.csproj").getroot()
    removed = []
    for group in root.iter("ItemGroup"):
        cond = group.get("Condition")
        if cond:
            m = re.fullmatch(r"\s*'\$\(Configuration\)'\s*(==|!=)\s*'(\w+)'\s*", cond)
            if not m:
                raise ValueError(f"unsupported ItemGroup condition {cond!r}")
            if (m.group(2) == config) != (m.group(1) == "=="):
                continue
        for item in group.findall("Compile"):
            if item.get("Remove"):
                removed.append(item.get("Remove").replace("\\", "/"))
    return removed


def cs_files(config: str) -> list:
    """The .cs files (as paths relative to Source/) that one build configuration compiles."""
    every = sorted(p for p in SRC.rglob("*.cs") if not {"obj", "bin"} & set(p.relative_to(SRC).parts))
    dropped = set()
    for pattern in csproj_removed(config):
        if pattern.endswith("/**"):
            pattern += "/*"
        dropped |= {p for p in SRC.glob(pattern) if p.is_file()}
    return [p.relative_to(SRC).as_posix() for p in every if p not in dropped]


def type_spans(code: str) -> list:
    """Every class / struct / enum / interface in stripped code, with the extent of its body and its nesting."""
    spans = []
    for m in TYPE_RX.finditer(code):
        brace = code.find("{", m.end())
        if brace < 0 or ";" in code[m.end():brace]:
            continue
        depth, j = 0, brace
        while j < len(code):
            if code[j] == "{":
                depth += 1
            elif code[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        spans.append({"name": m.group(3), "kind": m.group(2), "partial": "partial" in m.group(1),
                      "start": m.start(), "open": brace, "close": j})
    for s in spans:
        parents = sorted((p for p in spans if p["open"] < s["start"] < p["close"]), key=lambda p: p["open"])
        s["top"] = not parents
        s["qual"] = ".".join([p["name"] for p in parents] + [s["name"]])
    return spans


def type_members(code: str, span: dict) -> set:
    """Names declared directly in a type: methods, properties, fields, constants, nested types, enum members."""
    body = code[span["open"] + 1:span["close"]]
    flat, depth = [], 0
    for ch in body:                  # keep this type's own level; every nested {...} block becomes a ';'
        if ch == "{":
            if depth == 0:
                flat.append(" ; ")
            depth += 1
        elif ch == "}":
            depth -= 1
        elif depth == 0:
            flat.append(ch)
    text = "".join(flat)
    names = set()
    if span["kind"] == "enum":
        for part in text.replace(";", ",").split(","):
            m = re.match(r"\s*(\w+)", part)
            if m:
                names.add(m.group(1))
        return names
    for stmt in text.split(";"):
        st = re.sub(r"\[[^\]]*\]", " ", stmt).strip()           # attributes
        if not st or st.startswith(("using ", "namespace ")):
            continue
        km = re.search(r"\b(?:class|struct|enum|interface)\s+(\w+)", st)
        if km:
            names.add(km.group(1))
            continue
        head = re.split(r"=>|=(?!>)", st, maxsplit=1)[0]
        mm = re.search(r"(\w+)\s*(?:<[^<>()]*>)?\s*\(", head)
        if mm:
            names.add(mm.group(1))
        else:
            ids = re.findall(r"\w+", head)
            if ids:
                names.add(ids[-1])
    return names


def csharp():
    section("C# per build configuration (structural check - no C# compiler was available)")
    code_of, files_of = {}, {}
    for config, symbols in CS_CONFIGS.items():
        files_of[config] = cs_files(config)
        code_of[config] = {}
        for rel in files_of[config]:
            try:
                code_of[config][rel] = strip_cs(preprocess((SRC / rel).read_text(encoding="utf-8"), symbols))
            except ValueError as e:
                say("fail", f"{config}: Source/{rel}", str(e))

    # 1. which files each configuration compiles
    expect = {
        "Release": (["Fonts/FontManager.cs", "Patches/Patch_TextInput.cs", "Patches/Patch_QuickSearchMatches.cs",
                     "TextInput/ArabicTextEngine.cs"],
                    ["RW17/FontManager.RW17.cs", "RW17/Patch_TextInput.RW17.cs", "RW17/WaylithFonts.cs"]),
        "Release17": (["RW17/FontManager.RW17.cs", "RW17/Patch_TextInput.RW17.cs", "RW17/WaylithFonts.cs",
                       "Patches/Patch_QuickSearchMatches.cs", "TextInput/ArabicTextEngine.cs"],
                      ["Fonts/FontManager.cs", "Patches/Patch_TextInput.cs"]),
    }
    for config, (must, must_not) in expect.items():
        missing = [f for f in must if f not in files_of[config]]
        extra = [f for f in must_not if f in files_of[config]]
        check(not missing and not extra, f"{config} compiles the right files ({len(files_of[config])} files)",
              "; ".join(([f"missing: {', '.join(missing)}"] if missing else []) + ([f"must not compile: {', '.join(extra)}"] if extra else [])))

    # 2. no type is defined twice
    for config in CS_CONFIGS:
        seen, dups = {}, []
        for rel, code in code_of[config].items():
            ns = re.search(r"\bnamespace\s+([\w.]+)", code)
            ns = ns.group(1) if ns else ""
            for s in type_spans(code):
                if not s["top"]:
                    continue
                key = f"{ns}.{s['name']}"
                if key in seen and not (s["partial"] and seen[key][1]):
                    dups.append(f"{key} in Source/{seen[key][0]} and Source/{rel}")
                seen.setdefault(key, (rel, s["partial"]))
        check(not dups, f"{config}: every type is defined once ({len(seen)} types)", "; ".join(dups))

    # 3. every Type.Member the code uses on one of the mod's own types is declared in that type
    for config in CS_CONFIGS:
        types = {}
        for rel, code in code_of[config].items():
            for s in type_spans(code):
                if s["top"]:
                    types.setdefault(s["name"], set()).update(type_members(code, s))
        problems, count = [], 0
        for rel, code in code_of[config].items():
            for m in re.finditer(r"[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)+", code):
                before = code[:m.start()].rstrip()
                if before.endswith("."):
                    continue                                    # a member of something else, not a type name
                parts = [p.strip() for p in m.group(0).split(".")]
                if parts[0] == "ArabicSupport":                 # fully qualified: drop the namespace
                    while len(parts) > 1 and parts[0] not in types:
                        parts.pop(0)
                if len(parts) > 1 and parts[0] in types:
                    count += 1
                    if parts[1] not in types[parts[0]]:
                        problems.append(f"Source/{rel}:{code.count(chr(10), 0, m.start()) + 1} {parts[0]}.{parts[1]}")
        check(not problems, f"{config}: every member used on the mod's own types is declared there ({count} uses checked)",
              "; ".join(problems[:12]))

    # 4. Harmony patch classes have what Harmony looks for
    for config in CS_CONFIGS:
        bad, count = [], 0
        for rel, code in code_of[config].items():
            for s in type_spans(code):
                if not s["top"]:
                    continue
                am = re.search(r"\[HarmonyPatch(\(.*?\))?\]\s*$", code[max(0, s["start"] - 600):s["start"]], re.S)
                if not am:
                    continue
                count += 1
                members = type_members(code, s)
                if not members & {"Prefix", "Postfix", "Transpiler", "Finalizer"}:
                    bad.append(f"{s['name']} has no Prefix/Postfix/Transpiler/Finalizer")
                if not am.group(1) and not members & {"TargetMethod", "TargetMethods"}:
                    bad.append(f"{s['name']} has [HarmonyPatch] without arguments and no TargetMethod")
        check(not bad, f"{config}: all {count} Harmony patch classes are complete", "; ".join(bad))


# --------------------------------------------------------------------------------------------------------------------
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Project self check", formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    ap.add_argument("--quick", action="store_true", help="skip the clipping simulation")
    ap.add_argument("--skip-originals", action="store_true",
                    help="skip the comparison with FontSources/Originals (use it after Tools/build_fonts.py rebuilt the fonts: "
                         "a new ttfautohint run is not byte-identical to the archived originals)")
    ap.add_argument("--json", type=Path, help="write all numbers to this file")
    a = ap.parse_args(argv)

    if hasattr(sys.stdout, "reconfigure"):                    # Windows consoles: never stop on a non-ASCII character
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    cfg = configuration()
    shipped_fonts(cfg, a.quick)
    if a.skip_originals:
        section("against the originals (FontSources/Originals)")
        print("skipped (--skip-originals)")
    else:
        against_originals(cfg)
    clipping(cfg, a.quick)
    library()
    references()
    csharp()

    print(f"\n{RESULTS['ok']} ok, {RESULTS['warn']} warnings, {RESULTS['fail']} failures")
    if a.json:
        a.json.parent.mkdir(parents=True, exist_ok=True)
        a.json.write_text(json.dumps({"summary": RESULTS, **DATA}, indent=1, default=str) + "\n", encoding="utf-8")
    return 1 if RESULTS["fail"] else 0


if __name__ == "__main__":
    sys.exit(main())
