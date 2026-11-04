using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using RoR2.UI;
using Ror2Common;
using UnityEngine;
using UnityEngine.UI;

namespace OlimarTest
{
    // Pikmin 1's enemy life gauge for Olimar's HUD in place of RoR2's health bars: the wheel of
    // `LifeGauge` (gauges.cpp, the style Bulborbs use). A pie of 32 slices from 12 o'clock,
    // anticlockwise, in the health colour (green, light green, yellow, red at 1, 3/4, 1/2 and 1/4),
    // the rest dark grey, in a black ring. It fades in once the enemy is hurt, its fill easing
    // toward the true value, and stays while it is wounded.
    //
    // RoR2's own bars are switched off (HUD.combatHealthBarViewer) for a HUD showing Olimar and put
    // back for any other body. The gauges are drawn on a canvas of their own, over every enemy
    // team's bodies in sight.
    sealed class PikminHealthGauges : MonoBehaviour
    {
        const int Slices = 32;
        // `LifeGauge::refresh`: how long a full or empty wheel stays, and how fast it fades.
        const float Hold = 5f, FadeRate = 2f;
        // The black ring around the coloured wheel (`outerRadius = innerRadius * 1.1666`).
        const float RingScale = 1.1666f;
        const float MaxMetres = 150f;

        static readonly Color Green = new Color32(0, 255, 0, 255), LightGreen = new Color32(128, 255, 0, 255),
            Yellow = new Color32(255, 255, 0, 255), Red = new Color32(255, 0, 0, 255),
            Remainder = new Color32(32, 32, 32, 192);

        enum State { Hidden, FadeIn, Display, FadeOut }

        sealed class Gauge
        {
            public RectTransform root;
            public Image ring, back, fill;
            public State state = State.Hidden;
            public float shown = 1f, fade, hold;
            public bool seen;
        }

        ConfigEntry<bool> enabledSetting, hideBarSetting;
        // RoR2's own health bars under the Pikmin row, hidden by a CanvasGroup; `made` when
        // this added the group (and so removes it again).
        readonly Dictionary<CanvasGroup, bool> hiddenBars = new Dictionary<CanvasGroup, bool>();
        readonly List<CanvasGroup> barsToShow = new List<CanvasGroup>();
        Canvas canvas;
        Sprite disc;
        readonly Dictionary<object, Gauge> gauges = new Dictionary<object, Gauge>();
        readonly List<object> gone = new List<object>();
        readonly List<PikminWorld.OnionGauge> onionGauges = new List<PikminWorld.OnionGauge>();
        readonly HashSet<CombatHealthBarViewer> silenced = new HashSet<CombatHealthBarViewer>();

        public static void Install(GameObject host, ConfigFile config)
        {
            var component = host.AddComponent<PikminHealthGauges>();
            component.enabledSetting = config.Bind("Look", "PikminHealthGauges", true,
                "Olimar's enemy health bars are Pikmin 1's life gauge, a coloured wheel that appears once an enemy is hurt, " +
                "instead of RoR2's bars. Other characters keep RoR2's bars. Can change during play.");
            component.hideBarSetting = config.Bind("Look", "HideRoR2HealthBar", true,
                "Hide RoR2's own health bar under the Pikmin HUD row while you play Olimar (his life is the circle in " +
                "that row). Other characters keep it. Can change during play.");
        }

        void Update()
        {
            bool on = enabledSetting != null && enabledSetting.Value;
            bool hideBar = hideBarSetting != null && hideBarSetting.Value;
            HUD shown = null;
            var list = HUD.readOnlyInstanceList;
            barsToShow.Clear();
            foreach (var bar in hiddenBars.Keys) barsToShow.Add(bar);
            for (int i = 0; i < list.Count; i++)
            {
                var hud = list[i];
                if (!hud) continue;
                var driver = hud.targetBodyObject ? hud.targetBodyObject.GetComponent<OlimarDriver>() : null;
                bool olimar = on && driver;
                SetRoR2Bars(hud.combatHealthBarViewer, !olimar);
                // The Pikmin row (OlimarDriver.BottomHud) covers where RoR2's bar sits.
                if (hideBar && driver && driver.Active && hud.healthBar)
                {
                    var group = HideBar(hud.healthBar.gameObject);
                    barsToShow.Remove(group);
                    // The level text and experience bar share that corner of RoR2's HUD and show through
                    // the Pikmin row's tint if left.
                    if (hud.expBar) barsToShow.Remove(HideBar(hud.expBar.gameObject));
                    if (hud.levelText) barsToShow.Remove(HideBar(hud.levelText.gameObject));
                }
                if (olimar && shown == null) shown = hud;
            }
            foreach (var group in barsToShow) ShowBar(group);
            if (shown == null)
            {
                Clear();
                return;
            }
            Draw(shown);
        }

