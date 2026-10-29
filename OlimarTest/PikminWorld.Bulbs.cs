using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace OlimarTest
{
    // Pikmin 2's Candypop Buds and the weight of Pikmin on a flying enemy. The rules are the core's (libpikmin's bulb.rs and
    // weigh.rs): a Pikmin thrown into a bud turns into seeds of the bud's colour; Pikmin hanging on a flier weigh it down.
    // This file decides where the buds stand and which RoR2 enemies count as small, large or a boss.
    internal partial class PikminWorld
    {
        /// Colours of the buds, by Pikmin colour index: Blue, Red, Yellow, Purple, White. Pikmin 2's `mBlackWhiteAppearanceRate`
        /// (Pom parms, fp03) is 0.15 for the two together; the rainbow Candypop Pop is one pick in ten here.
        static readonly float[] BulbWeights = { 0.85f / 3f, 0.85f / 3f, 0.85f / 3f, 0.075f, 0.075f };
        const float PopShare = 0.10f;

        /// Adds the five buds' models to a new view's table (before its meshes are built), when the Pikmin 2 enemy files
        /// are extracted. Without them no buds are placed.
        internal static void LoadBulbsInto(IntPtr view)
        {
            try
            {
                if (string.IsNullOrEmpty(Plugin.AssetsDir) || !P2Enemies.Ready) return;
                if (Native.pik_view_load_bulbs(view, Native.CString(System.IO.Path.Combine(Plugin.AssetsDir, "p2"))) < 0)
                    Plugin.Log.LogWarning("Candypop Buds: their models did not load; no buds are placed");
                else bulbModels = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Candypop Buds: {e.Message}");
            }
        }

        static bool bulbModels;

        /// Colour for the n-th bud of a stage: the first five are each colour once (in a shuffled order) where the stage has
        /// enough buds for that, then by Pikmin 2's odds.
        static (uint colour, bool queen) PickBulb(System.Random rng, int n, int total, uint[] firstFive)
        {
            if (total >= 6 && n < 5) return (firstFive[n], false);
            if (rng.NextDouble() < PopShare) return ((uint)rng.Next(3), true);
            double r = rng.NextDouble(), sum = 0;
            for (uint c = 0; c < BulbWeights.Length; c++)
            {
                sum += BulbWeights[c];
                if (r < sum) return (c, false);
            }
            return (0, false);
        }

        /// Scatters this stage's buds over open ground, away from the player and from each other. Ship Repair has many, the
        /// other modes a few (Candypop/BulbsShipRepair, BulbsOther).
        IEnumerator ScatterBulbs(IntPtr forSim, uint forEpoch)
        {
            int wanted = ShipRepairMode.Active ? Plugin.BulbsShipRepair.Value : Plugin.BulbsOther.Value;
            if (wanted <= 0 || !bulbModels || ClientMode) yield break;
            CharacterBody player = null;
            float deadline = Time.unscaledTime + 90f;
            while (Time.unscaledTime < deadline && !player)
            {
                if (sim != forSim || epoch != forEpoch) yield break;
                if (StageReady)
                    player = CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b && b.isPlayerControlled && b.GetComponent<OlimarDriver>());
                if (!player) yield return new WaitForSeconds(0.5f);
            }
            if (!player) yield break;
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!graph) { Plugin.Log.LogInfo("Candypop Buds: this stage has no ground graph, so no buds."); yield break; }
            var nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
            if (nodes.Count == 0) yield break;
            var seed = Run.instance ? (int)(Run.instance.seed & 0x7fffffff) ^ (Run.instance.stageClearCount * 7919) : Environment.TickCount;
            var rng = new System.Random(seed ^ 0x0B01B);
            var firstFive = new uint[] { 0, 1, 2, 3, 4 }.OrderBy(_ => rng.Next()).ToArray();
            float spacing = Mathf.Max(5f, Plugin.BulbMinSpacing.Value);
            var placed = new List<Vector3>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int n = 0; n < wanted; n++)
            {
                bool found = false;
                Vector3 point = default;
                for (int attempt = 0; attempt < 250 && !found; attempt++)
                {
                    if (clock.ElapsedMilliseconds >= 4) { yield return null; clock.Restart(); }
                    if (sim != forSim || epoch != forEpoch || !StageReady) yield break;
                    if (!graph.GetNodePosition(nodes[rng.Next(nodes.Count)], out point)) continue;
                    // Strict spacing first, then a looser one on a small map.
                    float apart = attempt < 150 ? spacing : spacing * 0.4f;
                    if (player && Vector3.Distance(point, player.footPosition) < Mathf.Min(spacing, 20f)) continue;
                    if (placed.Any(p => (p.x - point.x) * (p.x - point.x) + (p.z - point.z) * (p.z - point.z) < apart * apart)) continue;
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.8f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.4f, 2.2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    found = true;
                }
                if (!found) continue;
                var (colour, queen) = PickBulb(rng, n, wanted, firstFive);
                var at = UnityHost.ToSim(point);
                if (Native.pik_sim_add_bulb(forSim, colour, queen ? 1u : 0u, ref at) >= 0) placed.Add(point);
            }
            Plugin.Log.LogInfo($"Candypop Buds: {placed.Count} placed on stage {Run.instance?.stageClearCount + 1} ({(ShipRepairMode.Active ? "Ship Repair" : "other modes")}).");
        }

        // ---- the weight of Pikmin on a flying enemy ------------------------------------------------------------------

        static uint[] weighNeeds;

        /// Tells the sim how much weight of Pikmin brings this flying enemy down: a boss 25 (a Purple counts five), a large
        /// flier 15, a small one 5. A creature that is not flying has no need.
        void SetGroundNeed(CharacterBody b, ulong id)
        {
            if (!Plugin.WeighDownFliers.Value || !b.isFlying) return;
            weighNeeds ??= new[] { Native.pik_weigh_need(0), Native.pik_weigh_need(1), Native.pik_weigh_need(2), Native.pik_weigh_need(3) };
            int cls = b.isBoss || b.isChampion ? 2 : b.hullClassification == HullClassification.Human ? 0 : 1;
            SendNeed(id, weighNeeds[cls]);
        }

        // What the sim was last told for each creature, so the native call is made when a need is new or changed rather than
        // every tick for every flier (the sim keeps the need until the creature leaves its frame).
        // A creature missing from a gather is forgotten by the sim, so it is told again when it returns (the stamp is the
        // gather it was last seen in).
        readonly Dictionary<ulong, (uint need, uint stamp)> sentNeeds = new Dictionary<ulong, (uint need, uint stamp)>();
        uint gatherStamp;

        void SendNeed(ulong id, uint need)
        {
            if (sentNeeds.TryGetValue(id, out var old) && old.need == need && old.stamp + 1 >= gatherStamp)
            {
                sentNeeds[id] = (need, gatherStamp);
                return;
            }
            sentNeeds[id] = (need, gatherStamp);
            Native.pik_sim_set_ground_need(sim, id, need);
        }

        /// Forgets what was sent for creatures that are gone (checked now and then, not every tick).
        void PruneNeeds()
        {
            if (sentNeeds.Count <= bodies.Count + shots.Count + 16) return;
            var gone = new List<ulong>();
            foreach (var id in sentNeeds.Keys)
                if (!bodies.ContainsKey(id) && !shots.ContainsKey(id)) gone.Add(id);
            foreach (var id in gone) sentNeeds.Remove(id);
        }

        /// `WeighedDown`: enough Pikmin hang on a flying creature; it is held to the ground for a moment longer.
        void WeighCreature(in Native.Event e)
        {
            float seconds = Plugin.WeighGroundSeconds.Value;
            if (e.has_creature != 0 && shots.ContainsKey(e.creature))
            {
                // One Pikmin on a shot brings it down: it is destroyed (the Pikmin's hit source for the credit).
                HitShot(e.creature, Native.SourcePiki, e.a);
                return;
            }
            if (seconds <= 0f || e.has_creature == 0 || !bodies.TryGetValue(e.creature, out var body) || !body) return;
            if (!body.healthComponent || !body.healthComponent.alive) return;
            PikminGrounded.Apply(body, seconds);
        }
    }
}
