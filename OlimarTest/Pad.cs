using Rewired;
using Action = RewiredConsts.Action;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // The Pikmin control scheme's input: a GameCube-style pad read by button position through
    // Rewired's gamepad template, so the player's RoR2 binds don't matter. A: throw/pluck,
    // B: jump, RT + B: whistle, X: raise or lower the Onion (interact when a prompt is up), RB: the dismiss strike (a melee blow; it no
    // longer dismisses the squad), d-pad left or right: next colour to throw, RT + right stick: swarm
    // (the C-stick), LB: recentre the camera, right stick click: zoom,
    // LT: lock on (RB then cycles) and board in the beam, RT + tap LT: call the Onion to him.
    // RT is the alternate-command modifier: RT + X dumps and RT + Y fills a flying Onion.
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
        public bool confirm, cancel, releaseAll, returnAll, heal, healSummon, recallStuck;
        // RT (the alternate-command modifier) held, and its remote Onion commands: RT + X dumps and RT + Y
        // fills, away from the beam. Consumed by the driver.
        public bool modifierHeld, remoteDump, remoteFill;
        // LT in an Onion's beam boards it (Onion Ride); LB recalls stuck carriers; RT + tap LT anywhere calls the Onion to him and boards.
        public bool board, summonPressed;
        // Ride controls: the left stick click (the boost) and any of A/B/X/Y pressed (the exit).
        public bool boostPressed, anyFacePressed;
        /// Y or X was pressed with RT held, so RoR2 mustn't also see an equipment or interact press.
        public bool BlocksInteract => modifierHeld || swallowInteract;
        bool swallowInteract;
        public bool usingPad;
        // Throw and whistle stay up until let go (the press that closed a window).
        bool swallowThrow, swallowWhistle, swallowRelocate, swallowEquipment, swallowStrike;
        bool strikeHeld;
        bool relocateHeld, equipmentHeld;
        public bool EquipmentBlocked => swallowEquipment || (modifierHeld && equipmentHeld) || swallowInteract;

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
                bool rt = gp.rightTrigger.value > 0.6f;
                modifierHeld = rt;
                jumpButton.Read(gp.b.value, rt);
                whistleNow = jumpButton.Whistle;
                // RT + X is the remote dump, never the hold-X relocation.
                relocateNow = interact.Claim(gp.x.value && !rt, interactPrompt);
                relocateHeld = gp.x.value && !rt;
                equipmentHeld = gp.y.value;
                releaseAll |= gp.x.justPressed;
                returnAll |= gp.y.justPressed;
                remoteDump |= rt && gp.x.justPressed;
                remoteFill |= rt && gp.y.justPressed;
                boostPressed |= gp.leftStick.press.justPressed;
                anyFacePressed |= gp.a.justPressed || gp.b.justPressed || gp.x.justPressed || gp.y.justPressed;
                swallowInteract = rt && (gp.x.value || gp.y.value);
                cycleNow = gp.dPad.left.value || gp.dPad.right.value;
                // LT holds a lock-on, and boards in the beam; LB recentres the camera. The Onion
                // call is RT + a fresh tap of LT: if LT is already down for a lock, pressing RT
                // does nothing (the other RT commands still work); release LT and tap it again
                // while RT is held and the call fires as if RT had come first.
                bool lb = gp.leftBumper.justPressed;
                bool lt = Pressed(gp.leftTrigger);
                recentre |= lb && !rt;
                board |= lt;
                summonPressed |= lt && rt;
                zoom |= gp.rightStick.press.justPressed;
                strikeNow = gp.rightBumper.value;
                strikeHeld = gp.rightBumper.value;
                heal |= gp.rightBumper.justPressed;
                // RT + RB calls the Onion over him to heal him, not the strike.
                if (rt && gp.rightBumper.justPressed) { healSummon = true; swallowStrike = true; }
                recallStuck |= lb;
                if (Plugin.Camera.Value == CameraMode.Orbit)
                {
                    look = rt ? Vector2.zero : Dead(gp.rightStick.value);
                    swarm = rt ? Dead(gp.rightStick.value) : Vector2.zero;
                    recentre = lb && !rt;
                    lockHeld = gp.leftTrigger.value > 0.6f;
                    cycleTarget |= lockHeld && gp.rightBumper.justPressed;
                    targetDirection = Dead(gp.rightStick.value);
                    zoom = gp.rightStick.press.justPressed;
                    topView = false;
                    // RB cycles targets while LT holds a lock; otherwise it strikes.
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
                modifierHeld = false;
                swarmToCursor = player.GetButton(Action.SpecialSkill);
                throwNow = player.GetButton(Action.PrimarySkill);
                jumpButton.Read(player.GetButton(Action.SecondarySkill), player.GetButton(Action.Jump));
                whistleNow = jumpButton.Whistle;
                relocateNow = player.GetButton(Action.UtilitySkill);
                relocateHeld = relocateNow;
                equipmentHeld = player.GetButton(Action.Equipment);
                // Keyboard: V dumps and B fills the Onion from afar (the pad's RT + X / RT + Y).
                remoteDump |= UnityEngine.Input.GetKeyDown(KeyCode.V);
                remoteFill |= UnityEngine.Input.GetKeyDown(KeyCode.B);
                boostPressed |= player.GetButtonDown(Action.Sprint);
                anyFacePressed |= player.GetButtonDown(Action.PrimarySkill) || player.GetButtonDown(Action.SecondarySkill)
                                  || player.GetButtonDown(Action.UtilitySkill) || player.GetButtonDown(Action.Equipment);
                swallowInteract = UnityEngine.Input.GetKey(KeyCode.V) || UnityEngine.Input.GetKey(KeyCode.B);
                releaseAll |= player.GetButtonDown(Action.UtilitySkill);
                returnAll |= player.GetButtonDown(Action.Equipment);
                strikeNow = UnityEngine.Input.GetKey(KeyCode.F);
                strikeHeld = strikeNow;
                heal |= UnityEngine.Input.GetKeyDown(KeyCode.F);
                // Keyboard stand-in for RT + RB: hold Z and tap F.
                if (UnityEngine.Input.GetKey(KeyCode.Z) && UnityEngine.Input.GetKeyDown(KeyCode.F))
                {
                    healSummon = true;
                    strikeNow = false;
                    swallowStrike = true;
                }
                recallStuck |= UnityEngine.Input.GetKeyDown(KeyCode.G);
                // A wheel notch is a press for this frame.
                cycleNow = UnityEngine.Input.mouseScrollDelta.y != 0f;
                recentre |= player.GetButtonDown(Action.Jump);
                // Keyboard: Jump in a beam boards (as LT does) and Z calls the Onion to him.
                board |= player.GetButtonDown(Action.Jump);
                summonPressed |= UnityEngine.Input.GetKeyDown(KeyCode.Z);
                zoom |= UnityEngine.Input.GetKeyDown(KeyCode.C);
                confirm |= player.GetButtonDown(Action.PrimarySkill) || UnityEngine.Input.GetKeyDown(KeyCode.Return)
                           || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
                cancel |= player.GetButtonDown(Action.SecondarySkill) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace);
            }
            Finish();
        }

        // ---- VR ------------------------------------------------------------------------------------
        //
        // In a headset with motion controls and VR.Controls (OlimarVr.Controls) the controllers are read
        // as they are (ror2-common's VrPad), right-handed (the VR mod's left-handed mode mirrors it):
        // the left stick walks (its click boosts a ride), the right trigger throws and plucks, the right
        // grip whistles at the pointer and is the pad's RT modifier (it blocks jumping; with X / Y it
        // dumps / fills a flying Onion, with B it calls the Onion to heal, with a tap of the left trigger
        // it calls the Onion to board), the left grip swarms toward the pointer, the left trigger locks on
        // (B then cycles) and boards in a beam, A jumps, B strikes, holding X lowers or raises the Onion,
        // and the right stick click picks the next colour. The cursor is the pointer (OlimarVr).

        readonly VrPad vr = new VrPad();
        VrPad.Hand lastDominant, lastOff;
        const float Squeeze = 0.6f;

        public void ReadVr(bool interactPrompt, bool blocked)
        {
            vr.Read(blocked);
            var d = vr.Dominant;
            var o = vr.Off;
            usingPad = true;
            move = Dead(o.Stick);
            swarm = look = targetDirection = Vector2.zero;
            recentre = zoom = topView = false;
            bool grip = d.Grip > Squeeze;
            modifierHeld = grip;
            throwNow = d.Trigger > Squeeze;
            jumpButton.Read(d.Primary, grip);
            whistleNow = grip;
            swarmToCursor = o.Grip > Squeeze;
            bool x = o.Primary, y = o.Secondary;
            bool aPressed = VrPad.Pressed(d.Primary, lastDominant.Primary), bPressed = VrPad.Pressed(d.Secondary, lastDominant.Secondary);
            bool xPressed = VrPad.Pressed(x, lastOff.Primary), yPressed = VrPad.Pressed(y, lastOff.Secondary);
            relocateNow = interact.Claim(x && !grip, interactPrompt);
            relocateHeld = x && !grip;
            equipmentHeld = y;
            releaseAll |= xPressed;
            returnAll |= yPressed;
            remoteDump |= grip && xPressed;
            remoteFill |= grip && yPressed;
            boostPressed |= VrPad.Pressed(o.StickClick, lastOff.StickClick);
            anyFacePressed |= aPressed || bPressed || xPressed || yPressed;
            swallowInteract = grip && (x || y);
            cycleNow = VrPad.Pressed(d.StickClick, lastDominant.StickClick);
            bool lt = VrPad.AxisPressed(o.Trigger, lastOff.Trigger, Squeeze);
            board |= lt;
            summonPressed |= lt && grip;
            lockHeld = o.Trigger > Squeeze;
            cycleTarget |= lockHeld && bPressed;
            strikeNow = !lockHeld && d.Secondary;
            strikeHeld = d.Secondary;
            heal |= bPressed;
            if (grip && bPressed) { healSummon = true; swallowStrike = true; }
            // Under the beam: the left stick click calls stuck carriers and the right trigger sends the
            // plucker (A jumps there, as everywhere).
            recallStuck |= VrPad.Pressed(o.StickClick, lastOff.StickClick);
            confirm |= VrPad.AxisPressed(d.Trigger, lastDominant.Trigger, Squeeze);
            cancel |= bPressed;
            lastDominant = d;
            lastOff = o;
            Finish();
        }

        // Shared by both reads: the presses a closed window swallowed, the taps latched until a tick.
        void Finish()
        {
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






