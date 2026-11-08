// OnionMenuPolicy.Ready compares against Native.OnionAnimDormant. The real Native class is the P/Invoke surface of
// pikmin.dll, so the check carries the one constant it needs; Program.cs pins the same codes (7 folded, 8 reviving).
namespace OlimarTest
{
    internal static class Native
    {
        public const byte OnionAnimDormant = 7, OnionAnimReviving = 8;
    }
}
