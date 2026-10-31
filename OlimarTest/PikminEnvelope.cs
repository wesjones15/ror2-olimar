using System.Collections.Generic;
using Ror2Common;

namespace OlimarTest
{
    // The wire formats of Olimar's multiplayer. The server's sim is the only real one (it needs
    // RoR2's enemies and does the damage); every few ticks libpikmin encodes its entities and events
    // (pik_sim_snapshot) and `PikminEnvelope` wraps that in what the host adds: which Olimar is which
    // captain, his Onion's state and the arrival's progress. Clients lay the snapshot over a replica
    // sim (pik_replica_apply) and draw it like the real thing. A client playing Olimar moves himself
    // (a small local sim, so he feels no lag) and sends the server his pose and what his buttons did
    // (`PikminInputPacket`), plus the Onion commands (`OnionCommandPacket`). No Unity types here.

    /// What the host knows about one captain that the sim doesn't: whose body he is, and his Onion.
    internal struct CaptainInfo
    {
        public byte id;
        /// The body's network id (0 if it has none), so each machine finds its own copy of him.
        public uint netId;
        public byte flags;
        /// Arrival or healing progress 0..1 (distinguished by flags), else -1.
        public float beam;
        /// Stored Pikmin consumed for the current healing effect (zero otherwise).
        public byte healingCount;
        /// His Onion's id in the sim, or -1.
        public short onion;

        public const byte Temporary = 1, Arriving = 2, Following = 4, Airborne = 8, Healing = 16,
            MenuBeam = 32, MenuReady = 64, Riding = 128;

        public bool IsTemporary => (flags & Temporary) != 0;
        public bool IsArriving => (flags & Arriving) != 0;
        public bool IsHealing => (flags & Healing) != 0;
        /// His Onion hovers after him (the X button lands it), rather than standing.
        public bool OnionFollowing => (flags & Following) != 0;
        /// His Onion is in the air (following, taking off or landing).
        public bool OnionAirborne => (flags & Airborne) != 0;
        public bool OnionMenuBeam => (flags & MenuBeam) != 0;
        public bool OnionMenuReady => (flags & MenuReady) != 0;
        /// Flying his Onion (Onion Ride).
        public bool IsRiding => (flags & Riding) != 0;
    }

    internal static class PikminEnvelope
    {
        /// Bumped whenever a wire format changes; a message with another number is ignored.
        public const byte Protocol = 11;
        /// pikmin.dll's ABI (Native.AbiVersion), compared so a mismatched build is refused.
        public static byte Abi = 52;

        public enum Status { Ok, Corrupt, WrongProtocol, WrongAbi }

        /// `epoch` counts the server's sims (a new stage starts a new one); `snapshot` is
        /// pik_sim_snapshot's bytes.
        public static byte[] Write(uint epoch, IList<CaptainInfo> captains, byte[] snapshot, int snapshotLength)
        {
            var w = new ByteWriter(16 + captains.Count * 12 + snapshotLength);
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
                w.U8(c.healingCount);
                w.I16(c.onion);
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
                captains.Add(new CaptainInfo { id = r.U8(), netId = r.U32(), flags = r.U8(), beam = r.F32(), healingCount = r.U8(), onion = r.I16() });
            uint length = r.U32();
            if (r.Failed || length != (uint)r.Remaining) return Status.Corrupt;
            snapshot = new byte[length];
            for (int i = 0; i < length; i++) snapshot[i] = r.U8();
            return r.Failed ? Status.Corrupt : Status.Ok;
        }
    }

    /// A client's Olimar, as the server's captain for him needs it each tick: where his own sim has
    /// him (the server's captain follows that pose, like VR), where his cursor is, which buttons
    /// were down (or pressed since the last packet), and what he is locked onto.
    internal struct PikminInputPacket
    {
        public uint netId;
        /// Pose in sim units.
        public float px, py, pz, vx, vy, vz, face;
        public float cx, cy, cz;
        public byte buttons;
        public float swarmX, swarmY;
        /// The network id of the enemy he's locked onto, or 0.
        public uint lockNetId;
        /// The pellet, corpse or ship part he's locked onto (its id plus one), or 0.
        public uint lockPellet;

