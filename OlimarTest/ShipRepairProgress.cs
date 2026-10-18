using System;
using System.Collections.Generic;

namespace OlimarTest
{
    // Run identity and native entity IDs live in the host adapter. This ledger has no
    // Unity types, so duplicates, stage boundaries and clock semantics can be checked headless.
    internal sealed class ShipRepairProgress
    {
        public const uint AllParts = (1u << 30) - 1;
        public enum Phase : byte { Loading, Arriving, Playing, Winning, Failed, Results }
        public uint Mask { get; private set; }
        public uint Epoch { get; private set; }
        public Phase State { get; private set; } = Phase.Loading;
        public double Seconds { get; private set; }
        public bool Started { get; private set; }
        public readonly List<int> Order = new List<int>(30);
        public int Count => Order.Count;
        public int Stages { get; private set; }
        public bool Accepting { get; private set; }

        public void EnterStage()
        {
            if (State >= Phase.Winning) return;
            Epoch++;
            Stages++;
            Accepting = false;
            State = Phase.Arriving;
        }
        public void Release()
        {
            if (State != Phase.Arriving) return;
            Started = Accepting = true;
            State = Phase.Playing;
        }
        public void CloseStage()
        {
            Accepting = false;
            if (State < Phase.Winning) State = Phase.Loading;
        }
        public void Tick(double dt, bool pausedOrLoading)
        {
            if (Started && State < Phase.Winning && !pausedOrLoading && dt > 0 && !double.IsNaN(dt) && !double.IsInfinity(dt))
                Seconds += dt;
        }
        public bool Receive(uint epoch, int part)
        {
            if (!Accepting || State != Phase.Playing || epoch != Epoch || part < 0 || part >= 30 || (Mask & (1u << part)) != 0) return false;
            Mask |= 1u << part;
            Order.Add(part);
            if (Mask == AllParts) { Accepting = false; State = Phase.Winning; }
            return true;
        }
        public void Fail()
        {
            if (State >= Phase.Winning) return;
            State = Phase.Failed;
            Accepting = false;
        }
        public void Complete()
        {
            if (State == Phase.Winning) State = Phase.Results;
        }
        public int[] Sample(ulong seed, int count = 10)
        {
            var candidates = new List<int>();
            for (int part = 0; part < 30; part++) if ((Mask & (1u << part)) == 0) candidates.Add(part);
            ulong rng = seed ^ ((ulong)Epoch * 0x9E3779B97F4A7C15UL) ^ 0x5348495050415254UL;
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                rng ^= rng >> 12; rng ^= rng << 25; rng ^= rng >> 27;
                int j = (int)(unchecked(rng * 2685821657736338717UL) % (uint)(i + 1));
                int swap = candidates[i]; candidates[i] = candidates[j]; candidates[j] = swap;
            }
            return candidates.GetRange(0, Math.Min(Math.Max(count, 0), candidates.Count)).ToArray();
        }
    }
}
