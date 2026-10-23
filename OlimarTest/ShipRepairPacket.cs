using System;
using System.Collections.Generic;
using System.Linq;
using Ror2Common;

namespace OlimarTest
{
    // Full reliable objective state, independent of native motion snapshots. Plain data
    // makes malformed/truncated messages testable without loading Unity or the game.
    internal sealed class ShipRepairPacket
    {
        public const byte Protocol = 5, Abi = 45;
        // A reliable UNET message may carry at most 1100 bytes; the 0.3.8 packet with 17 named parts was 1103
        // and never reached the clients. Names are looked up from the disc on the receiver and delivered
        // parts are left out, so a state message stays far under that.
        public const int MaxBytes = 1000;
        public bool Competitive;
        public uint Winner;
        public int TeamPikmin;
        public ShipRepairRacer[] Racers = Array.Empty<ShipRepairRacer>();
        public string RunId;
        public uint Epoch, Mask;
        public ShipRepairProgress.Phase State;
        public float Seconds;
        public int Stages;
        public int[] Order;
        public Part[] Parts;
        internal sealed class Part
        {
            public int Id, Slot;
            public uint Pellet, Weight;
            public string Name;
            public float X, Y, Z;
            public bool Discovered, Delivered, Stalled, EnemyDrop, Spawned, StageBoss, Recycling;
        }
        /// The packet as bytes, without the parts that do not fit under `MaxBytes` (reserved enemy
        /// drops go first, then the furthest-down list entries).
        public byte[] WriteFitting()
        {
            var all = Parts;
            Parts = all.Where(p => !p.Delivered).OrderByDescending(p => p.Spawned).ToArray();
            var bytes = Write();
            while (bytes.Length > MaxBytes && Parts.Length > 0)
            {
                Parts = Parts.Take(Parts.Length - 1).ToArray();
                bytes = Write();
            }
            Parts = all;
            return bytes;
        }
        public byte[] Write()
        {
            var w = new ByteWriter();
            w.U8(Protocol); w.U8(Abi); w.Str(RunId); w.U32(Epoch); w.U32(Mask); w.U8((byte)State);
            w.F32(Seconds); w.I32(Stages); w.U8((byte)Order.Length);
            foreach (int id in Order) w.U8((byte)id);
            w.Bool(Competitive); w.U32(Winner); w.I32(TeamPikmin); w.U8((byte)Racers.Length);
            foreach (var racer in Racers)
            {
                w.U32(racer.User); w.Str(racer.Name); w.U32(racer.Mask); w.I32(racer.Salvaged);
                w.I32(racer.Pikmin); w.U8((byte)racer.Order.Count);
                foreach (int id in racer.Order) w.U8((byte)id);
            }
            var sent = Parts.Where(p => !p.Delivered).ToArray();
            w.U16((ushort)sent.Length);
            foreach (var p in sent)
            {
                w.U8((byte)p.Id); w.U16((ushort)p.Slot); w.U8((byte)p.Weight);
                w.U8((byte)((p.Discovered ? 1 : 0) | (p.Stalled ? 2 : 0) | (p.EnemyDrop ? 4 : 0) |
                    (p.Spawned ? 8 : 0) | (p.StageBoss ? 16 : 0) | (p.Recycling ? 32 : 0)));
                if (!p.Spawned) continue;
                w.U32(p.Pellet); w.F32(p.X); w.F32(p.Y); w.F32(p.Z);
            }
            return w.ToArray();
        }
        public static bool Read(ByteReader r, out ShipRepairPacket packet)
        {
            packet = null;
            if (r.U8() != Protocol || r.U8() != Abi) return false;
            var p = new ShipRepairPacket { RunId = r.Str(), Epoch = r.U32(), Mask = r.U32(), State = (ShipRepairProgress.Phase)r.U8(), Seconds = r.F32(), Stages = r.I32() };
            int count = r.U8();
            if (r.Failed || !Guid.TryParse(p.RunId, out _) || p.Epoch == 0 || count > 30 || (p.Mask & ~ShipRepairProgress.AllParts) != 0 ||
                p.State > ShipRepairProgress.Phase.Results || !Finite(p.Seconds) || p.Seconds < 0 || p.Seconds > 316224000f || p.Stages < 1 || p.Stages > 1000000) return false;
            uint mask = 0; p.Order = new int[count];
            for (int i = 0; i < count; i++)
            {
                int id = r.U8(); if (id >= 30 || (mask & (1u << id)) != 0) return false;
                mask |= 1u << id; p.Order[i] = id;
            }
            if (mask != p.Mask || ((p.State == ShipRepairProgress.Phase.Winning || p.State == ShipRepairProgress.Phase.Results) && mask != ShipRepairProgress.AllParts)) return false;
            p.Competitive = r.Bool(); p.Winner = r.U32(); p.TeamPikmin = r.I32(); count = r.U8();
            if (count > 64 || p.TeamPikmin < 0 || (!p.Competitive && (count != 0 || p.Winner != 0)) ||
                (p.Competitive && p.TeamPikmin != 0)) return false;
            var racers = new List<ShipRepairRacer>();
            for (int i = 0; i < count; i++)
            {
                var racer = new ShipRepairRacer { User = r.U32(), Name = r.Str() };
                uint expected = r.U32(); racer.Salvaged = r.I32(); racer.Pikmin = r.I32(); int deliveries = r.U8();
                if (racer.User == 0 || racer.Salvaged < 0 || racer.Pikmin < 0 ||
                    racers.Any(other => other.User == racer.User) || deliveries > 30) return false;
                for (int j = 0; j < deliveries; j++) if (!racer.Receive(r.U8())) return false;
                if (racer.Mask != expected) return false;
                racers.Add(racer);
            }
            bool victory = p.State == ShipRepairProgress.Phase.Winning || p.State == ShipRepairProgress.Phase.Results;
            var completers = racers.Where(player => player.Mask == ShipRepairProgress.AllParts).ToArray();
            if (p.Competitive && (racers.Count == 0 ||
                (victory != (completers.Length == 1 && p.Winner != 0)) ||
                (!victory && (completers.Length != 0 || p.Winner != 0 ||
                    (p.State != ShipRepairProgress.Phase.Failed && racers.Any(player => player.Pikmin != 0)))) ||
                (victory && (!p.Order.SequenceEqual(completers[0].Order) ||
                    ShipRepairRace.Best(racers, completers[0].User) != p.Winner)))) return false;
            p.Racers = racers.ToArray();
            count = r.U16(); if (count > 451) return false;
            var parts = new List<Part>();
            for (int i = 0; i < count; i++)
            {
                var part = new Part { Id = r.U8(), Slot = r.U16(), Weight = r.U8() };
                int flags = r.U8();
                part.Discovered = (flags & 1) != 0; part.Stalled = (flags & 2) != 0; part.EnemyDrop = (flags & 4) != 0;
                part.Spawned = (flags & 8) != 0; part.StageBoss = (flags & 16) != 0; part.Recycling = (flags & 32) != 0;
                if ((flags & ~63) != 0) return false;
                if (part.Spawned) { part.Pellet = r.U32(); part.X = r.F32(); part.Y = r.F32(); part.Z = r.F32(); }
                else part.Pellet = uint.MaxValue - (uint)part.Slot;
                if (part.Id >= 30 || !Finite(part.X) || !Finite(part.Y) || !Finite(part.Z) || part.Weight == 0 || part.Weight > 100 ||
                    part.Slot < 0 || part.Slot >= 451 || (part.StageBoss && !part.EnemyDrop) ||
                    (part.Recycling && (!p.Competitive || part.Spawned || part.Delivered)) ||
                    (!part.Spawned && ((!part.EnemyDrop && !part.Recycling) || part.Discovered || part.Delivered || part.Stalled || part.Pellet != uint.MaxValue - part.Slot)) ||
                    (!p.Competitive && (mask & (1u << part.Id)) != 0) ||
                    parts.Any(other => other.Slot == part.Slot || (!p.Competitive && other.Id == part.Id) || other.Pellet == part.Pellet)) return false;
                parts.Add(part);
            }
            if (r.Failed || !r.Done) return false;
            p.Parts = parts.ToArray(); packet = p; return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
