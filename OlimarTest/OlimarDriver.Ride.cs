using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Olimar's side of Onion Ride (the world's side is PikminWorld.Ride.cs): while he rides he is hidden inside
    // the Onion, can't be hurt or targeted (the Onion takes the damage), and his only controls are the left
    // stick to steer, its click to boost, and A/B/X/Y to climb out.
    internal partial class OlimarDriver
    {
        Vector2 rideMove;
        bool rideBoost, rideExit, rideProtected;
        int rideStartFrame;
        float rideEndedAt = -10f;
        Vector3 rideFeet;

        /// Flying the Onion now (the server's word; clients don't ride yet).
        public bool RideActive => captain >= 0 && PikminWorld.Instance && PikminWorld.Instance.Riding(captain);

        /// The left stick in ground-plane directions, camera-relative.
        public Vector2 RideMove => rideMove;

        public bool TakeRideBoost() { bool b = rideBoost; rideBoost = false; return b; }
        public bool TakeRideExit() { bool b = rideExit; rideExit = false; return b; }

        internal void RideBegan(Vector3 at)
        {
            rideFeet = at;
            rideStartFrame = Time.frameCount;
            rideMove = Vector2.zero;
            rideBoost = rideExit = false;
            CloseMenu();
            flyingX.Cancel();
            if (Body && !rideProtected)
            {
                Body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
                if (Body.hurtBoxGroup) Body.hurtBoxGroup.hurtBoxesDeactivatorCounter++;
                rideProtected = true;
            }
            transform.position = at + feetToOrigin;
            smoother.Reset();
            if (native) native.ForgetVelocity();
        }

        internal void RidePlace(Vector3 at) => rideFeet = at;

        /// The ride ended with the Onion at `onion`: he drops out of the beam onto the ground beneath it, or
        /// falls if there is none. `safe` is where fall recovery sends him: the last ground the ride crossed.
        internal void RideEnded(Vector3 onion, Vector3 safe)
        {
            rideEndedAt = Time.unscaledTime;
            if (rideProtected && Body)
            {
                Body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
                if (Body.hurtBoxGroup) Body.hurtBoxGroup.hurtBoxesDeactivatorCounter--;
            }
            rideProtected = false;
            rideMove = Vector2.zero;
            rideBoost = rideExit = false;
            var feet = Physics.Raycast(onion + Vector3.up * 1f, Vector3.down, out var hit, 60f, UnityHost.Mask,
                QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.5f ? hit.point : onion;
            transform.position = feet + feetToOrigin;
            smoother.Reset();
            if (native) native.ForgetVelocity();
            putBack = feet;
            wallGuard.Placed(feet);
            // Falling off the map now sends him to the last ground the ride passed over, not where he boarded.
            fallGuard.Reset();
            fallGuard.SetSafe(safe);
            pad.Swallow();
        }

        /// A press ending a ride must not start another.
        bool RideRecentlyEnded => Time.unscaledTime - rideEndedAt < 0.4f;

        void RideInput()
        {
            rideMove = cam != null ? cam.StickToWorld(pad.move) : pad.move;
            if (pad.boostPressed) rideBoost = true;
            // The press that boarded (B) is in this frame's flags: only a later one climbs out.
            if (pad.anyFacePressed && Time.frameCount > rideStartFrame) rideExit = true;
            transform.position = rideFeet + feetToOrigin;
        }

        void RemoteOnionInput()
        {
            var world = PikminWorld.Instance;
            if (!world || !Active || MenuOpen || RideActive) return;
            if (pad.remoteDump && world.OnionFlying(DataCaptain)) world.ReleaseFlyingOnion(DataCaptain);
            if (pad.remoteFill && world.OnionFlying(DataCaptain)) world.FillFlyingOnion(DataCaptain);
        }
    }
}
