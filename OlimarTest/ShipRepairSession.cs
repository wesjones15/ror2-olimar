using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    public sealed partial class ShipRepairSession : MonoBehaviour
    {
        const short StateMessage = 0x4F04, RecoveryMessage = 0x4F05;
        internal static ShipRepairSession Instance { get; private set; }
        internal readonly ShipRepairProgress Progress = new ShipRepairProgress();
        readonly ShipRepairRace race = new ShipRepairRace();
        internal bool Competitive { get; private set; }
        internal uint Winner => race.Winner;
        internal uint Completer => race.Completer;
        public IEnumerable<int> PartsFor(uint user) => Competitive
            ? race.Players.TryGetValue(user, out var player) ? player.Order : Enumerable.Empty<int>() : Progress.Order;
        sealed class Part
        {
            public int id, slot;
            public uint pellet;
            public string name;
            public uint weight;
            public Vector3 pos, safe, last, origin;
            public bool discovered, delivered, enemyDrop, spawned, stageBoss;
            public float stalled;
            public float recycleAt;
        }
        readonly List<Part> parts = new List<Part>(10);
        readonly HashSet<int> rewardedEnemies = new HashSet<int>();
        readonly ShipRepairDropPacing dropPacing = new ShipRepairDropPacing();
        const float PartSpacing = 25f;
        // Parts lying on the map when a stage opens (slots 1..MapParts). Enemies add more on top:
        // slot 0 is the stage boss's, the slots after the map parts are the elite/miniboss drops.
        internal const int MapParts = 10;
        /// The Main Engine (the first part in the story): without it a Dolphin cannot fly, so it is the first thing
        /// to find, and it lies near every player when a stage opens until it has been fitted.
        internal const int EnginePart = 4;
        public bool HasEngine(uint user) => PartsFor(user).Contains(EnginePart);
        // A common enemy's chance to drop a part, still gated by the killer's drop interval.
        const double CommonDropChance = 0.04;
        static readonly Dictionary<int, string> partNames = new Dictionary<int, string>();
        static string NameOf(int id)
        {
            if (partNames.TryGetValue(id, out var known)) return known;
            var world = PikminWorld.Instance;
            if (!world || world.SimHandle == IntPtr.Zero) return "Ship part";
            var bytes = new byte[128];
            Native.pik_sim_ship_part_name(world.SimHandle, (uint)id, bytes, (uint)bytes.Length);
            var name = Native.Utf8(bytes);
            if (string.IsNullOrEmpty(name)) return "Ship part";
            return partNames[id] = name;
        }
        float enemyPartInterval;
        readonly HashSet<CharacterMaster> stageBossMasters = new HashSet<CharacterMaster>();
        System.Random bonusRng;
        Run run;
        Stage stage;
        IntPtr stageSim;
        float arrivalAge, sendAge, winningAge, takeoffAge, toastUntil;
        string runId, toast;
        uint remoteEpoch, remoteMask;
        float remoteSeconds;
        int remoteStages, receivedCount;
        ShipRepairProgress.Phase remotePhase;
        bool hydrated, playedVictory, sentResults, placementsReady;
        int[] remoteOrder = Array.Empty<int>();
        Action pendingFailure;
        static string lastResult;
        internal static string ResultSummary => lastResult;
        /// A run of another mode starts: the last Ship Repair run's results are not its game-over screen.
        internal static void ClearResults()
        {
            lastResult = lastResultRun = ResultDetails = null;
            resultLines = Array.Empty<ShipRepairSession.ResultLine>(); ResultWon = false;
        }
        internal sealed class ResultLine
        {
            internal string Name;
            internal int Parts, Pikmin, Score;
            /// Treasures the crew recovered and their worth in hundredths of a part (shared repair only).
            internal int Treasures, TreasureHundredths;
            internal bool TreasuresOn;
            internal bool Winner;
        }
        static ResultLine[] resultLines = Array.Empty<ResultLine>();
        internal static IEnumerable<ResultLine> ResultLines => resultLines;
        internal static string ResultDetails { get; private set; }
        internal static bool ResultWon { get; private set; }
        SceneDef shownScene;
        Stage shownStage;
        float siteUntil;
        static string lastResultRun;
        static byte[] pendingPacket;
        int teamPikmin;

        internal bool Winning => NetworkServer.active ? Progress.State == ShipRepairProgress.Phase.Winning : remotePhase == ShipRepairProgress.Phase.Winning;
        uint LocalUserId => LocalUserManager.GetFirstLocalUser()?.currentNetworkUser ? LocalUserManager.GetFirstLocalUser().currentNetworkUser.netId.Value : 0;
        int Count => Competitive ? race.Players.TryGetValue(LocalUserId, out var player) ? player.Order.Count : 0
            : NetworkServer.active ? Progress.Count : CountBits(remoteMask);
        string Standings => string.Join("\n", race.Players.Values.OrderByDescending(p => p.Score).ThenByDescending(p => p.Order.Count).ThenBy(p => p.User)
            .Select(p => p.Name + " — " + p.Order.Count + " parts · " + p.Pikmin + " Pikmin · " + p.Treasures + " treasures (" +
                ShipRepairTreasures.Format(p.TreasureHundredths) + " pts) · " + p.Score + " score"));
        ResultLine[] ScoreRows() => Competitive
            ? race.Players.Values.OrderByDescending(p => Winner == 0 ? p.Order.Count : p.Score)
                .ThenByDescending(p => p.Order.Count).ThenBy(p => p.User)
                .Select(p => new ResultLine { Name = p.Name, Parts = p.Order.Count, Pikmin = p.Pikmin,
                    Treasures = p.Treasures, TreasureHundredths = p.TreasureHundredths, TreasuresOn = TreasuresOn,
                    Score = p.Score, Winner = p.User == Winner }).ToArray()
            : new[] { new ResultLine { Name = "CREW", Parts = Count, Pikmin = teamPikmin,
                Treasures = treasureCount, TreasureHundredths = treasurePoints, TreasuresOn = TreasuresOn,
                Score = Count + teamPikmin / 50 + TreasureScore, Winner = ResultWon } };
        string WinText => Competitive ? (race.Players.TryGetValue(Winner, out var winner) ? winner.Name : "A player") + " won Ship Repair!" : "The Dolphin is repaired!";
        double Seconds => NetworkServer.active ? Progress.Seconds : remoteSeconds;
        internal bool Terminal => NetworkServer.active ? Progress.State >= ShipRepairProgress.Phase.Winning : remotePhase >= ShipRepairProgress.Phase.Winning;
        internal bool HoldingFirstArrival => NetworkServer.active
            ? Progress.State == ShipRepairProgress.Phase.Arriving && Progress.Stages <= 1 && !Progress.Started && arrivalAge < 20f
            : !hydrated || (remoteStages <= 1 && remotePhase <= ShipRepairProgress.Phase.Arriving);

        void Awake()
        {
            Instance = this;
            run = GetComponent<Run>();
            runId = run.GetUniqueId().ToString();
            Competitive = NetworkServer.active && Plugin.CompetitiveMode.Value;
            enemyPartInterval = float.IsNaN(Plugin.EnemyPartIntervalSeconds.Value) || float.IsInfinity(Plugin.EnemyPartIntervalSeconds.Value)
                ? 30f : Mathf.Clamp(Plugin.EnemyPartIntervalSeconds.Value, 0f, 600f);
            if (NetworkServer.active) EnrollPlayers();
            TreasureAwake();
            lastResult = lastResultRun = ResultDetails = null;
            resultLines = Array.Empty<ResultLine>(); ResultWon = false;
            if (pendingPacket != null) { var packet = pendingPacket; pendingPacket = null; Receive(new ByteReader(packet)); }
        }
        void EnrollPlayers()
        {
            if (!Competitive) return;
            foreach (var user in NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating))
                race.Enroll(user.netId.Value, user.userName.Length > 60 ? user.userName.Substring(0, 60) : user.userName);
        }
        void OnDestroy()
        {
            CloseStage();
            ShipPartNavigation.Reset();
            if (Instance == this) Instance = null;
            pendingPacket = null;
        }

        internal static void RegisterMessages()
        {
            if (ShipRepairPacket.Abi != Native.AbiVersion) throw new InvalidOperationException("Repair protocol ABI does not match Native.cs.");
            Mp.Register(StateMessage, null, (from, reader) => {
                if (NetworkServer.active) return;
                if (Instance) Instance.Receive(reader);
                else if (ShipRepairMode.Active) pendingPacket = reader.Raw;
            });
            Mp.Register(RecoveryMessage, (from, reader) => {
                string identity = reader.Str(); uint epoch = reader.U32(); int id = reader.I32();
                if (reader.Failed || !reader.Done || !Instance || identity != Instance.runId || epoch != Instance.Progress.Epoch) return;
                // Recovery cannot award anything, and is permitted only for a verified jam.
                Instance.Recover(id);
            }, null);
        }

        internal void CloseStage()
        {
            if (NetworkServer.active) Progress.CloseStage();
        }

        void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            EnrollPlayers();
            ShipRepairGuests.Tick();
            var world = PikminWorld.Instance;
            bool sceneReady = Stage.instance && world && world.StageReady && world.SimHandle != IntPtr.Zero;
            if (sceneReady && (stage != Stage.instance || stageSim != world.SimHandle))
            {
                CloseStage();
                ShipPartNavigation.Reset();
                stage = Stage.instance; stageSim = world.SimHandle;
                parts.Clear(); rewardedEnemies.Clear(); stageBossMasters.Clear(); arrivalAge = 0; placementsReady = false;
                TreasureStage();
                dropPacing.Reset();
                Progress.EnterStage();
                bonusRng = new System.Random(unchecked((int)run.seed ^ (int)Progress.Epoch * 104729));
                StartCoroutine(Spawn(world, Progress.Epoch));
                Broadcast();
            }
            // Run.inSceneTransition starts at the exit countdown, while hauling is
            // still playable. Count that time; close only at actual AdvanceStage.
            bool loading = !sceneReady || stage != Stage.instance || Progress.State == ShipRepairProgress.Phase.Loading;
            Progress.Tick(Time.fixedDeltaTime, loading || Time.timeScale == 0f);
            CheckSkinUnlocks();
            if (loading) return;
            if (TeleporterInteraction.instance && TeleporterInteraction.instance.bossGroup)
                foreach (var master in TeleporterInteraction.instance.bossGroup.combatSquad.readOnlyMembersList) stageBossMasters.Add(master);
            if (Progress.State == ShipRepairProgress.Phase.Arriving)
            {
                arrivalAge += Time.fixedDeltaTime;
                var drivers = FindObjectsOfType<OlimarDriver>().Where(d => d.Body && d.Body.isPlayerControlled).ToArray();
                int expected = NetworkUser.readOnlyInstancesList.Count(u => u.isParticipating && !ShipRepairGuests.IsGuestUser(u));
                bool guestsOnly = expected == 0 && ShipRepairGuests.AnyGuests && arrivalAge >= 3f;
                if (arrivalAge >= 20f || guestsOnly || (drivers.Length >= expected && expected > 0 && drivers.All(d => !d.Arriving)))
                {
                    Progress.Release(); Broadcast();
                }
            }
            if (Progress.State == ShipRepairProgress.Phase.Playing) { TrackParts(world); TrackTreasures(world); }
            if (Winning) WinTick(world);
            sendAge += Time.fixedDeltaTime;
            if (sendAge > 0.5f) { sendAge = 0; Broadcast(); }
        }

        IEnumerator Spawn(PikminWorld world, uint epoch)
        {
            if (!ShipRepairMode.Eligible(SceneCatalog.GetSceneDefForCurrentScene())) yield break;
            // The receiver's carry goal moves with its arrival pose. Place against
            // its landed goal, otherwise the first frame's airborne goal rejects every site.
            float deadline = Time.unscaledTime + 40f;
            bool landed = false;
            while (!landed && Time.unscaledTime < deadline)
            {
                if (!world || !world.StageReady || Progress.Epoch != epoch || Progress.State == ShipRepairProgress.Phase.Loading || Terminal) yield break;
                for (int i = 0; i < world.OnionCount; i++)
                    if (world.RepairDolphinsReady && world.Onions[i].is_ship != 0 && world.Onions[i].anim == 0) landed = true;
                if (!landed) yield return null;
            }
            if (!landed) { Plugin.Log.LogWarning("Ship Repair: no landed Dolphin; this stage has no parts."); yield break; }
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            var body = CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b.isPlayerControlled && b.GetComponent<OlimarDriver>())
                ?? CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b.isPlayerControlled);
            if (!graph || !body) { Plugin.Log.LogWarning("Ship Repair: no ground graph or arrival body; this stage has no parts."); yield break; }
            for (uint id = 0; id < 30; id++)
                if (Native.pik_sim_ship_part_weight(world.SimHandle, id) == 0)
                    throw new InvalidOperationException("Ship Repair requires all 30 extracted ship part models and configs. Missing part " + id);
            var nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
            var rng = new System.Random(unchecked((int)run.seed ^ (int)Progress.Epoch * 7919));
            var active = NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating).Select(u => u.netId.Value).ToArray();
            var masks = Competitive ? active.Select(id => race.Players[id].Mask).ToArray() : new[] { Progress.Mask };
            // The engine is placed by hand below, so the draw never picks it.
            masks = masks.Select(m => m | (1u << EnginePart)).ToArray();
            yield return PlaceEngines(world, epoch, rng);
            int budget = 1 + MapParts + ShipRepairRace.EnemyBudget(active.Length, Competitive);
            var sample = ShipRepairRace.StageIds(run.seed, epoch, budget, masks);
            // Slot 0: guaranteed stage-boss part. Slots 1..MapParts: parts lying on the map.
            // All remaining slots are non-stage-boss enemy drops.
            var goals = new List<Vector3>();
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].is_ship != 0 && Native.pik_sim_onion_goal(world.SimHandle, world.Onions[i].id, out var goal) != 0)
                    goals.Add(UnityHost.ToUnity(goal));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int slot = 0; slot < sample.Length; slot++)
            {
                int id = sample[slot];
                Vector3 point = default;
                bool found = false;
                // Strict first (spacing, a carry distance a Pikmin path can manage), then relaxed, so a
                // map with few clear sites still gets its parts rather than none.
                for (int attempt = 0; attempt < 450 && nodes.Count > 0; attempt++)
                {
                    if (clock.ElapsedMilliseconds >= 4) { yield return null; clock.Restart(); }
                    if (!world || !world.StageReady || Progress.Epoch != epoch || Progress.State == ShipRepairProgress.Phase.Loading || Terminal) yield break;
                    if (!graph.GetNodePosition(nodes[rng.Next(nodes.Count)], out point)) continue;
                    float spacing = attempt < 150 ? PartSpacing : attempt < 300 ? 14f : 8f;
                    if (Vector3.Distance(point, body.footPosition) < (attempt < 300 ? 20f : 8f) ||
                        parts.Any(p => HorizontalDistanceSquared(point, p.origin) < spacing * spacing)) continue;
                    if (attempt < 250 && goals.Count > 0)
                    {
                        float nearest = goals.Min(g => Mathf.Sqrt(HorizontalDistanceSquared(point, g)));
                        if (nearest < 15f || nearest > 170f) continue;
                    }
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!ReachableDolphin(world, point)) continue;
                    found = true; break;
                }
                if (!found) { Plugin.Log.LogWarning($"Ship Repair: no safe reachable site for part {id} after 450 attempts."); continue; }
                var name = new byte[128];
                Native.pik_sim_ship_part_name(world.SimHandle, (uint)id, name, (uint)name.Length);
                var part = new Part { id = id, slot = slot, pellet = uint.MaxValue - (uint)slot, name = Native.Utf8(name), weight = Native.pik_sim_ship_part_weight(world.SimHandle, (uint)id),
                    pos = point, last = point, safe = point, origin = point, stageBoss = slot == 0, enemyDrop = slot == 0 || slot > MapParts };
                parts.Add(part);
                if (!part.enemyDrop && !SpawnPart(world, part, point, (float)rng.NextDouble() * Mathf.PI * 2f)) parts.Remove(part);
                Broadcast();
                yield return null;
            }
            Plugin.Log.LogInfo($"Ship Repair: stage {Progress.Epoch}, {parts.Count} reachable parts, {Progress.Count}/30 recovered.");
            yield return PlaceTreasures(world, epoch, rng, graph, nodes, goals, body);
            placementsReady = true;
        }

        /// One Main Engine near each player who has not fitted one (one for the crew in the shared repair), on
        /// ground the Dolphin's Pikmin can carry it from.
        IEnumerator PlaceEngines(PikminWorld world, uint epoch, System.Random rng)
        {
            var bodies = CharacterBody.readOnlyInstancesList.Where(b => b.isPlayerControlled).ToList();
            int placed = 0;
            foreach (var body in bodies)
            {
                uint user = UserOfBody(body);
                if (user == 0 || (Competitive ? HasEngine(user) : HasEngine(0))) continue;
                if (!Competitive && placed > 0) break;
                Vector3 point = default;
                bool found = false;
                for (int attempt = 0; attempt < 160 && !found; attempt++)
                {
                    if (!world || !world.StageReady || Progress.Epoch != epoch || Terminal) yield break;
                    if (attempt % 20 == 19) yield return null;
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float radius = 6f + attempt / 40 * 3f + (float)rng.NextDouble() * 4f;
                    var around = body.footPosition + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                    if (!Physics.Raycast(around + Vector3.up * 10f, Vector3.down, out var hit, 24f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (parts.Any(p => HorizontalDistanceSquared(point, p.origin) < 36f)) continue;
                    if (attempt < 120 && !ReachableDolphin(world, point)) continue;
                    found = true;
                }
                if (!found) { Plugin.Log.LogWarning("Ship Repair: no site for a Main Engine near " + body.GetDisplayName()); continue; }
                var name = new byte[128];
                Native.pik_sim_ship_part_name(world.SimHandle, (uint)EnginePart, name, (uint)name.Length);
                int slot = 500 + placed;
                var engine = new Part
                {
                    id = EnginePart, slot = slot, pellet = uint.MaxValue - (uint)slot, name = Native.Utf8(name),
                    weight = Native.pik_sim_ship_part_weight(world.SimHandle, (uint)EnginePart),
                    pos = point, last = point, safe = point, origin = point, discovered = true,
                };
                parts.Add(engine);
                if (!SpawnPart(world, engine, point, (float)rng.NextDouble() * Mathf.PI * 2f)) { parts.Remove(engine); continue; }
                placed++;
                Broadcast();
                yield return null;
            }
            if (placed > 0)
                ShowToast("The Dolphin can't fly without its Main Engine — it is lying near you. Carry it to the Dolphin.");
        }

        static uint UserOfBody(CharacterBody body)
        {
            var master = body ? body.master : null;
            var controller = master ? master.GetComponent<PlayerCharacterMasterController>() : null;
            return controller && controller.networkUser ? controller.networkUser.netId.Value : 0;
        }

        void TrackParts(PikminWorld world)
        {
            // Reserve within the ten-part budget. If combat produces no usable death
            // callback, release physical wreckage when the teleporter finishes.
            if (TeleporterInteraction.instance && TeleporterInteraction.instance.isCharged)
                foreach (var part in parts.Where(p => p.enemyDrop && !p.spawned && p.recycleAt == 0f))
                    if (ClearOfParts(part.safe, part) && SpawnPart(world, part, part.safe)) ShowToast(part.name + " revealed in the wreckage — carry it to the Dolphin.");
            foreach (var part in parts)
            {
                if (part.recycleAt > 0f && Time.time >= part.recycleAt)
                {
                    if (ClearOfParts(part.origin, part) && SpawnPart(world, part, part.origin)) { ShowToast(part.name + " returned to the map."); Broadcast(); }
                }
                if (part.delivered || !part.spawned) continue;
                for (int i = 0; i < world.PelletCount; i++)
                {
                    var pellet = world.Pellets[i];
                    if (pellet.id != part.pellet || pellet.state == 3) continue;
                    part.pos = UnityHost.ToUnity(pellet.pos);
                    bool moved = Vector3.Distance(part.last, part.pos) > 0.25f;
                    part.stalled = pellet.carriers >= pellet.min_carriers && !moved ? part.stalled + Time.fixedDeltaTime : 0f;
                    if (moved) part.last = part.pos;
                    if (pellet.state != 2 && Physics.Raycast(part.pos + Vector3.up * 2f, Vector3.down, out var floor, 5f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        && floor.normal.y > 0.65f) part.safe = floor.point;
                    if (part.pos.y < part.safe.y - 30f) Recover(part.slot, true);
                    if (!part.discovered && CharacterBody.readOnlyInstancesList.Any(b => b.isPlayerControlled && Vector3.Distance(b.footPosition, part.pos) < 25f))
                    {
                        part.discovered = true;
                        ShowToast($"Found {part.name} — needs {part.weight} Pikmin");
                        Broadcast();
                    }
                }
            }
        }

        static bool ReachableDolphin(PikminWorld world, Vector3 from)
        {
            var route = new List<Vector3>();
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].is_ship != 0 && Native.pik_sim_onion_goal(world.SimHandle, world.Onions[i].id, out var goal) != 0 &&
                    ShipPartNavigation.TryPath(from, UnityHost.ToUnity(goal), route)) return true;
            return false;
        }

        bool SpawnPart(PikminWorld world, Part part, Vector3 point, float yaw = 0f)
        {
            if (part.spawned) return false;
            var pos = UnityHost.ToSim(point);
            int pellet = Native.pik_sim_spawn_ship_part(world.SimHandle, (uint)part.id, ref pos, yaw);
            if (pellet < 0) { Plugin.Log.LogError("Ship Repair: native part spawn refused " + part.id); return false; }
            part.pellet = (uint)pellet; part.spawned = true;
            part.recycleAt = part.stalled = 0f;
            part.pos = part.safe = part.last = point;
            return true;
        }

        internal void EnemyDefeated(PikminWorld world, DamageReport report)
        {
            if (!NetworkServer.active || !Progress.Accepting || !placementsReady || stage != Stage.instance || stageSim != world.SimHandle) return;
            var victim = report.victimBody;
            if (!victim || report.victimTeamIndex != TeamIndex.Monster) return;
            if (TekiBody.GuaranteesPart(victim)) { TekiPartReward(world, report, victim); return; }
            bool stageBoss = stageBossMasters.Contains(victim.master) || (TeleporterInteraction.instance && TeleporterInteraction.instance.bossGroup &&
                TeleporterInteraction.instance.bossGroup.combatSquad.ContainsMember(victim.master));
            bool powerful = victim.isBoss || victim.isChampion || victim.baseMaxHealth >= 600f || (victim.isElite && victim.baseMaxHealth >= 200f);
            if (!stageBoss && !powerful && (bonusRng == null || bonusRng.NextDouble() >= CommonDropChance)) return;
            if (!rewardedEnemies.Add(victim.GetInstanceID())) return;
            uint earner = KillerUser(report);
            if (!stageBoss && !dropPacing.Ready(earner, Time.time)) return;
            // Exactly one reserved stage-boss reward across multi-member boss groups.
            // Stage bosses never drain the separately scaled miniboss/elite quota.
            var drops = parts.Where(p => p.enemyDrop && !p.spawned && p.recycleAt == 0f && !p.delivered && p.stageBoss == stageBoss).Take(1).ToArray();
            if (!stageBoss && drops.Length == 0 && bonusRng != null && bonusRng.NextDouble() < 0.2)
            {
                var bonus = BonusPart(world);
                if (bonus != null) drops = new[] { bonus };
            }
            for (int i = 0; i < drops.Length; i++)
            {
                var part = drops[i];
                if (!DropSite(world, part, victim.footPosition, out var point)) continue;
                if (!SpawnPart(world, part, point)) continue;
                if (!stageBoss) dropPacing.Dropped(earner, Time.time, enemyPartInterval);
                part.discovered = true;
                ShowToast(victim.GetDisplayName() + " dropped " + part.name + " — carry it to the Dolphin.");
            }
            Broadcast();
        }

        static uint KillerUser(DamageReport report)
        {
            var master = report.attackerMaster;
            if (!master && report.attackerBody) master = report.attackerBody.master;
            for (int depth = 0; master && depth < 8; depth++)
            {
                var player = master.GetComponent<PlayerCharacterMasterController>();
                if (player && player.networkUser) return player.networkUser.netId.Value;
                master = master.minionOwnership ? master.minionOwnership.ownerMaster : null;
            }
            return 0;
        }
        static float HorizontalDistanceSquared(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
        bool ClearOfParts(Vector3 point, Part ignored) => parts.All(p => p == ignored || !p.spawned || p.delivered ||
            HorizontalDistanceSquared(point, p.pos) >= PartSpacing * PartSpacing);
        bool DropSite(PikminWorld world, Part part, Vector3 desired, out Vector3 point)
        {
            if (Physics.Raycast(desired + Vector3.up * 6f, Vector3.down, out var hit, 18f, UnityHost.Mask, QueryTriggerInteraction.Ignore) &&
                hit.normal.y >= 0.7f && !Physics.CheckSphere(hit.point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore) &&
                ClearOfParts(hit.point, part) && ReachableDolphin(world, hit.point))
            { point = hit.point; return true; }
            // A kill fest at the teleporter must not build a local pile. Prefer the
            // reserved site, then another validated site clear of existing hauls.
            foreach (var safe in new[] { part.origin }.Concat(parts.Select(p => p.origin)).Distinct().Take(32))
                if (ClearOfParts(safe, part) && ReachableDolphin(world, safe)) { point = safe; return true; }
            point = default; return false;
        }

        Part BonusPart(PikminWorld world, uint forUser = 0)
        {
            var active = NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating).Select(u => u.netId.Value).ToArray();
            uint cooperativeUnavailable = parts.Where(p => !p.delivered).Aggregate(Progress.Mask, (mask, p) => mask | (1u << p.id));
            var masks = TekiRules.BenefitMasks(Competitive, forUser != 0 && race.Players.TryGetValue(forUser, out var beneficiary) ? beneficiary.Mask : (uint?)null,
                    active.Where(id => race.Players.ContainsKey(id)).Select(id => race.Players[id].Mask), cooperativeUnavailable)
                .Select(m => m | (1u << EnginePart)).ToArray();
            var ids = ShipRepairRace.StageIds(run.seed ^ (uint)bonusRng.Next(), Progress.Epoch, 1, masks);
            if (ids.Length == 0) return null;
            // Bound simultaneous world records, not the number earned in a stage.
            // Consumed slots are recycled, so farming can keep yielding parts.
            var part = parts.FirstOrDefault(p => p.delivered);
            if (part == null && parts.Count >= 451) return null;
            var site = parts.FirstOrDefault();
            if (site == null) return null;
            var safe = site.origin;
            var name = new byte[128];
            Native.pik_sim_ship_part_name(world.SimHandle, (uint)ids[0], name, 128);
            int slot = part?.slot ?? Enumerable.Range(0, 451).First(candidate => parts.All(p => p.slot != candidate));
            var next = new Part { id = ids[0], slot = slot, name = Native.Utf8(name),
                weight = Native.pik_sim_ship_part_weight(world.SimHandle, (uint)ids[0]), enemyDrop = true,
                pos = safe, safe = safe, origin = safe, last = safe };
            next.pellet = uint.MaxValue - (uint)next.slot;
            if (part != null) parts.Remove(part);
            parts.Add(next);
            return next;
        }

        internal void Receipt(PikminWorld world, Native.Event receipt)
        {
            if (!NetworkServer.active || stage != Stage.instance || stageSim != world.SimHandle) return;
            if (receipt.c >= ShipRepairTreasures.PartBase) { TreasureReceipt(world, receipt); return; }
            var part = parts.FirstOrDefault(p => p.spawned && p.pellet == receipt.a && p.id == receipt.c && !p.delivered);
            if (part == null || !Progress.Accepting) return;
            bool receiver = false, onion = false;
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].id == receipt.b) { receiver = true; onion = world.Onions[i].is_ship == 0; }
            if (!receiver) return;
            uint user = Competitive ? world.UserForShip((uint)receipt.b) : 0;
            if (Competitive && onion && race.Salvage(user, part.id))
            {
                uint seeds = part.weight * 2;
                for (uint color = 0; color < 3; color++) Native.pik_sim_give_onion_seeds(world.SimHandle, (uint)receipt.b, color, seeds / 3 + (color < seeds % 3 ? 1u : 0u));
                ShowToast(race.Players[user].Name + " fed " + part.name + " to their Onion — " + seeds + " seeds!");
                if (NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating).Any(u => race.Players.TryGetValue(u.netId.Value, out var rival) && (rival.Mask & (1u << part.id)) == 0))
                {
                    part.spawned = part.discovered = false; part.stalled = 0f;
                    part.pellet = uint.MaxValue - (uint)part.slot; part.recycleAt = Time.time + 5f;
                }
                else part.delivered = true;
                Broadcast(); return;
            }
            if (onion) return;
            bool accepted = Competitive ? user != 0 && race.Receive(user, part.id) : Progress.Receive(Progress.Epoch, part.id);
            if (!accepted)
            {
                // Two copies can finish intake in the same native tick. Put the
                // rejected physical copy back; another racer may still need it.
                part.spawned = false; SpawnPart(world, part, part.safe); Broadcast(); return;
            }
            if (Competitive && race.Completer != 0)
            {
                race.FinalizeScores(world.ScorePikmin);
                foreach (int id in race.Players[race.Completer].Order) Progress.Receive(Progress.Epoch, id);
            }
            part.delivered = true;
            world.FitShipPart((uint)part.id, user);
            // The Main Engine lets the Dolphin fly to the teleporter.
            if (part.id == EnginePart) world.EngineFitted(Competitive ? user : 0);
            ShowToast(Competitive ? race.Players[user].Name + " recovered " + part.name + " — " + race.Players[user].Order.Count + "/30" : $"{part.name} recovered — {Progress.Count}/30");
            Plugin.Log.LogInfo($"Ship Repair: delivered {part.id} ({part.name}) at {Progress.Seconds:F2}s; {Progress.Count}/30.");
            if (Winning)
            {
                foreach (var director in FindObjectsOfType<CombatDirector>()) director.enabled = false;
                foreach (var body in CharacterBody.readOnlyInstancesList)
                    if (body.isPlayerControlled) body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
            }
            Broadcast();
        }

        void Recover(int id, bool fell = false)
        {
            var part = parts.FirstOrDefault(p => p.spawned && p.slot == id && !p.delivered);
            var world = PikminWorld.Instance;
            if (part == null || !world || world.SimHandle != stageSim || Terminal || (!fell && part.stalled < 30f)) return;
            var pos = UnityHost.ToSim(part.safe);
            if (Native.pik_sim_recover_pellet(stageSim, part.pellet, ref pos) == 0) return;
            part.pos = part.last = part.safe; part.stalled = 0f;
            ShowToast(part.name + " returned to safe ground. Reassign its carriers.");
            Broadcast();
        }

        void WinTick(PikminWorld world)
        {
            winningAge += Time.fixedDeltaTime;
            if (!playedVictory)
            {
                playedVictory = true;
                SpawnMusic.PlayVictory();
            }
            // Normally 8 + 4 seconds; simultaneous final deliveries may have a
            // fitting queue. Let it finish before takeoff, with a bounded fallback.
            if (takeoffAge > 0f || (winningAge >= 8f && (world.RepairsPending == 0 || winningAge >= 65f)))
            {
                takeoffAge += Time.fixedDeltaTime;
                world.ShipTakeoff(Mathf.Clamp01(takeoffAge / 4f));
                if (takeoffAge >= 4f) FinishWin();
            }
        }
        void FinishWin()
        {
            if (sentResults) return;
            sentResults = true;
            SaveSummary(true);
            ShipRepairUnlocks.OnWin(runId);
            Progress.Complete();
            Broadcast();
            run.BeginGameOver(ShipRepairMode.Ending);
        }

        void CheckSkinUnlocks() => OlimarSkinUnlocks.CheckParts(user => Competitive ? PartsFor(user).Count() : Count);
        internal void ScheduleFailure(Action gameOver)
        {
            if (pendingFailure == null && !Winning) pendingFailure = gameOver;
        }
        void LateUpdate()
        {
            var failure = pendingFailure;
            pendingFailure = null;
            if (failure == null || Winning) return;
            Failed();
            failure();
        }
        internal void Failed()
        {
            Progress.Fail();
            if (Competitive && NetworkServer.active && PikminWorld.Instance)
                race.CapturePikmin(PikminWorld.Instance.ScorePikmin);
            SaveSummary(false);
            Broadcast();
        }
        void SaveSummary(bool won)
        {
            ResultWon = won;
            if (!Competitive && NetworkServer.active && PikminWorld.Instance)
                teamPikmin = PikminWorld.Instance.ScorePikmin(0);
            ResultDetails = FormatTime(Seconds) + "  |  " + Progress.Stages + " DAYS  |  SEED " + run.seed + TreasureDetail();
            resultLines = ScoreRows();
            lastResultRun = runId;
            lastResult = $"Ship Repair — {(won ? WinText : "Run ended")}\n{FormatTime(Seconds)} · {Progress.Stages} stages · Seed {run.seed} · {run.selectedDifficulty}\n" +
                (Competitive ? Standings : Count + " parts · " + teamPikmin + " Pikmin" + TreasureSummary() + " · " +
                    (Count + teamPikmin / 50 + TreasureScore) + " score");
            try
            {
                string directory = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "OlimarShipRepairReports");
                Directory.CreateDirectory(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, runId + ".txt"), lastResult +
                    $"\nRules: competitive={Competitive}; 30 unique parts; {MapParts} map + 1 stage boss + 7 other enemy parts per {(Competitive ? "player" : "team")}; per-player enemy interval={enemyPartInterval}s; spacing=25m; Dolphin delivery; all Olimar; artifacts allowed; ABI {Native.AbiVersion}/protocol {ShipRepairPacket.Protocol}" +
                    $"\nPlayers: {run.participatingPlayerCount}; starting squad R/Y/B: {Plugin.StartRed.Value}/{Plugin.StartYellow.Value}/{Plugin.StartBlue.Value}" +
                    "\nParts: " + (Competitive ? string.Join("\n", race.Players.Values.Select(p => p.User + " " + p.Name + ": " + string.Join(",", p.Order))) : string.Join(",", Progress.Order)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Repair report could not be saved: " + e.Message); }
        }

        void ShowToast(string message) { toast = message; toastUntil = Time.unscaledTime + 5f; }
        bool warnedPacketSize;
        void Broadcast()
        {
            if (!NetworkServer.active) return;
            if (Progress.Epoch == 0) return;
            var packet = new ShipRepairPacket {
                RunId = runId, Epoch = Progress.Epoch, Mask = Progress.Mask, State = Progress.State,
                Seconds = (float)Progress.Seconds, Stages = Progress.Stages, Order = Progress.Order.ToArray(),
                Competitive = Competitive, Winner = Winner, TeamPikmin = teamPikmin,
                TreasureCount = treasureCount, TreasurePoints = treasurePoints, Treasures = TreasuresPacket(),
                Racers = Competitive ? race.Players.Values.ToArray() : Array.Empty<ShipRepairRacer>(),
                Parts = parts.Select(p => new ShipRepairPacket.Part { Id = p.id, Slot = p.slot, Pellet = p.pellet, Name = p.name, Weight = p.weight,
                    X = p.pos.x, Y = p.pos.y, Z = p.pos.z, Discovered = p.discovered, Delivered = p.delivered, Stalled = p.stalled >= 30f,
                    EnemyDrop = p.enemyDrop, Spawned = p.spawned, StageBoss = p.stageBoss, Recycling = p.recycleAt > 0f }).ToArray(),
            };
            var bytes = packet.WriteFitting();
            if (bytes.Length >= ShipRepairPacket.MaxBytes && !warnedPacketSize)
            {
                warnedPacketSize = true;
                Plugin.Log.LogWarning("Ship Repair: state message is " + bytes.Length + " bytes; some parts are left out of it.");
            }
            Mp.SendToAll(StateMessage, bytes, true);
        }
        void Receive(ByteReader reader)
        {
            if (!ShipRepairPacket.Read(reader, out var packet) || packet.RunId != runId || packet.Epoch < remoteEpoch ||
                packet.Order.Length < receivedCount || packet.Seconds < remoteSeconds || (packet.Mask & remoteMask) != remoteMask ||
                !remoteOrder.SequenceEqual(packet.Order.Take(remoteOrder.Length))) return;
            if (hydrated && packet.Competitive != Competitive) return;
            if (packet.Competitive && race.Players.Values.Any(previous => !packet.Racers.Any(next => next.User == previous.User &&
                (next.Mask & previous.Mask) == previous.Mask && next.Salvaged >= previous.Salvaged &&
                next.Pikmin >= previous.Pikmin && next.Treasures >= previous.Treasures && next.TreasureHundredths >= previous.TreasureHundredths &&
                previous.Order.SequenceEqual(next.Order.Take(previous.Order.Count))))) return;
            int before = Count;
            Competitive = packet.Competitive;
            race.Hydrate(packet.Racers, packet.Winner);
            teamPikmin = packet.TeamPikmin;
            if (hydrated && packet.TreasureCount > treasureCount) ShowToast("Treasure recovered — " + packet.TreasureCount + " so far");
            ReceiveTreasures(packet);
            if (hydrated && Competitive && Count > before) ShowToast("Your Dolphin — " + Count + "/30");
            if (hydrated && packet.Order.Length > receivedCount)
                ShowToast($"Ship part recovered — {packet.Order.Length}/30");
            parts.Clear();
            parts.AddRange(packet.Parts.Select(p => new Part { id = p.Id, slot = p.Slot, pellet = p.Pellet, name = NameOf(p.Id), weight = p.Weight,
                pos = new Vector3(p.X, p.Y, p.Z), discovered = p.Discovered, delivered = p.Delivered, stalled = p.Stalled ? 30f : 0f,
                enemyDrop = p.EnemyDrop, spawned = p.Spawned, stageBoss = p.StageBoss, recycleAt = p.Recycling ? 1f : 0f }));
            remoteEpoch = packet.Epoch; remoteMask = packet.Mask; remotePhase = packet.State; remoteSeconds = packet.Seconds; remoteStages = packet.Stages;
            remoteOrder = packet.Order; receivedCount = packet.Order.Length; hydrated = true;
            CheckSkinUnlocks();
            if (Winning && !playedVictory) { playedVictory = true; SpawnMusic.PlayVictory(); }
            if (packet.State == ShipRepairProgress.Phase.Failed || packet.State >= ShipRepairProgress.Phase.Winning)
            {
                ResultWon = packet.State != ShipRepairProgress.Phase.Failed;
                if (ResultWon) ShipRepairUnlocks.OnWin(runId);
                ResultDetails = FormatTime(Seconds) + "  |  " + remoteStages + " DAYS  |  SEED " + run.seed + TreasureDetail();
                resultLines = ScoreRows();
                lastResultRun = runId;
                lastResult = $"Ship Repair — {(remoteMask == ShipRepairProgress.AllParts ? WinText : "Run ended")}\n{FormatTime(Seconds)} · {remoteStages} stages · Seed {run.seed} · {run.selectedDifficulty}\n" +
                    (Competitive ? Standings : Count + " parts · " + teamPikmin + " Pikmin" + TreasureSummary() + " · " +
                        (Count + teamPikmin / 50 + TreasureScore) + " score");
            }
        }

        // In VR its overlays go on the wrist panel with Olimar's HUD (OlimarVr); a desktop click on its
        // button still reaches it.
        void OnGUI() => OlimarVr.Character.OnWrist(DrawGui);

        void DrawGui()
        {
            if (!run || !ShipRepairMode.Active || Ror2Common.PauseMenu.IsOpen) return;
            if (run.isGameOverServer || (Terminal && !Winning))
            {
                return;
            }
            var scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (Stage.instance && scene && ShipRepairMode.Eligible(scene) &&
                (scene != shownScene || Stage.instance != shownStage))
            {
                shownScene = scene; shownStage = Stage.instance;
                siteUntil = Time.unscaledTime + 5f;
            }
            if (Event.current.type == EventType.Repaint)
                ShipRepairUi.Hud(Count, ScoreRows(), Competitive, LocalTreasureCount(), LocalTreasurePoints(),
                    scene ? Language.GetString(scene.nameToken) : null,
                    scene && !string.IsNullOrEmpty(scene.subtitleToken) ? Language.GetString(scene.subtitleToken) : null,
                    siteUntil);
            if (Time.unscaledTime < toastUntil)
            {
                GUI.Box(new Rect(Screen.width / 2f - 360, 75, 720, 50), GUIContent.none);
                ShipRepairUi.PikminText(toast, Screen.width / 2f, 86f, 28f, 690f, new Color(1f, .94f, .72f));
            }
            if (Winning)
            {
                GUI.Box(new Rect(Screen.width / 2f - 220, 130, 440, 60), WinText + "\n" + FormatTime(Seconds));
                if (NetworkServer.active && GUI.Button(new Rect(Screen.width / 2f - 75, 200, 150, 30), "Skip celebration")) FinishWin();
                return;
            }
            if (TeleporterInteraction.instance && TeleporterInteraction.instance.isCharged)
                GUI.Label(new Rect(20, 175, 490, 30), $"Leaving {parts.Count(p => !p.delivered)} parts behind — they can return.");
            TreasureLabels();
            int row = 0;
            foreach (var part in parts.Where(p => p.discovered && !p.delivered))
            {
                if (part.stalled >= 30f && GUI.Button(new Rect(20, 210 + row++ * 30, 300, 28), "Recover stuck " + part.name))
                {
                    if (NetworkServer.active) Recover(part.slot);
                    else { var w = new ByteWriter(); w.Str(runId); w.U32(remoteEpoch); w.I32(part.slot); Mp.SendToServer(RecoveryMessage, w.ToArray(), true); }
                }
                // Once Pikmin have it, the carrier count over it says what is needed; the name steps aside.
                if (HasCarriers(part)) continue;
                var user = LocalUserManager.GetFirstLocalUser();
                var camera = user?.cameraRigController ? user.cameraRigController.sceneCam : Camera.main;
                if (!camera) continue;
                var screen = camera.WorldToScreenPoint(part.pos + Vector3.up * 2f);
                if (screen.z <= 0) continue;
                float labelX = Mathf.Clamp(screen.x, 130f, Screen.width - 130f), labelY = Mathf.Clamp(Screen.height - screen.y, 40, Screen.height - 70);
                ShipRepairUi.PikminText(part.name, labelX, labelY, 26f, 260f, new Color(1f, .78f, .28f));
                ShipRepairUi.PikminText(part.weight + " carriers" + (Competitive && race.Players.TryGetValue(LocalUserId, out var me) && (me.Mask & (1u << part.id)) != 0 ? " - Onion salvage" : ""),
                    labelX, labelY + 28f, 18f, 260f, new Color(1f, .94f, .72f));
            }
        }
        // Whether any Pikmin have taken hold of the part (the world's pellets, or a client's replica of them).
        static bool HasCarriers(Part part)
        {
            var world = PikminWorld.Instance;
            if (!world) return false;
            for (int i = 0; i < world.PelletCount; i++)
                if (world.Pellets[i].id == part.pellet) return world.Pellets[i].state <= 1 && world.Pellets[i].carriers > 0;
            return false;
        }
        static int CountBits(uint mask) { int n = 0; while (mask != 0) { mask &= mask - 1; n++; } return n; }
        static string FormatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }
}
