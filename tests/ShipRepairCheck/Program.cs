using System;
using System.Linq;
using OlimarTest;
using Ror2Common;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main()
    {
        Check(FlintSpawnPolicy.Encounter(.3f, true, 0, .5f, .12f), "uncommon Ship encounter");
        Check(!FlintSpawnPolicy.Encounter(.3f, false, 1, .5f, .12f), "other modes are rarer");
        Check(FlintSpawnPolicy.Encounter(.1f, false, 1, .5f, .12f), "rare Olimar encounter");
        Check(!FlintSpawnPolicy.Encounter(0f, false, 0, .5f, .12f), "no Olimar outside Ship: no beetle");
        Check(!FlintSpawnPolicy.Encounter(0f, true, 1, 0f, .12f), "zero chance disables");
        Check(TekiRoster.Of(56).NoCard && TekiRoster.Of(56).NoCorpse && TekiRoster.Of(56).Hazard, "placed immortal beetle gives no death reward");
        var digits = new HudDigitChanges();
        digits.Observe(119, 0f); Check(digits.Direction(0) == 0, "first HUD draw is calm");
        digits.Observe(120, 1f);
        Check(digits.Direction(0) == 1 && digits.Direction(1) == 1 && digits.Direction(2) == 0, "only changed decimal places bounce");
        digits.Observe(120, 1.2f); Check(Math.Abs(digits.Elapsed(0, 1.2f) - .2f) < .0001f, "Repaint doesn't reset/advance animation");
        digits.Observe(119, 1.3f); Check(digits.Direction(0) == -1 && digits.Direction(1) == -1, "decreases contract");
        digits.Observe(999, 2f); digits.Observe(1000, 2.1f);
        Check(digits.Direction(3) == 1 && digits.Elapsed(0, 2.1f) == 0f, "carry and fast updates restart changed places");
        Console.WriteLine("PASS: Flint encounters and per-digit HUD animation tracking.");
        Check(PosySpawnPolicy.Target(true, 1, 0, 6, 2) == 6, "Ship Repair: common Posies for any survivor");
        Check(PosySpawnPolicy.Target(false, 4, 1, 6, 2) == 2, "Classic: population counts Olimars, not other survivors");
        Check(PosySpawnPolicy.Target(false, 4, 0, 6, 2) == 0, "Classic without Olimar has no Posies");
        Check(PosySpawnPolicy.Target(true, 100, 0, 6, 2) == 24 && PosySpawnPolicy.Target(false, 100, 100, 6, 2) == 8, "Posy stage caps");
        Check(PosySpawnPolicy.Target(true, 4, 1, 0, 2) == 0 && PosySpawnPolicy.Target(false, 4, 1, 6, -1) == 0, "disabled and negative Posy counts");
        Check(PosySpawnPolicy.RefillSeconds(true) < PosySpawnPolicy.RefillSeconds(false), "Classic replenishes less often");
        Check(PosySpawnPolicy.InitialSize(0) == 2 && PosySpawnPolicy.InitialSize(1) == 2 && PosySpawnPolicy.InitialSize(3) == 2, "every Posy spawns bloomed");
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
        Check(ShipRepairMusicPolicy.Track(123, 0) == 4, "Impact Site on stage one");
        uint previousSong = 4;
        for (int musicStage = 1; musicStage < 30; musicStage++)
        {
            uint song = ShipRepairMusicPolicy.Track(123, musicStage);
            Check(ShipRepairMusicPolicy.Tracks.Contains(song) && song != previousSong && song == ShipRepairMusicPolicy.Track(123, musicStage), "seeded stage music without consecutive repetition or dependence on join time");
            previousSong = song;
        }
        Check(ShipRepairMusicPolicy.Pikmin2Chance(0)==20 && ShipRepairMusicPolicy.Pikmin2Chance(2)==35 && ShipRepairMusicPolicy.Pikmin2Chance(5)==50,"Pikmin 2 is less common early and increases later");
        int earlyP2=0,lateP2=0;
        for (ulong seed=0;seed<1000;seed++) {
            uint early=ShipRepairMusicPolicy.MixedTrack(seed,0),late=ShipRepairMusicPolicy.MixedTrack(seed,6);
            if (early>=0x100) earlyP2++;if(late>=0x100) lateP2++;
            Check(early==ShipRepairMusicPolicy.MixedTrack(seed,0),"mixed music is deterministic for late joins");
        }
        Check(earlyP2>100 && earlyP2<300 && lateP2>400 && lateP2<600,"seeded music distribution keeps early Pikmin 1 majority");
        Check(ShipRepairMusicPolicy.Explore(true, true, true, false, false) &&
            !ShipRepairMusicPolicy.Explore(true, true, true, false, true) &&
            !ShipRepairMusicPolicy.Explore(true, false, true, false, false) &&
            !ShipRepairMusicPolicy.Explore(true, true, true, true, false) &&
            !ShipRepairMusicPolicy.Explore(false, true, true, false, false), "stage music yields to bosses, other modes, victory and local opt-out");
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
        Check(race.Completer == 22 && race.Winner == 0 && !race.Receive(11, 5) && !race.Salvage(11, 4),
            "first completed Dolphin closes part delivery without choosing the score winner");
        race.FinalizeScores(user => user == 11 ? 1500 : 49);
        Check(race.Winner == 11 && race.Players[11].Score == 31 && race.Players[22].Score == 30,
            "Pikmin bonus can beat the first player with thirty parts");
        var tied = new ShipRepairRace(); tied.Enroll(33, "C"); tied.Enroll(44, "D");
        foreach (int id in Enumerable.Range(0, 29)) Check(tied.Receive(44, id), "tie rival parts");
        foreach (int id in Enumerable.Range(0, 30)) Check(tied.Receive(33, id), "tie completer parts");
        tied.FinalizeScores(user => user == 44 ? 50 : 0);
        Check(tied.Winner == 33, "equal score favors more repaired parts");
        Check(tied.Players[44].Score == 30 && race.Players[22].Score == 30,
            "bonus starts at fifty Pikmin, not forty-nine");
        Check(race.Enroll(11, "A renamed").Order.Count == 1, "stage enrollment retains progress");
        var packet = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 1u << 4,
            State = ShipRepairProgress.Phase.Playing, Seconds = 124.5f, Stages = 2, Order = new[] { 4 },
            Parts = new[] { new ShipRepairPacket.Part { Id = 4, Slot = 4, Pellet = 22, Name = "Main Engine", Weight = 20, Delivered = true, Spawned = true },
                new ShipRepairPacket.Part { Id = 5, Slot = 5, Pellet = uint.MaxValue - 5, Name = "Radar", Weight = 10, X = 15.5f, EnemyDrop = true } } };
        var bytes = packet.Write();
        Check(ShipRepairPacket.Read(new ByteReader(bytes), out var decoded) && decoded.RunId == packet.RunId && decoded.Parts.Length == 1 && decoded.Parts[0].Id == 5, "objective packet round trip (delivered parts are left out)");
        Check(decoded.Parts[0].EnemyDrop && !decoded.Parts[0].Spawned && decoded.Parts[0].Pellet == uint.MaxValue - 5, "pending enemy objective round trip");
        packet.Parts[1].Id = 4;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "a part already recovered cannot lie on the map");
        packet.Parts[1].Id = 5; packet.Parts[1].Spawned = true; packet.Parts[1].Pellet = 23;
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Parts[0].Spawned && decoded.Parts[0].Pellet == 23, "physical enemy drop round trip");
        packet.Parts[1].Spawned = false; packet.Parts[1].Pellet = uint.MaxValue - 5;
        var crowded = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 0, State = ShipRepairProgress.Phase.Playing,
            Seconds = 5f, Stages = 1, Order = new int[0], Parts = Enumerable.Range(0, 17).Select(i => new ShipRepairPacket.Part {
                Id = i, Slot = i, Pellet = (uint)(100 + i), Name = "Long Part Name " + i, Weight = 20, X = i, Y = 2, Z = 3, Spawned = true, EnemyDrop = i == 0, StageBoss = i == 0 }).ToArray() };
        Check(crowded.Write().Length < 700, "seventeen parts fit a reliable message with room to spare (0.3.8 was 1103 bytes)");
        crowded.Parts = Enumerable.Range(0, 400).Select(i => new ShipRepairPacket.Part { Id = i % 30, Slot = i, Pellet = (uint)(100 + i), Weight = 20, Spawned = true }).ToArray();
        Check(crowded.WriteFitting().Length <= ShipRepairPacket.MaxBytes, "oversized part lists are trimmed to the message limit");
        crowded.Parts = Enumerable.Range(0, 30).Select(i => new ShipRepairPacket.Part { Id = i, Slot = i, Pellet = (uint)(100 + i), Weight = 20, X = i, Spawned = true }).ToArray();
        Check(crowded.Write().Length <= ShipRepairPacket.MaxBytes && ShipRepairPacket.Read(new ByteReader(crowded.Write()), out var thirty) && thirty.Parts.Length == 30,
            "all thirty spawned parts fit and decode");
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
        packet.Winner = 11; packet.State = ShipRepairProgress.Phase.Winning;
        packet.Mask = ShipRepairProgress.AllParts; packet.Order = race.Players[22].Order.ToArray();
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Winner == 11 &&
            decoded.Racers.First(p => p.User == 11).Pikmin == 1500 && decoded.Racers.First(p => p.User == 11).Salvaged == 1,
            "score winner differs from completer and Pikmin counts survive packet round trip");
        packet.Winner = 22;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "packet rejects a parts completer who lost on score");
        packet.Winner = 11;
        race.Players[11].Pikmin = -1;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "negative Pikmin score rejected");
        race.Players[11].Pikmin = 1500;
        packet.State = ShipRepairProgress.Phase.Playing; packet.Winner = 0;
        packet.Racers = new[] { new ShipRepairRacer { User = 11, Name = "A" }, new ShipRepairRacer { User = 22, Name = "B" } };
        packet.Mask = 0; packet.Order = Array.Empty<int>();
        packet.State = ShipRepairProgress.Phase.Failed;
        packet.Racers[0].Pikmin = 50;
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Racers[0].Score == 1,
            "failed run preserves final Pikmin bonus without declaring a winner");
        packet.State = ShipRepairProgress.Phase.Playing;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _),
            "active race cannot claim final Pikmin before the end");
        packet.Racers[0].Pikmin = 0;
        packet.Parts[1].EnemyDrop = false; packet.Parts[1].Recycling = true;
        Check(ShipRepairPacket.Read(new ByteReader(packet.Write()), out decoded) && decoded.Parts[0].Recycling, "map part recycling round trip");
        packet.Parts[1].Recycling = false;
        Check(!ShipRepairPacket.Read(new ByteReader(packet.Write()), out _), "unspawned map part needs recycle state");
        // Pikmin enemies: a big one always rewards a part, a small one only by the usual rule.
        Check(TekiRules.RewardsPart(true, false, false, false, 0.99, 0.04), "big enemy ignores the drop interval and chance");
        Check(!TekiRules.RewardsPart(false, false, false, true, 0.5, 0.04), "small enemy needs the 4% roll");
        Check(TekiRules.RewardsPart(false, false, false, true, 0.01, 0.04), "small enemy can roll a drop");
        Check(!TekiRules.RewardsPart(false, false, false, false, 0.01, 0.04), "small enemy waits for the killer's interval");
        Check(TekiRules.RewardsPart(false, false, true, true, 0.99, 0.04), "powerful enemy drops without a roll");
        Check(TekiRules.RewardsPart(false, true, false, false, 0.99, 0.04), "stage boss ignores the interval");
        // In a race a big enemy's part is one its killer lacks; in the shared game, one the crew lacks.
        uint killer = 0b1011, rival = 0b0100, crew = 0b0111;
        for (uint epoch = 1; epoch < 40; epoch++)
        {
            var raced = ShipRepairRace.StageIds(77, epoch, 1, TekiRules.BenefitMasks(true, killer, new[] { killer, rival }, 0));
            Check(raced.Length == 1 && (killer & (1u << raced[0])) == 0, "race reward is a part the killer lacks");
            var shared = ShipRepairRace.StageIds(77, epoch, 1, TekiRules.BenefitMasks(false, null, new[] { killer, rival }, crew));
            Check(shared.Length == 1 && (crew & (1u << shared[0])) == 0, "shared reward is a part the crew lacks");
        }
        Check(ShipRepairRace.StageIds(77, 1, 1, TekiRules.BenefitMasks(true, ShipRepairProgress.AllParts, new[] { killer }, 0)).Length == 0,
            "a killer with every part has nothing to be rewarded with");
        Check(TekiRules.BenefitMasks(true, null, new[] { killer, rival }, 0).SequenceEqual(new[] { killer, rival }), "unknown killer: any racer's need");
        // The disc's corpse: Dwarf 3-6 Pikmin, Spotty 10-20; the multiplier never makes it 0 or inverts it.
        Check(TekiRules.Carriers(3, 6, 1.0) == (3u, 6u) && TekiRules.Carriers(10, 20, 0.5) == (5u, 10u), "carrier counts");
        Check(TekiRules.Carriers(3, 6, 0.0) == (1u, 1u) && TekiRules.Carriers(10, 20, -2.0).min >= 1, "carrier floor");
        Check(TekiRules.CorpseDrawId(5) != 5 && TekiRules.CorpseDrawId(5) >= 0x8000_0000u, "corpse draw ids sit above the bodies'");
        // The roster of Pikmin enemies: kinds are the native table's order, every one has a template and sane
        // director numbers, and the big ones (which always drop a part) cost more than the small ones.
        for (int i = 0; i < TekiRoster.All.Length; i++)
        {
            var e = TekiRoster.All[i];
            Check(e.Kind == i && TekiRoster.Of((byte)i) == e, "roster kind " + i + " is its index");
            Check(!string.IsNullOrEmpty(e.Body) && !string.IsNullOrEmpty(e.Master) && e.Body.EndsWith("Body.prefab") && e.Master.EndsWith("Master.prefab"), "template of " + e.Display);
            Check((e.Weight > 0 || e.NoCard) && e.Cost > 0 && e.Token == e.Token.ToUpperInvariant() && !e.Token.Contains(" "), "numbers and token of " + e.Display);
        }
        Check(TekiRoster.All.Length == 57 && TekiRoster.Of(57) == null, "fifty-seven kinds including P1 Kogane");
        Check(TekiRoster.All.Select(e => e.Token).Distinct().Count() == TekiRoster.All.Length, "tokens are unique");
        int smallest = TekiRoster.All.Where(e => e.Big).Min(e => e.Cost), largest = TekiRoster.All.Where(e => !e.Big).Max(e => e.Cost);
        Check(smallest > largest, "every big enemy costs more than any small one");
        Check(TekiRoster.All.Where(e => !e.Big).Sum(e => e.Weight) > 3 * TekiRoster.All.Where(e => e.Big).Sum(e => e.Weight) / 2,
            "small enemies are the common ones");
        Check(TekiRoster.All.Where(e => e.Flying).All(e => e.Kind == 8 || e.Kind == 14 || e.Kind == 18 || e.Kind == 46), "the fliers are the Snitchbug, the Puffy Blowhog, the Honeywisp and the Spectralid");
        Check(TekiRoster.All.All(e => e.Ported), "every kind runs the decomp's strategy");
        Check(TekiRoster.All.Where(e => e.Ephemeral).Select(e => e.Display).OrderBy(x => x).SequenceEqual(new[] { "Honeywisp", "Rolling Boulder" }) && TekiRoster.All.Where(e => e.Water).All(e => e.NoCard), "the Honeywisp and the boulder are ephemeral, water enemies are placed not drawn");
        // Hazards cannot be killed. The fixtures are placed by Ship Repair; Pikmin 2's loot bugs, Bomb-rock and Spectralids
        // (hit to flip, set off or scatter, never killed) are drawn like any other enemy.
        Check(TekiRoster.All.Where(e => e.Hazard).Select(e => e.Display).OrderBy(x => x).SequenceEqual(new[] { "Bomb-rock", "Doodlebug", "Fire Geyser", "Gas Pipe",
            "Iridescent Flint Beetle", "Iridescent Flint Beetle (Pikmin 2)", "Iridescent Glint Beetle", "Pearly Clamclamp", "Unmarked Spectralid" }), "the enemies that cannot be killed");
        Check(TekiRoster.All.Where(e => e.Hazard && e.Weight == 0).All(e => e.NoCard) && TekiRoster.All.Where(e => e.Hazard && e.NoCard).Select(e => e.Display).OrderBy(x => x)
            .SequenceEqual(new[] { "Fire Geyser", "Gas Pipe", "Iridescent Flint Beetle", "Iridescent Flint Beetle (Pikmin 2)", "Pearly Clamclamp" }), "geysers, gas pipes and shells are placed, not drawn");
        Check(TekiRoster.All.Where(e => e.Water).Select(e => e.Display).OrderBy(x => x).SequenceEqual(new[] { "Water Dumple", "Wogpole" }), "the waterborne enemies");
        // Death and soul effects follow the carcass size (BTeki::createDeadEffect / createSoulEffect).
        Check(TekiRules.DeathEffects(2).SequenceEqual(new[] { 61, 62, 63 }) && TekiRules.DeathEffects(0).SequenceEqual(new[] { 55, 56, 57 })
            && TekiRules.DeathEffects(1).SequenceEqual(new[] { 58, 59, 60 }) && TekiRules.DeathEffects(3).Length == 0, "death effects by size");
        Check(TekiRules.SoulEffect(2) == 66 && TekiRules.SoulEffect(1) == 65 && TekiRules.SoulEffect(0) == 64 && TekiRules.SoulEffect(3) == -1, "soul by size");
        Console.WriteLine("PASS: Pikmin enemy part rewards, beneficiary masks and corpse sizes.");
        Console.WriteLine("PASS: sampling, receipts, epoch boundaries, clocks, failure, victory, restart, and objective packet validation.");
        TreasureCheck();
        SurvivorOrderCheck();
        return 0;
    }

    // Pikmin 2 treasures: tiers, worth (always under a part), seeded stage picks, and the state message's treasure section.
    static void TreasureCheck()
    {
        Check(ShipRepairTreasures.TierOf(10) == ShipRepairTreasures.Tier.Common && ShipRepairTreasures.TierOf(59) == ShipRepairTreasures.Tier.Common
            && ShipRepairTreasures.TierOf(60) == ShipRepairTreasures.Tier.Fine && ShipRepairTreasures.TierOf(129) == ShipRepairTreasures.Tier.Fine
            && ShipRepairTreasures.TierOf(130) == ShipRepairTreasures.Tier.Rare && ShipRepairTreasures.TierOf(299) == ShipRepairTreasures.Tier.Rare
            && ShipRepairTreasures.TierOf(300) == ShipRepairTreasures.Tier.Legendary && ShipRepairTreasures.TierOf(3000) == ShipRepairTreasures.Tier.Legendary, "tiers by Pokos");
        // Whatever the setting, the richest treasure is worth less than one Dolphin part (100 hundredths).
        foreach (float weight in new[] { 0f, 0.05f, ShipRepairTreasures.DefaultWeight, 0.2f, 1f, 100f, float.NaN, float.PositiveInfinity, -3f })
            foreach (uint pokos in new uint[] { 10, 100, 250, 1000, 3000 })
                Check(ShipRepairTreasures.Points(pokos, weight) >= 0 && ShipRepairTreasures.Points(pokos, weight) < 100, "a treasure is worth less than a part at weight " + weight);
        Check(ShipRepairTreasures.Points(40, 0.1f) == 10 && ShipRepairTreasures.Points(100, 0.1f) == 15 && ShipRepairTreasures.Points(200, 0.1f) == 25 && ShipRepairTreasures.Points(1000, 0.1f) == 40,
            "default worth: 0.10, 0.15, 0.25 and 0.40 of a part by tier");
        Check(ShipRepairTreasures.Points(40, 0f) == 0 && ShipRepairTreasures.Points(1000, 5f) == 80, "weight 0 turns the category off, and the weight is capped at 0.2");
        Check(ShipRepairTreasures.Whole(99) == 0 && ShipRepairTreasures.Whole(100) == 1 && ShipRepairTreasures.Whole(-5) == 0 && ShipRepairTreasures.Format(125) == "1.25" && ShipRepairTreasures.Format(10) == "0.1", "whole parts' worth and display");

        // A table like the disc's: carriers 1..60 and Pokos spread over the tiers.
        var table = Enumerable.Range(0, ShipRepairTreasures.TableSize).Select(i => new ShipRepairTreasures.Info { Index = i, Carriers = (uint)(1 + i % 60), Pokos = (uint)(10 + i * 13 % 1000) }).ToArray();
        var a = ShipRepairTreasures.Pick(77, 3, 5, table, null, 25);
        Check(a.Length == 5 && a.Distinct().Count() == 5, "five distinct treasures");
        Check(a.SequenceEqual(ShipRepairTreasures.Pick(77, 3, 5, table, null, 25)), "seeded, so the same every time");
        Check(!a.SequenceEqual(ShipRepairTreasures.Pick(77, 4, 5, table, null, 25)), "another stage, other treasures");
        Check(a.All(i => table[i].Carriers <= 25), "none needs more Pikmin than the limit");
        var taken = new System.Collections.Generic.HashSet<int>(a);
        Check(ShipRepairTreasures.Pick(77, 3, 5, table, taken, 25).All(i => !taken.Contains(i)), "a collected treasure does not come again");
        Check(ShipRepairTreasures.Pick(1, 1, 500, table, null, 25).Length == table.Count(t => t.Carriers <= 25), "asking for more than exist gives all that qualify");
        Check(ShipRepairTreasures.Pick(1, 1, 5, table, null, 25).Length == 5 && ShipRepairTreasures.Pick(1, 1, 0, table, null, 25).Length == 0, "nothing to pick");
        Check(ShipRepairTreasures.Pick(1, 1, 500, table, null, 0).Length == table.Length && ShipRepairTreasures.Pick(1, 1, 500, table, null, 5).Length == table.Count(t => t.Carriers <= 5), "a limit of 0 is no limit");
        int[] tiers = new int[4];
        for (uint epoch = 1; epoch <= 400; epoch++)
            foreach (int i in ShipRepairTreasures.Pick(9, epoch, 5, table, null, 60)) tiers[(int)ShipRepairTreasures.TierOf(table[i].Pokos)]++;
        Check(tiers[0] > tiers[1] || tiers[1] > tiers[2], "common treasures are commoner than rare ones: " + string.Join("/", tiers));

        // The state message carries the crew's treasure tally and the treasures lying in the stage.
        var p = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 0, State = ShipRepairProgress.Phase.Playing, Seconds = 9f, Stages = 1,
            Order = new int[0], Parts = new ShipRepairPacket.Part[0], TreasureCount = 3, TreasurePoints = 40,
            Treasures = new[] { new ShipRepairPacket.Treasure { Index = 5, Pellet = 40 }, new ShipRepairPacket.Treasure { Index = 187, Pellet = 41 } } };
        Check(ShipRepairPacket.Read(new ByteReader(p.Write()), out var d) && d.TreasureCount == 3 && d.TreasurePoints == 40 && d.Treasures.Length == 2
            && d.Treasures[1].Index == 187 && d.Treasures[1].Pellet == 41, "treasure section round trip");
        p.Treasures = new[] { new ShipRepairPacket.Treasure { Index = 5, Pellet = 40 }, new ShipRepairPacket.Treasure { Index = 5, Pellet = 41 } };
        Check(!ShipRepairPacket.Read(new ByteReader(p.Write()), out _), "the same treasure cannot lie twice");
        p.Treasures = new[] { new ShipRepairPacket.Treasure { Index = ShipRepairTreasures.TableSize, Pellet = 40 } };
        Check(!ShipRepairPacket.Read(new ByteReader(p.Write()), out _), "treasure index is bounded by the table");
        p.Treasures = new ShipRepairPacket.Treasure[0]; p.TreasurePoints = 3 * 99 + 1;
        Check(!ShipRepairPacket.Read(new ByteReader(p.Write()), out _), "points are bounded by the count (under a part each)");
        p.TreasurePoints = 0; p.Competitive = true; p.Racers = new[] { new ShipRepairRacer { User = 1, Name = "A" } };
        Check(!ShipRepairPacket.Read(new ByteReader(p.Write()), out _), "a competitive race has no treasures");
        p.TreasureCount = 0; Check(ShipRepairPacket.Read(new ByteReader(p.Write()), out _), "and reads fine without them");
        var many = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 0, State = ShipRepairProgress.Phase.Playing, Seconds = 9f, Stages = 1,
            Order = new int[0], Parts = Enumerable.Range(0, 400).Select(i => new ShipRepairPacket.Part { Id = i % 30, Slot = i, Pellet = (uint)(100 + i), Weight = 20, Spawned = true }).ToArray(),
            Treasures = Enumerable.Range(0, ShipRepairPacket.MaxTreasures).Select(i => new ShipRepairPacket.Treasure { Index = i, Pellet = (uint)(900 + i) }).ToArray() };
        Check(many.WriteFitting().Length <= ShipRepairPacket.MaxBytes, "treasures and a long part list still fit the message limit");
        // The race: a racer's treasures are their own category, below a part each, and count in whole parts' worth.
        var racerA = new ShipRepairRacer { User = 1, Name = "A" }; var racerB = new ShipRepairRacer { User = 2, Name = "B" };
        racerA.Receive(4); racerB.Receive(4); racerB.Receive(5);
        for (int i = 0; i < 9; i++) racerA.ReceiveTreasure(ShipRepairTreasures.Points(1000, 0.2f));
        Check(racerA.Treasures == 9 && racerA.TreasureHundredths == 9 * 80 && racerA.Score == 1 + 7, "nine rich treasures add seven whole parts' worth (80 hundredths each)");
        racerB.ReceiveTreasure(500);
        Check(racerB.TreasureHundredths == 99 && racerB.Score == 2, "no single treasure is worth a whole part");
        Check(ShipRepairRace.Best(new[] { racerA, racerB }, 2) == 1, "treasures can decide the race's winner when parts are close");
        var raced = new ShipRepairPacket { RunId = Guid.NewGuid().ToString(), Epoch = 2, Mask = 0, State = ShipRepairProgress.Phase.Playing, Seconds = 9f, Stages = 1,
            Competitive = true, Order = new int[0], Parts = new ShipRepairPacket.Part[0], Racers = new[] { racerA, racerB },
            Treasures = new[] { new ShipRepairPacket.Treasure { Index = 1, Pellet = 7 } } };
        Check(ShipRepairPacket.Read(new ByteReader(raced.Write()), out var rd) && rd.Racers.Length == 2 && rd.Racers[0].Treasures == 9
            && rd.Racers[0].TreasureHundredths == 720 && rd.Racers[0].Score == racerA.Score && rd.Treasures.Length == 1, "racers' treasure tallies round trip with the stage's treasures");
        racerA.TreasureHundredths = 9 * 99 + 1;
        Check(!ShipRepairPacket.Read(new ByteReader(raced.Write()), out _), "a racer's points are bounded by their count");
        racerA.TreasureHundredths = 720;
        Console.WriteLine("PASS: treasure tiers, worth under a part, seeded stage picks and the state message's treasure section.");
    }

    sealed class Def { public string Name; public float Pos; }

    // The select-bar order: RoR2 sorts the survivors once by desiredSortPosition, then SurvivorOrder re-sorts the slice
    // holding Olimar and Mario when the lobby mode changes.
    static void SurvivorOrderCheck()
    {
        var olimar = new Def { Name = "Olimar", Pos = SurvivorOrder.OlimarPosition(false) };
        var mario = new Def { Name = "Mario", Pos = SurvivorOrder.MarioPosition(false) };
        var all = new[] { new Def { Name = "Commando", Pos = 0 }, new Def { Name = "Huntress", Pos = 2 }, olimar, mario,
            new Def { Name = "Samus", Pos = 102 }, new Def { Name = "Fox", Pos = 103 }, new Def { Name = "DK", Pos = 109 } };
        var ordered = all.OrderBy(d => d.Pos).ToArray(); // the catalog's one-time sort
        var slots = new int[all.Length];
        Func<string> names = () => string.Join(",", ordered.Select(d => d.Name));
        const string vanilla = "Commando,Huntress,";
        const string melee = ",Samus,Fox,DK";
        Check(names() == vanilla + "Mario,Olimar" + melee, "outside Ship Repair Mario leads Olimar: " + names());
        Check(SurvivorOrder.Lead < 102 && SurvivorOrder.Second < 102, "Melee positions start at 102");

        Action<bool> apply = repair =>
        {
            olimar.Pos = SurvivorOrder.OlimarPosition(repair); mario.Pos = SurvivorOrder.MarioPosition(repair);
            SurvivorOrder.Reorder(ordered, olimar, mario, d => d.Pos, (d, slot) => slots[Array.IndexOf(all, d)] = slot);
        };
        apply(true);
        Check(names() == vanilla + "Olimar,Mario" + melee, "Ship Repair puts Olimar first: " + names());
        Check(slots[Array.IndexOf(all, olimar)] == 2 && slots[Array.IndexOf(all, mario)] == 3, "slot callback for the swapped pair");
        Check(!SurvivorOrder.Reorder(ordered, olimar, mario, d => d.Pos, null), "re-applying the same mode moves nothing");
        apply(false);
        Check(names() == vanilla + "Mario,Olimar" + melee, "leaving Ship Repair restores Mario first: " + names());
        apply(true); apply(false); apply(true); apply(false);
        Check(names() == vanilla + "Mario,Olimar" + melee, "repeated mode flips are stable");

        // A third-party survivor between them keeps its place relative to both.
        var other = new Def { Name = "Other", Pos = 100.5f };
        ordered = all.Concat(new[] { other }).OrderBy(d => d.Pos).ToArray();
        Check(names() == vanilla + "Mario,Other,Olimar" + melee, "third-party setup: " + names());
        slots = new int[all.Length + 1]; all = all.Concat(new[] { other }).ToArray();
        apply(true);
        Check(names() == vanilla + "Olimar,Other,Mario" + melee, "third-party between the pair: " + names());

        // Without Mario's plugin there is nothing to reorder.
        Check(!SurvivorOrder.Reorder(ordered, olimar, null, d => d.Pos, null), "no Mario, no reorder");
        Console.WriteLine("PASS: character-select order (Mario, Olimar outside Ship Repair; Olimar, Mario inside; Melee untouched).");
    }
}
