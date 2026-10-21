using System;
using System.Collections.Generic;
using System.Linq;

namespace OlimarTest
{
    // The rules for Pikmin enemies in Ship Repair that need no engine: whether an enemy's death rewards
    // a part and whom it benefits, and how big a corpse is. Kept apart from RoR2 so ShipRepairCheck can
    // run them.
    internal static class TekiRules
    {
        /// An enemy's death rewards a part: a big Pikmin enemy always does (it ignores the killer's
        /// drop interval and the chance), anything else by Ship Repair's usual rule. `powerful` is a
        /// boss, champion or elite-sized monster, `roll` a 0..1 draw against `commonChance`.
        public static bool RewardsPart(bool big, bool stageBoss, bool powerful, bool pacingReady, double roll, double commonChance)
        {
            if (big) return true;
            if (!stageBoss && !powerful && !(roll < commonChance)) return false;
            return stageBoss || pacingReady;
        }

        /// The owned-part masks to draw a reward from, for ShipRepairRace.StageIds: the killer's own in
        /// a race (so the part is one the killer lacks), or everyone's if the killer isn't a racer,
        /// and what the crew has or has on the map in the shared game.
        public static uint[] BenefitMasks(bool competitive, uint? killerOwned, IEnumerable<uint> allOwned, uint sharedUnavailable)
        {
            if (!competitive) return new[] { sharedUnavailable };
            if (killerOwned.HasValue) return new[] { killerOwned.Value };
            return allOwned.ToArray();
        }

        /// The carriers a corpse needs: the disc's pellet config times a multiplier, at least 1, with the
        /// most never below the least.
        public static (uint min, uint max) Carriers(uint discMin, uint discMax, double scale)
        {
            uint min = (uint)Math.Max(1, Math.Round(discMin * Math.Max(0.0, scale)));
            uint max = Math.Max(min, (uint)Math.Max(1, Math.Round(discMax * Math.Max(0.0, scale))));
            return (min, max);
        }

        /// The effects an enemy's death plays by its `TPI_CarcassSize` (0 large, 1 medium, 2 small, 3 none):
        /// `EFF_Teki_DeathWave`, `DeathGlow` and `DeathSmoke` (`BTeki::createDeadEffect`).
        public static int[] DeathEffects(int carcass) =>
            carcass == 2 ? new[] { 61, 62, 63 } : carcass == 1 ? new[] { 58, 59, 60 } : carcass == 0 ? new[] { 55, 56, 57 } : new int[0];

        /// The soul that rises when the corpse is made, or -1 (`BTeki::createSoulEffect`).
        public static int SoulEffect(int carcass) => carcass == 2 ? 66 : carcass == 1 ? 65 : carcass == 0 ? 64 : -1;

        /// The draw id of a corpse's model: above every enemy body's, which TekiBody numbers from 1
        /// below 2^31.
        public static uint CorpseDrawId(uint pellet) => 0x8000_0000u | pellet;
    }
}
