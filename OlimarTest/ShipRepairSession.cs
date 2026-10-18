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
    public sealed class ShipRepairSession : MonoBehaviour
    {
        const short StateMessage = 0x4F04, RecoveryMessage = 0x4F05;
        internal static ShipRepairSession Instance { get; private set; }
        internal readonly ShipRepairProgress Progress = new ShipRepairProgress();
        readonly ShipRepairRace race = new ShipRepairRace();
        internal bool Competitive { get; private set; }
        internal uint Winner => race.Winner;
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
        internal sealed class ResultLine
        {
            internal string Name;
            internal int Score;
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

        internal bool Winning => NetworkServer.active ? Progress.State == ShipRepairProgress.Phase.Winning : remotePhase == ShipRepairProgress.Phase.Winning;
        uint LocalUserId => LocalUserManager.GetFirstLocalUser()?.currentNetworkUser ? LocalUserManager.GetFirstLocalUser().currentNetworkUser.netId.Value : 0;
        int Count => Competitive ? race.Players.TryGetValue(LocalUserId, out var player) ? player.Order.Count : 0
            : NetworkServer.active ? Progress.Count : CountBits(remoteMask);
        string Standings => string.Join("\n", race.Players.Values.OrderByDescending(p => p.Order.Count).ThenBy(p => p.User).Select(p => p.Name + " — " + p.Order.Count + "/30 · " + p.Salvaged + " salvaged"));
        ResultLine[] ScoreRows() => Competitive
            ? race.Players.Values.OrderByDescending(p => p.Order.Count).ThenBy(p => p.User)
                .Select(p => new ResultLine { Name = p.Name, Score = p.Order.Count, Winner = p.User == Winner }).ToArray()
            : NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating)
                .Select(u => new ResultLine { Name = u.userName, Score = Count, Winner = ResultWon }).ToArray();
        string WinText => Competitive ? (race.Players.TryGetValue(Winner, out var winner) ? winner.Name : "A player") + " repaired their Dolphin!" : "The Dolphin is repaired!";
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
            var world = PikminWorld.Instance;
            bool sceneReady = Stage.instance && world && world.StageReady && world.SimHandle != IntPtr.Zero;
            if (sceneReady && (stage != Stage.instance || stageSim != world.SimHandle))
            {
                CloseStage();
                ShipPartNavigation.Reset();
                stage = Stage.instance; stageSim = world.SimHandle;
                parts.Clear(); rewardedEnemies.Clear(); stageBossMasters.Clear(); arrivalAge = 0; placementsReady = false;
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
            if (loading) return;
            if (TeleporterInteraction.instance && TeleporterInteraction.instance.bossGroup)
                foreach (var master in TeleporterInteraction.instance.bossGroup.combatSquad.readOnlyMembersList) stageBossMasters.Add(master);
            if (Progress.State == ShipRepairProgress.Phase.Arriving)
            {
                arrivalAge += Time.fixedDeltaTime;
                var drivers = FindObjectsOfType<OlimarDriver>().Where(d => d.Body && d.Body.isPlayerControlled).ToArray();
                int expected = NetworkUser.readOnlyInstancesList.Count(u => u.isParticipating);
                if (arrivalAge >= 20f || (drivers.Length >= expected && expected > 0 && drivers.All(d => !d.Arriving)))
                {
                    Progress.Release(); Broadcast();
                }
            }
            if (Progress.State == ShipRepairProgress.Phase.Playing) TrackParts(world);
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
            var body = CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b.isPlayerControlled && b.GetComponent<OlimarDriver>());
            if (!graph || !body) { Plugin.Log.LogWarning("Ship Repair: no ground graph or arrival body; this stage has no parts."); yield break; }
            for (uint id = 0; id < 30; id++)
                if (Native.pik_sim_ship_part_weight(world.SimHandle, id) == 0)
                    throw new InvalidOperationException("Ship Repair requires all 30 extracted ship part models and configs. Missing part " + id);
            var nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
            var rng = new System.Random(unchecked((int)run.seed ^ (int)Progress.Epoch * 7919));
            var active = NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating).Select(u => u.netId.Value).ToArray();
            var masks = Competitive ? active.Select(id => race.Players[id].Mask).ToArray() : new[] { Progress.Mask };
            int budget = 3 + ShipRepairRace.EnemyBudget(active.Length, Competitive);
            var sample = ShipRepairRace.StageIds(run.seed, epoch, budget, masks);
            // Slot 0: guaranteed stage-boss part. Slots 1-2: shared map parts.
            // All remaining slots are non-stage-boss enemy drops.
            for (int slot = 0; slot < sample.Length; slot++)
            {
                int id = sample[slot];
                Vector3 point = default;
                bool found = false;
                for (int attempt = 0; attempt < 200 && nodes.Count > 0; attempt++)
                {
                    if (attempt % 8 == 7) yield return null;
                    if (!world || !world.StageReady || Progress.Epoch != epoch || Progress.State == ShipRepairProgress.Phase.Loading || Terminal) yield break;
                    if (!graph.GetNodePosition(nodes[rng.Next(nodes.Count)], out point)) continue;
                    if (Vector3.Distance(point, body.footPosition) < 20f || parts.Any(p => HorizontalDistanceSquared(point, p.origin) < PartSpacing * PartSpacing)) continue;
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!ReachableDolphin(world, point)) continue;
                    found = true; break;
                }
                if (!found) { Plugin.Log.LogWarning($"Ship Repair: no safe reachable site for part {id} after 200 attempts."); continue; }
                var name = new byte[128];
                Native.pik_sim_ship_part_name(world.SimHandle, (uint)id, name, (uint)name.Length);
                var part = new Part { id = id, slot = slot, pellet = uint.MaxValue - (uint)slot, name = Native.Utf8(name), weight = Native.pik_sim_ship_part_weight(world.SimHandle, (uint)id),
                    pos = point, last = point, safe = point, origin = point, stageBoss = slot == 0, enemyDrop = slot == 0 || slot >= 3 };
                parts.Add(part);
                if (!part.enemyDrop && !SpawnPart(world, part, point, (float)rng.NextDouble() * Mathf.PI * 2f)) parts.Remove(part);
                Broadcast();
                yield return null;
            }
            Plugin.Log.LogInfo($"Ship Repair: stage {Progress.Epoch}, {parts.Count} reachable parts, {Progress.Count}/30 recovered.");
            placementsReady = true;
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
            bool stageBoss = stageBossMasters.Contains(victim.master) || (TeleporterInteraction.instance && TeleporterInteraction.instance.bossGroup &&
                TeleporterInteraction.instance.bossGroup.combatSquad.ContainsMember(victim.master));
            bool powerful = victim.isBoss || victim.isChampion || victim.baseMaxHealth >= 600f || (victim.isElite && victim.baseMaxHealth >= 200f);
            if (!stageBoss && !powerful) return;
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

        Part BonusPart(PikminWorld world)
        {
            var active = NetworkUser.readOnlyInstancesList.Where(u => u.isParticipating).Select(u => u.netId.Value).ToArray();
            uint cooperativeUnavailable = parts.Where(p => !p.delivered).Aggregate(Progress.Mask, (mask, p) => mask | (1u << p.id));
            var masks = Competitive ? active.Where(id => race.Players.ContainsKey(id)).Select(id => race.Players[id].Mask).ToArray() : new[] { cooperativeUnavailable };
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
            if (Competitive && Winner != 0)
                foreach (int id in race.Players[Winner].Order) Progress.Receive(Progress.Epoch, id);
            part.delivered = true;
            world.FitShipPart((uint)part.id, user);
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
            Progress.Complete();
            Broadcast();
            run.BeginGameOver(ShipRepairMode.Ending);
        }
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
            SaveSummary(false);
            Broadcast();
        }
        void SaveSummary(bool won)
        {
            ResultWon = won;
            ResultDetails = FormatTime(Seconds) + "  |  " + Progress.Stages + " DAYS  |  SEED " + run.seed;
            resultLines = ScoreRows();
            lastResultRun = runId;
            lastResult = $"Ship Repair — {(won ? WinText : "Run ended")}\n{FormatTime(Seconds)} · {Progress.Stages} stages · Seed {run.seed} · {run.selectedDifficulty}\n" +
                (Competitive ? Standings : Count + "/30 parts");
            try
            {
                string directory = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "OlimarShipRepairReports");
                Directory.CreateDirectory(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, runId + ".txt"), lastResult +
                    $"\nRules: competitive={Competitive}; 30 unique parts; 2 map + 1 stage boss + 7 other enemy parts per {(Competitive ? "player" : "team")}; per-player enemy interval={enemyPartInterval}s; spacing=25m; Dolphin delivery; all Olimar; no artifacts; ABI {Native.AbiVersion}/protocol {ShipRepairPacket.Protocol}" +
                    $"\nPlayers: {run.participatingPlayerCount}; starting squad R/Y/B: {Plugin.StartRed.Value}/{Plugin.StartYellow.Value}/{Plugin.StartBlue.Value}" +
                    "\nParts: " + (Competitive ? string.Join("\n", race.Players.Values.Select(p => p.User + " " + p.Name + ": " + string.Join(",", p.Order))) : string.Join(",", Progress.Order)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Repair report could not be saved: " + e.Message); }
        }

        void ShowToast(string message) { toast = message; toastUntil = Time.unscaledTime + 5f; }
        void Broadcast()
        {
            if (!NetworkServer.active) return;
            if (Progress.Epoch == 0) return;
            var packet = new ShipRepairPacket {
                RunId = runId, Epoch = Progress.Epoch, Mask = Progress.Mask, State = Progress.State,
                Seconds = (float)Progress.Seconds, Stages = Progress.Stages, Order = Progress.Order.ToArray(),
                Competitive = Competitive, Winner = Winner, Racers = Competitive ? race.Players.Values.ToArray() : Array.Empty<ShipRepairRacer>(),
                Parts = parts.Select(p => new ShipRepairPacket.Part { Id = p.id, Slot = p.slot, Pellet = p.pellet, Name = p.name, Weight = p.weight,
                    X = p.pos.x, Y = p.pos.y, Z = p.pos.z, Discovered = p.discovered, Delivered = p.delivered, Stalled = p.stalled >= 30f,
                    EnemyDrop = p.enemyDrop, Spawned = p.spawned, StageBoss = p.stageBoss, Recycling = p.recycleAt > 0f }).ToArray(),
            };
            Mp.SendToAll(StateMessage, packet.Write(), true);
        }
        void Receive(ByteReader reader)
        {
            if (!ShipRepairPacket.Read(reader, out var packet) || packet.RunId != runId || packet.Epoch < remoteEpoch ||
                packet.Order.Length < receivedCount || packet.Seconds < remoteSeconds || (packet.Mask & remoteMask) != remoteMask ||
                !remoteOrder.SequenceEqual(packet.Order.Take(remoteOrder.Length))) return;
            if (hydrated && packet.Competitive != Competitive) return;
            if (packet.Competitive && race.Players.Values.Any(previous => !packet.Racers.Any(next => next.User == previous.User &&
                (next.Mask & previous.Mask) == previous.Mask && next.Salvaged >= previous.Salvaged && previous.Order.SequenceEqual(next.Order.Take(previous.Order.Count))))) return;
            int before = Count;
            Competitive = packet.Competitive;
            race.Hydrate(packet.Racers, packet.Winner);
            if (hydrated && Competitive && Count > before) ShowToast("Your Dolphin — " + Count + "/30");
            if (hydrated && packet.Order.Length > receivedCount)
                ShowToast($"Ship part recovered — {packet.Order.Length}/30");
            parts.Clear();
            parts.AddRange(packet.Parts.Select(p => new Part { id = p.Id, slot = p.Slot, pellet = p.Pellet, name = p.Name, weight = p.Weight,
                pos = new Vector3(p.X, p.Y, p.Z), discovered = p.Discovered, delivered = p.Delivered, stalled = p.Stalled ? 30f : 0f,
                enemyDrop = p.EnemyDrop, spawned = p.Spawned, stageBoss = p.StageBoss, recycleAt = p.Recycling ? 1f : 0f }));
            remoteEpoch = packet.Epoch; remoteMask = packet.Mask; remotePhase = packet.State; remoteSeconds = packet.Seconds; remoteStages = packet.Stages;
            remoteOrder = packet.Order; receivedCount = packet.Order.Length; hydrated = true;
            if (Winning && !playedVictory) { playedVictory = true; SpawnMusic.PlayVictory(); }
            if (packet.State == ShipRepairProgress.Phase.Failed || packet.State >= ShipRepairProgress.Phase.Winning)
            {
                ResultWon = packet.State != ShipRepairProgress.Phase.Failed;
                ResultDetails = FormatTime(Seconds) + "  |  " + remoteStages + " DAYS  |  SEED " + run.seed;
                resultLines = ScoreRows();
                lastResultRun = runId;
                lastResult = $"Ship Repair — {(remoteMask == ShipRepairProgress.AllParts ? WinText : "Run ended")}\n{FormatTime(Seconds)} · {remoteStages} stages · Seed {run.seed} · {run.selectedDifficulty}\n" +
                    (Competitive ? Standings : Count + "/30 parts");
            }
        }

        void OnGUI()
        {
            if (!run || !ShipRepairMode.Active) return;
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
                ShipRepairUi.Hud(Count, ScoreRows(), Competitive,
                    scene ? Language.GetString(scene.nameToken) : null,
                    scene && !string.IsNullOrEmpty(scene.subtitleToken) ? Language.GetString(scene.subtitleToken) : null,
                    siteUntil);
            if (Time.unscaledTime < toastUntil) GUI.Box(new Rect(Screen.width / 2f - 260, 75, 520, 45), toast);
            if (Winning)
            {
                GUI.Box(new Rect(Screen.width / 2f - 220, 130, 440, 60), WinText + "\n" + FormatTime(Seconds));
                if (NetworkServer.active && GUI.Button(new Rect(Screen.width / 2f - 75, 200, 150, 30), "Skip celebration")) FinishWin();
                return;
            }
            if (TeleporterInteraction.instance && TeleporterInteraction.instance.isCharged)
                GUI.Label(new Rect(20, 175, 490, 30), $"Leaving {parts.Count(p => !p.delivered)} parts behind — they can return.");
            int row = 0;
            foreach (var part in parts.Where(p => p.discovered && !p.delivered))
            {
                if (part.stalled >= 30f && GUI.Button(new Rect(20, 210 + row++ * 30, 300, 28), "Recover stuck " + part.name))
                {
                    if (NetworkServer.active) Recover(part.slot);
                    else { var w = new ByteWriter(); w.Str(runId); w.U32(remoteEpoch); w.I32(part.slot); Mp.SendToServer(RecoveryMessage, w.ToArray(), true); }
                }
                var user = LocalUserManager.GetFirstLocalUser();
                var camera = user?.cameraRigController ? user.cameraRigController.sceneCam : Camera.main;
                if (!camera) continue;
                var screen = camera.WorldToScreenPoint(part.pos + Vector3.up * 2f);
                if (screen.z <= 0) continue;
                GUI.Label(new Rect(Mathf.Clamp(screen.x - 90, 0, Screen.width - 180), Mathf.Clamp(Screen.height - screen.y, 40, Screen.height - 40), 220, 40),
                    $"◆ {part.name}\n{part.weight} carriers" + (Competitive && race.Players.TryGetValue(LocalUserId, out var me) && (me.Mask & (1u << part.id)) != 0 ? " · Onion salvage" : ""));
            }
        }
        static int CountBits(uint mask) { int n = 0; while (mask != 0) { mask &= mask - 1; n++; } return n; }
        static string FormatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }
}
