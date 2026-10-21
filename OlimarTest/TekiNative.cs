using System;
using System.Runtime.InteropServices;

namespace OlimarTest
{
    // Pikmin's enemies ("teki") in libpikmin: what the disc says about each kind, and the instances
    // the view draws (libpikmin-ffi view.rs PikTekiInfo / PikTekiInstance). The sim has no enemies;
    // RoR2's monster bodies are the creatures and these only dress them.
    internal static class TekiNative
    {
        public const byte KindTeki = 12;
        public const int InfoSize = 404, InstanceSize = 44, Slots = 10;

        // Indices into Info.motions (libpikmin-present teki::slot).
        public const int Wait = 0, Turn = 1, Chase = 2, Attack = 3, Gulp = 4, Flick = 5, Dead = 6, Hurt = 7, Sleep = 8, Cry = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct Motion
        {
            public uint index;
            public float start, end, loopStart, loopEnd, action0, action1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Info
        {
            public uint kind, model;
            public float life, scale, weight, runSpeed, turnSpeed, visibleRange, visibleAngle, attackableRange,
                attackableAngle, attackRange, attackPower, collisionRadius, corpseSize, corpseHeight;
            public uint corpseMinCarriers, corpseMaxCarriers;
            public int corpseSeedsMatching, corpseSeedsOther;
            public uint swallowCount;
            public int carcassSize;
            public Native.Vec3 corpseOffset, lo, hi;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = Slots)] public Motion[] motions;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Instance
        {
            public uint id, kind;
            public Native.Vec3 pos;
            public float yaw;
            public uint motion;
            public float frame, flash, scale, alpha;
        }

        [DllImport(Native.Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_teki_models(byte on);
        [DllImport(Native.Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void pik_view_set_tekis(IntPtr view, Instance[] list, uint count);
        [DllImport(Native.Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_view_spawn_effect(IntPtr view, uint id, ref Native.Vec3 pos);
        [DllImport(Native.Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte pik_teki_info(byte[] assets, uint kind, ref Info output);
    }

    // The two kinds the plugin spawns, read once from the extracted assets.
    internal static class TekiCatalog
    {
        public const byte Dwarf = 0, Spotty = 1, Honeywisp = 18, Breadbug = 19;
        public static readonly byte Kinds = (byte)TekiRoster.All.Length;
        static readonly TekiNative.Info[] infos = new TekiNative.Info[TekiRoster.All.Length];
        static readonly bool[] have = new bool[TekiRoster.All.Length];
        static bool tried;

        /// Whether the files for at least one kind loaded. Reads them the first time the assets are ready.
        public static bool Ready
        {
            get
            {
                if (!tried && Plugin.AssetsReady) Load();
                foreach (bool h in have) if (h) return true;
                return false;
            }
        }

        public static bool Has(byte kind) => Ready && kind < Kinds && have[kind];
        public static TekiNative.Info Get(byte kind) => infos[kind];

        static void Load()
        {
            tried = true;
            if (Marshal.SizeOf(typeof(TekiNative.Info)) != TekiNative.InfoSize)
            {
                Plugin.Log.LogError("Pikmin enemies: TekiNative.Info doesn't match pikmin.dll's struct; they are off.");
                return;
            }
            var dir = Native.CString(Plugin.AssetsDir);
            for (byte k = 0; k < Kinds; k++)
            {
                var info = new TekiNative.Info { motions = new TekiNative.Motion[TekiNative.Slots] };
                if (TekiNative.pik_teki_info(dir, k, ref info) == 0)
                {
                    Plugin.Log.LogWarning($"Pikmin enemy {TekiRoster.All[k].Display}: its files aren't in the extracted assets (tekis/, tekipara/); it won't spawn.");
                    continue;
                }
                infos[k] = info;
                have[k] = true;
            }
        }
    }
}
