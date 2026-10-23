using System.Collections.Generic;
using System.IO;

namespace ArabicSupport.Fonts
{
    /// <summary>Basic checks on TrueType / OpenType font files (RimWorld 1.7 build only).</summary>
    internal static class SfntFile
    {
        /// <summary>True when the bytes start like a TrueType, OpenType (CFF) or Apple TrueType font.</summary>
        public static bool LooksLikeFont(byte[] d)
        {
            if (d == null || d.Length < 12)
                return false;

            bool trueType = d[0] == 0x00 && d[1] == 0x01 && d[2] == 0x00 && d[3] == 0x00;
            bool openType = d[0] == (byte)'O' && d[1] == (byte)'T' && d[2] == (byte)'T' && d[3] == (byte)'O';
            bool appleTrueType = d[0] == (byte)'t' && d[1] == (byte)'r' && d[2] == (byte)'u' && d[3] == (byte)'e';

            return trueType || openType || appleTrueType;
        }
    }

    /// <summary>
    /// Which characters a font has a glyph for, read from its "cmap" table
    /// (Basic Multilingual Plane only, which is all this mod needs).
    ///
    /// RimWorld 1.7's engine cannot be asked "does this font have a glyph for
    /// X", so the mod looks into the font file itself: it needs to know that
    /// the font works at all, and whether it carries the pre-placed harakat
    /// glyphs that fonts built by Tools/build_fonts.py contain.
    ///
    /// This reader was compared with fontTools' own cmap for all 83 fonts in
    /// this repository (6 mod fonts and 77 extra fonts) and gave identical results.
    /// </summary>
    internal sealed class SfntCoverage
    {
        private readonly HashSet<int> codePoints = new HashSet<int>();

        public int Count
        {
            get { return codePoints.Count; }
        }

        public bool Has(int codePoint)
        {
            return codePoints.Contains(codePoint);
        }

        /// <summary>Throws InvalidDataException when the file is not a usable font.</summary>
        public static SfntCoverage Read(byte[] font)
        {
            if (!SfntFile.LooksLikeFont(font))
                throw new InvalidDataException("not a TrueType/OpenType font");

            int tables = U16(font, 4);
            int cmap = -1;

            for (int k = 0; k < tables; k++)
            {
                int r = 12 + 16 * k;
                if (font[r] == (byte)'c' && font[r + 1] == (byte)'m' && font[r + 2] == (byte)'a' && font[r + 3] == (byte)'p')
                {
                    cmap = checked((int)U32(font, r + 8));
                    break;
                }
            }

            if (cmap < 0)
                throw new InvalidDataException("the font has no cmap table");

            var result = new SfntCoverage();
            int subtables = U16(font, cmap + 2);

            for (int k = 0; k < subtables; k++)
            {
                int r = cmap + 4 + 8 * k;
                int platform = U16(font, r);
                int encoding = U16(font, r + 2);
                int sub = checked(cmap + (int)U32(font, r + 4));

                bool unicode = platform == 0 || (platform == 3 && (encoding == 1 || encoding == 10));
                if (!unicode)
                    continue;

                int format = U16(font, sub);
                if (format == 4)
                    result.ReadFormat4(font, sub);
                else if (format == 12)
                    result.ReadFormat12(font, sub);
            }

            return result;
        }

        private void ReadFormat4(byte[] f, int sub)
        {
            int segmentsX2 = U16(f, sub + 6);
            int segments = segmentsX2 / 2;

            int endBase = sub + 14;
            int startBase = endBase + segmentsX2 + 2;
            int deltaBase = startBase + segmentsX2;
            int rangeBase = deltaBase + segmentsX2;

            for (int i = 0; i < segments; i++)
            {
                int end = U16(f, endBase + 2 * i);
                int start = U16(f, startBase + 2 * i);
                int delta = (short)U16(f, deltaBase + 2 * i);
                int rangeOffset = U16(f, rangeBase + 2 * i);

                if (start == 0xFFFF)
                    continue;                      // the closing segment

                for (int cp = start; cp <= end; cp++)
                {
                    int glyph;

                    if (rangeOffset == 0)
                    {
                        glyph = (cp + delta) & 0xFFFF;
                    }
                    else
                    {
                        int address = rangeBase + 2 * i + rangeOffset + 2 * (cp - start);
                        if (address + 2 > f.Length)
                            continue;

                        glyph = U16(f, address);
                        if (glyph != 0)
                            glyph = (glyph + delta) & 0xFFFF;
                    }

                    if (glyph != 0)
                        codePoints.Add(cp);
                }
            }
        }

        private void ReadFormat12(byte[] f, int sub)
        {
            long groups = U32(f, sub + 12);
            if (groups > (f.Length - sub - 16) / 12)
                throw new InvalidDataException("the cmap table is damaged");

            for (long g = 0; g < groups; g++)
            {
                int at = sub + 16 + 12 * (int)g;
                long start = U32(f, at);
                long end = U32(f, at + 4);
                long glyph = U32(f, at + 8);

                for (long cp = start; cp <= end && cp <= 0xFFFF; cp++)
                {
                    if (glyph + (cp - start) != 0)
                        codePoints.Add((int)cp);
                }
            }
        }

        private static int U16(byte[] b, int o)
        {
            if (o < 0 || o > b.Length - 2)
                throw new InvalidDataException("a font table lies outside of the file");

            return (b[o] << 8) | b[o + 1];
        }

        private static uint U32(byte[] b, int o)
        {
            if (o < 0 || o > b.Length - 4)
                throw new InvalidDataException("a font table lies outside of the file");

            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
        }
    }
}
