using System;
using System.IO;
using OlimarTest;
using Ror2Common;

static class NumberAtlasCheck
{
    // Optional original/output RGBA files let a preview use the exact game's atlas builder.
    public static void Run(string assets, Action<bool, string> check, string original = null, string output = null)
    {
        var synthetic = new byte[32 * 32 * 4];
        Array.Fill(synthetic, (byte)127);
        var digits = new byte[10][];
        for (int d = 0; d < 10; d++)
        {
            digits[d] = new byte[16];
            for (int p = 0; p < 4; p++)
            {
                digits[d][p * 4] = (byte)(d + 10 + p / 2 * 20);
                digits[d][p * 4 + 3] = 255;
            }
        }
        DamageNumberAtlas.ReplaceDigits(synthetic, 32, 32, digits, 2, 2);
        bool mapping = true, suffixes = true;
        for (int d = 0; d < 10; d++)
        {
            int top = ((31 - (d / 4 * 8 + 1)) * 32 + d % 4 * 8 + 1) * 4;
            int bottom = ((31 - (d / 4 * 8 + 6)) * 32 + d % 4 * 8 + 1) * 4;
            mapping &= synthetic[top] == d + 10 && synthetic[bottom] == d + 30;
        }
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                if ((31 - y) / 8 * 4 + x / 8 >= 10)
                    for (int c = 0; c < 4; c++) suffixes &= synthetic[(y * 32 + x) * 4 + c] == 127;
        check(mapping, "damage digit cells and top/bottom orientation");
        check(suffixes, "all non-digit atlas cells preserved byte for byte");

        var dir = Native.CString(assets);
        bool loaded = true;
        for (uint d = 0; d < 10; d++)
        {
            digits[d] = new byte[64 * 64 * 4];
            loaded &= Native.pik_number_texture(dir, d, 0, out ViewTexture info, digits[d], (uint)digits[d].Length) != 0
                && info.width == 64 && info.height == 64;
        }
        check(loaded, "ten Pikmin HUD digits decoded through P/Invoke");
        var shortBuffer = new byte[] { 123, 123, 123, 123 };
        check(Native.pik_number_texture(dir, 0, 0, out _, shortBuffer, 4) == 0 && Array.TrueForAll(shortBuffer, b => b == 123),
            "digit decoder refuses short buffers without writing");
        if (original != null && output != null && loaded)
        {
            var atlas = File.ReadAllBytes(original);
            DamageNumberAtlas.ReplaceDigits(atlas, 512, 512, digits, 64, 64);
            File.WriteAllBytes(output, atlas);
            Console.WriteLine($"wrote damage atlas preview: {output}");
        }
    }
}
