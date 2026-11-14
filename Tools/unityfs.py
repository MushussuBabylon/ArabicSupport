#!/usr/bin/env python3
"""Arabic Support - read and rewrite ".fontbundle" files (UnityFS containers). Pure Python, no packages.

A .fontbundle is a small Unity AssetBundle (UnityFS version 7, LZ4) that holds one dynamic Font object.
`fontbundle_to_ttf.py` only reads them. This module can also write them back, which is what
`retune_font_metrics.py` needs: change the font's vertical metrics WITHOUT a Unity Editor, UnityPy or
ttfautohint, and without touching a single other byte of the bundle.

What a metric-only change touches inside a bundle (verified byte for byte by `verify_fonts.py`):

  * the TrueType font stored in the Font object's m_FontData (same length, only table bytes change),
  * the three floats Unity keeps next to it: m_LineSpacing (before the font), m_Ascent and m_Descent
    (right after it); build_fonts.py writes them as 16 * metric / unitsPerEm.

The container itself is rebuilt with the same header, flags, node table and 128 KB block layout as the
original. A block whose bytes did not change keeps its original compressed bytes; only changed blocks are
compressed again, with the LZ4 encoder below (checked by the strict decoder in this file, which enforces
the same end-of-block rules as the reference LZ4_decompress_safe that Unity uses).

    python Tools/unityfs.py --selftest        round-trip tests for the LZ4 coder and the container writer
"""

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple


class BundleError(ValueError):
    """The data is not a bundle (or not a font bundle) this module understands."""


# --------------------------------------------------------------------------
# LZ4 block format (https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md)

MIN_MATCH = 4
MF_LIMIT = 12          # a match must start at least this far before the end of the block
LAST_LITERALS = 5      # the last bytes of a block are always literals
MAX_OFFSET = 65535


def lz4_decompress(src: bytes, size: int, strict: bool = True) -> bytes:
    """Decode one LZ4 block into exactly `size` bytes.

    strict=True also applies the rules the reference decoder (LZ4_decompress_safe, used by Unity)
    enforces, so a block that passes here is accepted by Unity's loader as well:
    the last sequence holds literals only, the last 5 bytes are literals, and every earlier
    sequence ends at least 12 bytes before the end of the block.
    """
    dst = bytearray()
    sp, n = 0, len(src)
    if n == 0:
        raise BundleError("empty LZ4 block")
    while sp < n:
        token = src[sp]
        sp += 1

        literals = token >> 4
        if literals == 15:
            while True:
                if sp >= n:
                    raise BundleError("truncated LZ4 literal length")
                more = src[sp]
                sp += 1
                literals += more
                if more != 255:
                    break
        if sp + literals > n:
            raise BundleError("LZ4 literals run past the end of the block")
        dst += src[sp:sp + literals]
        sp += literals

        if sp >= n:                                   # last sequence: literals only
            break
        if strict and (len(dst) > size - MF_LIMIT or sp + 2 + 1 + LAST_LITERALS > n):
            raise BundleError("LZ4: a sequence with a match is too close to the end of the block")

        if sp + 2 > n:
            raise BundleError("truncated LZ4 offset")
        offset = src[sp] | (src[sp + 1] << 8)
        sp += 2
        if offset == 0 or offset > len(dst):
            raise BundleError("bad LZ4 offset")

        length = token & 15
        if length == 15:
            while True:
                if sp >= n:
                    raise BundleError("truncated LZ4 match length")
                more = src[sp]
                sp += 1
                length += more
                if more != 255:
                    break
        length += MIN_MATCH
        if strict and len(dst) + length > size - LAST_LITERALS:
            raise BundleError("LZ4: the last 5 bytes of a block must be literals")

        start = len(dst) - offset
        if offset >= length:
            dst += dst[start:start + length]
        else:                                         # overlapping copy, byte by byte
            for k in range(length):
                dst.append(dst[start + k])

    if len(dst) != size:
        raise BundleError(f"LZ4 size mismatch ({len(dst)} != {size})")
    return bytes(dst)


def _lz4_length(out: bytearray, value: int) -> None:
    while value >= 255:
        out.append(255)
        value -= 255
    out.append(value)


