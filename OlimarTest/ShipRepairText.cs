using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OlimarTest
{
    // Ship Repair's ordinary copy uses RoR2's own HUD font. Coordinates are in
    // the same 1080-high space as the disc-sourced IMGUI textures.
    internal sealed class ShipRepairText : MonoBehaviour
    {
        static ShipRepairText instance;
        readonly List<RoR2.UI.HGTextMeshProUGUI> lines = new List<RoR2.UI.HGTextMeshProUGUI>();
        int used, usedFrame = -1;

        static ShipRepairText Get()
        {
            if (instance) return instance;
            var go = new GameObject("Ship Repair RoR2 text", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(ShipRepairText));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 480;
            var scale = go.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920f, 1080f);
            scale.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scale.matchWidthOrHeight = 1f;
            instance = go.GetComponent<ShipRepairText>();
            return instance;
        }

        internal static void Draw(string value, float x, float y, float size, Color color,
            bool centered = false, float width = 0f, float height = 0f)
        {
            if (string.IsNullOrEmpty(value)) return;
            var self = Get();
            if (self.usedFrame != Time.frameCount) { self.usedFrame = Time.frameCount; self.used = 0; }
            while (self.lines.Count <= self.used)
            {
                var go = new GameObject("RoR2 text", typeof(RectTransform), typeof(RoR2.UI.HGTextMeshProUGUI));
                go.transform.SetParent(self.transform, false);
                var line = go.GetComponent<RoR2.UI.HGTextMeshProUGUI>();
                line.raycastTarget = false;
                line.richText = false;
                self.lines.Add(line);
            }
            var text = self.lines[self.used++];
            text.gameObject.SetActive(true);
            text.text = value;
            text.color = color;
            text.fontSize = size * .8f;
            text.enableWordWrapping = height > size * 1.8f;
            text.alignment = centered ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            float w = width > 0f ? width : centered ? 700f : 550f;
            rect.anchoredPosition = new Vector2(centered ? x - w * .5f : x, -y);
            rect.sizeDelta = new Vector2(w, height > 0f ? height : size * 1.4f);
        }

        void Update()
        {
            if (usedFrame < Time.frameCount - 1)
            {
                foreach (var text in lines) if (text) text.gameObject.SetActive(false);
            }
            else if (usedFrame == Time.frameCount - 1)
            {
                for (int i = used; i < lines.Count; i++)
                    if (lines[i]) lines[i].gameObject.SetActive(false);
            }
        }
    }
}
