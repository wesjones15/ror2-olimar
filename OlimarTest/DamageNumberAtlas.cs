using System;
using System.Collections.Generic;

namespace OlimarTest
{
    // RoR2 1.2.4.4's texDamageNumbers: a 4x4 atlas, 0..9 followed by two blanks and K/M/B/!.
    // Atlas pixels are bottom row first (Unity); decoded disc glyphs are top row first.
    internal static class DamageNumberAtlas
    {
        public static void ReplaceDigits(byte[] atlas, int width, int height,
            IReadOnlyList<byte[]> digits, int glyphWidth, int glyphHeight)
            => ReplaceGlyphs(atlas, width, height, digits, glyphWidth, glyphHeight);

        public static void ReplaceGlyphs(byte[] atlas, int width, int height,
            IReadOnlyList<byte[]> digits, int glyphWidth, int glyphHeight)
        {
            if (width < 16 || height < 16 || width % 4 != 0 || height % 4 != 0 ||
                atlas.Length != width * height * 4 || (digits.Count != 10 && digits.Count != 16) || glyphWidth < 1 || glyphHeight < 1)
                throw new ArgumentException("Unexpected damage number atlas or digit dimensions");
            foreach (var digit in digits)
                if (digit != null && digit.Length != glyphWidth * glyphHeight * 4)
                    throw new ArgumentException("Incomplete Pikmin digit");

            int cw = width / 4, ch = height / 4;
            int px = Math.Max(1, cw * 6 / 128), py = Math.Max(1, ch * 6 / 128);
            for (int d = 0; d < digits.Count; d++)
            {
                var src = digits[d];
                if (src == null) continue;
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int to = ((height - 1 - (d / 4 * ch + y)) * width + d % 4 * cw + x) * 4;
                        if (x < px || x >= cw - px || y < py || y >= ch - py)
                        {
                            Array.Clear(atlas, to, 4);
                            continue;
                        }
                        // Bilinear resize with the original atlas's six-pixel cell margin.
                        float sx = Math.Max(0, Math.Min(glyphWidth - 1, (x - px + 0.5f) * glyphWidth / (cw - 2 * px) - 0.5f));
                        float sy = Math.Max(0, Math.Min(glyphHeight - 1, (y - py + 0.5f) * glyphHeight / (ch - 2 * py) - 0.5f));
                        int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, glyphWidth - 1), y1 = Math.Min(y0 + 1, glyphHeight - 1);
                        for (int c = 0; c < 4; c++)
                        {
                            float a = src[(y0 * glyphWidth + x0) * 4 + c], b = src[(y0 * glyphWidth + x1) * 4 + c];
                            float e = src[(y1 * glyphWidth + x0) * 4 + c], f = src[(y1 * glyphWidth + x1) * 4 + c];
                            float top = a + (b - a) * (sx - x0), bottom = e + (f - e) * (sx - x0);
                            atlas[to + c] = (byte)Math.Round(top + (bottom - top) * (sy - y0));
                        }
                    }
            }
        }
    }
}
