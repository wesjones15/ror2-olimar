using RoR2;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Olimar played in Blowntobytes' VR mod: ror2-common's VrCharacter (the view only ever turned
    // by the player, inside him or behind him with VR.ThirdPerson, the wrist HUD) plus what's his
    // own. Wherever the view is, the Pikmin cursor is where the dominant hand points (the shared
    // pointer aim Mario's FLUDD uses), never where the head looks, and it isn't pinned to the
    // ground: it sits on whatever the pointer hits, an enemy's side or a ledge above him, as the
    // flat-screen cursor does only while locked on. Pikmin's camera never takes the headset.
    // Nothing here runs without the VR mod in a headset.
    internal static class OlimarVr
    {
        static VrCharacter character;
        /// The shared VR character, with Olimar's [VR] settings.
        internal static VrCharacter Character => character ?? (character = new VrCharacter(Plugin.VrSettings));

        /// The local player has `body` in a headset (first or third person).
        internal static bool InHeadset(CharacterBody body) => Plugin.VrSettings != null && Character.InHeadset(body);

        /// His cursor from the pointer (VrCharacter.Aim) in a headset with motion controls, cast up to
        /// `range` and resting `miss` along it in the air. False otherwise.
        internal static bool PointerCursor(CharacterBody body, float range, float miss, out VrCharacter.PointerAim aim)
        {
            aim = default;
            return InHeadset(body) && Character.Aim(body, out aim, range, miss);
        }

        /// The input's `use_cursor` for a pointer aim: free (2, the sim keeps the point's height)
        /// where it hit something, on the ground below it (1) where it points into the air.
        internal static byte CursorMode(in VrCharacter.PointerAim aim) => (byte)(aim.Hit ? 2 : 1);

        /// Each frame for the local body: the view behind him in third person (VR.ThirdPerson).
        internal static void PlaceView(CharacterBody body)
        {
            if (InHeadset(body)) Character.PlaceBehind(body);
        }
    }
}
