#!/usr/bin/env python3
"""Arabic Support - vertical line metrics measured from the ink of a font.

WHY THIS EXISTS
---------------
Unity (RimWorld 1.5 / 1.6) and the 1.7 engine size a line of text from three numbers stored in the font:
ascent (baseline to the top of the line box), descent (baseline to the bottom) and line gap. A label is
exactly one such box high. Ink that leaves the box is cut off, and the baseline sits `ascent` below
the top of the box, so a box that is too tall above the letters makes the text sit too low.

The first version of build_fonts.py chose the numbers from percentiles of the glyph outlines (the
98th percentile of the tops, the 2nd of the bottoms, plus a fixed margin). That leaves out the deepest
2 % of the Arabic glyphs, which are exactly the tails of yeh and noon that every Arabic sentence
contains, and it adds the same fixed margin above and below whatever the font looks like. The
result was a descent shorter than the real ink for 16 of the 34 bold fonts the project had (tails and lower
dots cut off), an ascent shorter than the ink for one more, and, for Mada and Mirza, a box more than 0.12 em
taller than the letters above (text too low).

WHAT THIS MODULE DOES INSTEAD
-----------------------------
1. Measures the real extremes of the glyphs the mod actually draws (Latin + Arabic letters, digits and
   punctuation, and every shaped presentation form) from the outlines.
2. Rasterises those glyphs with FreeType, hinted, at the three RimWorld font sizes (11 / 14 / 20 px) and
   at every other size from 9 to 32 px, and counts how many pixel rows really are above / below the
   baseline.
3. Takes the smallest ascent and descent for which nothing is cut off at the three RimWorld sizes
   under every plausible rounding rule of the engine's line box (see BOX MODELS), and widens the box a
   little more while that is cheap, so that nearby sizes (the mod's size sliders) stay clean too.

Every font gets its own numbers: nothing is copied from one font to another.

Pre-placed harakat (diacritics, U+E400-U+EE6F) are NOT part of the box. They are placed on top of / below
letters and the tallest stacks reach 1.3-1.6 em above the baseline: a box that held all of them would make every
line of the UI 23-43 % taller (0.33-0.58 em, for the ten mod fonts). They are measured and reported (`report`),
not fitted.

BOX MODELS
----------
How the engine turns the three numbers into pixel rows is not documented for the 1.7 engine, and for Unity
it is the FreeType rule, which also depends on the font's head.flags. All of these are accepted, the
box must be big enough for the smallest:
    ascent rows   = ceil(a) or round(a)                      a = ascent * size / unitsPerEm
    descent rows  = ceil(d) or round(d)                      d = descent * size / unitsPerEm
    below baseline = round(a + d) - ceil(a)                  (line height rounded on its own)
Because round(x) <= ceil(x) the requirement is simply
    round(a) >= rows above,  round(d) >= rows below,  round(a + d) - ceil(a) >= rows below.

Needs: fontTools. Pillow (FreeType) is used for the hinted measurement; without it only the outline
estimate plus a fixed safety margin is possible.

    python Tools/fontmetrics.py measure Fonts/ElMessiri-Bold.ttf     print what would be set (changes nothing)
    python Tools/fontmetrics.py report  Fonts/*.ttf                  clipping report at 8-32 px
"""

import math
import struct
import sys
import unicodedata
from dataclasses import dataclass, field
from io import BytesIO
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

from fontTools.ttLib import TTFont

try:
    from PIL import Image, ImageDraw, ImageFont
    HAVE_PIL = True
except ImportError:                                  # outline-only fallback, see fit_line_metrics
    HAVE_PIL = False

# --------------------------------------------------------------------------
# Settings (all in one place so the documentation can quote them)

