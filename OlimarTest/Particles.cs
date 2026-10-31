using System;
using System.Collections.Generic;
using Ror2Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace OlimarTest
{
    // Native P1/P2 particle quads: common TEV baking, independent samplers and refraction materials.
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
        readonly Dictionary<uint,Texture2D> masks = new Dictionary<uint,Texture2D>();
        readonly Dictionary<uint,uint> versions = new Dictionary<uint,uint>();
        readonly Dictionary<uint, Texture2D> textures = new Dictionary<uint, Texture2D>();

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
            foreach (var pair in textures) {
                if (!pair.Value) continue;
                uint version=Native.pik_view_particle_texture_version(view,pair.Key);
                if (versions.TryGetValue(pair.Key,out uint old) && old==version) continue;
                if (Native.pik_view_particle_texture(view,pair.Key,out var info,null,0)!=0) {
                    var rgba=new byte[info.width*info.height*4];
                    if (Native.pik_view_particle_texture(view,pair.Key,out info,rgba,(uint)rgba.Length)!=0) {pair.Value.SetPixelData(rgba,0);pair.Value.Apply(true,false);versions[pair.Key]=version;}
                }
            }
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
                g.uvs.Add(new Vector2(p.u0,p.v0));g.uvs.Add(new Vector2(p.u1,p.v1));g.uvs.Add(new Vector2(p.u2,p.v2));g.uvs.Add(new Vector2(p.u3,p.v3));
                for (int k = 0; k < 4; k++)
                {
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
            Material m;
            if (additive==2) {
                if (!masks.TryGetValue(id,out var mask)) {
                    Native.pik_view_particle_texture(view,id,out var info,null,0);
                    var rgba=new byte[info.width*info.height*4];Native.pik_view_particle_texture(view,id,out info,rgba,(uint)rgba.Length);
                    for (int i=0;i<rgba.Length;i+=4) {byte a=rgba[i+2];rgba[i]=rgba[i+1]=rgba[i+2]=255;rgba[i+3]=a;}
                    mask=ModelView.MakeTexture("Pikmin indirect coverage",info,rgba,true);masks[id]=mask;
                }
                m=ParticleMaterials.Refraction($"Pikmin particle refraction {id}",tex,mask);
            } else m=Shaders.SeeThrough($"pikmin particles {id}",tex,additive==1,true);
            if (!m) return null;
            m.renderQueue = 3100;
            // libpikmin folds an additive layer's alpha into its colour already.
            if (additive == 1) m.SetFloat("_SrcBlend", (float)BlendMode.One);
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
            versions[id]=Native.pik_view_particle_texture_version(view,id);
            return ModelView.MakeTexture($"pikmin particle texture {id}", t, px,true);
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
            foreach (var mask in masks.Values) if (mask) UnityEngine.Object.Destroy(mask);masks.Clear();versions.Clear();
            if (root) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
