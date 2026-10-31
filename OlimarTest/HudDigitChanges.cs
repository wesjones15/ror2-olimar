using System;
namespace OlimarTest
{
    // zen/Number.h NumberPicCallBack: independent timers for changed decimal places.
    // Absolute time prevents IMGUI's Layout/Repaint passes advancing the pulse twice.
    internal sealed class HudDigitChanges
    {
        readonly int[] directions = new int[10];
        readonly float[] changedAt = new float[10];
        int previous;
        bool seen;
        public void Observe(int value, float now)
        {
            value = Math.Max(0, value);
            if (seen && value != previous)
            {
                long divisor = 1;
                for (int place = 0; place < directions.Length; place++, divisor *= 10)
                    if (value / divisor % 10 != previous / divisor % 10)
                    { directions[place] = Math.Sign(value - previous); changedAt[place] = now; }
            }
            previous = value;
            seen = true;
        }
        public int Direction(int place) => directions[place];
        public float Elapsed(int place, float now) => Math.Max(0f, now - changedAt[place]);
    }
}
