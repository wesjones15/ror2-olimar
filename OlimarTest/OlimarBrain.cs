using Ror2Common;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // The decisions of an Olimar no player controls (Goobo Jr.'s clone), as MarioTest's MarioBrain
    // makes them for Mario. The AI master only walks (ror2-common's SurvivorAi.Drivers: stand and
    // face the enemy within throwing range, else run at it, else follow the leader); this works the
    // captain's controls from where the enemy and his Pikmin are:
    //  - enemy in range and in sight: lock onto it (the sim homes thrown Pikmin on the locked
    //    creature) and throw, one Pikmin at a time: press until one is in his hand, hold a moment,
    //    let go. With none left to throw he punches once it's close;
    //  - no enemy and Pikmin of his standing about: whistle where the nearest one is, which calls
    //    the squad back. Never while there is an enemy, since a whistle pulls them off it.
    internal sealed class OlimarBrain
    {
        public struct Controls
        {
            public bool throw_, whistle;
            /// Where the cursor is: the enemy, or the Pikmin to call.
            public Vector3 cursor;
            public bool hasCursor;
            /// The enemy to lock onto, else null.
            public CharacterBody target;
        }

        // Of the throw's reach, how much counts as in range.
        const float ReachShare = 0.8f;
        // How long a throw is held before letting go, and the gap after (s).
        const float HoldTime = 0.2f, ThrowGap = 0.3f;
        // A call lasts this long, and the wait before the next one (s).
        const float CallTime = 1f, CallGap = 2f;
        // Close enough to punch (m).
        const float PunchRange = 2.5f;

        /// How far he throws (m): Navi's cursor reach (300 units, the disc's) times the host's
        /// scale and Items.ThrowReach, less a margin; as far as the AI stands off.
        public static float HoldRange =>
            Mathf.Max(3f, 300f * UnityHost.Scale * Mathf.Max(0.1f, Plugin.ThrowReach.Value) * ReachShare);

        float holdFor, gap, calling, callWait;

        /// The controls for the next tick (`dt` s). `captain` is his id in the sim; `state` his
        /// last tick's state (null before the first).
        public Controls Think(CharacterBody body, PikminWorld world, int captain, in Native.Captain state,
            bool hasState, float dt)
        {
            var c = new Controls();
            gap -= dt;
            callWait -= dt;
            var enemy = SurvivorAi.Enemy(body);
            bool holding = hasState && state.held_piki >= 0;
            uint squad = hasState ? state.squad : 0u;
            if (enemy)
            {
                calling = 0f;
                var feet = body.footPosition;
                var at = enemy.corePosition;
                float d = new Vector2(at.x - feet.x, at.z - feet.z).magnitude;
                bool sight = !Physics.Linecast(body.corePosition, at, UnityHost.Mask, QueryTriggerInteraction.Ignore);
                if (!sight || d > HoldRange * 1.15f)
                {
                    holdFor = 0f;
                    return c;
                }
                c.target = enemy;
                c.cursor = at;
                c.hasCursor = true;
                // Nothing to throw but him close up: punch.
                if (squad == 0 && !holding && d > PunchRange) return c;
                if (gap > 0f)
                {
                    holdFor = 0f;
                    return c;
                }
                if (!holding) c.throw_ = true;
                else
                {
                    holdFor += dt;
                    c.throw_ = holdFor < HoldTime;
                    if (!c.throw_)
                    {
                        // Let go: the sim throws on the release.
                        holdFor = 0f;
                        gap = ThrowGap;
                    }
                }
                return c;
            }

            holdFor = 0f;
            if (calling > 0f)
            {
                calling -= dt;
                if (calling <= 0f) callWait = CallGap;
            }
            if (calling <= 0f && callWait <= 0f && hasState && Stranded(world, captain, state, out var nearest))
            {
                calling = CallTime;
            }
            if (calling > 0f && hasState && Stranded(world, captain, state, out var at2))
            {
                c.whistle = true;
                c.cursor = at2;
                c.hasCursor = true;
            }
            return c;
        }

        // His Pikmin standing about (not in his squad or busy), and the nearest one's place (m).
        static bool Stranded(PikminWorld world, int captain, in Native.Captain state, out Vector3 nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            var from = UnityHost.ToUnity(state.pos);
            bool any = false;
            for (int i = 0; i < world.PikiCount; i++)
            {
                var p = world.Pikis[i];
                if (p.alive == 0 || p.captain != captain || p.state != Native.PikiState.Idle) continue;
                var at = UnityHost.ToUnity(p.pos);
                float d = (at - from).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = at;
                any = true;
            }
            return any;
        }
    }
}