PRIMARY_SIZES = (11, 14, 20)                         # must-have sizes: GameFont.Tiny / Small / Medium (FontManager.DefaultSizes)
NEIGHBOUR_SIZES = (9, 10, 12, 13, 15, 16, 18, 19, 21, 22)   # one or two pixels off: what the size sliders are used for
REPORT_SIZES = tuple(range(8, 33))                   # sliders reach -6 ... +12 px
MIN_COVERAGE = 77                                    # of 255 (30 %): a pixel row only counts as ink from this much coverage up
SAFETY_PX = 0.03                                     # FreeType scales in 26.6 fixed point; stay clear of an exact .5 / integer
FLOOR_EM = 0.005                                     # never closer than this to the outline extremes
# Extra height that may be spent on the "wishes" (nearby sizes, optional characters) on top of what the three RimWorld
# sizes need. A wish is only granted if it fits in the budget, so a font that needs nothing extra gets nothing extra.
# 0.025 / 0.035 em is about a third / a half of a pixel at 14 px and was chosen by trying 0.012 / 0.020 (the first
# version), 0.025 / 0.035 and 0.030 / 0.045 on all ten fonts: it removes more than half of the cut-offs that remain
# at sizes 8-32 px (87 -> 40 glyph cut-offs in total under the FreeType rule, none at 11 / 14 / 20 px) for 0.015 em
# more line height on average; the next step costs 0.007 em more for 5 further cut-offs.
# The must-have sizes are 11 / 14 / 20 px. RimWorld 1.6 itself draws 12 / 14 / 21 px (the mod settings show them), so
# 12 and 21 are only checked as neighbours. Making them must-haves too would cost +0.13 em for Noto Kufi Arabic Bold
# and Noto Sans Arabic Bold, +0.05 em for Vazirmatn Bold, +0.01 em for IBM Plex Sans Arabic Bold and nothing for the
# other six. To do it, set PRIMARY_SIZES to (11, 12, 14, 20, 21), take 12 and 21 out of NEIGHBOUR_SIZES and run
# Tools/retune_font_metrics.py. (What is cut at the bottom of a label is a separate matter: Patches/LabelRoom.cs.)
TOP_BUDGET_EM = 0.025                                # extra height above the baseline
BOTTOM_BUDGET_EM = 0.035                             # ... and below it
MAX_ASCENT_EM, MAX_DESCENT_EM = 1.35, 0.85           # sanity limits (a box beyond these means a broken font)
FALLBACK_TOP_EM, FALLBACK_BOTTOM_EM = 0.03, 0.06     # margin above / below the outline extremes when Pillow is missing

# Character groups, in priority order: a code point belongs to the first group that lists it.
# REQUIRED groups must be inside the box at the three RimWorld sizes. OPTIONAL groups are added
# as long as they stay within the budget. REPORT groups are only measured.
REQUIRED_RANGES = {
    "latin": [(0x0020, 0x007E), (0x00A0, 0x00FF)],
    "arabic": [(0x060C, 0x060C), (0x061B, 0x061B), (0x061F, 0x061F), (0x0621, 0x064A),
               (0x0660, 0x0669), (0xFE80, 0xFEFC)],
}
PERSIAN_URDU_LETTERS = [0x0679, 0x067E, 0x0686, 0x0688, 0x0691, 0x0698, 0x06A9, 0x06AF, 0x06BA, 0x06BE,
                        0x06C1, 0x06C3, 0x06CC, 0x06D2, 0x06D3]
OPTIONAL_RANGES = {
    "latin_extended_a": [(0x0100, 0x017F)],
    "symbols": [(0x2000, 0x206F), (0x20A0, 0x20CF), (0x2100, 0x214F), (0x2190, 0x21FF),
                (0x2200, 0x22FF), (0x25A0, 0x25FF), (0x2600, 0x26FF)],
    "persian_urdu": [(0x06F0, 0x06F9)],               # + PERSIAN_URDU_LETTERS and their presentation forms
}
REPORT_RANGES = {
    "latin_extended_b": [(0x0180, 0x024F)],
    "harakat": [(0x064B, 0x0655), (0x0670, 0x0670), (0xE400, 0xEE6F)],
    "arabic_other": [(0x0600, 0x06FF), (0x0750, 0x077F), (0x08A0, 0x08FF), (0xFB50, 0xFDFF), (0xFE70, 0xFEFF)],
}
HARAKAT_RANGES = REPORT_RANGES["harakat"]
GROUP_ORDER = list(REQUIRED_RANGES) + list(OPTIONAL_RANGES) + list(REPORT_RANGES)


def _in(cp: int, ranges: Iterable[Tuple[int, int]]) -> bool:
    return any(a <= cp <= b for a, b in ranges)


def _persian_urdu_forms() -> set:
    """The letters above and every presentation form (U+FB50-FDFF, FE70-FEFF) that is a form of one of them."""
    out = set(PERSIAN_URDU_LETTERS)
    for cp in list(range(0xFB50, 0xFE00)) + list(range(0xFE70, 0xFF00)):
        d = unicodedata.decomposition(chr(cp)).split()
        if len(d) >= 2 and d[0].startswith("<") and int(d[1], 16) in PERSIAN_URDU_LETTERS:
            out.add(cp)
    return out


_PERSIAN_URDU = _persian_urdu_forms()


