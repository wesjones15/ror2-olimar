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
        public const byte Protocol = 1, Abi = 30;
        public string RunId;
        public uint Epoch, Mask;
        public ShipRepairProgress.Phase State;
        public float Seconds;
        public int Stages;
        public int[] Order;
        public Part[] Parts;
        internal sealed class Part
        {
            public int Id;
            public uint Pellet, Weight;
            public string Name;
            public float X, Y, Z;
            public bool Discovered, Delivered, Stalled;
        }
        public byte[] Write()
        {
            var w = new ByteWriter();
            w.U8(Protocol); w.U8(Abi); w.Str(RunId); w.U32(Epoch); w.U32(Mask); w.U8((byte)State);
            w.F32(Seconds); w.I32(Stages); w.U8((byte)Order.Length);
            foreach (int id in Order) w.U8((byte)id);
            w.U8((byte)Parts.Length);
            foreach (var p in Parts)
            {
                w.U8((byte)p.Id); w.U32(p.Pellet); w.Str(p.Name); w.U32(p.Weight);
                w.F32(p.X); w.F32(p.Y); w.F32(p.Z); w.Bool(p.Discovered); w.Bool(p.Delivered); w.Bool(p.Stalled);
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
            count = r.U8(); if (count > 10) return false;
            var parts = new List<Part>();
            for (int i = 0; i < count; i++)
            {
                var part = new Part { Id = r.U8(), Pellet = r.U32(), Name = r.Str(), Weight = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(),
                    Discovered = r.Bool(), Delivered = r.Bool(), Stalled = r.Bool() };
                if (part.Id >= 30 || !Finite(part.X) || !Finite(part.Y) || !Finite(part.Z) || part.Weight == 0 || part.Weight > 100 ||
                    part.Delivered != ((mask & (1u << part.Id)) != 0) || parts.Any(other => other.Id == part.Id || other.Pellet == part.Pellet)) return false;
                parts.Add(part);
            }
            if (r.Failed || !r.Done) return false;
            p.Parts = parts.ToArray(); packet = p; return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
