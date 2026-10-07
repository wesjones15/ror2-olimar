using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OlimarTest;

// A flat floor at y = 0, a wall along x = 400 and one creature 100 units ahead, answered from
// C# the way the plugin's UnityHost and PikminWorld answer them in the game.
static class Program
{
    static int failures;
    static int groundCalls, wallCalls;
    static readonly List<(ulong id, float amount, uint source)> hits = new List<(ulong, float, uint)>();

    static readonly Native.GroundFn groundFn = (IntPtr u, ref Native.Vec3 p, out float y) => { groundCalls++; y = 0f; return 1; };
    static readonly Native.WallsFn wallsFn = (IntPtr u, ref Native.Vec3 c, float r, out Native.Vec3 o) =>
    {
        wallCalls++;
        o = c;
        o.x = Math.Min(c.x, 400f - r);
    };
    static readonly Native.DamageFn damageFn = (u, creature, amount, part, source, id) => { hits.Add((creature, amount, source)); return 1; };

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args.Length > 0 ? args[0] : "pikmin.dll");
        Check(Native.Load(dll, out string err), $"load {dll} {err}");
        if (err != null) return 1;
        Check(Native.pik_tick_hz() == 30, "30 Hz");

        var host = new Native.Host
        {
            ground_height = Marshal.GetFunctionPointerForDelegate(groundFn),
            walls = Marshal.GetFunctionPointerForDelegate(wallsFn),
            damage = Marshal.GetFunctionPointerForDelegate(damageFn),
        };
        // An assets folder as the second argument checks the disc tuning loads too.
        IntPtr sim = Native.pik_sim_new(args.Length > 1 ? Native.CString(args[1]) : null, 7);
        if (args.Length > 1) Check(Native.pik_assets_complete(Native.CString(args[1])) == 1, "assets complete");
        Check(sim != IntPtr.Zero, "sim started: " + Native.Utf8(Native.pik_sim_params_source(sim)));

        var origin = new Native.Vec3(0, 0, 0);
        int c = Native.pik_sim_add_captain(sim, ref origin);
        for (int i = 0; i < 6; i++)
        {
            var at = new Native.Vec3(-20f - 4f * i, 0, 0);
            Native.pik_sim_spawn_piki(sim, Native.Red, ref at, c);
        }
        var target = new Native.Vec3(100, 0, 0);
        var creatures = new[] { new Native.Creature { id = 0xDEADBEEF01, pos = target, alive = 1, parts_start = 0, parts_len = 1 } };
        var parts = new[] { new Native.Part { centre = new Native.Vec3(100, 15, 0), radius = 20, stickable = 1 } };

        void Run(Native.Input input, float secs)
        {
            Native.pik_sim_set_input(sim, (uint)c, ref input);
            for (int i = 0; i < (int)(secs * 50); i++)
            {
                Native.pik_sim_set_creatures(sim, creatures, 1, parts, 1);
                Native.pik_sim_advance(sim, ref host, 0.02f);
            }
        }
        var aim = new Native.Input { use_cursor = 1, cursor = target };
        Run(aim, 1f);
        var throwing = aim;
        throwing.throw_ = 1;
        Run(throwing, 1f);
        Run(aim, 3f);

        var events = new Native.Event[256];
        uint n = Native.pik_sim_events(sim, events, (uint)events.Length);
        bool thrown = false, latched = false;
        for (int i = 0; i < n; i++)
        {
            thrown |= events[i].kind == Native.EventKind.Thrown;
            latched |= events[i].kind == Native.EventKind.Latched && events[i].has_creature == 1 && events[i].creature == 0xDEADBEEF01;
        }
        Check(thrown && latched, $"threw and latched on ({n} events)");
        Check(hits.Exists(h => h.id == 0xDEADBEEF01 && h.amount > 0 && h.source == Native.SourcePiki), $"damage callback ({hits.Count} hits)");
        Check(groundCalls > 0 && wallCalls > 0, $"ground {groundCalls}, walls {wallCalls} calls");

        Native.pik_sim_captain(sim, (uint)c, out var cap);
        Check(Math.Abs(cap.cursor_world.x - 100f) < 1f, $"cursor at {cap.cursor_world.x}");
        var pikis = new Native.Piki[4];
        uint total = Native.pik_sim_pikis(sim, pikis, 4);
        Check(total == 6 && pikis[0].color == Native.Red && pikis[0].alive == 1, $"{total} Pikmin read back into a short buffer");

        // Walk into the wall, with the creature (which he'd bump into) out of the way.
        creatures[0].pos = new Native.Vec3(100, 0, 600);
        parts[0].centre = new Native.Vec3(100, 15, 600);
        Run(new Native.Input { stick_x = 1 }, 6f);
        Native.pik_sim_captain(sim, (uint)c, out cap);
        Check(cap.pos.x > 300f && cap.pos.x <= 400f, $"stopped at the wall, x = {cap.pos.x}");

        // A corpse and an Onion.
        var desc = new Native.PelletDesc { pos = new Native.Vec3(50, 0, 50), radius = 20, height = 10, min_carriers = 1, max_carriers = 2, seeds_matching = 2, seeds_other = 1, color = -1 };
        Check(Native.pik_sim_spawn_pellet(sim, ref desc) == 0, "corpse pellet");
        var onionAt = new Native.Vec3(cap.pos.x - 30f, 0, 0);
        int onion = Native.pik_sim_add_onion(sim, ref onionAt, 0b111);
        Native.pik_sim_store_in_onion(sim, (uint)onion, Native.Yellow, 0, 4);
        Run(default, 0.2f);
        Check(Native.pik_sim_onion_in_reach(sim, (uint)c) == onion, "Onion in reach");
        Check(Native.pik_sim_onion_counts(sim, (uint)onion, (uint)c, Native.Yellow, out var counts) == 1 && counts.in_onion == 4,
            $"Onion counts {counts.in_onion}/{counts.in_squad}/{counts.on_field}/{counts.max_on_field}");
        Check(Native.pik_sim_onion_call_out(sim, (uint)c, (uint)onion, Native.Yellow, 10) == 4, "called 4 out");
        var onions = new Native.Onion[2];
        Check(Native.pik_sim_onions(sim, onions, 2) == 1 && onions[0].colors == 0b111, "Onion read back");
        var sprouts = new Native.Sprout[2];
        Native.pik_sim_sprouts(sim, sprouts, 2);
        var pellets = new Native.Pellet[2];
        Check(Native.pik_sim_pellets(sim, pellets, 2) == 1 && pellets[0].min_carriers == 1, "pellet read back");
        var centre = new Native.Vec3(cap.pos.x, 0, 0);
        Native.pik_sim_attack_pikis_in_sphere(sim, ref centre, 1000f, 1000f);
        Check(Native.pik_sim_poisoned(sim) == 0, "not poisoned");

        if (args.Length > 1)
        {
            // The view: models through the same structs the plugin builds meshes from, then a
            // frame posed (no sound: nothing should play from a check).
            IntPtr view = Native.pik_view_new(Native.CString(args[1]), 0);
            Check(view != IntPtr.Zero, "view: " + Native.Utf8(Native.pik_view_status(view)));
            uint models = Native.pik_view_model_count(view);
            int badModels = 0;
            for (uint m = 0; m < models; m++)
            {
                Native.pik_view_model(view, m, out var info);
                var batches = new Native.Batch[info.batch_count];
                var idx = new int[info.index_count];
                var uv = new float[info.vertex_count * 2];
                bool ok = Native.pik_view_batches(view, m, batches, info.batch_count) == info.batch_count
                          && Native.pik_view_indices(view, m, idx, info.index_count) == info.index_count
                          && Native.pik_view_uvs(view, m, uv, info.vertex_count) == info.vertex_count
                          && Array.TrueForAll(idx, i => i >= 0 && i < info.vertex_count);
                foreach (var b in batches)
                    if (b.texture >= 0)
                    {
                        ok &= Native.pik_view_texture(view, (uint)b.texture, out var t, null, 0) == 1;
                        var px = new byte[t.width * t.height * 4];
                        ok &= Native.pik_view_texture(view, (uint)b.texture, out t, px, (uint)px.Length) == 1;
                    }
                if (!ok) badModels++;
            }
            Check(models == 15 && badModels == 0, $"{models} models read back ({badModels} bad)");
            var frame = new Native.ViewFrame
            {
                dt = 1f / 60f, forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0), listener = c, volume = 1f,
            };
            Native.pik_view_update(view, sim, ref frame);
            uint nd = Native.pik_view_draws(view, null, 0);
            var draws = new Native.Draw[nd];
            Native.pik_view_draws(view, draws, nd);
            Native.pik_view_positions(view, out uint vc);
            Native.pik_view_colors(view, out uint cc);
            IntPtr idsPtr = Native.pik_view_texture_ids(view, out uint tc);
            var ids = new int[tc];
            Marshal.Copy(idsPtr, ids, 0, (int)tc);
            int textured = 0, badTextures = 0;
            foreach (int id in ids)
            {
                if (id < 0) continue;
                textured++;
                if (Native.pik_view_texture(view, (uint)id, out var t, null, 0) == 0 || t.width == 0) badTextures++;
            }
            Check(tc == cc && textured > 0 && badTextures == 0, $"{textured} textured batches drawn ({badTextures} bad)");
            var last = draws[nd - 1];
            Native.pik_view_model(view, last.model, out var li);
            Check(Array.Exists(draws, d => d.kind == Native.KindCaptain) && Array.Exists(draws, d => d.kind == Native.KindOnion)
                  && last.first_vertex + li.vertex_count == vc && last.first_color + li.batch_count == cc,
                $"{nd} draws, {vc} vertices, {cc} colours");

            // Whistling makes particles, each with a texture.
            var whistle = new Native.Input { whistle = 1 };
            Native.pik_sim_set_input(sim, (uint)c, ref whistle);
            for (int i = 0; i < 20; i++)
            {
                Native.pik_sim_set_creatures(sim, creatures, 1, parts, 1);
                Native.pik_sim_advance(sim, ref host, 1f / 60f);
                Native.pik_view_update(view, sim, ref frame);
            }
            Native.pik_view_particles(view, out uint pn);
            Check(pn > 0 && Native.pik_view_particle_texture(view, 0, out var pt, null, 0) == 1 && pt.width > 0,
                $"{pn} whistle particles");
            Native.pik_view_free(view);

            // The sound output opens and closes in a plain process (nothing is played).
            IntPtr loud = Native.pik_view_new(Native.CString(args[1]), 1);
            Check(loud != IntPtr.Zero, "view with sound: " + Native.Utf8(Native.pik_view_status(loud)));
            Native.pik_view_free(loud);
        }
        Native.pik_sim_free(sim);

        Console.WriteLine(failures == 0 ? "all good" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
