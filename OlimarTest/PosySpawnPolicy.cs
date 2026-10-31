using System;

namespace OlimarTest
{
    internal static class PosySpawnPolicy
    {
        public static int Target(bool ship, int players, int olimars, int shipPerPlayer, int classicPerOlimar) =>
            (int)Math.Min(ship ? 24L : 8L, (long)Math.Max(0, ship ? players : olimars) * Math.Max(0, ship ? shipPerPlayer : classicPerOlimar));

        public static float RefillSeconds(bool ship) => ship ? 20f : 60f;
        // Mostly ready to harvest, with buds/seedlings that play the disc's natural growth.
        public static int InitialSize(int index) => index % 4 == 3 ? 0 : index % 4 == 1 ? 1 : 2;
    }
}
