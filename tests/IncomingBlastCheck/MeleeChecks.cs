using System;
using OlimarTest;
using RoR2;
using UnityEngine;

static class MeleeChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    public static void Run(byte[] assets)
    {
        var sim = Native.pik_sim_new(assets, 2);
        Check(sim != IntPtr.Zero, "melee native sim starts");
        try
        {
            var owner = new Native.Vec3(1000,0,0); // Neither Olimar nor any proxy gets hit.
            int captain = Native.pik_sim_add_captain(sim, ref owner);
            foreach (float x in new[] { 0f, 0f, 250f })
            {
                var at = new Native.Vec3(x,0,0);
                Check(Native.pik_sim_spawn_piki(sim, 0, ref at, captain) >= 0, "spawn standing squad");
            }
            Native.Piki[] Read() { var p=new Native.Piki[3]; Native.pik_sim_pikis(sim,p,3); return p; }
            float health=Read()[0].health;
            var world=new PikminWorld(sim);
            var hand=new HitBox();
            hand.transform.position=new Vector3(0,.5f,0);
            hand.transform.lossyScale=new Vector3(2,2,2);
            var attack=new OverlapAttack { hitBoxGroup=new HitBoxGroup { hitBoxes=new[] { hand, hand } } };
            Time.realtimeSinceStartup=1f;
            Check(world.Swing(attack), "swipe hitting only native Pikmin returns a hit");
            float once=health-12f/110f*100f;
            Check(Math.Abs(Read()[0].health-once)<.001f && Math.Abs(Read()[1].health-once)<.001f,
                "multiple overlapping hand boxes hit both overlapping Pikmin only once");
            for (int frame=0;frame<20;frame++)
            {
                Time.realtimeSinceStartup+=.02f;
                world.Swing(attack, reportedVictims: 2);
            }
            Check(Math.Abs(Read()[0].health-once)<.001f, "repeated active frames and body reports do not multiply damage");
            Check(Read()[2].health==health, "distant Pikmin outside swipe survives");
            int swings=1;
            while (Read()[0].state!=Native.PikiState.Dying && swings++<20)
            {
                world.ResetSwing(attack);
                world.Swing(attack);
            }
            Check(Read()[0].state==Native.PikiState.Dying && Read()[1].state==Native.PikiState.Dying,
                "successive mob swipes kill idle squad with no RoR2 victims");
            Check(Read()[2].health==health, "out-of-range Pikmin survives repeated swipes");

            world.ResetSwing(attack);
            hand.transform.position=new Vector3(10,.5f,0);
            world.stepping=true;
            world.Swing(attack);
            Check(Read()[2].health==health, "melee defers during native simulation step");
            // An animation ending or attack object reuse cannot alter the queued hit.
            hand.enabled=false;
            hand.transform.position=new Vector3(100,.5f,0);
            attack.damage=999f;
            world.Drain();
            Check(Math.Abs(Read()[2].health-once)<.001f, "queued melee retains active geometry and damage");
            hand.enabled=true; hand.transform.position=new Vector3(10,.5f,0);
            attack.damage=12f; attack.isCrit=true;
            attack.attacker.component=new CharacterBody { critMultiplier=3f };
            world.ResetSwing(attack);
            world.Swing(attack);
            float expected=once-36f/110f*100f;
            Check(Math.Abs(Read()[2].health-expected)<.001f, "melee uses attacker's crit multiplier");
            Console.WriteLine("Incoming melee: ok (actual hook adapter + native sim; idle squad dies to swipes without RoR2 body hits)");
        }
        finally { Native.pik_sim_free(sim); PikminWorld.Instance=null; }
    }
}