def _lz4_literals_only(out: bytearray, src, start: int, end: int) -> None:
    count = end - start
    if count >= 15:
        out.append(0xF0)
        _lz4_length(out, count - 15)
    else:
        out.append(count << 4)
    out += src[start:end]


def lz4_compress(src: bytes) -> bytes:
    """A small greedy LZ4 block encoder (hash of 4 bytes, no lazy matching): valid LZ4, not the smallest."""
    n = len(src)
    out = bytearray()
    if n < MF_LIMIT + 1:                              # too small for any match
        _lz4_literals_only(out, src, 0, n)
        return bytes(out)

    table = {}
    anchor = 0
    ip = 0
    match_start_limit = n - MF_LIMIT                  # a match may start at ip <= n - 13
    match_end_limit = n - LAST_LITERALS               # and must end at or before n - 5

    while ip < match_start_limit:
        key = src[ip:ip + 4]
        ref = table.get(key)
        table[key] = ip
        if ref is None or ip - ref > MAX_OFFSET:
            ip += 1
            continue

        length = MIN_MATCH
        while ip + length < match_end_limit and src[ref + length] == src[ip + length]:
            length += 1

        literals = ip - anchor
        ml = length - MIN_MATCH
        token = (min(literals, 15) << 4) | min(ml, 15)
        out.append(token)
        if literals >= 15:
            _lz4_length(out, literals - 15)
        out += src[anchor:ip]
        offset = ip - ref
        out.append(offset & 0xFF)
        out.append(offset >> 8)
        if ml >= 15:
            _lz4_length(out, ml - 15)

        # index a few positions inside the match so later data can refer back to them
        for p in range(ip + 1, min(ip + length, match_start_limit), 2):
            table[src[p:p + 4]] = p
        ip += length
        anchor = ip

    _lz4_literals_only(out, src, anchor, n)
    return bytes(out)


# --------------------------------------------------------------------------
# UnityFS container

COMPRESSION_MASK = 0x3F
FLAG_HAS_DIRECTORY = 0x40
FLAG_INFO_AT_END = 0x80
KIND_NONE, KIND_LZMA, KIND_LZ4, KIND_LZ4HC = 0, 1, 2, 3


def _pack(data: bytes, kind: int) -> bytes:
    if kind == KIND_NONE:
        return bytes(data)
    if kind in (KIND_LZ4, KIND_LZ4HC):
        return lz4_compress(data)
    raise BundleError("only stored and LZ4 blocks can be written")


def _unpack(packed: bytes, size: int, kind: int) -> bytes:
    if kind == KIND_NONE:
        if len(packed) != size:
            raise BundleError("stored block has the wrong size")
        return bytes(packed)
    if kind in (KIND_LZ4, KIND_LZ4HC):
        return lz4_decompress(packed, size, strict=False)
    raise BundleError("LZMA bundles are not supported (rebuild them with Tools/build_fonts.py)")


def _cstring(buf: bytes, pos: int) -> Tuple[str, int]:
    end = buf.index(b"\0", pos)
    return buf[pos:end].decode("latin1"), end + 1


@dataclass
class Node:
    offset: int
    size: int
    flags: int
    path: str


@dataclass
class Bundle:
    version: int
    player_version: str
    engine_version: str
    flags: int
    info_hash: bytes
    block_sizes: List[int]            # uncompressed size of every block
    block_flags: List[int]
    packed_blocks: List[bytes]        # the original compressed bytes of every block
    nodes: List[Node]
    data: bytes                       # all blocks, decompressed and joined
    header_padding: bytes = b""
    info_raw: bytes = b""             # the block table + node table as read (decompressed)
    info_packed: bytes = b""          # ... and as stored in the file

    def clone(self) -> "Bundle":
        return Bundle(self.version, self.player_version, self.engine_version, self.flags, self.info_hash,
                      list(self.block_sizes), list(self.block_flags), list(self.packed_blocks),
                      [Node(n.offset, n.size, n.flags, n.path) for n in self.nodes], self.data,
                      self.header_padding, self.info_raw, self.info_packed)