        public const byte Whistle = 1, Throw = 2, Dismiss = 4, Cycle = 8, SwarmToCursor = 16;
        /// While he rides: the stick click was pressed, or any face button (climb out). The stick is in swarmX/Y.
        public const byte Boost = 32, Exit = 64;

        public static byte[] Write(in PikminInputPacket p)
        {
            var w = new ByteWriter(64);
            w.U8(PikminEnvelope.Protocol);
            w.U32(p.netId);
            w.F32(p.px); w.F32(p.py); w.F32(p.pz);
            w.F32(p.vx); w.F32(p.vy); w.F32(p.vz);
            w.F32(p.face);
            w.F32(p.cx); w.F32(p.cy); w.F32(p.cz);
            w.U8(p.buttons);
            w.F32(p.swarmX); w.F32(p.swarmY);
            w.U32(p.lockNetId);
            w.U32(p.lockPellet);
            return w.ToArray();
        }

        public static bool Read(ByteReader r, out PikminInputPacket p)
        {
            p = default;
            if (r.U8() != PikminEnvelope.Protocol) return false;
            p.netId = r.U32();
            p.px = r.F32(); p.py = r.F32(); p.pz = r.F32();
            p.vx = r.F32(); p.vy = r.F32(); p.vz = r.F32();
            p.face = r.F32();
            p.cx = r.F32(); p.cy = r.F32(); p.cz = r.F32();
            p.buttons = r.U8();
            p.swarmX = r.F32(); p.swarmY = r.F32();
            p.lockNetId = r.U32();
            p.lockPellet = r.U32();
            return !r.Failed && r.Done;
        }
    }

    /// An Onion command from a client: raise or land it, call Pikmin out of it or put them away.
    internal struct OnionCommandPacket
    {
        public uint netId;
        public byte kind;
        public byte colour;
        public uint count;

        public const byte Follow = 0, Land = 1, CallOut = 2, PutAway = 3;
        public const byte CallOutAny = 4, PutAwayAny = 5, ReleaseAll = 6, ReturnAll = 7;
        public const byte Heal = 8, ReleaseFlyingAll = 9, TriggerPlucker = 10, RecallStuck = 11, FillFlyingAll = 12, Board = 13, Summon = 14;

        public static byte[] Write(in OnionCommandPacket p)
        {
            var w = new ByteWriter(16);
            w.U8(PikminEnvelope.Protocol);
            w.U32(p.netId);
            w.U8(p.kind);
            w.U8(p.colour);
            w.U32(p.count);
            return w.ToArray();
        }

        public static bool Read(ByteReader r, out OnionCommandPacket p)
        {
            p = default;
            if (r.U8() != PikminEnvelope.Protocol) return false;
            p.netId = r.U32();
            p.kind = r.U8();
            p.colour = r.U8();
            p.count = r.U32();
            return !r.Failed && r.Done && p.kind <= Summon;
        }
    }

    /// The server tells a riding Olimar's machine where he was dropped when the ride ended, and the last ground
    /// the ride crossed (Unity metres).
    internal struct RideEndPacket
    {
        public uint netId;
        public float fx, fy, fz, sx, sy, sz;

        public static byte[] Write(in RideEndPacket p)
        {
            var w = new ByteWriter(32);
            w.U8(PikminEnvelope.Protocol);
            w.U32(p.netId);
            w.F32(p.fx); w.F32(p.fy); w.F32(p.fz);
            w.F32(p.sx); w.F32(p.sy); w.F32(p.sz);
            return w.ToArray();
        }

        public static bool Read(ByteReader r, out RideEndPacket p)
        {
            p = default;
            if (r.U8() != PikminEnvelope.Protocol) return false;
            p.netId = r.U32();
            p.fx = r.F32(); p.fy = r.F32(); p.fz = r.F32();
            p.sx = r.F32(); p.sy = r.F32(); p.sz = r.F32();
            return !r.Failed && r.Done;
        }
    }
}
