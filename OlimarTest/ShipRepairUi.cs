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
        static readonly Dictionary<char, Texture2D> locationGlyphs = new Dictionary<char, Texture2D>();
        // Location names in Pikmin 2's area-name lettering, when the Pikmin 2 disc has been extracted.
        static readonly Dictionary<char, Texture2D> titleGlyphs = new Dictionary<char, Texture2D>();
        static string loadedFrom;
        static readonly Color cream = new Color(1f, .94f, .72f);
        static readonly Color gold = new Color(1f, .78f, .28f);
        static readonly Color dim = new Color(.62f, .71f, .65f);
        static readonly Color panel = new Color(.045f, .105f, .12f, .96f);

        static void Load()
        {
            if (!Plugin.AssetsReady || string.IsNullOrEmpty(Plugin.AssetsDir)) return;
            if (loadedFrom == Plugin.AssetsDir) return;
            foreach (var texture in icons.Values.Concat(digits).Concat(locationGlyphs.Values).Concat(titleGlyphs.Values))
                if (texture) UnityEngine.Object.Destroy(texture);
            icons.Clear(); locationGlyphs.Clear(); titleGlyphs.Clear(); loadedFrom = Plugin.AssetsDir;
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
            for (uint c = 33; c <= 126; c++)
            {
                if (Native.pik_font_glyph_texture(path, c, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_font_glyph_texture(path, c, out info, rgba, (uint)rgba.Length) != 0)
                    locationGlyphs[(char)c] = Texture(info, rgba, "Pikmin bigFont " + (char)c);
            }
            for (uint c = 'A'; c <= 'z'; c++)
            {
                if (!char.IsLetter((char)c) || Native.pik_title_glyph_texture(path, c, out var info, null, 0) == 0) continue;
                var rgba = new byte[checked((int)(info.width * info.height * 4))];
                if (Native.pik_title_glyph_texture(path, c, out info, rgba, (uint)rgba.Length) != 0)
                    titleGlyphs[(char)c] = Texture(info, rgba, "Pikmin area-name " + (char)c);
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

        // Panels share the text's uGUI canvas, below the text they carry (see ShipRepairText.Fill).
        static void Fill(Rect rect, Color color) => ShipRepairText.Fill(rect, color);
        static void Icon(uint id, Rect rect)
        {
            if (icons.TryGetValue(id, out var icon) && icon) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }
        static string Short(string value, int max) => string.IsNullOrEmpty(value) ? "CAPTAIN" :
            value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        static void Text(string value, float x, float y, float size, Color color, bool centered = false)
        {
            ShipRepairText.Draw(value, x, y, size, color, centered);
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

        /// Part names and notices in Pikmin's own letters (bigFont), in capitals and shrunk to fit `maxWidth`.
        internal static void PikminText(string value, float center, float y, float size, float maxWidth, Color color)
        {
            Load();
            if (string.IsNullOrEmpty(value)) return;
            value = value.ToUpperInvariant().Replace('—', '-').Replace('·', '-');
            LocationText(value, center, y, Mathf.Min(size, maxWidth / (value.Length * .68f)), color, false);
        }

        static float LocationAdvance(char c, float size, bool lettering)
        {
            if (lettering && titleGlyphs.TryGetValue(c, out var title)) return size * 1.15f * title.width / title.height;
            return size * (c == ' ' ? .35f : .68f);
        }

        static void LocationText(string value, float center, float y, float size, Color color, bool lettering = true)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (locationGlyphs.Count == 0)
            {
                Text(value, center, y, size, color, true);
                return;
            }
            // Pikmin 2's lettering where the disc has it (letters of equal height, as wide as they are);
            // bigFont's square cells for digits, punctuation and anything else.
            var old = GUI.color;
            float total = 0f;
            foreach (char c in value) total += LocationAdvance(c, size, lettering);
            float x = center - total * .5f;
            foreach (char c in value)
            {
                if (lettering && titleGlyphs.TryGetValue(c, out var title))
                {
                    float h = size * 1.15f, w = h * title.width / title.height;
                    GUI.color = new Color(.08f, .035f, .01f, color.a);
                    GUI.DrawTexture(new Rect(x + 2f, y + 2f, w, h), title);
                    GUI.color = color;
                    GUI.DrawTexture(new Rect(x, y, w, h), title);
                }
                else if (locationGlyphs.TryGetValue(c, out var glyph))
                {
                    GUI.color = new Color(.08f, .035f, .01f, color.a);
                    GUI.DrawTexture(new Rect(x + 2f, y + 2f, size, size), glyph);
                    GUI.color = color;
                    GUI.DrawTexture(new Rect(x, y, size, size), glyph);
                }
                x += LocationAdvance(c, size, lettering);
            }
            GUI.color = old;
        }

        // RoR2's own objective list sits under the stage timer on the right. IMGUI always draws over
        // uGUI, so the leaderboard starts below whatever that panel is showing.
        static float objectivesBottom, nextObjectivesScan;
        static RoR2.UI.ObjectivePanelController objectivePanel;
        static readonly Vector3[] corners = new Vector3[4];
        static float ObjectivesBottom(float scale)
        {
            if (Time.unscaledTime >= nextObjectivesScan)
            {
                nextObjectivesScan = Time.unscaledTime + .5f;
                if (!objectivePanel) objectivePanel = UnityEngine.Object.FindObjectOfType<RoR2.UI.ObjectivePanelController>();
                objectivesBottom = 0f;
                if (objectivePanel)
                {
                    float lowest = float.PositiveInfinity;
                    foreach (var graphic in objectivePanel.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                    {
                        if (graphic.color.a < .05f) continue;
                        graphic.rectTransform.GetWorldCorners(corners);
                        var canvas = graphic.canvas ? graphic.canvas.rootCanvas : null;
                        var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                        for (int i = 0; i < 4; i++)
                            lowest = Mathf.Min(lowest, RectTransformUtility.WorldToScreenPoint(camera, corners[i]).y);
                    }
                    if (!float.IsInfinity(lowest)) objectivesBottom = Mathf.Clamp((Screen.height - lowest) / scale + 8f, 0f, 430f);
                }
            }
            return objectivesBottom;
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
            ShipRepairText.Draw("Compete with your friends to repair your Dolphin first!",
                x + 105f, y + 67f, 22f, gold, width: w - 123f, height: 57f);
            ShipRepairText.Draw("Build your pikmin army! you get bonus points at the end for your pikmin squad size.",
                x + 105f, y + 124f, 22f, cream, width: w - 123f, height: 72f);
            if (!string.IsNullOrEmpty(error)) Text(error, x + w * .5f, y + 192f, 19f, new Color(1f, .45f, .4f), true);
            GUI.matrix = old;
        }

        internal static void Hud(int score, IEnumerable<ShipRepairSession.ResultLine> players,
            bool competitive, int treasures, int treasureHundredths, string site, string subtitle, float siteUntil)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            float center = width * .5f;
            Icon(19, new Rect(center - 159f, 26f, 73f, 73f));
            Icon(7, new Rect(center - 145f, 31f, 47f, 61f));
            BubbleFraction(score, 30, new Rect(center - 83f, 30f, 245f, 65f));
            // The treasure category, below the parts: Pikmin 2 treasures the crew has brought to the Dolphin (shared repair).
            if (treasures >= 0)
                Text("TREASURES " + treasures + "  +" + ShipRepairTreasures.Format(treasureHundredths), center + 40f, 98f, 20f, new Color(.62f, .9f, 1f), true);
            Leaderboard(players, width - 328f, Mathf.Max(138f, ObjectivesBottom(Screen.height / 1080f)), 305f, 34f, competitive);
            Banner(site, subtitle, siteUntil, center);
            GUI.matrix = old;
        }

        /// The stage-entry card on its own ("DAY N" and the stage name in Pikmin letters),
        /// for every Olimar stage outside Ship Repair. The RoR2-font location line is not
        /// repeated; only the Pikmin lettering carries the place name.
        internal static void LocationBanner(string site, float until)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.matrix; float width = Begin();
            Banner(site, null, until, width * .5f);
            GUI.matrix = old;
        }

        static void Banner(string site, string subtitle, float until, float center)
        {
            if (string.IsNullOrEmpty(site) || Time.unscaledTime >= until) return;
            float alpha = Mathf.Clamp01(until - Time.unscaledTime);
            float opacity = Mathf.Min(1f, alpha * 1.5f);
            PikminText("DAY " + (RoR2.Run.instance ? RoR2.Run.instance.stageClearCount + 1 : 1),
                center, 363f, 26f, 720f, new Color(cream.r, cream.g, cream.b, opacity));
            if (!string.IsNullOrEmpty(site))
                LocationText(site, center, 401f, 55, new Color(.9f, .53f, .12f, opacity));
            if (!string.IsNullOrEmpty(subtitle))
                PikminText(subtitle, center, 493f, 26f, 720f, new Color(.92f, .9f, .79f, opacity));
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
            // Leave the stock gameover screen and its X/players uncovered.
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
            // With treasures on, their own column sits between the Pikmin and the score; the other two move left.
            bool treasures = rows.Any(r => r.TreasuresOn);
            Text("PARTS", x + (treasures ? 440f : 540f), y + 15f, 25f, gold, true);
            Text("PIKMIN", x + (treasures ? 560f : 680f), y + 15f, 25f, gold, true);
            if (treasures) Text("TREASURE", x + 690f, y + 15f, 25f, gold, true);
            Text("SCORE", x + 817f, y + 15f, 25f, gold, true);
            for (int i = 0; i < rows.Length; i++)
            {
                var p = rows[i]; float at = y + 62f + i * rowHeight;
                if (p.Winner) Fill(new Rect(x + 12f, at - 4f, 866f, rowHeight - 2f), new Color(.35f, .29f, .09f, .92f));
                if (p.Winner) Icon(10, new Rect(x + 19f, at, rowHeight - 10f, rowHeight - 10f));
                Text(Short(p.Name, treasures ? 13 : 21).ToUpperInvariant(), x + 74f, at, textSize, p.Winner ? gold : cream);
                BubbleNumber(p.Parts, new Rect(x + (treasures ? 392f : 482f), at - 4f, treasures ? 96f : 116f, rowHeight - 5f), 2,
                    new Color(.95f, .8f, .78f));
                BubbleNumber(p.Pikmin, new Rect(x + (treasures ? 505f : 617f), at - 4f, treasures ? 110f : 126f, rowHeight - 5f), 3,
                    new Color(.83f, .9f, .99f));
                if (treasures)
                    BubbleNumber(p.Treasures, new Rect(x + 636f, at - 4f, 108f, rowHeight - 5f), 2, new Color(.7f, .92f, 1f));
                BubbleNumber(p.Score, new Rect(x + 759f, at - 4f, 111f, rowHeight - 5f), 2,
                    p.Winner ? new Color(1f, .94f, .62f) : Color.white);
            }
            GUI.matrix = old;
            GUI.depth = oldDepth;
        }
    }
}
