using Rewired;
using Action = RewiredConsts.Action;
using UnityEngine;

namespace OlimarTest
{
    // The Pikmin control scheme's input: a GameCube-style pad read by button position through
    // Rewired's gamepad template, so the player's RoR2 binds don't matter. A: throw/pluck,
    // B: whistle, X: dismiss, right stick: swarm (the C-stick), L: recentre the camera,
    // R: zoom, Z (right bumper): top view, d-pad: the Onion menu. On keyboard and mouse it falls
    // back to RoR2's actions: move, primary throw, secondary whistle, utility dismiss, special
    // swarm toward the cursor, jump recentre, sprint zoom, arrow keys the Onion menu.
    //
    // Read every frame; held buttons are latched until the sim has run a tick, so a tap
    // between two 30 Hz ticks still registers. X is also RoR2's interact button: a press made
    // while an interact prompt is on screen (leaving the drop pod, a chest) goes to RoR2 only,
    // and doesn't dismiss until it's let go.
    internal class Pad
    {
        public Vector2 move, swarm;
        public bool swarmToCursor;
        // Down now, and pressed at some point since the sim last ran a tick.
        bool throwNow, whistleNow, dismissNow, throwTap, whistleTap, dismissTap;
        // The current X press belongs to an interact prompt.
        bool dismissForPrompt;
        public bool Throw => throwNow || throwTap;
        public bool Whistle => whistleNow || whistleTap;
        public bool Dismiss => dismissNow || dismissTap;
        public bool recentre, zoom, topView;
        public bool menuUp, menuDown, menuLeft, menuRight;
        public bool usingPad;

        /// `interactPrompt`: RoR2 is showing an interact prompt right now.
        public void Read(Player player, bool interactPrompt)
        {
            if (player == null) return;
            var gp = Gamepad(player);
            usingPad = gp != null;
            if (gp != null)
            {
                move = gp.leftStick.value;
                swarm = gp.rightStick.value;
                swarmToCursor = false;
                throwNow = gp.a.value;
                whistleNow = gp.b.value;
                bool x = gp.x.value;
                if (x && interactPrompt && !dismissNow) dismissForPrompt = true;
                if (!x) dismissForPrompt = false;
                dismissNow = x && !dismissForPrompt;
                recentre |= gp.leftBumper.justPressed || Pressed(gp.leftTrigger);
                zoom |= Pressed(gp.rightTrigger);
                topView |= gp.rightBumper.justPressed;
                menuUp |= gp.dPad.up.justPressed;
                menuDown |= gp.dPad.down.justPressed;
                menuLeft |= gp.dPad.left.justPressed;
                menuRight |= gp.dPad.right.justPressed;
            }
            else
            {
                move = new Vector2(player.GetAxis(Action.MoveHorizontal), player.GetAxis(Action.MoveVertical));
                swarm = Vector2.zero;
                swarmToCursor = player.GetButton(Action.SpecialSkill);
                throwNow = player.GetButton(Action.PrimarySkill);
                whistleNow = player.GetButton(Action.SecondarySkill);
                dismissNow = player.GetButton(Action.UtilitySkill);
                recentre |= player.GetButtonDown(Action.Jump);
                zoom |= player.GetButtonDown(Action.Sprint);
                menuUp |= UnityEngine.Input.GetKeyDown(KeyCode.UpArrow);
                menuDown |= UnityEngine.Input.GetKeyDown(KeyCode.DownArrow);
                menuLeft |= UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow);
                menuRight |= UnityEngine.Input.GetKeyDown(KeyCode.RightArrow);
            }
            throwTap |= throwNow;
            whistleTap |= whistleNow;
            dismissTap |= dismissNow;
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (swarm.sqrMagnitude > 1f) swarm.Normalize();
        }

        /// Clears the taps once the sim has run a tick with them.
        public void Ticked()
        {
            throwTap = whistleTap = dismissTap = false;
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
