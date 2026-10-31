using System.Collections.Generic;
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
        /// Seconds since boarding, for the lift into the Onion.
        public float boardAge;
        /// The Utility slot was full when he boarded, and how many charges it held once the ride's was spent: the
        /// recharge of the ride's charge waits for the ride to end (see HoldRecharge).
        public bool slotWasFull;
        public int stockAfter;
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

        /// The boarding clip is missing from the extracted assets (the view said so): he just vanishes inside.
        internal bool boardingMissing;
        static float boardFirst = -1f, boardLast;
        /// The clip is 30 frames a second; it plays at twice that, so the lift takes about a second and a half.
        const float BoardingSpeed = 60f;

        /// The lift into the Onion that boarding plays: the clip frame and the Onion's foot (sim units), while it lasts.
        public bool BoardingClip(int captain, out float frame, out Native.Vec3 at)
        {
            frame = -1f;
            at = default;
            if (boardingMissing) return false;
            if (boardFirst < 0f) Native.pik_view_boarding_frames(out boardFirst, out boardLast);
            float age;
            Vector3 foot;
            if (ClientMode)
            {
                if (!Riding(captain) || !RideOnionFoot(captain, out foot)) { rideSeen.Remove(captain); return false; }
                if (!rideSeen.TryGetValue(captain, out float since)) rideSeen[captain] = since = Time.time;
                age = Time.time - since;
            }
            else
            {
                if (!onionOf.TryGetValue(captain, out var f) || f.ride == null) return false;
                age = f.ride.boardAge;
                foot = f.position;
            }
            frame = boardFirst + age * BoardingSpeed;
            if (frame >= boardLast) return false;
            at = UnityHost.ToSim(foot);
            return true;
        }

        public bool Riding(int captain) => ClientMode
            ? remoteInfo.TryGetValue(captain, out var info) && info.IsRiding
            : onionOf.TryGetValue(captain, out var f) && f.ride != null;

        /// Where the Onion he rides is, wherever the sim runs: the flight on the server, the replica on a client.
        public bool RideOnionFoot(int captain, out Vector3 foot)
        {
            foot = default;
            if (!ClientMode)
            {
                if (!onionOf.TryGetValue(captain, out var f) || f.ride == null) return false;
                foot = f.position;
                return true;
            }
            if (!remoteInfo.TryGetValue(captain, out var info) || info.onion < 0) return false;
            for (int i = 0; i < OnionCount; i++)
                if (Onions[i].id == info.onion)
                {
                    foot = UnityHost.ToUnity(Onions[i].pos);
                    return true;
                }
            return false;
        }

        // When a client first saw a captain riding, for the lift into the Onion.
        readonly Dictionary<int, float> rideSeen = new Dictionary<int, float>();

        /// Olimar presses B in his Onion's beam. Spends a skill charge; `why` says what is missing.
        public bool BeginRide(int captain, out string why)
        {
            why = null;
            if (ClientMode)
            {
                // The host flies the Onion; it answers with the Riding flag.
                if (!moverDriver || OnionInReach(captain) < 0) { why = "Stand in the Onion's beam to board."; return false; }
                moverDriver.SendOnion(OnionCommandPacket.Board, 0, 0);
                return true;
            }
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body)
            { why = "There is no Onion to board."; return false; }
            if (f.ride != null) return false;
            if (f.dormant) { why = "The Onion is dormant."; return false; }
            if (OnionInReach(captain) != f.id) { why = "Stand in the Onion's beam to board."; return false; }
            var slot = d.Body.skillLocator ? d.Body.skillLocator.utility : null;
            // The charge goes at boarding, but its recharge waits for the ride to end (HoldRecharge).
            bool full = slot && slot.stock >= slot.maxStock;
            if (slot && !NativeSkills.Spend(slot, OlimarSkills.Ride)) { why = "Onion Ride is recharging."; return false; }
            var ride = new OnionRide { origin = f.position, groundY = f.position.y, slotWasFull = full, stockAfter = slot ? slot.stock : 0 };
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
            r.boardAge += dt;
            HoldRecharge(d, r);
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

        /// RoR2 recharges a spent charge from the moment it goes. Onion Ride's starts when the ride ends, so
        /// while he rides the slot's stopwatch is held at zero. With a charge already coming back from an earlier
        /// ride it keeps running for that one; only once that one is home (stock above what the ride left) does the
        /// stopwatch belong to this ride's charge, and it is held too. Cooldown items scale the interval as ever.
        static void HoldRecharge(OlimarDriver d, OnionRide r)
        {
            var slot = d && d.Body && d.Body.skillLocator ? d.Body.skillLocator.utility : null;
            if (!slot || !NativeSkills.Holds(slot, OlimarSkills.Ride) || slot.stock >= slot.maxStock) return;
            if (r.slotWasFull || slot.stock > r.stockAfter) slot.rechargeStopwatch = 0f;
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
