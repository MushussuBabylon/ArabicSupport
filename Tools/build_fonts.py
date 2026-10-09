#!/usr/bin/env python3
"""Arabic Support - font builder (no Unity Editor needed).

Turns the source fonts into the files the mod loads:

    python Tools/build_fonts.py            FontSources/  -> Fonts/<Name>.fontbundle
    python Tools/build_fonts.py --extra    ExtraFonts/FontSources/ -> ExtraFonts/Fonts/<Name>.fontbundle
    python Tools/build_fonts.py Cairo-Bold (only one font; works with --extra too)
    add --ttf to also write the processed .ttf next to each bundle

Fonts/ is shipped with the mod (Cairo only, to keep the mod small).
ExtraFonts/Fonts/ is NOT part of the mod: players who want one of those fonts
copy its .fontbundle into the mod's Fonts folder and pick it in the settings.

For every font it:
  * makes a static instance at the wanted weight (variable fonts),
  * adds every missing Arabic presentation form (isolated/initial/medial/final
    and lam-alef) using the font's own OpenType substitutions - the
    translation and the mod's typing support use those forms,
  * adds pre-placed copies of every haraka for every letter (U+E400-U+EE6F,
    placed with the font's own anchors) - Unity cannot position harakat, so
    the mod swaps each haraka for the copy made for the letter it sits on,
  * keeps the font's own Latin letters for English text,
  * sets compact line metrics, adds TrueType hinting, and packs the result in
    an AssetBundle using the font-bundle template (see LICENSE-fontbundle-tools.txt).

Usage:   pip install fonttools UnityPy ttfautohint-py

Configuration: <source folder>/fonts.json. Any extra static .ttf you drop into
<source folder>/Arabic is picked up automatically.
"""

import json
import re
import sys
import unicodedata
from io import BytesIO
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._g_l_y_f import Glyph
from fontTools.varLib import instancer
from fontTools import subset

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = Path(__file__).resolve().parent / "fontbundle_template"

if "--extra" in sys.argv:
    SRC = ROOT / "ExtraFonts" / "FontSources"
    OUT = ROOT / "ExtraFonts" / "Fonts"
else:
    SRC = ROOT / "FontSources"
    OUT = ROOT / "Fonts"

ARABIC_RANGES = [(0x0600, 0x06FF), (0x0750, 0x077F), (0x08A0, 0x08FF),
                 (0xFB50, 0xFDFF), (0xFE70, 0xFEFF)]
LATIN_RANGES = [(0x0020, 0x024F), (0x0300, 0x036F), (0x1E00, 0x1EFF),
                (0x2000, 0x206F), (0x20A0, 0x20CF), (0x2100, 0x214F),
                (0x2190, 0x21FF), (0x2200, 0x22FF), (0x25A0, 0x25FF),
                (0x2600, 0x26FF), (0xFFFD, 0xFFFD)]
BASE_RANGES = [(0x0621, 0x064A), (0x0671, 0x06D3), (0xFB50, 0xFBFF), (0xFE80, 0xFEFC)]
# Tall/deep Latin letters used for the line metrics (A-grave, E-acute, A-ring ...).
# Harakat placement (must match Source/Fonts/HarakatMapper.cs).
# Unity cannot position combining marks, so every (letter, haraka) pair gets a
# copy of the haraka already moved onto that letter, at a FIXED code point:
#   HARAKAT_PUA + slot * 16 + k
#   slot = index of the letter in HARAKAT_BASES
#   k    = 0..11  index of the haraka in MARKS
#          12..15 ON_SHADDA[k - 12] drawn on top of a shadda
MARKS = [0x064B, 0x064C, 0x064D, 0x064E, 0x064F, 0x0650, 0x0651, 0x0652,
         0x0670, 0x0653, 0x0654, 0x0655]
