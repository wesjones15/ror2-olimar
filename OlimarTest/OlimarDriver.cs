using System;
using RoR2;
using Ror2Common;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Olimar's link to the sim, on his body. On the server it registers him as a captain with
    // PikminWorld and, each sim tick, hands over his controls and takes back where he is:
    // - Pikmin scheme: the sim moves him (Navi's own walk, cursor and throw) and RoR2's
    //   KinematicCharacterMotor is off, like MarioTest does it; the pad is read by position
    //   (Pad) and the camera is PikminCamera. Everyone else sees him through RoR2's own
    //   CharacterNetworkTransform.
    // - Aim scheme (VR): RoR2 moves him and the sim follows his pose; the Pikmin cursor is
    //   where he aims and the skill buttons are throw, whistle, dismiss and swarm.
    // Off the server (a client playing Olimar) there's no sim yet: he walks like Commando
    // without skills. An AI Olimar (Goobo Jr.'s) walks by RoR2's motor and throws where it
    // aims, as in the Aim scheme.
    //
    // A teleport (anything that moves him far between ticks) brings his squad with him.
    // RoR2 updates its camera in Update: place the smoothed body before it reads the target.
    [DefaultExecutionOrder(-200)]
    internal partial class OlimarDriver : MonoBehaviour
    {
        public CharacterBody Body { get; private set; }
        CharacterMotor motor;
        InputBankTest input;
        CharacterDirection direction;
        HealthComponent health;
        NativeBody native;
        // His velocity on the last tick in the air, for RoR2's landing event.
        Vector3 airVelocity;
        bool wasOnGround = true;
        bool sprintPressed;
        bool deathSoundPlayed;

        internal void PlayDeathSound()
        {
            if (deathSoundPlayed) return;
            deathSoundPlayed = true;
            SpawnMusic.PlayDeath(Body);
        }

        LocalUser localUser;
        readonly Pad pad = new Pad();
        PikminCamera cam;
        readonly EnemyLock enemyLock = new EnemyLock();
        readonly OlimarBrain brain = new OlimarBrain();
        // The enemy an AI Olimar's brain locked onto this tick.
        CharacterBody brainTarget;
        CameraRigController camRig;

        int captain = -1;
        // On a machine that only shows the server's world: this body's captain there (-1: none), for
        // hiding Commando under Olimar and the beam-in. The server's PikminWorld sends who is who.
        int remoteCaptain = -1;
        bool warnedClient;

        static readonly System.Collections.Generic.List<OlimarDriver> all = new System.Collections.Generic.List<OlimarDriver>();
        public static System.Collections.Generic.IReadOnlyList<OlimarDriver> All => all;

        /// His captain in the world drawn and queried here (the squad, the Onion): the server's, which
        /// on a client is not the id of the small sim that moves him.
        public int ViewCaptain => NetworkServer.active ? captain : remoteCaptain;
        int DataCaptain => ViewCaptain;

        internal void SetRemoteCaptain(int id) => remoteCaptain = id;

        /// Another player's Olimar, and this is the server: his machine moves him, and the server's
        /// captain follows the pose it sends (as in VR).
        public bool IsRemote => NetworkServer.active && Body && !Body.hasEffectiveAuthority && !IsAi;

        /// A player's own Olimar on a machine that isn't the server.
        public bool IsLocalClient => !NetworkServer.active && Body && Body.hasEffectiveAuthority && !IsAi;

        float spawnAge;
        /// The enemy he's locked onto, for the server's captain (the locked body's network id).
        uint lockNetId;
        /// The pellet, corpse or ship part he's locked onto, plus one (0: none), for the same.
        uint lockThing;

        /// What the lock can take besides enemies: the pellets, corpses and ship parts on the ground that can
        /// still take a carrier.
        /// Where a Candypop Bud's mouth is above its foot, in sim units (libpikmin's bulb::HEIGHT times 0.6).
        const float BulbMouthSim = 46f * 0.6f;

        static void CollectLockPoints(System.Collections.Generic.List<EnemyLock.LockPoint> into)
        {
            var world = PikminWorld.Instance;
            if (!world) return;
            for (int n = 0; n < world.PelletCount; n++)
            {
                var p = world.Pellets[n];
                if (p.state > 1 || p.carriers >= p.max_carriers) continue;
                into.Add(new EnemyLock.LockPoint { Id = p.id, Position = UnityHost.ToUnity(p.pos) + Vector3.up * .6f });
            }
            // Candypop Buds that can take a Pikmin: open, with room. Their ids sit above the pellets' (Native.BulbLockBase).
            for (int n = 0; n < world.BulbCount; n++)
            {
                var b = world.Bulbs[n];
                if (b.state != 1 || b.free == 0) continue;
                into.Add(new EnemyLock.LockPoint { Id = Native.BulbLockBase + b.id, Position = UnityHost.ToUnity(b.pos) + Vector3.up * (BulbMouthSim * UnityHost.Scale) });
            }
        }

        // The latest of a client's Olimar's packets (server), with the buttons pressed since the last step.
        PikminInputPacket remotePacket;
        bool haveRemote;
        byte remotePulses;
        // What a local client's Olimar has sent (client).
        byte sentButtons, outPulses;
        int sendCount;
        internal bool Arriving;

        // Body origin minus feet: the sim tracks Olimar's feet.
        Vector3 feetToOrigin;
        // Share the camera-safe smoothing used by Mario, on Pikmin's 30 Hz clock.
        StepSmoother smoother;
        bool placed;

        /// His items' part in the sim (OlimarItems).
        public OlimarItems Items { get; private set; }

        /// Further than this between ticks is a teleport: the squad comes too.
        const float TeleportMetres = 12f;
        // Where he last stood, for putting him back after a fall off the map.
        readonly Unstuck fallGuard = new Unstuck();
        // Keeps him out of walls and slopes his sim let him into (ror2-common's WallGuard, as
        // Mario's and the Melee fighters'): a jump or a knock-back into a log's side. libpikmin
        // finds his floor from one collision radius up (15 units, 0.6 m), so lower lips are the
        // sim's to step up, and the sweep starts above that on his 1 m body.
        readonly WallGuard wallGuard = new WallGuard { StepHeight = 0.6f };
        static readonly float[] SweepHeights = { 0.65f, 0.8f, 0.95f };
        // A put-back for the sim: the next input places only the captain there (the core's host
        // pose), never the squad, unlike pik_sim_teleport_squad.
        Vector3? putBack;
        float guardLogAt;
        Vector3 lastFeet;
        bool lastFeetSet;

        // A jump pad's own throw, recorded from JumpVolume.OnTriggerStay. RoR2 clears the
        // motor's disableAirControlUntilCollision on the next movement hit, so that flag is
        // not a reliable marker by the time the input is built.
        float padLaunchedAt = -100f;
        Vector3 padLaunchVelocity;
        bool padFlight;

        /// The last tick's captain, for the cursor and whistle drawing.
        public Native.Captain State;
        public bool HasState;
        public bool Whistling => pad.Whistle;

        // The Onion shortcut popup is visible only while Olimar stands in its beam.
        int menuOnion = -1;
        int menuOpenedFrame;
        string menuNote;
        bool strikeSwallow;
        FlyingXPolicy flyingX;
        /// Locked onto an enemy this tick (RT held).
        public bool LockedOn { get; private set; }
        public bool MenuOpen => menuOnion >= 0;
        internal bool BlocksEquipment => MenuOpen || RideActive || pad.EquipmentBlocked;
        /// RoR2's interact press is not his: a ride, or RT + X / RT + Y.
        internal bool BlocksInteract => RideActive || pad.BlocksInteract;

        /// The sim moves the body (Pikmin scheme, a player's Olimar, running on the server,
        /// sim healthy).
        public bool SimMoves => Plugin.Scheme == ControlScheme.Pikmin && !IsAi && captain >= 0
                                && PikminWorld.Instance && PikminWorld.Instance.Controllable;

        /// Run by RoR2's AI (Goobo Jr.'s Olimar), not a player.
        public bool IsAi => Body && Body.master && !Body.master.playerCharacterMasterController;

        /// On the players' side (an umbra from Artifact of Vengeance isn't).
        bool OnPlayersTeam => !Body.teamComponent || Body.teamComponent.teamIndex == TeamIndex.Player;

        /// Alive, ours to steer, and a captain in the sim.
        public bool Active => !Arriving && !(ShipRepairSession.Instance &&
                              (ShipRepairSession.Instance.Terminal || ShipRepairSession.Instance.HoldingFirstArrival))
                              && captain >= 0 && Body && (Body.hasEffectiveAuthority || IsRemote)
                              && !Body.currentVehicle && (!health || health.alive);

        public int Captain => captain;

        void Awake()
        {
            Body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
            input = GetComponent<InputBankTest>();
            direction = GetComponent<CharacterDirection>();
            health = GetComponent<HealthComponent>();
            native = GetComponent<NativeBody>();
            smoother = new StepSmoother(transform);
            Items = new OlimarItems(this);
            spawn.Remember(transform);
            all.Add(this);
        }

        readonly GroundGate ground = new GroundGate();
        // A client's body starts where the network blend left it, not at its spawn (ror2-common's ClientSpawn).
        readonly ClientSpawn spawn = new ClientSpawn();

        void FixedUpdate()
        {
            if (!Body) return;
            if (!Body.hasEffectiveAuthority)
            {
                if (IsRemote) RemoteTick();
                return;
            }
            bool server = NetworkServer.active;
            // An AI Olimar (Goobo Jr.'s) lives on the server; and a client needs the assets to move him.
            if (!server && (IsAi || !Plugin.AssetsSettled || !Plugin.AssetsReady)) return;
            if (captain < 0)
            {
                // Register during the Dolphin arrival so the ship and airborne Onion draw together.
                if (Body.currentVehicle || (health && !health.alive) || !OnPlayersTeam) return;
                spawn.Settle(Body, motor);
                // A stage's floor can load after the body appears: wait in place for it, with RoR2's motor
                // off so he doesn't fall meanwhile (it comes back on below when the sim hands him back).
                if (!ground.Ready(Body.footPosition, Time.fixedDeltaTime, "Olimar"))
                {
                    if (motor && motor.Motor.enabled) motor.Motor.enabled = false;
                    return;
                }
                captain = server ? PikminWorld.Ensure().Register(this) : PikminWorld.RegisterLocal(this);
                if (captain < 0) return;
                feetToOrigin = transform.position - Body.footPosition;
                smoother.Reset();
                placed = false;
                // The sim probes what RoR2's own bodies collide with, not just the world layer.
                WorldProbe.UseBodyCollision(motor, gameObject);
            }
            bool steer = Arriving || (SimMoves && Active);
            // The sim has him, or hands him back to RoR2 (Aim scheme, the sim stopped).
            if (motor && motor.Motor.enabled == steer && (steer || (health && health.alive)))
                motor.Motor.enabled = !steer;
            if (steer)
            {
                if (Arriving && native) native.UpdateSprint(false, false, false);
                EnsureCamera();
            }
            else
            {
                smoother.Reset();
                if (native) native.ForgetVelocity();
                placed = false;
                ReleaseCamera();
            }
        }

        InteractionDriver interaction;

        // Whether RoR2 shows an interact prompt: the drop pod's exit, a chest, a shrine...
        bool InteractPrompt() => InteractGuard.PromptShown(Interaction());

        /// RoR2 shows a prompt that isn't this Onion's own (a chest, a shrine, the pod's exit), as of
        /// the last frame. Those keep X; the Onion's prompt doesn't.
        bool realPrompt;

        void Update()
        {
            if (health && !health.alive) PlayDeathSound();
            FindLocalUser();
            if (localUser != null) pad.Read(localUser.inputPlayer, realPrompt);
            sprintPressed |= input && input.sprint.down;
            if (captain < 0) return;

            if (SimMoves && placed && Active && !RideActive)
                smoother.SmoothAt(PikminWorld.Instance.BodyShownTime);

            if (cam != null)
            {
                if (!MenuOpen && !Arriving) cam.Pan(pad.look);
                if (pad.recentre && HasState && !MenuOpen) cam.Recentre(State.face);
                if (pad.zoom) cam.CycleZoom();
                if (pad.topView) cam.ToggleTop();
            }
            pad.recentre = pad.zoom = pad.topView = false;
            if (!RideActive) RideOver();
            if (RideActive)
            {
                RideInput();
                pad.confirm = pad.cancel = pad.releaseAll = pad.returnAll = pad.heal = pad.healSummon = pad.recallStuck = false;
                pad.remoteDump = pad.remoteFill = pad.boostPressed = pad.anyFacePressed = pad.board = pad.summonPressed = false;
                return;
            }
            OnionPrompt();
            if (MenuOpen && Time.frameCount > menuOpenedFrame) MenuInput();
            else if (!MenuOpen) { FlyingXInput(); RemoteOnionInput(); }
            pad.confirm = pad.cancel = pad.releaseAll = pad.returnAll = pad.heal = pad.healSummon = pad.recallStuck = false;
            pad.remoteDump = pad.remoteFill = pad.boostPressed = pad.anyFacePressed = pad.board = pad.summonPressed = false;

        }

        // Commando's model is hidden while Pikmin's Olimar is drawn in his place.
        CharacterModel charModel;
        bool bodyHidden;
        ItemDisplayRig itemDisplays;
        bool itemDisplaysTried;

        public void HideBody(bool hide)
        {
            if (hide == bodyHidden) return;
            if (itemDisplays != null)
            {
                if (hide) { itemDisplays.HideStock(); itemDisplays.SetVisible(true); }
                else { itemDisplays.ShowStock(); itemDisplays.SetVisible(false); }
                bodyHidden = hide;
                return;
            }
            if (!charModel && Body && Body.modelLocator && Body.modelLocator.modelTransform)
                charModel = Body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            if (!charModel) return;
            charModel.invisibilityCount += hide ? 1 : -1;
            bodyHidden = hide;
        }

        /// Places RoR2's item displays on the captain's drawn body (PikminView calls this each
        /// frame the captain is drawn, with the draw's feet, turn and units).
        public void PoseItems(IntPtr view, Vector3 at, Quaternion rotation, float units)
        {
            if (!ItemDisplayRig.Enabled || !Body) return;
            if (itemDisplays == null)
            {
                if (itemDisplaysTried) return;
                itemDisplaysTried = true;
                itemDisplays = ItemDisplayRig.Attach(Body, new OlimarItemAnchors(view, (uint)ViewCaptain));
                // A captain hidden before the rig existed was hidden through invisibilityCount,
                // which also hides every item display. The rig's own stock hide replaces it.
                if (itemDisplays != null && bodyHidden && charModel != null)
                {
                    charModel.invisibilityCount--;
                    charModel = null;
                }
            }
            itemDisplays?.Pose(at, rotation, units);
        }

        void OnDestroy()
        {
            itemDisplays?.Dispose(); itemDisplays = null;
            if (health && !health.alive) PlayDeathSound();
            all.Remove(this);
            if (PikminWorld.Instance) PikminWorld.Instance.Unregister(this);
            ReleaseCamera();
            InteractTarget.Override(interaction, null);
            if (panelTex) Destroy(panelTex);
            if (stripeTex) Destroy(stripeTex);
            DisposeBottomHud();
            DisposeMenuGlyphs();
        }

        void FindLocalUser()
        {
            if (localUser != null && localUser.cachedBody == Body) return;
            localUser = null;
            foreach (var u in LocalUserManager.readOnlyLocalUsersList)
                if (u != null && u.cachedBody == Body)
                {
                    localUser = u;
                    break;
                }
        }

        void EnsureCamera()
        {
            if (localUser == null) return;
            var rig = localUser.cameraRigController;
            if (!rig) return;
            if (!cam)
            {
                cam = gameObject.AddComponent<PikminCamera>();
                cam.Init(transform, Yaw() + Mathf.PI * 0.5f);
            }
            if (rig != camRig || !rig.IsOverrideCam(cam))
            {
                rig.SetOverrideCam(cam, 0.5f);
                camRig = rig;
            }
        }

        void ReleaseCamera()
        {
            if (camRig && cam && camRig.IsOverrideCam(cam))
                camRig.SetOverrideCam(null, 0.5f);
            camRig = null;
        }

        float Yaw() => UnityHost.Yaw(direction ? direction.forward : transform.forward);

        /// A pad fired under him (PikminWorld.JumpPads): keep its own throw for the next input.
        public void NoteJumpPad(JumpVolume pad)
        {
            if (!pad || !NetworkServer.active || padFlight) return;
            var v = pad.alignJumpVelocityToTransform
                ? pad.transform.up * pad.jumpVelocity.magnitude
                : pad.jumpVelocity;
            padLaunchVelocity = v;
            padLaunchedAt = Time.fixedTime;
        }

        // Replays a host launch (a pad's own throw, or another external velocity) in the sim.
        void Launch(PikminWorld world, Vector3 thrown, bool padLaunch)
        {
            if (padLaunch) padFlight = true;
            var v = UnityHost.ToSim(thrown);
            // The motor's own gravity (Physics.gravity times its scale/multiplier) is what a
            // pad's authored throw was built for.
            float padGravity = Mathf.Abs(Physics.gravity.y) * native.GravityScale / UnityHost.Scale;
            Native.pik_sim_launch_captain(world.SimHandle, (uint)captain, ref v,
                padLaunch ? padGravity : 0f, B(padLaunch));
            if (padLaunch)
                Plugin.Log.LogInfo($"jump pad: {thrown.x:0.##},{thrown.y:0.##},{thrown.z:0.##} m/s " +
                    $"({v.x:0},{v.y:0},{v.z:0} sim/s); gravity {padGravity:0.#} sim/s^2, motor scale {native.GravityScale:0.##}");
        }

        /// This tick's controls, called by PikminWorld just before it advances the sim.
        public Native.Input BuildInput()
        {
            var world = PikminWorld.Instance;
            var hold = native ? native.Control() : NativeBody.Hold.None;
            Native.pik_sim_set_captain_hold(world.SimHandle, (uint)captain, (uint)hold);
            var feet = transform.position - feetToOrigin;
            var i = new Native.Input();
            if (Plugin.Scheme == ControlScheme.Pikmin && !IsAi)
            {
                var stick = cam != null ? cam.StickToWorld(pad.move) : pad.move;
                i.stick_x = stick.x;
                i.stick_y = stick.y;
                Vector2 swarm = cam != null ? cam.StickToWorld(pad.swarm) : pad.swarm;
                if (pad.swarmToCursor && HasState)
                {
                    var to = UnityHost.ToUnity(State.cursor_world) - feet;
                    swarm = new Vector2(to.x, to.z).normalized;
                }
                i.swarm_x = swarm.x;
                i.swarm_y = swarm.y;
                i.throw_ = B(pad.Throw);
                i.whistle = B(pad.Whistle);
                i.dismiss = B(pad.Strike);
                i.cycle = B(pad.Cycle);
                i.jump = B(pad.Jump);
                i.jump_blocked = B(pad.jumpBlocked);
                // Follow the camera crosshair on the ground. Keep the free cursor at least
                // one metre from Olimar, so looking straight down can still aim close.
                // The sim keeps the existing ThrowReach maximum and finds the ground height.
                var sceneCam = camRig ? camRig.sceneCam : null;
                if (sceneCam)
                {
                    var ray = sceneCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    bool hitGround = Physics.Raycast(ray, out var hit, 60f, UnityHost.Mask,
                        QueryTriggerInteraction.Ignore);
                    var aim = hitGround ? hit.point : ray.GetPoint(60f);
                    const float minRadius = 1f;
                    var offset = new Vector2(aim.x - feet.x, aim.z - feet.z);
                    if (offset.sqrMagnitude < minRadius * minRadius)
                    {
                        if (offset.sqrMagnitude < 0.000001f)
                            offset = new Vector2(ray.direction.x, ray.direction.z);
                        if (offset.sqrMagnitude < 0.000001f)
                        {
                            var forward = UnityHost.Forward(Yaw());
                            offset = new Vector2(forward.x, forward.z);
                        }
                        offset = offset.normalized * minRadius;
                        aim.x = feet.x + offset.x;
                        aim.z = feet.z + offset.y;
                    }
                    i.cursor = UnityHost.ToSim(aim);
                    i.use_cursor = 1;
                }
                // Something else moved the body: put the captain where it is now.
                if (RideActive)
                {
                    // Inside the Onion: the captain goes where it goes, and nothing else happens.
                    Pose(ref i, rideFeet, Vector3.zero, Yaw());
                    i.stick_x = i.stick_y = i.swarm_x = i.swarm_y = 0f;
                    i.throw_ = i.whistle = i.dismiss = i.cycle = i.jump = 0;
                    i.use_cursor = 0;
                    putBack = null;
                }
                else if (native && padLaunchedAt >= 0f && Time.fixedTime - padLaunchedAt <= 0.25f)
                {
                    // The pad's own throw takes precedence over a smoother reset: posing him
                    // here would zero the velocity the pad just gave him.
                    var thrown = padLaunchVelocity;
                    padLaunchedAt = -1f;
                    smoother.Reset();
                    native.ForgetVelocity();
                    Launch(world, thrown, true);
                    putBack = null;
                }
                else if (native && !padFlight && native.JumpPadLaunch && native.TakeExternalVelocity(out var padThrown, 2f))
                {
                    // The same pad, seen through the motor's flag instead of the hook.
                    smoother.Reset();
                    Launch(world, padThrown, true);
                    putBack = null;
                }
                else if (!placed || smoother.MovedElsewhere)
                {
                    smoother.Reset();
                    if (native) native.ForgetVelocity();
                    Pose(ref i, feet, Vector3.zero, Yaw());
                    if (placed && HasState) Teleported(world, UnityHost.ToUnity(State.pos), feet);
                    putBack = null;
                }
                else if (putBack.HasValue)
                {
                    Pose(ref i, putBack.Value, Vector3.zero, Yaw());
                    putBack = null;
                }
                else if (native && native.TakeExternalVelocity(out var thrown, 2f))
                {
                    bool padLaunch = native.JumpPadLaunch;
                    // While a pad flight is running, its own repeated trigger reports are not
                    // new launches. A knock-back during it still lands (padLaunch is false and
                    // the flight is short).
                    if (!(padLaunch && padFlight)) Launch(world, thrown, padLaunch);
                }
                // Walking out of the beam dismisses the shortcut popup.
                if (MenuOpen)
                {
                    i.swarm_x = i.swarm_y = 0f;
                    // Jump is his own under the beam; only the Pikmin commands wait.
                    i.throw_ = i.whistle = i.dismiss = i.cycle = 0;
                }
            }
            else
            {
                if (lastFeetSet) Teleported(world, lastFeet, feet);
                Pose(ref i, feet, motor ? motor.velocity : Vector3.zero, Yaw());
                var ray = input.GetAimRay();
                var aim = Physics.Raycast(ray, out var h, 60f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                    ? h.point
                    : ray.GetPoint(20f);
                i.cursor = UnityHost.ToSim(aim);
                i.use_cursor = 1;
                i.throw_ = B(input.skill1.down);
                i.whistle = B(input.skill2.down);
                i.dismiss = B(input.skill3.down);
                // An AI's Olimar works his own controls (OlimarBrain).
                brainTarget = null;
                if (IsAi)
                {
                    var c = brain.Think(Body, world, captain, State, HasState, 1f / Mathf.Max(1u, Native.pik_tick_hz()));
                    i.throw_ = B(c.throw_);
                    i.whistle = B(c.whistle);
                    if (c.hasCursor) i.cursor = UnityHost.ToSim(c.cursor);
                    brainTarget = c.target;
                }
                if (input.skill4.down && !MenuOpen)
                {
                    var to = aim - feet;
                    var s = new Vector2(to.x, to.z).normalized;
                    i.swarm_x = s.x;
                    i.swarm_y = s.y;
                }
                if (MenuOpen) i.throw_ = i.whistle = i.dismiss = 0;
                // RoR2's AI uses its utility to dodge and its special at range: no dismissing
                // or swarming for it.
                if (IsAi) i.dismiss = 0;
                if (IsAi) i.swarm_x = i.swarm_y = 0f;
            }
            lastFeet = feet;
            lastFeetSet = true;
            if (hold != NativeBody.Hold.None)
            {
                i.stick_x = i.stick_y = 0f;
                i.jump = 0;
            }
            if (hold == NativeBody.Hold.All)
            {
                i.swarm_x = i.swarm_y = 0f;
                i.throw_ = i.whistle = i.dismiss = i.cycle = 0;
            }
            if (native)
                native.UpdateSprint(sprintPressed, i.stick_x * i.stick_x + i.stick_y * i.stick_y > 0.01f,
                    hold == NativeBody.Hold.None && !MenuOpen);
            sprintPressed = false;
            // Update handles held X and double taps; interactions claim the press first.
            // The Pikmin scheme's strike is its own button; only the aim scheme shares one with the Onion.
            if (!(Plugin.Scheme == ControlScheme.Pikmin && !IsAi) &&
                (InteractPrompt() || PikminWorld.Instance.OnionFlying(DataCaptain))) i.dismiss = 0;
            enemyLock.Range = Plugin.LockRange.Value;
            // The strike's cooldown is the Special slot's: no charge, no strike. A press made while it
            // recharges stays spent until the button is let go, so it doesn't fire the moment it is ready.
            if (!IsAi && Plugin.StrikeDamage.Value > 0f)
            {
                if (strikeSwallow)
                {
                    if (i.dismiss != 0) i.dismiss = 0;
                    else strikeSwallow = false;
                }
                else if (i.dismiss != 0 && !Items.StrikeReady())
                {
                    i.dismiss = 0;
                    strikeSwallow = Plugin.Scheme == ControlScheme.Pikmin ? pad.Strike : input.skill3.down;
                }
            }
            enemyLock.Points = CollectLockPoints;
            bool onThing = false;
            uint thingId = 0;
            var locked = IsAi ? brainTarget : enemyLock.Update(Body,
                Plugin.Scheme == ControlScheme.Pikmin && Plugin.Camera.Value == CameraMode.Orbit
                    && pad.lockHeld && !MenuOpen && !Arriving && !RideActive,
                pad.cycleTarget, pad.targetDirection, out onThing, out thingId);
            pad.cycleTarget = false;
            LockedOn = locked || onThing;
            Native.pik_sim_set_lock_target(PikminWorld.Instance.SimHandle, (uint)captain,
                locked ? unchecked((ulong)(uint)locked.GetInstanceID()) : 0, B(locked));
            Native.pik_sim_set_lock_pellet(PikminWorld.Instance.SimHandle, (uint)captain, thingId, B(onThing));
            lockNetId = locked ? locked.netId.Value : 0;
            lockThing = onThing ? thingId + 1 : 0;
            return i;
        }

        // ---- another player's Olimar, on the server ---------------------------------------------

        // The server's copy of a client's Olimar gets his captain once the body is up (and, for a fresh
        // body, once RoR2's arrival state has reached this machine, so the Dolphin comes down for him too).
        void RemoteTick()
        {
            if (captain >= 0 || Body.currentVehicle || (health && !health.alive) || !OnPlayersTeam) return;
            spawnAge += Time.fixedDeltaTime;
            if (!Arriving && spawnAge < 2f) return;
            captain = PikminWorld.Ensure().Register(this);
            if (captain >= 0) feetToOrigin = transform.position - Body.footPosition;
        }

        /// A packet from that Olimar's machine.
        internal void ReceiveInput(in PikminInputPacket p)
        {
            remotePulses |= p.buttons;
            remotePacket = p;
            haveRemote = true;
            ReceiveRide(p);
        }

        /// The server's captain's controls this tick: the pose and cursor his own machine's sim has him at, and
        /// the buttons it saw since the last step.
        internal Native.Input RemoteInput()
        {
            var world = PikminWorld.Instance;
            var hold = native ? native.Control() : NativeBody.Hold.None;
            Native.pik_sim_set_captain_hold(world.SimHandle, (uint)captain, (uint)hold);
            var i = new Native.Input();
            var feet = Body.footPosition;
            if (!haveRemote)
            {
                // Nothing from him yet: stay where the body is.
                Pose(ref i, feet, Vector3.zero, Yaw());
                return i;
            }
            var p = remotePacket;
            i.use_pose = 1;
            i.pose_pos = new Native.Vec3(p.px, p.py, p.pz);
            i.pose_vel = new Native.Vec3(p.vx, p.vy, p.vz);
            i.pose_face = p.face;
            i.cursor = new Native.Vec3(p.cx, p.cy, p.cz);
            i.use_cursor = 1;
            byte b = (byte)(remotePulses | p.buttons);
            remotePulses = 0;
            i.whistle = B((b & PikminInputPacket.Whistle) != 0);
            i.throw_ = B((b & PikminInputPacket.Throw) != 0);
            i.dismiss = B((b & PikminInputPacket.Dismiss) != 0);
            i.cycle = B((b & PikminInputPacket.Cycle) != 0);
            // While he rides, the stick steers the Onion and does not swarm.
            i.swarm_x = RideActive ? 0f : p.swarmX;
            i.swarm_y = RideActive ? 0f : p.swarmY;
            // The enemy he locked onto, by this machine's id for it.
            ulong creature = 0;
            if (p.lockNetId != 0)
            {
                var go = Ror2Common.Mp.Find(p.lockNetId);
                var target = go ? go.GetComponent<CharacterBody>() : null;
                if (target) creature = unchecked((ulong)(uint)target.GetInstanceID());
            }
            Native.pik_sim_set_lock_target(world.SimHandle, (uint)captain, creature, B(creature != 0));
            Native.pik_sim_set_lock_pellet(world.SimHandle, (uint)captain, p.lockPellet - 1, B(p.lockPellet != 0));
            // A teleport (anything that moves him far between ticks) brings his squad with him.
            var at = UnityHost.ToUnity(i.pose_pos);
            if (lastFeetSet) Teleported(world, lastFeet, at);
            lastFeet = at;
            lastFeetSet = true;
            if (hold == NativeBody.Hold.All) i.throw_ = i.whistle = i.dismiss = i.cycle = 0;
            return i;
        }

        // ---- a client's own Olimar ---------------------------------------------------------------

        /// After this tick of his local sim: his pose and buttons go to the server, every other tick or at once
        /// when a button changes. Presses between packets ride along in the next.
        internal void SendInput(in Native.Input i)
        {
            if (!Ror2Common.Mp.Networked || !Body || Body.netId.Value == 0 || !HasState)
            {
                outPulses = 0;
                return;
            }
            byte b = (byte)((i.whistle != 0 ? PikminInputPacket.Whistle : 0) | (i.throw_ != 0 ? PikminInputPacket.Throw : 0)
                | (i.dismiss != 0 ? PikminInputPacket.Dismiss : 0) | (i.cycle != 0 ? PikminInputPacket.Cycle : 0));
            bool riding = RideActive;
            if (riding)
            {
                // The host flies the Onion: the stick and the one-shot boost and exit go with every packet.
                if (rideBoost) { outPulses |= PikminInputPacket.Boost; rideBoost = false; }
                if (rideExit) { outPulses |= PikminInputPacket.Exit; rideExit = false; }
            }
            outPulses |= b;
            if (!riding && (++sendCount & 1) != 0 && b == sentButtons) return;
            sentButtons = b;
            var c = State;
            var packet = new PikminInputPacket
            {
                netId = Body.netId.Value,
                px = c.pos.x, py = c.pos.y, pz = c.pos.z, vx = c.vel.x, vy = c.vel.y, vz = c.vel.z, face = c.face,
                cx = c.cursor_world.x, cy = c.cursor_world.y, cz = c.cursor_world.z,
                buttons = outPulses, swarmX = riding ? rideMove.x : i.swarm_x, swarmY = riding ? rideMove.y : i.swarm_y, lockNetId = lockNetId, lockPellet = lockThing,
            };
            outPulses = 0;
            PikminNet.SendInput(packet);
        }

        /// The Onion command buttons, for the server to carry out.
        internal void SendOnion(byte kind, uint colour, uint count)
        {
            if (!Body || Body.netId.Value == 0) return;
            PikminNet.SendOnion(new OnionCommandPacket { netId = Body.netId.Value, kind = kind, colour = (byte)colour, count = count });
        }

        /// The sim's pose for the replica's overlay: where the smoothed body's feet are.
        internal Vector3 SmoothedFeet => transform.position - feetToOrigin;

        // The visible captain must use the same position as the camera target. The native
        // view's independent 30 Hz blend can finish early, or skip ticks at low frame rates.
        internal bool DrawAtSmoothedFeet => placed && SimMoves && Active && !IsRemote && !RideActive;

        // Moved from `from` to `to` since the last tick: if that's a teleport, the squad follows.
        void Teleported(PikminWorld world, Vector3 from, Vector3 to)
        {
            if ((to - from).sqrMagnitude < TeleportMetres * TeleportMetres) return;
            var p = UnityHost.ToSim(to);
            Native.pik_sim_teleport_squad(world.SimHandle, (uint)captain, ref p);
            if (Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"captain {captain} moved {(to - from).magnitude:F0} m at once; his squad came along");
        }

        static void Pose(ref Native.Input i, Vector3 feet, Vector3 vel, float face)
        {
            i.use_pose = 1;
            i.pose_pos = UnityHost.ToSim(feet);
            var v = vel / UnityHost.Scale;
            i.pose_vel = new Native.Vec3(v.x, v.y, v.z);
            i.pose_face = face;
        }

        /// A tick ran: this is where the sim has Olimar now.
        public void Ticked(Native.Captain c)
        {
            State = c;
            HasState = true;
            // A pad flight ends when he lands; the pad can launch him again after that.
            if (c.on_ground != 0) { padFlight = false; padLaunchedAt = -1f; }
            pad.Ticked();
            Items.Ticked(c);
            if (IsRemote || !SimMoves || !Active) return;
            if (RideActive)
            {
                transform.position = rideFeet + feetToOrigin;
                smoother.Reset();
                if (native) native.ForgetVelocity();
                placed = true;
                return;
            }

            // Recover at Mario's early clear-void point (or a valid stage zone),
            // preserving long descents onto lower ground and bringing the squad.
            var feet = UnityHost.ToUnity(c.pos);
            float height = UnityHost.NaviHeight * UnityHost.Scale, radius = Mathf.Max(0.1f, Body.radius);
            Unstuck.GroundProbe stand = (Vector3 s, float rise, float drop, out Vector3 f) =>
                WorldProbe.StandingGround(s, rise, drop, height, radius, out f);
            fallGuard.Step(feet, false, c.on_ground != 0, false, Time.fixedDeltaTime, height, stand, out _);
            if (fallGuard.FellEarly(feet, height, stand, out var back) || fallGuard.FellTooLong(feet, height, stand, out back))
            {
                var at = UnityHost.ToSim(back);
                var world = PikminWorld.Instance;
                Native.pik_sim_teleport_squad(world.SimHandle, (uint)captain, ref at);
                transform.position = back + feetToOrigin;
                smoother.Reset();
                if (native) native.ForgetVelocity();
                fallGuard.Reset();
                wallGuard.Placed(back);
                putBack = null;
                Plugin.Log.LogInfo($"Olimar fell off the map at {feet}; back on the surface at {back} with his squad");
                return;
            }
            // Standing or walking inside geometry (sunk into a log, jammed between rocks), which
            // the fall checks never see: two seconds wedged and he is put on clear ground nearby
            // with his squad (ror2-common's Unstuck.Wedged).
            if (fallGuard.Wedged(feet, height, radius, false, Time.fixedDeltaTime, stand, out var unwedged))
            {
                var at = UnityHost.ToSim(unwedged);
                Native.pik_sim_teleport_squad(PikminWorld.Instance.SimHandle, (uint)captain, ref at);
                transform.position = unwedged + feetToOrigin;
                smoother.Reset();
                if (native) native.ForgetVelocity();
                wallGuard.Placed(unwedged);
                putBack = null;
                Plugin.Log.LogInfo($"Olimar was wedged in geometry at {feet}; out at {unwedged} with his squad");
                return;
            }
            // The step his sim just took went through a wall or into a slope (a jump, a knock-back),
            // or sank him half his height under a floor: undo it before he falls in. Only he is put
            // back; the pose reaches the sim with the next input. Knocked down is a throw, swept
            // more closely.
            if (wallGuard.Step(feet, height, SweepHeights, UnityHost.WallNormalY, c.down != 0, false,
                out var guardBack, out var why))
            {
                putBack = guardBack;
                transform.position = guardBack + feetToOrigin;
                smoother.Reset();
                if (native) native.ForgetVelocity();
                if (Time.time >= guardLogAt)
                {
                    guardLogAt = Time.time + 1f;
                    Plugin.Log.LogInfo($"Olimar put back at {guardBack}: {why}");
                }
                return;
            }

            // Leave the body at the start of the blend, never ahead of the drawn model.
            smoother.Stepped(UnityHost.ToUnity(c.pos) + feetToOrigin,
                PikminWorld.Instance ? PikminWorld.Instance.BodyTickTime : 0.0);
            placed = true;

            // Navi's velocity is per second. RoR2 reads these for animation, camera and AI.
            if (native)
            {
                native.WroteVelocity(UnityHost.ToUnity(c.vel));
                // Touching down raises RoR2's landing event with the speed he came down at
                // (Mycelium Cap's spores); standing, it just ends a jump pad's hold.
                bool onGround = c.on_ground != 0;
                if (onGround && !wasOnGround) native.Landed(airVelocity);
                else if (onGround) native.Landed();
                else airVelocity = UnityHost.ToUnity(c.vel);
                wasOnGround = onGround;
            }
            else motor.velocity = UnityHost.ToUnity(c.vel);
            motor.Motor.GroundingStatus.IsStableOnGround = c.on_ground != 0;
            motor.Motor.GroundingStatus.FoundAnyGround = c.on_ground != 0;
            if (direction)
            {
                var facing = UnityHost.Forward(c.face);
                direction.forward = facing;
                direction.moveVector = UnityHost.ToUnity(c.vel).sqrMagnitude > 0.01f ? facing : Vector3.zero;
            }
        }

        static byte B(bool v) => v ? (byte)1 : (byte)0;

        // ------------------------------------------------------------------------------------
        // The Onion's beam offers direct actions. Olimar may walk out
        // while the popup is visible; RoR2 and his Pikmin keep running.

        InteractionDriver Interaction()
        {
            if (!interaction && Body) interaction = Body.GetComponent<InteractionDriver>();
            return interaction;
        }

        void FlyingXInput()
        {
            var world = PikminWorld.Instance;
            if (!world || !Active) { flyingX.Cancel(); return; }
            bool flying = world.OnionFlying(DataCaptain);
            var action = flyingX.Step(Time.unscaledTime, pad.RelocateHeld, realPrompt || InteractPrompt());
            if (action == FlyingXAction.Land) world.CommandOnion(DataCaptain, !flying);
        }

        // Show the shortcut popup automatically in the Onion beam. Real RoR2
        // interact prompts still have first claim on X.
        void OnionPrompt()
        {
            var world = PikminWorld.Instance;
            var driver = Interaction();
            InteractTarget.Override(driver, null);
            realPrompt = InteractGuard.PromptShown(driver);
            int onion = !realPrompt && world && localUser != null && Active
                ? world.OnionInReach(DataCaptain) : -1;
            if (onion < 0) { if (MenuOpen) CloseMenu(); }
            else if (menuOnion != onion) OpenOnionMenu(onion);
        }

        /// Onion `onion` has a beam Olimar can stand in now.
        public bool CanOpenOnion(int onion) =>
            onion >= 0 && !MenuOpen && Active && PikminWorld.Instance && PikminWorld.Instance.OnionInReach(DataCaptain) == onion;

        void OpenOnionMenu(int onion)
        {
            if (!CanOpenOnion(onion)) return;
            menuOnion = onion;
            menuNote = null;
            menuOpenedFrame = Time.frameCount;
            flyingX.Cancel();
            PikminWorld.Instance.PlaySound(0, Body.footPosition);
            pad.confirm = pad.cancel = pad.releaseAll = pad.returnAll = pad.heal = pad.recallStuck = false;
        }

        void CloseMenu()
        {
            if (MenuOpen && PikminWorld.Instance && Body)
                PikminWorld.Instance.PlaySound(3, Body.footPosition);
            menuOnion = -1;
            pad.Swallow();
        }

        void MenuInput()
        {
            var world = PikminWorld.Instance;
            if (!world || !Active || world.OnionInReach(DataCaptain) != menuOnion)
            {
                CloseMenu();
                return;
            }
            if (pad.releaseAll || pad.returnAll)
            {
                byte kind = pad.releaseAll ? OnionCommandPacket.ReleaseAll : OnionCommandPacket.ReturnAll;
                pad.releaseAll = pad.returnAll = false;
                if (world.ManageOnion(DataCaptain, kind)) menuNote = null;
                else { menuNote = "Stand in the Onion beam to manage Pikmin."; world.PlaySound(2, Body.footPosition); }
                return;
            }
            if (pad.heal)
            {
                pad.heal = false;
                world.HealFromOnion(DataCaptain);
                return;
            }
            if (pad.recallStuck)
            {
                pad.recallStuck = false;
                world.RecallStuckCarriers(DataCaptain);
                return;
            }
            if (pad.board || pad.summonPressed)
            {
                // LT boards the Onion.
                pad.board = pad.summonPressed = false;
                if (RideRecentlyEnded) return;
                if (!world.BeginRide(DataCaptain, out var why))
                {
                    menuNote = why;
                    world.PlaySound(2, Body.footPosition);
                }
                return;
            }
            if (pad.confirm)
            {
                pad.confirm = false;
                if (world.OnionFlying(DataCaptain))
                {
                    menuNote = "Land the Onion to send its plucker.";
                    world.PlaySound(2, Body.footPosition);
                    return;
                }
                if (!world.TriggerOnionPlucker(DataCaptain))
                    menuNote = "No ready sprouts near the Onion.";
                else menuNote = null;
            }
        }

        Texture2D panelTex, stripeTex;

        void MenuStyles()
        {
            if (panelTex) return;
            panelTex = Solid(new Color(0.05f, 0.06f, 0.08f, 0.88f));
            stripeTex = Solid(Color.white);
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // Each Olimar stage entry (outside Ship Repair, whose HUD draws its own) opens with the
        // stage's name in Pikmin 2's area lettering, like the disc's day-intro cards.
        static Stage bannerStage;
        static SceneDef bannerScene;
        static float bannerUntil;

        void StageEntryBanner()
        {
            if (ShipRepairMode.Active || localUser == null) return;
            var scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (Stage.instance && scene && (Stage.instance != bannerStage || scene != bannerScene))
            {
                bannerStage = Stage.instance;
                bannerScene = scene;
                bannerUntil = Time.unscaledTime + 5f;
            }
            if (!scene || Time.unscaledTime >= bannerUntil) return;
            ShipRepairUi.LocationBanner(Language.GetString(scene.nameToken),
                string.IsNullOrEmpty(scene.subtitleToken) ? null : Language.GetString(scene.subtitleToken),
                bannerUntil);
        }

        void OnGUI()
        {
            // RoR2's pause menu is up: nothing of ours is drawn over it.
            if (PauseMenu.IsOpen)
            {
                HideMenuGlyphs();
                return;
            }
            StageEntryBanner();
            BottomHud();
            if (!MenuOpen || localUser == null || !PikminWorld.Instance)
            {
                HideMenuGlyphs();
                return;
            }
            // Panel and text are one uGUI canvas (MenuGlyphs), so the text sits over its panel.
            ShowMenuGlyphs(menuNote);
        }
    }
}

