using System;
using System.Collections.Generic;
using System.Linq;

namespace OlimarTest
{
    /// <summary>
    /// Pikmin 2 treasures as Ship Repair's bonus objectives: how many lie in a stage, which, and what a delivery is
    /// worth. Plain data, so it is tested without the game. The treasures themselves (names, carriers, Pokos)
    /// come from the Pikmin 2 disc through libpikmin (<c>pik_sim_treasure_*</c>); nothing from the disc is here.
    ///
    /// Spec: <c>PelletConfig</c> (pelletConfig.cpp): <c>money</c> is the Pokos a delivery pays, <c>min</c> the Pikmin
    /// needed to lift it; <c>onyonMgr.cpp</c> adds the Pokos at delivery. Here a treasure scores in its own category
    /// below a Dolphin part (a part is 1 point; the best treasure is worth under a part).
    /// </summary>
    internal static class ShipRepairTreasures
    {
        /// <summary>The treasure's part id in a ship receipt: <c>libpikmin::TREASURE_PART_BASE</c> plus its index.</summary>
        public const int PartBase = 1000;
        /// <summary>Treasures in Pikmin 2's <c>otakara_config.txt</c> (the table's size with the cache present).</summary>
        public const int TableSize = 188;

        public enum Tier { Common, Fine, Rare, Legendary }

        /// <summary>One entry of the disc's table, as the host reads it.</summary>
        public struct Info
        {
            public int Index;
            /// <summary>Pikmin needed to lift it.</summary>
            public uint Carriers;
            public uint Pokos;
        }

        /// <summary>Pokos worth to tier: under 60 common (a half of the table), under 130 fine, under 300 rare, above that legendary.</summary>
        public static Tier TierOf(uint pokos) => pokos < 60 ? Tier.Common : pokos < 130 ? Tier.Fine : pokos < 300 ? Tier.Rare : Tier.Legendary;

        /// <summary>The tier's share of the base worth, in tenths of it: 1x, 1.5x, 2.5x and 4x.</summary>
        static readonly int[] TierTenths = { 10, 15, 25, 40 };
        /// <summary>The draw odds of each tier for a treasure that lies in a stage, in percent.</summary>
        static readonly int[] TierOdds = { 55, 28, 14, 3 };

        /// <summary>The base worth is a fraction of a part (1.0); a treasure never reaches a whole part.</summary>
        public const float MaxWeight = 0.2f;
        public const float DefaultWeight = 0.1f;

        public static float ClampWeight(float weight) => float.IsNaN(weight) || float.IsInfinity(weight) ? DefaultWeight : Math.Max(0f, Math.Min(MaxWeight, weight));

        /// <summary>
        /// What delivering a treasure of <paramref name="pokos"/> adds to the score, in hundredths of a Dolphin part. Always
        /// under 100 (a part), whatever the weight setting.
        /// </summary>
        public static int Points(uint pokos, float weight)
        {
            int hundredths = (int)Math.Round(ClampWeight(weight) * 100f * TierTenths[(int)TierOf(pokos)] / 10f);
            return Math.Max(0, Math.Min(99, hundredths));
        }

        /// <summary>The score a total of treasure points adds to the final score: whole parts' worth, rounded down.</summary>
        public static int Whole(int hundredths) => Math.Max(0, hundredths) / 100;

        /// <summary>"1.25" for 125 hundredths.</summary>
        public static string Format(int hundredths) => (Math.Max(0, hundredths) / 100f).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The treasures that lie in a stage: <paramref name="count"/> distinct ones, none of <paramref name="taken"/> (collected
        /// earlier in the run), none that need more than <paramref name="maxCarriers"/> Pikmin. Seeded, so the host and a
        /// replay agree. Tiers are drawn first (55/28/14/3 percent), then a treasure of the tier; an empty tier falls
        /// to the nearest common one.
        /// </summary>
        public static int[] Pick(ulong seed, uint epoch, int count, IReadOnlyList<Info> table, ISet<int> taken, uint maxCarriers)
        {
            var rng = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)epoch * 7907 ^ 0x2F2));
            var left = table.Where(t => t.Carriers > 0 && t.Carriers <= maxCarriers && t.Pokos > 0 && (taken == null || !taken.Contains(t.Index))).ToList();
            var chosen = new List<int>();
            for (int i = 0; i < count && left.Count > 0; i++)
            {
                int roll = rng.Next(100), tier = 0;
                for (int sum = 0; tier < TierOdds.Length - 1; tier++) { sum += TierOdds[tier]; if (roll < sum) break; }
                // The nearest non-empty tier, rarer ones first.
                var pool = Enumerable.Range(0, 4).OrderBy(t => Math.Abs(t - tier)).ThenByDescending(t => t)
                    .Select(t => left.Where(x => (int)TierOf(x.Pokos) == t).ToList()).First(p => p.Count > 0);
                var pick = pool[rng.Next(pool.Count)];
                chosen.Add(pick.Index); left.Remove(pick);
            }
            return chosen.ToArray();
        }
    }
}
