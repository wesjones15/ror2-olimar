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

        static bool warned;

        public static void Register()
        {
            PikminEnvelope.Abi = (byte)Native.AbiVersion;
            Mp.Register(SnapshotMsg, null, OnSnapshot);
        }

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
