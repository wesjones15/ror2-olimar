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
        readonly EnemyLock enemyLock = new EnemyLock();
        CameraRigController camRig;

        int captain = -1;
        bool warnedClient;
        internal bool Arriving;

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

        // The Onion window: which Onion it's open on (-1 closed), the colour shown and how many
        // to call out (> 0) or put away (< 0) on confirm.
        int menuOnion = -1;
        uint menuColour = Native.Red;
        int menuDelta, menuChoice;
        bool onionPressClaimed;
        int lastV, lastH;
        float repeatAt;
        public bool MenuOpen => menuOnion >= 0;

        /// The sim moves the body (Pikmin scheme, running on the server, sim healthy).
        public bool SimMoves => Plugin.Scheme == ControlScheme.Pikmin && captain >= 0
                                && PikminWorld.Instance && PikminWorld.Instance.Healthy;

        /// Alive, ours to steer, and a captain in the sim.
        public bool Active => !Arriving && captain >= 0 && Body && Body.hasEffectiveAuthority && !Body.currentVehicle
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
                // Register during the Dolphin arrival so the ship and airborne Onion draw together.
                if (Body.currentVehicle || (health && !health.alive)) return;
                captain = PikminWorld.Ensure().Register(this);
                if (captain < 0) return;
                feetToOrigin = transform.position - Body.footPosition;
                prev = cur = written = transform.position;
                placed = false;
            }
            bool steer = Arriving || (SimMoves && Active);
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

        // Whether RoR2 shows an interact prompt: the drop pod's exit, a chest, a shrine...
        bool InteractPrompt() => InteractGuard.PromptShown(Interaction());

        void Update()
        {
            FindLocalUser();
            if (localUser != null) pad.Read(localUser.inputPlayer, InteractPrompt());
            if (captain < 0) return;

            if (cam != null)
            {
                if (!MenuOpen && !Arriving) cam.Pan(pad.look);
                if (pad.recentre && HasState) cam.Recentre(State.face);
                if (pad.zoom) cam.CycleZoom();
                if (pad.topView) cam.ToggleTop();
            }
            pad.recentre = pad.zoom = pad.topView = false;
            bool openingMenu = !MenuOpen && pad.onionMenu && Active && PikminWorld.Instance;
            if (openingMenu) OpenOnionMenu(PikminWorld.Instance.OnionInReach(captain));
            pad.onionMenu = false;
            OnionPrompt();
            if (MenuOpen && !openingMenu) MenuInput();
            pad.confirm = pad.cancel = false;

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
            InteractTarget.Override(interaction, null);
            if (panelTex) Destroy(panelTex);
            if (stripeTex) Destroy(stripeTex);
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
                i.cycle = B(pad.Cycle);
                i.jump = B(pad.Jump);
                i.jump_blocked = B(pad.jumpBlocked);
                // Something else moved the body: put the captain where it is now.
                if (!placed || (transform.position - written).sqrMagnitude > 1f)
                    Pose(ref i, feet, Vector3.zero, Yaw());
                // The Onion window has the stick and buttons.
                if (MenuOpen)
                {
                    i.stick_x = i.stick_y = i.swarm_x = i.swarm_y = 0f;
                    i.throw_ = i.whistle = i.dismiss = i.cycle = i.jump = 0;
                }
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
                if (input.skill4.down && !MenuOpen)
                {
                    var to = aim - feet;
                    var s = new Vector2(to.x, to.z).normalized;
                    i.swarm_x = s.x;
                    i.swarm_y = s.y;
                }
                if (MenuOpen) i.throw_ = i.whistle = i.dismiss = 0;
            }
            // A utility/X press places a following Onion and stays consumed until release.
            // RoR2 interactions have first claim on the button.
            bool dismiss = Plugin.Scheme == ControlScheme.Pikmin ? pad.Dismiss : input.skill3.down;
            if (!dismiss) onionPressClaimed = false;
            if (!MenuOpen && dismiss && !InteractPrompt() && PikminWorld.Instance.OnionFlying(captain))
            {
                if (!onionPressClaimed) PikminWorld.Instance.CommandOnion(captain, false);
                onionPressClaimed = true;
            }
            if (onionPressClaimed || InteractPrompt()) i.dismiss = 0;
            var locked = enemyLock.Update(Body,
                Plugin.Scheme == ControlScheme.Pikmin && Plugin.Camera.Value == CameraMode.Orbit
                    && pad.lockHeld && !MenuOpen && !Arriving,
                pad.cycleTarget, pad.targetDirection);
            pad.cycleTarget = false;
            Native.pik_sim_set_lock_target(PikminWorld.Instance.SimHandle, (uint)captain,
                locked ? unchecked((ulong)(uint)locked.GetInstanceID()) : 0, B(locked));
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
        // The Onion menu (NaviContainerState, naviState.cpp). Under an Onion RoR2 shows its own
        // interact prompt (an InteractTarget, as his InteractionDriver's override), and the
        // interact button opens a window: up/down sets how many Pikmin come out of the Onion
        // (or go in), left/right the colour of a shared Onion, A confirms, B cancels. Pikmin
        // pauses the game for it; RoR2 can't (others keep playing), so Olimar stands still and
        // his Pikmin keep going meanwhile.

        InteractionDriver Interaction()
        {
            if (!interaction && Body) interaction = Body.GetComponent<InteractionDriver>();
            return interaction;
        }

        // Points RoR2's interact prompt at the Onion in reach, or takes it back.
        void OnionPrompt()
        {
            var world = PikminWorld.Instance;
            InteractTarget want = null;
            // Chests, shrines and other RoR2 interactions keep priority over our prompt.
            var driver = Interaction();
            InteractTarget.Override(driver, null);
            if (!InteractGuard.PromptShown(driver) && !MenuOpen && world && localUser != null && Active)
            {
                int o = world.OnionInReach(captain);
                if (o >= 0) want = world.OnionTarget(o);
            }
            InteractTarget.Override(Interaction(), want);
        }

        /// Onion `onion` can be opened by him now (its InteractTarget's prompt).
        public bool CanOpenOnion(int onion) =>
            onion >= 0 && !MenuOpen && Active && PikminWorld.Instance && PikminWorld.Instance.OnionInReach(captain) == onion;

        /// The interact button on Onion `onion`.
        public void OpenOnionMenu(int onion)
        {
            if (!CanOpenOnion(onion)) return;
            menuOnion = onion;
            menuChoice = 0;
            menuColour = Native.Red;
            menuDelta = 0;
            lastV = lastH = 0;
            pad.confirm = pad.cancel = false;
        }

        void CloseMenu()
        {
            menuOnion = -1;
            menuDelta = 0;
            pad.Swallow();
        }

        void MenuInput()
        {
            var world = PikminWorld.Instance;
            if (!world || !Active || world.OnionInReach(captain) != menuOnion)
            {
                CloseMenu();
                return;
            }
            int h = pad.menuH;
            if (h != 0 && lastH == 0)
            {
                menuChoice = (menuChoice + (h > 0 ? 1 : 4)) % 5;
                menuDelta = 0;
                if (menuChoice < 3) menuColour = MenuOrder[menuChoice];
            }
            lastH = h;
            if (menuChoice >= 3)
            {
                if (pad.confirm && world.CommandOnion(captain, menuChoice == 3)) CloseMenu();
                else if (pad.cancel) CloseMenu();
                return;
            }
            // One step a press; held, after a moment, quickly (the viewer's window does the same).
            int v = pad.menuV;
            if (v != 0 && (v != lastV || Time.unscaledTime >= repeatAt))
            {
                repeatAt = Time.unscaledTime + (v != lastV ? 0.35f : 0.06f);
                menuDelta += v;
            }
            lastV = v;
            if (world.OnionFlying(captain)) menuDelta = 0;
            if (world.OnionCounts(menuOnion, captain, menuColour, out var n))
            {
                int canOut = (int)Math.Min(n.in_onion, n.max_on_field > n.on_field ? n.max_on_field - n.on_field : 0);
                menuDelta = Mathf.Clamp(menuDelta, -(int)n.in_squad, canOut);
            }
            if (pad.confirm)
            {
                if (menuDelta > 0) world.CallOut(captain, menuOnion, menuColour, (uint)menuDelta);
                else if (menuDelta < 0) world.PutAway(captain, menuOnion, menuColour, (uint)-menuDelta);
                CloseMenu();
            }
            else if (pad.cancel)
            {
                CloseMenu();
            }
        }

        // The unified Onion always manages Red, Yellow and Blue.
        static readonly uint[] MenuOrder = { Native.Red, Native.Yellow, Native.Blue };

        static readonly string[] ColourNames = { "Blue", "Red", "Yellow" };
        static readonly Color[] MenuColours =
        {
            new Color(0.35f, 0.55f, 1f), // Blue
            new Color(1f, 0.3f, 0.25f),  // Red
            new Color(1f, 0.85f, 0.2f),  // Yellow
        };
        GUIStyle titleStyle, labelStyle, countStyle, noteStyle, hintStyle, hudStyle;
        Texture2D panelTex, stripeTex;

        void MenuStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            countStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            countStyle.normal.textColor = Color.white;
            noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            hintStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
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

        // The window, in the middle of the screen, laid out for 1080 lines and scaled.
        readonly int[] squadCounts = new int[3];

        // The squad by colour, above the skill bar: a chip per colour he has, the one he
        // throws next outlined (the d-pad or the mouse wheel moves it on).
        void SquadHud()
        {
            var world = PikminWorld.Instance;
            if (localUser == null || !world || !Active || captain < 0) return;
            world.SquadCounts(captain, squadCounts);
            int pick = world.ThrowColour(captain);
            MenuStyles();
            float k = Screen.height / 1080f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float sw = Screen.width / k;
            const float chip = 64f, gap = 10f;
            float x = sw - 3 * (chip + gap) - 40f, y = 1080f - 250f;
            var savedColour = GUI.color;
            foreach (var c in MenuOrder)
            {
                int n = squadCounts[c];
                var r = new Rect(x, y, chip, chip);
                x += chip + gap;
                bool picked = pick == (int)c;
                GUI.color = new Color(1f, 1f, 1f, n > 0 ? 1f : 0.35f);
                GUI.DrawTexture(r, panelTex);
                GUI.color = MenuColours[c] * new Color(1f, 1f, 1f, n > 0 ? 1f : 0.35f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 6f, r.width, 6f), stripeTex);
                if (picked)
                {
                    GUI.color = MenuColours[c];
                    GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f), stripeTex);
                    GUI.DrawTexture(new Rect(r.x - 3f, r.yMax, r.width + 6f, 3f), stripeTex);
                    GUI.DrawTexture(new Rect(r.x - 3f, r.y, 3f, r.height), stripeTex);
                    GUI.DrawTexture(new Rect(r.xMax, r.y, 3f, r.height), stripeTex);
                }
                GUI.color = savedColour;
                hudStyle.normal.textColor = n > 0 ? MenuColours[c] : new Color(0.5f, 0.5f, 0.5f);
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 6f), n.ToString(), hudStyle);
            }
            GUI.color = savedColour;
            GUI.matrix = saved;
        }

        void OnGUI()
        {
            SquadHud();
            if (!MenuOpen || localUser == null || !PikminWorld.Instance) return;
            if (!PikminWorld.Instance.OnionCounts(menuOnion, captain, menuColour, out var n)) return;
            MenuStyles();
            float k = Screen.height / 1080f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float sw = Screen.width / k;
            const float w = 620f, h = 380f;
            var r = new Rect((sw - w) / 2f, 1080f * 0.5f - h / 2f, w, h);
            var colour = MenuColours[menuColour];
            var savedColour = GUI.color;
            GUI.DrawTexture(r, panelTex);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(r.x, r.y, w, 6f), stripeTex);
            GUI.color = savedColour;

            float x = r.x + 40f, cw = w - 80f, y = r.y + 18f;
            titleStyle.normal.textColor = colour;
            bool shared = (PikminWorld.Instance.OnionColours(menuOnion) & (PikminWorld.Instance.OnionColours(menuOnion) - 1)) != 0;
            GUI.Label(new Rect(r.x, y, w, 40f), menuChoice == 3 ? "< Follow Olimar >" : menuChoice == 4 ? "< Land Here >" : $"< {ColourNames[menuColour]} Pikmin >", titleStyle);
            y += 44f;
            GUI.Label(new Rect(r.x, y, w, 30f), PikminWorld.Instance.OnionFlying(captain)
                ? "Flying — select Land Here to manage Pikmin" : "Landed — select Follow Olimar to take off", noteStyle);
            y += 36f;
            Row(x, y, cw, "In the Onion", (int)n.in_onion - menuDelta);
            y += 52f;
            noteStyle.normal.textColor = menuDelta == 0 ? new Color(0.6f, 0.6f, 0.6f) : colour;
            string move = menuDelta > 0 ? $"calling {menuDelta} out" : menuDelta < 0 ? $"putting {-menuDelta} away" : "-";
            GUI.Label(new Rect(r.x, y, w, 30f), move, noteStyle);
            y += 38f;
            Row(x, y, cw, "With Olimar", (int)n.in_squad + menuDelta);
            y += 56f;
            noteStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            GUI.Label(new Rect(r.x, y, w, 28f), $"Pikmin on the field: {n.on_field + (uint)Math.Max(menuDelta, 0)} / {n.max_on_field}", noteStyle);
            y += 38f;
            string hint = pad.usingPad
                ? "Up: call out   Down: put away" + (shared ? "   Left/right: type / command" : "") + "   A: OK   B: cancel"
                : "W: call out   S: put away" + (shared ? "   A/D: type / command" : "") + "   Click: OK   Right-click: cancel";
            GUI.Label(new Rect(r.x + 20f, y, w - 40f, 44f), hint, hintStyle);
            GUI.matrix = saved;
        }

        void Row(float x, float y, float w, string label, int count)
        {
            GUI.Label(new Rect(x, y, w, 46f), label, labelStyle);
            GUI.Label(new Rect(x, y, w, 46f), count.ToString(), countStyle);
        }
    }
}
