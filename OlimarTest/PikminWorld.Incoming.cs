using RoR2;
using Ror2Common;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    internal partial class PikminWorld
    {
        void EnemyBlast(Vector3 centre, float radius, float damage, BlastAttack.FalloffModel falloff,
            bool lineOfSight, bool freeze, bool fire, float proc)
        {
            if (!NetworkServer.active || sim == System.IntPtr.Zero
                || (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal)) return;
            if (stepping)
            {
                // Capture values, not BlastAttack: callers may reuse it after Fire returns.
                afterStep.Add(() => EnemyBlast(centre, radius, damage, falloff, lineOfSight, freeze, fire, proc));
                return;
            }
            PikiCount = Read(ref Pikis, readPikis ??= (a, n) => Native.pik_sim_pikis(sim, a, n));
            if (freeze) Freeze(centre, radius, proc);
            float full = Mathf.Max(1f, Plugin.PikminHealth.Value * NearestOlimarHealth(centre));
            int hurt = 0;
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive == 0) continue;
                // Pikmin colours: 0 blue, 1 red, 2 yellow, 3 purple, 4 white. Red is fireproof.
                if (fire && p.color == 1) continue;
                var at = UnityHost.ToUnity(p.pos);
                float size = p.size * UnityHost.Scale;
                float distance = Mathf.Max(0f, Vector3.Distance(at, centre) - size);
                float fraction = BlastDamage.Fraction(falloff, distance, radius);
                if (fraction <= 0f) continue;
                // Use the body's centre for terrain occlusion, not its foot on the floor.
                if (lineOfSight && Physics.Linecast(centre, at + Vector3.up * size,
                    UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                if (damage > 0f)
                    hurt += Native.pik_sim_attack_piki(sim, p.id, damage * fraction / full * 100f);
            }
            if (hurt > 0 && Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"Enemy blast hurt {hurt} Pikmin (radius {radius:0.##} m)");
        }

    }
}
