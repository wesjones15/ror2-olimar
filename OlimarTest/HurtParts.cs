using System;
using UnityEngine;

namespace OlimarTest
{
    // An enemy's hurtbox as the spheres the sim latches Pikmin onto. The sim knows a part only as a
    // sphere (a Pikmin that touches it sticks to its surface), so a part has to be a sphere that sits
    // on the real shape. The collider's bounding box doesn't: it is axis-aligned, so a tilted capsule or
    // a long box gets a box and a radius far bigger than itself, and the Pikmin stuck to its surface
    // hang in the air beside the enemy. Here each collider becomes spheres that fit it:
    // - a sphere collider is itself;
    // - a capsule is a row of spheres of its radius along its axis;
    // - a box is a row of spheres of its smallest half-size along its long axis, inside it, so a Pikmin
    //   latches on the surface or just under it, never off it;
    // - anything else (a mesh) a sphere of the bounding box's smallest half-extent.
    // Everything is computed from the transform each tick, not the collider's cached bounds, which only
    // catch up with an animated or moving enemy at the next physics sync (a frame or two behind).
    internal static class HurtParts
    {
        /// At most this many spheres for one collider.
        public const int MaxPerCollider = 5;

        public delegate void Add(Vector3 centre, float radius);

        public static int For(Collider col, Add add)
        {
            if (!col) return 0;
            var t = col.transform;
            var scale = t.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            switch (col)
            {
                case SphereCollider s:
                {
                    float r = s.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    return Emit(add, t.TransformPoint(s.center), r);
                }
                case CapsuleCollider c:
                {
                    int axis = Mathf.Clamp(c.direction, 0, 2);
                    float along = scale[axis];
                    float across = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float r = c.radius * across;
                    float half = Mathf.Max(0f, c.height * along * 0.5f - r);
                    var dir = t.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward).normalized;
                    return Row(add, t.TransformPoint(c.center), dir, half, r);
                }
                case BoxCollider b:
                {
                    var half = new Vector3(b.size.x * scale.x, b.size.y * scale.y, b.size.z * scale.z) * 0.5f;
                    int longest = half.x >= half.y && half.x >= half.z ? 0 : half.y >= half.z ? 1 : 2;
                    float r = Mathf.Max(0.02f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
                    var dir = t.TransformDirection(longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward).normalized;
                    return Row(add, t.TransformPoint(b.center), dir, Mathf.Max(0f, half[longest] - r), r);
                }
                default:
                {
                    var bounds = col.bounds;
                    var e = bounds.extents;
                    return Emit(add, bounds.center, Mathf.Max(0.02f, Mathf.Min(e.x, Mathf.Min(e.y, e.z))));
                }
            }
        }

        static int Emit(Add add, Vector3 centre, float radius)
        {
            add(centre, Mathf.Max(0.02f, radius));
            return 1;
        }

        // Spheres of `radius` from -half to +half along `dir` through `centre`, each overlapping the next.
        static int Row(Add add, Vector3 centre, Vector3 dir, float half, float radius)
        {
            radius = Mathf.Max(0.02f, radius);
            int n = half <= 0f ? 1 : Mathf.Clamp(Mathf.CeilToInt(half * 2f / radius) + 1, 2, MaxPerCollider);
            for (int i = 0; i < n; i++)
            {
                float k = n == 1 ? 0f : Mathf.Lerp(-half, half, i / (float)(n - 1));
                add(centre + dir * k, radius);
            }
            return n;
        }
    }
}
