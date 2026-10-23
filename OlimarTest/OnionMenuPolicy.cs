using System;

namespace OlimarTest
{
    // One signed transfer, bounded at both ends. Holding a direction never wraps into
    // the opposite action; colours are chosen internally only when the transfer executes.
    internal static class OnionMenuPolicy
    {
        // The central beam (the core's 70-unit container reach), at ground level.
        // A hovering Onion offers it only after its menu descent has finished.
        internal static bool InBeam(bool airborne, bool following, bool hoverReady, bool dormant, bool grounded,
            float x, float y, float z) =>
            (!airborne && !following || airborne && following && hoverReady) && !dormant && grounded
            && Math.Abs(y) < 25f && x * x + z * z < 70f * 70f;

        // The smaller entry circle makes walking back to the trailing Onion deliberate.
        // Hysteresis keeps the beam up while Olimar uses the whole menu circle.
        internal static bool UnderHover(float x, float z, bool engaged) =>
            x * x + z * z < (engaged ? 2.8f * 2.8f : 1.25f * 1.25f);

        internal static int Clamp(int delta, uint squad, uint stock, uint room) =>
            Math.Max(-(int)squad, Math.Min(delta, (int)Math.Min(stock, room)));

        internal static uint ReleaseCount(uint stock, uint room) =>
            Math.Min(stock > 0 ? stock - 1 : 0, room);

        internal static uint[] Distribute(uint count, uint blue, uint red, uint yellow)
        {
            var plan = new[] { blue, red, yellow };
            for (int i = 0; i < plan.Length; i++)
            {
                plan[i] = Math.Min(plan[i], count);
                count -= plan[i];
            }
            return plan;
        }
    }
}