def read_bundle(raw: bytes) -> Bundle:
    try:
        sig, p = _cstring(raw, 0)
        if sig != "UnityFS":
            raise BundleError("not a UnityFS bundle")
        version = struct.unpack_from(">I", raw, p)[0]
        p += 4
        player, p = _cstring(raw, p)
        engine, p = _cstring(raw, p)
        total, packed_info, info_size, flags = struct.unpack_from(">qIII", raw, p)
        p += 20
        if total != len(raw):
            raise BundleError(f"header says {total} bytes, the file has {len(raw)}")
        pad = b""
        if version >= 7:
            aligned = (p + 15) & ~15
            pad = raw[p:aligned]
            p = aligned

        if flags & FLAG_INFO_AT_END:
            packed = raw[len(raw) - packed_info:]
            data_end = len(raw) - packed_info
        else:
            packed = raw[p:p + packed_info]
            p += packed_info
            data_end = len(raw)

        info = _unpack(packed, info_size, flags & COMPRESSION_MASK)
        q = 16
        info_hash = info[:16]
        count = struct.unpack_from(">i", info, q)[0]
        q += 4
        sizes, packed_sizes, bflags = [], [], []
        for _ in range(count):
            s, ps, bf = struct.unpack_from(">IIH", info, q)
            q += 10
            sizes.append(s)
            packed_sizes.append(ps)
            bflags.append(bf)

        node_count = struct.unpack_from(">i", info, q)[0]
        q += 4
        nodes = []
        for _ in range(node_count):
            off, size, nf = struct.unpack_from(">qqI", info, q)
            q += 20
            path, q = _cstring(info, q)
            nodes.append(Node(off, size, nf, path))

        packed_blocks, parts = [], []
        for s, ps, bf in zip(sizes, packed_sizes, bflags):
            blob = raw[p:p + ps]
            p += ps
            packed_blocks.append(blob)
            parts.append(_unpack(blob, s, bf & COMPRESSION_MASK))
        if p != data_end:
            raise BundleError("the blocks do not fill the file as the block table says")

        return Bundle(version, player, engine, flags, info_hash, sizes, bflags, packed_blocks, nodes,
                      b"".join(parts), pad, info, packed)
    except BundleError:
        raise
    except (struct.error, IndexError, ValueError) as e:
        raise BundleError(f"the bundle could not be read ({e})")


def write_bundle(b: Bundle, original: Optional[Bundle] = None) -> bytes:
    """Serialise a bundle. Blocks identical to `original`'s keep their original compressed bytes."""
    if sum(b.block_sizes) != len(b.data):
        raise BundleError("block sizes do not add up to the data length")

    blocks, packed_sizes = [], []
    pos = 0
    for i, size in enumerate(b.block_sizes):
        chunk = b.data[pos:pos + size]
        pos += size
        kind = b.block_flags[i] & COMPRESSION_MASK
        reuse = None
        if original is not None and i < len(original.block_sizes) and original.block_sizes[i] == size \
                and original.block_flags[i] == b.block_flags[i]:
            old_start = sum(original.block_sizes[:i])
            if original.data[old_start:old_start + size] == chunk:
                reuse = original.packed_blocks[i]
        blob = reuse if reuse is not None else _pack(chunk, kind)
        if kind in (KIND_LZ4, KIND_LZ4HC) and reuse is None:
            # never write a block this module could not read back with the strict decoder
            if lz4_decompress(blob, size, strict=True) != chunk:
                raise BundleError("internal error: LZ4 round trip failed")
        blocks.append(blob)
        packed_sizes.append(len(blob))

    info = bytearray(b.info_hash)
    info += struct.pack(">i", len(b.block_sizes))
    for s, ps, bf in zip(b.block_sizes, packed_sizes, b.block_flags):
        info += struct.pack(">IIH", s, ps, bf)
    info += struct.pack(">i", len(b.nodes))
    for n in b.nodes:
        info += struct.pack(">qqI", n.offset, n.size, n.flags) + n.path.encode("utf-8") + b"\0"
    info_kind = b.flags & COMPRESSION_MASK
    if original is not None and original.info_raw == bytes(info) and original.flags == b.flags:
        packed_info = original.info_packed
    else:
        packed_info = _pack(bytes(info), info_kind)
        if info_kind in (KIND_LZ4, KIND_LZ4HC) and lz4_decompress(packed_info, len(info), strict=True) != bytes(info):
            raise BundleError("internal error: LZ4 round trip failed (block table)")

    head = b"UnityFS\0" + struct.pack(">I", b.version) + b.player_version.encode("latin1") + b"\0" + \
        b.engine_version.encode("latin1") + b"\0"
    fixed = 8 + 4 + 4 + 4 + 4                         # total size, packed info, info size, flags (20 bytes)
    header_len = len(head) + 20
    pad = b""
    if b.version >= 7:
        pad = b"\0" * (((header_len + 15) & ~15) - header_len)
    total = header_len + len(pad) + sum(packed_sizes) + len(packed_info)
    header = head + struct.pack(">qIII", total, len(packed_info), len(info), b.flags) + pad

    out = bytearray(header)
    if b.flags & FLAG_INFO_AT_END:
        for blob in blocks:
            out += blob
        out += packed_info
    else:
        out += packed_info
        for blob in blocks:
            out += blob
    assert len(out) == total
    return bytes(out)