def group_of(cp: int) -> Optional[str]:
    for name, ranges in REQUIRED_RANGES.items():
        if _in(cp, ranges):
            return name
    for name, ranges in OPTIONAL_RANGES.items():
        if _in(cp, ranges) or (name == "persian_urdu" and cp in _PERSIAN_URDU):
            return name
    if _in(cp, HARAKAT_RANGES):
        return "harakat"
    for name, ranges in REPORT_RANGES.items():
        if _in(cp, ranges):
            return name
    return None


def groups_of_font(cmap: Dict[int, str]) -> Dict[str, List[int]]:
    out: Dict[str, List[int]] = {g: [] for g in GROUP_ORDER}
    for cp in sorted(cmap):
        g = group_of(cp)
        if g:
            out[g].append(cp)
    return out


# --------------------------------------------------------------------------
# Reading / patching the vertical metrics in the font file itself

def _table_directory(data: bytes) -> Dict[str, Tuple[int, int, int]]:
    """tag -> (offset of the directory entry, table offset, table length)."""
    if data[:4] not in (b"\x00\x01\x00\x00", b"true", b"OTTO"):
        raise ValueError("not a TrueType/OpenType font")
    n = struct.unpack_from(">H", data, 4)[0]
    out = {}
    for i in range(n):
        e = 12 + 16 * i
        tag = data[e:e + 4].decode("latin1")
        _, off, length = struct.unpack_from(">III", data, e + 4)
        out[tag] = (e, off, length)
    return out


