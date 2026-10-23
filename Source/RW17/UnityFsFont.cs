using System;
using System.IO;
using System.Text;

namespace ArabicSupport.Fonts
{
    /// <summary>
    /// Reads the TrueType font out of a ".fontbundle" (RimWorld 1.7 build only).
    ///
    /// A .fontbundle is a small Unity AssetBundle (UnityFS container, LZ4) that
    /// holds one dynamic Font. RimWorld 1.5 and 1.6 hand the bundle to Unity.
    /// RimWorld 1.7 does not run on Unity: its engine (Waylith) takes the raw
    /// bytes of a font file instead. So the bundle is opened here, its blocks
    /// are decompressed, and the font is cut out of the result. The same
    /// .fontbundle files therefore work on every supported game version.
    ///
    /// The font itself is easy to find: it is a TrueType file (sfnt) with a
    /// "cmap" table that Unity stores as "int32 length + bytes". Tools/
    /// fontbundle_to_ttf.py does the same steps in Python and was run on every
    /// bundle of this repository (the stored length matched the font's own
    /// table directory every time).
    ///
    /// Only LZ4 and uncompressed bundles are understood (what
    /// Tools/build_fonts.py writes). Anything else gives a clear message.
    /// </summary>
    internal static class UnityFsFont
    {
        private const int MaxBundleBytes = 256 * 1024 * 1024;

        /// <summary>The font file's bytes, or null (with the reason in error).</summary>
        public static byte[] Extract(byte[] file, out string error)
        {
            error = null;

            try
            {
                byte[] data = ReadBundleData(file, out error);
                if (data == null)
                    return null;

                byte[] font = FindSfnt(data);
                if (font == null)
                    error = "no TrueType font was found inside the bundle.";

                return font;
            }
            catch (Exception ex)
            {
                error = "the bundle could not be read (" + ex.Message + ").";
                return null;
            }
        }

        // ------------------------------------------------------------------
        // UnityFS container

        /// <summary>All blocks of the bundle, decompressed and joined.</summary>
        private static byte[] ReadBundleData(byte[] f, out string error)
        {
            error = null;
            int p = 0;

            if (ReadCString(f, ref p) != "UnityFS")
            {
                error = "this is not a Unity bundle.";
                return null;
            }

            uint version = ReadU32(f, ref p);
            ReadCString(f, ref p);                 // player version
            ReadCString(f, ref p);                 // engine version
            p += 8;                                // total size (int64)
            int packedInfoSize = checked((int)ReadU32(f, ref p));
            int infoSize = checked((int)ReadU32(f, ref p));
            uint flags = ReadU32(f, ref p);

            if (version >= 7)
                p = (p + 15) & ~15;

            byte[] packedInfo;
            if ((flags & 0x80) != 0)
            {
                // The table of blocks is stored at the very end of the file.
                packedInfo = Slice(f, f.Length - packedInfoSize, packedInfoSize);
            }
            else
            {
                packedInfo = Slice(f, p, packedInfoSize);
                p += packedInfoSize;
            }

            byte[] info = Unpack(packedInfo, infoSize, (int)(flags & 0x3F), out error);
            if (info == null)
                return null;

            int q = 16;                            // skip the 16-byte hash
            int blockCount = ReadI32(info, ref q);
            if (blockCount < 0 || blockCount > 100000)
            {
                error = "the block table is damaged.";
                return null;
            }

            int[] sizes = new int[blockCount];
            int[] packedSizes = new int[blockCount];
            int[] blockFlags = new int[blockCount];
            long total = 0;

            for (int i = 0; i < blockCount; i++)
            {
                sizes[i] = checked((int)ReadU32(info, ref q));
                packedSizes[i] = checked((int)ReadU32(info, ref q));
                blockFlags[i] = ReadU16(info, ref q);
                total += sizes[i];
            }

            if (total > MaxBundleBytes)
            {
                error = "the bundle is too large.";
                return null;
            }

            byte[] result = new byte[(int)total];
            int written = 0;

            for (int i = 0; i < blockCount; i++)
            {
                byte[] packed = Slice(f, p, packedSizes[i]);
                p += packedSizes[i];

                byte[] block = Unpack(packed, sizes[i], blockFlags[i] & 0x3F, out error);
                if (block == null)
                    return null;

                Buffer.BlockCopy(block, 0, result, written, block.Length);
                written += block.Length;
            }

            return result;
        }

        private static byte[] Unpack(byte[] src, int size, int kind, out string error)
        {
            error = null;

            if (kind == 0)
                return src;                        // stored

            if (kind == 2 || kind == 3)            // LZ4 / LZ4HC
                return Lz4(src, size);

            error = "the bundle uses LZMA compression, which is not supported (rebuild it with Tools/build_fonts.py).";
            return null;
        }

