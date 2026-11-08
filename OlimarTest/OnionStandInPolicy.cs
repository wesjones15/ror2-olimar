using System;
namespace OlimarTest
{
    // Procedural boarding follows the ride clock, independently of optional clips.
    internal static class OnionStandInPolicy
    {
        internal const float BoardingSeconds = 1.4f;
        internal const float PodHeightSim = 110f;
        internal const float CaptainHeight = 1.8f;
        internal static float BoardingHeight(float scale) => PodHeightSim * scale - CaptainHeight * .5f;
        internal static float Lift(float age) { float t = Math.Clamp(age / BoardingSeconds, 0f, 1f); return t * t * (3f - 2f * t); }
        internal static bool CaptainVisible(bool riding, float age) => !riding || age < BoardingSeconds;
        internal static bool LegsVisible(bool airborne) => !airborne;
    }
}
