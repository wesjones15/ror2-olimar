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
        uint treasureMaxCarriers = 25;
        const float TreasureSpacing = 14f;

        internal int TreasureCount => treasureCount;
        internal int TreasurePoints => treasurePoints;
        /// Treasure points rounded down to whole parts' worth, what the final score takes from the category.
        int TreasureScore => ShipRepairTreasures.Whole(treasurePoints);

        /// Extracts Pikmin 2's treasures from the user's Pikmin 2 disc once, beside Louie's files (a few hundred small
        /// archives, so on the calling thread). Without the disc or the files Ship Repair simply has no treasures.
        internal static void PrepareTreasures(string configuredDisc, string cacheDir, string here, string assetsParent)
        {
            try
            {
                if (Native.pik_p2_treasures_complete(Native.CString(cacheDir)) != 0) return;
                string disc = configuredDisc?.Trim();
                if (string.IsNullOrEmpty(disc) || !System.IO.File.Exists(disc))
                {
                    var found = new byte[1024];
                    disc = Native.pik_p2_find_disc(Native.CString(here + ";" + assetsParent), found, (uint)found.Length) != 0 ? Native.Utf8(found) : null;
                }
                if (string.IsNullOrEmpty(disc)) { Plugin.Log.LogInfo("Treasures: no Pikmin 2 disc found (set Skins.Pikmin2Disc), so Ship Repair has none"); return; }
                Plugin.Log.LogInfo($"Treasures: extracting Pikmin 2's treasures from {disc} into {cacheDir}...");
                if (Native.pik_p2_extract_treasures(Native.CString(disc), Native.CString(cacheDir)) != 0)
                    Plugin.Log.LogWarning("Treasures: extraction from the Pikmin 2 disc failed, so Ship Repair has none");
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
            treasureMaxCarriers = (uint)Mathf.Clamp(Plugin.TreasureMaxCarriers.Value, 1, 100);
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
            if (Competitive || treasuresPerStage <= 0 || nodes.Count == 0) yield break;
            var table = TreasureTable(world);
            if (table.Count == 0) { Plugin.Log.LogInfo("Ship Repair: no Pikmin 2 treasures (the Pikmin 2 files are not extracted)."); yield break; }
            var picks = ShipRepairTreasures.Pick(run.seed, epoch, treasuresPerStage, table, collectedTreasures, treasureMaxCarriers);
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
            if (t == null || !Progress.Accepting || Competitive) return;
            bool ship = false;
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].id == receipt.b && world.Onions[i].is_ship != 0) ship = true;
            if (!ship) return;
            t.delivered = true;
            collectedTreasures.Add(index);
            int points = ShipRepairTreasures.Points(t.pokos, treasureWeight);
            treasureCount++; treasurePoints += points;
            ShowToast($"{t.name} recovered — +{ShipRepairTreasures.Format(points)} treasure points");
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
        string TreasureSummary() => treasureCount == 0 && treasuresPerStage == 0 ? "" :
            " · " + treasureCount + " treasures (" + ShipRepairTreasures.Format(treasurePoints) + " pts)";

        /// The results screen's detail line gets the treasure category in the shared repair.
        string TreasureDetail() => Competitive || (treasureCount == 0 && treasuresPerStage == 0) ? "" :
            "  |  " + treasureCount + " TREASURES +" + ShipRepairTreasures.Format(treasurePoints);

        /// Names a treasure on the ground until Pikmin have taken hold of it.
        void TreasureLabels()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var camera = user?.cameraRigController ? user.cameraRigController.sceneCam : Camera.main;
            if (!camera) return;
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
                if (Vector3.Distance(camera.transform.position, t.pos) > 45f) continue;
                var screen = camera.WorldToScreenPoint(t.pos + Vector3.up * 2f);
                if (screen.z <= 0) continue;
                float x = Mathf.Clamp(screen.x, 130f, Screen.width - 130f), y = Mathf.Clamp(Screen.height - screen.y, 40, Screen.height - 70);
                ShipRepairUi.PikminText(t.name, x, y, 24f, 260f, new Color(.62f, .9f, 1f));
                ShipRepairUi.PikminText(t.carriers + " carriers - treasure", x, y + 26f, 17f, 260f, new Color(1f, .94f, .72f));
            }
        }
    }
}
