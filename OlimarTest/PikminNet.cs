using Ror2Common;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Olimar's multiplayer messages. The server's PikminWorld runs the one real sim and sends
    // snapshots of it (PikminEnvelope); every other client lays them over a replica and draws and
    // plays it (PikminWorld.ReceiveSnapshot). Everyone needs the same plugin build and the Pikmin
    // disc's assets; a mismatch is logged once and that machine just doesn't see the Pikmin.
    internal static class PikminNet
    {
        /// The server's world: server to every client.
        public const short SnapshotMsg = 0x4F01;
        /// A client's Olimar: his pose and buttons for the server's captain (client to server).
        public const short InputMsg = 0x4F02;
        /// An Onion command from a client's Olimar (client to server).
        public const short OnionMsg = 0x4F03;
        /// Where a riding Olimar was dropped when the ride ended (server to clients).
        public const short RideEndMsg = 0x4F04;

        static bool warned;

        public static void Register()
        {
            PikminEnvelope.Abi = (byte)Native.AbiVersion;
            Mp.Register(SnapshotMsg, null, OnSnapshot);
            Mp.Register(InputMsg, OnInput, null);
            Mp.Register(OnionMsg, OnOnion, null);
            Mp.Register(RideEndMsg, null, OnRideEnd);
        }

        static bool Finite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

        // The server takes a client's Olimar's pose and buttons for his captain.
        static void OnInput(NetworkConnection from, ByteReader reader)
        {
            if (!PikminInputPacket.Read(reader, out var p)) return;
            if (!(Finite(p.px + p.py + p.pz) && Finite(p.vx + p.vy + p.vz) && Finite(p.face) && Finite(p.cx + p.cy + p.cz)
                  && Finite(p.swarmX + p.swarmY))) return;
            var go = Mp.Find(p.netId);
            var driver = go ? go.GetComponent<OlimarDriver>() : null;
            if (driver && driver.IsRemote) driver.ReceiveInput(p);
        }

        // ...and his Onion commands.
        static void OnOnion(NetworkConnection from, ByteReader reader)
        {
            if (!OnionCommandPacket.Read(reader, out var p)) return;
            var go = Mp.Find(p.netId);
            var driver = go ? go.GetComponent<OlimarDriver>() : null;
            var world = PikminWorld.Instance;
            if (!driver || !driver.IsRemote || !world || driver.Captain < 0) return;
            world.RemoteOnionCommand(driver, p);
        }

        /// A client's Olimar sends his pose and buttons to the server.
        public static void SendInput(in PikminInputPacket p) =>
            Mp.SendToServer(InputMsg, PikminInputPacket.Write(p), true);

        public static void SendRideEnd(uint netId, UnityEngine.Vector3 feet, UnityEngine.Vector3 safe) =>
            Mp.SendToAll(RideEndMsg, RideEndPacket.Write(new RideEndPacket
            {
                netId = netId, fx = feet.x, fy = feet.y, fz = feet.z, sx = safe.x, sy = safe.y, sz = safe.z,
            }), true);

        // A client's Olimar is told where the host dropped him out of the Onion.
        static void OnRideEnd(NetworkConnection unused, ByteReader reader)
        {
            if (NetworkServer.active || !RideEndPacket.Read(reader, out var p)) return;
            if (!(Finite(p.fx + p.fy + p.fz) && Finite(p.sx + p.sy + p.sz))) return;
            var go = Mp.Find(p.netId);
            var driver = go ? go.GetComponent<OlimarDriver>() : null;
            if (driver && driver.IsLocalClient)
                driver.RideEndedHere(new UnityEngine.Vector3(p.fx, p.fy, p.fz), new UnityEngine.Vector3(p.sx, p.sy, p.sz));
        }

        public static void SendOnion(in OnionCommandPacket p) =>
            Mp.SendToServer(OnionMsg, OnionCommandPacket.Write(p), true);

        static void OnSnapshot(NetworkConnection unused, ByteReader reader)
        {
            // The host's own client side gets these too: it has the real sim.
            if (NetworkServer.active) return;
            var captains = new System.Collections.Generic.List<CaptainInfo>();
            var status = PikminEnvelope.Read(reader, out uint epoch, captains, out byte[] snapshot, out byte theirs);
            if (status != PikminEnvelope.Status.Ok)
            {
                if (status != PikminEnvelope.Status.Corrupt && !warned)
                {
                    warned = true;
                    Plugin.Log.LogWarning(status == PikminEnvelope.Status.WrongAbi
                        ? $"The host's Pikmin run libpikmin ABI {theirs}, this plugin {Native.AbiVersion}: install the same Olimar release as the host. You won't see their Pikmin."
                        : $"The host's Pikmin use network protocol {theirs}, this plugin {PikminEnvelope.Protocol}: install the same Olimar release as the host. You won't see their Pikmin.");
                }
                return;
            }
            PikminWorld.ReceiveSnapshot(epoch, captains, snapshot);
        }
    }
}
