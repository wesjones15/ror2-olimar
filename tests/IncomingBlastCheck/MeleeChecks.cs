using System;
using OlimarTest;
using RoR2;
using UnityEngine;

static class MeleeChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }

    // One swing catches the two Pikmin nearest the swing; a second swing inside the
    // immunity window is ignored; the window expiring lets the next one land.
    static void CapAndWindow(byte[] assets)
    {
        var sim = Native.pik_sim_new(assets, 2);
        Check(sim != IntPtr.Zero, "melee native sim starts");
        try
        {
            var owner = new Native.Vec3(1000, 0, 0); // Neither Olimar nor any proxy gets hit.
            int captain = Native.pik_sim_add_captain(sim, ref owner);
            foreach (float x in new[] { 0f, 10f, 20f, 30f, 250f })
            {
                var at = new Native.Vec3(x, 0, 0);
                Check(Native.pik_sim_spawn_piki(sim, 0, ref at, captain) >= 0, "spawn standing squad");
            }
            Native.Piki[] Read() { var p = new Native.Piki[5]; Native.pik_sim_pikis(sim, p, 5); return p; }
            float health = Read()[0].health;
            var world = new PikminWorld(sim);
            var hand = new HitBox();
            hand.transform.position = new Vector3(0, .5f, 0);
            hand.transform.lossyScale = new Vector3(2, 2, 2);
            var attack = new OverlapAttack { hitBoxGroup = new HitBoxGroup { hitBoxes = new[] { hand, hand } } };
            Time.realtimeSinceStartup = 1f;

            float hit = 12f * .6f / 110f * 100f;
            float once = health - hit;
            Check(world.Swing(attack), "a swipe hitting only native Pikmin returns a hit");
            Check(Math.Abs(Read()[0].health - once) < .001f && Math.Abs(Read()[1].health - once) < .001f,
                "the cap takes the two Pikmin nearest the swing, once each");
            Check(Read()[2].health == health && Read()[3].health == health,
                "the cap leaves the next Pikmin unhurt");
            Check(Read()[4].health == health, "a distant Pikmin outside the swipe survives");

            Time.realtimeSinceStartup += .1f;
            world.ResetSwing(attack);
            world.Swing(attack);
            Check(Math.Abs(Read()[0].health - once) < .001f && Math.Abs(Read()[1].health - once) < .001f,
                "a second swing inside the immunity window is ignored");

            Time.realtimeSinceStartup += .31f;
            world.ResetSwing(attack);
            world.Swing(attack);
            float twice = once - hit;
            Check(Math.Abs(Read()[0].health - twice) < .001f, "the window expires and the next swing lands");
            Console.WriteLine("Incoming melee: ok (cap, nearest-first ranking and the immunity window)");
        }
        finally { Native.pik_sim_free(sim); PikminWorld.Instance = null; }
    }

    // The original adapter checks, at the new cap/scale/window: one swing per target,
    // crit before the scale, deferred hits keep their captured geometry and damage.
    static void KillSquad(byte[] assets)
    {
        var sim = Native.pik_sim_new(assets, 2);
        Check(sim != IntPtr.Zero, "melee native sim starts");
        try
        {
            var owner = new Native.Vec3(1000, 0, 0);
            int captain = Native.pik_sim_add_captain(sim, ref owner);
            foreach (float x in new[] { 0f, 0f, 250f })
            {
                var at = new Native.Vec3(x, 0, 0);
                Check(Native.pik_sim_spawn_piki(sim, 0, ref at, captain) >= 0, "spawn standing squad");
            }
            Native.Piki[] Read() { var p = new Native.Piki[3]; Native.pik_sim_pikis(sim, p, 3); return p; }
            float health = Read()[0].health;
            var world = new PikminWorld(sim);
            var hand = new HitBox();
            hand.transform.position = new Vector3(0, .5f, 0);
            hand.transform.lossyScale = new Vector3(2, 2, 2);
            var attack = new OverlapAttack { hitBoxGroup = new HitBoxGroup { hitBoxes = new[] { hand, hand } } };
            Time.realtimeSinceStartup = 1f;

            float hit = 12f * .6f / 110f * 100f;
            float once = health - hit;
            Check(world.Swing(attack), "a swipe hitting only native Pikmin returns a hit");
            Check(Math.Abs(Read()[0].health - once) < .001f && Math.Abs(Read()[1].health - once) < .001f,
                "multiple overlapping hand boxes hit both overlapping Pikmin once");
            for (int frame = 0; frame < 20; frame++)
            {
                Time.realtimeSinceStartup += .02f;
                world.Swing(attack, reportedVictims: 2);
            }
            Check(Math.Abs(Read()[0].health - once) < .001f,
                "repeated active frames and body reports do not multiply damage");
            Check(Read()[2].health == health, "distant Pikmin outside swipe survives");

            int swings = 1;
            while (Read()[0].state != Native.PikiState.Dying && swings++ < 60)
            {
                Time.realtimeSinceStartup += .31f;
                world.ResetSwing(attack);
                world.Swing(attack);
            }
            Check(Read()[0].state == Native.PikiState.Dying && Read()[1].state == Native.PikiState.Dying,
                "successive mob swipes kill idle squad with no RoR2 victims");
            Check(Read()[2].health == health, "out-of-range Pikmin survives repeated swipes");

            // A queued hit keeps the geometry and damage captured when the swing fired.
            Time.realtimeSinceStartup += .31f;
            hand.transform.position = new Vector3(10, .5f, 0);
            world.stepping = true;
            world.Swing(attack);
            Check(Read()[2].health == health, "melee defers during native simulation step");
            hand.enabled = false;
            hand.transform.position = new Vector3(100, .5f, 0);
            attack.damage = 999f;
            world.Drain();
            Check(Math.Abs(Read()[2].health - once) < .001f,
                "queued melee retains active geometry and damage");

            // Crit multiplies before the Pikmin damage scale.
            Time.realtimeSinceStartup += .4f;
            hand.enabled = true;
            hand.transform.position = new Vector3(10, .5f, 0);
            attack.damage = 12f;
            attack.isCrit = true;
            attack.attacker.component = new CharacterBody { critMultiplier = 3f };
            world.ResetSwing(attack);
            world.Swing(attack);
            float expected = once - 3f * hit;
            Check(Math.Abs(Read()[2].health - expected) < .001f, "melee uses the attacker's crit multiplier");
            Console.WriteLine("Incoming melee: ok (actual hook adapter + native sim; idle squad dies to swipes without RoR2 body hits)");
        }
        finally { Native.pik_sim_free(sim); PikminWorld.Instance = null; }
    }

    public static void Run(byte[] assets)
    {
        CapAndWindow(assets);
        KillSquad(assets);
    }
}
