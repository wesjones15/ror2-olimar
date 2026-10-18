using UnityEngine;

namespace OlimarTest
{
    // The disc's w08_160.bti is the glossy 160x88 number window used by Pikmin's
    // menus. Nine-slice its edges so HUD and results use the same original frame.
    internal static class PikminUiSkin
    {
        internal static void Bubble(Texture2D texture, Rect rect) => Bubble(texture, rect, Color.white);

        internal static void Bubble(Texture2D texture, Rect rect, Color tint)
        {
            if (!texture) return;
            float bx = Mathf.Min(24f, rect.width * .25f);
            float by = Mathf.Min(18f, rect.height * .25f);
            float x0 = rect.xMin, x1 = x0 + bx, x3 = rect.xMax, x2 = x3 - bx;
            float y0 = rect.yMin, y1 = y0 + by, y3 = rect.yMax, y2 = y3 - by;
            const float u1 = 24f / 160f, u2 = 136f / 160f;
            const float v1 = 18f / 88f, v2 = 1f - 18f / 88f;
            var before = GUI.color;
            GUI.color = before * tint;
            Slice(texture, x0, y0, x1, y1, 0f, v2, u1, 1f);
            Slice(texture, x1, y0, x2, y1, u1, v2, u2, 1f);
            Slice(texture, x2, y0, x3, y1, u2, v2, 1f, 1f);
            Slice(texture, x0, y1, x1, y2, 0f, v1, u1, v2);
            Slice(texture, x1, y1, x2, y2, u1, v1, u2, v2);
            Slice(texture, x2, y1, x3, y2, u2, v1, 1f, v2);
            Slice(texture, x0, y2, x1, y3, 0f, 0f, u1, v1);
            Slice(texture, x1, y2, x2, y3, u1, 0f, u2, v1);
            Slice(texture, x2, y2, x3, y3, u2, 0f, 1f, v1);
            GUI.color = before;
        }

        static void Slice(Texture2D texture, float x0, float y0, float x1, float y1,
            float u0, float v0, float u1, float v1) =>
            GUI.DrawTextureWithTexCoords(new Rect(x0, y0, x1 - x0, y1 - y0), texture,
                new Rect(u0, v0, u1 - u0, v1 - v0), true);

        internal static void Digits(Texture2D[] digits, int value, Rect rect, int minDigits = 1)
        {
            string count = Mathf.Max(0, value).ToString().PadLeft(minDigits, '0');
            float cell = Mathf.Min(rect.height * .8f, rect.width / count.Length);
            float x = rect.center.x - count.Length * cell * .5f;
            for (int i = 0; i < count.Length; i++)
            {
                var texture = digits[count[i] - '0'];
                if (texture) GUI.DrawTexture(new Rect(x, rect.y + (rect.height - cell) * .5f, cell, cell), texture);
                x += cell;
            }
        }
    }
}
