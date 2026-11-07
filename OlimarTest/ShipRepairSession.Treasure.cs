using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ror2Common;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace OlimarTest
{
    // Pikmin 2's treasures as bonus objectives of the shared (cooperative) repair: a few lie around each stage, the
    // Pikmin carry them to the Dolphin like a part, and each delivery scores in its own category, worth less than
    // a part (ShipRepairTreasures). Nothing here is part of the 30 parts: no fitting, no win condition. The race
    // (competitive) mode has no treasures. Their models are not drawn yet (the view shows a plain pellet of the
    // treasure's size), see libsms/docs/p2-treasures.md.
    public sealed partial class ShipRepairSession
    {
        sealed class Treasure
        {
            public int index;
            public uint pellet;
            public string name;
            public uint carriers, pokos;
            public Vector3 pos, safe, last, origin;
            public bool spawned, delivered, discovered;
        }
        readonly List<Treasure> treasures = new List<Treasure>(16);
        // Collected in this run (a treasure comes once) and what they are worth: the crew's tally, shown and scored.
        readonly HashSet<int> collectedTreasures = new HashSet<int>();
        int treasureCount, treasurePoints;
        int treasuresPerStage;
        float treasureWeight = ShipRepairTreasures.DefaultWeight;
        uint treasureMaxCarriers;
        const float TreasureSpacing = 14f;

        /// Whether the results show the treasure column: treasures were on this run (the host's setting, or any were found).
        bool TreasuresOn => treasuresPerStage > 0 || treasureCount > 0 || treasures.Count > 0 || race.Players.Values.Any(p => p.Treasures > 0);
        internal int TreasureCount => treasureCount;
        internal int TreasurePoints => treasurePoints;
        /// Treasure points rounded down to whole parts' worth, what the final score takes from the category.
        int TreasureScore => ShipRepairTreasures.Whole(treasurePoints);

        /// Extracts Pikmin 2's treasures from the user's Pikmin 2 disc once, beside Louie's files (a few hundred small
        /// archives, so on the calling thread). Without the disc or the files Ship Repair simply has no treasures.
        /// Version of `pik_p2_extract_treasures`'s output (SharedFiles extracted.txt, set "p2treasures"): bump it when
        /// the extractor writes an existing file differently.
        const int TreasureExtractorVersion = 1;

        internal static void PrepareTreasures(string cacheDir, string searchDirs)
        {
            try
            {
                bool complete = Native.pik_p2_treasures_complete(Native.CString(cacheDir)) != 0;
                if (complete && SharedFiles.Current(SharedFiles.Pikmin, "p2treasures", TreasureExtractorVersion)) return;
                string disc = OlimarSkin.FindPikmin2Disc(searchDirs);
                if (string.IsNullOrEmpty(disc))
                {
                    // Files from an older extractor are still used as they are.
                    if (!complete) Plugin.Log.LogInfo("Treasures: no Pikmin 2 disc found, so Ship Repair has none. " + SharedFiles.MissingDisc("Pikmin 2"));
                    return;
                }
                Plugin.Log.LogInfo($"Treasures: extracting Pikmin 2's treasures from {disc} into {cacheDir}...");
                if (Native.pik_p2_extract_treasures(Native.CString(disc), Native.CString(cacheDir)) != 0)
                    Plugin.Log.LogWarning("Treasures: extraction from the Pikmin 2 disc failed");
                else if (Native.pik_p2_treasures_complete(Native.CString(cacheDir)) != 0)
                    SharedFiles.MarkCurrent(SharedFiles.Pikmin, "p2treasures", TreasureExtractorVersion);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Treasures: {e.Message}");
            }
        }

        /// Adds the treasures' models to a new view's table (before its meshes are built from it), when the Pikmin 2 files are
        /// extracted; treasure pellets are drawn with them, sparkle and make Pikmin 2's sounds. Otherwise they are plain pellets.
        internal static void LoadTreasuresInto(IntPtr view)
        {
            try
            {
                if (string.IsNullOrEmpty(Plugin.AssetsDir)) return;
                var cache = Native.CString(System.IO.Path.Combine(Plugin.AssetsDir, "p2"));
                if (Native.pik_p2_treasures_complete(cache) == 0) return;
                int first = Native.pik_view_load_treasures(view, cache);
                if (first < 0) Plugin.Log.LogWarning("Treasures: their models did not load; they are drawn as plain pellets");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Treasures: {e.Message}");
            }
        }

        void TreasureAwake()
        {
            treasuresPerStage = Mathf.Clamp(Plugin.TreasuresPerStage.Value, 0, ShipRepairPacket.MaxTreasures);
            treasureWeight = ShipRepairTreasures.ClampWeight(Plugin.TreasureScoreWeight.Value);
            treasureMaxCarriers = (uint)Mathf.Clamp(Plugin.TreasureMaxCarriers.Value, 0, 200);
        }

        static string TreasureName(int index)
        {
            var world = PikminWorld.Instance;
            if (!world || world.SimHandle == IntPtr.Zero) return "Treasure";
            var bytes = new byte[128];
            Native.pik_sim_treasure_name(world.SimHandle, (uint)index, bytes, (uint)bytes.Length);
            var name = Native.Utf8(bytes);
            return string.IsNullOrEmpty(name) ? "Treasure" : name;
        }

        /// The disc's table as the host reads it (weights and worth), or empty when Pikmin 2 is not extracted.
        static List<ShipRepairTreasures.Info> TreasureTable(PikminWorld world)
        {
            var table = new List<ShipRepairTreasures.Info>();
            uint count = Native.pik_sim_treasure_count(world.SimHandle);
            for (uint i = 0; i < count && i < ShipRepairTreasures.TableSize; i++)
            {
                uint carriers = Native.pik_sim_treasure_weight(world.SimHandle, i);
                if (carriers == 0) continue;
                table.Add(new ShipRepairTreasures.Info { Index = (int)i, Carriers = carriers, Pokos = Native.pik_sim_treasure_value(world.SimHandle, i) });
            }
            return table;
        }

        /// Lays this stage's treasures on ground the Dolphin's Pikmin can carry them from: clear of the parts and of each
        /// other, off the arrival point, and a carry distance from the receiver.
        IEnumerator PlaceTreasures(PikminWorld world, uint epoch, System.Random rng, NodeGraph graph, List<NodeGraph.NodeIndex> nodes, List<Vector3> goals, CharacterBody body)
        {
            if (treasuresPerStage <= 0 || nodes.Count == 0) yield break;
            var table = TreasureTable(world);
            if (table.Count == 0) { Plugin.Log.LogInfo("Ship Repair: no Pikmin 2 treasures (the Pikmin 2 files are not extracted)."); yield break; }
            // The race shares one pool of treasures; each extra racer adds two to it.
            int wanted = Mathf.Min(ShipRepairPacket.MaxTreasures, treasuresPerStage + (Competitive ? 2 * Mathf.Max(0, race.Players.Count - 1) : 0));
            var picks = ShipRepairTreasures.Pick(run.seed, epoch, wanted, table, collectedTreasures, treasureMaxCarriers);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (int index in picks)
            {
                Vector3 point = default;
                bool found = false;
                for (int attempt = 0; attempt < 300 && !found; attempt++)
                {
                    if (clock.ElapsedMilliseconds >= 4) { yield return null; clock.Restart(); }
                    if (!world || !world.StageReady || Progress.Epoch != epoch || Progress.State == ShipRepairProgress.Phase.Loading || Terminal) yield break;
                    if (!graph.GetNodePosition(nodes[rng.Next(nodes.Count)], out point)) continue;
                    float spacing = attempt < 150 ? PartSpacing : TreasureSpacing;
                    if (Vector3.Distance(point, body.footPosition) < 20f) continue;
                    if (parts.Any(p => HorizontalDistanceSquared(point, p.origin) < spacing * spacing) ||
                        treasures.Any(t => HorizontalDistanceSquared(point, t.origin) < spacing * spacing)) continue;
                    if (attempt < 150 && goals.Count > 0)
                    {
                        float nearest = goals.Min(g => Mathf.Sqrt(HorizontalDistanceSquared(point, g)));
                        if (nearest < 15f || nearest > 170f) continue;
                    }
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!ReachableDolphin(world, point)) continue;
                    found = true;
                }
                if (!found) { Plugin.Log.LogWarning($"Ship Repair: no safe reachable site for treasure {index}."); continue; }
                var info = table.First(t => t.Index == index);
                var treasure = new Treasure { index = index, name = TreasureName(index), carriers = info.Carriers, pokos = info.Pokos,
                    pos = point, last = point, safe = point, origin = point };
                var pos = UnityHost.ToSim(point);
                int pellet = Native.pik_sim_spawn_treasure(world.SimHandle, (uint)index, ref pos, (float)rng.NextDouble() * Mathf.PI * 2f);
                if (pellet < 0) { Plugin.Log.LogError("Ship Repair: native treasure spawn refused " + index); continue; }
                treasure.pellet = (uint)pellet; treasure.spawned = true;
                treasures.Add(treasure);
                Broadcast();
                yield return null;
            }
            Plugin.Log.LogInfo($"Ship Repair: stage {Progress.Epoch}, {treasures.Count} treasures; {treasureCount} recovered so far ({ShipRepairTreasures.Format(treasurePoints)} points).");
        }

        void TreasureStage() => treasures.Clear();

        /// Follows each treasure's pellet: its position for the label, and a fall under the map puts it back on safe ground.
        void TrackTreasures(PikminWorld world)
        {
            foreach (var t in treasures)
            {
                if (t.delivered || !t.spawned) continue;
                for (int i = 0; i < world.PelletCount; i++)
                {
                    var pellet = world.Pellets[i];
                    if (pellet.id != t.pellet || pellet.state == 3) continue;
                    t.pos = UnityHost.ToUnity(pellet.pos);
                    if (pellet.state != 2 && Physics.Raycast(t.pos + Vector3.up * 2f, Vector3.down, out var floor, 5f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        && floor.normal.y > 0.65f) t.safe = floor.point;
                    if (t.pos.y < t.safe.y - 30f)
                    {
                        var back = UnityHost.ToSim(t.safe);
                        if (Native.pik_sim_recover_pellet(stageSim, t.pellet, ref back) != 0) t.pos = t.last = t.safe;
                    }
                    if (!t.discovered && CharacterBody.readOnlyInstancesList.Any(b => b.isPlayerControlled && Vector3.Distance(b.footPosition, t.pos) < 25f))
                    {
                        t.discovered = true;
                        ShowToast($"Treasure: {t.name} — needs {t.carriers} Pikmin");
                        Broadcast();
                    }
                }
            }
        }

        /// A ship took a treasure (<c>ShipPartCollected</c> with a treasure's part id): it scores in the treasure category.
        void TreasureReceipt(PikminWorld world, Native.Event receipt)
        {
            int index = receipt.c - ShipRepairTreasures.PartBase;
            var t = treasures.FirstOrDefault(x => x.spawned && x.pellet == receipt.a && x.index == index && !x.delivered);
            if (t == null || !Progress.Accepting) return;
            bool ship = false;
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].id == receipt.b && world.Onions[i].is_ship != 0) ship = true;
            if (!ship) return;
            int points = ShipRepairTreasures.Points(t.pokos, treasureWeight);
            string who = "";
            if (Competitive)
            {
                // The racer whose Dolphin took it scores; once someone has all thirty parts the race is decided.
                uint user = world.UserForShip((uint)receipt.b);
                if (race.Completer != 0 || user == 0 || !race.Players.TryGetValue(user, out var racer)) return;
                racer.ReceiveTreasure(points);
                who = racer.Name + ": ";
            }
            else { treasureCount++; treasurePoints += points; }
            t.delivered = true;
            collectedTreasures.Add(index);
            ShowToast($"{who}{t.name} recovered — +{ShipRepairTreasures.Format(points)} treasure points");
            Plugin.Log.LogInfo($"Ship Repair: treasure {index} ({t.name}, {t.pokos} Pokos) delivered for {points} hundredths; {treasureCount} treasures, {treasurePoints} total.");
            Broadcast();
        }

        ShipRepairPacket.Treasure[] TreasuresPacket() => treasures.Where(t => t.spawned && !t.delivered).Take(ShipRepairPacket.MaxTreasures)
            .Select(t => new ShipRepairPacket.Treasure { Index = t.index, Pellet = t.pellet }).ToArray();

        /// A client's copy of the host's treasure state: the tally, and the treasures lying in the stage.
        void ReceiveTreasures(ShipRepairPacket packet)
        {
            treasureCount = packet.TreasureCount; treasurePoints = packet.TreasurePoints;
            treasures.Clear();
            treasures.AddRange(packet.Treasures.Select(t => new Treasure { index = t.Index, pellet = t.Pellet, name = TreasureName(t.Index), spawned = true }));
        }

        /// The crew's treasure line for the results and the saved report.
        string TreasureSummary() => Competitive || (treasureCount == 0 && treasuresPerStage == 0) ? "" :
            " · " + treasureCount + " treasures (" + ShipRepairTreasures.Format(treasurePoints) + " pts)";

        /// The results screen's detail line gets the treasure category in the shared repair.
        string TreasureDetail() => treasuresPerStage == 0 && treasureCount == 0 && !race.Players.Values.Any(p => p.Treasures > 0) ? "" :
            "  |  " + (Competitive ? race.Players.Values.Sum(p => p.Treasures) : treasureCount) + " TREASURES";

        /// The local player's treasures (their own Dolphin's in the race, the crew's otherwise) for the HUD tally; -1 when
        /// the host turned treasures off.
        int LocalTreasureCount() => treasuresPerStage <= 0 && treasureCount == 0 && treasures.Count == 0 ? -1
            : Competitive ? (race.Players.TryGetValue(LocalUserId, out var me) ? me.Treasures : 0) : treasureCount;
        int LocalTreasurePoints() => Competitive ? (race.Players.TryGetValue(LocalUserId, out var me) ? me.TreasureHundredths : 0) : treasurePoints;

        /// Names a treasure on the ground until Pikmin have taken hold of it.
        void TreasureLabels()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var camera = user?.cameraRigController ? user.cameraRigController.sceneCam : Camera.main;
            if (!camera && !VrSheet.Active) return;
            var world = PikminWorld.Instance;
            foreach (var t in treasures)
            {
                if (t.delivered || !t.spawned || !world) continue;
                bool known = false, carried = false;
                for (int i = 0; i < world.PelletCount; i++)
                    if (world.Pellets[i].id == t.pellet)
                    {
                        known = world.Pellets[i].state <= 1;
                        carried = known && world.Pellets[i].carriers > 0;
                        t.pos = UnityHost.ToUnity(world.Pellets[i].pos);
                        if (t.carriers == 0) t.carriers = world.Pellets[i].min_carriers;
                    }
                if (!known || carried) continue;
                var eye = VrSheet.Active ? Vr.Head().position : camera.transform.position;
                if (Vector3.Distance(eye, t.pos) > 45f) continue;
                // In VR over the treasure on the sheet before the eye (VrSheet), not on the wrist.
                var screen = VrSheet.WorldToScreenPoint(camera, t.pos + Vector3.up * 2f);
                if (screen.z <= 0) continue;
                float x = Mathf.Clamp(screen.x, 130f, Screen.width - 130f), y = Mathf.Clamp(Screen.height - screen.y, 40, Screen.height - 70);
                ShipRepairUi.PikminText(t.name, x, y, 24f, 260f, new Color(.62f, .9f, 1f));
                ShipRepairUi.PikminText(t.carriers + " carriers - treasure", x, y + 26f, 17f, 260f, new Color(1f, .94f, .72f));
            }
        }
    }
}
