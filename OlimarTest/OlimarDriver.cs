using System;
using RoR2;
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
    // without skills.
    internal class OlimarDriver : MonoBehaviour
    {
        public CharacterBody Body { get; private set; }
        CharacterMotor motor;
        InputBankTest input;
        CharacterDirection direction;
        HealthComponent health;

        LocalUser localUser;
        readonly Pad pad = new Pad();
        PikminCamera cam;
        CameraRigController camRig;

        int captain = -1;
        bool warnedClient;

        // Body origin minus feet: the sim tracks Olimar's feet.
        Vector3 feetToOrigin;
        // Body origin at the last two sim ticks, for drawing between them, and where it was
        // last put, to notice when something else (a teleporter, an out-of-bounds rescue)
        // moves the body.
        Vector3 prev, cur, written;
        bool placed;

        /// The last tick's captain, for the cursor and whistle drawing.
        public Native.Captain State;
        public bool HasState;
        public bool Whistling => pad.Whistle;

        // The Onion menu.
        int menuOnion = -1;
        uint menuColour = Native.Red;
        const uint MenuStep = 5;

        /// The sim moves the body (Pikmin scheme, running on the server, sim healthy).
        public bool SimMoves => Plugin.Scheme == ControlScheme.Pikmin && captain >= 0
                                && PikminWorld.Instance && PikminWorld.Instance.Healthy;

        /// Alive, ours to steer, and a captain in the sim.
        public bool Active => captain >= 0 && Body && Body.hasEffectiveAuthority && !Body.currentVehicle
                              && (!health || health.alive);

        public int Captain => captain;

        void Awake()
        {
            Body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
            input = GetComponent<InputBankTest>();
            direction = GetComponent<CharacterDirection>();
            health = GetComponent<HealthComponent>();
        }

        void FixedUpdate()
        {
            if (!Body || !Body.hasEffectiveAuthority) return;
            if (!NetworkServer.active)
            {
                if (!warnedClient)
                    Plugin.Log.LogWarning("Olimar's Pikmin only run on the host for now; as a client he walks like Commando.");
                warnedClient = true;
                return;
            }
            if (captain < 0)
            {
                // Out of the drop pod first: the Onion goes down where he lands.
                if (Body.currentVehicle || (health && !health.alive)) return;
                captain = PikminWorld.Ensure().Register(this);
                if (captain < 0) return;
                feetToOrigin = transform.position - Body.footPosition;
                prev = cur = written = transform.position;
                placed = false;
            }
            bool steer = SimMoves && Active;
            // The sim has him, or hands him back to RoR2 (Aim scheme, the sim stopped).
            if (motor && motor.Motor.enabled == steer && (steer || (health && health.alive)))
                motor.Motor.enabled = !steer;
            if (steer)
            {
                Body.isSprinting = false;
                EnsureCamera();
            }
            else
            {
                ReleaseCamera();
            }
        }

        InteractionDriver interaction;

        // Whether RoR2 shows an interact prompt (`ContextManager` asks the same question): the
        // drop pod's exit, a chest, a shrine...
        bool InteractPrompt()
        {
            if (!interaction && Body) interaction = Body.GetComponent<InteractionDriver>();
            if (!interaction) return false;
            var target = interaction.FindBestInteractableObject();
            if (!target) return false;
            var it = target.GetComponent<IInteractable>();
            return it == null || it.GetInteractability(interaction.interactor) != Interactability.Disabled;
        }

        void Update()
        {
            FindLocalUser();
            if (localUser != null) pad.Read(localUser.inputPlayer, InteractPrompt());
            if (captain < 0) return;

            if (cam != null)
            {
                if (pad.recentre && HasState) cam.Recentre(State.face);
                if (pad.zoom) cam.CycleZoom();
                if (pad.topView) cam.ToggleTop();
            }
            pad.recentre = pad.zoom = pad.topView = false;
            OnionMenu();

            if (SimMoves && placed && Active)
            {
                var world = PikminWorld.Instance;
                float a = world ? world.RenderAlpha : 1f;
                var p = Vector3.Lerp(prev, cur, a);
                transform.position = p;
                written = p;
            }
        }

        // Commando's model is hidden while Pikmin's Olimar is drawn in his place.
        CharacterModel charModel;
        bool bodyHidden;

        public void HideBody(bool hide)
        {
            if (hide == bodyHidden) return;
            if (!charModel && Body && Body.modelLocator && Body.modelLocator.modelTransform)
                charModel = Body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            if (!charModel) return;
            charModel.invisibilityCount += hide ? 1 : -1;
            bodyHidden = hide;
        }

        void OnDestroy()
        {
            if (PikminWorld.Instance) PikminWorld.Instance.Unregister(this);
            ReleaseCamera();
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
                cam.Init(transform, Yaw());
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

        /// This tick's controls, called by PikminWorld just before it advances the sim.
        public Native.Input BuildInput()
        {
            var feet = transform.position - feetToOrigin;
            var i = new Native.Input();
            if (Plugin.Scheme == ControlScheme.Pikmin)
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
                i.dismiss = B(pad.Dismiss);
                // Something else moved the body: put the captain where it is now.
                if (!placed || (transform.position - written).sqrMagnitude > 1f)
                    Pose(ref i, feet, Vector3.zero, Yaw());
            }
            else
            {
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
                if (input.skill4.down)
                {
                    var to = aim - feet;
                    var s = new Vector2(to.x, to.z).normalized;
                    i.swarm_x = s.x;
                    i.swarm_y = s.y;
                }
            }
            return i;
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
            pad.Ticked();
            if (!SimMoves || !Active) return;

            prev = placed ? cur : transform.position;
            cur = UnityHost.ToUnity(c.pos) + feetToOrigin;
            transform.position = cur;
            written = cur;
            placed = true;

            // Navi's velocity is per second. RoR2 reads these for animation, camera and AI.
            motor.velocity = UnityHost.ToUnity(c.vel);
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
        // The Onion menu: stand under an Onion; d-pad (or arrow keys) left/right picks a
        // colour, up calls Pikmin out, down puts the squad's away. Pikmin pauses the game for
        // its window; this doesn't.

        void OnionMenu()
        {
            var world = PikminWorld.Instance;
            menuOnion = world && localUser != null ? world.OnionInReach(captain) : -1;
            if (menuOnion < 0)
            {
                pad.menuUp = pad.menuDown = pad.menuLeft = pad.menuRight = false;
                return;
            }
            uint colours = world.OnionColours(menuOnion);
            if ((colours & (1u << (int)menuColour)) == 0 || pad.menuRight) menuColour = NextColour(colours, menuColour, 1);
            if (pad.menuLeft) menuColour = NextColour(colours, menuColour, 2);
            if (pad.menuUp) world.CallOut(captain, menuOnion, menuColour, MenuStep);
            if (pad.menuDown) world.PutAway(captain, menuOnion, menuColour, MenuStep);
            pad.menuUp = pad.menuDown = pad.menuLeft = pad.menuRight = false;
        }

        // Red → Yellow → Blue, skipping colours the Onion doesn't take.
        static readonly uint[] MenuOrder = { Native.Red, Native.Yellow, Native.Blue };

        static uint NextColour(uint colours, uint from, int step)
        {
            int at = Array.IndexOf(MenuOrder, from);
            for (int k = 1; k <= 3; k++)
            {
                uint c = MenuOrder[(at + k * step) % 3];
                if ((colours & (1u << (int)c)) != 0) return c;
            }
            return from;
        }

        static readonly string[] ColourNames = { "Blue", "Red", "Yellow" };
        GUIStyle menuStyle;

        void OnGUI()
        {
            if (menuOnion < 0 || localUser == null || !PikminWorld.Instance) return;
            if (!PikminWorld.Instance.OnionCounts(menuOnion, captain, menuColour, out var n)) return;
            if (menuStyle == null)
                menuStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            string keys = pad.usingPad ? "d-pad" : "arrow keys";
            string text = $"Onion: {ColourNames[menuColour]} Pikmin\n" +
                          $"{n.in_onion} inside, {n.in_squad} in your squad, {n.on_field}/{n.max_on_field} in the field\n" +
                          $"{keys}: up calls {MenuStep} out, down puts {MenuStep} away, left/right colour";
            float w = 560f, h = 90f;
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - h - 140f, w, h), text, menuStyle);
        }
    }
}
