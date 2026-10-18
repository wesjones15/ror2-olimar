using System;

namespace OlimarTest
{
    internal static class OlimarJump
    {
        // The body inherits Commando's jump power. Keep its unmodified height as a
        // minimum, with a little clearance, regardless of Olimar's smaller model scale.
        internal static float Height(float power, float baseline, float gravity, float unit)
        {
            power = Math.Max(power, baseline);
            return (power * power / (2f * Math.Max(0.01f, Math.Abs(gravity))) + 0.1f)
                / Math.Max(0.0001f, unit);
        }
    }
}
