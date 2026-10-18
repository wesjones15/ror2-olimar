using System;
using System.Collections.Generic;

namespace OlimarTest
{
    // Server gameplay time: each killer has an independent gate. Failed placement
    // and kills during cooldown neither consume quota nor queue future rewards.
    internal sealed class ShipRepairDropPacing
    {
        readonly Dictionary<uint, double> readyAt = new Dictionary<uint, double>();
        public void Reset() => readyAt.Clear();
        public bool Ready(uint user, double now) => user != 0 && !double.IsNaN(now) && !double.IsInfinity(now) &&
            (!readyAt.TryGetValue(user, out var ready) || now >= ready);
        public void Dropped(uint user, double now, double interval)
        {
            if (Ready(user, now)) readyAt[user] = now + Math.Max(0, interval);
        }
    }
}
