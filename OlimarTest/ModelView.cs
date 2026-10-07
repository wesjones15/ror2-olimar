using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Pikmin's own models from the user's disc, through libpikmin's view (pik_view_*): one
    // Unity mesh per model built at load, then every frame one object per posed instance
    // (Olimar, each Pikmin and its leaf, Onions, pellets, sprouts, the cursor) with the
    // vertices libpikmin skinned for it. Materials use RoR2's own HGStandard shader: the model's
    // texture times a colour libpikmin works out from each material's TEV setup, so the game's
    // lighting applies. The same view plays the game's sounds for the sim's events (not its
    // music).
    internal sealed class ModelView : IDisposable
    {
        IntPtr view;
        readonly Transform root;

        class Model
        {
            public Mesh template;
            public Material[] materials;
            public int vertexCount;
            public int batchCount;
            public Bounds bounds;
        }

        class Instance
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;
            public uint model;
            public bool seen;
        }

        Model[] models;
        readonly Dictionary<ulong, Instance> instances = new Dictionary<ulong, Instance>();
        Native.Draw[] draws = new Native.Draw[512];
        int drawCount;
        Vector3[] vbuf = new Vector3[0], nbuf = new Vector3[0];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor");

        public string Status { get; private set; }

        /// This frame's instances (after Update).
        public int DrawCount => drawCount;
        public Native.Draw DrawAt(int i) => draws[i];

        ModelView(IntPtr view, Transform root)
        {
            this.view = view;
            this.root = root;
        }

        /// Loads the models and opens the sound output. Null (and logs why) if the models can't
        /// be loaded; the caller then draws stand-ins.
        public static ModelView Create(string assetsDir, Transform parent, bool sound)
        {
            var v = Native.pik_view_new(Native.CString(assetsDir), (byte)(sound ? 1 : 0));
            if (v == IntPtr.Zero)
            {
                Plugin.Log.LogWarning($"Pikmin's models didn't load from {assetsDir}; drawing stand-ins");
                return null;
            }
            var root = new GameObject("PikminModels").transform;
            root.SetParent(parent, false);
            var mv = new ModelView(v, root);
            try
            {
                mv.Build();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"building Pikmin's meshes: {e}");
                mv.Dispose();
                return null;
            }
            mv.Status = Native.Utf8(Native.pik_view_status(v));
            return mv;
        }

        static Shader shader;

        // RoR2's HGStandard, taken off Commando's own material.
        static Shader HgStandard()
        {
            if (shader) return shader;
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var cm = commando ? commando.GetComponentInChildren<CharacterModel>() : null;
            if (cm && cm.baseRendererInfos != null)
                foreach (var info in cm.baseRendererInfos)
                    if (info.defaultMaterial && info.defaultMaterial.shader)
                    {
                        shader = info.defaultMaterial.shader;
                        break;
                    }
            if (!shader)
            {
                // The primitive's default material as a last resort.
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shader = probe.GetComponent<Renderer>().sharedMaterial.shader;
                UnityEngine.Object.DestroyImmediate(probe);
                Plugin.Log.LogWarning($"HGStandard not found; using {shader.name}");
            }
            return shader;
        }

        void Build()
        {
            int n = (int)Native.pik_view_model_count(view);
            models = new Model[n];
            var textures = new Dictionary<(uint, int), Texture2D>();
            for (uint m = 0; m < n; m++)
            {
                if (Native.pik_view_model(view, m, out var info) == 0)
                    throw new Exception($"model {m} has no info");
                var batches = new Native.Batch[info.batch_count];
                Native.pik_view_batches(view, m, batches, info.batch_count);
                var indices = new int[info.index_count];
                Native.pik_view_indices(view, m, indices, info.index_count);
                var uvPairs = new float[info.vertex_count * 2];
                Native.pik_view_uvs(view, m, uvPairs, info.vertex_count);
                var uvs = new Vector2[info.vertex_count];
                for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uvPairs[2 * i], uvPairs[2 * i + 1]);

                var mesh = new Mesh { name = $"pikmin model {m}" };
                mesh.vertices = new Vector3[info.vertex_count];
                mesh.uv = uvs;
                mesh.subMeshCount = batches.Length;
                var materials = new Material[batches.Length];
                for (int b = 0; b < batches.Length; b++)
                {
                    var tri = new int[batches[b].index_count];
                    Array.Copy(indices, batches[b].first_index, tri, 0, tri.Length);
                    mesh.SetTriangles(tri, b, false);
                    Texture2D tex = null;
                    if (batches[b].texture >= 0 && !textures.TryGetValue((m, batches[b].texture), out tex))
                    {
                        tex = LoadTexture(m, (uint)batches[b].texture);
                        textures[(m, batches[b].texture)] = tex;
                    }
                    materials[b] = MakeMaterial(batches[b], tex);
                }
                // Animations reach past the bind pose; a roomy box saves recomputing it.
                var lo = new Vector3(info.lo.x, info.lo.y, info.lo.z);
                var hi = new Vector3(info.hi.x, info.hi.y, info.hi.z);
                var bounds = new Bounds((lo + hi) * 0.5f, (hi - lo) * 2f + Vector3.one * 20f);
                mesh.bounds = bounds;
                models[m] = new Model
                {
                    template = mesh,
                    materials = materials,
                    vertexCount = (int)info.vertex_count,
                    batchCount = batches.Length,
                    bounds = bounds,
                };
            }
        }

        Texture2D LoadTexture(uint model, uint slot)
        {
            if (Native.pik_view_texture(view, model, slot, out var t, null, 0) == 0) return null;
            var px = new byte[t.width * t.height * 4];
            if (Native.pik_view_texture(view, model, slot, out t, px, (uint)px.Length) == 0) return null;
            // Rows top first, as Unity's v = 0 is the first row: GX's v = 0 is the top.
            var tex = new Texture2D((int)t.width, (int)t.height, TextureFormat.RGBA32, true)
            {
                name = $"pikmin texture {model}/{slot}",
                filterMode = FilterMode.Bilinear,
                wrapModeU = Wrap(t.wrap_s),
                wrapModeV = Wrap(t.wrap_t),
            };
            tex.LoadRawTextureData(px);
            tex.Apply(true, true);
            return tex;
        }

        static TextureWrapMode Wrap(byte w) =>
            w == 1 ? TextureWrapMode.Clamp : w == 2 ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;

        static Material MakeMaterial(Native.Batch b, Texture2D tex)
        {
            var m = new Material(HgStandard()) { name = "pikmin" };
            if (tex) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            // Translucent materials (Olimar's helmet, the Onion's glow) become cutouts:
            // HGStandard is deferred.
            if (b.blend != 0)
            {
                m.SetFloat("_EnableCutout", 1f);
                m.EnableKeyword("CUTOUT");
            }
            // Both sides, as the game draws some of these (leaves, the cursor) and the
            // handedness flip above is easy to get backwards.
            m.SetFloat("_Cull", 0f);
            // Smoothed two tone like RoR2's characters; "unlitish" for what the game draws unlit
            // (Pikmin eyes, the cursor).
            m.SetFloat("_RampInfo", b.lit != 0 ? 1f : 3f);
            m.SetFloat("_SpecularStrength", 0.1f);
            m.SetFloat("_SpecularExponent", 10f);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Fade", 1f);
            return m;
        }

        /// Poses the sim and moves the objects. `frame` carries the camera and volume.
        public unsafe void Update(IntPtr sim, ref Native.ViewFrame frame, float scale)
        {
            if (view == IntPtr.Zero || sim == IntPtr.Zero) return;
            Native.pik_view_update(view, sim, ref frame);
            uint n = Native.pik_view_draws(view, null, 0);
            if (n > draws.Length) draws = new Native.Draw[Math.Max(n, (uint)draws.Length * 2)];
            drawCount = (int)Native.pik_view_draws(view, draws, (uint)draws.Length);
            var pos = (Vector3*)Native.pik_view_positions(view, out uint vc);
            var nrm = (Vector3*)Native.pik_view_normals(view, out _);
            var col = (Color*)Native.pik_view_colors(view, out uint cc);

            foreach (var inst in instances.Values) inst.seen = false;
            for (int i = 0; i < drawCount; i++)
            {
                var d = draws[i];
                if (d.model >= models.Length) continue;
                var model = models[d.model];
                if (d.first_vertex + model.vertexCount > vc || d.first_color + model.batchCount > cc) continue;
                var inst = Get(d.key, d.model);
                inst.seen = true;
                if (!inst.go.activeSelf) inst.go.SetActive(true);
                var t = inst.go.transform;
                t.position = new Vector3(d.pos.x, d.pos.y, d.pos.z) * scale;
                t.rotation = new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w);
                // The sim's numbers go into Unity unchanged, but Pikmin's models are right-handed
                // and Unity is left-handed: mirroring each model across its own X puts Olimar's
                // right hand back on his right (and the pellets' numbers the right way round)
                // without moving or turning anything.
                t.localScale = new Vector3(-scale, scale, scale);

                int vn = model.vertexCount;
                if (vbuf.Length < vn)
                {
                    vbuf = new Vector3[vn];
                    nbuf = new Vector3[vn];
                }
                fixed (Vector3* vd = vbuf)
                fixed (Vector3* nd = nbuf)
                {
                    long bytes = (long)vn * sizeof(Vector3);
                    Buffer.MemoryCopy(pos + d.first_vertex, vd, bytes, bytes);
                    Buffer.MemoryCopy(nrm + d.first_vertex, nd, bytes, bytes);
                }
                inst.mesh.SetVertices(vbuf, 0, vn);
                inst.mesh.SetNormals(nbuf, 0, vn);

                for (int b = 0; b < model.batchCount; b++)
                {
                    block.Clear();
                    block.SetColor(ColorId, col[d.first_color + b]);
                    block.SetFloat(FadeId, Mathf.Clamp01(d.alpha));
                    if ((d.flags & 1) != 0) block.SetColor(FlashId, new Color(1f, 1f, 1f, 0.5f));
                    inst.renderer.SetPropertyBlock(block, b);
                }
            }
            foreach (var inst in instances.Values)
                if (!inst.seen && inst.go.activeSelf) inst.go.SetActive(false);
        }

        Instance Get(ulong key, uint model)
        {
            if (instances.TryGetValue(key, out var inst) && inst.go)
            {
                if (inst.model == model) return inst;
                // A leaf became a bud: same object, new model.
                UnityEngine.Object.Destroy(inst.mesh);
                inst.mesh = UnityEngine.Object.Instantiate(models[model].template);
                inst.mesh.MarkDynamic();
                inst.go.GetComponent<MeshFilter>().sharedMesh = inst.mesh;
                inst.renderer.sharedMaterials = models[model].materials;
                inst.model = model;
                return inst;
            }
            var go = new GameObject($"pikmin {key >> 56}/{key & 0xFFFFFFFF}");
            go.transform.SetParent(root, false);
            var mesh = UnityEngine.Object.Instantiate(models[model].template);
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = models[model].materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            inst = new Instance { go = go, renderer = r, mesh = mesh, model = model };
            instances[key] = inst;
            return inst;
        }

        public void Dispose()
        {
            if (view != IntPtr.Zero)
            {
                Native.pik_view_free(view);
                view = IntPtr.Zero;
            }
            foreach (var inst in instances.Values)
                if (inst.mesh) UnityEngine.Object.Destroy(inst.mesh);
            instances.Clear();
            if (models != null)
                foreach (var m in models)
                {
                    if (m == null) continue;
                    if (m.template) UnityEngine.Object.Destroy(m.template);
                    foreach (var mat in m.materials)
                    {
                        if (!mat) continue;
                        var tex = mat.GetTexture("_MainTex");
                        if (tex) UnityEngine.Object.Destroy(tex);
                        UnityEngine.Object.Destroy(mat);
                    }
                }
            models = null;
            if (root) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