def _checksum(blob: bytes) -> int:
    blob = blob + b"\0" * (-len(blob) % 4)
    return sum(struct.unpack(">%dI" % (len(blob) // 4), blob)) & 0xFFFFFFFF


def read_vertical_metrics(data: bytes) -> dict:
    """The numbers every engine might read: hhea, OS/2 typo, OS/2 win, flags and head.flags."""
    t = _table_directory(data)
    _, ho, _ = t["hhea"]
    _, oo, ol = t["OS/2"]
    _, he, _ = t["head"]
    out = {
        "upm": struct.unpack_from(">H", data, he + 18)[0],
        "head_flags": struct.unpack_from(">H", data, he + 16)[0],
        "hhea_ascent": struct.unpack_from(">h", data, ho + 4)[0],
        "hhea_descent": struct.unpack_from(">h", data, ho + 6)[0],
        "hhea_linegap": struct.unpack_from(">h", data, ho + 8)[0],
        "os2_version": struct.unpack_from(">H", data, oo)[0],
        "fs_selection": struct.unpack_from(">H", data, oo + 62)[0],
    }
    if ol >= 78:
        out.update({
            "typo_ascender": struct.unpack_from(">h", data, oo + 68)[0],
            "typo_descender": struct.unpack_from(">h", data, oo + 70)[0],
            "typo_linegap": struct.unpack_from(">h", data, oo + 72)[0],
            "win_ascent": struct.unpack_from(">H", data, oo + 74)[0],
            "win_descent": struct.unpack_from(">H", data, oo + 76)[0],
        })
    return out


def metrics_are_consistent(m: dict) -> bool:
    """hhea, typo and win agree (what the builder always writes) and the line gap is 0."""
    return (m["hhea_ascent"] == m["typo_ascender"] == m["win_ascent"]
            and -m["hhea_descent"] == -m["typo_descender"] == m["win_descent"]
            and m["hhea_linegap"] == 0 and m["typo_linegap"] == 0)


def patch_vertical_metrics(data: bytes, ascent: int, descent: int) -> bytes:
    """Return the font with ascent / descent written to hhea, OS/2 typo and OS/2 win (line gap 0, USE_TYPO_METRICS on).

    Only fixed-width fields inside hhea, OS/2 and head change, so the file has exactly the same length
    and every other table (glyphs, hinting programs, names ...) stays byte-for-byte identical. The table
    checksums and head.checkSumAdjustment are recomputed.
    """
    if not (0 < ascent <= 32767 and 0 < descent <= 32767):
        raise ValueError("ascent / descent out of range")
    t = _table_directory(data)
    for tag in ("hhea", "OS/2", "head"):
        if tag not in t:
            raise ValueError(f"font has no {tag} table")
    buf = bytearray(data)
    _, ho, hl = t["hhea"]
    _, oo, ol = t["OS/2"]
    if hl < 10 or ol < 78:
        raise ValueError("hhea / OS/2 table too short")
    struct.pack_into(">hhh", buf, ho + 4, ascent, -descent, 0)
    struct.pack_into(">hhh", buf, oo + 68, ascent, -descent, 0)
    struct.pack_into(">HH", buf, oo + 74, ascent, descent)
    version = struct.unpack_from(">H", buf, oo)[0]
    if version < 4 and ol >= 96:                            # USE_TYPO_METRICS is defined from OS/2 version 4 on
        struct.pack_into(">H", buf, oo, 4)
        version = 4
    if version >= 4:
        fs = struct.unpack_from(">H", buf, oo + 62)[0]
        struct.pack_into(">H", buf, oo + 62, fs | (1 << 7))

    # checksums: patched tables first, then head.checkSumAdjustment for the whole file
    _, he, hdl = t["head"]
    struct.pack_into(">I", buf, he + 8, 0)
    for tag in ("hhea", "OS/2", "head"):
        entry, off, length = t[tag]
        struct.pack_into(">I", buf, entry + 4, _checksum(bytes(buf[off:off + length])))
    adjust = (0xB1B0AFBA - _checksum(bytes(buf))) & 0xFFFFFFFF
    struct.pack_into(">I", buf, he + 8, adjust)
    return bytes(buf)


def apply_to_font(font: TTFont, ascent: int, descent: int) -> None:
    """The same change on an in-memory fontTools font (used by build_fonts.py before hinting)."""
    hh, os2 = font["hhea"], font["OS/2"]
    hh.ascent, hh.descent, hh.lineGap = ascent, -descent, 0
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = ascent, -descent, 0
    os2.usWinAscent, os2.usWinDescent = ascent, descent
    if os2.version < 4:
        os2.version = 4
    os2.fsSelection |= 1 << 7


# --------------------------------------------------------------------------
# Outline measurement

@dataclass
class Extreme:
    top: int = 0              # highest ink, font units above the baseline
    top_cp: int = 0
    bottom: int = 0           # deepest ink, font units below the baseline (positive)
    bottom_cp: int = 0
    count: int = 0            # glyphs with ink


def measure_outlines(font: TTFont, groups: Optional[Dict[str, List[int]]] = None) -> Dict[str, Extreme]:
    """Per group: the highest and deepest outline point (unhinted), with the code point that sets it."""
    cmap = font.getBestCmap()
    glyf = font["glyf"]
    groups = groups or groups_of_font(cmap)
    cache: Dict[str, Optional[Tuple[int, int]]] = {}
    out: Dict[str, Extreme] = {}
    for name, cps in groups.items():
        e = Extreme(top=-10 ** 9, bottom=-10 ** 9)
        for cp in cps:
            g = cmap[cp]
            if g not in cache:
                gl = glyf[g]
                if gl.numberOfContours == 0:
                    cache[g] = None
                else:
                    gl.recalcBounds(glyf)
                    cache[g] = (gl.yMax, -gl.yMin)
            b = cache[g]
            if b is None:
                continue
            e.count += 1
            if b[0] > e.top:
                e.top, e.top_cp = b[0], cp
            if b[1] > e.bottom:
                e.bottom, e.bottom_cp = b[1], cp
        if e.count == 0:
            e = Extreme()
        out[name] = e
    return out


# --------------------------------------------------------------------------
# Hinted measurement (FreeType through Pillow)

class Probe:
    """Pixel rows of glyphs at one size, hinted. rows = (above, below) the baseline.

    `rows()` is the bounding box FreeType reports (any coverage at all); `visible()` only counts a pixel
    row when at least MIN_COVERAGE/255 of it is covered, so a 1 % sliver of paint at the edge of
    an outline does not count as ink.
    """

    def __init__(self, data: bytes, size: int):
        if not HAVE_PIL:
            raise RuntimeError("Pillow is needed for the hinted measurement")
        self.size = size
        self.font = ImageFont.truetype(BytesIO(data), size, layout_engine=ImageFont.Layout.BASIC)
        self._rows: Dict[int, Optional[Tuple[int, int]]] = {}
        self._vis: Dict[int, Optional[Tuple[int, int]]] = {}

    def rows(self, cp: int) -> Optional[Tuple[int, int]]:
        if cp not in self._rows:
            l, t, r, b = self.font.getbbox(chr(cp), anchor="ls")
            self._rows[cp] = (-t, b) if (r - l > 0 and b - t > 0) else None
        return self._rows[cp]

    def visible(self, cp: int) -> Optional[Tuple[int, int]]:
        if cp not in self._vis:
            r = self.rows(cp)
            if r is None:
                self._vis[cp] = None
            else:
                above, below = r
                left, _, right, _ = self.font.getbbox(chr(cp), anchor="ls")
                img = Image.new("L", (max(1, right - left) + 4, above + below + 4), 0)
                ImageDraw.Draw(img).text((2 - left, 2 + above), chr(cp), font=self.font, fill=255, anchor="ls")
                mask = img.point(lambda v: 255 if v >= MIN_COVERAGE else 0)
                bb = mask.getbbox()
                if bb is None:
                    self._vis[cp] = None
                else:
                    base = 2 + above                     # row index of the baseline
                    self._vis[cp] = (max(0, base - bb[1]), max(0, bb[3] - base))
        return self._vis[cp]

    def extremes(self, cps: Sequence[int]) -> Tuple[int, int, int, int]:
        """(rows above, cp, rows below, cp) of the group, counted on visible ink only."""
        order = sorted((cp for cp in cps if self.rows(cp)), key=lambda c: -self.rows(c)[0])
        top, top_cp = 0, 0
        for cp in order:
            if self.rows(cp)[0] <= top:
                break
            v = self.visible(cp)
            if v and v[0] > top:
                top, top_cp = v[0], cp
        order = sorted((cp for cp in cps if self.rows(cp)), key=lambda c: -self.rows(c)[1])
        bot, bot_cp = 0, 0
        for cp in order:
            if self.rows(cp)[1] <= bot:
                break
            v = self.visible(cp)
            if v and v[1] > bot:
                bot, bot_cp = v[1], cp
        return top, top_cp, bot, bot_cp


def _round(x: float) -> int:
    """FreeType's FT_PIX_ROUND: to the nearest pixel, halves up."""
    return int(math.floor(x + 0.5))


def _ceil(x: float) -> int:
    return int(math.ceil(x - 1e-9))


def uses_rounding(head_flags: int) -> bool:
    """head.flags bit 3 ("force ppem to integer"): FreeType then rounds the line box to the nearest pixel instead of
    rounding the ascent up and the descent down."""
    return bool(head_flags & 8)


def rooms(asc: int, desc: int, upm: int, size: int, ft_round: bool) -> dict:
    """Pixel rows above / below the baseline that a line box really offers, for each way an engine can round it.

        must    = how FreeType builds the box for this font (the engine of RimWorld 1.5 / 1.6): ascent rows
                  A(a), descent rows A(d) with A = ceil, or round when head.flags bit 3 is set; and the
                  variant where the line height is rounded on its own, so the rows below the baseline are
                  round(a + d) - A(a). The smaller of the two descent values counts.
        strict  = the smallest of every box model that is plausible for another engine: additionally
                  everything rounded to the nearest pixel, and round(a + d) - ceil(a).
    where a = ascent * size / unitsPerEm and d = descent * size / unitsPerEm.
    """
    a, d = asc * size / upm, desc * size / upm
    A = _round if ft_round else _ceil
    above, below_a, below_b = A(a), A(d), _round(a + d) - A(a)
    must = (above, min(below_a, below_b))
    strict = (min(above, _round(a)),
              min(below_a, below_b, _round(d), _round(a + d) - _ceil(a)))
    return {"must": must, "strict": strict}


def freetype_box(data: bytes, size: int) -> Tuple[int, int]:
    """(ascent rows, descent rows) as the FreeType inside Pillow reports them for this exact file at this size."""
    f = ImageFont.truetype(BytesIO(data), size, layout_engine=ImageFont.Layout.BASIC)
    a, d = f.getmetrics()
    return a, d


# --------------------------------------------------------------------------
# Fitting

@dataclass
class Fit:
    ascent: int
    descent: int
    upm: int
    before: Tuple[int, int]
    outline: Dict[str, Extreme]
    method: str                                # "hinted" or "outline"
    ft_round: bool = False
    hinted_rows: Dict[int, Tuple[int, int, int, int]] = field(default_factory=dict)   # size -> (T1, B1, T12, B12)
    must: Tuple[int, int] = (0, 0)             # smallest box that satisfies the must-haves
    drivers: Dict[str, str] = field(default_factory=dict)
    notes: List[str] = field(default_factory=list)

    @property
    def line_height_em(self) -> float:
        return (self.ascent + self.descent) / self.upm


def _units(px_value: float, size: int, upm: int) -> float:
    return px_value * upm / size


def outline_metrics(font: TTFont) -> Tuple[int, int]:
    """Ascent and descent from the outlines alone (no hinting): the extremes of the required characters plus a margin.

    build_fonts.py uses it for the font it saves before hinting; fit_line_metrics replaces it by the hinted fit.
    """
    upm = font["head"].unitsPerEm
    groups = groups_of_font(font.getBestCmap())
    ink = measure_outlines(font, {g: groups[g] for g in REQUIRED_RANGES})
    top = max(e.top for e in ink.values())
    bottom = max(e.bottom for e in ink.values())
    return top + int(round(FALLBACK_TOP_EM * upm)), bottom + int(round(FALLBACK_BOTTOM_EM * upm))


def fit_line_metrics(data: bytes) -> Fit:
    """Choose ascent and descent for this font file (a hinted TrueType font as the mod ships it).

    MUST-HAVES (always satisfied): every character of the REQUIRED groups, at the three RimWorld sizes,
    has all of its visible rows inside the box, both under the rule FreeType uses (see `rooms`, "must") and
    under the strictest rounding of any plausible engine ("strict"), and the box reaches the unhinted outline
    extremes (so unhinted rendering and every other size are covered too).
    WISHES (taken as far as the small budgets allow, counted from the FreeType-rule must-have): the strictest
    box model at every size, the sizes one or two pixels away from the defaults, and the OPTIONAL groups.
    """
    font = TTFont(BytesIO(data))
    upm = font["head"].unitsPerEm
    ft_round = uses_rounding(font["head"].flags)
    cmap = font.getBestCmap()
    groups = groups_of_font(cmap)
    outline = measure_outlines(font, groups)
    before = (font["hhea"].ascent, -font["hhea"].descent)

    req = [g for g in REQUIRED_RANGES if groups[g]]
    opt = [g for g in OPTIONAL_RANGES if groups[g]]
    top1 = max((outline[g].top for g in req), default=0)
    bot1 = max((outline[g].bottom for g in req), default=0)
    top12 = max([top1] + [outline[g].top for g in opt])
    bot12 = max([bot1] + [outline[g].bottom for g in opt])
    floor_u = int(round(FLOOR_EM * upm))
    notes: List[str] = []

    if not HAVE_PIL:
        asc, desc = top1 + int(round(FALLBACK_TOP_EM * upm)), bot1 + int(round(FALLBACK_BOTTOM_EM * upm))
        notes.append("Pillow not available: outline extremes plus a fixed safety margin")
        return Fit(asc, desc, upm, before, outline, "outline", ft_round, notes=notes)

    req_cps = [cp for g in req for cp in groups[g]]
    opt_cps = [cp for g in opt for cp in groups[g]]
    eps = SAFETY_PX
    sizes = PRIMARY_SIZES + NEIGHBOUR_SIZES

    ext: Dict[int, Tuple[int, int, int, int]] = {}
    who: Dict[int, Tuple[int, int]] = {}
    for size in sizes:
        p = Probe(data, size)
        t1, tc, b1, bc = p.extremes(req_cps)
        t12, _, b12, _ = p.extremes(req_cps + opt_cps)
        ext[size] = (t1, b1, t12, b12)
        who[size] = (tc, bc)

    half = 0.5 if ft_round else 1.0                      # rows = ceil(a) needs a > rows - 1, round(a) needs a >= rows - 0.5

    # ---- ascent
    # The box FreeType builds (what Unity does) at the three RimWorld sizes, and the unhinted outline ...
    must_list = [(top1 + floor_u, "outline")] + [(_units(ext[s][0] - half + eps, s, upm), f"{s}px") for s in PRIMARY_SIZES]
    must_asc_ft, asc_driver = max(must_list)
    # ... and, at the same three sizes, the strictest rounding an engine could apply (everything to the nearest pixel):
    # nobody has documented how the 1.7 engine rounds, and for most fonts this costs nothing.
    strict_list = [(_units(ext[s][0] - 0.5 + eps, s, upm), f"{s}px, strictest rounding") for s in PRIMARY_SIZES]
    must_asc, asc_driver = max([(must_asc_ft, asc_driver)] + strict_list)
    wishes = [_units(ext[s][0] - 0.5 + eps, s, upm) for s in sizes]           # strict model, required characters
    wishes += [_units(ext[s][2] - 0.5 + eps, s, upm) for s in sizes]          # ... and with the optional ones
    wishes += [_units(ext[s][2] - half + eps, s, upm) for s in sizes] + [top12 + floor_u]
    asc = int(math.ceil(max([must_asc] + [w for w in wishes if w <= must_asc_ft + TOP_BUDGET_EM * upm])))

    # ---- descent (depends on the ascent through the rounding of the line height)
    def below_must(rows: int, size: int) -> float:
        a = asc * size / upm
        A = _round(a) if ft_round else _ceil(a)
        return _units(max(rows - half + eps, rows + A - 0.5 + eps - a), size, upm)

    def below_strict(rows: int, size: int) -> float:
        a = asc * size / upm
        return _units(max(rows - 0.5 + eps, rows + _ceil(a) - 0.5 + eps - a), size, upm)

    must_list = [(bot1 + floor_u, "outline")] + [(below_must(ext[s][1], s), f"{s}px") for s in PRIMARY_SIZES]
    must_desc_ft, desc_driver = max(must_list)
    strict_list = [(below_strict(ext[s][1], s), f"{s}px, strictest rounding") for s in PRIMARY_SIZES]
    must_desc, desc_driver = max([(must_desc_ft, desc_driver)] + strict_list)
    wishes = [below_must(ext[s][1], s) for s in NEIGHBOUR_SIZES]
    wishes += [below_strict(ext[s][1], s) for s in sizes] + [below_strict(ext[s][3], s) for s in sizes]
    wishes += [below_must(ext[s][3], s) for s in sizes] + [bot12 + floor_u]
    desc = int(math.ceil(max([must_desc] + [w for w in wishes if w <= must_desc_ft + BOTTOM_BUDGET_EM * upm])))

    if asc > MAX_ASCENT_EM * upm or desc > MAX_DESCENT_EM * upm:
        notes.append(f"box of {asc}/{desc} is unusually large for unitsPerEm {upm}")

    # ---- what is left over
    for s in sizes:
        r = rooms(asc, desc, upm, s, ft_round)
        if ext[s][0] > r["must"][0] or ext[s][1] > r["must"][1]:
            notes.append(f"{s}px: required characters are cut off")
    return Fit(asc, desc, upm, before, outline, "hinted", ft_round, ext, (int(math.ceil(must_asc)), int(math.ceil(must_desc))),
               {"ascent": asc_driver, "descent": desc_driver}, notes)


def fit_and_patch(data: bytes) -> Tuple[bytes, Fit]:
    fit = fit_line_metrics(data)
    return patch_vertical_metrics(data, fit.ascent, fit.descent), fit


# --------------------------------------------------------------------------
# Reporting

def clipping_report(data: bytes, sizes: Sequence[int] = REPORT_SIZES, asc: Optional[int] = None,
                    desc: Optional[int] = None, only_groups: Optional[Iterable[str]] = None) -> dict:
    """How many glyphs of each group have visible ink outside the line box, per pixel size.

    For every group and size: (number of glyphs, rows of the worst one) cut off at the top and at the bottom, for
    the box FreeType builds for this font ("must", see `rooms`) and for the strictest box model ("strict").
    asc / desc replace the numbers stored in the file (what-if); the file itself is not changed.
    only_groups limits the work (default: the required and the optional groups, not the harakat).
    """
    font = TTFont(BytesIO(data))
    upm = font["head"].unitsPerEm
    ft_round = uses_rounding(font["head"].flags)
    groups = groups_of_font(font.getBestCmap())
    wanted = set(only_groups) if only_groups is not None else set(REQUIRED_RANGES) | set(OPTIONAL_RANGES)
    groups = {g: cps for g, cps in groups.items() if g in wanted}
    if asc is None:
        asc, desc = font["hhea"].ascent, -font["hhea"].descent
    elif asc != font["hhea"].ascent or desc != -font["hhea"].descent:
        data = patch_vertical_metrics(data, asc, desc)
    out = {"upm": upm, "ascent": asc, "descent": desc, "ft_round": ft_round, "sizes": {}}
    for s in sizes:
        p = Probe(data, s)
        r = rooms(asc, desc, upm, s, ft_round)
        entry = {"must": r["must"], "strict": r["strict"], "freetype": freetype_box(data, s), "groups": {}}
        lo_a, lo_b = min(r["must"][0], r["strict"][0]), min(r["must"][1], r["strict"][1])
        for g, cps in groups.items():
            cnt = {"must": [0, 0, 0, 0], "strict": [0, 0, 0, 0]}          # n top, max top, n bottom, max bottom
            worst = [0, 0]
            for cp in cps:
                rows = p.rows(cp)
                if rows is None:
                    continue
                v = rows
                if rows[0] > lo_a or rows[1] > lo_b:
                    v = p.visible(cp)
                    if v is None:
                        continue
                for model in ("must", "strict"):
                    over_t, over_b = v[0] - r[model][0], v[1] - r[model][1]
                    c = cnt[model]
                    if over_t > 0:
                        c[0] += 1
                        if over_t > c[1]:
                            c[1] = over_t
                            if model == "must":
                                worst[0] = cp
                    if over_b > 0:
                        c[2] += 1
                        if over_b > c[3]:
                            c[3] = over_b
                            if model == "must":
                                worst[1] = cp
            entry["groups"][g] = {"n": len(cps), "must": tuple(cnt["must"]), "strict": tuple(cnt["strict"]),
                                  "worst_top": worst[0], "worst_bottom": worst[1]}
        out["sizes"][s] = entry
    return out


def harakat_fit(data: bytes, asc: Optional[int] = None, desc: Optional[int] = None) -> dict:
    """How the pre-placed harakat (U+E400-U+EE6F, one glyph per letter and mark) sit against the line box.

    They are not part of the fitted box (see the module docstring). This tells how many of the distinct
    placed glyphs lie completely inside a box of asc / desc, and how far the others stick out (font units, outline).
    """
    font = TTFont(BytesIO(data))
    upm = font["head"].unitsPerEm
    if asc is None:
        asc, desc = font["hhea"].ascent, -font["hhea"].descent
    cmap = font.getBestCmap()
    glyf = font["glyf"]
    seen, tops, bottoms = set(), [], []
    for cp in sorted(cmap):
        if not (0xE400 <= cp <= 0xEE6F):
            continue
        g = cmap[cp]
        if g in seen:
            continue
        seen.add(g)
        gl = glyf[g]
        if gl.numberOfContours == 0:
            continue
        gl.recalcBounds(glyf)
        tops.append(gl.yMax)
        bottoms.append(-gl.yMin)
    n = len(tops)
    over_t = sorted(t - asc for t in tops if t > asc)
    over_b = sorted(b - desc for b in bottoms if b > desc)
    inside = sum(1 for t, b in zip(tops, bottoms) if t <= asc and b <= desc)

    def pct(values, q):
        return values[min(len(values) - 1, int(len(values) * q))] / upm if values else 0.0

    return {"glyphs": n, "inside": inside, "inside_share": inside / n if n else 1.0,
            "above": len(over_t), "above_median_em": pct(over_t, 0.5), "above_max_em": (over_t[-1] / upm if over_t else 0.0),
            "below": len(over_b), "below_median_em": pct(over_b, 0.5), "below_max_em": (over_b[-1] / upm if over_b else 0.0)}


def required_clean(report: dict, model: str = "must", groups: Iterable[str] = tuple(REQUIRED_RANGES),
                   sizes: Iterable[int] = PRIMARY_SIZES) -> List[str]:
    """Problems that remain in a clipping_report (empty = the required characters are never cut off)."""
    bad = []
    for s in sizes:
        e = report["sizes"].get(s) or report["sizes"].get(str(s))
        for g in groups:
            r = e["groups"].get(g)
            if r and (r[model][0] or r[model][2]):
                bad.append(f"{s}px {g}: {r[model][0]} glyphs cut at the top, {r[model][2]} at the bottom")
    return bad


# --------------------------------------------------------------------------
# Command line

def _cli(argv: List[str]) -> int:
    if len(argv) < 3 or argv[1] not in ("measure", "report"):
        print(__doc__)
        return 2
    import glob
    paths: List[str] = []
    for a in argv[2:]:
        paths += sorted(glob.glob(a)) or [a]
    for path in paths:
        data = open(path, "rb").read()
        fit = fit_line_metrics(data)
        name = path.replace("\\", "/").split("/")[-1]
        print(f"\n{name}  unitsPerEm {fit.upm}   now {fit.before[0]}/{fit.before[1]} -> proposed {fit.ascent}/{fit.descent} "
              f"({fit.method}, line height {fit.line_height_em:.3f} em, must-have {fit.must[0]}/{fit.must[1]} "
              f"set by {fit.drivers.get('ascent', '?')} / {fit.drivers.get('descent', '?')}, "
              f"FreeType rounds line box: {'to nearest' if fit.ft_round else 'ascent up, descent down'})")
        for note in fit.notes:
            print("   !", note)
        if argv[1] == "measure":
            for g in GROUP_ORDER:
                e = fit.outline.get(g)
                if e and e.count:
                    print(f"   {g:17s} {e.count:5d} glyphs  top {e.top / fit.upm:+.3f} em (U+{e.top_cp:04X})   "
                          f"bottom -{e.bottom / fit.upm:.3f} em (U+{e.bottom_cp:04X})")
        else:
            for label, (a, d) in (("now", fit.before), ("proposed", (fit.ascent, fit.descent))):
                rep = clipping_report(data, asc=a, desc=d)
                for model in ("must", "strict"):
                    problems = required_clean(rep, model)
                    print(f"   {label:9s} {model:6s} {'clean at 11/14/20 px' if not problems else '; '.join(problems)}")
    return 0


if __name__ == "__main__":
    sys.exit(_cli(sys.argv))
