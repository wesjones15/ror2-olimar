using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace OlimarTest
{
    internal static class ShipPartNavigation
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int PathFn(IntPtr user, ref Native.Vec3 from, ref Native.Vec3 to, IntPtr output, uint capacity);
        internal static readonly PathFn Callback = CarryPath;
        sealed class CachedPath { public float time; public bool reachable; public Vector3[] points; }
        static readonly Dictionary<(int, int, int, int, int, int, int), CachedPath> paths =
            new Dictionary<(int, int, int, int, int, int, int), CachedPath>();
        internal static void Reset() => paths.Clear();

        public static bool TryPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!graph) return false;
            var key = (graph.GetInstanceID(), Mathf.RoundToInt(from.x * 4), Mathf.RoundToInt(from.y * 4), Mathf.RoundToInt(from.z * 4),
                Mathf.RoundToInt(to.x * 4), Mathf.RoundToInt(to.y * 4), Mathf.RoundToInt(to.z * 4));
            if (paths.TryGetValue(key, out var cached) && Time.realtimeSinceStartup - cached.time < 0.5f)
            {
                if (cached.reachable) { result.AddRange(cached.points); result[result.Count - 1] = to; }
                return cached.reachable;
            }
            bool reachable = ComputePath(graph, from, to, result);
            if (paths.Count >= 256) paths.Clear();
            paths[key] = new CachedPath { time = Time.realtimeSinceStartup, reachable = reachable, points = reachable ? result.ToArray() : Array.Empty<Vector3>() };
            return reachable;
        }

        static bool ComputePath(NodeGraph graph, Vector3 from, Vector3 to, List<Vector3> result)
        {
            using (var path = new RoR2.Path(graph))
            {
                var task = graph.ComputePath(new NodeGraph.PathRequest {
                    path = path, startPos = from,
                    endPos = to,
                    hullClassification = HullClassification.Human, maxJumpHeight = 0f, maxSlope = 45f, maxSpeed = 1000f,
                });
                task.Wait();
                if (!task.wasReachable || path.waypointsCount > 250) return false;
                for (int i = 0; i < path.waypointsCount; i++)
                {
                    var waypoint = path[i];
                    if (waypoint.minJumpHeight > 0.1f || !graph.GetNodePosition(waypoint.nodeIndex, out var pos)) return false;
                    result.Add(pos);
                }
                result.Add(to);
            }
            // No jump links or gaps: carriers are grounded, unlike the player's motor.
            var previous = from;
            for (int i = 0; i < result.Count; i++)
            {
                var target = result[i];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(previous, target) / 2f));
                if (steps > 200) return false;
                for (int step = 0; step <= steps; step++)
                {
                    var point = Vector3.Lerp(previous, target, step / (float)steps);
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var floor, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || floor.normal.y < 0.65f) return false;
                }
                if (Physics.Linecast(previous + Vector3.up, target + Vector3.up, UnityHost.Mask, QueryTriggerInteraction.Ignore)) return false;
                previous = target;
            }
            return result.Count > 0;
        }

        static int CarryPath(IntPtr unused, ref Native.Vec3 from, ref Native.Vec3 to, IntPtr output, uint capacity)
        {
            try
            {
                var points = new List<Vector3>();
                if (ShipRepairMode.Active)
                {
                    if (!TryPath(UnityHost.ToUnity(from), UnityHost.ToUnity(to), points)) return -1;
                }
                else points.Add(UnityHost.ToUnity(to));
                if (points.Count > capacity) return -1;
                int stride = Marshal.SizeOf<Native.Vec3>();
                for (int i = 0; i < points.Count; i++) Marshal.StructureToPtr(UnityHost.ToSim(points[i]), IntPtr.Add(output, i * stride), false);
                return points.Count;
            }
            catch (Exception error) { Plugin.Log.LogWarning("Ship part path: " + error.Message); return -1; }
        }
    }
}
