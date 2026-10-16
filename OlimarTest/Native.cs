using System;
using System.Runtime.InteropServices;
using System.Text;
using Ror2Common;

namespace OlimarTest
{
    // The libpikmin C ABI (libpikmin/crates/libpikmin-ffi/src/lib.rs and view.rs, PIK_ABI_VERSION 28). Every
    // struct mirrors a #[repr(C)] one there field for field, and Load() refuses a pikmin.dll
    // whose sizes differ. The view's model structs are ror2-common's (ViewSource.cs), which
    // libsms's view shares. Sim units: the decomp's (roughly cm), Y up; a facing angle a (radians)
    // points along (sin a, 0, cos a), the same as a Unity yaw.
    internal static class Native
    {
        public const string Dll = "pikmin";
        public const uint AbiVersion = 30;
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_part_onion_delivery(IntPtr sim, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_ship_part_weight(IntPtr sim, uint part);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_ship_part_name(IntPtr sim, uint part, byte[] output, uint cap);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_ship_part(IntPtr sim, uint part, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_recover_pellet(IntPtr sim, uint pellet, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_take_death_sting(IntPtr view, [Out] float[] samples, uint max);
        // Item hooks (libpikmin's items.rs, plucker.rs and jump.rs).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_attack_rate(IntPtr sim, uint captain, float rate);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_motion(IntPtr sim, uint captain, float jump, float accel);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_squad_speed(IntPtr sim, uint captain, float scale);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_jumps(IntPtr sim, uint captain, uint airJumps, byte stomp);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_boost_captain_jump(IntPtr sim, uint captain, float factor);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_growth(IntPtr sim, float growthRate, float rejoinDelay);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_sprout_stages(IntPtr sim, float stage, float flower);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_dismiss_strike(IntPtr sim, float damageScale, float reach, float cooldown, float charge);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_punch_damage(IntPtr sim, float damage);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_give_ship_part(IntPtr sim, uint ship, uint part);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_cursor_homing(IntPtr sim, byte on);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_squad_pace(IntPtr sim, float leaf, float bud, float flower);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_kill_growth(IntPtr sim, uint kills);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_credit_kill(IntPtr sim, ulong creature);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_auto_carry(IntPtr sim, float range);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_give_onion_seeds(IntPtr sim, uint onion, uint color, uint seeds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_auto_pluck(IntPtr sim, float seconds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_emergency(IntPtr sim, float seconds, uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_onion_owner(IntPtr sim, uint onion, int captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_sim_heal_squad(IntPtr sim, uint captain, float amount);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_sim_heal_pikis_in_sphere(IntPtr sim, ref Vec3 centre, float radius, float amount);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_teleport_squad(IntPtr sim, uint captain, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_captain_piki_count(IntPtr sim, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_spawn_squad(IntPtr sim, uint captain, ref Vec3 pos, uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_remove_captain_pikis(IntPtr sim, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_down_captain(IntPtr sim, uint captain, byte down);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_hold(IntPtr sim, uint captain, uint hold);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_launch_captain(IntPtr sim, uint captain, ref Vec3 velocity, float gravity, byte squad);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_recruit_to_pellet(IntPtr sim, uint pellet, ulong defeated, float range);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_lock_target(IntPtr sim, uint captain, ulong target, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_camera_preset(uint zoom, byte top, out Vec3 settings);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_camera_pan(uint mode, ref Vec3 angles, float x, float y, float dt, float minPitch);

        // Colour indices (PikiColor::index, the decomp's order).
        public const uint Blue = 0, Red = 1, Yellow = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Vec3
        {
            public float x, y, z;
            public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Host
        {
            public IntPtr user, ground_height, walls, damage, carry_path;
        }

        public const uint SourcePiki = 0, SourceCaptain = 1, SourceStrike = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public float stick_x, stick_y;
            public float swarm_x, swarm_y;
            public Vec3 pose_pos, pose_vel;
            public float pose_face;
            public Vec3 cursor;
            public byte whistle, throw_, dismiss, use_pose, use_cursor, cycle, jump, jump_blocked;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Creature
        {
            public ulong id;
            public Vec3 pos;
            public float yaw;
            public byte alive, flying, _p0, _p1;
            public uint parts_start, parts_len;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Part
        {
            public Vec3 centre;
            public float radius;
            public byte stickable, _p0, _p1, _p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PelletDesc
        {
            public ulong creature;
            public Vec3 pos;
            public float yaw, radius, height;
            public uint min_carriers, max_carriers;
            public int seeds_matching, seeds_other, color;
            public byte has_creature, _p0, _p1, _p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Captain
        {
            public Vec3 pos, vel;
            public float face;
            public Vec3 cursor_world;
            public float whistle_radius, health;
            public int held_piki;
            public uint squad;
            public byte on_ground, down, throwing, plucking;
        }

        // Piki.state values (piki_state in lib.rs).
        public static class PikiState
        {
            public const byte Idle = 0, Squad = 1, GoHang = 2, Held = 3, Flying = 4, Attack = 5,
                Knocked = 6, Carry = 7, Pressed = 8, Swallowed = 9, Dying = 10, Fading = 11,
                Absorb = 12, GrowUp = 13, Enter = 14, Exit = 15, Dead = 16, Plucker = 17;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Piki
        {
            public ulong creature;
            public Vec3 pos, vel;
            public float face, size;
            public uint id;
            public int captain, pellet;
            /// Out of piki_max_health (100 by default).
            public float health;
            public byte state, color, happa, alive, has_creature, on_ground, frozen, _p1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Pellet
        {
            public ulong creature;
            public Vec3 pos;
            public float yaw, radius, scale;
            public uint id;
            public int onion;
            public uint carriers, min_carriers, max_carriers;
            // 0 idle, 1 carried, 2 being sucked up, 3 gone.
            public byte state, has_creature, _p0, _p1;
            public uint _p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Onion
        {
            public Vec3 pos;
            public uint id, colors;
            public uint stored0, stored1, stored2;
            // 0 idle, 1 suction, 2 seeds, 3 takeoff, 4 away, 5 landing, 6 hover, 7 dormant, 8 reviving.
            public byte anim, is_ship, _p0, _p1;

            public uint Stored(uint color) => color == 0 ? stored0 : color == 1 ? stored1 : stored2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sprout
        {
            public Vec3 pos;
            public uint id;
            // state: 0 flying from the Onion, 1 buried, 2 seed showing, 3 pluckable, 4 wilted.
            public byte color, happa, state, _p0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OnionCounts
        {
            public uint in_onion, in_squad, on_field, max_on_field;
        }

        // Event kinds, in the order of libpikmin's Event enum (event_kind in lib.rs).
        public enum EventKind : uint
        {
            PikiEnteredOnion, PikiLeftOnion, OnionLanded, NectarSplash, NectarGone, PikiDrinking,
            Bloomed, Whistle, Recruited, Thrown, Landed, Latched, Hit, KnockedOff, PikiHurt,
            ShipPartCollected, CarrierGrabbed, Deflowered, PikiGone, PikiDied, Swallowed, SpitOut,
            CaptainHurt, CaptainDown, Dismissed, Punched, PelletLifted, PelletRerouted,
            PelletDropped, PelletArrived, PelletDelivered, SeedLaunched, SeedStored,
            SproutPlanted, SproutGrew, SproutWilted, Plucked,
            // b the Onion, a its owner, c how many.
            EmergencyPikmin,
            // a the plucker, b its Onion.
            PluckerOut,
            // a the plucked Pikmin, b the captain it joins (-1 none), c the plucker.
            PluckerPulled,
            // a the captain, c 1 in mid-air.
            CaptainJumped,
            // a the captain, pos where, value the drop (sim units).
            CaptainStomped,
            // a the Pikmin, b its captain, pos where.
            PikiStomped,
            // a the captain, value the fall (sim units), c 1 for a stomp's landing.
            CaptainLanded,
            // a the Pikmin, c the stage it grew to (0 leaf, 1 bud, 2 flower).
            PikiGrew,
            // a the captain.
            StrikeStarted,
        }

        // a: the Pikmin/pellet/sprout/captain it's about; b: a second id; c: a colour, leaf stage
        // or count; creature with has_creature; value: damage, health, radius or seeds.
        [StructLayout(LayoutKind.Sequential)]
        public struct Event
        {
            public ulong creature;
            public Vec3 pos;
            public float value;
            public EventKind kind;
            public uint a;
            public int b, c;
            public byte has_creature, _p0, _p1, _p2;
            public uint _p3;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sizes
        {
            public uint vec3, host, input, creature, part, pellet_desc, captain, piki, pellet,
                onion, sprout, evt, onion_counts, model_info, batch, texture, draw, view_frame, particle;
        }

        // ---- The view (pik_view_*): the disc's models posed each frame, and its sounds ----

        // Models (libpikmin_present::view::models).
        public const uint ModelOlimar = 0, ModelOnion = 7, ModelPikihead = 12, ModelCursor = 13, ModelMarker = 14;

        // Draw kinds (libpikmin_present::view::kinds).
        public const byte KindCaptain = 0, KindPiki = 1, KindHappa = 2, KindOnion = 3, KindPellet = 4,
            KindSprout = 5, KindSproutHappa = 6, KindCursor = 7, KindMarker = 8, KindShip = 9, KindBeam = 10, KindShipPart = 11;

        // Models' info, batches, textures and draws are ror2-common's ViewModelInfo, ViewBatch
        // (blends: ViewBlend; libpikmin_present::view::blend's additive is the Onions' light cones), ViewTexture and
        // ViewDraw.

        /// A camera-facing quad: corners top-left, top-right, bottom-right, bottom-left (texture
        /// (0,0) (1,0) (1,1) (0,1)), drawn as texture x colour.
        [StructLayout(LayoutKind.Sequential)]
        public struct Particle
        {
            public Vec3 c0, c1, c2, c3;
            public float r, g, b, a;
            public uint texture;
            /// 1: added to the screen; 0: alpha blended.
            public byte additive, _p0, _p1, _p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ViewFrame
        {
            public float dt;
            public Vec3 eye, forward, up;
            public int listener;
            public float volume;
            /// Ticks behind the newest sim state to draw (0..1), matching the body's blend.
            public float lag;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_new(byte[] assetsDir, byte sound);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_free(IntPtr view);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_status(IntPtr view);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_model_count(IntPtr view);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_view_model(IntPtr view, uint model, out ViewModelInfo info);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_batches(IntPtr view, uint model, [Out] ViewBatch[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_indices(IntPtr view, uint model, [Out] int[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_uvs(IntPtr view, uint model, [Out] float[] outPairs, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_view_texture(IntPtr view, uint id, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_update(IntPtr view, IntPtr sim, ref ViewFrame frame);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_show_cursors(IntPtr view, byte show);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_materialize(IntPtr view, uint captain, float progress);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_repaired_ship(byte on);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_captain_pikmin_jump(byte on);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_trumpet_gain(IntPtr view, float gain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_draws(IntPtr view, [Out] ViewDraw[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_positions(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_normals(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_colors(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_texture_ids(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_particles(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_view_frame_uvs(IntPtr view, out uint count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_view_particle_texture(IntPtr view, uint id, out ViewTexture info, [Out] byte[] rgba, uint len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate byte GroundFn(IntPtr user, ref Vec3 pos, out float height);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WallsFn(IntPtr user, ref Vec3 centre, float radius, out Vec3 pushed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate byte DamageFn(IntPtr user, ulong creature, float amount, int part, uint source, uint sourceId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_abi_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_tick_hz();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_struct_sizes(out Sizes sizes);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_assets_complete(byte[] dir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_number_texture(byte[] dir, uint digit, byte shadow, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_find_disc(byte[] dirs, byte[] outPath, uint outLen);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_extract(byte[] disc, byte[] outDir, byte[] msg, uint msgLen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_sim_new(byte[] assetsDir, ulong seed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_free(IntPtr sim);
        /// The sim's entities and events since the last call, encoded for another machine's replica; the
        /// length written, or 0 if `cap` was too small.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_snapshot(IntPtr sim, [Out] byte[] output, uint cap);
        /// A sim that only holds what the server's snapshots say (it is never stepped).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_replica_new(byte[] assetsDir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_replica_apply(IntPtr sim, byte[] bytes, uint len);
        /// Blends a replica toward its newest snapshot; once a frame, before pik_view_update.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_replica_update(IntPtr sim, float dt);
        /// On a replica: shows the server's `captain` moving as `fromCaptain` of `mover` does (a client's own
        /// Olimar, so he responds at once); `pos` (smoothed feet, or null) and `face` override the mover's.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_replica_overlay_captain(IntPtr replica, uint captain, IntPtr mover, uint fromCaptain, ref Vec3 pos, float face);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_replica_overlay_captain(IntPtr replica, uint captain, IntPtr mover, uint fromCaptain, IntPtr pos, float face);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_sim_params_source(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_poisoned(IntPtr sim);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_captain(IntPtr sim, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_input(IntPtr sim, uint captain, ref Input input);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_piki(IntPtr sim, uint color, ref Vec3 pos, int captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_onion(IntPtr sim, ref Vec3 pos, uint colors);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_ship(IntPtr sim, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_onion_pose(IntPtr sim, uint onion, ref Vec3 pos, byte flying);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_make_onion_dormant(IntPtr sim, uint onion, float seconds, ref Vec3 ground);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_collect_ground_pellet(IntPtr sim, uint onion, uint pellet);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_onion_flight_motion(IntPtr sim, uint onion, byte landing, float progress);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_onion_color(IntPtr sim, uint onion, uint color);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_pellet(IntPtr sim, ref PelletDesc desc);
        /// A 1, 5, 10 or 20 pellet (the largest not above `number`) in colour `color` (0-2), with
        /// the disc's carriers, seeds and size.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_numbered_pellet(IntPtr sim, uint number, uint color, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_plant_sprout(IntPtr sim, uint color, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_creatures(IntPtr sim, [In] Creature[] creatures, uint n, [In] Part[] parts, uint m);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_advance(IntPtr sim, ref Host host, float dt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_sim_alpha(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong pik_sim_tick(IntPtr sim);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_shake_off(IntPtr sim, ulong creature);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_captain_throw_color(IntPtr sim, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_auto_rejoin(IntPtr sim, byte on);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_shake_protection(IntPtr sim, float seconds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_reach(IntPtr sim, uint captain, float scale);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_speed(IntPtr sim, uint captain, float scale);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_attack_pikis_in_sphere(IntPtr sim, ref Vec3 centre, float radius, float damage);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_freeze_pikis_in_sphere(IntPtr sim, ref Vec3 centre, float radius, float seconds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_kill_piki(IntPtr sim, uint piki);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_onion_in_reach(IntPtr sim, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_onion_counts(IntPtr sim, uint onion, uint captain, uint color, out OnionCounts counts);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onion_put_away(IntPtr sim, uint captain, uint onion, uint color, uint n);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onion_call_out(IntPtr sim, uint captain, uint onion, uint color, uint n);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_store_in_onion(IntPtr sim, uint onion, uint color, uint happa, uint n);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_captain(IntPtr sim, uint captain, out Captain state);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_pikis(IntPtr sim, [Out] Piki[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_pellets(IntPtr sim, [Out] Pellet[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onions(IntPtr sim, [Out] Onion[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_sprouts(IntPtr sim, [Out] Sprout[] outArr, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_events(IntPtr sim, [Out] Event[] outArr, uint max);

        /// Loads pikmin.dll from the plugin folder up front, so the [DllImport]s above bind to it
        /// whatever Mono's search path is, then checks it speaks this plugin's ABI.
        public static bool Load(string path, out string error)
        {
            if (!NativeLib.Load(path, out error)) return false;
            uint v = pik_abi_version();
            if (v != AbiVersion)
            {
                error = $"{path} speaks ABI {v}, this plugin wants {AbiVersion}; rebuild both";
                return false;
            }
            pik_struct_sizes(out var s);
            var mine = new Sizes
            {
                vec3 = Size<Vec3>(), host = Size<Host>(), input = Size<Input>(),
                creature = Size<Creature>(), part = Size<Part>(), pellet_desc = Size<PelletDesc>(),
                captain = Size<Captain>(), piki = Size<Piki>(), pellet = Size<Pellet>(),
                onion = Size<Onion>(), sprout = Size<Sprout>(), evt = Size<Event>(),
                onion_counts = Size<OnionCounts>(), model_info = Size<ViewModelInfo>(), batch = Size<ViewBatch>(),
                texture = Size<ViewTexture>(), draw = Size<ViewDraw>(), view_frame = Size<ViewFrame>(),
                particle = Size<Particle>(),
            };
            if (!s.Equals(mine))
            {
                error = $"{path}'s struct sizes differ from this plugin's (native {Describe(s)}, plugin {Describe(mine)}); rebuild both";
                return false;
            }
            error = null;
            return true;
        }

        static uint Size<T>() => (uint)Marshal.SizeOf(typeof(T));

        static string Describe(Sizes s) =>
            $"{s.vec3}/{s.host}/{s.input}/{s.creature}/{s.part}/{s.pellet_desc}/{s.captain}/" +
            $"{s.piki}/{s.pellet}/{s.onion}/{s.sprout}/{s.evt}/{s.onion_counts}/" +
            $"{s.model_info}/{s.batch}/{s.texture}/{s.draw}/{s.view_frame}/{s.particle}";

        public static string Utf8(IntPtr p) => NativeLib.Utf8(p);

        public static string Utf8(byte[] buf)
        {
            int n = Array.IndexOf(buf, (byte)0);
            return Encoding.UTF8.GetString(buf, 0, n < 0 ? buf.Length : n);
        }

        public static byte[] CString(string s) => NativeLib.CString(s);
    }

    // pik_view_* as ror2-common's IViewSource.
    internal sealed class PikViewSource : IViewSource
    {
        public readonly IntPtr Handle;

        public PikViewSource(IntPtr view) { Handle = view; }

        public uint ModelCount() => Native.pik_view_model_count(Handle);
        public bool Model(uint model, out ViewModelInfo info) => Native.pik_view_model(Handle, model, out info) != 0;
        public uint Batches(uint model, ViewBatch[] output, uint max) => Native.pik_view_batches(Handle, model, output, max);
        public uint Indices(uint model, int[] output, uint max) => Native.pik_view_indices(Handle, model, output, max);
        public uint Uvs(uint model, float[] output, uint max) => Native.pik_view_uvs(Handle, model, output, max);
        public bool Texture(uint id, out ViewTexture info, byte[] rgba, uint len) => Native.pik_view_texture(Handle, id, out info, rgba, len) != 0;
        public uint Draws(ViewDraw[] output, uint max) => Native.pik_view_draws(Handle, output, max);
        public IntPtr Positions(out uint count) => Native.pik_view_positions(Handle, out count);
        public IntPtr Normals(out uint count) => Native.pik_view_normals(Handle, out count);
        public IntPtr Colors(out uint count) => Native.pik_view_colors(Handle, out count);
        public IntPtr TextureIds(out uint count) => Native.pik_view_texture_ids(Handle, out count);
        public IntPtr FrameUvs(out uint count) => Native.pik_view_frame_uvs(Handle, out count);
    }
}
