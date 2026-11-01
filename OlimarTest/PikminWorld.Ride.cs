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
        /// How high above the ground the Onion flies. The squad climbs aboard the flying Onion
        /// (the core jumps them to a flying leg's midpoint), so the ride never hovers low.
        const float RideHeight = 3.5f;
        const float RideClimbSpeed = 6f;
        /// How low a called Onion comes for a rescue or heal visit, how fast it tracks him once
        /// close, and how near his feet it counts as being over him.
        const float RescueHeight = 1.5f, RescueTrackSpeed = 20f;
        const float RescueCaptureRadius = 0.75f, RescueCaptureDrop = 0.8f;
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

        /// Olimar presses LB in his Onion's beam (or his called Onion scoops him up). Spends a skill
        /// charge; `why` says what is missing. `fromCall` skips the standing-in-the-beam test: the
        /// called Onion has already locked its beam over him, moving or airborne.
        public bool BeginRide(int captain, out string why, bool fromCall = false)
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
            if (!fromCall && OnionInReach(captain) != f.id) { why = "Stand in the Onion's beam to board."; return false; }
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
            f.summon = f.healSummon = false;
            f.healVisit = 0f;
            f.menuLockout = 0f;
            f.releaseWhenReady = false;
            f.filling = false;
            f.pickup = -1;
            f.pickupDwell = 0f;
            f.transitionDuration = 0f;
            // His squad goes in through the flying legs while the Onion scoops him up.
            ApplyOnionTransfer(captain, f.id, OnionCommandPacket.ReturnAll, 0);
            d.RideBegan(f.position, d.HasState ? d.State.face : UnityHost.Yaw(d.Body.transform.forward));
            WriteFlight(f);
            Plugin.Log.LogInfo($"captain {captain}: boarded Onion {f.id}; grace of up to {OnionRidePolicy.GraceSeconds:0} s from {f.position}");
            return true;
        }

        /// How fast a called Onion eases in over him once the raise's fast glide has brought it close, and how long it keeps trying.
        const float SummonSpeed = 12f, SummonSeconds = 25f;

        /// RT + LT: calls his Onion to where he stands and boards him when its beam is over him; again
        /// to call it off. The Onion keeps tracking him while it descends, so he need not stand still.
        public bool ToggleSummon(int captain, out string why)
        {
            why = null;
            if (ClientMode)
            {
                if (!moverDriver || !remoteInfo.TryGetValue(captain, out var info) || info.onion < 0) { why = "There is no Onion to call."; return false; }
                moverDriver.SendOnion(OnionCommandPacket.Summon, 0, 0);
                return true;
            }
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body)
            { why = "There is no Onion to call."; return false; }
            if (f.ride != null) return false;
            if (f.healVisit > 0f || f.summon) { ReleaseRescue(f); return true; }
            if (f.dormant) { why = "The Onion is dormant."; return false; }
            var slot = d.Body.skillLocator ? d.Body.skillLocator.utility : null;
            // Checked now so a called Onion never flies over for nothing.
            if (slot && !NativeSkills.Ready(slot, OlimarSkills.Ride)) { why = "Onion Ride is recharging."; return false; }
            f.summon = true;
            f.healSummon = false;
            f.healVisit = 0f;
            f.menuBeam = f.menuReturning = false;
            f.menuLockout = 0f;
            f.summonAge = 0f;
            // It comes the way a raised Onion does: a landed one takes off and a far one flies over fast (the raise's
            // 1.8 s glide), free since it is not the ride's charge; then it eases in over him (UpdateSummon).
            if (!f.following || !f.airborne) CommandOnion(captain, true);
            else if (Vector3.Distance(f.position, d.Body.footPosition) > 15f)
            {
                f.transitionStart = f.position;
                f.transitionElapsed = 0f;
                f.transitionDuration = 1.8f;
                f.foldFirst = false;
                WriteFlight(f);
            }
            return true;
        }

        /// RT + RB: the same call as ToggleSummon, but the Onion heals him from its stock on arrival
        /// instead of boarding. Free: the heal spends stored Pikmin, not the ride's charge.
        public bool ToggleHealSummon(int captain, out string why)
        {
            why = null;
            if (ClientMode)
            {
                if (!moverDriver || !remoteInfo.TryGetValue(captain, out var info) || info.onion < 0) { why = "There is no Onion to call."; return false; }
                moverDriver.SendOnion(OnionCommandPacket.HealSummon, 0, 0);
                return true;
            }
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body)
            { why = "There is no Onion to call."; return false; }
            if (f.ride != null) return false;
            if (f.healVisit > 0f || f.healSummon) { ReleaseRescue(f); return true; }
            if (f.dormant) { why = "The Onion is dormant."; return false; }
            // A normal call already on its way just switches to healing.
            if (f.summon) { f.healSummon = true; f.healVisit = 0f; return true; }
            f.summon = true;
            f.healSummon = true;
            f.healVisit = 0f;
            f.menuBeam = f.menuReturning = false;
            f.menuLockout = 0f;
            f.summonAge = 0f;
            // It comes the way a raised Onion does: a landed one takes off and a far one flies over fast (the raise's
            // 1.8 s glide), free since it is not the ride's charge; then it eases in over him (UpdateSummon).
            if (!f.following || !f.airborne) CommandOnion(captain, true);
            else if (Vector3.Distance(f.position, d.Body.footPosition) > 15f)
            {
                f.transitionStart = f.position;
                f.transitionElapsed = 0f;
                f.transitionDuration = 1.8f;
                f.foldFirst = false;
                WriteFlight(f);
            }
            return true;
        }

        /// While called: descend over him, locking the beam on him even while he moves, and take him
        /// as soon as it is over him. RT + RB starts a heal visit instead of boarding. True while
        /// this moved the Onion; false lets the usual flight run this step.
        bool UpdateSummon(OnionFlight f)
        {
            f.summonAge += Time.fixedDeltaTime;
            if (!driverOf.TryGetValue(f.captain, out var d) || !d || !d.Body || f.dormant || !OwnerAlive(f) || f.summonAge > SummonSeconds)
            {
                ReleaseRescue(f);
                return false;
            }
            // A raised or far Onion is still on the raise's fast glide; the normal flight does that.
            if (f.transitionDuration > 0f) return false;

            f.following = f.airborne = true;
            f.filling = false;
            f.pickup = -1;
            f.pickupDwell = 0f;
            f.menuBeam = true;
            f.menuReturning = false;
            var feet = d.Body.footPosition;
            var target = new Vector3(feet.x, feet.y + RescueHeight, feet.z);
            float dx = feet.x - f.position.x, dz = feet.z - f.position.z;
            if (dx * dx + dz * dz > 9f)
                f.position = Vector3.MoveTowards(f.position, target, SummonSpeed * Time.fixedDeltaTime);
            else
                // Close: lock the beam exactly over him and only ease the height, so a moving
                // (or jumping) Olimar never walks out from under the rescue.
                f.position = new Vector3(feet.x,
                    Mathf.MoveTowards(f.position.y, target.y, RescueTrackSpeed * Time.fixedDeltaTime), feet.z);

            var flat = new Vector2(feet.x - f.position.x, feet.z - f.position.z);
            if (flat.magnitude > RescueCaptureRadius
                || Mathf.Abs(f.position.y - target.y) > RescueCaptureDrop) return true;

            bool heal = f.healSummon;
            f.summon = false;
            f.healSummon = false;
            if (heal)
            {
                if (!HealFromOnion(f.captain, true))
                {
                    ReleaseRescue(f);
                    if (Plugin.LogEvents.Value)
                        Plugin.Log.LogInfo($"captain {f.captain}: the called Onion couldn't heal him");
                    return true;
                }
                // Keep the beam over him while the healing effect plays; the heal VFX follow
                // him, while each ghost stays where it was spawned (libpikmin-present).
                f.healVisit = HealingSeconds;
                return true;
            }
            if (!BeginRide(f.captain, out var why, true))
            {
                ReleaseRescue(f);
                if (Plugin.LogEvents.Value)
                    Plugin.Log.LogInfo($"captain {f.captain}: the called Onion couldn't board him: {why}");
            }
            return true;
        }

        /// RT + RB's visit: hold the beam over Olimar and follow him until the healing effect ends.
        void UpdateHealVisit(OnionFlight f)
        {
            f.healVisit -= Time.fixedDeltaTime;
            if (f.healVisit <= 0f || !driverOf.TryGetValue(f.captain, out var d) || !d || !d.Body || !OwnerAlive(f))
            {
                ReleaseRescue(f);
                return;
            }
            f.following = f.airborne = true;
            f.filling = false;
            f.pickup = -1;
            f.pickupDwell = 0f;
            f.menuBeam = true;
            f.menuReturning = false;
            var feet = d.Body.footPosition;
            // Horizontal position is his exactly; height eases so a jump does not snap the Onion.
            f.position = new Vector3(feet.x,
                Mathf.MoveTowards(f.position.y, feet.y + RescueHeight, RescueTrackSpeed * Time.fixedDeltaTime), feet.z);
        }

        /// End a rescue or visit: beam off, back to the ordinary follow, and no menu until it has
        /// carried the Onion away again.
        void ReleaseRescue(OnionFlight f)
        {
            f.summon = f.healSummon = false;
            f.healVisit = 0f;
            f.menuBeam = f.menuReturning = false;
            f.menuLockout = 1.5f;
            f.following = f.airborne = true;
            f.filling = false;
            f.pickup = -1;
            f.pickupDwell = 0f;
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
            // Read the local stick here directly: the boarding lift must not stop the Onion
            // waiting for the driver's next render frame (or for a client to be told it rides).
            var stick = !d.IsRemote && !d.IsAi ? d.PollRideMove() : d.RideMove;
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
            // The ride keeps the flying presentation throughout: it does not drop to the Fill
            // Onion's low, legs-out hover to take the squad in (the core's hover entry jumps
            // Pikmin to the middle of a flying leg and climbs from there, so the height is free).
            f.filling = false;
            float wanted = r.groundY + RideHeight;
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

