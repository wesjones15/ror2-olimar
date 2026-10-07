using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OlimarTest
{
    // The libpikmin C ABI (libpikmin/crates/libpikmin-ffi/src/lib.rs, PIK_ABI_VERSION 1). Every
    // struct mirrors a #[repr(C)] one there field for field, and Load() refuses a pikmin.dll
    // whose sizes differ. Sim units: the decomp's (roughly cm), Y up; a facing angle a (radians)
    // points along (sin a, 0, cos a), the same as a Unity yaw.
    internal static class Native
    {
        public const string Dll = "pikmin";
        public const uint AbiVersion = 1;

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

        public const uint SourcePiki = 0, SourceCaptain = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public float stick_x, stick_y;
            public float swarm_x, swarm_y;
            public Vec3 pose_pos, pose_vel;
            public float pose_face;
            public Vec3 cursor;
            public byte whistle, throw_, dismiss, use_pose, use_cursor, _p0, _p1, _p2;
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
                Absorb = 12, GrowUp = 13, Enter = 14, Exit = 15, Dead = 16;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Piki
        {
            public ulong creature;
            public Vec3 pos, vel;
            public float face, size;
            public uint id;
            public int captain, pellet;
            public byte state, color, happa, alive, has_creature, on_ground, _p0, _p1;
            public uint _p2;
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
            // 0 idle, 1 suction, 2 emitting seeds, 3 taking off, 4 away, 5 landing.
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
                onion, sprout, evt, onion_counts;
        }

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
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_find_disc(byte[] dirs, byte[] outPath, uint outLen);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_extract(byte[] disc, byte[] outDir, byte[] msg, uint msgLen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_sim_new(byte[] assetsDir, ulong seed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_free(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_sim_params_source(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_sim_poisoned(IntPtr sim);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_captain(IntPtr sim, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_input(IntPtr sim, uint captain, ref Input input);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_piki(IntPtr sim, uint color, ref Vec3 pos, int captain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_add_onion(IntPtr sim, ref Vec3 pos, uint colors);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_spawn_pellet(IntPtr sim, ref PelletDesc desc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_sim_plant_sprout(IntPtr sim, uint color, ref Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_set_creatures(IntPtr sim, [In] Creature[] creatures, uint n, [In] Part[] parts, uint m);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_advance(IntPtr sim, ref Host host, float dt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float pik_sim_alpha(IntPtr sim);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong pik_sim_tick(IntPtr sim);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_sim_shake_off(IntPtr sim, ulong creature);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_sim_attack_pikis_in_sphere(IntPtr sim, ref Vec3 centre, float radius, float damage);
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

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        /// Loads pikmin.dll from the plugin folder up front, so the [DllImport]s above bind to it
        /// whatever Mono's search path is, then checks it speaks this plugin's ABI.
        public static bool Load(string path, out string error)
        {
            if (LoadLibraryW(path) == IntPtr.Zero)
            {
                error = $"LoadLibrary({path}) failed: Win32 error {Marshal.GetLastWin32Error()}";
                return false;
            }
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
                onion_counts = Size<OnionCounts>(),
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
            $"{s.piki}/{s.pellet}/{s.onion}/{s.sprout}/{s.evt}/{s.onion_counts}";

        public static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n];
            Marshal.Copy(p, bytes, 0, n);
            return Encoding.UTF8.GetString(bytes);
        }

        public static string Utf8(byte[] buf)
        {
            int n = Array.IndexOf(buf, (byte)0);
            return Encoding.UTF8.GetString(buf, 0, n < 0 ? buf.Length : n);
        }

        public static byte[] CString(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            Array.Resize(ref b, b.Length + 1);
            return b;
        }
    }
}
