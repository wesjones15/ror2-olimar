using System;
using System.Collections.Generic;
using System.Linq;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // The disc's bigFont and English screen textures are decoded by libpikmin.
    // Nothing extracted from the disc is bundled with the plugin.
    internal static class ShipRepairUi
    {
        static readonly Dictionary<char, Texture2D> glyphs = new Dictionary<char, Texture2D>();
        static readonly Dictionary<uint, Texture2D> icons = new Dictionary<uint, Texture2D>();
        static string loadedFrom;
        static GUIStyle fallback;
        static readonly Color cream = new Color(1f, .94f, .72f);
        static readonly Color gold = new Color(1f, .78f, .28f);
        static readonly Color dim = new Color(.62f, .71f, .65f);
        static readonly Color panel = new Color(.045f, .105f, .12f, .96f);

        static void Load()
        {
            if (!Plugin.AssetsReady || string.IsNullOrEmpty(Plugin.AssetsDir)) return;
            if (loadedFrom == Plugin.AssetsDir) return;
            foreach (var texture in glyphs.Values.Concat(icons.Values)) if (texture) UnityEngine.Object.Destroy(texture);
            glyphs.Clear(); icons.Clear(); loadedFrom = Plugin.AssetsDir;
            var path = Native.CString(loadedFrom);
            foreach (uint id in new uint[] { 7, 8, 9, 10, 11 })
            {
                if (Native.pik_hud_icon_texture(path, id, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_hud_icon_texture(path, id, out info, rgba, (uint)rgba.Length) != 0)
                    icons[id] = Texture(info, rgba, "Ship Repair icon " + id);
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

        static Texture2D Glyph(char c)
        {
            if (glyphs.TryGetValue(c, out var glyph)) return glyph;
            glyphs[c] = null;
            if (c < '!' || c > '~' || loadedFrom == null) return null;
            var path = Native.CString(loadedFrom);
            if (Native.pik_font_glyph_texture(path, c, out var info, null, 0) == 0) return null;
            var rgba = new byte[checked((int)(info.width * info.height * 4))];
            if (Native.pik_font_glyph_texture(path, c, out info, rgba, (uint)rgba.Length) == 0) return null;
            return glyphs[c] = Texture(info, rgba, "Pikmin font " + c);
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
        static float Width(string value, float size) => value.Length * size * .62f;
        static string Short(string value, int max) => string.IsNullOrEmpty(value) ? "CAPTAIN" :
            value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        static void Text(string value, float x, float y, float size, Color color, bool centered = false)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (centered) x -= Width(value, size) * .5f;
            var before = GUI.color; GUI.color = color;
            foreach (char c in value)
            {
                if (c == ' ') { x += size * .42f; continue; }
                var glyph = Glyph(c);
                if (glyph) GUI.DrawTexture(new Rect(x, y, size * .66f, size), glyph);
                else
                {
                    if (fallback == null) fallback = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                    fallback.fontSize = Mathf.RoundToInt(size * .8f);
                    fallback.normal.textColor = Color.white;
                    GUI.Label(new Rect(x, y, size, size), c.ToString(), fallback);
                }
                x += size * .62f;
            }
            GUI.color = before;
        }

        static float Begin()
        {
            Load();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            return Screen.width / scale;
        }

        internal static void Lobby(string rule, string error)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            float x = (width - 880f) * .5f, y = 95f;
            Fill(new Rect(x, y, 880f, 190f), panel);
            Fill(new Rect(x, y, 880f, 5f), gold);
            Icon(7, new Rect(x + 28f, y + 30f, 125f, 125f));
            Text("SHIP REPAIR", x + 170f, y + 20f, 55f, cream);
            Text("RECOVER 30 DOLPHIN PARTS", x + 170f, y + 87f, 26f, gold);
            Text(error ?? rule, x + 170f, y + 135f, 22f, error == null ? dim : new Color(1f, .45f, .4f));
            GUI.matrix = old;
        }

        internal static void Hud(int score, string time, IEnumerable<ShipRepairSession.ResultLine> players,
            bool competitive, string site, float siteUntil)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            float center = width * .5f;
            Fill(new Rect(center - 205f, 24f, 410f, 86f), panel);
            Icon(7, new Rect(center - 190f, 29f, 75f, 75f));
            Text(score.ToString() + "/30", center + 30f, 29f, 49f, cream, true);
            Text("PARTS  " + time, center + 60f, 81f, 19f, dim, true);
            Leaderboard(players, width - 328f, 138f, 305f, 34f, competitive);
            if (!string.IsNullOrEmpty(site) && Time.unscaledTime < siteUntil)
            {
                float alpha = Mathf.Clamp01(siteUntil - Time.unscaledTime);
                var tint = new Color(cream.r, cream.g, cream.b, Mathf.Min(1f, alpha * 1.5f));
                Fill(new Rect(center - 405f, 178f, 810f, 110f), new Color(panel.r, panel.g, panel.b, panel.a * tint.a));
                Text("DAY " + (RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1), center, 190f, 25f, gold, true);
                Text(site.ToUpperInvariant(), center, 223f, 43f, tint, true);
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
                Text(p.Score + "/30", x + width - 44f, at, 20f, p.Winner ? gold : cream, true);
            }
        }

        internal static void Results(string detail, IEnumerable<ShipRepairSession.ResultLine> players, bool won)
        {
            if (Event.current.type != EventType.Repaint) return;
            int oldDepth = GUI.depth;
            GUI.depth = -1000;
            var old = GUI.matrix; float width = Begin();
            // RoR2's bottom continuation prompt and ready indicators remain visible.
            Fill(new Rect(0f, 0f, width, 994f), new Color(.025f, .065f, .09f, 1f));
            float center = width * .5f;
            Icon(7, new Rect(center - 77f, 80f, 154f, 154f));
            Text(won ? "DOLPHIN REPAIRED" : "SHIP REPAIR ENDED", center, 254f, 55f, cream, true);
            Text(detail.ToUpperInvariant(), center, 327f, 23f, dim, true);
            var rows = players.ToArray();
            float x = center - 445f, y = 405f;
            float rowHeight = Mathf.Min(68f, 475f / Mathf.Max(1, rows.Length));
            float textSize = Mathf.Min(35f, rowHeight * .58f);
            Fill(new Rect(x, y, 890f, 57f + rows.Length * rowHeight), panel);
            Fill(new Rect(x, y, 890f, 5f), gold);
            Text("CAPTAINS", x + 45f, y + 15f, 28f, gold);
            Text("PARTS", x + 755f, y + 15f, 28f, gold);
            for (int i = 0; i < rows.Length; i++)
            {
                var p = rows[i]; float at = y + 62f + i * rowHeight;
                if (p.Winner) Fill(new Rect(x + 12f, at - 4f, 866f, rowHeight - 2f), new Color(.35f, .29f, .09f, .92f));
                if (p.Winner) Icon(10, new Rect(x + 19f, at, rowHeight - 10f, rowHeight - 10f));
                Text(Short(p.Name, 27).ToUpperInvariant(), x + 76f, at, textSize, p.Winner ? gold : cream);
                Text(p.Score + "/30", x + 800f, at, textSize, p.Winner ? gold : cream, true);
            }
            Icon(8, new Rect(center - 132f, 907f, 49f, 49f));
            Text("CONTINUE", center + 15f, 915f, 25f, cream, true);
            GUI.matrix = old;
            GUI.depth = oldDepth;
        }
    }
}
