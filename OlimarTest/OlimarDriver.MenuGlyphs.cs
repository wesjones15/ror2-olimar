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

        static TextMeshProUGUI MenuGlyphLine(Transform parent, string name, float y, float width,
            float height, int size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RoR2.UI.HGTextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(width, height);
            var text = go.GetComponent<RoR2.UI.HGTextMeshProUGUI>();
            text.alignment = alignment;
            text.fontSize = size;
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
                onionGlyphTitle = MenuGlyphLine(onionGlyphCanvas.transform, "Onion title", 165f,
                    530f, 48f, 32, TextAlignmentOptions.Center);
                onionGlyphActions = MenuGlyphLine(onionGlyphCanvas.transform, "Onion shortcuts", -14f,
                    490f, 285f, 27, TextAlignmentOptions.TopLeft);
                onionGlyphActions.lineSpacing = 12f;
                onionGlyphNote = MenuGlyphLine(onionGlyphCanvas.transform, "Onion note", -182f,
                    520f, 38f, 19, TextAlignmentOptions.Center);
            }
            onionGlyphCanvas.SetActive(true);
            onionGlyphTitle.text = "ONION";
            onionGlyphActions.text = MenuGlyph(PadButton.X, "X") + "  Dump Onion Pikmin (keep one)\n" +
                MenuGlyph(PadButton.Y, "Y") + "  Fill Onion with squad\n" +
                MenuGlyph(PadButton.RB, "RB") + "  Heal Olimar from Onion stock\n" +
                MenuGlyph(PadButton.LB, "LB") + "  Call stuck carriers\n" +
                MenuGlyph(PadButton.A, "A") + "  Send Onion plucker";
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
