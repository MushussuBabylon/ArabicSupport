#!/usr/bin/env python3
"""Arabic Support - give already built fonts their fitted line metrics, without rebuilding them.

build_fonts.py builds a font from its source and fits the line metrics as its last step. This tool does only
that last step for fonts that are already built (a .fontbundle and/or its .ttf): it fits the metrics of
Tools/fontmetrics.py to the glyphs that are in the file, writes them into the font, and repacks the bundle
with Tools/unityfs.py. Nothing else in the font or the bundle changes (verify_fonts.py checks that).
It needs only fontTools and Pillow, no Unity tools and no ttfautohint, so it also works on a plain machine.

    python Tools/retune_font_metrics.py SRC DST              fit every font in SRC, write the result to DST
    python Tools/retune_font_metrics.py SRC DST --only Cairo-Bold,Rubik-Bold
    python Tools/retune_font_metrics.py --check DIR          report fonts whose metrics differ from the fit (changes nothing)

SRC and DST may be the same folder. SRC holds `<Id>.fontbundle`; a `<Id>.ttf` next to it is checked against the
bundle and written to DST as well (RimWorld 1.7 loads the .ttf when there is one). A `.ttf` without a bundle is
retuned on its own.
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import fontmetrics as fm        # noqa: E402
import unityfs as ufs           # noqa: E402


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_font(bundle: Path, ttf: Path):
    """(bundle bytes or None, font bytes). The font inside the bundle wins; a .ttf next to it must be the same file."""
    raw = bundle.read_bytes() if bundle and bundle.exists() else None
    loose = ttf.read_bytes() if ttf and ttf.exists() else None
    if raw is None:
        if loose is None:
            raise FileNotFoundError(f"neither {bundle} nor {ttf} exists")
        return None, loose
    b = ufs.read_bundle(raw)
    off, length = ufs.find_sfnt(b.data)
    inside = b.data[off:off + length]
    if loose is not None and loose != inside:
        raise SystemExit(f"{ttf.name} is not the font that is inside {bundle.name}: rebuild them together "
                         f"(python Tools/build_fonts.py --ttf)")
    return raw, inside


def retune(font_id: str, src: Path, dst: Path, write_ttf: bool = True) -> dict:
    raw, ttf = read_font(src / f"{font_id}.fontbundle", src / f"{font_id}.ttf")
    old = fm.read_vertical_metrics(ttf)
    fit = fm.fit_line_metrics(ttf)
    new_ttf = fm.patch_vertical_metrics(ttf, fit.ascent, fit.descent)
    assert len(new_ttf) == len(ttf)
    rec = {
        "id": font_id, "unitsPerEm": fit.upm,
        "before": {"ascent": old["hhea_ascent"], "descent": -old["hhea_descent"]},
        "after": {"ascent": fit.ascent, "descent": fit.descent},
        "lineHeightEm": {"before": round((old["hhea_ascent"] - old["hhea_descent"]) / fit.upm, 4),
                         "after": round(fit.line_height_em, 4)},
        "method": fit.method, "freetypeRoundsLineBox": fit.ft_round,
        "mustHave": {"ascent": fit.must[0], "descent": fit.must[1]}, "setBy": fit.drivers, "notes": fit.notes,
    }
    dst.mkdir(parents=True, exist_ok=True)
    if raw is not None:
        new_raw = ufs.replace_font(raw, new_ttf, (fit.ascent, fit.descent, fit.upm),
                                   (old["hhea_ascent"], -old["hhea_descent"], fit.upm))
        # read it back the way the game will
        back = ufs.read_bundle(new_raw)
        off, length = ufs.find_sfnt(back.data)
        assert back.data[off:off + length] == new_ttf, "the font inside the new bundle is not the patched font"
        floats = ufs.read_unity_floats(back.data, off, length)
        for k, v in ufs.metrics_floats(fit.ascent, fit.descent, fit.upm).items():
            assert abs(floats[k] - v) < 1e-3, f"{k} was not written"
        (dst / f"{font_id}.fontbundle").write_bytes(new_raw)
        rec["bundleBytes"] = len(new_raw)
        rec["bundleSha256"] = sha256(new_raw)
    if write_ttf:
        (dst / f"{font_id}.ttf").write_bytes(new_ttf)
    rec["ttfBytes"] = len(new_ttf)
    rec["ttfSha256"] = sha256(new_ttf)
    return rec


def check(folder: Path) -> int:
    """Does every font in the folder already have the metrics the fitter chooses?"""
    problems = 0
    ids = sorted({p.stem for p in folder.glob("*.fontbundle")} | {p.stem for p in folder.glob("*.ttf")})
    for font_id in ids:
        raw, data = read_font(folder / f"{font_id}.fontbundle", folder / f"{font_id}.ttf")
        m = fm.read_vertical_metrics(data)
        fit = fm.fit_line_metrics(data)
        ok = (m["hhea_ascent"], -m["hhea_descent"]) == (fit.ascent, fit.descent) and fm.metrics_are_consistent(m)
        if raw is not None:
            b = ufs.read_bundle(raw)
            off, length = ufs.find_sfnt(b.data)
            floats = ufs.read_unity_floats(b.data, off, length)
            want = ufs.metrics_floats(m["hhea_ascent"], -m["hhea_descent"], m["upm"])
            ok = ok and all(abs(floats[k] - v) < 1e-3 for k, v in want.items())
        print(f"{'ok      ' if ok else 'DIFFERS '} {font_id:26s} in file {m['hhea_ascent']}/{-m['hhea_descent']}  fit {fit.ascent}/{fit.descent}")
        problems += 0 if ok else 1
    return problems


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", nargs="?", type=Path)
    ap.add_argument("dst", nargs="?", type=Path)
    ap.add_argument("--only", help="comma separated font ids")
    ap.add_argument("--check", type=Path, metavar="DIR", help="only compare, change nothing")
    ap.add_argument("--no-ttf", action="store_true", help="do not write .ttf files")
    ap.add_argument("--json", type=Path, help="write the before/after numbers to this file")
    a = ap.parse_args(argv)

    if a.check:
        return 1 if check(a.check) else 0
    if not a.src or not a.dst:
        ap.print_help()
        return 2

    ids = sorted({p.stem for p in a.src.glob("*.fontbundle")} | {p.stem for p in a.src.glob("*.ttf")})
    if a.only:
        wanted = [x.strip() for x in a.only.split(",") if x.strip()]
        missing = [x for x in wanted if x not in ids]
        if missing:
            raise SystemExit("not found in " + str(a.src) + ": " + ", ".join(missing))
        ids = wanted
    records = []
    for font_id in ids:
        rec = retune(font_id, a.src, a.dst, write_ttf=not a.no_ttf)
        records.append(rec)
        b, f = rec["before"], rec["after"]
        print(f"{font_id:26s} {b['ascent']:5d}/{b['descent']:<5d} -> {f['ascent']:5d}/{f['descent']:<5d} "
              f"(per {rec['unitsPerEm']:4d} em)  line height {rec['lineHeightEm']['before']:.3f} -> {rec['lineHeightEm']['after']:.3f} em"
              + ("   " + "; ".join(rec["notes"]) if rec["notes"] else ""))
    if a.json:
        a.json.parent.mkdir(parents=True, exist_ok=True)
        a.json.write_text(json.dumps(records, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
