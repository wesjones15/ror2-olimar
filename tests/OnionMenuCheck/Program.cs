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
        Check(flying.Step(1f, true, false) == FlyingXAction.None, "hold begins");
        Check(flying.Step(1.59f, true, false) == FlyingXAction.None, "less than .6s waits");
        Check(flying.Step(1.61f, true, false) == FlyingXAction.Land, "hold relocates at .6s");
        Check(flying.Step(3f, true, false) == FlyingXAction.None, "continuing hold cannot fire twice");
        flying.Step(3.1f, false, false);
        flying.Step(4f, true, false);
        Check(flying.Step(4.1f, false, false) == FlyingXAction.None, "a short tap never relocates");
        flying.Step(5f, true, false);
        flying.Step(5.1f, false, false);
        Check(flying.Step(5.2f, true, false) == FlyingXAction.None, "two quick taps no longer dump or relocate");
        Check(flying.Step(6f, true, false) == FlyingXAction.Land, "the second press only counts as its own hold");
        flying.Cancel();
        flying.Step(7f, true, false);
        Check(flying.Step(7.7f, true, true) == FlyingXAction.None, "interact cancels pending relocation");
        Check(PelletDropPolicy.Number(false, false, PelletDropPolicy.Size.Small) == 1, "ordinary enemy: 1");
        Check(PelletDropPolicy.Number(false, false, PelletDropPolicy.Size.Large) == 5, "heavy body (Beetle Guard/Golem): 5");
        Check(PelletDropPolicy.Number(false, false, PelletDropPolicy.Size.Huge) == 10, "bigger than a heavy: 10");
        Check(PelletDropPolicy.Number(false, true, PelletDropPolicy.Size.Large) == 10, "named boss: 10");
        Check(PelletDropPolicy.Number(true, true, PelletDropPolicy.Size.Huge) == 20, "stage boss wins: 20");
        float jump = OlimarJump.Height(15f, 15f, -30f, 0.04f);
        Check(jump * 0.04f > 15f * 15f / 60f, "Commando jump height plus ledge clearance");
        Check(OlimarJump.Height(8f, 15f, -30f, 0.04f) == jump, "base Commando height is the minimum");
        Check(OlimarJump.Height(20f, 15f, -30f, 0.04f) > jump, "jump-power items increase height");
        Check(Math.Abs(OlimarJump.Height(15f, 15f, -30f, 0.02f) * 0.02f - jump * 0.04f) < 0.001f,
            "smaller model scale does not reduce world-space jump height");
        var ride = OnionRidePolicy.Begin();
        float scale = 0f;
        for (int i = 0; i < 4 * 30; i++) scale = ride.Step(1f / 30f, 3f, false);
        Check(!ride.graceOver && ride.activeLeft == OnionRidePolicy.ActiveSeconds && scale == OnionRidePolicy.SpeedScale,
            "grace holds the active clock while he stays within 10 m for under 5 s");
        ride.Step(1f / 30f, 10.5f, false);
        Check(ride.graceOver, "leaving the 10 m circle ends the grace at once");
        ride.Step(0.5f, 0f, false);
        Check(Math.Abs(ride.activeLeft - (OnionRidePolicy.ActiveSeconds - 0.5f)) < 0.001f, "returning to the start does not reset or pause the clock");
        var slow = OnionRidePolicy.Begin();
        for (int i = 0; i < 5 * 30 + 1; i++) slow.Step(1f / 30f, 0f, false);
        Check(slow.graceOver, "five seconds ends the grace even standing still");
        for (int i = 0; i < 10 * 30 + 2; i++) slow.Step(1f / 30f, 0f, false);
        Check(slow.Expired, "ten active seconds end the ride");
        var fast = OnionRidePolicy.Begin();
        Check(fast.Step(1f / 30f, 0f, true) == OnionRidePolicy.BoostScale && fast.Boosting, "stick click boosts to triple speed");
        float after = 0f;
        for (int i = 0; i < 2 * 30 + 3; i++) after = fast.Step(1f / 30f, 0f, false);
        Check(after == OnionRidePolicy.SpeedScale && !fast.Boosting, "the boost lasts two seconds");
        Check(OnionRidePolicy.PerLeg(7, 0) + OnionRidePolicy.PerLeg(7, 1) + OnionRidePolicy.PerLeg(7, 2) == 7
              && OnionRidePolicy.PerLeg(6, 2) == 2, "the squad splits across the three legs");
        Console.WriteLine("OnionMenuCheck: ok (count bounds, field cap, mixed colours, reserve, repeated release)");
    }
}
