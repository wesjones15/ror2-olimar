using System.Collections.Generic;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Ship Repair needs the Pikmin and Pikmin 2 discs: every player, the host included. It has no stand-in version yet, so a player without
    // them can neither start it nor stay in its lobby or run. The players' reports travel in an OptionalContentSession (bit 1: the Pikmin
    // files are here; bit 4: the Pikmin 2 files are; bit 2: they are still being prepared, which waits instead of refusing).
    internal static class ShipRepairFiles
    {
        const uint Pikmin = 1, Preparing = 2, Pikmin2 = 4, Files = Pikmin | Pikmin2;
        const float RemoveAfter = 4f;
        static OptionalContentSession session;
        static readonly Dictionary<NetworkConnection, float> removeAt = new Dictionary<NetworkConnection, float>();

        static uint Local() => (Plugin.AssetsReady ? Pikmin : 0u) | (Pikmin2Session.LocalMask() != 0 ? Pikmin2 : 0u)
            | (!Plugin.AssetsSettled ? Preparing : 0u);

        internal static void Install(GameObject owner) =>
            session = OptionalContentSession.Attach(owner, 0x4F32, "Ship Repair", Local, () => ShipRepairMode.LobbyActive || ShipRepairMode.Active);

        /// This machine lacks a disc's files and is not still preparing them.
        internal static bool MissingHere => Plugin.AssetsSettled && (Local() & Files) != Files;

        /// Every player has them (the host's reading of the reports; a client reads what the host broadcast).
        internal static bool Everyone => session != null ? session.Has(Files) : (Local() & Files) == Files;

        /// Why Ship Repair cannot start, or null.
        internal static string Problem()
        {
            if (MissingHere)
            {
                uint here = Local();
                string which = (here & Pikmin) == 0 && (here & Pikmin2) == 0 ? "the Pikmin and Pikmin 2 disc images"
                    : (here & Pikmin) == 0 ? "the Pikmin disc image" : "the Pikmin 2 disc image";
                return "Ship Repair needs " + which + " in " + SharedFiles.DiscHint + " (restart after adding them).";
            }
            if (!Everyone)
                return "Ship Repair is waiting for every player's Pikmin and Pikmin 2 files.";
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
                        baseToken = "Ship Repair needs the Pikmin and Pikmin 2 disc images on every player's machine: a player without them is being removed.",
                    });
                    Plugin.Log.LogWarning("Ship Repair: a player without the Pikmin and Pikmin 2 files is being removed.");
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