        CanvasGroup HideBar(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (!group)
            {
                group = go.AddComponent<CanvasGroup>();
                hiddenBars[group] = true;
            }
            else if (!hiddenBars.ContainsKey(group)) hiddenBars[group] = false;
            group.alpha = 0f;
            return group;
        }

        void ShowBar(CanvasGroup group)
        {
            if (hiddenBars.TryGetValue(group, out bool made) && group)
            {
                if (made) Destroy(group);
                else group.alpha = 1f;
            }
            hiddenBars.Remove(group);
        }

        // RoR2's world bars on or off for one HUD.
        void SetRoR2Bars(CombatHealthBarViewer viewer, bool show)
        {
            if (!viewer) return;
            if (show)
            {
                if (silenced.Remove(viewer))
                {
                    viewer.enabled = true;
                    if (viewer.container) viewer.container.gameObject.SetActive(true);
                }
            }
            else if (silenced.Add(viewer) || viewer.enabled)
            {
                viewer.enabled = false;
                if (viewer.container) viewer.container.gameObject.SetActive(false);
            }
        }

        void Draw(HUD hud)
        {
            var body = hud.targetBodyObject.GetComponent<CharacterBody>();
            var rig = hud.cameraRigController;
            var cam = rig ? rig.sceneCam : null;
            if (!body || !cam || !body.teamComponent)
            {
                Clear();
                return;
            }
            EnsureCanvas();
            // In VR the overlay only reaches the desktop window: the gauges are drawn in front of the
            // headset's camera instead, at the distance of the other labels (VrSheet).
            if (Vr.Active)
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != cam)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = VrSheet.Distance;
                }
            }
            else if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // An overlay would draw over RoR2's pause menu: only while the game runs.
            canvas.gameObject.SetActive(Time.timeScale > 0f);
            var mine = body.teamComponent.teamIndex;
            foreach (var g in gauges.Values) g.seen = false;
            foreach (var team in new[] { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void })
            {
                if (team == mine) continue;
                var members = TeamComponent.GetTeamMembers(team);
                for (int i = 0; i < members.Count; i++)
                {
                    var enemy = members[i] ? members[i].body : null;
                    var health = enemy ? enemy.healthComponent : null;
                    if (!health || !health.alive || health.dontShowHealthbar) continue;
                    Track(health, health.combinedHealthFraction, WorldAbove(enemy), cam);
                }
            }
            // Olimar's Onions: wounded ones, and the dormant ones refilling.
            onionGauges.Clear();
            if (PikminWorld.Instance) PikminWorld.Instance.OnionGauges(onionGauges);
            foreach (var o in onionGauges) Track(o.key, o.ratio, o.position, cam, o.keep);
            gone.Clear();
            foreach (var kv in gauges)
                if ((kv.Key is UnityEngine.Object dead && !dead) || !kv.Value.seen) gone.Add(kv.Key);
            foreach (var key in gone) Remove(key);
        }

        static Vector3 WorldAbove(CharacterBody enemy) =>
            enemy.corePosition + Vector3.up * (Mathf.Max(enemy.radius, 0.5f) * 1.1f + 1f);

        void Track(object key, float health, Vector3 world, Camera cam, bool keep = false)
        {
            float ratio = Mathf.Clamp01(health);
            if (!gauges.TryGetValue(key, out var g))
            {
                // Full-health enemies get no gauge until they are hurt.
                if (ratio >= 0.999f) return;
                g = Make();
                g.shown = ratio;
                g.state = State.FadeIn;
                gauges[key] = g;
            }
            g.seen = true;
            float dt = Time.unscaledDeltaTime;
            // `LifeGauge::updValue`: any hurt enemy not yet fully shown fades in.
            if (ratio < 1f && g.fade != 1f && g.state != State.FadeIn) g.state = State.FadeIn;
            switch (g.state)
            {
                case State.FadeIn:
                    Ease(g, ratio, dt);
                    g.fade += FadeRate * dt;
                    if (g.fade > 1f)
                    {
                        g.fade = 1f;
                        g.state = State.Display;
                        g.hold = Hold;
                    }
                    break;
                case State.Display:
                    Ease(g, ratio, dt);
                    g.hold -= dt;
                    if (g.hold <= 0f)
                    {
                        g.hold = 0f;
                        // The wheel stays while the enemy is wounded.
                        if (!keep && (g.shown <= 0f || g.shown >= 1f)) g.state = State.FadeOut;
                    }
                    break;
                case State.FadeOut:
                    g.fade -= FadeRate * dt;
                    if (g.fade < 0f)
                    {
                        g.fade = 0f;
                        g.state = State.Hidden;
                    }
                    break;
            }
            Place(g, world, cam);
        }

        // `LifeGauge::adjustValue`: the shown value eases toward the real one, snapping when close.
        static void Ease(Gauge g, float target, float dt)
        {
            g.shown += 2f * dt * (target - g.shown);
            if (Mathf.Abs(g.shown - target) < 1f / 64f) g.shown = target;
        }

        void Place(Gauge g, Vector3 world, Camera cam)
        {
            var screen = cam.WorldToScreenPoint(world);
            bool visible = g.state != State.Hidden && screen.z > 0.5f && screen.z < MaxMetres;
            if (g.root.gameObject.activeSelf != visible) g.root.gameObject.SetActive(visible);
            if (!visible) return;
            // Smaller with distance, like the game's `mScale * (1 - depth)`.
            float radius = Mathf.Clamp(520f / screen.z, 7f, 22f) * Mathf.Max(0.5f, Screen.height / 1080f);
            g.root.anchoredPosition = new Vector2(screen.x, screen.y);
            g.ring.rectTransform.sizeDelta = Vector2.one * (radius * 2f * RingScale);
            g.back.rectTransform.sizeDelta = Vector2.one * (radius * 2f);
            g.fill.rectTransform.sizeDelta = Vector2.one * (radius * 2f);
            float a = g.fade;
            g.ring.color = new Color(0f, 0f, 0f, a);
            g.back.color = new Color(Remainder.r, Remainder.g, Remainder.b, Remainder.a * a);
            var c = HealthColour(g.shown);
            g.fill.color = new Color(c.r, c.g, c.b, a);
            // Slices from 12 o'clock; the first is lit for any health at all.
            g.fill.fillAmount = g.shown > 0f ? Mathf.Max(1f, Mathf.Floor(Slices * g.shown)) / Slices : 0f;
        }

        // `LifeGauge::refresh`: green at full, light green at 3/4, yellow at 1/2, red from 1/4 down.
        static Color HealthColour(float r)
        {
            if (r > 0.75f) return Color.Lerp(Green, LightGreen, 1f - (r - 0.75f) / 0.25f);
            if (r > 0.5f) return Color.Lerp(LightGreen, Yellow, 1f - (r - 0.5f) / 0.25f);
            if (r > 0.25f) return Color.Lerp(Yellow, Red, 1f - (r - 0.25f) / 0.25f);
            return Red;
        }

        Gauge Make()
        {
            var g = new Gauge();
            var go = new GameObject("Pikmin gauge", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            g.root = (RectTransform)go.transform;
            g.root.anchorMin = g.root.anchorMax = Vector2.zero;
            g.root.pivot = new Vector2(0.5f, 0.5f);
            g.ring = Layer(g.root, "ring", false);
            g.back = Layer(g.root, "remainder", false);
            g.fill = Layer(g.root, "health", true);
            go.SetActive(false);
            return g;
        }

        Image Layer(RectTransform parent, string name, bool wheel)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = disc;
            image.raycastTarget = false;
            if (wheel)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Radial360;
                image.fillOrigin = (int)Image.Origin360.Top;
                image.fillClockwise = false;
            }
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return image;
        }

        void EnsureCanvas()
        {
            if (canvas) return;
            var go = new GameObject("Pikmin enemy gauges", typeof(Canvas));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Pikmin gauge disc", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - size * 0.5f) * (x + 0.5f - size * 0.5f)
                        + (y + 0.5f - size * 0.5f) * (y + 0.5f - size * 0.5f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(r - d + 0.5f)));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        void Remove(object key)
        {
            if (gauges.TryGetValue(key, out var g) && g.root) Destroy(g.root.gameObject);
            gauges.Remove(key);
        }

        // No HUD showing Olimar: the gauges go, RoR2's bars are back (SetRoR2Bars).
        void Clear()
        {
            foreach (var g in gauges.Values)
                if (g.root) Destroy(g.root.gameObject);
            gauges.Clear();
            if (canvas) canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var viewer in silenced)
                if (viewer)
                {
                    viewer.enabled = true;
                    if (viewer.container) viewer.container.gameObject.SetActive(true);
                }
            barsToShow.Clear();
            foreach (var bar in hiddenBars.Keys) barsToShow.Add(bar);
            foreach (var group in barsToShow) ShowBar(group);
            Clear();
        }
    }
}