SHADDA = 0x0651
ON_SHADDA = [0x064B, 0x064C, 0x064E, 0x064F]
HARAKAT_BASES = list(range(0xFE80, 0xFEFD)) + list(range(0x0621, 0x064B))
HARAKAT_PUA = 0xE400                     # mod placeholder markers use E000-E3FF
HARAKAT_END = HARAKAT_PUA + len(HARAKAT_BASES) * 16 - 1
# Letter each haraka most often sits on in the translation: used for the
# plain haraka characters (text the mod does not process, e.g. text boxes).
DEFAULT_BASE = {0x064B: 0xFE8E, 0x064C: 0xFEAA, 0x064D: 0xFED1, 0x064E: 0xFE92,
                0x064F: 0xFEF3, 0x0650: 0xFEC2, 0x0651: 0xFEE0, 0x0652: 0xFE91,
                0x0670: 0xFEE4, 0x0653: 0xFE8E, 0x0654: 0xFE8E, 0x0655: 0xFE8D}

METRIC_LATIN = "\u00C0\u00C9\u00C5bdhklgjpqy|()[]"


def in_ranges(cp, ranges):
    return any(a <= cp <= b for a, b in ranges)


def log(*a):
    print(*a, flush=True)


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------

def instance(font, axes, weight=None):
    if "fvar" not in font:
        return font
    pins = {}
    for ax in font["fvar"].axes:
        v = axes.get(ax.axisTag, ax.defaultValue)
        if ax.axisTag == "wght" and weight is not None and ax.axisTag not in axes:
            v = weight
        pins[ax.axisTag] = max(ax.minValue, min(ax.maxValue, v))
    return instancer.instantiateVariableFont(font, pins, updateFontNames=False)


def lookups_for(table, tags):
    if table is None:
        return []
    idx = set()
    for fr in table.FeatureList.FeatureRecord:
        if fr.FeatureTag in tags:
            idx.update(fr.Feature.LookupListIndex)
    out = []
    for li in sorted(idx):
        lk = table.LookupList.Lookup[li]
        for st in lk.SubTable:
            t = lk.LookupType
            if t in (7, 9):
                t = st.ExtensionLookupType
                st = st.ExtSubTable
            out.append((li, t, st))
    return out


def all_cmaps(font):
    return [t for t in font["cmap"].tables if t.isUnicode()]


def set_cmap(font, cp, glyph):
    for t in all_cmaps(font):
        if cp > 0xFFFF and t.format == 4:
            continue
        t.cmap[cp] = glyph
    if not any(t.format == 12 for t in font["cmap"].tables) and cp > 0xFFFF:
        pass


def add_glyph(font, name, glyph, advance):
    glyf = font["glyf"]
    glyf[name] = glyph
    glyph.recalcBounds(glyf)
    font["hmtx"][name] = (int(advance), int(getattr(glyph, "xMin", 0) or 0))


def composite(parts):
    """parts: [(glyphName, dx, dy)] -> composite Glyph."""
    from fontTools.ttLib.tables._g_l_y_f import GlyphComponent
    g = Glyph()
    g.numberOfContours = -1
    g.components = []
    for name, dx, dy in parts:
        c = GlyphComponent()
        c.glyphName = name
        c.x, c.y = int(round(dx)), int(round(dy))
        c.flags = 0x4                            # ROUND_XY_TO_GRID
        g.components.append(c)
    return g


def bbox(font, name):
    g = font["glyf"][name]
    if g.isComposite() or not hasattr(g, "xMin"):
        g.recalcBounds(font["glyf"])
    if not hasattr(g, "xMin") or g.numberOfContours == 0:
        return None
    return g.xMin, g.yMin, g.xMax, g.yMax


