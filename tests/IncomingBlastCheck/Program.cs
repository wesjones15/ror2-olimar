using System;
using System.IO;
using OlimarTest;
using RoR2;
using Ror2Common;
using UnityEngine;

static class Program
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    static int Main(string[] args)
    {
        Check(Native.Load(Path.GetFullPath(args[0]), out string error), error);
        byte[] assets = args.Length > 1 && Directory.Exists(args[1]) ? Native.CString(args[1]) : null;
        if (assets == null) Console.WriteLine("SKIP: disc health check (asset cache absent); exercising default health");
        var sim = Native.pik_sim_new(assets, 1);
        Check(sim != IntPtr.Zero, "native sim starts");
        try
        {
            var owner = new Native.Vec3(1000, 0, 0); // Olimar is well outside the explosion.
            int captain = Native.pik_sim_add_captain(sim, ref owner);
            foreach (float x in new[] { 0f, 0f, 250f })
            {
                var at = new Native.Vec3(x, 0, 0);
                Check(Native.pik_sim_spawn_piki(sim, 0, ref at, captain) >= 0, "spawn idle Pikmin");
            }
            Native.Piki[] Read() { var p = new Native.Piki[3]; Native.pik_sim_pikis(sim, p, 3); return p; }
            float health = Read()[0].health;
            if (assets != null) Check(health == 30f, "retail disc health loaded");
            var world = new PikminWorld(sim);
            // Four reported RoR2 victims still produce just one native hit per member.
            BlastDamage.Fire(_ =>
            {
                for (int i = 0; i < 4; i++) Check(BlastDamage.CoversReport(DamageType.AOE), "duplicate victim reports covered");
                return new BlastAttack.Result { hitCount = 4 };
            }, new BlastAttack(), _ => world.Apply(12f));
            float once = health - 12f / 110f * 100f;
            Check(Math.Abs(Read()[0].health-once) < .001f && Math.Abs(Read()[1].health-once) < .001f,
                "overlapping members each take exactly one hit");
            Check(Read()[2].health == health, "outside blast remains unharmed");
            Physics.Blocked = true;
            world.Apply(12f, los: true);
            Check(Math.Abs(Read()[0].health-once) < .001f, "LOS blast respects terrain occlusion");
            Physics.Blocked = false;
            world.stepping = true;
            world.Apply(12f);
            Check(Math.Abs(Read()[0].health-once) < .001f, "damage defers while native sim is stepping");
            world.Drain();
            // Terrain impact: no RoR2 body hit and no player attack are needed for damage.
            int hits = 2;
            while (Read()[0].state != Native.PikiState.Dying && hits++ < 20)
                BlastDamage.Fire(_ => default, new BlastAttack(), _ => world.Apply(12f));
            Check(Read()[0].state == Native.PikiState.Dying && Read()[1].state == Native.PikiState.Dying,
                "repeated terrain explosions kill the idle squad");
            Check(Read()[2].health == health, "distant Pikmin survive repeated explosions");
            Console.WriteLine("IncomingBlastCheck: ok (actual adapter + native sim: idle squad hurt and killed without RoR2 victims)");
            return 0;
        }
        finally { Native.pik_sim_free(sim); }
    }
}
