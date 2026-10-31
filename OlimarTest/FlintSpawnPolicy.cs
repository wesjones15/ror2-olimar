using System;
namespace OlimarTest
{
    internal static class FlintSpawnPolicy
    {
        // One roll per eligible player per stage; never farm refills after it burrows.
        public static bool Encounter(float roll, bool ship, int olimars, float shipChance, float otherChance) =>
            (ship || olimars > 0) && roll >= 0f && roll < Math.Max(0f, Math.Min(1f, ship ? shipChance : otherChance));
    }
}
