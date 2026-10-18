using System;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    internal partial class OlimarDriver
    {
        // drawGameInfo.cpp's lower play10.blo uses NaviTexCallBack, LifeIconCallBack,
        // PikiIconCallBack and the formation/map/total number callbacks. These are
        // decoded from the player's extracted screen/eng_tex files, never bundled.
        readonly Texture2D[] bottomIcons = new Texture2D[7];
        readonly Texture2D[] bottomDigits = new Texture2D[10];
        float bottomLoadAt;
        GUIStyle bottomLabel;

        void DisposeBottomHud()
        {
            foreach (var texture in bottomIcons) if (texture) Destroy(texture);
            foreach (var texture in bottomDigits) if (texture) Destroy(texture);
        }

        void LoadBottomHud()
        {
            if (!Plugin.AssetsReady || Time.unscaledTime < bottomLoadAt) return;
            bottomLoadAt = Time.unscaledTime + 5f;
            var path = Native.CString(Plugin.AssetsDir);
            for (uint i = 0; i < bottomIcons.Length; i++)
            {
                if (bottomIcons[i]) continue;
                if (Native.pik_hud_icon_texture(path, i, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_hud_icon_texture(path, i, out info, rgba, (uint)rgba.Length) != 0)
                    bottomIcons[i] = HudTexture(info, rgba, "Pikmin lower HUD icon " + i);
            }
            for (uint i = 0; i < bottomDigits.Length; i++)
            {
                if (bottomDigits[i]) continue;
                if (Native.pik_number_texture(path, i, 0, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_number_texture(path, i, 0, out info, rgba, (uint)rgba.Length) != 0)
                    bottomDigits[i] = HudTexture(info, rgba, "Pikmin lower HUD digit " + i);
            }
        }

        static Texture2D HudTexture(ViewTexture info, byte[] topFirst, string name)
        {
            int stride = checked((int)info.width * 4);
            var bottomFirst = new byte[topFirst.Length];
            for (int row = 0; row < info.height; row++)
                Buffer.BlockCopy(topFirst, row * stride, bottomFirst,
                    ((int)info.height - row - 1) * stride, stride);
            var texture = new Texture2D((int)info.width, (int)info.height, TextureFormat.RGBA32, false)
                { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.LoadRawTextureData(bottomFirst);
            texture.Apply(false, true);
            return texture;
        }

        void BottomHud()
        {
            var world = PikminWorld.Instance;
            if (Event.current.type != EventType.Repaint || localUser == null || !world || !Active ||
                DataCaptain < 0 || Ror2Common.RunAudio.Ended) return;
            MenuStyles();
            LoadBottomHud();
            int field = 0, free = world.SproutCount, squad = 0;
            int[] colours = { 0, 0, 0 };
            int heldColour = -1;
            int nearestColour = -1;
            float nearestDist = float.PositiveInfinity;
            var captainPos = HasState ? State.pos : UnityHost.ToSim(Body.footPosition);
            for (int i = 0; i < world.PikiCount; i++)
            {
                var p = world.Pikis[i];
                if (p.alive == 0 || p.state == Native.PikiState.Dead) continue;
                field++;
                bool formed = p.state == Native.PikiState.Squad || p.state == Native.PikiState.GoHang ||
                    p.state == Native.PikiState.Held;
                if (!formed) { free++; continue; }
                if (p.captain != DataCaptain) continue;
                squad++;
                if (p.color < colours.Length) colours[p.color]++;
                if (p.state == Native.PikiState.Held) heldColour = p.color;
                if (p.state == Native.PikiState.Squad)
                {
                    float dx = p.pos.x - captainPos.x, dz = p.pos.z - captainPos.z;
                    float dist = dx * dx + dz * dz;
                    if (dist < nearestDist) { nearestDist = dist; nearestColour = p.color; }
                }
            }
            int stored = 0;
            for (int i = 0; i < world.OnionCount; i++)
            {
                var onion = world.Onions[i];
                if (onion.is_ship == 0)
                    stored += (int)(onion.stored0 + onion.stored1 + onion.stored2);
            }
            int total = field + world.SproutCount + stored;
            int selected = LockedOn ? -1 : world.ThrowColour(DataCaptain);
            int next = heldColour >= 0 ? heldColour
                : selected >= 0 && selected < colours.Length && colours[selected] > 0 ? selected
                : nearestColour;

            float scale = Screen.height / 1080f;
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float y = 1080f - 129f;
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(20f, y, 690f, 108f), panelTex);
            GUI.color = savedColor;
            if (bottomIcons[0]) GUI.DrawTexture(new Rect(30f, y + 15f, 63f, 84f), bottomIcons[0], ScaleMode.ScaleToFit);
            if (bottomIcons[1]) GUI.DrawTexture(new Rect(103f, y + 20f, 67f, 67f), bottomIcons[1]);
            DrawHealthRing(136.5f, y + 53.5f, health && health.fullHealth > 0f
                ? Mathf.Clamp01(health.health / health.fullHealth) : 0f);
            int icon = next >= 0 && next <= 2 ? next + 4 : 3;
            if (bottomIcons[icon]) GUI.DrawTexture(new Rect(190f, y + 19f, 68f, 68f), bottomIcons[icon]);
            if (bottomLabel == null)
                bottomLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 17,
                    fontStyle = FontStyle.Bold };
            bottomLabel.normal.textColor = new Color(0.8f, 0.88f, 0.74f);
            BottomCount(282f, y, "FREE", free);
            BottomCount(415f, y, "SQUAD", squad);
            BottomCount(558f, y, "TOTAL", total);
            GUI.color = savedColor;
            GUI.matrix = savedMatrix;
        }

        void DrawHealthRing(float x, float y, float ratio)
        {
            var matrix = GUI.matrix;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                float px = x + Mathf.Sin(angle) * 39f;
                float py = y - Mathf.Cos(angle) * 39f;
                GUI.color = i < Mathf.CeilToInt(ratio * 32f)
                    ? Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(0.25f, 1f, 0.25f), ratio)
                    : new Color(0.2f, 0.2f, 0.2f, 0.8f);
                GUIUtility.RotateAroundPivot(i * 360f / 32f, new Vector2(px, py));
                GUI.DrawTexture(new Rect(px - 2.5f, py - 6f, 5f, 12f), stripeTex);
                GUI.matrix = matrix;
            }
            GUI.color = Color.white;
        }

        void BottomCount(float x, float y, string label, int count)
        {
            // play10.blo's i_base64 bubble frames the formation/map/total counters.
            if (bottomIcons[2]) GUI.DrawTexture(new Rect(x + 13f, y + 25f, 88f, 78f), bottomIcons[2]);
            GUI.Label(new Rect(x, y + 4f, 130f, 26f), label, bottomLabel);
            string digits = Mathf.Max(0, count).ToString();
            float digitWidth = digits.Length > 3 ? 24f : 31f;
            float at = x + 57f - digitWidth * digits.Length * 0.5f;
            for (int i = 0; i < digits.Length; i++)
            {
                int digit = digits[i] - '0';
                if (bottomDigits[digit]) GUI.DrawTexture(new Rect(at, y + 47f, digitWidth, 36f), bottomDigits[digit]);
                else GUI.Label(new Rect(at, y + 43f, digitWidth, 47f), digits[i].ToString(), hudStyle);
                at += digitWidth;
            }
        }
    }
}
