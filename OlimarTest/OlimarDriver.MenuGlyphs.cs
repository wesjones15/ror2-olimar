using Ror2Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OlimarTest
{
    internal partial class OlimarDriver
    {
        GameObject onionGlyphCanvas;
        TextMeshProUGUI onionGlyphTitle, onionGlyphActions, onionGlyphNote;

        // The popup is one uGUI canvas: its background first, its text after, so the text is
        // drawn over the panel (IMGUI always draws over uGUI, which dimmed the glyphs).
        // Laid out for 1080 lines, small, on the right so Olimar stays in view.
        const float MenuW = 400f, MenuH = 330f, MenuMargin = 56f;

        static RectTransform MenuRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static void MenuFill(RectTransform rect, Color colour)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
        }

        static TextMeshProUGUI MenuGlyphLine(Transform parent, string name, Vector2 anchor, Vector2 position,
            Vector2 size, int fontSize, TextAlignmentOptions alignment)
        {
            var rect = MenuRect(name, parent, anchor, anchor, anchor, position, size);
            rect.gameObject.AddComponent<RoR2.UI.HGTextMeshProUGUI>();
            var text = rect.GetComponent<RoR2.UI.HGTextMeshProUGUI>();
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.color = new Color(.85f, .88f, .82f);
            text.richText = true;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        string MenuGlyph(PadButton button, string fallback) =>
            GlyphOverride.PhysicalGlyph(localUser.inputPlayer, button) ?? fallback;

        void ShowMenuGlyphs(string note)
        {
            if (!onionGlyphCanvas)
            {
                onionGlyphCanvas = new GameObject("Olimar Onion RoR2 button glyphs",
                    typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                var canvas = onionGlyphCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 500;
                var scaler = onionGlyphCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
                var panel = MenuRect("Onion panel", onionGlyphCanvas.transform, new Vector2(1f, .5f),
                    new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-MenuMargin, 0f),
                    new Vector2(MenuW, MenuH));
                MenuFill(panel, new Color(0.05f, 0.06f, 0.08f, 0.88f));
                var stripe = MenuRect("Onion stripe", panel, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(.5f, 1f), Vector2.zero, new Vector2(0f, 5f));
                MenuFill(stripe, new Color(0.8f, 0.95f, 0.65f));
                var top = new Vector2(.5f, 1f);
                var bottom = new Vector2(.5f, 0f);
                onionGlyphTitle = MenuGlyphLine(panel, "Onion title", top, new Vector2(0f, -22f),
                    new Vector2(MenuW - 20f, 36f), 24, TextAlignmentOptions.Center);
                onionGlyphActions = MenuGlyphLine(panel, "Onion shortcuts", top, new Vector2(0f, -66f),
                    new Vector2(MenuW - 44f, 230f), 20, TextAlignmentOptions.TopLeft);
                onionGlyphActions.lineSpacing = 10f;
                onionGlyphNote = MenuGlyphLine(panel, "Onion note", bottom, new Vector2(0f, 14f),
                    new Vector2(MenuW - 24f, 28f), 15, TextAlignmentOptions.Center);
            }
            onionGlyphCanvas.SetActive(true);
            onionGlyphTitle.text = "ONION";
            bool flying = PikminWorld.Instance.OnionFlying(DataCaptain);
            string disabled = "<color=#777B78>", enabled = "</color>";
            onionGlyphActions.text = MenuGlyph(PadButton.X, "X") + "  Dump Onion Pikmin (keep one)\n" +
                MenuGlyph(PadButton.Y, "Y") + "  Fill Onion with squad\n" +
                MenuGlyph(PadButton.RB, "RB") + "  Heal Olimar from Onion stock\n" +
                MenuGlyph(PadButton.LB, "LB") + "  Call stuck carriers\n" +
                (flying ? disabled : "") + MenuGlyph(PadButton.A, "A") + "  Send Onion plucker" + (flying ? enabled : "") + "\n" +
                MenuGlyph(PadButton.LT, "LT") + "  Board the Onion (Onion Ride)";
            onionGlyphNote.text = note ?? "";
        }

        void HideMenuGlyphs()
        {
            if (onionGlyphCanvas) onionGlyphCanvas.SetActive(false);
        }

        void DisposeMenuGlyphs()
        {
            if (onionGlyphCanvas) Destroy(onionGlyphCanvas);
        }
    }
}
