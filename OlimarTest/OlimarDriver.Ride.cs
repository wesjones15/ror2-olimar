using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Olimar's side of Onion Ride (the world's side is PikminWorld.Ride.cs): while he rides he is hidden inside
    // the Onion, can't be hurt or targeted (the Onion takes the damage), and his only controls are the left
    // stick to steer, its click to boost, and A/B/X/Y to climb out.
    //
    // The server flies the Onion. A client's Olimar sends the stick, boost and exit in his input packet (his
    // own machine reads the pad), his body follows the Onion the server draws, and the server tells him where
    // he is dropped (PikminNet.RideEndMsg).
    internal partial class OlimarDriver
    {
        Vector2 rideMove;
        bool rideBoost, rideExit, rideProtected, rideWas;
        int rideStartFrame;
        float rideEndedAt = -10f;
        Vector3 rideFeet;

        /// Flying the Onion now: the server's word, on any machine.
        public bool RideActive => ViewCaptain >= 0 && PikminWorld.Instance && PikminWorld.Instance.Riding(ViewCaptain);

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
            // A remote Olimar's body is placed by his own machine.
            if (IsRemote) return;
            transform.position = at + feetToOrigin;
            smoother.Reset();
            if (native) native.ForgetVelocity();
        }

        internal void RidePlace(Vector3 at) => rideFeet = at;

        /// The ride ended with the Onion at `onion` (on the server): he drops out of the beam onto the ground
        /// beneath it, or falls if there is none. `safe` is where fall recovery sends him: the last ground the
        /// ride crossed. A remote Olimar is told, and places himself.
        internal void RideEnded(Vector3 onion, Vector3 safe)
        {
            if (rideProtected && Body)
            {
                Body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
                if (Body.hurtBoxGroup) Body.hurtBoxGroup.hurtBoxesDeactivatorCounter--;
            }
            rideProtected = false;
            var feet = Physics.Raycast(onion + Vector3.up * 1f, Vector3.down, out var hit, 60f, UnityHost.Mask,
                QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.5f ? hit.point : onion;
            if (IsRemote)
            {
                rideMove = Vector2.zero;
                rideBoost = rideExit = false;
                if (Body && Body.netId.Value != 0) PikminNet.SendRideEnd(Body.netId.Value, feet, safe);
                return;
            }
            RideEndedHere(feet, safe);
        }

        /// Places him out of the Onion on this machine (his own, where his body is moved).
        internal void RideEndedHere(Vector3 feet, Vector3 safe)
        {
            rideEndedAt = Time.unscaledTime;
            rideMove = Vector2.zero;
            rideBoost = rideExit = false;
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
            var world = PikminWorld.Instance;
            // The press that boarded (B) is in this frame's flags: only a later one climbs out.
            if (!rideWas) rideStartFrame = Time.frameCount;
            rideWas = true;
            if (MenuOpen) CloseMenu();
            rideMove = cam != null ? cam.StickToWorld(pad.move) : pad.move;
            if (pad.boostPressed) rideBoost = true;
            if (pad.anyFacePressed && Time.frameCount > rideStartFrame) rideExit = true;
            // He is wherever the Onion is (on a client, where the host's Onion is drawn).
            if (world && world.RideOnionFoot(ViewCaptain, out var foot)) rideFeet = foot;
            transform.position = rideFeet + feetToOrigin;
        }

        void RideOver() => rideWas = false;

        /// The server takes a remote Olimar's ride controls from his packet.
        void ReceiveRide(in PikminInputPacket p)
        {
            if (!RideActive) return;
            rideMove = new Vector2(p.swarmX, p.swarmY);
            if ((p.buttons & PikminInputPacket.Boost) != 0) rideBoost = true;
            if ((p.buttons & PikminInputPacket.Exit) != 0) rideExit = true;
        }

        void RemoteOnionInput()
        {
            var world = PikminWorld.Instance;
            if (!world || !Active || MenuOpen || RideActive) return;
            if (pad.remoteDump && world.OnionFlying(DataCaptain)) world.ReleaseFlyingOnion(DataCaptain);
            if (pad.remoteFill && world.OnionFlying(DataCaptain)) world.FillFlyingOnion(DataCaptain);
            if (pad.summonPressed && !world.ToggleSummon(DataCaptain, out var why))
            {
                if (Plugin.LogEvents.Value) Plugin.Log.LogInfo($"captain {DataCaptain}: Onion summon refused: {why}");
                world.PlaySound(2, Body.footPosition);
            }
            if (pad.healSummon)
            {
                pad.healSummon = false;
                if (!world.ToggleHealSummon(DataCaptain, out var healWhy))
                {
                    if (Plugin.LogEvents.Value) Plugin.Log.LogInfo($"captain {DataCaptain}: Onion heal call refused: {healWhy}");
                    world.PlaySound(2, Body.footPosition);
                }
            }
        }
    }
}
