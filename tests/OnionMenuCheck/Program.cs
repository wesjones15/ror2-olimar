using System;
using OlimarTest;

static class Program
{
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    static uint Total(uint[] plan) => plan[0] + plan[1] + plan[2];

    static void Main()
    {
        int delta = 0;
        for (int i = 0; i < 1000; i++) delta = OnionMenuPolicy.Clamp(delta + 1, 20, 30, 100);
        Check(delta == 30, "holding call out stops at stock without wrapping to put away");
        for (int i = 0; i < 1000; i++) delta = OnionMenuPolicy.Clamp(delta - 1, 20, 30, 100);
        Check(delta == -20, "holding put away stops at squad without wrapping");
        Check(OnionMenuPolicy.Clamp(20, 5, 40, 3) == 3, "field cap limits precise requests");
        Check(OnionMenuPolicy.Clamp(20, 0, 0, 0) == 0, "empty and full field are safe");
        Check(OnionMenuPolicy.Clamp(-20, 3, 40, 40) == -3, "live squad changes reclamp selection");
        Check(OnionMenuPolicy.ReleaseCount(0, 100) == 0, "empty stock does not underflow");
        Check(OnionMenuPolicy.ReleaseCount(1, 100) == 0, "one stays inside");
        Check(OnionMenuPolicy.ReleaseCount(30, 100) == 29, "bulk release reserves one in total");
        Check(OnionMenuPolicy.ReleaseCount(30, 2) == 2, "bulk release respects field cap");
        Check(OnionMenuPolicy.ReleaseCount(30, 0) == 0, "full field releases none");
        var plan = OnionMenuPolicy.Distribute(OnionMenuPolicy.ReleaseCount(30, 100), 7, 11, 12);
        Check(plan[0] == 7 && plan[1] == 11 && plan[2] == 11, "bulk release spans every colour");
        Check(OnionMenuPolicy.ReleaseCount(30 - Total(plan), 100) == 0, "repeating release never puts Pikmin away or empties reserve");
        plan = OnionMenuPolicy.Distribute(10, 3, 4, 8);
        Check(plan[0] == 3 && plan[1] == 4 && plan[2] == 3, "precise count spans colours");
        Check(Total(OnionMenuPolicy.Distribute(15, 3, 4, 8)) == 15, "whole squad returns across colours");
        Check(Total(OnionMenuPolicy.Distribute(100, 3, 4, 8)) == 15, "stale request cannot exceed available Pikmin");
        Console.WriteLine("OnionMenuCheck: ok (count bounds, field cap, mixed colours, reserve, repeated release)");
    }
}
