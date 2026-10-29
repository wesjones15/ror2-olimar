using System;
using System.Runtime.InteropServices;

namespace OlimarTest
{
    // Pikmin's enemies with their own AI (libpikmin-ffi teki_ai.rs): the strategies ported from the decomp run
    // in the native library; RoR2's bodies carry their health and hurt boxes and follow where they go.
    internal static class TekiAiNative
    {
        public const int NaviSize = 24, StateSize = 60, OutSize = 72;

        [StructLayout(LayoutKind.Sequential)]
        public struct Navi
        {
            public uint id;
            public Native.Vec3 pos;
            public float size;
            /// 1 while the player is buried (a Mamuta's punch).
            public byte alive, buried, p1, p2;
        }

        // flags: bit 0 visible, 1 alive, 2 flying, 3 collidable, 4 holding a pellet, 5 off the ground.
        [StructLayout(LayoutKind.Sequential)]
        public struct State
        {
            public uint id, kind;
            public Native.Vec3 pos;
            public float yaw;
            public uint motion;
            public float frame, flash, shake, fade;
            public int state;
            public uint flags;
            public int heldPellet;
            public float hp;

            public bool Visible => (flags & 1) != 0;
            public bool Alive => (flags & 2) != 0;
            public bool Flying => (flags & 4) != 0;
            public bool Collidable => (flags & 8) != 0;
            /// Flying, jumping or falling: its body follows it up and down.
            public bool Airborne => (flags & 32) != 0;
        }

        // A Long Legs boss's legs (teki_ai.rs PikLegs, 100 bytes): the body is drawn at `trace` facing `face` with its legs folded onto `feet`.
        // flags: bit 0 the legs stand on their own feet, 1 the clip is blended with them, 2 `gun` is where its gun points.
        public const int LegsSize = 100;

        [StructLayout(LayoutKind.Sequential)]
        public struct Legs
        {
            public uint id, flags;
            public Native.Vec3 trace;
            public float face;
            public Native.Vec3 foot0, foot1, foot2, foot3;
            public float bend0, bend1, bend2, bend3;
            public Native.Vec3 gun;
        }

        // kind: 1 effect, 2 sound (text), 3 spawn, 4 hit a player (y < 0: a burn), 5 finished, 6 began dying, 7 shake,
        // 8 pellets (a = the number's index 0..3, b = how many, y = the color), 9 nectar, 10 an effect along vel.
        [StructLayout(LayoutKind.Sequential)]
        public struct Out
        {
            public uint kind, teki, a;
            public Native.Vec3 pos;
            public float x, y;
            public Native.Vec3 vel;
            public uint b;
            public ulong t0, t1, t2;

            public string Text()
            {
                var bytes = new byte[24];
                BitConverter.GetBytes(t0).CopyTo(bytes, 0);
                BitConverter.GetBytes(t1).CopyTo(bytes, 8);
                BitConverter.GetBytes(t2).CopyTo(bytes, 16);
                int n = Array.IndexOf(bytes, (byte)0);
                return System.Text.Encoding.ASCII.GetString(bytes, 0, n < 0 ? bytes.Length : n);
            }
        }

        const string Dll = Native.Dll;
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pik_tekis_new(byte[] assets, ulong seed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_free(IntPtr tekis);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_sizes([Out] uint[] sizes);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_tekis_has_strategy(IntPtr tekis, uint kind);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int pik_tekis_spawn(IntPtr tekis, uint kind, ulong creature, ref Native.Vec3 pos, float yaw);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_remove(IntPtr tekis, uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_damage(IntPtr tekis, uint id, float amount, int attackerNavi);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_position(IntPtr tekis, uint id, ref Native.Vec3 pos);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_territory(IntPtr tekis, uint id, float territory, ref Native.Vec3 target);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_press(IntPtr tekis, uint id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_ground(IntPtr tekis, uint id, float ground, byte water);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_goal(IntPtr tekis, uint id, ref Native.Vec3 goal);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_count(IntPtr tekis, uint id, uint index, int value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_tekis_set_spawn_velocity(IntPtr tekis, uint id, ref Native.Vec3 velocity);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_tekis_update(IntPtr tekis, IntPtr sim, Navi[] navis, uint count, uint ticks);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_tekis_outs(IntPtr tekis, [Out] Out[] outs, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_tekis_state(IntPtr tekis, uint id, out State state);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint pik_legs_size();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_tekis_legs(IntPtr tekis, uint id, out Legs legs);
    }
}
