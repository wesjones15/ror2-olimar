using Ror2Common;

namespace OlimarTest
{
    // Olimar's entry in the pause menu's controls window (ror2-common PauseControls).
    internal static class OlimarControls
    {
        public static ControlSheet Sheet()
        {
            var sheet = new ControlSheet
            {
                Subtitle = "Captain of the Dolphin, with his Pikmin",
                Footer = "X is also RoR2's interact: with a prompt up it goes to that, not the Onion.",
            };

            sheet.Pad("Moving")
                .Add("Left stick", "Walk, run")
                .Add("B", "Jump")
                .Add("Right stick", "Swarm the squad")
                .Add("LT", "Recentre the camera")
                .Add("Right stick click", "Zoom")
                .Add("RT", "Hold to lock on to an enemy");
            sheet.Pad("Pikmin")
                .Add("A", "Hold to grab one, release to throw")
                .Add("D-pad left / right", "Next colour to throw")
                .Add("LB + B", "Whistle: call Pikmin to you")
                .Add("RB", "Dismiss strike (a melee blow)")
                .Add("RB while locked", "Cycle the target")
                .Add("A at a sprout", "Pluck it");
            sheet.Pad("The Onion")
                .Add("X", "Raise it to follow, or lower it")
                .Add("X X (flying)", "Dump the Pikmin out of it")
                .Add("In its beam: X", "Call Pikmin out")
                .Add("In its beam: Y", "Put the squad away")
                .Add("In its beam: RB", "Heal from its stock")
                .Add("In its beam: LB", "Recall stuck carriers");

            sheet.Keys("Moving")
                .Add("Move", "Walk (Sprint: run)")
                .Add("Secondary", "Jump")
                .Add("Special", "Swarm toward the cursor")
                .Add("Jump", "Recentre the camera")
                .Add("C", "Zoom");
            sheet.Keys("Pikmin")
                .Add("Primary", "Hold to grab one, release to throw")
                .Add("Mouse wheel", "Next colour to throw")
                .Add("Jump + Secondary", "Whistle: call Pikmin to you")
                .Add("F", "Dismiss strike (a melee blow)")
                .Add("G", "Recall stuck carriers");
            sheet.Keys("The Onion")
                .Add("Utility", "Raise it to follow, or lower it")
                .Add("Utility Utility (flying)", "Dump the Pikmin out of it")
                .Add("In its beam: Utility", "Call Pikmin out")
                .Add("In its beam: Equipment", "Put the squad away");
            return sheet;
        }
    }
}
