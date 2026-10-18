using System;
using UnityEngine;

namespace OlimarTest
{
    internal partial class PikminView
    {
        Texture2D carryDigits;
        float nextCarryDigitLoad;
        readonly int[] carrierColours = new int[3];

        // The original LifeGaugeMgr loads intro/item_0_9.bti and GaugeInfo::refresh
        // stacks the required count, dash and current count above a lifted object
        // (pikmin-main/src/plugPikiColin/gauges.cpp). Keep that presentation while
        // tinting both values to the colour with the most attached carriers.
        void LoadCarryDigits()
        {
            if (carryDigits || !Plugin.AssetsReady || Time.unscaledTime < nextCarryDigitLoad) return;
            nextCarryDigitLoad = Time.unscaledTime + 5f;
            var dir = Native.CString(Plugin.AssetsDir);
            if (Native.pik_carry_digit_texture(dir, out var info, null, 0) == 0 ||
                info.width == 0 || info.width % 11 != 0 || info.height == 0) return;
            var source = new byte[checked((int)(info.width * info.height * 4))];
            if (Native.pik_carry_digit_texture(dir, out info, source, (uint)source.Length) == 0) return;
            // BTI decoding yields top-first rows; Unity's raw RGBA data is bottom-first.
            int stride = checked((int)info.width * 4);
            var pixels = new byte[source.Length];
            for (int y = 0; y < info.height; y++)
                Buffer.BlockCopy(source, y * stride, pixels, ((int)info.height - 1 - y) * stride, stride);
            carryDigits = new Texture2D((int)info.width, (int)info.height, TextureFormat.RGBA32, false)
                { name = "Pikmin carry digits", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            carryDigits.LoadRawTextureData(pixels);
            carryDigits.Apply(false, true);
        }

        void ReleaseCarryHud()
        {
            if (carryDigits) Destroy(carryDigits);
            carryDigits = null;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !world || !world.Running ||
                Ror2Common.RunAudio.Ended) return;
            var camera = Camera.main;
            if (!camera) return;
            LoadCarryDigits();
            for (int i = 0; i < world.PelletCount; i++)
            {
                var pellet = world.Pellets[i];
                if (pellet.state > 1 || pellet.carriers == 0) continue;
                Array.Clear(carrierColours, 0, carrierColours.Length);
                for (int j = 0; j < world.PikiCount; j++)
                {
                    var piki = world.Pikis[j];
                    if (piki.alive != 0 && piki.state == Native.PikiState.Carry &&
                        piki.pellet == pellet.id && piki.color < carrierColours.Length)
                        carrierColours[piki.color]++;
                }
                int dominant = 0;
                for (int color = 1; color < carrierColours.Length; color++)
                    if (carrierColours[color] > carrierColours[dominant]) dominant = color;
                var colorTint = carrierColours[dominant] > 0 ? PikiColours[dominant] : Color.white;
                var above = UnityHost.ToUnity(pellet.pos) + Vector3.up *
                    Mathf.Max(1.4f, pellet.radius * UnityHost.Scale * Mathf.Max(0.05f, pellet.scale));
                var screen = camera.WorldToScreenPoint(above);
                if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
                    screen.y < 0f || screen.y > Screen.height) continue;
                DrawCarryCount(screen.x, Screen.height - screen.y, (int)pellet.carriers,
                    (int)pellet.min_carriers, colorTint);
            }
        }

        void DrawCarryCount(float x, float y, int current, int required, Color tint)
        {
            float size = Mathf.Clamp(Screen.height / 1080f * 31f, 18f, 52f);
            if (!carryDigits)
            {
                float scale = Screen.height / 1080f;
                ShipRepairText.Draw($"{current} / {required}", x / scale, (y - 58f) / scale,
                    24f, tint, true, 120f, 36f);
                return;
            }
            var saved = GUI.color;
            GUI.color = tint;
            // GaugeInfo::refresh shrinks the less relevant value to 75%.
            bool lifting = current >= required;
            DrawCarryNumber(x, y - size * 2.1f, current, size * (lifting ? 1f : 0.75f));
            DrawCarryGlyph(x, y - size * 1.1f, 10, size * 0.75f);
            DrawCarryNumber(x, y - size * 0.2f, required, size * (lifting ? 0.75f : 1f));
            GUI.color = saved;
        }

        void DrawCarryNumber(float x, float y, int value, float size)
        {
            string digits = Mathf.Clamp(value, 0, 99).ToString();
            float advance = size * 0.78f;
            float start = x - (digits.Length - 1) * advance * 0.5f;
            for (int i = 0; i < digits.Length; i++)
                DrawCarryGlyph(start + i * advance, y, digits[i] - '0', size);
        }

        void DrawCarryGlyph(float x, float y, int glyph, float size)
        {
            GUI.DrawTextureWithTexCoords(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                carryDigits, new Rect(glyph / 11f, 0f, 1f / 11f, 1f));
        }
    }
}
