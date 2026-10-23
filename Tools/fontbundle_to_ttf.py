#!/usr/bin/env python3
"""Arabic Support - take the TrueType font out of .fontbundle files.

A .fontbundle is a small Unity AssetBundle (UnityFS container, LZ4) that holds
one font. RimWorld 1.5 and 1.6 give the bundle to Unity. RimWorld 1.7 does not
run on Unity, so the 1.7 build of the mod opens the bundle itself
(Source/RW17/UnityFsFont.cs does the same steps as this script) and hands the
font to the new engine. This script writes the font as a plain .ttf, e.g. to
use one of the extra fonts somewhere else, or to put a .ttf next to a bundle:
on RimWorld 1.7 the mod prefers Fonts/<Name>.ttf over Fonts/<Name>.fontbundle.

    python Tools/fontbundle_to_ttf.py Fonts                  every bundle in Fonts/  -> Fonts/<Name>.ttf
    python Tools/fontbundle_to_ttf.py Fonts/Cairo-Bold.fontbundle -o out
    python Tools/fontbundle_to_ttf.py --check ExtraFonts/Fonts   only test that every bundle opens

No packages needed (Python 3.8+). Only LZ4 and uncompressed bundles are
understood, which is what Tools/build_fonts.py writes; "build_fonts.py --ttf"
writes the same .ttf files directly while it builds the bundles.
"""

import argparse
import struct
import sys
from pathlib import Path


class BundleError(ValueError):
    """The file is not a bundle this script understands."""


# --------------------------------------------------------------------------
# LZ4 block decompression

def lz4_block(src: bytes, size: int) -> bytes:
    dst = bytearray()
    sp, n = 0, len(src)
    while sp < n:
        token = src[sp]
        sp += 1

        literals = token >> 4
        if literals == 15:
            while True:
                more = src[sp]
                sp += 1
                literals += more
                if more != 255:
                    break
        dst += src[sp:sp + literals]
        sp += literals
        if sp >= n:
            break                               # the last sequence only has literals

        offset = src[sp] | (src[sp + 1] << 8)
        sp += 2

        length = token & 15
        if length == 15:
            while True:
                more = src[sp]
                sp += 1
                length += more
                if more != 255:
                    break
        length += 4

        start = len(dst) - offset
        if offset == 0 or start < 0:
            raise BundleError("bad LZ4 offset")
        for k in range(length):                 # the copy may overlap itself
            dst.append(dst[start + k])

    if len(dst) != size:
        raise BundleError(f"LZ4 size mismatch ({len(dst)} != {size})")
    return bytes(dst)


def unpack(src: bytes, size: int, kind: int) -> bytes:
    if kind == 0:
        return src
    if kind in (2, 3):                          # LZ4 / LZ4HC
        return lz4_block(src, size)
    raise BundleError("LZMA compression is not supported (rebuild the bundle with Tools/build_fonts.py)")


# --------------------------------------------------------------------------
# UnityFS container

def cstring(data: bytes, pos: int):
    end = data.index(b"\0", pos)
    return data[pos:end].decode("latin1"), end + 1


def bundle_data(f: bytes) -> bytes:
    """All blocks of the bundle, decompressed and joined."""
    signature, p = cstring(f, 0)
    if signature != "UnityFS":
        raise BundleError("not a Unity bundle")

    version = struct.unpack_from(">I", f, p)[0]
    p += 4
    _, p = cstring(f, p)                        # player version
    _, p = cstring(f, p)                        # engine version
    _total, packed_info, info_size, flags = struct.unpack_from(">qIII", f, p)
    p += 20
    if version >= 7:
        p = (p + 15) & ~15

    if flags & 0x80:                            # the block table is at the end of the file
        packed = f[len(f) - packed_info:]
    else:
        packed = f[p:p + packed_info]
        p += packed_info

    info = unpack(packed, info_size, flags & 0x3F)
    q = 16                                      # skip the 16-byte hash
    count = struct.unpack_from(">i", info, q)[0]
    q += 4
    blocks = []
    for _ in range(count):
        blocks.append(struct.unpack_from(">IIH", info, q))
        q += 10

    out = bytearray()
    for size, packed_size, block_flags in blocks:
        out += unpack(f[p:p + packed_size], size, block_flags & 0x3F)
        p += packed_size
    return bytes(out)


# --------------------------------------------------------------------------
# the font inside the decompressed data

def sfnt_length(d: bytes, i: int) -> int:
    """Size of the font starting at d[i] from its table directory, or -1 if it is not a real font."""
    remaining = len(d) - i
    if remaining < 12:
        return -1
    tables = (d[i + 4] << 8) | d[i + 5]
    if tables < 1 or tables > 256 or 12 + 16 * tables > remaining:
        return -1

    end = 12 + 16 * tables
    has_cmap = False
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


def find_sfnt(d: bytes) -> bytes:
    for i in range(4, len(d) - 12):
        if d[i:i + 4] not in (b"\x00\x01\x00\x00", b"OTTO", b"true"):
            continue
        length = sfnt_length(d, i)
        if length <= 0:
            continue
        stored = struct.unpack_from("<i", d, i - 4)[0]   # Unity stores byte arrays as "int32 length, bytes"
        if length <= stored <= len(d) - i:
            length = stored
        return d[i:i + length]
    raise BundleError("no TrueType font found inside the bundle")


def extract(bundle: bytes) -> bytes:
    """The TrueType (or OpenType) font file inside the bundle's bytes."""
    try:
        return find_sfnt(bundle_data(bundle))
    except (struct.error, IndexError, ValueError) as e:
        if isinstance(e, BundleError):
            raise
        raise BundleError(f"the bundle could not be read ({e})")


# --------------------------------------------------------------------------

def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Extract the font from .fontbundle files.")
    parser.add_argument("paths", nargs="+", help=".fontbundle files, or folders containing them")
    parser.add_argument("-o", "--out", help="folder for the .ttf files (default: next to each bundle)")
    parser.add_argument("--check", action="store_true", help="only test that every bundle opens; write nothing")
    args = parser.parse_args(argv)

    files = []
    for name in args.paths:
        p = Path(name)
        if p.is_dir():
            files.extend(sorted(p.glob("*.fontbundle")))
        else:
            files.append(p)
    if not files:
        print("No .fontbundle files found.")
        return 1

    out_dir = Path(args.out) if args.out else None
    if out_dir and not args.check:
        out_dir.mkdir(parents=True, exist_ok=True)

    failed = 0
    for path in files:
        try:
            font = extract(path.read_bytes())
        except (OSError, BundleError) as e:
            print(f"FAILED {path.name}: {e}")
            failed += 1
            continue

        if args.check:
            print(f"OK     {path.name}  ({len(font)} bytes of font)")
            continue

        target = (out_dir or path.parent) / (path.stem + ".ttf")
        target.write_bytes(font)
        print(f"OK     {path.name} -> {target}  ({len(font)} bytes)")

    print(f"{len(files) - failed} of {len(files)} bundles done.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
