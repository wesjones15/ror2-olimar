using System.Collections.Generic;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Ship Repair needs the Pikmin disc: every player, the host included. It has no stand-in version yet, so a player without the files can
    // neither start it nor stay in its lobby or run. The players' reports travel in an OptionalContentSession (bit 1: the files are here;
    // bit 2: they are still being prepared, which waits instead of refusing). Pikmin 2 stays a separate, optional bonus (Pikmin2Session).
    internal static class ShipRepairFiles
    {
        const uint Files = 1, Preparing = 2;
        const float RemoveAfter = 4f;
        static OptionalContentSession session;
        static readonly Dictionary<NetworkConnection, float> removeAt = new Dictionary<NetworkConnection, float>();

        static uint Local() => (Plugin.AssetsReady ? Files : 0u) | (!Plugin.AssetsSettled ? Preparing : 0u);

        internal static void Install(GameObject owner) =>
            session = OptionalContentSession.Attach(owner, 0x4F32, "Ship Repair", Local, () => ShipRepairMode.LobbyActive || ShipRepairMode.Active);

        /// This machine has no Pikmin files and is not still preparing them.
        internal static bool MissingHere => Plugin.AssetsSettled && (Local() & Files) == 0;

        /// Every player has them (the host's reading of the reports; a client reads what the host broadcast).
        internal static bool Everyone => session != null ? session.Has(Files) : (Local() & Files) != 0;

        /// Why Ship Repair cannot start, or null.
        internal static string Problem()
        {
            if (MissingHere)
                return "Ship Repair needs the Pikmin disc image in " + SharedFiles.DiscHint + " (restart after adding it).";
            if (!Everyone)
                return "Ship Repair is waiting for every player's Pikmin files.";
            return null;
        }

        /// The server, each second of a Ship Repair lobby: a player who reported without the files (and is not still preparing them) is told
        /// and removed after a few seconds.
        internal static void Enforce()
        {
            if (session == null || !NetworkServer.active) return;
            var lacking = new HashSet<NetworkConnection>(session.Lacking(Files, Preparing));
            foreach (var conn in lacking)
            {
                if (!removeAt.ContainsKey(conn))
                {
                    removeAt[conn] = Time.unscaledTime + RemoveAfter;
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                    {
                        baseToken = "Ship Repair needs the Pikmin disc image on every player's machine: a player without it is being removed.",
                    });
                    Plugin.Log.LogWarning("Ship Repair: a player without the Pikmin files is being removed.");
                }
                else if (Time.unscaledTime >= removeAt[conn])
                {
                    removeAt.Remove(conn);
                    conn.Disconnect();
                }
            }
            foreach (var conn in new List<NetworkConnection>(removeAt.Keys))
                if (!lacking.Contains(conn)) removeAt.Remove(conn);
        }
    }
}
