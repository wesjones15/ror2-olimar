using System.Collections.Generic;
using Ror2Common;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Drawing of the sim, on the machine running it. With the user's assets, Pikmin's own
    // models, animations and sounds (PikminModels); without them, Unity primitives for Pikmin (a
    // capsule with a leaf, bud or flower on top), corpse pellets (flat discs), Onions (a pod on
    // three legs) and sprouts, plus a ring for each captain's cursor. Either way a ring shows
    // the whistle. Positions are interpolated between the last two 30 Hz ticks. None of this is
    // networked yet.
    internal partial class PikminView : MonoBehaviour
    {
        public PikminWorld world;
        PikminModels models;
        internal uint RepairsPending => models?.RepairsPending ?? 0;
        sealed class HealingAudio
        {
            public bool active;
            public int ghosts;
            public float nextGhost;
        }
        readonly Dictionary<int, HealingAudio> healingAudio = new Dictionary<int, HealingAudio>();
        float recallWhistleUntil;
        internal void PlaySound(byte sound, Vector3 at)
        {
            models?.PlaySound(sound, UnityHost.ToSim(at));
            if (sound == 6) recallWhistleUntil = Time.unscaledTime + 0.28f;
        }
        readonly HashSet<int> drawnCaptains = new HashSet<int>();

        static readonly Color[] PikiColours =
        {
            new Color(0.15f, 0.35f, 0.95f), // Blue
            new Color(0.95f, 0.15f, 0.1f),  // Red
            new Color(1f, 0.85f, 0.1f),     // Yellow
        };
        static readonly Color[] HappaColours =
        {
            new Color(0.2f, 0.75f, 0.2f), // leaf
            new Color(0.85f, 0.75f, 0.5f), // bud
            new Color(1f, 1f, 1f),         // flower
        };
        static readonly Color PelletColour = new Color(0.45f, 0.35f, 0.3f);
        static readonly Color OnionWhite = new Color(0.9f, 0.9f, 0.85f);

        // Onion leg ends relative to its position, in sim units (libpikmin onion_menu
        // LEG_FEET / LEG_TOPS, measured on the Onion's model).
        static readonly Vector3[] LegFeet = { new Vector3(2.3f, 0f, -115.2f), new Vector3(-100.9f, 0f, 55.7f), new Vector3(98.6f, 0f, 59.6f) };
        static readonly Vector3[] LegTops = { new Vector3(0.7f, 88.4f, -19.5f), new Vector3(-18.4f, 88.4f, 9.3f), new Vector3(17.6f, 88.3f, 10.7f) };

        class PikiShape
        {
            public GameObject root;
            public Renderer body, head;
            public Vector3 prev, cur;
            public bool seen;
        }

        readonly List<PikiShape> pikis = new List<PikiShape>();
        readonly List<GameObject> pellets = new List<GameObject>();
        readonly List<GameObject> onions = new List<GameObject>();
        readonly List<GameObject> sprouts = new List<GameObject>();
        readonly Dictionary<int, LineRenderer> cursors = new Dictionary<int, LineRenderer>();
        readonly Dictionary<int, LineRenderer> whistles = new Dictionary<int, LineRenderer>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        Material baseMat;
        Transform root;

        void Awake()
        {
            root = new GameObject("PikminShapes").transform;
            root.SetParent(transform, false);
        }

        /// A new sim: fresh models, animation state and sound emitters for it.
        public void StartModels()
        {
            DisposeModels();
            if (Ror2Common.RunAudio.Ended) return;
            models = PikminModels.Create(Plugin.AssetsDir, transform, Plugin.Sound.Value);
            if (models != null)
            {
                Plugin.Log.LogInfo($"Pikmin models: {models.Status}");
                // Goobo Jr.'s Olimar is a gummy, as RoR2 draws its clones.
                models.Models.SkinFor = d => d.kind == Native.KindCaptain && Plugin.GummyClones.Value
                    && world && world.IsTemporaryCaptain((int)d.id) ? GummyClone.Material() : null;
            }
        }

        void DisposeModels()
        {
            healingAudio.Clear();
            if (models != null)
            {
                models.Dispose();
                models = null;
            }
            foreach (var d in OlimarDriver.All) d.HideBody(false);
        }

        void OnDestroy()
        {
            DisposeModels();
            ReleaseCarryHud();
        }

        public void StopModels() => DisposeModels();

        public void Clear()
        {
            DisposeModels();
            foreach (Transform t in root) Destroy(t.gameObject);
            pikis.Clear();
            pellets.Clear();
            onions.Clear();
            sprouts.Clear();
            cursors.Clear();
            whistles.Clear();
        }

        /// A tick ran: shift the Pikmin's positions for interpolation.
        public void Snapshot()
        {
            for (int i = 0; i < world.PikiCount; i++)
            {
                while (pikis.Count <= i) pikis.Add(new PikiShape());
                var s = pikis[i];
                var now = UnityHost.ToUnity(world.Pikis[i].pos);
                s.prev = s.seen ? s.cur : now;
                s.cur = now;
                s.seen = true;
            }
        }

        void LateUpdate()
        {
            if (Ror2Common.RunAudio.Ended) return;
            if (!world || !world.Running) return;
            using var timing = OlimarPerformance.Measure(OlimarPerformance.Part.Draw);
            if (models != null && world.Healthy)
            {
                DrawModels();
                DrawCaptains(cursorRings: false);
                world.Corpses.Update(world);
                return;
            }
            if (models != null)
                foreach (var d in OlimarDriver.All) d.HideBody(false);
            else
                foreach (var d in OlimarDriver.All) d.HideBody(d.Arriving);
            float a = world.RenderAlpha;
            DrawPikis(a);
            DrawPellets();
            DrawOnions();
            DrawSprouts();
            DrawCaptains(cursorRings: true);
            world.Corpses.Update(world);
        }

        void DrawModels()
        {
            if (recallWhistleUntil > 0f && Time.unscaledTime >= recallWhistleUntil)
            {
                models.PlaySound(7, default);
                recallWhistleUntil = 0f;
            }
            models.Celebrate(ShipRepairSession.Instance && ShipRepairSession.Instance.Winning);
            var frame = new Native.ViewFrame
            {
                dt = Time.deltaTime, listener = -1, volume = Volume(),
                // One 30 Hz tick between snapshots, even when RoR2's physics runs at 60 Hz.
                lag = StepSmoother.LagTicks(world.RenderAlpha, 1f),
            };
            var user = LocalUserManager.GetFirstLocalUser();
            var cam = GameSettings.PlayerCamera();
            if (cam)
            {
                frame.eye = UnityHost.ToSim(cam.position);
                frame.forward = new Native.Vec3(cam.forward.x, cam.forward.y, cam.forward.z);
                frame.up = new Native.Vec3(cam.up.x, cam.up.y, cam.up.z);
            }
            var drivers = OlimarDriver.All;
            // Olimar beams down from the Dolphin; only the Pikmin exit the Onion.
            foreach (var d in drivers)
                if (d.ViewCaptain >= 0)
                {
                    uint captain = (uint)d.ViewCaptain;
                    models.Materialize(captain, world.BeamProgress(captain));
                    float progress = world.HealingProgress(captain);
                    uint ghosts = world.HealingCount(captain);
                    models.Heal(captain, progress, ghosts);
                    HealingSounds(d, progress, ghosts);
                }
            foreach (var d in drivers)
                if (d.ViewCaptain >= 0 && user != null && d.Body && d.Body == user.cachedBody)
                    frame.listener = d.ViewCaptain;
            for (int i = 0; i < world.OnionCount; i++)
            {
                var onion = world.Onions[i];
                if (onion.is_ship != 0) continue;
                var at = UnityHost.ToUnity(onion.pos);
                if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit,
                    250f, UnityHost.Mask, QueryTriggerInteraction.Ignore) && hit.normal.y >= .5f)
                    models.SetOnionGround(onion.id, hit.point.y / UnityHost.Scale);
            }
            // A pellet with its enemy's body is drawn as the body.
            models.Update(world.DataHandle, ref frame, UnityHost.Scale, d =>
                (d.kind == Native.KindCaptain ? world.CaptainHidden(d.id)
                    : (d.kind == Native.KindCursor || d.kind == Native.KindMarker) && world.CaptainArriving(d.id)) ||
                d.kind == Native.KindPellet &&
                world.Corpses.Pose(d.id, new Vector3(d.pos.x, d.pos.y, d.pos.z) * UnityHost.Scale,
                    new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w)));

            // Olimar's own model stands in for Commando's wherever it's drawn.
            drawnCaptains.Clear();
            for (int i = 0; i < models.DrawCount; i++)
            {
                var dr = models.DrawAt(i);
                if (dr.kind == Native.KindCaptain) drawnCaptains.Add((int)dr.id);
            }
            foreach (var d in drivers)
                d.HideBody(d.Arriving || d.ViewCaptain >= 0 && drawnCaptains.Contains(d.ViewCaptain));
        }

        void HealingSounds(OlimarDriver driver, float progress, uint count)
        {
            int id = driver.ViewCaptain;
            if (!healingAudio.TryGetValue(id, out var audio))
                healingAudio[id] = audio = new HealingAudio();
            var at = driver.Body ? driver.Body.footPosition : driver.transform.position;
            if (progress >= 0f && !audio.active)
            {
                audio.active = true;
                audio.ghosts = (int)count;
                audio.nextGhost = Time.unscaledTime + 0.1f;
                // The heal cue leads; one death/ghost cue follows for each Onion Pikmin spent.
                PlaySound(4, at);
            }
            if (progress < 0f) audio.active = false;
            while (audio.ghosts > 0 && Time.unscaledTime >= audio.nextGhost)
            {
                PlaySound(5, at);
                audio.ghosts--;
                audio.nextGhost += 0.055f;
            }
        }

        // RoR2's master and sound-effects volume, times ours.
        static float Volume() => GameSettings.SfxVolume() * Mathf.Clamp(Plugin.SoundVolume.Value, 0f, 2f);

        void DrawPikis(float a)
        {
            for (int i = 0; i < pikis.Count; i++)
            {
                var s = pikis[i];
                bool show = i < world.PikiCount && Visible(world.Pikis[i]);
                if (!show)
                {
                    if (s.root) s.root.SetActive(false);
                    continue;
                }
                var p = world.Pikis[i];
                if (!s.root) MakePiki(s);
                s.root.SetActive(true);
                float size = Mathf.Max(p.size, 4f) * UnityHost.Scale;
                s.root.transform.position = Vector3.Lerp(s.prev, s.cur, a);
                s.root.transform.rotation = Quaternion.Euler(0f, p.face * Mathf.Rad2Deg, 0f);
                s.root.transform.localScale = Vector3.one * size * Shrink(p.state);
                s.body.sharedMaterial = Mat(PikiColours[Mathf.Min(p.color, (byte)2)]);
                s.head.sharedMaterial = Mat(HappaColours[Mathf.Min(p.happa, (byte)2)]);
            }
        }

        static bool Visible(in Native.Piki p) =>
            p.alive != 0 && p.state != Native.PikiState.Dead && p.state != Native.PikiState.Swallowed;

        static float Shrink(byte state) =>
            state == Native.PikiState.Fading || state == Native.PikiState.Absorb ? 0.6f : 1f;

        // In units of the Pikmin's radius: a body 4 tall and 2 wide, a stem, and the leaf.
        void MakePiki(PikiShape s)
        {
            s.root = new GameObject("Piki");
            s.root.transform.SetParent(root, false);
            var body = Prim(PrimitiveType.Capsule, s.root.transform);
            body.transform.localPosition = new Vector3(0f, 2f, 0f);
            body.transform.localScale = new Vector3(2f, 2f, 2f);
            var stem = Prim(PrimitiveType.Cylinder, s.root.transform);
            stem.transform.localPosition = new Vector3(0f, 4.6f, 0f);
            stem.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);
            stem.GetComponent<Renderer>().sharedMaterial = Mat(HappaColours[0]);
            var head = Prim(PrimitiveType.Sphere, s.root.transform);
            head.transform.localPosition = new Vector3(0f, 5.4f, 0f);
            head.transform.localScale = new Vector3(1.1f, 0.5f, 1.1f);
            s.body = body.GetComponent<Renderer>();
            s.head = head.GetComponent<Renderer>();
        }

        void DrawPellets()
        {
            for (int i = 0; i < Mathf.Max(pellets.Count, world.PelletCount); i++)
            {
                while (pellets.Count <= i) pellets.Add(null);
                bool show = i < world.PelletCount && world.Pellets[i].state != 3 && !world.Corpses.Has(world.Pellets[i].id);
                if (!show)
                {
                    if (pellets[i]) pellets[i].SetActive(false);
                    continue;
                }
                var p = world.Pellets[i];
                if (!pellets[i])
                {
                    pellets[i] = Prim(PrimitiveType.Cylinder, root);
                    pellets[i].GetComponent<Renderer>().sharedMaterial = Mat(PelletColour);
                }
                var go = pellets[i];
                go.SetActive(true);
                float r = p.radius * UnityHost.Scale * Mathf.Max(0.05f, p.scale);
                float h = r * 0.35f;
                go.transform.position = UnityHost.ToUnity(p.pos) + Vector3.up * h;
                go.transform.rotation = Quaternion.Euler(0f, p.yaw * Mathf.Rad2Deg, 0f);
                go.transform.localScale = new Vector3(r * 2f, h, r * 2f);
            }
        }

        void DrawOnions()
        {
            for (int i = 0; i < Mathf.Max(onions.Count, world.OnionCount); i++)
            {
                while (onions.Count <= i) onions.Add(null);
                bool show = i < world.OnionCount && world.Onions[i].anim != 4;
                if (!show)
                {
                    if (onions[i]) onions[i].SetActive(false);
                    continue;
                }
                var o = world.Onions[i];
                if (!onions[i]) onions[i] = o.is_ship != 0 ? MakeDolphin() : MakeOnion(1u << (int)world.OnionColour((int)o.id));
                onions[i].SetActive(true);
                onions[i].transform.position = UnityHost.ToUnity(o.pos);
                onions[i].transform.localScale = Vector3.one * UnityHost.Scale;
            }
        }

        GameObject MakeDolphin()
        {
            var go = new GameObject("Dolphin (asset fallback)");
            go.transform.SetParent(root, false);
            var hull = Prim(PrimitiveType.Capsule, go.transform);
            hull.transform.localPosition = new Vector3(0f, 100f, 0f);
            hull.transform.localScale = new Vector3(110f, 100f, 110f);
            hull.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.8f, 0.8f, 0.75f));
            var nose = Prim(PrimitiveType.Sphere, go.transform);
            nose.transform.localPosition = new Vector3(0f, 190f, 0f);
            nose.transform.localScale = Vector3.one * 110f;
            nose.GetComponent<Renderer>().sharedMaterial = Mat(Color.red);
            return go;
        }

        GameObject MakeOnion(uint colours)
        {
            var go = new GameObject("Onion");
            go.transform.SetParent(root, false);
            Color c = OnionWhite;
            for (int k = 0; k < 3; k++)
                if (colours == 1u << k) c = PikiColours[k];
            var pod = Prim(PrimitiveType.Sphere, go.transform);
            pod.transform.localPosition = new Vector3(0f, 110f, 0f);
            pod.transform.localScale = new Vector3(90f, 70f, 90f);
            pod.GetComponent<Renderer>().sharedMaterial = Mat(c);
            for (int k = 0; k < 3; k++)
            {
                var leg = Prim(PrimitiveType.Cylinder, go.transform);
                var from = LegFeet[k];
                var to = LegTops[k];
                leg.transform.localPosition = (from + to) * 0.5f;
                leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
                leg.transform.localScale = new Vector3(6f, (to - from).magnitude * 0.5f, 6f);
                leg.GetComponent<Renderer>().sharedMaterial = Mat(OnionWhite);
            }
            return go;
        }

        void DrawSprouts()
        {
            for (int i = 0; i < Mathf.Max(sprouts.Count, world.SproutCount); i++)
            {
                while (sprouts.Count <= i) sprouts.Add(null);
                if (i >= world.SproutCount)
                {
                    if (sprouts[i]) sprouts[i].SetActive(false);
                    continue;
                }
                var s = world.Sprouts[i];
                if (!sprouts[i]) sprouts[i] = Prim(PrimitiveType.Sphere, root);
                var go = sprouts[i];
                go.SetActive(true);
                bool flying = s.state == 0;
                float r = (flying ? 4f : 5f) * UnityHost.Scale;
                go.transform.position = UnityHost.ToUnity(s.pos) + Vector3.up * r;
                go.transform.localScale = flying ? Vector3.one * r * 2f : new Vector3(r * 2.5f, r, r * 2.5f);
                var colour = flying ? PikiColours[Mathf.Min(s.color, (byte)2)] : HappaColours[Mathf.Min(s.happa, (byte)2)];
                go.GetComponent<Renderer>().sharedMaterial = Mat(colour);
            }
        }

        void DrawCaptains(bool cursorRings)
        {
            var seen = new HashSet<int>();
            foreach (var d in OlimarDriver.All)
            {
                if (!d.HasState || d.Captain < 0 || !d.Active) continue;
                seen.Add(d.Captain);
                if (cursorRings)
                {
                    var cursor = Line(cursors, d.Captain, new Color(1f, 0.4f, 0.1f), 0.06f);
                    Ring(cursor, UnityHost.ToUnity(d.State.cursor_world), 0.35f);
                }
                var whistle = Line(whistles, d.Captain, new Color(1f, 0.9f, 0.2f), 0.05f);
                bool on = d.Whistling && d.State.whistle_radius > 0f;
                whistle.enabled = on;
                if (on) Ring(whistle, UnityHost.ToUnity(d.State.cursor_world), d.State.whistle_radius * UnityHost.Scale);
            }
            foreach (var kv in cursors) kv.Value.enabled = cursorRings && seen.Contains(kv.Key);
            foreach (var kv in whistles) if (!seen.Contains(kv.Key)) kv.Value.enabled = false;
        }

        const int RingPoints = 32;

        static void Ring(LineRenderer line, Vector3 centre, float radius)
        {
            line.enabled = true;
            centre = UnityHost.Snap(centre, 1f) + Vector3.up * 0.08f;
            for (int k = 0; k < RingPoints; k++)
            {
                float t = k * 2f * Mathf.PI / RingPoints;
                line.SetPosition(k, centre + new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t)) * radius);
            }
        }

        LineRenderer Line(Dictionary<int, LineRenderer> lines, int key, Color colour, float width)
        {
            if (lines.TryGetValue(key, out var line) && line) return line;
            var go = new GameObject("Ring");
            go.transform.SetParent(root, false);
            line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = RingPoints;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.sharedMaterial = Mat(colour);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            lines[key] = line;
            return line;
        }

        GameObject Prim(PrimitiveType type, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            // Drawing only: no collider for RoR2's physics or our own raycasts to hit.
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // The primitive's own default material, recoloured, for rings too. RoR2 draws it fine in
        // its deferred pipeline; its own Hopoo shaders want textures this doesn't have, and
        // Shader.Find throws for shaders outside RoR2's table (RoR2BepInExPack's
        // LegacyShaderDetours), Sprites/Default included.
        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m) && m) return m;
            if (!baseMat)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseMat = probe.GetComponent<Renderer>().sharedMaterial;
                DestroyImmediate(probe);
            }
            m = new Material(baseMat);
            m.color = c;
            materials[c] = m;
            return m;
        }
    }
}
