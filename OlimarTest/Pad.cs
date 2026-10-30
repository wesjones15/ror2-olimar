using Rewired;
using Action = RewiredConsts.Action;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // The Pikmin control scheme's input: a GameCube-style pad read by button position through
    // Rewired's gamepad template, so the player's RoR2 binds don't matter. A: throw/pluck,
    // B: jump, LB + B: whistle, X: raise or lower the Onion (interact when a prompt is up), RB: the dismiss strike (a melee blow; it no
    // longer dismisses the squad), d-pad left or right: next colour to throw, right stick: swarm
    // (the C-stick), LT: recentre the camera, right stick click: zoom, RT: top view (fixed camera).
    // On keyboard and mouse it falls back to RoR2's actions: move, primary throw, secondary jump,
    // jump + secondary whistle, utility relocates the Onion, F strikes, special swarm toward the
    // cursor, jump recentre, C zoom, sprint to run, mouse wheel for the next colour.
    // Standing in an Onion's beam shows direct X/Y/RB/LB/A shortcuts; G recalls
    // stuck carriers on keyboard.
    //
    // Read every frame; held buttons are latched until the sim has run a tick, so a tap
    // between two 30 Hz ticks still registers. X is also RoR2's interact button: a press made
    // while an interact prompt is on screen (leaving the drop pod, a chest) goes to RoR2 only,
    // and doesn't relocate until it's let go. Y is equipment outside the Onion window.
    internal class Pad
    {
        public Vector2 move, swarm;
        public Vector2 look, targetDirection;
        public bool lockHeld, cycleTarget;
        public bool swarmToCursor;
        // Down now, and pressed at some point since the sim last ran a tick.
        bool throwNow, whistleNow, relocateNow, strikeNow, cycleNow, throwTap, whistleTap, relocateTap, strikeTap, cycleTap;
        readonly JumpWhistleButton jumpButton = new JumpWhistleButton();
        public bool jumpBlocked => jumpButton.Blocked;
        public bool Jump => jumpButton.Jump;
        // X presses that belong to an interact prompt.
        readonly InteractGuard interact = new InteractGuard();
        public bool Throw => throwNow || throwTap;
        public bool Whistle => whistleNow || whistleTap;
        public bool Relocate => relocateNow || relocateTap;
        public bool RelocateHeld => relocateNow;
        public bool Strike => strikeNow || strikeTap;
        public bool Cycle => cycleNow || cycleTap;
        public bool recentre, zoom, topView;
        // Direct shortcuts while standing in the Onion beam.
        public bool confirm, cancel, releaseAll, returnAll, heal, recallStuck;
        public bool usingPad;
        // Throw and whistle stay up until let go (the press that closed a window).
        bool swallowThrow, swallowWhistle, swallowRelocate, swallowEquipment, swallowStrike;
        bool strikeHeld;
        bool relocateHeld, equipmentHeld;
        public bool EquipmentBlocked => swallowEquipment;

        /// `interactPrompt`: RoR2 is showing an interact prompt right now.
        public void Read(Player player, bool interactPrompt)
        {
            if (player == null) return;
            var gp = Gamepad(player);
            usingPad = gp != null;
            if (gp != null)
            {
                move = Dead(gp.leftStick.value);
                swarm = Dead(gp.rightStick.value);
                swarmToCursor = false;
                throwNow = gp.a.value;
                jumpButton.Read(gp.b.value, gp.leftBumper.value);
                whistleNow = jumpButton.Whistle;
                relocateNow = interact.Claim(gp.x.value, interactPrompt);
                relocateHeld = gp.x.value;
                equipmentHeld = gp.y.value;
                releaseAll |= gp.x.justPressed;
                returnAll |= gp.y.justPressed;
                cycleNow = gp.dPad.left.value || gp.dPad.right.value;
                recentre |= Pressed(gp.leftTrigger);
                zoom |= gp.rightStick.press.justPressed;
                topView |= Pressed(gp.rightTrigger);
                strikeNow = gp.rightBumper.value;
                strikeHeld = gp.rightBumper.value;
                heal |= gp.rightBumper.justPressed;
                recallStuck |= gp.leftBumper.justPressed;
                if (Plugin.Camera.Value == CameraMode.Orbit)
                {
                    look = gp.leftBumper.value ? Vector2.zero : Dead(gp.rightStick.value);
                    swarm = gp.leftBumper.value ? Dead(gp.rightStick.value) : Vector2.zero;
                    recentre = Pressed(gp.leftTrigger);
                    lockHeld = gp.rightTrigger.value > 0.6f;
                    cycleTarget |= lockHeld && gp.rightBumper.justPressed;
                    targetDirection = Dead(gp.rightStick.value);
                    zoom = gp.rightStick.press.justPressed;
                    topView = false;
                    // RB cycles targets while RT holds a lock; otherwise it strikes.
                    strikeNow = !lockHeld && gp.rightBumper.value;
                    if (cycleTarget) look = Vector2.zero;
                }
                else { look = Vector2.zero; lockHeld = cycleTarget = false; }
                confirm |= gp.a.justPressed;
                cancel |= gp.b.justPressed;
            }
            else
            {
                move = new Vector2(player.GetAxis(Action.MoveHorizontal), player.GetAxis(Action.MoveVertical));
                swarm = Vector2.zero;
                look = Vector2.zero;
                lockHeld = cycleTarget = false;
                swarmToCursor = player.GetButton(Action.SpecialSkill);
                throwNow = player.GetButton(Action.PrimarySkill);
                jumpButton.Read(player.GetButton(Action.SecondarySkill), player.GetButton(Action.Jump));
                whistleNow = jumpButton.Whistle;
                relocateNow = player.GetButton(Action.UtilitySkill);
                relocateHeld = relocateNow;
                equipmentHeld = player.GetButton(Action.Equipment);
                releaseAll |= player.GetButtonDown(Action.UtilitySkill);
                returnAll |= player.GetButtonDown(Action.Equipment);
                strikeNow = UnityEngine.Input.GetKey(KeyCode.F);
                strikeHeld = strikeNow;
                heal |= UnityEngine.Input.GetKeyDown(KeyCode.F);
                recallStuck |= UnityEngine.Input.GetKeyDown(KeyCode.G);
                // A wheel notch is a press for this frame.
                cycleNow = UnityEngine.Input.mouseScrollDelta.y != 0f;
                recentre |= player.GetButtonDown(Action.Jump);
                zoom |= UnityEngine.Input.GetKeyDown(KeyCode.C);
                confirm |= player.GetButtonDown(Action.PrimarySkill) || UnityEngine.Input.GetKeyDown(KeyCode.Return)
                           || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
                cancel |= player.GetButtonDown(Action.SecondarySkill) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace);
            }
            if (swallowThrow)
            {
                if (throwNow) throwNow = false;
                else swallowThrow = false;
            }
            if (swallowWhistle)
            {
                if (whistleNow) whistleNow = false;
                else swallowWhistle = false;
            }
            if (swallowRelocate)
            {
                relocateNow = false;
                if (!relocateHeld) swallowRelocate = false;
            }
            if (swallowEquipment && !equipmentHeld) swallowEquipment = false;
            if (swallowStrike)
            {
                strikeNow = false;
                if (!strikeHeld) swallowStrike = false;
            }
            throwTap |= throwNow;
            whistleTap |= whistleNow;
            relocateTap |= relocateNow;
            strikeTap |= strikeNow;
            cycleTap |= cycleNow;
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (swarm.sqrMagnitude > 1f) swarm.Normalize();
        }

        /// Keeps throw and whistle up until they're let go, and drops pending taps: the press
        /// that closed the Onion window isn't a throw or a whistle.
        public void Swallow()
        {
            swallowThrow = swallowWhistle = true;
            swallowRelocate = true;
            swallowStrike = true;
            swallowEquipment |= equipmentHeld;
            jumpButton.Swallow();
            throwNow = whistleNow = relocateNow = strikeNow = cycleNow = false;
            throwTap = whistleTap = relocateTap = strikeTap = cycleTap = false;
        }

        /// Clears the taps once the sim has run a tick with them.
        public void Ticked()
        {
            throwTap = whistleTap = relocateTap = strikeTap = cycleTap = false;
            jumpButton.Ticked();
        }

        // Radial deadzone with the live range rescaled, so a drifting stick reads exactly zero at rest.
        static Vector2 Dead(Vector2 stick)
        {
            float zone = Plugin.StickDeadzone != null ? Plugin.StickDeadzone.Value : 0f;
            if (zone <= 0f) return stick;
            float length = stick.magnitude;
            if (length <= zone) return Vector2.zero;
            return stick * (Mathf.Min(1f, (length - zone) / (1f - zone)) / length);
        }

        static bool Pressed(IControllerTemplateAxis axis) => axis.value > 0.6f && axis.valuePrev <= 0.6f;

        static IGamepadTemplate Gamepad(Player player)
        {
            var last = player.controllers.GetLastActiveController();
            if (last == null || last.type != ControllerType.Joystick) return null;
            if (!last.ImplementsTemplate<IGamepadTemplate>()) return null;
            return last.GetTemplate<IGamepadTemplate>();
        }
    }
}