# --------------------------------------------------------------------------
# The font inside a font bundle

def sfnt_length(d: bytes, i: int) -> int:
    """Size of the font starting at d[i] from its table directory, or -1 if it is not a real font."""
    remaining = len(d) - i
    if remaining < 12:
        return -1
    tables = (d[i + 4] << 8) | d[i + 5]
    if tables < 1 or tables > 256 or 12 + 16 * tables > remaining:
        return -1
    end, has_cmap = 12 + 16 * tables, False
    for k in range(tables):
        r = i + 12 + 16 * k
        offset, length = struct.unpack_from(">II", d, r + 8)
        if offset + length > remaining:
            return -1
        end = max(end, offset + length)
        if d[r:r + 4] == b"cmap":
            has_cmap = True
    if not has_cmap:
        return -1
    return min((end + 3) & ~3, remaining)


def find_sfnt(d: bytes) -> Tuple[int, int]:
    """(offset, length) of the font in the decompressed bundle data. Same rules as Source/RW17/UnityFsFont.cs."""
    for i in range(4, len(d) - 12):
        if d[i:i + 4] not in (b"\x00\x01\x00\x00", b"OTTO", b"true"):
            continue
        length = sfnt_length(d, i)
        if length <= 0:
            continue
        stored = struct.unpack_from("<i", d, i - 4)[0]        # Unity stores byte arrays as "int32 length, bytes"
        if length <= stored <= len(d) - i:
            length = stored
        return i, length
    raise BundleError("no TrueType font found inside the bundle")


def font_unity_floats(d: bytes, off: int, length: int) -> dict:
    """Where Unity keeps its copy of the font's vertical metrics, relative to the embedded font:

        off - 68   m_LineSpacing   (float32, just before m_FontSize / the texture reference / m_FontData)
        off - 52   m_FontSize      (float32, 16.0)
        off + length      m_Ascent    (float32)
        off + length + 4  m_Descent   (float32, negative)
    """
    return {"m_LineSpacing": off - 68, "m_FontSize": off - 52, "m_Ascent": off + length, "m_Descent": off + length + 4}


def read_unity_floats(d: bytes, off: int, length: int) -> dict:
    return {k: struct.unpack_from("<f", d, p)[0] for k, p in font_unity_floats(d, off, length).items()}


def metrics_floats(asc: int, desc: int, upm: int, font_size: float = 16.0) -> dict:
    """The values build_fonts.py stores (make_bundle): metrics scaled to the 16 px reference size."""
    return {"m_LineSpacing": font_size * (asc + desc) / upm, "m_Ascent": font_size * asc / upm,
            "m_Descent": -font_size * desc / upm}


