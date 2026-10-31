using RoR2;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    /// What an Onion Ride remembers: where it began, its clock, the ground it last saw, and how many
    /// of each colour the Onion held when Olimar boarded (so the ride's squad can be told from the reserve).
    internal sealed class OnionRide
    {
        public OnionRidePolicy policy = OnionRidePolicy.Begin();
        public Vector3 origin, safeGround;
        public bool hasSafe;
        public float groundY;
        public readonly uint[] reserve = new uint[Native.ColourCount];
    }

    // Onion Ride (the Utility skill): Olimar steps into the Onion's beam, presses B, and flies the Onion for
    // a few seconds: the escape the on-foot kit lacks. The Onion keeps its own flight (height above the
    // ground, legs out to take Pikmin), but is steered by the left stick, faster than he walks. His squad
    // goes in through the legs as for Fill. At the end it is sneezed out: a third down each leg at once.
    internal partial class PikminWorld
    {
        /// How high above the ground the Onion flies, and how low while Pikmin are still climbing aboard
        /// (the same low hover Fill uses, so the legs are in reach).
        const float RideHeight = 3.5f, RideBoardingHeight = 0.6f;
        const float RideClimbSpeed = 6f;
        /// Ground nearer than this below the Onion counts as somewhere to put him back.
        const float RideSafeReach = 8f;

        public bool Riding(int captain) => !ClientMode && onionOf.TryGetValue(captain, out var f) && f.ride != null;

        /// Olimar presses B in his Onion's beam. Spends a skill charge; `why` says what is missing.
        public bool BeginRide(int captain, out string why)
        {
            why = null;
            if (ClientMode) { why = "Onion Ride needs the host for now."; return false; }
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body)
            { why = "There is no Onion to board."; return false; }
            if (f.ride != null) return false;
            if (f.dormant) { why = "The Onion is dormant."; return false; }
            if (OnionInReach(captain) != f.id) { why = "Stand in the Onion's beam to board."; return false; }
            var slot = d.Body.skillLocator ? d.Body.skillLocator.utility : null;
            // The charge goes at boarding and RoR2 starts the recharge from there, so cooldown reduction and
            // extra-charge items work as they do for any skill.
            if (slot && !NativeSkills.Spend(slot, OlimarSkills.Ride)) { why = "Onion Ride is recharging."; return false; }
            var ride = new OnionRide { origin = f.position, groundY = f.position.y };
            for (uint c = 0; c < Native.ColourCount; c++)
                if (OnionCounts(f.id, captain, c, out var n)) ride.reserve[c] = n.in_onion;
            f.ride = ride;
            f.following = f.airborne = true;
            f.menuBeam = f.menuReturning = false;
            f.releaseWhenReady = false;
            f.filling = false;
            f.pickup = -1;
            f.pickupDwell = 0f;
            f.transitionDuration = 0f;
            // His squad goes in through the legs, which are out while the Onion hovers low.
            ApplyOnionTransfer(captain, f.id, OnionCommandPacket.ReturnAll, 0);
            d.RideBegan(f.position);
            WriteFlight(f);
            Plugin.Log.LogInfo($"captain {captain}: boarded Onion {f.id}; grace of up to {OnionRidePolicy.GraceSeconds:0} s from {f.position}");
            return true;
        }

        /// Each physics step of a ride: steer, keep the height, run the clocks, notice the end.
        void UpdateRide(OnionFlight f)
        {
            var r = f.ride;
            float dt = Time.fixedDeltaTime;
            if (!driverOf.TryGetValue(f.captain, out var d) || !d || !d.Body || !OwnerAlive(f)) { EndRide(f); return; }
            if (d.TakeRideExit()) { EndRide(f); return; }
            bool boost = d.TakeRideBoost();
            var from = f.position;
            var flat = new Vector2(from.x - r.origin.x, from.z - r.origin.z);
            float scale = r.policy.Step(dt, flat.magnitude, boost);
            if (r.policy.Expired) { EndRide(f); return; }

            // Steer on the ground plane at twice his walking speed (three times while boosting).
            var stick = d.RideMove;
            float speed = Mathf.Max(5f, d.Body.moveSpeed) * scale;
            var step = new Vector3(stick.x, 0f, stick.y) * (speed * dt);
            if (step.sqrMagnitude > 1e-8f)
            {
                // Slide along walls and cliffs instead of through them.
                var dir = step.normalized;
                if (Physics.SphereCast(from + Vector3.up * 3.5f, 1.5f, dir, out var wall, step.magnitude + 0.5f,
                    UnityHost.Mask, QueryTriggerInteraction.Ignore))
                {
                    step = Vector3.ProjectOnPlane(step, new Vector3(wall.normal.x, 0f, wall.normal.z).normalized);
                    step.y = 0f;
                }
            }
            var at = from + step;

            // Height follows the ground like the following Onion's, and holds its last height over a pit.
            bool ground = Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 60f, UnityHost.Mask,
                QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.5f;
            if (ground)
            {
                r.groundY = hit.point.y;
                // Somewhere close enough beneath him to put him back on if he falls off the map.
                if (at.y - hit.point.y < RideSafeReach)
                {
                    r.safeGround = hit.point;
                    r.hasSafe = true;
                }
            }
            bool boarding = Native.pik_sim_onion_returning_count(sim, (uint)f.id) != 0;
            f.filling = boarding;
            float wanted = r.groundY + (boarding ? RideBoardingHeight : RideHeight);
            at.y = Mathf.MoveTowards(from.y, wanted, RideClimbSpeed * dt);
            f.position = at;
            d.RidePlace(at);
            WriteFlight(f);
        }

        /// The ride is over (he pressed a button, the time ran out, or the Onion fell): he is dropped out of the
        /// beam, the squad it carried is sneezed out at once, and the Onion goes back to following him.
        /// `fromDeath`: the Onion is about to go dormant, so it hands everyone back before it shuts.
        void EndRide(OnionFlight f, bool fromDeath = false)
        {
            var r = f.ride;
            if (r == null) return;
            f.ride = null;
            f.filling = false;
            int onion = f.id, captain = f.captain;
            uint sneezed = 0;
            // The Pikmin that went in during the ride (above what the reserve held when he boarded) come out
            // together, a third down each leg. Reserve Pikmin stay where they are.
            for (uint c = 0; c < Native.ColourCount; c++)
            {
                if (!OnionCounts(onion, captain, c, out var n)) continue;
                uint carried = n.in_onion > r.reserve[c] ? n.in_onion - r.reserve[c] : 0;
                if (carried > 0) sneezed += Native.pik_sim_onion_burst_out(sim, (uint)captain, (uint)onion, c, carried);
            }
            // Those still climbing give up and drop back to the field with the squad.
            uint dropped = Native.pik_sim_onion_cancel_entries(sim, (uint)captain, (uint)onion);
            f.following = true;
            f.airborne = true;
            f.transitionDuration = 0f;
            f.menuBeam = f.menuReturning = false;
            if (driverOf.TryGetValue(captain, out var d) && d)
                d.RideEnded(f.position, r.hasSafe ? r.safeGround : r.origin);
            WriteFlight(f);
            Plugin.Log.LogInfo($"captain {captain}: Onion Ride over{(fromDeath ? " (the Onion was destroyed)" : "")}; " +
                $"{sneezed} Pikmin sneezed out, {dropped} dropped back from the legs");
        }
    }
}
