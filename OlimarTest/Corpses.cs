using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Enemies' own bodies as the corpses Pikmin carry (the sim's pellets: BTeki::dieSoon →
    // becomePellet). At death the body's model is kept back from RoR2's corpse clean-up and lies
    // where the death left it. Once Pikmin lift its pellet the ragdoll is frozen, set down on
    // the carriers, and follows the pellet from then on, shrinking into the Onion with it. A
    // pellet whose body is gone (a void death, a fall out of the world) is drawn as a numbered
    // pellet instead. Host only, like the rest of the drawing.
    internal sealed class Corpses
    {
        class Body
        {
            public Transform model;
            public ModelLocator locator;
            public bool frozen;
            // The model's pose in its pellet's frame, and its own scale.
            public Vector3 offset;
            public Quaternion turn;
            public Vector3 size;
            // Where the pellet is drawn this frame, if the model view said.
            public bool posed;
            public Vector3 pos;
            public Quaternion rot;
        }

        readonly Dictionary<uint, Body> byPellet = new Dictionary<uint, Body>();
        readonly List<(uint id, bool delivered)> gone = new List<(uint, bool)>();

        /// Keeps `body`'s model as pellet `pellet`'s corpse.
        public void Claim(uint pellet, CharacterBody body)
        {
            var ml = body ? body.modelLocator : null;
            var model = ml ? ml.modelTransform : null;
            if (!model) return;
            // Kept when the body object goes, and not handed to RoR2's Corpse clean-up.
            ml.preserveModel = true;
            ml.noCorpse = true;
            if (model.parent) model.SetParent(null, true);
            byPellet[pellet] = new Body { model = model, locator = ml };
        }

        /// Whether pellet `pellet` is drawn as its body. Notes where the pellet is drawn this
        /// frame (Unity space), which the body follows once it's carried.
        public bool Pose(uint pellet, Vector3 pos, Quaternion rot)
        {
            if (!byPellet.TryGetValue(pellet, out var b) || !b.model) return false;
            b.posed = true;
            b.pos = pos;
            b.rot = rot;
            return true;
        }

        public bool Has(uint pellet) => byPellet.TryGetValue(pellet, out var b) && b.model;

        /// Moves the bodies being carried, and lets go of the ones whose pellet is gone.
        public void Update(PikminWorld world)
        {
            gone.Clear();
            foreach (var kv in byPellet)
            {
                var b = kv.Value;
                int i = Find(world, kv.Key);
                if (!b.model || i < 0 || world.Pellets[i].state == 3)
                {
                    gone.Add((kv.Key, i >= 0 && world.Pellets[i].state == 3));
                    continue;
                }
                var p = world.Pellets[i];
                // Lying where it fell until it's first lifted.
                if (!b.frozen && p.state == 0)
                {
                    b.posed = false;
                    continue;
                }
                if (!b.posed)
                {
                    b.pos = UnityHost.ToUnity(p.pos);
                    b.rot = Quaternion.Euler(0f, p.yaw * Mathf.Rad2Deg, 0f);
                }
                if (!b.frozen) Freeze(b, UnityHost.ToUnity(p.pos).y);
                float s = Mathf.Clamp(p.scale, 0.01f, 1f);
                b.model.SetPositionAndRotation(b.pos + b.rot * (b.offset * s), b.rot * b.turn);
                b.model.localScale = b.size * s;
                b.posed = false;
            }
            foreach (var (id, delivered) in gone)
            {
                Release(byPellet[id], delivered);
                byPellet.Remove(id);
            }
        }

        static int Find(PikminWorld world, uint pellet)
        {
            for (int i = 0; i < world.PelletCount; i++)
                if (world.Pellets[i].id == pellet) return i;
            return -1;
        }

        // The ragdoll stops and stops colliding, and the body is set down centred on its pellet
        // with its lowest point on the ground (`ground`, Unity y).
        static void Freeze(Body b, float ground)
        {
            if (b.locator) b.locator.autoUpdateModelTransform = false;
            foreach (var rb in b.model.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.detectCollisions = false;
            }
            foreach (var col in b.model.GetComponentsInChildren<Collider>())
                col.enabled = false;

            var shift = Vector3.zero;
            bool any = false;
            var box = new Bounds();
            foreach (var r in b.model.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (!any) box = r.bounds;
                else box.Encapsulate(r.bounds);
                any = true;
            }
            if (any)
            {
                shift = b.pos - box.center;
                shift.y = ground - box.min.y;
            }
            var inv = Quaternion.Inverse(b.rot);
            b.offset = inv * (b.model.position + shift - b.pos);
            b.turn = inv * b.model.rotation;
            b.size = b.model.localScale;
            b.frozen = true;
        }

        // A carried body is done with (delivered, or its sim's gone); one still lying where it
        // fell goes back to RoR2's corpse clean-up.
        static void Release(Body b, bool delivered)
        {
            if (!b.model) return;
            if (b.frozen || delivered) Object.Destroy(b.model.gameObject);
            else b.model.gameObject.AddComponent<RoR2.Corpse>();
        }

        /// The sim is gone: bodies on the ground are RoR2's again, carried ones go.
        public void Clear()
        {
            foreach (var b in byPellet.Values) Release(b, delivered: false);
            byPellet.Clear();
        }
    }
}
