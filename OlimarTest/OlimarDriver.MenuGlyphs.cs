using Ror2Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OlimarTest
{
    internal partial class OlimarDriver
    {
        GameObject onionGlyphCanvas;
        TextMeshProUGUI onionGlyphActions, onionGlyphHelp;

        static TextMeshProUGUI MenuGlyphLine(Transform parent, string name, float y, float height, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RoR2.UI.HGTextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(670f, height);
            var text = go.GetComponent<RoR2.UI.HGTextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = size;
            text.color = new Color(.85f, .88f, .82f);
            text.richText = true;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        string MenuGlyph(PadButton button, string fallback) =>
            GlyphOverride.PhysicalGlyph(localUser.inputPlayer, button) ?? fallback;

        void ShowMenuGlyphs()
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
                onionGlyphActions = MenuGlyphLine(onionGlyphCanvas.transform, "Choose and confirm", -64f, 48f, 19);
                onionGlyphHelp = MenuGlyphLine(onionGlyphCanvas.transform, "Onion shortcuts", -140f, 108f, 19);
            }
            onionGlyphCanvas.SetActive(true);
            onionGlyphActions.text = "LS ↑/↓ choose count     " + MenuGlyph(PadButton.A, "A") +
                " confirm     " + MenuGlyph(PadButton.B, "B") + " close";
            onionGlyphHelp.text = MenuGlyph(PadButton.X, "X") + " release all but one stored Pikmin\n" +
                MenuGlyph(PadButton.Y, "Y") + " return the whole squad\n" +
                MenuGlyph(PadButton.RB, "RB") + " heal from Onion stock (keep one)\n" +
                "Leaf 5% / Bud 10% / Flower 20% health";
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
