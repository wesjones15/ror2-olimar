using System;
using System.Runtime.InteropServices;
using System.Text;
using Ror2Common;

namespace OlimarTest
{
    // The libpikmin C ABI (libpikmin/crates/libpikmin-ffi/src/lib.rs and view.rs). Every
    // struct mirrors a #[repr(C)] one there field for field, and Load() refuses a pikmin.dll
    // whose sizes differ. The view's model structs are ror2-common's (ViewSource.cs), which
    // libsms's view shares. Sim units: the decomp's (roughly cm), Y up; a facing angle a (radians)
    // points along (sin a, 0, cos a), the same as a Unity yaw.
    internal static class Native
    {
        public const string Dll = "pikmin";
        public const uint AbiVersion = 52;
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_music_new(byte[] assets);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_music_update(IntPtr music, uint song, byte nearbyEnemy, float volume, byte paused);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_music_menu_sound(IntPtr music, uint cue);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_music_free(IntPtr music);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_part_competitive_delivery(IntPtr sim, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_part_owner(IntPtr sim, uint ship, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_ship_repair_mode(IntPtr view, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_onion_ground(IntPtr view, uint onion, float height);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_onion_menu_beam(IntPtr view, uint onion, byte active);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_part_host_delivery(IntPtr sim, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_victory_pcm(byte[] assets, float[] output, uint cap, out uint rate);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_celebrating(IntPtr view, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_repairs_pending(IntPtr view);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_part_onion_delivery(IntPtr sim, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_ship_part_weight(IntPtr sim, uint part);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_ship_part_name(IntPtr sim, uint part, byte[] output, uint cap);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_ship_part(IntPtr sim, uint part, ref Vec3 pos, float yaw);
        /// Pikmin 2's treasures (the Pikmin 2 cache beside the assets): how many, each one's carriers, Pokos and English name,
        /// and one spawned as a pellet a ship receives like a part (its receipt's part id is 1000 + the index).
        /// Whether the Pikmin 2 cache folder holds the treasure files, and extracting them from the user's Pikmin 2 image (0 on success).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_p2_treasures_complete(byte[] dir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_p2_extract_treasures(byte[] iso, byte[] outDir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_treasure_count(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_treasure_weight(IntPtr sim, uint index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_treasure_value(IntPtr sim, uint index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_treasure_name(IntPtr sim, uint index, byte[] output, uint cap);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_treasure(IntPtr sim, uint index, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_recover_pellet(IntPtr sim, uint pellet, ref Vec3 pos);
        // A guest character's (Mario's) hands: hold or move a part, put it down, put it into a ship's receiver.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_hold_pellet(IntPtr sim, uint pellet, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_release_pellet(IntPtr sim, uint pellet, ref Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_hand_ship_part(IntPtr sim, uint ship, uint pellet);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ship_hand_owned(IntPtr sim, uint ship);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_render_stings(byte[] dir, [Out] byte[] message, uint length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_take_death_sting(IntPtr view, [Out] float[] samples, uint max);
        // Item hooks (libpikmin's items.rs, plucker.rs and jump.rs).
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_attack_rate(IntPtr sim, uint captain, float rate);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_motion(IntPtr sim, uint captain, float jump, float accel);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_captain_jump_height(IntPtr sim, uint captain, float height);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_sim_sacrifice_onion(IntPtr sim, uint onion, float missing, out uint killed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_healing(IntPtr view, uint captain, float progress, uint ghosts);
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
        /// How many living Pikmin of each colour (blue, red, yellow) attacked the creature; ask before pik_sim_credit_kill.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_attackers(IntPtr sim, ulong creature, [Out] uint[] byColour);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_auto_carry(IntPtr sim, float range);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_give_onion_seeds(IntPtr sim, uint onion, uint color, uint seeds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_auto_pluck(IntPtr sim, float seconds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_trigger_plucker(IntPtr sim, uint onion);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_recall_stuck_carriers(IntPtr sim, uint captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_adopt_orphans(IntPtr sim, uint from, uint to);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_send_orphans_home(IntPtr sim, uint from, uint onion);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_onion_dormancy_seconds(IntPtr sim, uint onion, float seconds);
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_lock_pellet(IntPtr sim, uint captain, uint pellet, byte enabled);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_camera_preset(uint zoom, byte top, out Vec3 settings);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_camera_pan(uint mode, ref Vec3 angles, float x, float y, float dt, float minPitch);

        // Colour indices (PikiColor::index, the decomp's order).
        public const uint Blue = 0, Red = 1, Yellow = 2, Purple = 3, White = 4;
        /// How many colours there are (Purple and White are Pikmin 2's, in Louie's squad).
        public const int ColourCount = 5;

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
            public uint id, generation;
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
            // 0 idle, 1 carried, 2 being sucked up, 3 gone, 4 held by a guest character.
            public byte state, has_creature, has_ship_part;
            /// The sim's colour for a numbered pellet (0 blue, 1 red, 2 yellow), or 255.
            public byte color;
            public uint _p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Onion
        {
            public Vec3 pos;
            public uint id, colors;
            public uint stored0, stored1, stored2, stored3, stored4;
            // 0 idle, 1 suction, 2 seeds, 3 takeoff, 4 away, 5 landing, 6 hover, 7 dormant, 8 reviving.
            public byte anim, is_ship, _p0, _p1;

            public uint Stored(uint color) => color == 0 ? stored0 : color == 1 ? stored1 : color == 2 ? stored2 : color == 3 ? stored3 : color == 4 ? stored4 : 0;
            public uint StoredTotal => stored0 + stored1 + stored2 + stored3 + stored4;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sprout
        {
            public Vec3 pos;
            public uint id;
            // state: 0 flying from the Onion, 1 buried, 2 seed showing, 3 pluckable, 4 wilted.
            public byte color, happa, state, _p0;
        }

        // A Candypop Bud (libpikmin's PikBulb, 36 bytes). state: 0 opening, 1 open, 2 closing, 3 spitting, 4 withering.
        [StructLayout(LayoutKind.Sequential)]
        public struct Bulb
        {
            public Vec3 pos;
            public uint id, colour, queen, state;
            public float t;
            public uint free;
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
            // a the Pikmin, b its captain: it took off to repeat his jump-pad throw.
            PikiLaunched,
            // a the Purple Pikmin, pos where it came down.
            Pounded,
            // a the Purple Pikmin that pounded, creature the flying creature it brought down.
            Grounded,
            // a the White Pikmin that was eaten, creature what ate it.
            PoisonedEater,
            // a the Pikmin that went into a Candypop Bud, b the bud, c its colour, pos the bud.
            BulbSwallowed,
            // a the bud, c the colour it spat, b how many sprouts, pos where they leave.
            BulbShot,
            // a the heaviest Pikmin on a flying creature, b the weight (a Purple counts five), creature the flier.
            WeighedDown,
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_view_texture_version(IntPtr view, uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_update(IntPtr view, IntPtr sim, ref ViewFrame frame);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_show_cursors(IntPtr view, byte show);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_play_sound(IntPtr view, byte sound, Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_paused(IntPtr view, byte paused);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_pose_budget(IntPtr view, ref Vec3 eye, float far);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_materialize(IntPtr view, uint captain, float progress);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_repaired_ship(byte on);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_captain_pikmin_jump(byte on);
        /// The Sunshine Pianta an Olimar can wear (skin 2), appended to the model table: call before the
        /// table is read. The model's index, or -1 when the Sunshine files are missing.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_pianta(IntPtr view, byte[] sunshineDir, byte female, uint colour);
        /// Which body a captain wears: 0 Olimar, 2 the Pianta.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_captain_skin(IntPtr view, uint captain, byte skin);
        /// Pikachu and the Ice Climbers (skin 3) from the Melee cache, four models appended to the table; Pikachu's
        /// model index or -1.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_pikachu(IntPtr view, byte[] meleeDir);
        /// Pikachu's height and an Ice Climber's, as multiples of Olimar's.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_pikachu_ratios(IntPtr view, float captain, float climber);
        /// Whether the Melee cache holds the Ice Climbers' and Pikachu's models.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_ssbm_ice_climbers_complete(byte[] dir);
        /// Extracts those models from the user's Melee image into the Melee cache; 0 on success.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_ssbm_extract_ice_climbers(byte[] iso, byte[] outDir);
        /// A Melee image in a ';'-separated list of folders; 0 when none.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_ssbm_find_disc(byte[] dirs, byte[] found, uint foundLen);
        /// Louie (skin 1) from the Pikmin 2 cache, appended to the model table; his model index or -1.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_louie(IntPtr view, byte[] pikmin2Dir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_pikmin2(IntPtr view, byte[] pikmin2Dir);
        /// Pikmin 2's 188 treasures from the Pikmin 2 cache, appended to the model table (treasure pellets are then drawn with
        /// them, sparkle and sound); the first model's index or -1.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_treasures(IntPtr view, byte[] pikmin2Dir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_ship_hocotate(IntPtr view, uint onion, byte on);
        /// Louie's height as a multiple of Olimar's.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_louie_height(IntPtr view, float ratio);
        /// Whether the Pikmin 2 cache folder is complete.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_p2_complete(byte[] dir);
        /// Extracts Louie's files from the user's Pikmin 2 image into a folder; 0 on success.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_p2_extract(byte[] iso, byte[] outDir);
        /// Whether the Pikmin 2 cache folder holds the enemies' files, and extracting them from the user's image; 0 on success.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_p2_enemies_complete(byte[] dir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_p2_enemies_extract(byte[] iso, byte[] outDir);
        /// A Pikmin 2 image in a ';'-separated list of folders; 0 when none.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_p2_find_disc(byte[] dirs, byte[] found, uint foundLen);
        /// The Pianta's height to the top of his head, sim units.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_pianta_height(IntPtr view, float units);
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_number_change_scale(int direction, float elapsed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_damage_suffix_texture(byte[] dir, uint glyph, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_carry_digit_texture(byte[] dir, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_hud_icon_texture(byte[] dir, uint icon, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_font_glyph_texture(byte[] dir, uint character, out ViewTexture info, [Out] byte[] rgba, uint len);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_find_disc(byte[] dirs, byte[] outPath, uint outLen);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_extract(byte[] disc, byte[] outDir, byte[] msg, uint msgLen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_sim_new(byte[] assetsDir, ulong seed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_field_limit(IntPtr sim, uint limit);
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_piki_happa(IntPtr sim, uint piki, uint happa);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onion_stock(IntPtr sim, uint onion, uint color, uint happa);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_finish_orphan_recall(IntPtr sim, uint captain, uint onion);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onion_returning_count(IntPtr sim, uint onion);
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_networked(IntPtr sim, byte enabled);
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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_onion_goal(IntPtr sim, uint id, out Vec3 goal);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_sprouts(IntPtr sim, [Out] Sprout[] outArr, uint max);
        // A lock-on "thing" id at or above this is a Candypop Bud (its Bulb.id plus this); below it a pellet.
        public const uint BulbLockBase = 0x40000000;
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_bulb_slots(IntPtr sim, uint normal, uint queen);
        // Candypop Buds: colour 0-4 (Blue, Red, Yellow, Purple, White); queen makes the rainbow Pop.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_bulb(IntPtr sim, uint colour, uint queen, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_remove_bulb(IntPtr sim, uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_bulbs(IntPtr sim, [Out] Bulb[] outArr, uint max);
        // How much weight of Pikmin brings a flying creature down (0 takes the rule away); see pik_weigh_need for the numbers.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_ground_need(IntPtr sim, ulong creature, uint need);
        // The weight for a class: 0 small flier, 1 large, 2 boss, 3 projectile.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_weigh_need(uint kind);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_view_load_bulbs(IntPtr view, byte[] pikmin2Dir);
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
    internal sealed class PikViewSource : IViewSource, IAnimatedTextureSource
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
        public uint TextureVersion(uint id) => Native.pik_view_texture_version(Handle, id);
    }
}