def mark_anchors(font):
    """GPOS mark-to-base anchors, one (marks, bases) pair per subtable because
    mark classes are numbered separately in each subtable."""
    out = []
    gpos = font["GPOS"].table if "GPOS" in font else None
    subtables = list(lookups_for(gpos, {"mark"}))
    if gpos is not None and gpos.LookupList is not None:     # + lookups only used from contextual rules
        for li, lk in enumerate(gpos.LookupList.Lookup):
            for st in lk.SubTable:
                t = lk.LookupType
                if t == 9:
                    t, st = st.ExtensionLookupType, st.ExtSubTable
                subtables.append((li, t, st))
    for _, t, st in subtables:
        if t != 4 or getattr(st, "Format", 1) != 1:
            continue
        marks, bases = {}, {}
        for g, rec in zip(st.MarkCoverage.glyphs, st.MarkArray.MarkRecord):
            if rec.MarkAnchor is not None:
                marks[g] = (rec.Class, rec.MarkAnchor.XCoordinate, rec.MarkAnchor.YCoordinate)
        for g, rec in zip(st.BaseCoverage.glyphs, st.BaseArray.BaseRecord):
            bases[g] = {c: (a.XCoordinate, a.YCoordinate)
                        for c, a in enumerate(rec.BaseAnchor) if a is not None}
        out.append((marks, bases))
    return out


def anchor_offset(anchors, mark, base):
    for marks, bases in anchors:
        if mark in marks and base in bases:
            c, mx, my = marks[mark]
            if c in bases[base]:
                ax, ay = bases[base][c]
                return ax - mx, ay - my
    return None


def fallback_offset(font, anchors, mark, base):
    """No anchor for this mark on this base: put the mark where the font puts
    it on its other letters (same distance above/below the outline, same
    horizontal shift from the centre)."""
    from statistics import median
    tb = bbox(font, base)
    if tb is None:
        return None
    rel, mark_xy = [], None
    for marks, bases in anchors:
        if mark not in marks:
            continue
        c, mx, my = marks[mark]
        for bg, d in bases.items():
            if c not in d or bg == base:
                continue
            bb = bbox(font, bg)
            if bb is None:
                continue
            ax, ay = d[c]
            above = ay > (bb[1] + bb[3]) / 2
            rel.append((above, ax - (bb[0] + bb[2]) / 2, ay - (bb[3] if above else bb[1]), mx, my))
            if len(rel) >= 60:
                break
    if not rel:
        return None
    above = sum(r[0] for r in rel) * 2 >= len(rel)
    rel = [r for r in rel if r[0] == above]
    ax = (tb[0] + tb[2]) / 2 + median(r[1] for r in rel)
    ay = (tb[3] if above else tb[1]) + median(r[2] for r in rel)
    dx, dy = ax - median(r[3] for r in rel), ay - median(r[4] for r in rel)
    mb = bbox(font, mark)
    if mb is not None:
        top, bottom = mb[3] + dy, mb[1] + dy
        if bottom < tb[3] and top > tb[1]:
            # It would land inside the letter (e.g. jeem's centre dot on an
            # initial/medial hah, which has no bowl): put it under the letter.
            h = mb[3] - mb[1]
            dx = (tb[0] + tb[2]) / 2 - (mb[0] + mb[2]) / 2
            dy = tb[1] - 0.4 * h - mb[3]
    return dx, dy


def is_mark(font, g, anchors):
    gdef = font["GDEF"].table if "GDEF" in font else None
    if gdef is not None and gdef.GlyphClassDef is not None:
        return gdef.GlyphClassDef.classDefs.get(g) == 3
    return any(g in marks for marks, _ in anchors)


def is_empty(font, g):
    """True for placeholder glyphs with no outline (not a space)."""
    gl = font["glyf"][g]
    return not gl.isComposite() and gl.numberOfContours == 0 and font["hmtx"][g][0] > 0 \
        and g not in ("space", "uni0020", "nbspace", "uni00A0")


_serial = [0]


