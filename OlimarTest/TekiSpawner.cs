using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // Ship Repair places some Pikmin enemies itself instead of leaving them to the combat director: a Breadbug on
    // the first stage that always drops a ship part, Honeywisps that fly their paths for nectar, and the
    // waterborne enemies, which only appear in the stage's water.
    internal sealed class TekiSpawner : MonoBehaviour
    {
        public static void Install(GameObject host) => host.AddComponent<TekiSpawner>();

        const float MinRange = 22f, MaxRange = 55f;
        Stage stage;
        float stageSince, nextTry, nextWisp, nextWater, nextPlant;
        bool breadbugDone, clamDone;
        bool flintRolled;
        int geysersPlaced, gasPipesPlaced;
        float nextPosyRefill;
        readonly List<TekiBody> wisps = new List<TekiBody>();
        readonly List<TekiBody> waterborne = new List<TekiBody>();
        readonly List<TekiBody> posies = new List<TekiBody>();

        void FixedUpdate()
        {
            if (!NetworkServer.active || !Run.instance || !Stage.instance || !TekiBodies.Enabled.Value) return;
            if (Stage.instance != stage)
            {
                stage = Stage.instance;
                stageSince = Time.time;
                breadbugDone = clamDone = false;
                flintRolled = false;
                geysersPlaced = gasPipesPlaced = 0;
                nextTry = nextWisp = nextWater = nextPlant = Time.time + 6f;
                nextPosyRefill = Time.time + 6f;
                wisps.Clear();
                waterborne.Clear();
                posies.Clear();
            }
            if (Time.time < nextTry || !TekiCatalog.Ready) return;
            nextTry = Time.time + 2f;
            var players = Players();
            if (players.Count == 0) return;
            bool shipMode = ShipRepairMode.Active;
            var olimars = players.FindAll(p => p.GetComponent<OlimarDriver>());
            var around = shipMode ? players : olimars;
            if (around.Count == 0) return;
            if (!flintRolled && TekiCatalog.Has(TekiCatalog.FlintBeetle))
            {
                flintRolled = true;
                int made = 0;
                foreach (var player in around)
                {
                    if (made >= 2) break;
                    if (!FlintSpawnPolicy.Encounter((float)Run.instance.spawnRng.nextNormalizedFloat, shipMode, olimars.Count,
                        TekiBodies.FlintShipChance.Value, TekiBodies.FlintOtherChance.Value)) continue;
                    var at = PointNear(new List<CharacterBody> { player }, false, out _);
                    if (at.HasValue && TekiBodies.Spawn(TekiCatalog.FlintBeetle, at.Value, Random.Range(0f, 360f))) made++;
                }
            }
            // Posies are safe resources in both modes; other placed Pikmin enemies
            // remain Ship Repair fixtures. Fill the initial quota together, then
            // replenish harvested plants at the mode's slower cadence.
            if (Time.time >= nextPosyRefill && TekiCatalog.Has(TekiCatalog.PelletPosy))
            {
                nextPosyRefill = Time.time + PosySpawnPolicy.RefillSeconds(shipMode);
                posies.RemoveAll(p => !p || !p.Body || !p.Body.healthComponent || !p.Body.healthComponent.alive);
                int target = PosySpawnPolicy.Target(shipMode, players.Count, olimars.Count,
                    TekiBodies.PosiesPerPlayer.Value, TekiBodies.ClassicPosiesPerOlimar.Value);
                for (int n = posies.Count; n < target; n++)
                {
                    var at = PointNear(around, false, out _);
                    if (!at.HasValue) break;
                    bool crowded = posies.Exists(p => p && p.Body && (p.Body.footPosition - at.Value).sqrMagnitude < 4f * 4f);
                    if (crowded) continue;
                    var posy = TekiBodies.Spawn(TekiCatalog.PelletPosy, at.Value, Random.Range(0f, 360f));
                    if (!posy) break;
                    posy.count0 = PosySpawnPolicy.InitialSize(n);
                    posies.Add(posy);
                }
            }
            if (!shipMode) return;

            // The first stage's Breadbug, which carries a ship part for whoever defeats it.
            if (!breadbugDone && Run.instance.stageClearCount == 0 && TekiCatalog.Has(TekiCatalog.Breadbug))
            {
                var at = PointNear(players, false, out _);
                if (at.HasValue)
                {
                    var bug = TekiBodies.Spawn(TekiCatalog.Breadbug, at.Value, Random.Range(0f, 360f));
                    if (bug)
                    {
                        bug.guaranteedPart = true;
                        breadbugDone = true;
                        Chat.AddMessage("<style=cWorldEvent>A Breadbug scuttles nearby. Defeat it and it drops a ship part.</style>");
                    }
                }
            }

            if (Time.time >= nextWisp && TekiCatalog.Has(TekiCatalog.Honeywisp) && TekiBodies.HoneywispsPerPlayer.Value > 0)
            {
                nextWisp = Time.time + 18f;
                wisps.RemoveAll(w => !w);
                if (wisps.Count < Mathf.Min(6, players.Count * TekiBodies.HoneywispsPerPlayer.Value))
                {
                    var at = PointNear(players, false, out _);
                    if (at.HasValue)
                    {
                        var w = TekiBodies.Spawn(TekiCatalog.Honeywisp, at.Value + Vector3.up * 3f, Random.Range(0f, 360f));
                        if (w) wisps.Add(w);
                    }
                }
            }

            // Hazard fixtures and a shell remain Ship Repair only; the first stage
            // has none of the dangerous ones.
            if (Time.time >= nextPlant)
            {
                nextPlant = Time.time + 20f;
                if (Run.instance.stageClearCount > 0)
                {
                    if (geysersPlaced < TekiBodies.FireGeysersPerStage.Value && TekiCatalog.Has(TekiCatalog.FireGeyser))
                    {
                        var at = PointNear(players, false, out _);
                        if (at.HasValue && TekiBodies.Spawn(TekiCatalog.FireGeyser, at.Value, Random.Range(0f, 360f))) geysersPlaced++;
                    }
                    if (gasPipesPlaced < TekiBodies.GasPipesPerStage.Value && TekiCatalog.Has(TekiCatalog.GasPipe))
                    {
                        var at = PointNear(players, false, out _);
                        if (at.HasValue && TekiBodies.Spawn(TekiCatalog.GasPipe, at.Value, Random.Range(0f, 360f))) gasPipesPlaced++;
                    }
                    if (!clamDone && TekiBodies.ClamclampEveryStage.Value && TekiCatalog.Has(TekiCatalog.Clamclamp))
                    {
                        var at = PointNear(players, false, out _);
                        if (at.HasValue)
                        {
                            var clam = TekiBodies.Spawn(TekiCatalog.Clamclamp, at.Value, Random.Range(0f, 360f));
                            if (clam)
                            {
                                // Its pearl is a ship part for whoever breaks it.
                                clam.count0 = 1;
                                clamDone = true;
                                Chat.AddMessage("<style=cWorldEvent>A Pearly Clamclamp sits nearby. Break its pearl while it is open for a ship part.</style>");
                            }
                        }
                    }
                }
            }

            if (Time.time >= nextWater && TekiBodies.WaterEnemiesPerPlayer.Value > 0)
            {
                nextWater = Time.time + 12f;
                waterborne.RemoveAll(w => !w);
                if (waterborne.Count < Mathf.Min(6, players.Count * TekiBodies.WaterEnemiesPerPlayer.Value))
                {
                    var at = PointNear(players, true, out bool found);
                    if (at.HasValue && found)
                    {
                        // Any of the waterborne kinds (Water Dumple, Wogpole).
                        var swimmers = new List<TekiSpec>();
                        foreach (var spec in TekiRoster.All)
                            if (spec.Water && TekiCatalog.Has(spec.Kind)) swimmers.Add(spec);
                        if (swimmers.Count > 0)
                        {
                            var pick = swimmers[Random.Range(0, swimmers.Count)];
                            var w = TekiBodies.Spawn(pick.Kind, at.Value, Random.Range(0f, 360f));
                            if (w) waterborne.Add(w);
                        }
                    }
                }
            }
        }

        static List<CharacterBody> Players()
        {
            var list = new List<CharacterBody>();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var b = pc && pc.master ? pc.master.GetBody() : null;
                if (b && b.healthComponent && b.healthComponent.alive) list.Add(b);
            }
            return list;
        }

        // A spot on the ground (or the water's surface, with `water`) a good way from a random player.
        static Vector3? PointNear(List<CharacterBody> players, bool water, out bool inWater)
        {
            inWater = false;
            int mask = UnityHost.Mask;
            for (int attempt = 0; attempt < (water ? 40 : 12); attempt++)
            {
                var around = players[Random.Range(0, players.Count)].footPosition;
                float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(MinRange, water ? MaxRange * 2f : MaxRange);
                var top = around + new Vector3(Mathf.Sin(a) * r, 40f, Mathf.Cos(a) * r);
                if (water)
                {
                    if (Physics.Raycast(top, Vector3.down, out var surface, 120f, LayerIndex.water.mask, QueryTriggerInteraction.Collide))
                    {
                        inWater = true;
                        return surface.point;
                    }
                    continue;
                }
                if (!Physics.Raycast(top, Vector3.down, out var hit, 90f, mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
                if (Physics.CheckSphere(hit.point + Vector3.up * 1.2f, 0.8f, mask, QueryTriggerInteraction.Ignore)) continue;
                return hit.point;
            }
            return null;
        }

        /// A Honeywisp (or anything ephemeral) is finished: its body goes.
        public static void Remove(TekiBody b)
        {
            if (!b || !b.Body) return;
            var master = b.Body.master;
            if (master) NetworkServer.Destroy(master.gameObject);
            if (b) NetworkServer.Destroy(b.gameObject);
        }
    }
}
