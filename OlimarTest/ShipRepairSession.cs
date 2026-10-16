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
        sealed class Part
        {
            public int id;
            public uint pellet;
            public string name;
            public uint weight;
            public Vector3 pos, safe, last;
            public bool discovered, delivered;
            public float stalled;
        }
        readonly List<Part> parts = new List<Part>(10);
        Run run;
        Stage stage;
        IntPtr stageSim;
        float arrivalAge, sendAge, winningAge, toastUntil;
        string runId, toast;
        uint remoteEpoch, remoteMask;
        float remoteSeconds;
        int remoteStages, receivedCount;
        ShipRepairProgress.Phase remotePhase;
        bool hydrated, playedVictory, sentResults;
        int[] remoteOrder = Array.Empty<int>();
        Action pendingFailure;
        static string lastResult;
        static string lastResultRun;
        static byte[] pendingPacket;

        internal bool Winning => NetworkServer.active ? Progress.State == ShipRepairProgress.Phase.Winning : remotePhase == ShipRepairProgress.Phase.Winning;
        int Count => NetworkServer.active ? Progress.Count : CountBits(remoteMask);
        double Seconds => NetworkServer.active ? Progress.Seconds : remoteSeconds;
        internal bool Terminal => NetworkServer.active ? Progress.State >= ShipRepairProgress.Phase.Winning : remotePhase >= ShipRepairProgress.Phase.Winning;
        internal bool HoldingFirstArrival => NetworkServer.active
            ? Progress.Stages <= 1 && !Progress.Started && arrivalAge < 20f
            : !hydrated || (remoteStages <= 1 && remotePhase <= ShipRepairProgress.Phase.Arriving);

        void Awake()
        {
            Instance = this;
            run = GetComponent<Run>();
            runId = run.GetUniqueId().ToString();
            lastResult = lastResultRun = null;
            if (pendingPacket != null) { var packet = pendingPacket; pendingPacket = null; Receive(new ByteReader(packet)); }
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
            var world = PikminWorld.Instance;
            bool sceneReady = Stage.instance && world && world.StageReady && world.SimHandle != IntPtr.Zero;
            if (sceneReady && (stage != Stage.instance || stageSim != world.SimHandle))
            {
                CloseStage();
                ShipPartNavigation.Reset();
                stage = Stage.instance; stageSim = world.SimHandle;
                parts.Clear(); arrivalAge = 0;
                Progress.EnterStage();
                StartCoroutine(Spawn(world, Progress.Epoch));
                Broadcast();
            }
            // Run.inSceneTransition starts at the exit countdown, while hauling is
            // still playable. Count that time; close only at actual AdvanceStage.
            bool loading = !sceneReady || stage != Stage.instance || Progress.State == ShipRepairProgress.Phase.Loading;
            Progress.Tick(Time.fixedDeltaTime, loading || Time.timeScale == 0f);
            if (loading) return;
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
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            var body = CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b.isPlayerControlled && b.GetComponent<OlimarDriver>());
            if (!graph || !body) { Plugin.Log.LogWarning("Ship Repair: no ground graph or arrival body; this stage has no parts."); yield break; }
            for (uint id = 0; id < 30; id++)
                if (Native.pik_sim_ship_part_weight(world.SimHandle, id) == 0)
                    throw new InvalidOperationException("Ship Repair requires all 30 extracted ship part models and configs. Missing part " + id);
            var nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
            var rng = new System.Random(unchecked((int)run.seed ^ (int)Progress.Epoch * 7919));
            var route = new List<Vector3>();
            foreach (int id in Progress.Sample(run.seed))
            {
                Vector3 point = default;
                bool found = false;
                for (int attempt = 0; attempt < 200 && nodes.Count > 0; attempt++)
                {
                    if (attempt % 8 == 7) yield return null;
                    if (!world || !world.StageReady || Progress.Epoch != epoch || Progress.State == ShipRepairProgress.Phase.Loading || Terminal) yield break;
                    if (!graph.GetNodePosition(nodes[rng.Next(nodes.Count)], out point)) continue;
                    if (Vector3.Distance(point, body.footPosition) < 20f || parts.Any(p => Vector3.Distance(point, p.pos) < 15f)) continue;
                    if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 6f, UnityHost.Mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f) continue;
                    point = hit.point;
                    if (Physics.CheckSphere(point + Vector3.up * 2.2f, 2f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!ShipPartNavigation.TryPath(point, body.footPosition, route)) continue;
                    found = true; break;
                }
                if (!found) { Plugin.Log.LogWarning($"Ship Repair: no safe reachable site for part {id} after 200 attempts."); continue; }
                var pos = UnityHost.ToSim(point);
                int pellet = Native.pik_sim_spawn_ship_part(world.SimHandle, (uint)id, ref pos, (float)rng.NextDouble() * Mathf.PI * 2f);
                if (pellet < 0) { Plugin.Log.LogError("Ship Repair: native part spawn refused " + id); continue; }
                var name = new byte[128];
                Native.pik_sim_ship_part_name(world.SimHandle, (uint)id, name, (uint)name.Length);
                parts.Add(new Part { id = id, pellet = (uint)pellet, name = Native.Utf8(name), weight = Native.pik_sim_ship_part_weight(world.SimHandle, (uint)id),
                    pos = point, last = point, safe = point });
                Broadcast();
                yield return null;
            }
            Plugin.Log.LogInfo($"Ship Repair: stage {Progress.Epoch}, {parts.Count} reachable parts, {Progress.Count}/30 recovered.");
        }

        void TrackParts(PikminWorld world)
        {
            foreach (var part in parts)
            {
                if (part.delivered) continue;
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
                    if (part.pos.y < part.safe.y - 30f) Recover(part.id, true);
                    if (!part.discovered && CharacterBody.readOnlyInstancesList.Any(b => b.isPlayerControlled && Vector3.Distance(b.footPosition, part.pos) < 25f))
                    {
                        part.discovered = true;
                        ShowToast($"Found {part.name} — needs {part.weight} Pikmin");
                        Broadcast();
                    }
                }
            }
        }

        internal void Receipt(PikminWorld world, Native.Event receipt)
        {
            if (!NetworkServer.active || stage != Stage.instance || stageSim != world.SimHandle) return;
            var part = parts.FirstOrDefault(p => p.pellet == receipt.a && p.id == receipt.c && !p.delivered);
            if (part == null || !Progress.Accepting) return;
            bool receiver = false;
            for (int i = 0; i < world.OnionCount; i++)
                if (world.Onions[i].id == receipt.b && world.Onions[i].is_ship == 0) receiver = true;
            if (!receiver || !Progress.Receive(Progress.Epoch, part.id)) return;
            part.delivered = true;
            world.FitShipPart((uint)part.id);
            ShowToast($"{part.name} recovered — {Progress.Count}/30");
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
            var part = parts.FirstOrDefault(p => p.id == id && !p.delivered);
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
            if (winningAge >= 8f) world.ShipTakeoff(Mathf.Clamp01((winningAge - 8f) / 4f));
            if (winningAge >= 12f) FinishWin();
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
            lastResultRun = runId;
            lastResult = $"Ship Repair — {(won ? "Dolphin repaired" : "Run ended")}\n{Count}/30 parts · {FormatTime(Seconds)} · {Progress.Stages} stages\nSeed {run.seed} · {run.selectedDifficulty}";
            try
            {
                string directory = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "OlimarShipRepairReports");
                Directory.CreateDirectory(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, runId + ".txt"), lastResult +
                    "\nRules: 30 unique parts; up to 10 per normal stage; all Olimar; no artifacts; ABI 30/protocol 1" +
                    $"\nPlayers: {run.participatingPlayerCount}; starting squad R/Y/B: {Plugin.StartRed.Value}/{Plugin.StartYellow.Value}/{Plugin.StartBlue.Value}" +
                    "\nParts: " + string.Join(",", Progress.Order));
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
                Parts = parts.Select(p => new ShipRepairPacket.Part { Id = p.id, Pellet = p.pellet, Name = p.name, Weight = p.weight,
                    X = p.pos.x, Y = p.pos.y, Z = p.pos.z, Discovered = p.discovered, Delivered = p.delivered, Stalled = p.stalled >= 30f }).ToArray(),
            };
            Mp.SendToAll(StateMessage, packet.Write(), true);
        }
        void Receive(ByteReader reader)
        {
            if (!ShipRepairPacket.Read(reader, out var packet) || packet.RunId != runId || packet.Epoch < remoteEpoch ||
                packet.Order.Length < receivedCount || packet.Seconds < remoteSeconds || (packet.Mask & remoteMask) != remoteMask ||
                !remoteOrder.SequenceEqual(packet.Order.Take(remoteOrder.Length))) return;
            if (hydrated && packet.Order.Length > receivedCount)
                ShowToast($"Ship part recovered — {packet.Order.Length}/30");
            parts.Clear();
            parts.AddRange(packet.Parts.Select(p => new Part { id = p.Id, pellet = p.Pellet, name = p.Name, weight = p.Weight,
                pos = new Vector3(p.X, p.Y, p.Z), discovered = p.Discovered, delivered = p.Delivered, stalled = p.Stalled ? 30f : 0f }));
            remoteEpoch = packet.Epoch; remoteMask = packet.Mask; remotePhase = packet.State; remoteSeconds = packet.Seconds; remoteStages = packet.Stages;
            remoteOrder = packet.Order; receivedCount = packet.Order.Length; hydrated = true;
            if (Winning && !playedVictory) { playedVictory = true; SpawnMusic.PlayVictory(); }
            if (packet.State >= ShipRepairProgress.Phase.Winning)
            {
                lastResultRun = runId;
                lastResult = $"Ship Repair — {(remoteMask == ShipRepairProgress.AllParts ? "Dolphin repaired" : "Run ended")}\n{Count}/30 parts · {FormatTime(Seconds)} · {remoteStages} stages\nSeed {run.seed} · {run.selectedDifficulty}";
            }
        }

        void OnGUI()
        {
            if (!run || !ShipRepairMode.Active) return;
            if (run.isGameOverServer || (Terminal && !Winning))
            {
                if (lastResultRun == runId) GUI.Box(new Rect(20, 100, 420, 100), lastResult);
                return;
            }
            GUI.Box(new Rect(20, 100, 340, 70), $"S.S. Dolphin — {Count}/30 parts\nRepair time {FormatTime(Seconds)}\n{parts.Count(p => !p.delivered)} parts remaining on this stage");
            if (Time.unscaledTime < toastUntil) GUI.Box(new Rect(Screen.width / 2f - 260, 75, 520, 45), toast);
            if (Winning)
            {
                GUI.Box(new Rect(Screen.width / 2f - 220, 130, 440, 60), "The Dolphin is repaired!\n" + FormatTime(Seconds));
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
                    if (NetworkServer.active) Recover(part.id);
                    else { var w = new ByteWriter(); w.Str(runId); w.U32(remoteEpoch); w.I32(part.id); Mp.SendToServer(RecoveryMessage, w.ToArray(), true); }
                }
                var user = LocalUserManager.GetFirstLocalUser();
                var camera = user?.cameraRigController ? user.cameraRigController.sceneCam : Camera.main;
                if (!camera) continue;
                var screen = camera.WorldToScreenPoint(part.pos + Vector3.up * 2f);
                if (screen.z <= 0) continue;
                GUI.Label(new Rect(Mathf.Clamp(screen.x - 90, 0, Screen.width - 180), Mathf.Clamp(Screen.height - screen.y, 40, Screen.height - 40), 220, 40),
                    $"◆ {part.name}\n{part.weight} carriers");
            }
        }
        static int CountBits(uint mask) { int n = 0; while (mask != 0) { mask &= mask - 1; n++; } return n; }
        static string FormatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }
}
