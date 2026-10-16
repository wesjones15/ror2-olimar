using System;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // libpikmin's world queries answered with Unity physics against RoR2's world layer
    // (ror2-common's WorldProbe): the ground under a point (MapMgr::getMinY, mapMgr.cpp) and
    // pushing a sphere out of walls (MapMgr::traceMove). Sim units in, sim units out; positions convert with Scale (metres per
    // sim unit) on the same axes, since both are Y-up with the same handedness of yaw.
    internal static class UnityHost
    {
        public static float Scale = 0.04f;

        /// Olimar's model stands 25.1 units tall in its bind pose (navi's model on the disc).
        public const float NaviHeight = 25.1f;

        // Faces steeper than this (|normal.y| below it) are walls; the rest is ground the sim
        // snaps to.
        const float WallNormalY = 0.5f;
        const int WallRays = 12;
        const float GroundReach = 200f;

        static readonly Native.GroundFn groundFn = Ground;
        static readonly Native.WallsFn wallsFn = Walls;

        public static int Mask => WorldProbe.Mask;

        /// The callback table. The delegates live in statics so the GC never collects them.
        public static Native.Host Table(Native.DamageFn damage) => new Native.Host
        {
            user = IntPtr.Zero,
            ground_height = Marshal.GetFunctionPointerForDelegate(groundFn),
            walls = Marshal.GetFunctionPointerForDelegate(wallsFn),
            damage = Marshal.GetFunctionPointerForDelegate(damage),
            carry_path = Marshal.GetFunctionPointerForDelegate(ShipPartNavigation.Callback),
        };

        public static Vector3 ToUnity(Native.Vec3 v) => new Vector3(v.x, v.y, v.z) * Scale;

        public static Native.Vec3 ToSim(Vector3 v)
        {
            v /= Scale;
            return new Native.Vec3(v.x, v.y, v.z);
        }

        /// A Unity direction's yaw in the sim's convention (radians, atan2(x, z)).
        public static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z);

        public static Vector3 Forward(float yaw) => new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

        /// Ground straight below `p` (Unity space), or `p` itself over a void.
        public static Vector3 Snap(Vector3 p, float above = 2f)
        {
            if (Physics.Raycast(p + Vector3.up * above, Vector3.down, out var h, GroundReach, Mask, QueryTriggerInteraction.Ignore))
                return h.point;
            return p;
        }

        static byte Ground(IntPtr user, ref Native.Vec3 pos, out float height)
        {
            height = 0f;
            try
            {
                // A hair above the probe point, so a floor exactly at it counts.
                if (!WorldProbe.Ground(ToUnity(pos) + Vector3.up * 0.01f, GroundReach, out var h))
                    return 0;
                height = h.point.y / Scale;
                return 1;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ground probe: {e}");
                return 0;
            }
        }

        // A ring of horizontal rays out to the sphere's radius (WorldProbe.PushOutOfWalls). The
        // sim sub-steps moves shorter than the radius, so a ray starting inside a collider
        // (which Unity wouldn't report) only happens if something else put it there.
        static void Walls(IntPtr user, ref Native.Vec3 centre, float radius, out Native.Vec3 pushed)
        {
            pushed = centre;
            try
            {
                var c = WorldProbe.PushOutOfWalls(ToUnity(centre), radius * Scale, WallNormalY, null, 0, WallRays);
                var back = ToSim(c);
                pushed = new Native.Vec3(back.x, centre.y, back.z);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"wall probe: {e}");
            }
        }
    }
}
