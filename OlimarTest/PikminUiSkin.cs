using UnityEngine;

namespace OlimarTest
{
    // The disc's p_bg.bti is a 40x24 bubble button. Preserve its round corners when
    // making wider count windows for both the lower HUD and Ship Repair screens.
    internal static class PikminUiSkin
    {
        internal static void Bubble(Texture2D texture, Rect rect)
        {
            if (!texture) return;
            float bx = Mathf.Min(14f, rect.width * .25f);
            float by = Mathf.Min(8f, rect.height * .25f);
            float[] x = { rect.xMin, rect.xMin + bx, rect.xMax - bx, rect.xMax };
            float[] y = { rect.yMin, rect.yMin + by, rect.yMax - by, rect.yMax };
            float[] u = { 0f, 14f / 40f, 26f / 40f, 1f };
            float[] v = { 1f, 1f - 8f / 24f, 8f / 24f, 0f };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    GUI.DrawTextureWithTexCoords(
                        new Rect(x[col], y[row], x[col + 1] - x[col], y[row + 1] - y[row]),
                        texture, new Rect(u[col], v[row + 1], u[col + 1] - u[col], v[row] - v[row + 1]), true);
        }

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
