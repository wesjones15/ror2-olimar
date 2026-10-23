using System;
using OlimarTest;

static class Program
{
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    static uint Total(uint[] plan) => plan[0] + plan[1] + plan[2];

    static void Main()
    {
        Check(OnionMenuPolicy.InBeam(false, false, false, false, true, 0, 0, 0), "standing in landed beam enables interact");
        Check(!OnionMenuPolicy.InBeam(true, true, false, false, true, 0, 0, 0), "following Onion before descent has no menu");
        Check(OnionMenuPolicy.InBeam(true, true, true, false, true, 0, 0, 0), "hovering Onion at beam height enables menu");
        Check(!OnionMenuPolicy.InBeam(true, false, true, false, true, 0, 0, 0), "landing or taking-off Onion never opens menu");
        Check(!OnionMenuPolicy.InBeam(false, false, false, true, true, 0, 0, 0), "dormant Onion never opens menu");
        Check(!OnionMenuPolicy.InBeam(false, false, false, false, false, 0, 0, 0), "jumping through beam does not open menu");
        Check(!OnionMenuPolicy.InBeam(false, false, false, false, true, 71, 0, 0), "outside beam keeps X for relocation");
        Check(!OnionMenuPolicy.InBeam(true, true, true, false, true, 0, 30, 0), "above beam ground level cannot interact");
        Check(!OnionMenuPolicy.InBeam(true, true, true, false, true, 0, -30, 0), "below the Onion on another floor cannot interact");
        Check(OnionMenuPolicy.UnderHover(1f, 0f, false) && !OnionMenuPolicy.UnderHover(2f, 0f, false), "backtrack entry is narrow");
        Check(OnionMenuPolicy.UnderHover(2.7f, 0f, true) && !OnionMenuPolicy.UnderHover(2.9f, 0f, true), "beam exit has hysteresis");
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
        var flying = new FlyingXPolicy();
        Check(flying.Step(1f, true, true, false, true) == FlyingXAction.None, "flying first X waits");
        Check(flying.Step(1.5f, true, true, false, false) == FlyingXAction.Release,
            "second X dumps even after the landing charge was spent");
        Check(flying.Step(2f, true, true, false, true) == FlyingXAction.None, "new flying press starts a window");
        Check(flying.Step(2.8f, false, true, false, true) == FlyingXAction.Land,
            "single X lands only after the double-press window");
        Check(flying.Step(3f, true, true, false, false) == FlyingXAction.None, "cooldown press still listens");
        Check(flying.Step(3.8f, false, true, false, true) == FlyingXAction.None,
            "cooldown press does not land if the charge refills during the window");
        Check(flying.Step(4f, true, true, true, true) == FlyingXAction.None,
            "another RoR2 interact prompt claims X");
        Check(flying.Step(4.8f, false, true, false, true) == FlyingXAction.None,
            "claimed X never lands the Onion");
        Check(flying.Step(5f, true, true, false, true) == FlyingXAction.None, "flying press queues");
        Check(flying.Step(5.1f, false, false, false, true) == FlyingXAction.None, "landing cancels pending X");
        float jump = OlimarJump.Height(15f, 15f, -30f, 0.04f);
        Check(jump * 0.04f > 15f * 15f / 60f, "Commando jump height plus ledge clearance");
        Check(OlimarJump.Height(8f, 15f, -30f, 0.04f) == jump, "base Commando height is the minimum");
        Check(OlimarJump.Height(20f, 15f, -30f, 0.04f) > jump, "jump-power items increase height");
        Check(Math.Abs(OlimarJump.Height(15f, 15f, -30f, 0.02f) * 0.02f - jump * 0.04f) < 0.001f,
            "smaller model scale does not reduce world-space jump height");
        Console.WriteLine("OnionMenuCheck: ok (count bounds, field cap, mixed colours, reserve, repeated release)");
    }
}
