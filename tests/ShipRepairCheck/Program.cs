using System;
using System.Linq;
using OlimarTest;
using Ror2Common;

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
        var pacing = new ShipRepairDropPacing();
        Check(pacing.Ready(11, 0) && pacing.Ready(22, 0), "each player can earn an initial drop");
        pacing.Dropped(11, 0, 30);
        Check(!pacing.Ready(11, 29.99) && pacing.Ready(22, 0), "one player's drop never gates another player");
        pacing.Dropped(22, 10, 30);
        Check(pacing.Ready(11, 30) && !pacing.Ready(22, 30) && pacing.Ready(22, 40), "independent exact cooldown boundaries");
        Check(!pacing.Ready(0, 100) && !pacing.Ready(11, double.NaN), "unattributed and invalid clock values rejected");
        pacing.Reset();
        int burstDrops = 0;
        for (int time = 0; time <= 60; time++)
            foreach (uint user in new uint[] { 11, 22, 33, 44 })
                if (pacing.Ready(user, time)) { pacing.Dropped(user, time, 30); burstDrops++; }
        Check(burstDrops == 12, "kill fest yields at most one drop per player per thirty seconds, with no queued burst");
        pacing.Reset(); Check(pacing.Ready(11, 0), "stage reset clears old cooldowns");
        var race = new ShipRepairRace();
        race.Enroll(11, "A"); race.Enroll(22, "B");
        Check(race.Receive(11, 4) && race.Receive(22, 4), "same part is independently unique for each racer");
        Check(!race.Receive(11, 4) && !race.Salvage(11, 5), "duplicate progress and unowned salvage rejected");
        Check(race.Salvage(11, 4) && race.Players[11].Salvaged == 1 && race.Players[11].Order.Count == 1, "Onion salvage does not advance victory count");
        Check(ShipRepairRace.EnemyBudget(4, true) == 28 && ShipRepairRace.EnemyBudget(4, false) == 7, "non-boss supply scales only for races");
        var soloIds = ShipRepairRace.StageIds(123, 1, 10, new uint[] { 0 });
        var groupIds = ShipRepairRace.StageIds(123, 1, 31, new uint[] { 0, 0, 0, 0 });
        Check(soloIds.Length == 10 && soloIds.Distinct().Count() == 10 && groupIds.Length == 31, "solo and four-player supply");
        Check(ShipRepairRace.StageIds(5, 3, 31, new[] { ShipRepairProgress.AllParts ^ 1u, ShipRepairProgress.AllParts ^ 2u }).OrderBy(p => p).SequenceEqual(new[] { 0, 1 }), "near-complete races only offer needed copies");
        foreach (int id in Enumerable.Range(0, 30).Where(p => p != 4)) Check(race.Receive(22, id), "rival recovery");
        Check(race.Winner == 22 && !race.Receive(11, 5) && !race.Salvage(11, 4), "first completed Dolphin secures race");
        Check(race.Enroll(11, "A renamed").Order.Count == 1, "stage enrollment retains progress");
        var packet = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 1u << 4,
            State = ShipRepairProgress.Phase.Playing, Seconds = 124.5f, Stages = 2, Order = new[] { 4 },
            Parts = new[] { new ShipRepairPacket.Part { Id = 4, Slot = 4, Pellet = 22, Name = "Main Engine", Weight = 20, Delivered = true, Spawned = true },
                new ShipRepairPacket.Part { Id = 5, Slot = 5, Pellet = uint.MaxValue - 5, Name = "Radar", Weight = 10, X = 15.5f, EnemyDrop = true } } };
        var bytes = packet.Write();
        Check(ShipRepairPacket.Read(new ByteReader(bytes), out var decoded) && decoded.RunId == packet.RunId && decoded.Parts[1].X == 15.5f, "objective packet round trip");
        Check(decoded.Parts[1].EnemyDrop && !decoded.Parts[1].Spawned, "pending enemy objective round trip");
        packet.Parts[1].Delivered = true;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "unspawned enemy objective cannot be delivered");
        packet.Parts[1].Delivered = false; packet.Parts[1].Spawned = true; packet.Parts[1].Pellet = 23;
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Parts[1].Spawned, "physical enemy drop round trip");
        packet.Parts[1].Spawned = false; packet.Parts[1].Pellet = uint.MaxValue - 5;
        for (int length = 0; length < bytes.Length; length++)
            Check(!ShipRepairPacket.Read(new ByteReader(bytes.Take(length).ToArray()), out _), "truncated packet accepted");
        var corrupt = (byte[])bytes.Clone(); corrupt[0] = 99;
        Check(!ShipRepairPacket.Read(new ByteReader(corrupt), out _), "wrong protocol");
        corrupt = (byte[])bytes.Clone(); corrupt[1] = 29;
        Check(!ShipRepairPacket.Read(new ByteReader(corrupt), out _), "wrong ABI");
        packet.Order = new[] { 4, 4 };
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "duplicate journal identity");
        packet.Order = new[] { 4 }; packet.Seconds = float.NaN;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "invalid clock");
        packet.Seconds = 10; packet.State = ShipRepairProgress.Phase.Winning;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "win without thirty parts");
        packet.State = ShipRepairProgress.Phase.Playing; packet.Parts[1].Id = 30;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "out-of-range world part");
        packet.Parts[1].Id = 5;
        packet.Competitive = true; packet.Racers = race.Players.Values.ToArray();
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "complete racer requires authoritative winner");
        packet.Winner = 22; packet.State = ShipRepairProgress.Phase.Winning;
        packet.Mask = ShipRepairProgress.AllParts; packet.Order = race.Players[22].Order.ToArray();
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Winner == 22 && decoded.Racers.First(p => p.User == 11).Salvaged == 1, "race winner and salvage packet round trip");
        packet.State = ShipRepairProgress.Phase.Playing; packet.Winner = 0;
        packet.Racers = new[] { new ShipRepairRacer { User = 11, Name = "A" }, new ShipRepairRacer { User = 22, Name = "B" } };
        packet.Mask = 0; packet.Order = Array.Empty<int>();
        packet.Parts[1].EnemyDrop = false; packet.Parts[1].Recycling = true;
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Parts[1].Recycling, "map part recycling round trip");
        packet.Parts[1].Recycling = false;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "unspawned map part needs recycle state");
        Console.WriteLine("PASS: sampling, receipts, epoch boundaries, clocks, failure, victory, restart, and objective packet validation.");
        return 0;
    }
}
