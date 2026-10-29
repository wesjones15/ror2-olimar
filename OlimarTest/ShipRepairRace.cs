using System;
using System.Collections.Generic;
using System.Linq;

namespace OlimarTest
{
    internal sealed class ShipRepairRacer
    {
        public uint User;
        public string Name;
        public uint Mask;
        public int Salvaged;
        public int Pikmin;
        /// Pikmin 2 treasures this racer's Dolphin received and their worth in hundredths of a part: a category
        /// of its own, always worth less than a part (ShipRepairTreasures), counted in whole parts' worth in the score.
        public int Treasures, TreasureHundredths;
        public int Score => Order.Count + Pikmin / 50 + ShipRepairTreasures.Whole(TreasureHundredths);
        public void ReceiveTreasure(int hundredths) { Treasures++; TreasureHundredths += Math.Max(0, Math.Min(99, hundredths)); }
        public readonly List<int> Order = new List<int>();
        public bool Receive(int part)
        {
            if (part < 0 || part >= 30 || (Mask & (1u << part)) != 0) return false;
            Mask |= 1u << part; Order.Add(part); return true;
        }
    }

    internal sealed class ShipRepairRace
    {
        public readonly Dictionary<uint, ShipRepairRacer> Players = new Dictionary<uint, ShipRepairRacer>();
        public uint Winner { get; private set; }
        public uint Completer { get; private set; }
        public ShipRepairRacer Enroll(uint user, string name)
        {
            if (user == 0) throw new ArgumentOutOfRangeException(nameof(user));
            if (!Players.TryGetValue(user, out var player)) Players.Add(user, player = new ShipRepairRacer { User = user });
            player.Name = name; return player;
        }
        public bool Receive(uint user, int part)
        {
            if (Completer != 0 || !Players.TryGetValue(user, out var player) || !player.Receive(part)) return false;
            if (player.Mask == ShipRepairProgress.AllParts) Completer = user;
            return true;
        }
        public bool Salvage(uint user, int part)
        {
            if (Completer != 0 || part < 0 || part >= 30 || !Players.TryGetValue(user, out var player) || (player.Mask & (1u << part)) == 0) return false;
            player.Salvaged++; return true;
        }
        public void FinalizeScores(Func<uint, int> pikminCount)
        {
            if (Completer == 0 || Winner != 0) return;
            CapturePikmin(pikminCount);
            Winner = Best(Players.Values, Completer);
        }
        public void CapturePikmin(Func<uint, int> pikminCount)
        {
            foreach (var player in Players.Values) player.Pikmin = Math.Max(0, pikminCount(player.User));
        }
        public static uint Best(IEnumerable<ShipRepairRacer> players, uint completer) => players
            .OrderByDescending(player => player.Score)
            .ThenByDescending(player => player.Order.Count)
            .ThenBy(player => player.User == completer ? 0 : 1)
            .ThenBy(player => player.User)
            .Select(player => player.User).FirstOrDefault();
        public void Hydrate(IEnumerable<ShipRepairRacer> players, uint winner)
        {
            Players.Clear(); foreach (var player in players) Players.Add(player.User, player);
            Completer = Players.Values.FirstOrDefault(player => player.Mask == ShipRepairProgress.AllParts)?.User ?? 0;
            Winner = winner;
        }
        public static int EnemyBudget(int players, bool competitive) => 7 * (competitive ? Math.Max(1, players) : 1);
        public static int[] StageIds(ulong seed, uint epoch, int count, IEnumerable<uint> missingMasks)
        {
            var masks = missingMasks.ToArray();
            var order = new ShipRepairProgress().Sample(seed ^ ((ulong)epoch << 32), 30);
            var used = new int[30];
            var result = new List<int>();
            for (int slot = 0; slot < count; slot++)
            {
                int id = order.Where(p => used[p] < masks.Count(mask => (mask & (1u << p)) == 0))
                    .OrderBy(p => used[p]).DefaultIfEmpty(-1).First();
                if (id < 0) break;
                result.Add(id); used[id]++;
            }
            return result.ToArray();
        }
    }
}
