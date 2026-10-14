using System.Collections.Generic;
using Ror2Common;

namespace OlimarTest
{
    // The wire format of the server's Pikmin world, for the other players' replicas. The server's
    // sim is the only real one (it needs RoR2's enemies and does the damage); every few ticks
    // libpikmin encodes its entities and events (pik_sim_snapshot) and this wraps that in what the
    // host adds: which Olimar is which captain, and the arrival's progress. Clients lay the snapshot
    // over a replica sim (pik_replica_apply) and draw it like the real thing. No Unity types here.

    /// What the host knows about one captain that the sim doesn't: whose body he is.
    internal struct CaptainInfo
    {
        public byte id;
        /// The body's network id (0 if it has none), so each machine finds its own copy of him.
        public uint netId;
        public byte flags;
        /// Olimar's beam-in progress 0..1 while it lasts, else -1.
        public float beam;

        public const byte Temporary = 1, Arriving = 2;

        public bool IsTemporary => (flags & Temporary) != 0;
        public bool IsArriving => (flags & Arriving) != 0;
    }

    internal static class PikminEnvelope
    {
        /// Bumped whenever the wire format changes; a message with another number is ignored.
        public const byte Protocol = 1;
        /// pikmin.dll's ABI (Native.AbiVersion), compared so a mismatched build is refused.
        public static byte Abi = 25;

        public enum Status { Ok, Corrupt, WrongProtocol, WrongAbi }

        /// `epoch` counts the server's sims (a new stage starts a new one); `snapshot` is
        /// pik_sim_snapshot's bytes.
        public static byte[] Write(uint epoch, IList<CaptainInfo> captains, byte[] snapshot, int snapshotLength)
        {
            var w = new ByteWriter(16 + captains.Count * 10 + snapshotLength);
            w.U8(Protocol);
            w.U8(Abi);
            w.U32(epoch);
            w.U8((byte)captains.Count);
            foreach (var c in captains)
            {
                w.U8(c.id);
                w.U32(c.netId);
                w.U8(c.flags);
                w.F32(c.beam);
            }
            w.U32((uint)snapshotLength);
            w.Bytes(snapshot, 0, snapshotLength);
            return w.ToArray();
        }

        public static Status Read(ByteReader r, out uint epoch, List<CaptainInfo> captains, out byte[] snapshot, out byte theirs)
        {
            epoch = 0;
            snapshot = null;
            theirs = r.U8();
            byte abi = r.U8();
            if (r.Failed) return Status.Corrupt;
            if (theirs != Protocol) return Status.WrongProtocol;
            if (abi != Abi)
            {
                theirs = abi;
                return Status.WrongAbi;
            }
            epoch = r.U32();
            int n = r.U8();
            for (int i = 0; i < n; i++)
                captains.Add(new CaptainInfo { id = r.U8(), netId = r.U32(), flags = r.U8(), beam = r.F32() });
            uint length = r.U32();
            if (r.Failed || length != (uint)r.Remaining) return Status.Corrupt;
            snapshot = new byte[length];
            for (int i = 0; i < length; i++) snapshot[i] = r.U8();
            return r.Failed ? Status.Corrupt : Status.Ok;
        }
    }
}
