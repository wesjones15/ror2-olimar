using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Olimar on the character select screen (ror2-common's DisplayModel): his own sim, with
    // him and a red, a yellow and a blue Pikmin standing idle on a flat floor at the display's
    // root (so they breathe and look about), drawn through a silent view with no cursor. Views take a moment to build (the
    // bakes), so released ones wait in a pool for the next lobby.
    internal sealed class OlimarDisplay : IDisplayActor
    {
        static readonly Stack<OlimarDisplay> spare = new Stack<OlimarDisplay>();
        static readonly Native.GroundFn floorFn = Floor;
        static Native.Host floor = new Native.Host { ground_height = Marshal.GetFunctionPointerForDelegate(floorFn) };

        readonly PikminModels view;
        readonly IntPtr sim;

        public ModelView Models => view.Models;
        public float Scale => UnityHost.Scale;

        OlimarDisplay(PikminModels view, IntPtr sim)
        {
            this.view = view;
            this.sim = sim;
        }

        /// A display Olimar, or null (the prefab keeps Commando) if the disc's files aren't
        /// there yet or won't load.
        public static IDisplayActor Get()
        {
            if (spare.Count > 0) return spare.Pop();
            if (Native.pik_assets_complete(Native.CString(Plugin.AssetsDir)) == 0) return null;
            var sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), 0);
            if (sim == IntPtr.Zero) return null;
            var origin = new Native.Vec3(0f, 0f, 0f);
            var view = Native.pik_sim_add_captain(sim, ref origin) >= 0
                ? PikminModels.Create(Plugin.AssetsDir, null, sound: false, name: "Olimar (lobby)")
                : null;
            if (view == null)
            {
                Plugin.Log.LogWarning("Olimar's lobby model didn't load; showing Commando");
                Native.pik_sim_free(sim);
                return null;
            }
            view.ShowCursors(false);
            // A Pikmin of each colour beside him, free (not in his squad), so they stand where
            // they're put, facing the camera like him.
            foreach (var (colour, x, z) in new[] { (Native.Red, -16f, -2f), (Native.Yellow, 16f, -2f), (Native.Blue, 29f, -10f) })
            {
                var at = new Native.Vec3(x, 0f, z);
                Native.pik_sim_spawn_piki(sim, colour, ref at, -1);
            }
            return new OlimarDisplay(view, sim);
        }

        public void Step(float dt, Transform display)
        {
            Native.pik_sim_advance(sim, ref floor, dt);
            var frame = new Native.ViewFrame
            {
                dt = dt,
                forward = new Native.Vec3(0f, 0f, 1f),
                up = new Native.Vec3(0f, 1f, 0f),
                listener = -1,
            };
            // The camera in the display's space, for the helmet's reflection.
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

        public void Release()
        {
            Models.HideAll();
            spare.Push(this);
        }

        static byte Floor(IntPtr user, ref Native.Vec3 pos, out float height)
        {
            height = 0f;
            return 1;
        }
    }
}
