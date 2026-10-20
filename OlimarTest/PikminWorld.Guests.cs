using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Guest characters in Ship Repair (Mario): players of another plugin's body who have no captain, Pikmin or
    // Onion. Each gets a Dolphin landed beside the teleporter, which takes ship parts put into its receiver by
    // hand; they pick the parts up, carry them and throw them (ShipRepairGuests). A guest's Dolphin is a
    // DolphinFlight under a negative key, so the rest of the world treats it like any landed ship.
    internal partial class PikminWorld
    {
        readonly Dictionary<int, uint> guestUsers = new Dictionary<int, uint>();
        readonly Dictionary<uint, int> guestKeyOf = new Dictionary<uint, int>();
        int nextGuestKey = -1;

        /// The sim is this stage's.
        internal bool StageCurrent => sim != IntPtr.Zero && Stage.instance == simStage;

        internal bool GuestReady(uint user) => guestKeyOf.TryGetValue(user, out int key) && dolphins.ContainsKey(key);

        /// Starts this stage's sim for a guest if need be and lands his Dolphin at the teleporter. False until
        /// it can (the assets, the teleporter, a clear dock).
        internal bool RegisterGuest(CharacterBody body, uint user)
        {
            if (ClientMode || !ShipRepairMode.Active || !Plugin.AssetsSettled || !body || user == 0) return false;
            if (sim != IntPtr.Zero && Stage.instance != simStage) NewStage();
            if (sim == IntPtr.Zero && !StartSim()) return false;
            return GuestReady(user) || SpawnGuestDolphin(body, user);
        }

        bool SpawnGuestDolphin(CharacterBody body, uint user)
        {
            var teleporter = TeleporterInteraction.instance;
            if (!teleporter) return false;
            var route = new List<Vector3>();
            int index = dolphins.Count;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = (index * 137.5f + attempt * 22.5f) * Mathf.Deg2Rad;
                float radius = 16f + (attempt / 16) * 8f;
                var candidate = teleporter.transform.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                if (!Physics.Raycast(candidate + Vector3.up * 12f, Vector3.down, out var hit, 24f, UnityHost.Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
                candidate = hit.point;
                if (Physics.CheckCapsule(candidate + Vector3.up * 3.2f, candidate + Vector3.up * 8f, 3f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                if (dolphins.Values.Any(other => Vector3.ProjectOnPlane(candidate - (other.dock ?? other.landing), Vector3.up).sqrMagnitude < 14f * 14f)) continue;
                var toward = Vector3.ProjectOnPlane(teleporter.transform.position - candidate, Vector3.up);
                float yaw = UnityHost.Yaw(toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward);
                // Its goal (the receiver) is 100 sim units ahead of it.
                var intake = candidate + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (100f * UnityHost.Scale);
                if (!ShipPartNavigation.TryPath(body.footPosition, intake, route)) continue;
                var pos = UnityHost.ToSim(candidate);
                int id = Native.pik_sim_add_ship(sim, ref pos, yaw);
                if (id < 0) return false;
                Native.pik_sim_set_onion_pose(sim, (uint)id, ref pos, 0);
                int key = nextGuestKey--;
                guestUsers[key] = user;
                guestKeyOf[user] = key;
                var session = ShipRepairSession.Instance;
                if (session)
                {
                    foreach (int part in session.PartsFor(user)) Native.pik_sim_give_ship_part(sim, (uint)id, (uint)part);
                    if (session.Competitive) Native.pik_sim_set_ship_hand_owned(sim, (uint)id);
                }
                dolphins[key] = new DolphinFlight { id = id, captain = key, start = candidate, landing = candidate, complete = true, dock = candidate };
                Plugin.Log.LogInfo($"Ship Repair: Dolphin {id} landed at the teleporter for guest {body.GetDisplayName()}.");
                return true;
            }
            Plugin.Log.LogWarning("Ship Repair: no clear, reachable teleporter dock for a guest's Dolphin yet.");
            return false;
        }

        /// A part by pellet id, from the server's sim or a client's replica.
        internal bool TryPart(uint pellet, out Native.Pellet found)
        {
            for (int i = 0; i < PelletCount; i++)
                if (Pellets[i].id == pellet && Pellets[i].has_ship_part != 0) { found = Pellets[i]; return true; }
            found = default;
            return false;
        }

        /// Server: the part is in a guest's hands at `at`.
        internal bool HoldPart(uint pellet, Vector3 at, float yaw)
        {
            if (sim == IntPtr.Zero || ClientMode) return false;
            var p = UnityHost.ToSim(at);
            return Native.pik_sim_hold_pellet(sim, pellet, ref p, yaw) != 0;
        }

        /// Server: a held part is set down at `at`, where Pikmin can lift it.
        internal bool ReleasePart(uint pellet, Vector3 at, float yaw)
        {
            if (sim == IntPtr.Zero || ClientMode) return false;
            var p = UnityHost.ToSim(at);
            return Native.pik_sim_release_pellet(sim, pellet, ref p, yaw) != 0;
        }

        /// Server: puts the part into a Dolphin's receiver, if one that takes it is in reach: any in the shared
        /// repair, only the guest's own in a race.
        internal bool HandPart(uint user, uint pellet)
        {
            if (sim == IntPtr.Zero || ClientMode) return false;
            bool race = ShipRepairSession.Instance && ShipRepairSession.Instance.Competitive;
            foreach (var d in dolphins.Values)
            {
                if (race && UserForCaptain(d.captain) != user) continue;
                if (Native.pik_sim_hand_ship_part(sim, (uint)d.id, pellet) != 0) return true;
            }
            return false;
        }
    }
}
