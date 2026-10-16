using System;
using System.Linq;
using OlimarTest;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main()
    {
        var run = new ShipRepairProgress();
        run.EnterStage();
        Check(!run.Receive(run.Epoch, 0), "arrival cannot award parts");
        run.Tick(50, false);
        Check(run.Seconds == 0, "first arrival is excluded");
        run.Release();
        run.Tick(10, false); run.Tick(20, true); run.Tick(double.NaN, false);
        Check(run.Seconds == 10, "pause/loading/invalid clock values");
        var selected = run.Sample(123);
        Check(selected.Length == 10 && selected.Distinct().Count() == 10, "ten unique candidates");
        Check(selected.SequenceEqual(run.Sample(123)), "seeded reproducibility");
        foreach (int part in selected.Take(7)) Check(run.Receive(run.Epoch, part), "valid receipt");
        Check(!run.Receive(run.Epoch, selected[0]), "duplicate receipt");
        Check(!run.Receive(run.Epoch, -1) && !run.Receive(run.Epoch, 30), "range validation");
        uint previousEpoch = run.Epoch;
        run.CloseStage();
        Check(!run.Receive(previousEpoch, selected[7]), "departure closes receipt boundary");
        run.EnterStage();
        run.Tick(2, false);
        Check(run.Seconds == 12, "later arrival counts");
        run.Release();
        Check(!run.Receive(previousEpoch, selected[7]), "stale stage receipt");
        Check(run.Sample(999).All(p => !selected.Take(7).Contains(p)), "recovered parts excluded");
        Check(run.Sample(123, 30).Contains(selected[7]), "abandoned part remains eligible");
        foreach (int part in run.Sample(999, 30).Take(19)) Check(run.Receive(run.Epoch, part), "later deliveries");
        Check(run.Count == 26 && run.Sample(123).Length == 4, "final four only");
        foreach (int part in run.Sample(123)) Check(run.Receive(run.Epoch, part), "finish recovery");
        Check(run.Count == 30 && run.Mask == ShipRepairProgress.AllParts && run.State == ShipRepairProgress.Phase.Winning, "win at thirty unique parts");
        run.Fail(); run.CloseStage(); run.EnterStage(); run.Tick(100, false);
        Check(run.State == ShipRepairProgress.Phase.Winning && run.Seconds == 12 && run.Count == 30, "secured win is immutable");
        run.Complete(); Check(run.State == ShipRepairProgress.Phase.Results, "results transition");
        var failure = new ShipRepairProgress(); failure.EnterStage(); failure.Release(); failure.Tick(4, false); failure.Fail(); failure.Tick(20, false);
        Check(failure.State == ShipRepairProgress.Phase.Failed && failure.Seconds == 4, "failure freezes metrics");
        Check(new ShipRepairProgress().Count == 0, "new run starts empty");
        Console.WriteLine("PASS: sampling, receipts, epoch boundaries, clocks, failure, victory, and restart.");
        return 0;
    }
}
