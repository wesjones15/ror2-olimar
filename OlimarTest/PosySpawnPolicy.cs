using System;

namespace OlimarTest
{
    internal static class PosySpawnPolicy
    {
        public static int Target(bool ship, int players, int olimars, int shipPerPlayer, int classicPerOlimar) =>
            (int)Math.Min(ship ? 24L : 8L, (long)Math.Max(0, ship ? players : olimars) * Math.Max(0, ship ? shipPerPlayer : classicPerOlimar));

        public static float RefillSeconds(bool ship) => ship ? 20f : 60f;
        // Every Posy spawns bloomed (size 2, taipalm.cpp's grown model): ready to harvest, never a bud.
        public static int InitialSize(int index) => 2;
    }
}
