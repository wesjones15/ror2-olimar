using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Olimar's skill portraits, from his own Pikmin 1 disc files on first run (the same
    // contract as the Melee portraits). A throwaway sim is posed for each move; PikminModels
    // draws the models and libpikmin's particles, and ror2-common's SkillCard frames it.
    internal static class OlimarSkillIcons
    {
        static readonly Native.GroundFn groundFn = Ground;
        static Native.Host floor = new Native.Host
        {
            ground_height = Marshal.GetFunctionPointerForDelegate(groundFn),
        };

        static readonly Color Green = new Color(0.32f, 0.70f, 0.34f);
        static readonly Color Red = new Color(0.90f, 0.26f, 0.20f);
        static readonly Color Yellow = new Color(0.95f, 0.78f, 0.18f);
        static readonly Color Blue = new Color(0.24f, 0.52f, 0.94f);

        public static void RequestAll()
        {
            SkillCard.Request(OlimarSkills.Throw, "OlimarThrow", () => Plugin.AssetsReady, Throw);
            SkillCard.Request(OlimarSkills.Whistle, "OlimarWhistle", () => Plugin.AssetsReady, Whistle);
            SkillCard.Request(OlimarSkills.Relocate, "OlimarRelocateOnion", () => Plugin.AssetsReady, Onion);
            SkillCard.Request(OlimarSkills.Strike, "OlimarDismissStrike", () => Plugin.AssetsReady, Strike);
            SkillCard.Request(OlimarSkills.SetPluckers, "OlimarPluckers", () => Plugin.AssetsReady, Pluckers);
        }

        static byte Ground(IntPtr user, ref Native.Vec3 pos, out float height)
        {
            height = 0f;
            return 1;
        }

        // A red sprout in a mound of soil.
        static SkillCardStage Pluckers()
        {
            var card = Card("Olimar Pluckers", Green);
            var scene = new Scene(card, "Olimar pluckers portrait");
            var at = new Native.Vec3(0f, 0f, 0f);
            Native.pik_sim_plant_sprout(scene.Sim, Native.Red, ref at);
            Native.pik_sim_set_sprout_stages(scene.Sim, 1f, 60f);
            scene.Step(150, d => d.kind != Native.KindSprout && d.kind != Native.KindSproutHappa
                && d.kind != Native.KindCaptain);
            scene.AddDirt(Vector3.zero, 0.42f);
            card.Center = new Vector3(0f, 0.28f, 0f);
            card.Radius = 0.52f;
            card.Yaw = 0.55f;
            card.Pitch = 0.22f;
            card.Distance = 1.55f;
            card.Disposing = scene.Dispose;
            return card;
        }

        // A thrown Pikmin at the top of its arc, Olimar behind it.
        static SkillCardStage Throw()
        {
            var card = Card("Olimar Throw", Red);
            var scene = new Scene(card, "Olimar throw portrait");
            scene.SpawnSquad(3);
            var input = default(Native.Input);
            input.cursor = new Native.Vec3(95f, 0f, 0f);
            input.throw_ = 1;
            Native.pik_sim_set_input(scene.Sim, 0, ref input);
            scene.Step(7, null);
            var at = scene.HighestFlyingPiki();
            card.Center = at + new Vector3(0f, 0.25f, 0f);
            card.Radius = 0.62f;
            card.Yaw = 0.75f;
            card.Pitch = 0.15f;
            card.Distance = 1.65f;
            card.Disposing = scene.Dispose;
            return card;
        }

        // The whistle ring at Olimar's feet.
        static SkillCardStage Whistle()
        {
            var card = Card("Olimar Whistle", Yellow);
            var scene = new Scene(card, "Olimar whistle portrait");
            scene.SpawnSquad(4);
            var input = default(Native.Input);
            input.whistle = 1;
            Native.pik_sim_set_input(scene.Sim, 0, ref input);
            scene.Step(8, null);
            card.Center = scene.CaptainAt(0f, 0.30f, 0f);
            card.Radius = 1.95f;
            card.Yaw = 0.55f;
            card.Pitch = 0.42f;
            card.Distance = 1.60f;
            card.Disposing = scene.Dispose;
            return card;
        }

        // The Onion in flight, with the beam and its own effects.
        static SkillCardStage Onion()
        {
            var card = Card("Olimar Onion", Blue);
            var scene = new Scene(card, "Olimar onion portrait");
            var at = new Native.Vec3(0f, 210f, 0f);
            int onion = Native.pik_sim_add_onion(scene.Sim, ref at, 0b111);
            if (onion >= 0)
            {
                Native.pik_sim_set_onion_pose(scene.Sim, (uint)onion, ref at, 1);
                Native.pik_sim_set_onion_flight_motion(scene.Sim, (uint)onion, 0, 0.5f);
            }
            scene.Step(20, d => d.kind == Native.KindCaptain || d.kind == Native.KindPiki
                || d.kind == Native.KindHappa || d.kind == Native.KindCursor || d.kind == Native.KindMarker);
            card.Center = new Vector3(0f, 210f * scene.Scale + 0.4f, 0f);
            card.Radius = 2.8f;
            card.Yaw = 0.40f;
            card.Pitch = 0.08f;
            card.Distance = 1.70f;
            card.Disposing = scene.Dispose;
            return card;
        }

        // Olimar's dismiss strike, mid-call, with the squad charging.
        static SkillCardStage Strike()
        {
            var card = Card("Olimar Dismiss Strike", Red);
            var scene = new Scene(card, "Olimar strike portrait");
            scene.SpawnSquad(5);
            Native.pik_sim_set_dismiss_strike(scene.Sim, 1f, 150f, 0f, 10f);
            var input = default(Native.Input);
            input.dismiss = 1;
            Native.pik_sim_set_input(scene.Sim, 0, ref input);
            scene.Step(5, null);
            card.Center = scene.CaptainAt(0f, 0.70f, 0.20f);
            card.Radius = 1.10f;
            card.Yaw = 0.45f;
            card.Pitch = 0.18f;
            card.Distance = 1.60f;
            card.Disposing = scene.Dispose;
            return card;
        }

        static SkillCardStage Card(string name, Color tint) => new SkillCardStage(name) { Tint = tint };

        sealed class Scene : IDisposable
        {
            readonly IntPtr sim;
            readonly PikminModels view;
            readonly Transform root;
            readonly float scale = UnityHost.Scale;
            readonly List<GameObject> props = new List<GameObject>();
            readonly List<Material> materials = new List<Material>();

            public IntPtr Sim => sim;
            public float Scale => scale;

            public Scene(SkillCardStage card, string name)
            {
                var dir = Native.CString(Plugin.AssetsDir);
                sim = Native.pik_sim_new(dir, (ulong)DateTime.Now.Ticks);
                if (sim == IntPtr.Zero) throw new InvalidOperationException("Olimar's portrait sim didn't load");
                var origin = new Native.Vec3(0f, 0f, 0f);
                if (Native.pik_sim_add_captain(sim, ref origin) < 0)
                {
                    Native.pik_sim_free(sim);
                    throw new InvalidOperationException("Olimar's portrait captain didn't spawn");
                }
                view = PikminModels.Create(Plugin.AssetsDir, card.Root, false, name);
                if (view == null)
                {
                    Native.pik_sim_free(sim);
                    throw new InvalidOperationException("Olimar's portrait model didn't load");
                }
                view.ShowCursors(false);
                root = card.Root;
            }

            public void SpawnSquad(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var p = new Native.Vec3((i - count / 2) * 22f, 0f, 35f + (i % 2) * 18f);
                    Native.pik_sim_spawn_piki(sim, i % 2 == 0 ? Native.Red : Native.Yellow, ref p, 0);
                }
            }

            public void Step(int ticks, Func<ViewDraw, bool> hide)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Native.pik_sim_advance(sim, ref floor, 1f / 30f);
                    Frame(1f / 30f, hide);
                }
            }

            void Frame(float dt, Func<ViewDraw, bool> hide)
            {
                var frame = new Native.ViewFrame
                {
                    dt = dt,
                    lag = 0f,
                    forward = new Native.Vec3(0f, 0f, 1f),
                    up = new Native.Vec3(0f, 1f, 0f),
                    volume = 0f,
                };
                view.Update(sim, ref frame, scale,
                    d => d.kind == Native.KindCursor || d.kind == Native.KindMarker || (hide != null && hide(d)));
            }

            public Vector3 CaptainAt(float right, float up, float forward)
            {
                if (Native.pik_sim_captain(sim, 0, out var c) == 0) return Vector3.zero;
                return new Vector3(c.pos.x, c.pos.y, c.pos.z) * scale + new Vector3(right, up, forward);
            }

            public Vector3 HighestFlyingPiki()
            {
                var pikis = new Native.Piki[64];
                uint n = Native.pik_sim_pikis(sim, pikis, (uint)pikis.Length);
                Vector3 best = CaptainAt(0f, 1.2f, 0.4f);
                float top = float.MinValue;
                for (uint i = 0; i < n; i++)
                {
                    var p = pikis[i];
                    if (p.state != Native.PikiState.Flying && p.state != Native.PikiState.Idle) continue;
                    if (p.pos.y <= top) continue;
                    top = p.pos.y;
                    best = new Vector3(p.pos.x, p.pos.y, p.pos.z) * scale;
                }
                return best;
            }

            public void AddDirt(Vector3 at, float radius)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "portrait soil";
                var collider = go.GetComponent<Collider>();
                if (collider) UnityEngine.Object.Destroy(collider);
                go.transform.SetParent(root, false);
                go.transform.position = at + Vector3.up * 0.018f;
                go.transform.localScale = new Vector3(radius, 0.018f, radius);
                var mat = new Material(Shaders.HgStandard()) { name = "portrait soil" };
                mat.SetColor("_Color", new Color(0.20f, 0.13f, 0.08f));
                var r = go.GetComponent<Renderer>();
                if (r) r.sharedMaterial = mat;
                props.Add(go);
                materials.Add(mat);
            }

            public void Dispose()
            {
                view?.Dispose();
                foreach (var p in props) if (p) UnityEngine.Object.Destroy(p);
                foreach (var m in materials) if (m) UnityEngine.Object.Destroy(m);
                if (sim != IntPtr.Zero) Native.pik_sim_free(sim);
            }
        }
    }
}