def replace_font(raw: bytes, new_ttf: bytes, new_metrics: Optional[Tuple[int, int, int]] = None,
                 expect_old_metrics: Optional[Tuple[int, int, int]] = None) -> bytes:
    """Return the bundle with its font replaced by `new_ttf` (must have exactly the same length).

    new_metrics = (ascent, descent, unitsPerEm) also updates Unity's three metric floats. When
    expect_old_metrics is given the stored floats must match it first (refuses to touch a bundle that
    was not built the way build_fonts.py builds them).
    """
    bundle = read_bundle(raw)
    off, length = find_sfnt(bundle.data)
    if len(new_ttf) != length:
        raise BundleError(f"the replacement font has {len(new_ttf)} bytes, the bundle's font {length}: "
                          "only same-size (metrics-only) replacements are supported")
    data = bytearray(bundle.data)

    if new_metrics is not None:
        positions = font_unity_floats(data, off, length)
        if expect_old_metrics is not None:
            have = struct.unpack_from("<f", data, positions["m_FontSize"])[0]
            if abs(have - 16.0) > 1e-3:
                raise BundleError(f"m_FontSize is {have}, expected 16.0: this bundle was not built by build_fonts.py")
            for k, v in metrics_floats(*expect_old_metrics).items():
                have = struct.unpack_from("<f", data, positions[k])[0]
                if abs(have - v) > 1e-3:
                    raise BundleError(f"{k} is {have}, expected {v}: this bundle was not built by build_fonts.py")
        for k, v in metrics_floats(*new_metrics).items():
            struct.pack_into("<f", data, positions[k], v)

    data[off:off + length] = new_ttf
    new = bundle.clone()
    new.data = bytes(data)
    return write_bundle(new, original=bundle)


# --------------------------------------------------------------------------
# Self test

def _selftest() -> int:
    import os
    import random

    rng = random.Random(7)
    failures = 0

    def check(name, ok):
        nonlocal failures
        print(("ok    " if ok else "FAIL  ") + name)
        failures += 0 if ok else 1

    samples = {
        "empty-ish": b"x",
        "tiny": b"abcdefghij",
        "13 bytes": bytes(range(13)),
        "zeros": bytes(100000),
        "text": (b"The quick brown fox jumps over the lazy dog. " * 3000),
        "random": bytes(rng.getrandbits(8) for _ in range(30000)),
        "mixed": b"".join((bytes(rng.getrandbits(8) for _ in range(rng.randint(1, 40))) +
                           b"ArabicSupport" * rng.randint(0, 9)) for _ in range(3000)),
        "long runs": b"".join(bytes([rng.getrandbits(8)]) * rng.randint(1, 700) for _ in range(300)),
    }
    for name, data in samples.items():
        try:
            packed = lz4_compress(data)
            ok = lz4_decompress(packed, len(data), strict=True) == data
        except BundleError as e:
            ok = False
            print("      ", name, e)
        check(f"lz4 round trip, strict decoder: {name} ({len(data)} -> {len(packed) if ok else '?'} bytes)", ok)

    # the decoder must reject streams the reference decoder rejects
    bad = bytearray(lz4_compress(b"abcabcabcabcabcabcabcabcabcabcabcabcabc" * 4))
    try:
        lz4_decompress(bytes(bad[:-3]), 160, strict=True)
        check("truncated stream is rejected", False)
    except BundleError:
        check("truncated stream is rejected", True)

    # container: read -> write -> read gives the same bundle, and unchanged files are byte-identical
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    candidates = []
    for sub in ("Fonts", os.path.join("FontLibrary", "Fonts")):
        folder = os.path.join(root, sub)
        if os.path.isdir(folder):
            candidates += [os.path.join(folder, f) for f in sorted(os.listdir(folder)) if f.endswith(".fontbundle")][:3]
    for path in candidates[:4]:
        raw = open(path, "rb").read()
        b = read_bundle(raw)
        same = write_bundle(b, original=b) == raw
        check(f"unchanged bundle is rewritten byte-identically: {os.path.basename(path)}", same)
        off, length = find_sfnt(b.data)
        patched = bytearray(b.data)
        patched[off + 300] ^= 0x01                              # change one byte inside the font
        nb = b.clone()
        nb.data = bytes(patched)
        out = write_bundle(nb, original=b)
        back = read_bundle(out)
        check(f"one changed byte survives write + strict-LZ4 read: {os.path.basename(path)}", back.data == nb.data)
    return 1 if failures else 0


if __name__ == "__main__":
    import sys

    if "--selftest" in sys.argv:
        sys.exit(_selftest())
    print(__doc__)
