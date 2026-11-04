using Rewired;
using RoR2;
using RoR2.UI;
using Ror2Common;

namespace OlimarTest
{
    // The pause window chooses the section for the player's last input device.
    internal static class OlimarControls
    {
        static string Pad(Rewired.Player player, PadButton button, string fallback) =>
            GlyphOverride.PhysicalGlyph(player, button) ?? fallback;

        static string Key(MPEventSystem eventSystem, string action, string fallback)
        {
            if (!eventSystem) return fallback;
            var glyph = Glyphs.GetGlyphString(eventSystem, action, AxisRange.Full,
                MPEventSystem.InputSource.MouseAndKeyboard, true);
            return string.IsNullOrEmpty(glyph) ? fallback : glyph;
        }

        public static ControlSheet Sheet()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var player = user?.inputPlayer;
            var eventSystem = user?.eventSystem;
            string A = Pad(player, PadButton.A, "A"), B = Pad(player, PadButton.B, "B");
            string X = Pad(player, PadButton.X, "X"), Y = Pad(player, PadButton.Y, "Y");
            string LB = Pad(player, PadButton.LB, "LB"), RB = Pad(player, PadButton.RB, "RB");
            string RT = Pad(player, PadButton.RT, "RT"), RS = Pad(player, PadButton.RS, "RS");
            string LS = Pad(player, PadButton.LS, "Left stick"), LT = Pad(player, PadButton.LT, "LT");
            string primary = Key(eventSystem, "PrimarySkill", "Primary");
            string secondary = Key(eventSystem, "SecondarySkill", "Secondary");
            string utility = Key(eventSystem, "UtilitySkill", "Utility");
            string equipment = Key(eventSystem, "Equipment", "Equipment");
            string jump = Key(eventSystem, "Jump", "Jump");
            string special = Key(eventSystem, "SpecialSkill", "Special");

            var sheet = new ControlSheet
            {
                Subtitle = "Captain of the S.S. Dolphin,\nConqueror of PNF-404",
            };

            sheet.Pad("")
                .Add(B, "Jump")
                .Add(A, "Grab and Throw Pikmin")
                .Add(RB, "Dismiss Strike / Pikmin Charge")
                .Add("hold " + LT, "Lock on to target")
                .Add(LT + " + tap " + RB, "Cycle target")
                .Add(LT + " + tap " + A, "Rapidly throw Pikmin at target")
                .Add(RT + " + " + B, "Whistle to call Pikmin")
                .Add(RT + " + " + RS, "Swarm Pikmin")
                .Add(RT + " + " + X, "Dump flying Onion's Pikmin")
                .Add(RT + " + " + Y, "Fill flying Onion with squad")
                .Add("hold " + RT + " + tap " + LT, "Call the Onion to you and board it")
                .Add("hold " + X + " (0.6s)", "Land/lift Onion");
            sheet.Pad("While Standing Under Onion")
                .Add(X, "Empty Onion")
                .Add(Y, "Fill Onion with Squad")
                .Add(LB, "Call straggler Pikmin")
                .Add(RB, "Restore health... for a price")
                .Add(A, "Send Onion plucker")
                .Add(LT, "Board Onion (Onion Ride)");
            sheet.Pad("Riding the Onion")
                .Add(LS, "Steer the Onion")
                .Add(RS, "Camera")
                .Add("click " + LS, "Speed boost (2s)")
                .Add(A + " / " + B + " / " + X + " / " + Y, "Get out and sneeze out the squad");

            // Keyboard actions use the player's RoR2 bindings. F and G are direct
            // keys in Pad.Read, so they remain literal here.
            sheet.Keys("")
                .Add(secondary, "Jump")
                .Add(primary, "Grab and Throw Pikmin")
                .Add("F", "Dismiss Strike / Pikmin Charge")
                .Add(jump + " + " + secondary, "Whistle to call Pikmin")
                .Add(special, "Swarm Pikmin")
                .Add("hold " + utility + " (0.6s)", "Land/lift Onion")
                .Add("V", "Dump flying Onion's Pikmin")
                .Add("B", "Fill flying Onion with squad")
                .Add("Z", "Call the Onion to you and board it");
            sheet.Keys("While Standing Under Onion")
                .Add(utility, "Empty Onion")
                .Add(equipment, "Fill Onion with Squad")
                .Add("G", "Call straggler Pikmin")
                .Add("F", "Restore health... for a price")
                .Add(primary, "Send Onion plucker")
                .Add(jump, "Board Onion (Onion Ride)");
            sheet.Keys("Riding the Onion")
                .Add("Move", "Steer the Onion")
                .Add("Sprint", "Speed boost (2s)")
                .Add(primary + " / " + secondary + " / " + utility + " / " + equipment, "Get out and sneeze out the squad");
            ControlSheetLayout.SpaceRows(sheet);
            return sheet;
        }

        // His VR layout (Pad.ReadVr), for a headset with motion controls. The flat sheet above is
        // left as it is. Both sections carry it, since the window picks one by the last device.
        public static ControlSheet VrSheet()
        {
            var sheet = new ControlSheet
            {
                Subtitle = "Captain of the S.S. Dolphin,\nConqueror of PNF-404",
            };
            foreach (var section in new[] { sheet.Pad(""), sheet.Keys("") })
                section
                    .Add("Point", "Aim the cursor (not held to the ground)")
                    .Add("L stick", "Walk (head is forward)")
                    .Add("A", "Jump")
                    .Add("R trigger", "Grab and Throw Pikmin")
                    .Add("B", "Dismiss Strike / Pikmin Charge")
                    .Add("R grip", "Whistle to call Pikmin")
                    .Add("L grip", "Swarm Pikmin to the cursor")
                    .Add("hold L trigger", "Lock on to target")
                    .Add("L trigger + B / R stick click", "Cycle target")
                    .Add("R grip + X", "Dump flying Onion's Pikmin")
                    .Add("R grip + Y", "Fill flying Onion with squad")
                    .Add("R grip + L trigger", "Call the Onion to you and board it")
                    .Add("hold X (0.6s)", "Land/lift Onion");
            foreach (var section in new[] { sheet.Pad("While Standing Under Onion"), sheet.Keys("While Standing Under Onion") })
                section
                    .Add("X", "Empty Onion")
                    .Add("Y", "Fill Onion with Squad")
                    .Add("L stick click", "Call straggler Pikmin")
                    .Add("B", "Restore health... for a price")
                    .Add("R trigger", "Send Onion plucker")
                    .Add("L trigger", "Board Onion (Onion Ride)");
            foreach (var section in new[] { sheet.Pad("Riding the Onion"), sheet.Keys("Riding the Onion") })
                section
                    .Add("L stick", "Steer the Onion")
                    .Add("click L stick", "Speed boost (2s)")
                    .Add("A / B / X / Y", "Get out and sneeze out the squad");
            ControlSheetLayout.SpaceRows(sheet);
            return sheet;
        }
    }
}

