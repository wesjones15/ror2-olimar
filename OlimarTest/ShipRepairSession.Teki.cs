using UnityEngine;
using RoR2;

namespace OlimarTest
{
    // Pikmin's big enemies (TekiBodies) in Ship Repair.
    public sealed partial class ShipRepairSession
    {
        // A big Pikmin enemy always drops a part, and one its killer still needs (in a race, one they lack),
        // whatever the killer's drop interval and the usual chance say. The corpse is the sim's, left
        // by PikminWorld.OnDeath.
        void TekiPartReward(PikminWorld world, DamageReport report, CharacterBody victim)
        {
            if (bonusRng == null || !rewardedEnemies.Add(victim.GetInstanceID())) return;
            uint earner = KillerUser(report);
            var part = BonusPart(world, earner);
            if (part == null) return;
            if (!DropSite(world, part, victim.footPosition, out var point) || !SpawnPart(world, part, point)) { Broadcast(); return; }
            part.discovered = true;
            ShowToast(victim.GetDisplayName() + " dropped " + part.name + " — carry it to the Dolphin.");
            Broadcast();
        }
    }
}
