using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using Ror2Common;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    internal partial class PikminWorld
    {
        static Hook mobMeleeHook, mobMeleeResetHook, mobMeleeClearHook;
        object incomingMeleeWorld = new object();
        // Per-Pikmin deadlines, the same shape as procReadyAt (PikminWorld.cs) and
        // nextShake (PikminWorld.Combat.cs): stamped now + iFrames on a landed swipe
        // and compared on the next one. Host-side only; clients see the health in
        // the snapshot.
        float[] meleeImmuneUntil = new float[0];

        static void HookMobMelee()
        {
            if (mobMeleeHook != null) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public;
                var fire = typeof(OverlapAttack).GetMethod("Fire", flags, null, new[] { typeof(List<HurtBox>) }, null);
                var reset = typeof(OverlapAttack).GetMethod("Reset", flags, null, Type.EmptyTypes, null);
                var clear = typeof(OverlapAttack).GetMethod("ResetIgnoredHealthComponents", flags, null, Type.EmptyTypes, null);
                // Install resets first: an installed Fire hook must always have its swing lifecycle.
                mobMeleeResetHook = new Hook(reset, (Action<Action<OverlapAttack>, OverlapAttack>)MobMeleeReset);
                mobMeleeClearHook = new Hook(clear, (Action<Action<OverlapAttack>, OverlapAttack>)MobMeleeReset);
                mobMeleeHook = new Hook(fire,
                    (Func<Func<OverlapAttack, List<HurtBox>, bool>, OverlapAttack, List<HurtBox>, bool>)MobMeleeFired);
            }
            catch (Exception e)
            {
                mobMeleeHook?.Dispose(); mobMeleeHook = null;
                mobMeleeResetHook?.Dispose(); mobMeleeResetHook = null;
                mobMeleeClearHook?.Dispose(); mobMeleeClearHook = null;
                Plugin.Log.LogWarning($"Pikmin mob melee: hook failed ({e.GetType().Name}: {e.Message}); only hits reported on RoR2 bodies reach Pikmin");
            }
        }

        static void MobMeleeReset(Action<OverlapAttack> original, OverlapAttack attack)
        {
            original(attack);
            OverlapDamage.Reset(attack);
        }

        static bool MobMeleeFired(Func<OverlapAttack, List<HurtBox>, bool> original, OverlapAttack attack, List<HurtBox> results) =>
            !Instance ? original(attack, results) : OverlapDamage.Fire(original, attack, results, EnemyMeleeFired);

        static bool EnemyMeleeFired(OverlapDamage.Sample sample, OverlapDamage.Swing swing)
        {
            try { return Instance && Instance.EnemyMelee(sample, swing); }
            catch (Exception e) { Plugin.Log.LogError($"Pikmin mob melee: {e}"); return false; }
        }

        bool EnemyMelee(OverlapDamage.Sample sample, OverlapDamage.Swing swing)
        {
            if (!NetworkServer.active || sim == IntPtr.Zero
                || (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal)) return false;
            if (stepping)
            {
                var world = incomingMeleeWorld;
                // Sample captured hitboxes and attack values while the swing was active.
                afterStep.Add(() => { if (ReferenceEquals(world, incomingMeleeWorld)) EnemyMelee(sample, swing); });
                return false;
            }
            PikiCount = Read(ref Pikis, readPikis ??= (a, n) => Native.pik_sim_pikis(sim, a, n));
            var attacker = sample.Attacker ? sample.Attacker.GetComponent<CharacterBody>() : null;
            float damage = sample.Damage * (sample.Crit ? (attacker ? attacker.critMultiplier : 2f) : 1f)
                * Mathf.Max(0f, Plugin.PikminMeleeDamageScale.Value);
            bool freeze = (sample.Type & DamageType.Freeze2s) != 0;
            bool fire = (sample.Type & DamageType.IgniteOnHit) != 0;
            float now = sample.Now;
            float immunity = Mathf.Max(0f, Plugin.PikminMeleeIFrames.Value);
            int cap = Mathf.Max(0, Plugin.PikminMeleeMaxVictims.Value);
            if (cap > 0 && attacker && (attacker.isChampion || attacker.isElite)) cap += 2;

            // A Pikmin 1 attack tests the part it touches, not the whole arc: rank the
            // Pikmin in the swing by how close they are to its volume, then take the cap.
            // The ones latched on the attacker are nearest, so they take it and the squad
            // behind them does not.
            var candidates = new List<(float rank, int index)>();
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive == 0 || p.state == Native.PikiState.Dying) continue;
                var feet = UnityHost.ToUnity(p.pos);
                float radius = p.size * UnityHost.Scale;
                var centre = feet + Vector3.up * radius;
                if (!sample.Touches(centre, radius)) continue;
                candidates.Add((sample.SquaredDistanceTo(centre), i));
            }
            candidates.Sort((a, b) => a.rank.CompareTo(b.rank));
            int victims = cap <= 0 ? candidates.Count : Mathf.Min(cap, candidates.Count);
            bool touched = false;
            int hurt = 0;
            for (int k = 0; k < victims; k++)
            {
                var p = Pikis[candidates[k].index];
                if (now < Immunity(p.id)) continue;
                // Fire trails/breaths arrive as OverlapAttacks with IgniteOnHit. Red is fireproof.
                if (fire && p.color == 1) continue;
                ulong victim = ((ulong)p.generation << 32) | p.id;
                if (!swing.TryHit(incomingMeleeWorld, victim, now, sample.Retrigger)) continue;
                touched = true;
                var feet = UnityHost.ToUnity(p.pos);
                float full = Mathf.Max(1f, Plugin.PikminHealth.Value * NearestOlimarHealth(feet));
                if (damage > 0f && Native.pik_sim_attack_piki(sim, p.id, damage / full * 100f) != 0)
                {
                    hurt++;
                    if (immunity > 0f) SetImmunity(p.id, now + immunity);
                }
                if (freeze) Freeze(feet, 0f, sample.Proc);
            }
            if (hurt > 0 && Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"Enemy melee hurt {hurt} Pikmin");
            return touched;
        }

        float Immunity(uint piki) => piki < meleeImmuneUntil.Length ? meleeImmuneUntil[piki] : 0f;

        void SetImmunity(uint piki, float until)
        {
            if (piki >= meleeImmuneUntil.Length)
                Array.Resize(ref meleeImmuneUntil, Mathf.NextPowerOfTwo((int)piki + 1));
            meleeImmuneUntil[piki] = until;
        }
    }
}
