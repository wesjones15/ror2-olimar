using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Olimar on the character select screen (ror2-common's DisplayModel): his own sim on a flat
    // floor at the display's root, drawn through a view with no cursor. Each time he's picked
    // he plays an intro, as RoR2's survivors do (Commando's "Display" clip, MUL-T's
    // "PoseForLobby"): Pikmin stand scattered in front of him, he whistles (FUE, and the
    // whistle's SE_GATHER), they answer and run to him. Then, for his idle, they run circles
    // around him (his squad swarming round and round). Views take a moment to build (the
    // bakes), so released ones wait in a pool for the next lobby; the sim starts afresh each
    // time.
    internal sealed class OlimarDisplay : IDisplayActor
    {
        static readonly Stack<OlimarDisplay> spare = new Stack<OlimarDisplay>();
        static readonly Native.GroundFn floorFn = Floor;
        static Native.Host floor = new Native.Host { ground_height = Marshal.GetFunctionPointerForDelegate(floorFn) };

        // The intro: when he starts and stops whistling, and when the squad starts circling.
        const float WhistleFrom = 0.35f, WhistleUntil = 1.6f, CircleFrom = 2.8f;
        // One lap of the squad around him, seconds; how hard the swarm pushes (0..1).
        const float LapSeconds = 7f, CirclePush = 0.75f;
        // Where the Pikmin wait before he calls them (sim units; +Z faces the camera, all
        // inside his whistle's reach).
        static readonly (uint colour, float x, float z)[] Scattered =
        {
            (Native.Red, -52f, 18f), (Native.Yellow, -30f, 46f), (Native.Blue, -6f, 58f),
            (Native.Red, 22f, 52f), (Native.Yellow, 44f, 30f), (Native.Blue, 58f, 4f),
        };

        readonly PikminModels view;
        IntPtr sim;
        float time;

        public ModelView Models => view.Models;
        public float Scale => UnityHost.Scale;

        OlimarDisplay(PikminModels view)
        {
            this.view = view;
        }

        /// A display Olimar starting his intro, or null (the prefab keeps Commando) if the
        /// disc's files aren't there yet or won't load.
        public static IDisplayActor Get()
        {
            OlimarDisplay d;
            if (spare.Count > 0) d = spare.Pop();
            else
            {
                if (Native.pik_assets_complete(Native.CString(Plugin.AssetsDir)) == 0) return null;
                var view = PikminModels.Create(Plugin.AssetsDir, null, sound: Plugin.Sound.Value, name: "Olimar (lobby)");
                if (view == null)
                {
                    Plugin.Log.LogWarning("Olimar's lobby model didn't load; showing Commando");
                    return null;
                }
                view.ShowCursors(false);
                d = new OlimarDisplay(view);
            }
            if (!d.Begin())
            {
                d.Models.HideAll();
                spare.Push(d);
                return null;
            }
            return d;
        }

        // A new sim: Olimar facing the camera, his Pikmin scattered and free (not his squad yet).
        bool Begin()
        {
            FreeSim();
            sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), (ulong)DateTime.Now.Ticks);
            if (sim == IntPtr.Zero) return false;
            var origin = new Native.Vec3(0f, 0f, 0f);
            if (Native.pik_sim_add_captain(sim, ref origin) < 0)
            {
                FreeSim();
                return false;
            }
            foreach (var (colour, x, z) in Scattered)
            {
                var at = new Native.Vec3(x, 0f, z);
                Native.pik_sim_spawn_piki(sim, colour, ref at, -1);
            }
            time = 0f;
            return true;
        }

        public void Step(float dt, Transform display)
        {
            if (sim == IntPtr.Zero) return;
            time += dt;
            var input = new Native.Input();
            bool whistling = time >= WhistleFrom && time < WhistleUntil;
            Buttons(ref input, whistling);
            Native.pik_sim_set_input(sim, 0, ref input);
            Native.pik_sim_advance(sim, ref floor, dt);
            var frame = new Native.ViewFrame
            {
                dt = dt,
                lag = StepSmoother.LagTicks(Native.pik_sim_alpha(sim), 1f),
                forward = new Native.Vec3(0f, 0f, 1f),
                up = new Native.Vec3(0f, 1f, 0f),
                // He's the listener through the whistle (its sound is his own); after that the
                // camera is, so the squad's swarming trumpet stays quiet.
                listener = time < CircleFrom ? 0 : -1,
                volume = GameSettings.SfxVolume() * Mathf.Clamp(Plugin.SoundVolume.Value, 0f, 2f),
            };
            // The camera in the display's space, for the helmet's reflection and the sounds.
            var cam = Camera.main;
            if (cam && display)
            {
                var inv = Quaternion.Inverse(display.rotation);
                var f = inv * cam.transform.forward;
                var u = inv * cam.transform.up;
                frame.eye = UnityHost.ToSim(inv * (cam.transform.position - display.position));
                frame.forward = new Native.Vec3(f.x, f.y, f.z);
                frame.up = new Native.Vec3(u.x, u.y, u.z);
            }
            view.Pose(sim, ref frame);
        }

        // The intro's buttons: the whistle, then the swarm stick turning round and round.
        void Buttons(ref Native.Input input, bool whistling)
        {
            input.whistle = (byte)(whistling ? 1 : 0);
            if (time < CircleFrom) return;
            float a = (time - CircleFrom) / LapSeconds * 2f * Mathf.PI;
            input.swarm_x = Mathf.Sin(a) * CirclePush;
            input.swarm_y = Mathf.Cos(a) * CirclePush;
        }

        public void Release()
        {
            // Let go of the whistle, so its sound stops, before the sim goes.
            if (sim != IntPtr.Zero)
            {
                var none = new Native.Input();
                Native.pik_sim_set_input(sim, 0, ref none);
                Native.pik_sim_advance(sim, ref floor, 0.05f);
                var frame = new Native.ViewFrame { dt = 0f, listener = -1, forward = new Native.Vec3(0f, 0f, 1f), up = new Native.Vec3(0f, 1f, 0f) };
                view.Pose(sim, ref frame);
            }
            FreeSim();
            Models.HideAll();
            spare.Push(this);
        }

        void FreeSim()
        {
            if (sim == IntPtr.Zero) return;
            Native.pik_sim_free(sim);
            sim = IntPtr.Zero;
        }

        static byte Floor(IntPtr user, ref Native.Vec3 pos, out float height)
        {
            height = 0f;
            return 1;
        }
    }
}
