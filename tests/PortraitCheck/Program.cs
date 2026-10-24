using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OlimarTest;
using Ror2Common;

static class Program
{
    static readonly Native.GroundFn ground = (IntPtr user, ref Native.Vec3 pos, out float height) => { height = 0; return 1; };
    static Native.Host floor = new Native.Host { ground_height = Marshal.GetFunctionPointerForDelegate(ground) };
    static string assets;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Rig : IDisposable
    {
        public IntPtr sim, view;
        public Rig()
        {
            sim = Native.pik_sim_new(Native.CString(assets), 0x5eed);
            var pos = new Native.Vec3(); Check(Native.pik_sim_add_captain(sim, ref pos) == 0, "captain didn't spawn");
            view = Native.pik_view_new(Native.CString(assets), 0);
            Check(sim != IntPtr.Zero && view != IntPtr.Zero, "portrait didn't load");
            Native.pik_view_show_cursors(view, 0);
        }
        public void Input(byte throwing = 0, byte whistle = 0, byte dismiss = 0)
        {
            var input = new Native.Input { throw_ = throwing, whistle = whistle, dismiss = dismiss,
                cursor = new Native.Vec3(0, 0, 45), use_cursor = 1 };
            Native.pik_sim_set_input(sim, 0, ref input);
        }
        public void Step(int ticks, Action<int> before = null, Action<int> after = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                before?.Invoke(i); Native.pik_sim_advance(sim, ref floor, 1f / 30f); after?.Invoke(i);
                var frame = new Native.ViewFrame { dt = 1f / 30f, forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0) };
                Native.pik_view_update(view, sim, ref frame);
            }
        }
        public ViewDraw[] Draws()
        { var draws = new ViewDraw[256]; uint n = Native.pik_view_draws(view, draws, 256); return draws.Take((int)n).ToArray(); }
        public void Dispose() { Native.pik_view_free(view); Native.pik_sim_free(sim); }
    }
    static void Main(string[] args)
    {
        assets = Path.GetFullPath(args[1]);
        if (!Directory.Exists(assets)) { Console.WriteLine("SKIP: native Pikmin disc cache absent"); return; }
        var handle = NativeLibrary.Load(Path.GetFullPath(args[0]));
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, search) => name == Native.Dll ? handle : IntPtr.Zero);
        Check(Native.Load(args[0], out var error), error);
        using (var r = new Rig())
        {
            var pos = new Native.Vec3(); Native.pik_sim_plant_sprout(r.sim, Native.Red, ref pos); r.Step(16);
            Check(r.Draws().Any(d => d.kind == Native.KindSprout) && r.Draws().Any(d => d.kind == Native.KindSproutHappa), "sprout and leaf must both draw");
        }
        using (var r = new Rig())
        {
            var pos = new Native.Vec3(0, 0, 12); Native.pik_sim_spawn_piki(r.sim, Native.Red, ref pos, 0);
            r.Step(13, i => r.Input(throwing: (byte)(i == 5 ? 1 : 0)));
            var pikis = new Native.Piki[16]; uint n = Native.pik_sim_pikis(r.sim, pikis, 16);
            Check(pikis.Take((int)n).Any(p => p.state == Native.PikiState.Flying), "throw frame must be airborne");
            Check(r.Draws().Any(d => d.kind == Native.KindPiki), "thrown Pikmin has no draw");
        }
        using (var r = new Rig())
        {
            r.Input(whistle: 1); r.Step(16);
            Native.pik_view_particles(r.view, out var count); Check(count > 0, "whistle has no native VFX");
        }
        using (var r = new Rig())
        {
            var pos = new Native.Vec3(0, 100, 0);
            int onion = Native.pik_sim_add_onion(r.sim, ref pos, 1u << (int)Native.Red);
            Native.pik_sim_set_onion_pose(r.sim, (uint)onion, ref pos, 1);
            Native.pik_sim_set_onion_flight_motion(r.sim, (uint)onion, 0, 0);
            r.Step(30, null, i => Native.pik_sim_set_onion_flight_motion(r.sim, (uint)onion, 0, i / 60f));
            Check(r.Draws().Any(d => d.kind == Native.KindOnion), "flying Onion has no draw");
            Native.pik_view_particles(r.view, out var count); Check(count > 0, "flying Onion has no native VFX");
        }
        using (var r = new Rig())
        {
            for (int i = 0; i < 12; i++)
            {
                var pos = new Native.Vec3((i % 4) * 10 - 15, 0, -38 - (i / 4) * 9);
                Native.pik_sim_spawn_piki(r.sim, new[] { Native.Red, Native.Yellow, Native.Blue }[i % 3], ref pos, 0);
            }
            var target = new Native.Vec3(150, 0, 150);
            Native.pik_sim_set_creatures(r.sim, new[] { new Native.Creature { id = 1, pos = target, alive = 1, parts_len = 1 } }, 1,
                new[] { new Native.Part { centre = target, radius = 20, stickable = 1 } }, 1);
            Native.pik_sim_set_dismiss_strike(r.sim, 1, 150, 0, 1);
            r.Step(10, i => r.Input(dismiss: (byte)(i == 2 ? 1 : 0)));
            var pikis = new Native.Piki[16]; uint n = Native.pik_sim_pikis(r.sim, pikis, 16);
            Check(n == 12 && pikis.Take((int)n).Any(p => p.state == Native.PikiState.Attack), "dismiss must send the squad charging");
        }
        Console.WriteLine("PASS: Olimar ABI, sprout/leaf, airborne throw, whistle VFX, flying Onion VFX and charging squad");
    }
}