def joined_glyph(font, seq, anchors):
    """One glyph drawing `seq` (logical order): base glyphs right-to-left,
    marks (dots, hamza, madda) attached to the previous base with the font's
    own GPOS anchors, the way a full text engine would draw them."""
    _serial[0] += 1
    name = f"as.joined{_serial[0]}"
    hm = font["hmtx"]
    total = sum(hm[g][0] for g in seq if not is_mark(font, g, anchors))
    parts, x, last = [], total, None
    for g in seq:
        if not is_mark(font, g, anchors) or last is None:
            x -= hm[g][0]
            last = (g, x)
            parts.append((g, x, 0))
            continue
        bg, bx = last
        off = anchor_offset(anchors, g, bg) or fallback_offset(font, anchors, g, bg) or (0, 0)
        parts.append((g, bx + off[0], off[1]))
    add_glyph(font, name, composite(parts), total)
    return name


# --------------------------------------------------------------------------
# Arabic presentation forms
# --------------------------------------------------------------------------

FORM = {"<isolated>": "isol", "<initial>": "init", "<medial>": "medi", "<final>": "fina"}


def fill_presentation_forms(font):
    cmap = font.getBestCmap()
    gsub = font["GSUB"].table if "GSUB" in font else None
    anchors = mark_anchors(font)
    # Fonts that rename or split letters first (alef -> alef.isol,
    # beh -> dotless beh + dot) and apply the joining forms afterwards
    decomp = {}
    for _, t, st in lookups_for(gsub, {"ccmp"}):
        if t == 2:
            for k, seq in st.mapping.items():
                if seq:
                    decomp.setdefault(k, list(seq))
    single = {}

    def from_parts(base, form):
        seq = decomp[base]
        first = single[form].get(seq[0], seq[0] if form == "isol" else None)
        if first is None:
            return None
        return first if len(seq) == 1 else joined_glyph(font, [first] + seq[1:], anchors)

    def form_of(base, form):
        g = single[form].get(base)
        if base in decomp:
            g = from_parts(base, form) or g
        return g

    for tag in ("isol", "init", "medi", "fina"):
        m = {}
        for _, t, st in lookups_for(gsub, {tag}):
            if t == 1:
                for k, v in st.mapping.items():
                    m.setdefault(k, v)
            elif t == 2:                                # some fonts (Mada, Reem Kufi...) use type 2
                for k, seq in st.mapping.items():
                    if len(seq) == 1:
                        m.setdefault(k, seq[0])
                    elif seq and k not in m:
                        m[k] = joined_glyph(font, seq, anchors)
        single[tag] = m
    ligs = {}
    for _, t, st in lookups_for(gsub, {"rlig", "liga", "calt", "ccmp", "dlig"}):
        if t == 4:
            for first, lst in st.ligatures.items():
                for lig in lst:
                    ligs.setdefault((first, tuple(lig.Component)), lig.LigGlyph)

    added = 0
    orig = dict(cmap)                       # letter -> glyph before we change anything
    # The font's own glyph for such a letter is only an input for those
    # substitutions (often empty or with a dummy width): draw it from parts.
    for cp in range(0x0620, 0x0700):
        base = cmap.get(cp)
        if base is None:
            continue
        g = None
        if base in decomp:
            g = from_parts(base, "isol")
        elif is_empty(font, base):
            g = single["isol"].get(base)
        if g is not None and g != base and not is_empty(font, g):
            set_cmap(font, cp, g)
            cmap[cp] = g
    for cp in list(range(0xFB50, 0xFE00)) + list(range(0xFE70, 0xFF00)):
        if cp in cmap and not is_empty(font, cmap[cp]):
            continue
        d = unicodedata.decomposition(chr(cp)).split()
        if len(d) < 2 or d[0] not in FORM:
            continue
        form = FORM[d[0]]
        chars = [int(x, 16) for x in d[1:]]
        if len(chars) == 1:
            base = orig.get(chars[0])
            if base is None:
                continue
            g = single[form].get(base, base if form == "isol" else None)
            if base in decomp:                          # OpenType order: ccmp first, then the forms
                g = from_parts(base, form) or g
            if g is not None and is_empty(font, g):
                g = None
            if g is None:
                continue
            set_cmap(font, cp, g)
            cmap[cp] = g
            added += 1
        elif len(chars) == 2 and chars[0] == 0x0644 and chars[1] in (0x0622, 0x0623, 0x0625, 0x0627):  # lam-alef
            lam, alef = orig.get(0x0644), orig.get(chars[1])
            if lam is None or alef is None:
                continue
            lam_i = cmap.get(0x0644)                    # isolated lam/alef, fixed above
            alef_i = cmap.get(chars[1])
            lam_c = [form_of(lam, "init"), lam_i] if form == "isol" else [form_of(lam, "medi"), form_of(lam, "fina"), lam_i]
            alef_c = [form_of(alef, "fina"), alef_i]
            lam_c = [x for x in lam_c if x and not is_empty(font, x)] or [None]
            alef_c = [x for x in alef_c if x and not is_empty(font, x)] or [None]
            if lam_c[0] is None or alef_c[0] is None:
                continue
            g = None
            for l in lam_c:
                for a in alef_c:
                    if l and a and (l, (a,)) in ligs:
                        g = ligs[(l, (a,))]
                        break
                if g:
                    break
            if g is None:                                           # no ligature: draw the two letters joined
                l = lam_c[0] or lam
                a = alef_c[0] or alef
                adv_a = font["hmtx"][a][0]
                name = f"lamalef.{cp:04X}"
                add_glyph(font, name, composite([(a, 0, 0), (l, adv_a, 0)]), adv_a + font["hmtx"][l][0])
                g = name
            set_cmap(font, cp, g)
            cmap[cp] = g
            added += 1
    return added


