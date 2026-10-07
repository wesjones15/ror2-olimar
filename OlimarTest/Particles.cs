using System;
using System.Collections.Generic;
using Ror2Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace OlimarTest
{
    // Pikmin's particles (the whistle, dead Pikmin's ghosts): camera-facing quads libpikmin's
    // view works out each frame (pik_view_particles), drawn on Cloud Remap, one mesh per texture
    // and blend rebuilt every frame. Pikmin's own; libsms's view has no particles, so this isn't
    // in ror2-common.
    internal sealed class Particles : IDisposable
    {
        readonly IntPtr view;
        readonly Transform root;

        // Particles sharing a texture and blend, as one mesh.
        class Group
        {
            public GameObject go;
            public Mesh mesh;
            public Material material;
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> triangles = new List<int>();
        }

        readonly Dictionary<(uint, byte), Group> groups = new Dictionary<(uint, byte), Group>();
        readonly Dictionary<uint, Texture2D> textures = new Dictionary<uint, Texture2D>();
        static readonly Vector2[] Corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };

        /// `view` stays owned by the caller. `parent` null keeps the objects across scenes.
        public Particles(IntPtr view, Transform parent)
        {
            this.view = view;
            root = new GameObject("PikminParticles").transform;
            if (parent) root.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(root.gameObject);
        }

        /// This frame's particles (after the view's update), in world space times `scale`.
        public unsafe void Draw(float scale)
        {
            var ps = (Native.Particle*)Native.pik_view_particles(view, out uint n);
            foreach (var g in groups.Values)
            {
                g.vertices.Clear();
                g.uvs.Clear();
                g.colors.Clear();
                g.triangles.Clear();
            }
            for (uint i = 0; i < n && ps != null; i++)
            {
                var p = ps[i];
                var g = GroupFor(p.texture, p.additive);
                if (g == null) continue;
                int b = g.vertices.Count;
                g.vertices.Add(new Vector3(p.c0.x, p.c0.y, p.c0.z) * scale);
                g.vertices.Add(new Vector3(p.c1.x, p.c1.y, p.c1.z) * scale);
                g.vertices.Add(new Vector3(p.c2.x, p.c2.y, p.c2.z) * scale);
                g.vertices.Add(new Vector3(p.c3.x, p.c3.y, p.c3.z) * scale);
                var c = new Color(p.r, p.g, p.b, p.a);
                for (int k = 0; k < 4; k++)
                {
                    g.uvs.Add(Corners[k]);
                    g.colors.Add(c);
                }
                g.triangles.Add(b);
                g.triangles.Add(b + 1);
                g.triangles.Add(b + 2);
                g.triangles.Add(b);
                g.triangles.Add(b + 2);
                g.triangles.Add(b + 3);
            }
            foreach (var g in groups.Values)
            {
                bool any = g.vertices.Count > 0;
                if (g.go.activeSelf != any) g.go.SetActive(any);
                if (!any) continue;
                g.mesh.Clear();
                g.mesh.SetVertices(g.vertices);
                g.mesh.SetUVs(0, g.uvs);
                g.mesh.SetColors(g.colors);
                g.mesh.SetTriangles(g.triangles, 0, true);
            }
        }

        // The mesh for particles with texture `id` and this blend; null without Cloud Remap or
        // the texture.
        Group GroupFor(uint id, byte additive)
        {
            if (groups.TryGetValue((id, additive), out var g)) return g;
            if (!textures.TryGetValue(id, out var tex))
            {
                try { tex = LoadTexture(id); }
                catch (Exception e) { Plugin.Log.LogWarning($"Pikmin particle texture {id}: {e.Message}"); }
                textures[id] = tex;
            }
            if (!tex) return null;
            var m = Shaders.SeeThrough($"pikmin particles {id}", tex, additive != 0, true);
            if (!m) return null;
            m.renderQueue = 3100;
            // libpikmin folds an additive layer's alpha into its colour already.
            if (additive != 0) m.SetFloat("_SrcBlend", (float)BlendMode.One);
            var go = new GameObject($"pikmin particles {id}");
            go.transform.SetParent(root, false);
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            g = new Group { go = go, mesh = mesh, material = m };
            groups[(id, additive)] = g;
            return g;
        }

        Texture2D LoadTexture(uint id)
        {
            if (Native.pik_view_particle_texture(view, id, out var t, null, 0) == 0) return null;
            var px = new byte[t.width * t.height * 4];
            if (Native.pik_view_particle_texture(view, id, out t, px, (uint)px.Length) == 0) return null;
            return ModelView.MakeTexture($"pikmin particle texture {id}", t, px);
        }

        public void Dispose()
        {
            foreach (var g in groups.Values)
            {
                if (g.mesh) UnityEngine.Object.Destroy(g.mesh);
                if (g.material) UnityEngine.Object.Destroy(g.material);
            }
            groups.Clear();
            foreach (var tex in textures.Values)
                if (tex) UnityEngine.Object.Destroy(tex);
            textures.Clear();
            if (root) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
