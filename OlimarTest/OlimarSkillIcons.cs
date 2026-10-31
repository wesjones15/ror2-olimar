using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Approved viewer compositions, rebuilt from the player's own disc cache by Unity.
    // PikiHeadItem / RollJmp / NaviWhistle / GoalItem / Fue retain their native assets.
    internal static class OlimarSkillIcons
    {
        static readonly Native.GroundFn groundFn = Ground;
        static Native.Host floor = new Native.Host { ground_height = Marshal.GetFunctionPointerForDelegate(groundFn) };
        const float ViewerYaw = 3.65f;

        public static void RequestAll()
        {
            SkillCard.Request(OlimarSkills.Throw, "OlimarThrow", () => Plugin.AssetsReady, Throw, () => Plugin.AssetsDir);
            SkillCard.Request(OlimarSkills.Whistle, "OlimarWhistle", () => Plugin.AssetsReady, Whistle, () => Plugin.AssetsDir);
            SkillCard.Request(OlimarSkills.Ride, "OlimarRelocateOnion", () => Plugin.AssetsReady, Onion, () => Plugin.AssetsDir);
            SkillCard.Request(OlimarSkills.Strike, "OlimarDismissStrike", () => Plugin.AssetsReady, Strike, () => Plugin.AssetsDir);
            SkillCard.Request(OlimarSkills.SetPluckers, "OlimarPluckers", () => Plugin.AssetsReady, Pluckers, () => Plugin.AssetsDir);
        }

        static byte Ground(IntPtr user, ref Native.Vec3 pos, out float height) { height = 0f; return 1; }
        static bool HideCaptain(ViewDraw draw) => draw.kind == Native.KindCaptain;

        static SkillCardStage Pluckers()
        {
            var card = Card("Olimar Pluckers", new Color32(105, 57, 144, 255), 42f, 0.23f);
            var scene = new Scene(card);
            var origin = new Native.Vec3(0f, 0f, 0f);
            Native.pik_sim_plant_sprout(scene.Sim, Native.Red, ref origin);
            // itemAI.cpp PikiHeadAI::Wait: UGWAIT (3). No forced growth into a bud.
            scene.Step(16, d => d.kind != Native.KindSprout && d.kind != Native.KindSproutHappa);
            scene.AddDirt();
            card.Center = scene.Metres(0f, 7f, 0f);
            Finish(card, scene);
            return card;
        }

        static SkillCardStage Throw()
        {
            var card = Card("Olimar Throw", new Color32(140, 207, 242, 255), 34f, 0.08f);
            var scene = new Scene(card);
            var at = new Native.Vec3(0f, 0f, 12f);
            Native.pik_sim_spawn_piki(scene.Sim, Native.Red, ref at, 0);
            // Same press/release and frame 13 as the approved native viewer capture.
            scene.Step(13, HideCaptain, frame => scene.Input(new Native.Input
                { cursor = new Native.Vec3(0f, 0f, 45f), use_cursor = 1, throw_ = (byte)(frame == 5 ? 1 : 0) }));
            var flying = scene.FlyingPiki();
            var right = new Vector3(-Mathf.Cos(ViewerYaw), 0f, Mathf.Sin(ViewerYaw));
            card.Center = scene.Metres(flying.x, flying.y + 16f, flying.z) - right * (3f * scene.Scale);
            Finish(card, scene);
            return card;
        }

        static SkillCardStage Whistle()
        {
            var card = Card("Olimar Whistle", new Color32(20, 26, 43, 255), 300f, 0.65f);
            var scene = new Scene(card);
            // NaviWhistle.h: particles 9/10/11 and NaviFue's circle (20).
            scene.Input(new Native.Input { whistle = 1, use_cursor = 1, cursor = new Native.Vec3(0f, 0f, 45f) });
            scene.Step(16, null);
            card.Center = scene.Metres(0f, 5f, 30f);
            Finish(card, scene);
            return card;
        }

        static SkillCardStage Onion()
        {
            var card = Card("Olimar Onion", new Color32(31, 88, 150, 255), 190f, 0.12f);
            var scene = new Scene(card);
            var at = new Native.Vec3(0f, 100f, 0f);
            int onion = Native.pik_sim_add_onion(scene.Sim, ref at, 1u << (int)Native.Red);
            if (onion < 0) throw new InvalidOperationException("Portrait Onion didn't spawn");
            Native.pik_sim_set_onion_pose(scene.Sim, (uint)onion, ref at, 1);
            Native.pik_sim_set_onion_flight_motion(scene.Sim, (uint)onion, 0, 0f);
            // Cross the real animation's effect keys instead of posing only its last frame.
            scene.Step(30, HideCaptain, null, frame =>
                Native.pik_sim_set_onion_flight_motion(scene.Sim, (uint)onion, 0, frame / 60f));
            card.Center = scene.Metres(0f, 175f, 0f);
            Finish(card, scene);
            return card;
        }

        static SkillCardStage Strike()
        {
            var card = Card("Olimar Dismiss Strike", new Color32(58, 115, 30, 255), 36f, 0.12f);
            var scene = new Scene(card);
            for (int i = 0; i < 12; i++)
            {
                var at = new Native.Vec3((i % 4) * 10f - 15f, 0f, -38f - (i / 4) * 9f);
                uint color = new[] { Native.Red, Native.Yellow, Native.Blue }[i % 3];
                Native.pik_sim_spawn_piki(scene.Sim, color, ref at, 0);
            }
            Native.pik_sim_set_dismiss_strike(scene.Sim, 1f, 150f, 0f, 1f);
            // Off-camera target: the real strike command selects StrikeRush on the
            // pointing side. It is a sim query only; no enemy model enters the portrait.
            var target = new Native.Vec3(150f, 0f, 150f);
            var creatures = new[] { new Native.Creature { id = 1, pos = target, alive = 1, parts_len = 1 } };
            var parts = new[] { new Native.Part { centre = target, radius = 20f, stickable = 1 } };
            Native.pik_sim_set_creatures(scene.Sim, creatures, 1, parts, 1);
            scene.Step(10, null, frame => scene.Input(new Native.Input
                { cursor = new Native.Vec3(0f, 0f, 45f), use_cursor = 1, dismiss = (byte)(frame == 2 ? 1 : 0) }));
            card.Center = scene.CaptainAt() + scene.Metres(0f, 12f, 0f);
            Finish(card, scene);
            return card;
        }

        static SkillCardStage Card(string name, Color color, float distance, float pitch)
            => new SkillCardStage(name) { Tint = color, Yaw = Mathf.PI - ViewerYaw, Pitch = pitch,
                CameraDistance = distance * UnityHost.Scale, FieldOfView = 40f, Radius = 1f };

        static void Finish(SkillCardStage card, Scene scene)
        {
            card.BeforeRender = (camera, layer) => scene.Frame(0f, camera);
            card.Disposing = scene.Dispose;
        }

        sealed class Scene : IDisposable
        {
            readonly PikminModels view;
            readonly Transform root;
            readonly List<UnityEngine.Object> props = new List<UnityEngine.Object>();
            Func<ViewDraw, bool> hidden;
            public IntPtr Sim { get; }
            public float Scale => UnityHost.Scale;
            public Vector3 Metres(float x, float y, float z) => new Vector3(x, y, z) * Scale;

            public Scene(SkillCardStage card)
            {
                root = card.Root;
                Sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), 0x5eed);
                if (Sim == IntPtr.Zero) throw new InvalidOperationException("Olimar's portrait sim didn't load");
                var origin = new Native.Vec3(0f, 0f, 0f);
                if (Native.pik_sim_add_captain(Sim, ref origin) < 0)
                { Native.pik_sim_free(Sim); throw new InvalidOperationException("Portrait captain didn't spawn"); }
                view = PikminModels.Create(Plugin.AssetsDir, root, false, "Olimar skill portrait");
                if (view == null) { Native.pik_sim_free(Sim); throw new InvalidOperationException("Portrait models didn't load"); }
                view.ShowCursors(false);
            }

            public void Input(Native.Input input) => Native.pik_sim_set_input(Sim, 0, ref input);
            public void Step(int ticks, Func<ViewDraw, bool> hide, Action<int> before = null, Action<int> after = null)
            {
                hidden = hide;
                for (int i = 0; i < ticks; i++)
                {
                    before?.Invoke(i);
                    Native.pik_sim_advance(Sim, ref floor, 1f / 30f);
                    after?.Invoke(i);
                    Frame(1f / 30f);
                }
            }

            public void Frame(float dt, Camera camera = null)
            {
                var forward = camera ? camera.transform.forward : Vector3.forward;
                var up = camera ? camera.transform.up : Vector3.up;
                var frame = new Native.ViewFrame { dt = dt, lag = 0f, volume = 0f,
                    forward = new Native.Vec3(forward.x, forward.y, forward.z),
                    up = new Native.Vec3(up.x, up.y, up.z) };
                view.Update(Sim, ref frame, Scale,
                    d => d.kind == Native.KindCursor || d.kind == Native.KindMarker || (hidden != null && hidden(d)));
            }

            public Vector3 CaptainAt()
            {
                if (Native.pik_sim_captain(Sim, 0, out var captain) == 0) throw new InvalidOperationException("Portrait captain unavailable");
                return Metres(captain.pos.x, captain.pos.y, captain.pos.z);
            }

            public Native.Vec3 FlyingPiki()
            {
                var pikis = new Native.Piki[16];
                uint count = Native.pik_sim_pikis(Sim, pikis, (uint)pikis.Length);
                for (uint i = 0; i < count; i++) if (pikis[i].state == Native.PikiState.Flying) return pikis[i].pos;
                throw new InvalidOperationException("Throw portrait must contain an airborne Pikmin");
            }

            public void AddDirt()
            {
                // The approved shallow mound hides the underground half of PikiHeadItem.
                var vertices = new Vector3[50]; vertices[0] = new Vector3(0f, 2.5f, 0f) * Scale;
                var triangles = new int[48 * 3];
                for (int i = 0; i <= 48; i++)
                {
                    float angle = i * Mathf.PI * 2f / 48f;
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 14f, -1f, Mathf.Sin(angle) * 14f) * Scale;
                    if (i > 0) { int t = (i - 1) * 3; triangles[t] = 0; triangles[t + 1] = i + 1; triangles[t + 2] = i; }
                }
                var mesh = new Mesh { name = "portrait soil", vertices = vertices, triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var material = new Material(Shaders.HgStandard()) { name = "portrait soil" };
                material.SetColor("_Color", new Color(0.25f, 0.16f, 0.09f));
                var go = new GameObject("portrait soil"); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                props.Add(mesh); props.Add(material);
            }

            public void Dispose()
            {
                view?.Dispose();
                foreach (var prop in props) if (prop) UnityEngine.Object.Destroy(prop);
                if (Sim != IntPtr.Zero) Native.pik_sim_free(Sim);
            }
        }
    }
}
