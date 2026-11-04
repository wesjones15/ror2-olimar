using System;
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

        /// His own body isn't drawn for him in first person (VR.HideBody), in any skin; other players
        /// and his Pikmin, cursor and whistle still are.
        internal static bool HidesCaptain(OlimarDriver d) =>
            d && d.ViewCaptain >= 0 && Plugin.VrSettings != null && Character.HideBody(d.Body);

        /// Where the Pikmin he holds before a throw is drawn: in the dominant hand (his throwing hand), stood up
        /// on the controller (its up along the back of the hand, facing where it points) and turned with the view
        /// through a flip. Null with no hand.
        internal static (Vector3, Quaternion)? HeldAt()
        {
            var hand = Vr.Hand(true);
            if (!hand) return null;
            return VrRig.Turned(hand.position + hand.rotation * (Vector3.forward * 0.02f), hand.rotation);
        }

        static readonly float[] head = new float[16];

        /// After the view posed him (PikminView): the eye follows his head's height (VR.HeadFollow);
        /// his Pikmin jump (a roll) is a flip, turning the view over only with VR.FlipCamera.
        /// `feet` and `turn` are where his body is drawn.
        internal static void FollowHead(OlimarDriver d, IntPtr view, Vector3 feet, Quaternion turn)
        {
            var body = d ? d.Body : null;
            if (!InHeadset(body)) return;
            Character.PlaceBehind(body);
            if (!Character.FirstPerson(body) || view == IntPtr.Zero || d.ViewCaptain < 0) return;
            // Anchor 0 is the head in the body's model space, every skin (pik_view_anchor); ModelView
            // mirrors the models across their own X.
            if (Native.pik_view_anchor(view, (uint)d.ViewCaptain, 0, head) != 1) return;
            var offset = turn * new Vector3(-head[12], head[13], head[14]) * UnityHost.Scale;
            var s = d.State;
            var pose = s.on_ground != 0 && s.down == 0 && s.plucking == 0 ? VrCharacter.Pose.Standing
                : s.on_ground == 0 && Plugin.PikminJump.Value ? VrCharacter.Pose.Flip
                : VrCharacter.Pose.Other;
            Character.FollowHead(body, offset, turn * Vector3.forward, feet, Mathf.Max(offset.y, 0.3f), pose);
        }
    }
}
