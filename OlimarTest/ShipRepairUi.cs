using System;
using System.Collections.Generic;
using System.Linq;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // English screen textures are decoded from the player's disc by libpikmin.
    // Nothing extracted from the disc is bundled with the plugin.
    internal static class ShipRepairUi
    {
        static readonly Dictionary<uint, Texture2D> icons = new Dictionary<uint, Texture2D>();
        static readonly Texture2D[] digits = new Texture2D[10];
        static string loadedFrom;
        static GUIStyle textStyle, locationStyle;
        static Font locationFont;
        static readonly Color cream = new Color(1f, .94f, .72f);
        static readonly Color gold = new Color(1f, .78f, .28f);
        static readonly Color dim = new Color(.62f, .71f, .65f);
        static readonly Color panel = new Color(.045f, .105f, .12f, .96f);

        static void Load()
        {
            if (!Plugin.AssetsReady || string.IsNullOrEmpty(Plugin.AssetsDir)) return;
            if (loadedFrom == Plugin.AssetsDir) return;
            foreach (var texture in icons.Values.Concat(digits)) if (texture) UnityEngine.Object.Destroy(texture);
            icons.Clear(); loadedFrom = Plugin.AssetsDir;
            var path = Native.CString(loadedFrom);
            foreach (uint id in new uint[] { 7, 8, 9, 10, 19, 21 })
            {
                if (Native.pik_hud_icon_texture(path, id, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_hud_icon_texture(path, id, out info, rgba, (uint)rgba.Length) != 0)
                    icons[id] = Texture(info, rgba, "Ship Repair icon " + id);
            }
            for (uint i = 0; i < digits.Length; i++)
            {
                if (Native.pik_number_texture(path, i, 0, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_number_texture(path, i, 0, out info, rgba, (uint)rgba.Length) != 0)
                    digits[i] = Texture(info, rgba, "Ship Repair bubble digit " + i);
            }
        }

        static Texture2D Texture(ViewTexture info, byte[] topFirst, string name)
        {
            int stride = checked((int)info.width * 4);
            var bottomFirst = new byte[topFirst.Length];
            for (int row = 0; row < info.height; row++)
                Buffer.BlockCopy(topFirst, row * stride, bottomFirst, ((int)info.height - row - 1) * stride, stride);
            var texture = new Texture2D((int)info.width, (int)info.height, TextureFormat.RGBA32, false)
                { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.LoadRawTextureData(bottomFirst);
            texture.Apply(false, true);
            return texture;
        }

        static void Fill(Rect rect, Color color)
        {
            var before = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }
        static void Icon(uint id, Rect rect)
        {
            if (icons.TryGetValue(id, out var icon) && icon) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }
        static string Short(string value, int max) => string.IsNullOrEmpty(value) ? "CAPTAIN" :
            value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        static void Text(string value, float x, float y, float size, Color color, bool centered = false)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            textStyle.fontSize = Mathf.RoundToInt(size * .8f);
            textStyle.normal.textColor = color;
            var content = new GUIContent(value);
            float width = textStyle.CalcSize(content).x;
            GUI.Label(new Rect(centered ? x - width * .5f : x, y, width + 8f, size + 6f), content, textStyle);
        }

        static void BubbleNumber(int value, Rect rect, int minimumDigits = 1, Color? tint = null)
        {
            PikminUiSkin.Bubble(icons.TryGetValue(21, out var bubble) ? bubble : null,
                rect, tint ?? Color.white);
            PikminUiSkin.Digits(digits, value,
                new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, rect.height - 10f), minimumDigits);
        }

        static void BubbleFraction(int value, int maximum, Rect rect)
        {
            PikminUiSkin.Bubble(icons.TryGetValue(21, out var bubble) ? bubble : null, rect);
            PikminUiSkin.Digits(digits, value,
                new Rect(rect.x + 16f, rect.y + 8f, 86f, rect.height - 16f), 2);
            Text("/", rect.center.x, rect.y + 4f, rect.height - 5f, cream, true);
            PikminUiSkin.Digits(digits, maximum,
                new Rect(rect.xMax - 104f, rect.y + 8f, 86f, rect.height - 16f), 2);
        }

        static void LocationText(string value, float center, float y, int size, Color color)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (locationStyle == null)
            {
                locationFont = Font.CreateDynamicFontFromOSFont(new[] { "Comic Sans MS", "Segoe Print" }, 64);
                locationStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                    font = locationFont, fontStyle = FontStyle.Bold };
            }
            locationStyle.fontSize = size;
            var rect = new Rect(center - 690f, y, 1380f, size * 1.65f);
            locationStyle.normal.textColor = new Color(.1f, .045f, .015f, color.a);
            for (int dx = -2; dx <= 2; dx += 2)
                for (int dy = -2; dy <= 2; dy += 2)
                    GUI.Label(new Rect(rect.x + dx, rect.y + dy, rect.width, rect.height), value, locationStyle);
            locationStyle.normal.textColor = color;
            GUI.Label(rect, value, locationStyle);
        }

        static float Begin()
        {
            Load();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            return Screen.width / scale;
        }

        internal static void Lobby(string error)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            // Keep the banner clear of the survivor portraits on the left and
            // the difficulty/settings column on the right.
            float left = width * .36f, right = width * .76f;
            float w = Mathf.Min(600f, right - left - 12f);
            float x = left + (right - left - w) * .5f, y = 75f;
            Fill(new Rect(x, y, w, 218f), panel);
            Fill(new Rect(x, y, w, 5f), gold);
            Icon(7, new Rect(x + 17f, y + 37f, 81f, 112f));
            Text("SHIP REPAIR", x + 105f, y + 15f, 43f, cream);
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            textStyle.wordWrap = true;
            textStyle.fontSize = 17;
            textStyle.normal.textColor = gold;
            GUI.Label(new Rect(x + 105f, y + 67f, w - 123f, 57f),
                "Compete with your friends to repair your Dolphin first!", textStyle);
            textStyle.normal.textColor = cream;
            GUI.Label(new Rect(x + 105f, y + 124f, w - 123f, 72f),
                "Build your pikmin army! you get bonus points at the end for your pikmin squad size.", textStyle);
            textStyle.wordWrap = false;
            if (!string.IsNullOrEmpty(error)) Text(error, x + w * .5f, y + 192f, 19f, new Color(1f, .45f, .4f), true);
            GUI.matrix = old;
        }

        internal static void Hud(int score, IEnumerable<ShipRepairSession.ResultLine> players,
            bool competitive, string site, string subtitle, float siteUntil)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            float center = width * .5f;
            Icon(19, new Rect(center - 159f, 26f, 73f, 73f));
            Icon(7, new Rect(center - 145f, 31f, 47f, 61f));
            BubbleFraction(score, 30, new Rect(center - 83f, 30f, 245f, 65f));
            Leaderboard(players, width - 328f, 138f, 305f, 34f, competitive);
            if (!string.IsNullOrEmpty(site) && Time.unscaledTime < siteUntil)
            {
                float alpha = Mathf.Clamp01(siteUntil - Time.unscaledTime);
                float opacity = Mathf.Min(1f, alpha * 1.5f);
                Text("DAY " + (RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1),
                    center, 363f, 26f, new Color(cream.r, cream.g, cream.b, opacity), true);
                LocationText(site, center, 401f, 55, new Color(.9f, .53f, .12f, opacity));
                if (!string.IsNullOrEmpty(subtitle))
                    Text(subtitle, center, 493f, 26f, new Color(.92f, .9f, .79f, opacity), true);
            }
            GUI.matrix = old;
        }

        static void Leaderboard(IEnumerable<ShipRepairSession.ResultLine> players, float x, float y, float width, float row, bool competitive)
        {
            var rows = players.ToArray();
            Fill(new Rect(x, y, width, 42f + rows.Length * row), panel);
            Fill(new Rect(x, y, width, 3f), gold);
            Text(competitive ? "DOLPHIN RACE" : "DOLPHIN CREW", x + width * .5f, y + 9f, 22f, cream, true);
            for (int i = 0; i < rows.Length; i++)
            {
                var p = rows[i]; float at = y + 46f + i * row;
                if (p.Winner) Fill(new Rect(x + 5f, at - 3f, width - 10f, row - 2f), new Color(.43f, .32f, .08f, .75f));
                Text((i + 1) + ". " + Short(p.Name, 16), x + 12f, at, 19f, p.Winner ? gold : cream);
                BubbleNumber(p.Parts, new Rect(x + width - 86f, at - 3f, 74f, 29f), 2);
            }
        }

        internal static void Results(string detail, IEnumerable<ShipRepairSession.ResultLine> players, bool won)
        {
            if (Event.current.type != EventType.Repaint) return;
            int oldDepth = GUI.depth;
            GUI.depth = -1000;
            var old = GUI.matrix; float width = Begin();
            // RoR2's bottom continuation prompt and ready indicators remain visible.
            Fill(new Rect(0f, 0f, width, 994f), new Color(.015f, .04f, .09f, .98f));
            float center = width * .5f;
            Fill(new Rect(center - 485f, 57f, 970f, 299f), new Color(.035f, .12f, .2f, .93f));
            Fill(new Rect(center - 485f, 57f, 970f, 4f), new Color(.17f, .49f, .76f));
            Fill(new Rect(center - 485f, 352f, 970f, 4f), new Color(.17f, .49f, .76f));
            Icon(7, new Rect(center - 67f, 79f, 134f, 134f));
            Text(won ? "DOLPHIN REPAIRED" : "SHIP REPAIR ENDED", center, 224f, 53f, cream, true);
            Text(detail.ToUpperInvariant(), center, 290f, 24f, dim, true);
            var rows = players.ToArray();
            float x = center - 445f, y = 405f;
            float rowHeight = Mathf.Min(68f, 475f / Mathf.Max(1, rows.Length));
            float textSize = Mathf.Min(35f, rowHeight * .58f);
            Fill(new Rect(x, y, 890f, 57f + rows.Length * rowHeight), new Color(.035f, .12f, .2f, .95f));
            Fill(new Rect(x, y, 890f, 4f), new Color(.17f, .49f, .76f));
            Fill(new Rect(x, y + 53f + rows.Length * rowHeight, 890f, 4f), new Color(.17f, .49f, .76f));
            Text("CAPTAINS", x + 31f, y + 15f, 28f, gold);
            Text("PARTS", x + 540f, y + 15f, 25f, gold, true);
            Text("PIKMIN", x + 680f, y + 15f, 25f, gold, true);
            Text("SCORE", x + 817f, y + 15f, 25f, gold, true);
            for (int i = 0; i < rows.Length; i++)
            {
                var p = rows[i]; float at = y + 62f + i * rowHeight;
                if (p.Winner) Fill(new Rect(x + 12f, at - 4f, 866f, rowHeight - 2f), new Color(.35f, .29f, .09f, .92f));
                if (p.Winner) Icon(10, new Rect(x + 19f, at, rowHeight - 10f, rowHeight - 10f));
                Text(Short(p.Name, 21).ToUpperInvariant(), x + 74f, at, textSize, p.Winner ? gold : cream);
                BubbleNumber(p.Parts, new Rect(x + 482f, at - 4f, 116f, rowHeight - 5f), 2,
                    new Color(.95f, .8f, .78f));
                BubbleNumber(p.Pikmin, new Rect(x + 617f, at - 4f, 126f, rowHeight - 5f), 3,
                    new Color(.83f, .9f, .99f));
                BubbleNumber(p.Score, new Rect(x + 759f, at - 4f, 111f, rowHeight - 5f), 2,
                    p.Winner ? new Color(1f, .94f, .62f) : Color.white);
            }
            Icon(8, new Rect(center - 132f, 907f, 49f, 49f));
            Text("CONTINUE", center + 15f, 915f, 25f, cream, true);
            GUI.matrix = old;
            GUI.depth = oldDepth;
        }
    }
}