# --------------------------------------------------------------------------
# Harakat placement
# --------------------------------------------------------------------------

class Anchors:
    """Every mark anchor in GPOS (mark-to-base, mark-to-ligature,
    mark-to-mark). Keyed per subtable: mark classes are subtable-local."""

    def __init__(self, font):
        self.mark, self.base = {}, {}        # mark -> [(key, cls, x, y)], key -> {glyph: {cls: (x, y)}}
        self.mark1, self.mark2 = {}, {}      # same for mark-on-mark
        gpos = font["GPOS"].table if "GPOS" in font else None
        if gpos is None or gpos.LookupList is None:
            return
        for li, lk in enumerate(gpos.LookupList.Lookup):
            for si, st in enumerate(lk.SubTable):
                t = lk.LookupType
                if t == 9:
                    t, st = st.ExtensionLookupType, st.ExtSubTable
                key = (li, si)
                if getattr(st, "Format", 1) != 1:
                    continue
                if t == 4:
                    self._marks(key, st.MarkCoverage.glyphs, st.MarkArray, self.mark)
                    self._bases(key, st.BaseCoverage.glyphs,
                                [r.BaseAnchor for r in st.BaseArray.BaseRecord], self.base)
                elif t == 5:
                    recs = []
                    for la in st.LigatureArray.LigatureAttach:
                        comps = [c.LigatureAnchor for c in la.ComponentRecord]
                        pick = [None] * st.ClassCount
                        for c in range(st.ClassCount):        # a haraka on lam-alef belongs to the alef (last)
                            for comp in reversed(comps):
                                if comp[c] is not None:
                                    pick[c] = comp[c]
                                    break
                        recs.append(pick)
                    self._marks(key, st.MarkCoverage.glyphs, st.MarkArray, self.mark)
                    self._bases(key, st.LigatureCoverage.glyphs, recs, self.base)
                elif t == 6:
                    self._marks(key, st.Mark1Coverage.glyphs, st.Mark1Array, self.mark1)
                    self._bases(key, st.Mark2Coverage.glyphs,
                                [r.Mark2Anchor for r in st.Mark2Array.Mark2Record], self.mark2)

    @staticmethod
    def _marks(key, glyphs, array, out):
        for g, rec in zip(glyphs, array.MarkRecord):
            if rec.MarkAnchor is not None:
                out.setdefault(g, []).append((key, rec.Class, rec.MarkAnchor.XCoordinate, rec.MarkAnchor.YCoordinate))

    @staticmethod
    def _bases(key, glyphs, anchor_lists, out):
        d = out.setdefault(key, {})
        for g, anchors in zip(glyphs, anchor_lists):
            e = d.setdefault(g, {})
            for c, a in enumerate(anchors):
                if a is not None:
                    e.setdefault(c, (a.XCoordinate, a.YCoordinate))

    @staticmethod
    def _find(marks, bases, mark, base):
        for key, cls, mx, my in marks.get(mark, []):
            p = bases.get(key, {}).get(base, {}).get(cls)
            if p is not None:
                return p[0] - mx, p[1] - my
        return None

    def offset(self, mark, base):
        return self._find(self.mark, self.base, mark, base)

    def on_mark(self, mark, mark2):
        return self._find(self.mark1, self.mark2, mark, mark2)