        /// <summary>LZ4 block decompression.</summary>
        private static byte[] Lz4(byte[] src, int size)
        {
            byte[] dst = new byte[size];
            int sp = 0;
            int dp = 0;

            while (sp < src.Length)
            {
                int token = src[sp++];

                int literals = token >> 4;
                if (literals == 15)
                {
                    int more;
                    do
                    {
                        more = src[sp++];
                        literals += more;
                    }
                    while (more == 255);
                }

                if (literals > 0)
                {
                    Buffer.BlockCopy(src, sp, dst, dp, literals);
                    sp += literals;
                    dp += literals;
                }

                if (sp >= src.Length)
                    break;                         // the last sequence only has literals

                int offset = src[sp] | (src[sp + 1] << 8);
                sp += 2;

                int length = token & 15;
                if (length == 15)
                {
                    int more;
                    do
                    {
                        more = src[sp++];
                        length += more;
                    }
                    while (more == 255);
                }
                length += 4;

                int from = dp - offset;
                if (offset == 0 || from < 0)
                    throw new InvalidDataException("bad LZ4 offset");

                // The copy may overlap itself, so it goes byte by byte.
                for (int k = 0; k < length; k++)
                    dst[dp++] = dst[from++];
            }

            if (dp != size)
                throw new InvalidDataException("LZ4 size mismatch");

            return dst;
        }

        // ------------------------------------------------------------------
        // The font inside the decompressed data

        private static byte[] FindSfnt(byte[] d)
        {
            for (int i = 4; i + 12 <= d.Length; i++)
            {
                bool trueType = d[i] == 0x00 && d[i + 1] == 0x01 && d[i + 2] == 0x00 && d[i + 3] == 0x00;
                bool openType = d[i] == (byte)'O' && d[i + 1] == (byte)'T' && d[i + 2] == (byte)'T' && d[i + 3] == (byte)'O';
                bool appleTrueType = d[i] == (byte)'t' && d[i + 1] == (byte)'r' && d[i + 2] == (byte)'u' && d[i + 3] == (byte)'e';

                if (!trueType && !openType && !appleTrueType)
                    continue;

                int length = SfntLength(d, i);
                if (length <= 0)
                    continue;

                // Unity stores a byte array as "int32 length, bytes".
                int stored = d[i - 4] | (d[i - 3] << 8) | (d[i - 2] << 16) | (d[i - 1] << 24);
                if (stored >= length && stored <= d.Length - i)
                    length = stored;

                return Slice(d, i, length);
            }

            return null;
        }

        /// <summary>
        /// Size of the font that starts at d[i], from its table directory; -1
        /// when it does not look like a real font (a "cmap" table is required).
        /// </summary>
        private static int SfntLength(byte[] d, int i)
        {
            int remaining = d.Length - i;
            int tables = (d[i + 4] << 8) | d[i + 5];

            if (tables < 1 || tables > 256 || 12 + 16 * tables > remaining)
                return -1;

            long end = 12 + 16 * tables;
            bool hasCmap = false;

            for (int k = 0; k < tables; k++)
            {
                int r = i + 12 + 16 * k;
                long offset = BeU32(d, r + 8);
                long length = BeU32(d, r + 12);

                if (offset + length > remaining)
                    return -1;

                if (offset + length > end)
                    end = offset + length;

                if (d[r] == (byte)'c' && d[r + 1] == (byte)'m' && d[r + 2] == (byte)'a' && d[r + 3] == (byte)'p')
                    hasCmap = true;
            }

            if (!hasCmap)
                return -1;

            long padded = (end + 3) & ~3L;
            return (int)Math.Min(padded, (long)remaining);
        }

        // ------------------------------------------------------------------
        // Big-endian readers (the UnityFS header and block table are big-endian)

        private static string ReadCString(byte[] b, ref int p)
        {
            int start = p;
            while (p < b.Length && b[p] != 0)
                p++;

            if (p >= b.Length)
                throw new InvalidDataException("unterminated text");

            string s = Encoding.ASCII.GetString(b, start, p - start);
            p++;
            return s;
        }

        private static uint BeU32(byte[] b, int o)
        {
            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
        }

        private static uint ReadU32(byte[] b, ref int p)
        {
            uint v = BeU32(b, p);
            p += 4;
            return v;
        }

        private static int ReadI32(byte[] b, ref int p)
        {
            return unchecked((int)ReadU32(b, ref p));
        }

        private static int ReadU16(byte[] b, ref int p)
        {
            int v = (b[p] << 8) | b[p + 1];
            p += 2;
            return v;
        }

        private static byte[] Slice(byte[] b, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > b.Length - count)
                throw new InvalidDataException("a block lies outside of the file");

            byte[] result = new byte[count];
            Buffer.BlockCopy(b, offset, result, 0, count);
            return result;
        }
    }
}
