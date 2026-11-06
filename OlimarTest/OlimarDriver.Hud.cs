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
        readonly Texture2D[] bottomIcons = new Texture2D[22];
        readonly Texture2D[] bottomDigits = new Texture2D[10];
        readonly HudDigitChanges freeDigits = new HudDigitChanges(), squadDigits = new HudDigitChanges(), totalDigits = new HudDigitChanges();
        Texture2D lifeCircle;
        int lifeCircleStep = -1;
        float lastLife = 1f, lifeHurtAt;
        float bottomLoadAt;
        GUIStyle bottomLabel;

        void DisposeBottomHud()
        {
            foreach (var texture in bottomIcons) if (texture) Destroy(texture);
            foreach (var texture in bottomDigits) if (texture) Destroy(texture);
            if (lifeCircle) Destroy(lifeCircle);
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
            // The player's own HUD, or that of the Olimar they are spectating (his captain is known on every machine).
            bool watched = localUser == null && Ror2Common.LocalUsers.IsSpectated(Body) && (!health || health.alive);
            if (Event.current.type != EventType.Repaint || !world || (!watched && (localUser == null || !Active)) ||
                DataCaptain < 0 || Ror2Common.RunAudio.Ended) return;
            MenuStyles();
            LoadBottomHud();
            int field = 0, free = world.SproutCount, squad = 0;
            int[] colours = new int[Native.ColourCount];
            int heldColour = -1, heldStage = 0;
            int nearestColour = -1, nearestStage = 0, selectedStage = 0;
            float nearestDist = float.PositiveInfinity;
            float selectedDist = float.PositiveInfinity;
            int selected = LockedOn ? -1 : world.ThrowColour(DataCaptain);
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
                if (p.state == Native.PikiState.Held) { heldColour = p.color; heldStage = p.happa; }
                if (p.state == Native.PikiState.Squad)
                {
                    float dx = p.pos.x - captainPos.x, dz = p.pos.z - captainPos.z;
                    float dist = dx * dx + dz * dz;
                    if (dist < nearestDist) { nearestDist = dist; nearestColour = p.color; nearestStage = p.happa; }
                    if (p.color == selected && dist < selectedDist)
                    {
                        selectedDist = dist;
                        selectedStage = p.happa;
                    }
                }
            }
            int stored = 0;
            for (int i = 0; i < world.OnionCount; i++)
            {
                var onion = world.Onions[i];
                if (onion.is_ship == 0)
                    stored += (int)onion.StoredTotal;
            }
            int total = field + world.SproutCount + stored;
            int next = heldColour >= 0 ? heldColour
                : selected >= 0 && selected < colours.Length && colours[selected] > 0 ? selected
                : nearestColour;
            int stage = heldColour >= 0 ? heldStage : next == selected ? selectedStage : nearestStage;

            float scale = Screen.height / 1080f;
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float y = 1080f - 165f;
            GUI.color = new Color(0f, .04f, .055f, .7f);
            GUI.DrawTexture(new Rect(20f, y, 980f, 143f), panelTex);
            GUI.color = savedColor;
            float life = health && health.fullHealth > 0f
                ? Mathf.Clamp01(health.health / health.fullHealth) : 0f;
            // play10.blo has one n_64 Olimar bubble, a separately drawn life circle,
            // and a piki bubble. navi01 is a different full-body asset, not a HUD icon.
            if (life < lastLife - .001f) lifeHurtAt = Time.unscaledTime;
            lastLife = life;
            float pulse = Mathf.Max(0f, 1f - (Time.unscaledTime - lifeHurtAt) * 2f);
            float portraitSize = 106f + pulse * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 28f)) * 9f;
            if (bottomIcons[1]) GUI.DrawTexture(new Rect(39f - (portraitSize - 106f) * .5f,
                y + 20f - (portraitSize - 106f) * .5f, portraitSize, portraitSize), bottomIcons[1]);
            if (bottomIcons[19]) GUI.DrawTexture(new Rect(148f, y + 24f, 101f, 101f), bottomIcons[19]);
            DrawLifeCircle(new Rect(160f, y + 36f, 77f, 77f), life);
            if (bottomIcons[20] && life > 0f && (life < .5f || pulse > 0f))
            {
                GUI.color = new Color(1f, 1f, 1f,
                    (life < .5f ? .45f : pulse) * (1f - Mathf.Cos(Time.unscaledTime * (life < .2f ? 13f : 8f))) * .5f);
                GUI.DrawTexture(new Rect(148f, y + 24f, 101f, 101f), bottomIcons[20]);
                GUI.color = savedColor;
            }
            int icon = next >= 0 && next <= 2 ? (stage >= 2 ? 16 : stage == 1 ? 13 : 4) + next : 3;
            if (bottomIcons[icon]) GUI.DrawTexture(new Rect(276f, y + 19f, 108f, 108f), bottomIcons[icon]);
            if (bottomIcons[12]) GUI.DrawTexture(new Rect(386f, y + 60f, 39f, 39f), bottomIcons[12]);
            if (bottomLabel == null)
                bottomLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 19,
                    fontStyle = FontStyle.Bold };
            bottomLabel.normal.textColor = new Color(0.8f, 0.88f, 0.74f);
            BottomCount(438f, y, "FREE", free, 3, freeDigits);
            BottomCount(619f, y, "SQUAD", squad, 3, squadDigits);
            BottomCount(800f, y, "TOTAL", total, 4, totalDigits);
            GUI.color = savedColor;
            GUI.matrix = savedMatrix;
        }

        void DrawLifeCircle(Rect rect, float ratio)
        {
            int step = Mathf.RoundToInt(ratio * 128f);
            if (!lifeCircle || lifeCircleStep != step)
            {
                if (!lifeCircle) lifeCircle = new Texture2D(128, 128, TextureFormat.RGBA32, false)
                    { name = "Pikmin life circle", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                lifeCircleStep = step;
                var pixels = new Color[128 * 128];
                Color live = ratio < .2f ? new Color(1f, .15f, .1f) :
                    ratio < .5f ? new Color(1f, .92f, .1f) : new Color(.1f, 1f, .19f);
                for (int py = 0; py < 128; py++)
                    for (int px = 0; px < 128; px++)
                    {
                        float dx = (px - 63.5f) / 62f, dy = (py - 63.5f) / 62f;
                        float radius = Mathf.Sqrt(dx * dx + dy * dy);
                        if (radius > 1f) continue;
                        float angle = Mathf.Atan2(-dx, dy);
                        if (angle < 0f) angle += Mathf.PI * 2f;
                        bool filled = angle <= step * Mathf.PI * 2f / 128f;
                        Color c = filled ? live * (.72f + .28f * (1f - radius)) : new Color(.055f, .13f, .07f);
                        if (radius > .9f) c = Color.Lerp(c, new Color(.83f, .95f, .78f), (radius - .9f) * 4f);
                        if (filled && px < 55 && py > 72) c = Color.Lerp(c, Color.white, .2f);
                        c.a = Mathf.Clamp01((1f - radius) * 24f);
                        pixels[py * 128 + px] = c;
                    }
                lifeCircle.SetPixels(pixels);
                lifeCircle.Apply(false, false);
            }
            GUI.DrawTexture(rect, lifeCircle);
        }

        void BottomCount(float x, float y, string label, int count, int minimumDigits, HudDigitChanges changes)
        {
            GUI.Label(new Rect(x, y + 5f, 165f, 26f), label, bottomLabel);
            PikminUiSkin.Bubble(bottomIcons[21], new Rect(x, y + 33f, 165f, 89f));
            PikminUiSkin.Digits(bottomDigits, count, new Rect(x + 7f, y + 45f, 151f, 61f), minimumDigits, changes);
        }
    }
}