def build_harakat(font):
    """Adds the pre-placed harakat copies (see HARAKAT_PUA). Returns how many
    distinct copies were needed."""
    cmap = font.getBestCmap()
    upm = font["head"].unitsPerEm
    anchors = Anchors(font)
    marks = {cp: cmap[cp] for cp in MARKS if cp in cmap}
    if not marks:
        return 0
    mb = {cp: bbox(font, g) for cp, g in marks.items()}
    q = max(1, upm // 200)                                  # merge copies closer than 0.5% em

    def fallback(mcp, bcp):
        b, m = bbox(font, cmap[bcp]), mb[mcp]
        if b is None or m is None:
            return None
        bx, mx = (b[0] + b[2]) / 2, (m[0] + m[2]) / 2
        gap = 0.07 * upm
        if (m[1] + m[3]) / 2 > 0.2 * upm:                     # haraka above
            return bx - mx, max(0, b[3] + gap - m[1])
        return bx - mx, min(0, b[1] - gap - m[3])

    def shift(mcp, bcp):
        if bcp not in cmap:
            return None
        o = anchors.offset(marks[mcp], cmap[bcp])
        return o if o is not None else fallback(mcp, bcp)

    variants = {}                                           # (mark, dx, dy) -> glyph name
    codes = {}                                              # code point -> glyph name

    def want(mcp, dx, dy):
        k = (mcp, int(round(dx / q) * q), int(round(dy / q) * q))
        if k[1] == 0 and k[2] == 0:
            return marks[mcp]
        if k not in variants:
            name = f"as.mk{mcp:04X}.{k[1]}.{k[2]}".replace("-", "m")
            add_glyph(font, name, composite([(marks[mcp], k[1], k[2])]), 0)
            variants[k] = name
        return variants[k]

    for slot, bcp in enumerate(HARAKAT_BASES):
        sh = shift(SHADDA, bcp) if SHADDA in marks else None
        for i, mcp in enumerate(MARKS):
            if mcp not in marks:
                continue
            code = HARAKAT_PUA + slot * 16 + i
            s = shift(mcp, bcp)
            codes[code] = want(mcp, *s) if s is not None else marks[mcp]
            if mcp in ON_SHADDA:
                code2 = HARAKAT_PUA + slot * 16 + 12 + ON_SHADDA.index(mcp)
                rel = anchors.on_mark(marks[mcp], marks[SHADDA]) if SHADDA in marks else None
                if rel is None and SHADDA in marks:
                    m, sb = mb[mcp], mb[SHADDA]
                    if m and sb:
                        rel = ((sb[0] + sb[2]) / 2 - (m[0] + m[2]) / 2, sb[3] + 0.04 * upm - m[1])
                if sh is not None and rel is not None:
                    codes[code2] = want(mcp, sh[0] + rel[0], sh[1] + rel[1])
                else:
                    codes[code2] = codes[code]

    defaults = {}
    for mcp in marks:
        b = DEFAULT_BASE.get(mcp)
        if b in HARAKAT_BASES:
            defaults[mcp] = codes.get(HARAKAT_PUA + HARAKAT_BASES.index(b) * 16 + MARKS.index(mcp))
    for code, g in codes.items():
        set_cmap(font, code, g)
    for mcp, g in marks.items():                            # harakat never take up width
        font["hmtx"][g] = (0, font["hmtx"][g][1])
    for mcp, g in defaults.items():
        if g:
            set_cmap(font, mcp, g)
    return len(variants)


# --------------------------------------------------------------------------
# finishing
# --------------------------------------------------------------------------

def do_subset(font):
    ranges = ARABIC_RANGES + LATIN_RANGES + [(HARAKAT_PUA, HARAKAT_END)]
    unicodes = [cp for cp in font.getBestCmap() if in_ranges(cp, ranges)]
    opt = subset.Options()
    opt.layout_features = []
    opt.drop_tables += ["GSUB", "GPOS", "GDEF", "BASE", "JSTF", "STAT", "MVAR", "HVAR", "VVAR", "avar", "kern", "DSIG", "meta"]
    opt.hinting = False
    opt.name_IDs = ["*"]
    opt.notdef_outline = True
    opt.glyph_names = False
    opt.recalc_bounds = True
    s = subset.Subsetter(opt)
    s.populate(unicodes=unicodes)
    s.subset(font)


def percentile(vals, p):
    vals = sorted(vals)
    if not vals:
        return 0
    return vals[min(len(vals) - 1, int(len(vals) * p))]


def set_metrics(font):
    cmap = font.getBestCmap()
    glyf = font["glyf"]
    ys_max, ys_min, latin = [], [], []
    for cp, g in cmap.items():
        b = bbox(font, g)
        if not b:
            continue
        if in_ranges(cp, BASE_RANGES):
            ys_max.append(b[3]); ys_min.append(b[1])
        elif cp in (ord(c) for c in METRIC_LATIN):
            latin.append(b)
    upm = font["head"].unitsPerEm
    asc = max(percentile(ys_max, 0.98), max((b[3] for b in latin), default=0)) + upm * 0.06
    desc = max(-percentile(ys_min, 0.02), max((-b[1] for b in latin), default=0)) + upm * 0.04
    asc, desc = int(round(asc)), int(round(desc))
    hh = font["hhea"]
    hh.ascent, hh.descent, hh.lineGap = asc, -desc, 0
    os2 = font["OS/2"]
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = asc, -desc, 0
    os2.usWinAscent, os2.usWinDescent = asc, desc
    if os2.version < 4:
        os2.version = 4
    os2.fsSelection |= 1 << 7
    return asc, desc


def rename(font, family, version_note):
    name = font["name"]
    keep = {0, 7, 8, 9, 11, 12, 13, 14}
    name.names = [n for n in name.names if n.nameID in keep]
    ps = re.sub(r"[^A-Za-z0-9-]", "", family)[:60]
    for nid, val in ((1, family), (2, "Regular"), (3, f"{ps};ArabicSupport"), (4, family),
                     (5, version_note), (6, ps)):
        name.setName(val, nid, 3, 1, 0x409)
    if "post" in font:
        font["post"].formatType = 3.0
        font["post"].extraNames = []
        font["post"].mapping = {}


def autohint(data):
    try:
        import ttfautohint
        return ttfautohint.ttfautohint(in_buffer=data, hinting_range_max=40, default_script="arab",
                                       fallback_script="latn", hint_composites=True, no_info=True,
                                       increase_x_height=0, windows_compatibility=True), True
    except Exception as e:                       # hinting is a bonus, never a failure
        log(f"   (hinting skipped: {e})")
        return data, False


def make_bundle(data, name, family, upm, asc, desc):
    import UnityPy
    env = UnityPy.load(str(TEMPLATE))
    bundle = next(iter(env.files.values()))
    font_obj = next(o for o in env.objects if o.type.name == "Font")
    ab_obj = next(o for o in env.objects if o.type.name == "AssetBundle")
    f = font_obj.read()
    f.m_FontData = list(data)
    f.m_FontNames = [family]
    f.m_Name = name
    f.m_FontSize = 16.0
    f.m_Ascent = 16.0 * asc / upm
    f.m_Descent = -16.0 * desc / upm
    f.m_LineSpacing = 16.0 * (asc + desc) / upm
    # Leftovers from the template's original font must not leak into ours.
    for attr in ("m_KerningValues", "m_FallbackFonts", "m_CharacterRects"):
        if hasattr(f, attr):
            setattr(f, attr, [])
    if hasattr(f, "m_FontRenderingMode"):
        f.m_FontRenderingMode = 1                # HintedSmooth: crisper small text
    f.save()
    ab = ab_obj.read()
    ab.m_Name = name
    ab.m_AssetBundleName = name
    ab.m_Container = [(f"assets/fonts/{name.lower()}.ttf", info) for _, info in ab.m_Container]
    ab.save()
    inner = f"CAB-arabicsupport-{name.lower()}"
    sf = next(iter(bundle.files.values()))
    sf.name = inner
    bundle.files = {inner: sf}
    return bundle.save(packer="lz4")


# --------------------------------------------------------------------------

def load_config():
    cfg_path = SRC / "fonts.json"
    cfg = json.loads(cfg_path.read_text(encoding="utf-8")) if cfg_path.exists() else {}
    fonts = cfg.get("arabic", [])
    known = {e["file"] for e in fonts}
    for p in sorted((SRC / "Arabic").glob("*.[ot]tf")):
        rel = f"Arabic/{p.name}"
        if rel not in known and "[" not in p.name:
            fonts.append({"id": re.sub(r"[^A-Za-z0-9-]+", "-", p.stem), "file": rel})
    return fonts


def weight_of(font, axes):
    if "wght" in axes:
        return axes["wght"]
    return font["OS/2"].usWeightClass


def build(entry):
    font = TTFont(str(SRC / entry["file"]))
    axes = entry.get("axes", {})
    w = weight_of(font, axes)
    font = instance(font, axes)
    if "glyf" not in font:
        raise SystemExit(f"{entry['file']}: only TrueType (glyf) fonts are supported")
    added = fill_presentation_forms(font)
    marks = build_harakat(font)
    # Unity's font renderer never reads these; the presentation forms are in
    # the cmap now. Dropping them keeps bundles small and avoids GPOS overflow.
    for tag in ("GSUB", "GPOS", "GDEF", "JSTF", "BASE", "vhea", "vmtx", "VORG"):
        if tag in font:
            del font[tag]
    # Save and reload so the glyphs added above get proper glyph IDs
    # before subsetting.
    buf = BytesIO()
    font.save(buf)
    font = TTFont(BytesIO(buf.getvalue()))
    do_subset(font)
    asc, desc = set_metrics(font)
    fam = f"AS {entry['id']}"
    rename(font, fam, f"Version 1.0; Arabic Support build of {entry['file']}")
    buf = BytesIO()
    font.save(buf)
    data, hinted = autohint(buf.getvalue())
    name = entry["id"]
    bundle = make_bundle(data, name, fam, font["head"].unitsPerEm, asc, desc)
    (OUT / f"{name}.fontbundle").write_bytes(bundle)
    log(f"BUILT {name}.fontbundle  weight {w}, +{added} presentation forms, "
        f"{marks} harakat copies, {len(bundle)//1024} KB, asc {asc} desc {desc}{'  hinted' if hinted else ''}")
    if "--ttf" in sys.argv:
        (OUT / f"{name}.ttf").write_bytes(data)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    only = args[0] if args else None
    fonts = load_config()
    if not fonts:
        raise SystemExit(f"No fonts configured in {SRC}")
    OUT.mkdir(parents=True, exist_ok=True)
    failed = []
    for entry in fonts:
        if only and entry["id"] != only:
            continue
        try:
            build(entry)
        except Exception as e:                  # keep building the other fonts
            print(f"FAILED {entry['id']}: {e}")
            failed.append(entry["id"])
    if failed:
        raise SystemExit("Failed fonts: " + ", ".join(failed))


if __name__ == "__main__":
    main()
