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
            string LS = Pad(player, PadButton.LS, "Left stick");
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
                .Add("hold " + LB, "Lock on to target")
                .Add(LB + " + tap " + RB, "Cycle target")
                .Add(LB + " + tap " + A, "Rapidly throw Pikmin at target")
                .Add(RT + " + " + B, "Whistle to call Pikmin")
                .Add(RT + " + " + RS, "Swarm Pikmin")
                .Add(RT + " + " + X, "Dump flying Onion's Pikmin")
                .Add(RT + " + " + Y, "Fill flying Onion with squad")
                .Add("hold " + X + " (0.6s)", "Land/lift Onion");
            sheet.Pad("While Standing Under Onion")
                .Add(X, "Empty Onion")
                .Add(Y, "Fill Onion with Squad")
                .Add(LB, "Call straggler Pikmin")
                .Add(RB, "Restore health... for a price")
                .Add(A, "Send Onion plucker")
                .Add(B, "Board Onion (Onion Ride)");
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
                .Add("B", "Fill flying Onion with squad");
            sheet.Keys("While Standing Under Onion")
                .Add(utility, "Empty Onion")
                .Add(equipment, "Fill Onion with Squad")
                .Add("G", "Call straggler Pikmin")
                .Add("F", "Restore health... for a price")
                .Add(primary, "Send Onion plucker")
                .Add(secondary, "Board Onion (Onion Ride)");
            sheet.Keys("Riding the Onion")
                .Add("Move", "Steer the Onion")
                .Add("Sprint", "Speed boost (2s)")
                .Add(primary + " / " + secondary + " / " + utility + " / " + equipment, "Get out and sneeze out the squad");
            ControlSheetLayout.SpaceRows(sheet);
            return sheet;
        }
    }
}
