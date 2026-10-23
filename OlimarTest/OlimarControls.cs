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
                .Add("hold " + RT, "Lock on to target")
                .Add(RT + " + tap " + RB, "Cycle target")
                .Add(RT + " + tap " + A, "Rapidly throw Pikmin at target")
                .Add(LB + " + " + B, "Whistle to call Pikmin")
                .Add(LB + " + " + RS, "Swarm Pikmin")
                .Add(X, "Land/lift Onion")
                .Add("tap " + X + " + " + X, "Dump all Onion Pikmin");
            sheet.Pad("While Standing Under Onion")
                .Add(X, "Empty Onion")
                .Add(Y, "Fill Onion with Squad")
                .Add(LB, "Call straggler Pikmin")
                .Add(RB, "Restore Olimar's Health...\n<pos=170>For a price");

            // Keyboard actions use the player's RoR2 bindings. F and G are direct
            // keys in Pad.Read, so they remain literal here.
            sheet.Keys("")
                .Add(secondary, "Jump")
                .Add(primary, "Grab and Throw Pikmin")
                .Add("F", "Dismiss Strike / Pikmin Charge")
                .Add(jump + " + " + secondary, "Whistle to call Pikmin")
                .Add(special, "Swarm Pikmin")
                .Add(utility, "Land/lift Onion")
                .Add("tap " + utility + " + " + utility, "Dump all Onion Pikmin");
            sheet.Keys("While Standing Under Onion")
                .Add(utility, "Empty Onion")
                .Add(equipment, "Fill Onion with Squad")
                .Add("G", "Call straggler Pikmin")
                .Add("F", "Restore Olimar's Health...\n<pos=170>For a price");
            ControlSheetLayout.SpaceRows(sheet);
            return sheet;
        }
    }
}
