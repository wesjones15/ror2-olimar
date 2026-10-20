using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using Ror2Common;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // The host-side owner of libpikmin's sim. It runs on the server (the host, or solo) while
    // at least one Olimar is alive there, one sim per stage:
    // - every FixedUpdate it hands the sim each captain's input and RoR2's enemies (their
    //   hurtboxes become the parts Pikmin latch onto), then advances the 30 Hz clock;
    // - Pikmin hits come back through the damage callback and land as RoR2 damage from
    //   Olimar, with on-hit items at a low proc coefficient;
    // - enemy hits near Pikmin hurt them, and enemies that die leave a corpse pellet to carry;
    // - between stages the squad and the Onions' stock come along (Pikmin left elsewhere stay
    //   behind, as at sunset in Pikmin);
    // - items reach the sim through each Olimar's OlimarItems, and the shared parts here:
    //   per-Pikmin proc clocks, Wake of Vultures' affixes, healing wards, cooldown-scaled
    //   timers, Pluripotent Larva's squad, and Goobo Jr.'s temporary Olimar and squad.
    internal partial class PikminWorld : MonoBehaviour
    {
        public static PikminWorld Instance { get; private set; }

        const int MaxPartsPerCreature = 32;
        const float FallKillMetres = 60f;

        static readonly Native.DamageFn damageFn = Damage;

        IntPtr sim;
        Native.Host host;
        Stage simStage;
        bool reportedPoison;
        float alphaAtFixed;
        uint tickHz = 30;

        // ---- multiplayer ---------------------------------------------------------------------
        // The server's sends a snapshot of its sim every few ticks (PikminEnvelope); every other
        // machine runs a PikminWorld in ClientMode instead: `sim` is then a replica that holds what
        // the snapshots say (nothing steps), and everything that reads `sim` (the view, the squad
        // chips, the Onion window) sees the server's world.

        /// This machine shows the server's world instead of running its own.
        public bool ClientMode { get; private set; }
        // Counts the server's sims (one per stage); the replica starts over when it changes.
        uint epoch, ignoreThrough;
        int ticksSinceSend;
        byte[] snapshotBuffer;
        readonly List<CaptainInfo> sendInfos = new List<CaptainInfo>();
        // What the server said about each captain (client): whose body, arriving, beam-in.
        readonly Dictionary<int, CaptainInfo> remoteInfo = new Dictionary<int, CaptainInfo>();
        readonly Dictionary<int, (float at, byte count)> healing = new Dictionary<int, (float, byte)>();
        const float HealingSeconds = 2f;
        static bool warnedAssets, warnedSize;
        // Network ticks per second, and bytes sent in the last report period (Debug.LogNet).
        long bytesSent;
        float reportAt;

        // Captains: one per Olimar's CharacterMaster, so a respawned body keeps his Pikmin.
        readonly Dictionary<CharacterMaster, int> captainOf = new Dictionary<CharacterMaster, int>();
        readonly Dictionary<int, OlimarDriver> driverOf = new Dictionary<int, OlimarDriver>();
        readonly List<int> onionIds = new List<int>();
        // Each Onion's RoR2 interactable (the Onion menu's prompt).
        // Each Onion's RoR2 body, as its Olimar's minion: his items, its own position.
        readonly Dictionary<int, Minion> onionMinions = new Dictionary<int, Minion>();
        readonly Dictionary<CharacterBody, int> onionBodyIds = new Dictionary<CharacterBody, int>();
        // Goobo Jr.'s Olimars: captains whose squad is temporary and who have no Onion.
        readonly HashSet<int> tempCaptains = new HashSet<int>();
        // Pluripotent Larva uses seen per master (ExtraLifeVoidConsumed).
        readonly Dictionary<CharacterMaster, int> larvaSeen = new Dictionary<CharacterMaster, int>();
        // When each Pikmin may next proc (Items.ProcInterval), by Pikmin id.
        float[] procReadyAt = new float[128];
        readonly PikminAffixes affixes = new PikminAffixes();
        // Keep stepping until then after an Olimar goes down, so his knockdown plays.
        float stepUntil;
        // Healing wards about (Bustling Fungus and the like), refreshed now and then.
        HealingWard[] wards = new HealingWard[0];
        float wardsFoundAt = -10f;
        static System.Reflection.FieldInfo wardInterval;
        static bool wardLooked;

        sealed class Cargo
        {
            public readonly int[] squad = new int[3], stored = new int[3];
            public readonly uint colour = (uint)UnityEngine.Random.Range(0, 3);
            public bool saved;
        }
        sealed class OnionFlight
        {
            public int id, captain;
            public CharacterMaster owner;
            public Vector3 position, landing, transitionStart;
            public float transitionElapsed, transitionDuration;
            public bool following = true, airborne = true;
            public bool releaseWhenReady;
            public bool dormant;
            /// Dormant because its owner died (it wakes when he is alive again), not from damage.
            public bool ownerDeath;
            /// His squad is going home before it goes dormant: when that is over (Time.fixedTime), and the
            /// next time to send the Pikmin still outside.
            public float recallUntil, recallNext;
            /// Seconds dormant, and seconds into the revival (the sim's two phases), for its gauge.
            public float dormantAge, reviveAge;
            public bool reviving;
            public int pickup = -1;
            public float pickupDwell;
        }
        readonly Dictionary<CharacterMaster, Cargo> cargo = new Dictionary<CharacterMaster, Cargo>();
        sealed class DolphinFlight
        {
            public int id, captain;
            public Vector3 start, landing;
            public float elapsed;
            public bool complete, bringSquad;
            public Vector3? dock;
            public Vector3 relocationStart;
            public float relocationAge;
            public bool relocating;
            /// Landed and stuck there until the Main Engine is fitted (Ship Repair); then it flies to the teleporter.
            public bool waitingForEngine;
            // The Onion releases the squad; the Dolphin beams Olimar down separately.
            public bool onionDown, squadOut;
            /// Seconds the Onion has stood landed; Olimar's beam-in progress 0..1 (-1 not started).
            public float landedFor, beam = -1f;
            /// When the Onion lifts to follow him again (Time.fixedTime), 0 for never.
        }
        readonly Dictionary<int, DolphinFlight> dolphins = new Dictionary<int, DolphinFlight>();
        readonly Dictionary<int, OnionFlight> onionOf = new Dictionary<int, OnionFlight>();

        // This frame's creatures, and their bodies by id for the damage callback.
        Native.Creature[] creatures = new Native.Creature[64];
        Native.Part[] parts = new Native.Part[256];
        int creatureCount;
        readonly Dictionary<ulong, CharacterBody> bodies = new Dictionary<ulong, CharacterBody>();
        readonly Dictionary<ulong, int> creatureIndex = new Dictionary<ulong, int>();

        // Read back after each tick that ran.
        public Native.Piki[] Pikis = new Native.Piki[128];
        public Native.Pellet[] Pellets = new Native.Pellet[32];
        /// Enemies' bodies standing in for their corpse pellets.
        internal readonly Corpses Corpses = new Corpses();
        public Native.Onion[] Onions = new Native.Onion[4];
        public Native.Sprout[] Sprouts = new Native.Sprout[32];
        public int PikiCount, PelletCount, OnionCount, SproutCount;
        readonly Native.Event[] events = new Native.Event[64];

        PikminView view;
        internal uint RepairsPending => view ? view.RepairsPending : 0;
        internal void PlaySound(byte sound, Vector3 at) { if (view) view.PlaySound(sound, at); }

        // RoR2 events raised while the sim is stepping (a Pikmin hit kills an enemy, whose
        // death would spawn a pellet) wait here: calling back into the sim mid-step isn't
        // allowed.
        bool stepping;
        readonly List<Action> afterStep = new List<Action>();

        public bool Running => sim != IntPtr.Zero;

        // ---- a client's own Olimar -----------------------------------------------------------
        // He moves himself so he feels no lag: a small sim with only his captain runs here, on his
        // controls and this world's ground and walls, and his body follows it. The server's captain for him
        // follows the pose this sends (PikminInputPacket), and does everything with the squad.
        IntPtr moverSim;
        OlimarDriver moverDriver;
        bool moverPoisoned;
        float moverAlpha;

        /// A player's own Olimar on a client: his captain in the local sim (0), or -1 for now.
        internal static int RegisterLocal(OlimarDriver d)
        {
            if (!Plugin.AssetsSettled) return -1;
            return EnsureClient().MoverRegister(d);
        }

        int MoverRegister(OlimarDriver d)
        {
            if (moverSim == IntPtr.Zero)
            {
                moverSim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), (ulong)DateTime.Now.Ticks);
                if (moverSim == IntPtr.Zero)
                {
                    Plugin.Log.LogError("libpikmin's local sim didn't start");
                    return -1;
                }
                moverPoisoned = false;
                var feet = UnityHost.ToSim(d.Body.footPosition);
                if (Native.pik_sim_add_captain(moverSim, ref feet) < 0) return -1;
                Plugin.Log.LogInfo($"{d.Body.GetDisplayName()} moves on this machine; the host's Pikmin follow");
            }
            moverDriver = d;
            return 0;
        }

        void FreeMover()
        {
            if (moverSim != IntPtr.Zero) Native.pik_sim_free(moverSim);
            moverSim = IntPtr.Zero;
            moverDriver = null;
            moverPoisoned = false;
        }

        // One physics step of a client's Olimar's sim: his controls in, his pose and buttons to the server.
        void ClientFixed()
        {
            var d = moverDriver;
            if (moverSim == IntPtr.Zero || !d || !d.Body) return;
            if (Native.pik_sim_poisoned(moverSim) != 0)
            {
                if (!moverPoisoned)
                    Plugin.Log.LogError("libpikmin's local sim hit a bug and stopped; RoR2 moves Olimar until the next stage");
                moverPoisoned = true;
                return;
            }
            var input = default(Native.Input);
            if (d.Active) input = d.BuildInput();
            if (d.Arriving)
            {
                input.use_pose = 1;
                input.pose_pos = UnityHost.ToSim(d.Body.footPosition);
                input.pose_face = UnityHost.Yaw(d.Body.transform.forward);
            }
            Native.pik_sim_set_input(moverSim, 0, ref input);
            var body = d.Body;
            float speed = MinionStats.Of(body).moveSpeed;
            if (body.isSprinting) speed *= body.sprintingSpeedMultiplier;
            Native.pik_sim_set_captain_speed(moverSim, 0, speed);
            Native.pik_sim_set_captain_reach(moverSim, 0, Plugin.ThrowReach.Value);
            if (d.Active) d.Items.BeforeStep(moverSim, 0);
            Native.pik_sim_set_networked(moverSim, 0);
            uint ticks = Native.pik_sim_advance(moverSim, ref host, Time.fixedDeltaTime);
            moverAlpha = Native.pik_sim_alpha(moverSim);
            moverTicks += ticks;
            if (ticks == 0) return;
            if (Native.pik_sim_captain(moverSim, 0, out var cap) != 0) d.Ticked(cap);
            if (d.Active) d.SendInput(input);
        }

        /// An Onion command from a client's Olimar, for his captain here.
        internal void RemoteOnionCommand(OlimarDriver d, in OnionCommandPacket p)
        {
            int captain = d.Captain;
            switch (p.kind)
            {
                case OnionCommandPacket.Follow:
                    CommandOnion(captain, true);
                    break;
                case OnionCommandPacket.Land:
                    CommandOnion(captain, false);
                    break;
                case OnionCommandPacket.CallOut:
                case OnionCommandPacket.PutAway:
                {
                    int onion = OnionInReach(captain);
                    if (onion < 0 || p.colour > 2) return;
                    if (p.kind == OnionCommandPacket.CallOut) CallOut(captain, onion, p.colour, p.count);
                    else PutAway(captain, onion, p.colour, p.count);
                    break;
                }
                case OnionCommandPacket.CallOutAny:
                case OnionCommandPacket.PutAwayAny:
                case OnionCommandPacket.ReleaseAll:
                case OnionCommandPacket.ReturnAll:
                    ManageOnion(captain, p.kind, p.count);
                    break;
                case OnionCommandPacket.Heal:
                    HealFromOnion(captain);
                    break;
                case OnionCommandPacket.ReleaseFlyingAll:
                    ReleaseFlyingOnion(captain);
                    break;
                case OnionCommandPacket.TriggerPlucker:
                    TriggerOnionPlucker(captain);
                    break;
                case OnionCommandPacket.RecallStuck:
                    RecallStuckCarriers(captain);
                    break;
            }
        }

        /// The sim that takes this machine's Olimar's controls: the real one on the server, the small one that
        /// moves him on a client (his own captain there is its only one).
        internal IntPtr SimHandle => ClientMode ? moverSim : sim;

        internal uint UserForCaptain(int captain)
        {
            if (guestUsers.TryGetValue(captain, out var guest)) return guest;
            if (!driverOf.TryGetValue(captain, out var driver) || !driver || !driver.Body || !driver.Body.master) return 0;
            var controller = driver.Body.master.GetComponent<PlayerCharacterMasterController>();
            return controller && controller.networkUser ? controller.networkUser.netId.Value : 0;
        }

        internal uint UserForShip(uint ship)
        {
            foreach (var dolphin in dolphins.Values) if (dolphin.id == ship) return UserForCaptain(dolphin.captain);
            foreach (var onion in onionOf.Values) if (onion.id == ship) return UserForCaptain(onion.captain);
            return 0;
        }

        // The final Ship Repair score counts only Pikmin with the captain or in
        // their Onion. Free, planted, carrying and attacking Pikmin are excluded.
        internal int ScorePikmin(uint user)
        {
            if (sim == IntPtr.Zero || ClientMode) return 0;
            long count = 0;
            foreach (var flight in onionOf.Values)
            {
                var controller = flight.owner ? flight.owner.GetComponent<PlayerCharacterMasterController>() : null;
                if (!controller || !controller.networkUser ||
                    (user != 0 && controller.networkUser.netId.Value != user)) continue;
                if (OnionTotals(flight.id, flight.captain, out var pikmin))
                    count += (long)pikmin.in_onion + pikmin.in_squad;
            }
            return (int)Math.Min(int.MaxValue, count);
        }

        internal void FitShipPart(uint part, uint user = 0)
        {
            if (sim == IntPtr.Zero || ClientMode) return;
            foreach (var dolphin in dolphins.Values)
                if (user == 0 || UserForCaptain(dolphin.captain) == user) Native.pik_sim_give_ship_part(sim, (uint)dolphin.id, part);
        }

        internal void ShipTakeoff(float progress)
        {
            if (sim == IntPtr.Zero || ClientMode) return;
            foreach (var dolphin in dolphins.Values)
            {
                if (ShipRepairSession.Instance && ShipRepairSession.Instance.Competitive &&
                    UserForCaptain(dolphin.captain) != ShipRepairSession.Instance.Completer) continue;
                var pos = UnityHost.ToSim(dolphin.landing + Vector3.up * (progress * progress * 40f));
                Native.pik_sim_set_onion_pose(sim, (uint)dolphin.id, ref pos, 1);
                Native.pik_sim_set_onion_flight_motion(sim, (uint)dolphin.id, 0, progress);
            }
        }

        /// The sim to read the world from and draw: the real one, or on a client the replica of it.
        internal IntPtr DataHandle => sim;

        /// Whether his controls have a sim to act on.
        public bool Controllable => ClientMode ? moverSim != IntPtr.Zero && !moverPoisoned : Healthy;

        /// How far between his sim's last tick and the next, for smoothing his body.
        public float BodyAlpha => ClientMode ? moverAlpha : RenderAlpha;

        // Olimar's body is drawn by sim time (in ticks) so a slow machine, with several ticks between
        // two drawn frames, still glides through each of them. A client's own moves with its local sim.
        double serverTicks, moverTicks;

        /// The sim time of the tick his body was last set to.
        internal double BodyTickTime => ClientMode ? moverTicks : serverTicks;

        /// The sim time his body is drawn at this frame, a tick behind the newest state so there is always a
        /// later step to glide toward. The sim ran its step to the END of the fixed step (`fixedTime` plus
        /// `fixedDeltaTime`), and a drawn frame is somewhere inside that step, so the clock is the sim's time
        /// at the end of the step minus what is left of it. (0.3.8 left that remainder out, which put the
        /// frames past the newest step for part of every tick: the body froze there and then jumped, a
        /// stutter step at the sim's 30 Hz.)
        internal double BodyShownTime
        {
            get
            {
                float left = Time.fixedDeltaTime - Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime);
                return (ClientMode ? moverTicks + moverAlpha : serverTicks + alphaAtFixed) - left * tickHz - 1.0;
            }
        }

        /// Running and not stopped by a bug in the sim.
        public bool Healthy => sim != IntPtr.Zero && !reportedPoison;
        internal bool StageReady => Healthy && !ClientMode && simStage == Stage.instance;
        internal bool RepairDolphinsReady => dolphins.Values.Where(d => UserForCaptain(d.captain) != 0).All(d => d.complete);

        /// How far the clock is between the last tick and the next, for drawing.
        public float RenderAlpha => ClientMode ? 1f :
            Mathf.Clamp01(alphaAtFixed + (Time.time - Time.fixedTime) * tickHz);

        public static void Hook()
        {
            Run.onRunDestroyGlobal += _ =>
            {
                if (Instance) Destroy(Instance.gameObject);
            };
            GlobalEventManager.onDeathProcServer += r =>
            {
                if (Instance) Instance.OnDeath(r);
            };
            GlobalEventManager.onServerDamageDealt += r =>
            {
                if (Instance) Instance.OnDamageDealt(r);
            };
            HookBlasts();
        }

        public void StopPresentation()
        {
            if (view) view.StopModels();
        }

        public static PikminWorld Ensure()
        {
            if (!Instance)
            {
                var go = new GameObject("PikminWorld");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<PikminWorld>();
                Instance.view = go.AddComponent<PikminView>();
                Instance.view.world = Instance;
            }
            return Instance;
        }

        /// The world on a machine that isn't running the sim: it shows the server's.
        static PikminWorld EnsureClient()
        {
            if (!Instance)
            {
                var go = new GameObject("PikminWorld");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<PikminWorld>();
                Instance.ClientMode = true;
                Instance.view = go.AddComponent<PikminView>();
                Instance.view.world = Instance;
            }
            return Instance;
        }

        void Awake()
        {
            host = UnityHost.Table(damageFn);
            tickHz = Native.pik_tick_hz();
        }

        void OnDestroy()
        {
            FreeMover();
            FreeSim();
            if (Instance == this) Instance = null;
        }

        void FreeSim()
        {
            creaturesGathered = false;
            if (sim != IntPtr.Zero)
            {
                Native.pik_sim_free(sim);
                sim = IntPtr.Zero;
            }
            captainOf.Clear();
            driverOf.Clear();
            onionIds.Clear();
            onionOf.Clear();
            healing.Clear();
            dolphins.Clear();
            guestUsers.Clear();
            guestKeyOf.Clear();
            foreach (var m in onionMinions.Values) m.Release();
            onionMinions.Clear();
            onionBodyIds.Clear();
            bodies.Clear();
            creatureIndex.Clear();
            lastCore.Clear();
            foreach (var b in attackWatched)
                if (b) b.onSkillActivatedServer -= EnemyAttacked;
            attackWatched.Clear();
            thrownAt.Clear();
            tempCaptains.Clear();
            remoteInfo.Clear();
            foreach (var d in OlimarDriver.All)
                if (d) d.SetRemoteCaptain(-1);
            affixes.Clear();
            Array.Clear(procReadyAt, 0, procReadyAt.Length);
            PikiCount = PelletCount = OnionCount = SproutCount = 0;
            Corpses.Clear();
            if (view) view.Clear();
        }

        // ------------------------------------------------------------------------------------
        // Captains

        /// Gives an Olimar on the server his captain, starting this stage's sim with him if
        /// need be. -1 while the assets are still being extracted.
        public int Register(OlimarDriver d)
        {
            if (!Plugin.AssetsSettled) return -1;
            if (sim != IntPtr.Zero && Stage.instance != simStage)
                NewStage();
            var master = d.Body.master;
            if (sim != IntPtr.Zero && master && captainOf.TryGetValue(master, out int known))
            {
                driverOf[known] = d;
                if (ShipRepairMode.Active) d.Arriving = false;
                // Back from the dead (a respawn, Dio's, a Larva): on his feet again.
                Native.pik_sim_down_captain(sim, (uint)known, 0);
                SpawnDolphin(known, d, false);
                Larva(known, d);
                return known;
            }
            bool first = sim == IntPtr.Zero;
            if (first && !StartSim())
                return -1;

            var feet = UnityHost.ToSim(d.Body.footPosition);
            int id = Native.pik_sim_add_captain(sim, ref feet);
            if (id < 0) return -1;
            if (master) captainOf[master] = id;
            driverOf[id] = d;
            if (master) larvaSeen[master] = LarvaUses(master);
            if (IsGooboClone(d))
            {
                // Goobo Jr.'s Olimar: no ship, no Onion of his own, a temporary squad.
                tempCaptains.Add(id);
                d.Arriving = false;
                CopySquad(id, d);
                Plugin.Log.LogInfo($"captain {id} is Goobo Jr.'s {d.Body.GetDisplayName()}");
                return id;
            }
            PlaceOnions(id, d);
            SpawnDolphin(id, d);
            Plugin.Log.LogInfo($"captain {id} is {d.Body.GetDisplayName()} at {d.Body.footPosition}");
            return id;
        }

        public void Unregister(OlimarDriver d)
        {
            if (moverDriver == d) moverDriver = null;
            foreach (var kv in driverOf)
                if (kv.Value == d)
                {
                    driverOf.Remove(kv.Key);
                    if (tempCaptains.Remove(kv.Key) && sim != IntPtr.Zero)
                        EndTemporary(kv.Key, d);
                    break;
                }
        }

        // ---- Goobo Jr. ------------------------------------------------------------------

        // Goobo Jr.'s gummy (its identifier item), or any AI Olimar some player owns.
        static bool IsGooboClone(OlimarDriver d) => GummyClone.Is(d.Body);

        /// A Goobo Jr. clone's captain (a temporary squad's), for drawing it as a gummy.
        public bool IsTemporaryCaptain(int captain) => ClientMode
            ? remoteInfo.TryGetValue(captain, out var info) && info.IsTemporary
            : tempCaptains.Contains(captain);

        // The clone's squad: as many of each colour as its owner has with him now. They're new
        // Pikmin; the owner's squad and Onion lose nothing.
        void CopySquad(int captain, OlimarDriver d)
        {
            if (!Plugin.GooboSquad.Value) return;
            var owner = d.Body.master && d.Body.master.minionOwnership ? d.Body.master.minionOwnership.ownerMaster : null;
            if (!owner || !captainOf.TryGetValue(owner, out int ownerCaptain)) return;
            var counts = new int[3];
            SquadCounts(ownerCaptain, counts);
            var feet = d.Body.footPosition;
            int n = 0;
            for (uint c = 0; c < 3; c++)
                for (int i = 0; i < counts[c]; i++, n++)
                {
                    var off = Quaternion.Euler(0f, n * 37f, 0f) * Vector3.forward * (1f + 0.25f * (n / 8));
                    var p = UnityHost.ToSim(UnityHost.Snap(feet + off));
                    if (Native.pik_sim_spawn_piki(sim, c, ref p, captain) < 0) break;
                }
            Plugin.Log.LogInfo($"Goobo Jr.'s Olimar got a temporary squad of {n}");
        }

        // The clone is gone: so is its squad (what it delivered stays in the real Onion).
        void EndTemporary(int captain, OlimarDriver d)
        {
            uint gone = Native.pik_sim_remove_captain_pikis(sim, (uint)captain);
            Native.pik_sim_down_captain(sim, (uint)captain, 1);
            foreach (var kv in captainOf)
                if (kv.Value == captain)
                {
                    captainOf.Remove(kv.Key);
                    break;
                }
            Plugin.Log.LogInfo($"Goobo Jr.'s Olimar (captain {captain}) left; {gone} temporary Pikmin went with him");
        }

        // ---- Pluripotent Larva -----------------------------------------------------------

        static int LarvaUses(CharacterMaster master)
        {
            var inv = master ? master.inventory : null;
            var used = ByName.Item("ExtraLifeVoidConsumed");
            return inv && used != ItemIndex.None ? inv.GetItemCountEffective(used) : 0;
        }

        // A Larva brought him back: with no Pikmin left anywhere, he comes back with a squad.
        void Larva(int captain, OlimarDriver d)
        {
            var master = d.Body.master;
            if (!master) return;
            int uses = LarvaUses(master);
            larvaSeen.TryGetValue(master, out int seen);
            larvaSeen[master] = uses;
            if (uses <= seen || Plugin.LarvaSquad.Value <= 0) return;
            bool stocked = false;
            if (onionOf.TryGetValue(captain, out var f))
                for (int i = 0; i < OnionCount; i++)
                    if (Onions[i].id == f.id && Onions[i].Stored(0) + Onions[i].Stored(1) + Onions[i].Stored(2) > 0)
                        stocked = true;
            if (stocked || Native.pik_sim_captain_piki_count(sim, (uint)captain) > 0) return;
            var feet = UnityHost.ToSim(d.Body.footPosition);
            uint n = Native.pik_sim_spawn_squad(sim, (uint)captain, ref feet, (uint)Plugin.LarvaSquad.Value);
            Plugin.Log.LogInfo($"Pluripotent Larva: captain {captain} came back with {n} Pikmin");
        }

        bool StartSim()
        {
            sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), (ulong)DateTime.Now.Ticks);
            if (sim == IntPtr.Zero)
            {
                Plugin.Log.LogError("libpikmin's sim didn't start");
                return false;
            }
            Native.pik_sim_set_field_limit(sim, int.MaxValue);
            simStage = Stage.instance;
            Native.pik_sim_set_ship_part_host_delivery(sim, ShipRepairMode.Active ? (byte)1 : (byte)0);
            if (ShipRepairMode.Active && ShipRepairSession.Instance && ShipRepairSession.Instance.Competitive) Native.pik_sim_set_ship_part_competitive_delivery(sim, 1);
            reportedPoison = false;
            epoch++;
            Native.pik_sim_set_auto_rejoin(sim, Plugin.AutoRejoin.Value ? (byte)1 : (byte)0);
            Native.pik_sim_set_shake_protection(sim, Plugin.ShakeProtection.Value);
            Native.pik_sim_set_auto_carry(sim, Mathf.Max(0f, Plugin.AutoCarryMetres.Value) / UnityHost.Scale);
            Native.pik_sim_set_sprout_stages(sim, Mathf.Max(0f, Plugin.SecondsPerStage.Value), Mathf.Max(0f, Plugin.FlowerSeconds.Value));
            Native.pik_sim_set_kill_growth(sim, (uint)Mathf.Max(0, Plugin.KillsPerStage.Value));
            Native.pik_sim_set_dismiss_strike(sim, Mathf.Max(0f, Plugin.StrikeDamage.Value),
                Mathf.Max(0f, Plugin.StrikeReachMetres.Value) / UnityHost.Scale, 0f /* RoR2's Special slot keeps the cooldown */,
                Mathf.Max(0f, Plugin.StrikeChargeSeconds.Value));
            Native.pik_sim_set_squad_pace(sim, Plugin.PaceLeaf.Value, Plugin.PaceBud.Value, Plugin.PaceFlower.Value);
            Native.pik_sim_set_punch_damage(sim, Plugin.PunchDamage.Value);
            Native.pik_sim_set_cursor_homing(sim, Plugin.CursorHoming.Value ? (byte)1 : (byte)0);
            Plugin.Log.LogInfo($"Pikmin sim started ({Native.Utf8(Native.pik_sim_params_source(sim))})");
            view.StartModels();
            return true;
        }

        // A new stage: note who's coming along, then start over. The next Olimar to register
        // starts the new stage's sim.
        void NewStage()
        {
            ShipRepairSession.Instance?.CloseStage();
            foreach (var pair in captainOf)
            {
                if (!cargo.TryGetValue(pair.Key, out var saved)) continue;
                Array.Clear(saved.squad, 0, 3);
                Array.Clear(saved.stored, 0, 3);
                for (int i = 0; i < PikiCount; i++)
                {
                    var p = Pikis[i];
                    if (p.alive != 0 && p.captain == pair.Value && p.color < 3 && InSquad(p.state))
                        saved.squad[p.color]++;
                }
                if (onionOf.TryGetValue(pair.Value, out var flight))
                    for (int i = 0; i < OnionCount; i++)
                        if (Onions[i].id == flight.id)
                            for (uint c = 0; c < 3; c++) saved.stored[c] = (int)Onions[i].Stored(c);
                saved.saved = true;
            }
            FreeSim();
        }

        static bool InSquad(byte s) =>
            s == Native.PikiState.Squad || s == Native.PikiState.Held || s == Native.PikiState.GoHang;

        void PlaceOnions(int captain, OlimarDriver d)
        {
            var owner = d.Body.master;
            if (!cargo.TryGetValue(owner, out var saved)) cargo[owner] = saved = new Cargo();
            AddOnion(d.Body.footPosition + Vector3.up * 5f, 0b111, d.Body);
            int id = onionIds[onionIds.Count - 1];
            Native.pik_sim_set_onion_color(sim, (uint)id, saved.colour);
            Native.pik_sim_set_onion_owner(sim, (uint)id, captain);
            var flight = new OnionFlight { id = id, captain = captain, owner = owner,
                position = d.Body.footPosition + Vector3.up * 5f };
            onionOf[captain] = flight;
            if (ShipRepairMode.Active && ShipRepairSession.Instance && ShipRepairSession.Instance.Competitive && UserForCaptain(captain) != 0)
                Native.pik_sim_set_ship_part_owner(sim, (uint)id, (uint)captain);
            WriteFlight(flight);
        }

        // The ship is a presentation/carry goal only; no parts or repair objective spawn.
        /// How far behind Olimar the Dolphin lands. Its goal spot is 100 units (4 m) ahead of it
        /// (`UfoItem::getGoalPos`); he stands a metre nearer the ship than that, in its rings.
        const float DolphinBehind = 3f;

        void SpawnDolphin(int captain, OlimarDriver driver, bool bringSquad = true)
        {
            // Same-stage respawns retain the existing dock/flight and fitted ship.
            if (ShipRepairMode.Active && dolphins.ContainsKey(captain)) return;
            var body = driver.Body;
            var back = -Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized;
            if (back.sqrMagnitude < 0.5f) back = Vector3.back;
            var landing = body.footPosition;
            if (Physics.Raycast(landing + back * DolphinBehind + Vector3.up * 2f, Vector3.down, out var floor,
                12f, UnityHost.Mask, QueryTriggerInteraction.Ignore) && floor.normal.y > 0.5f)
                landing = floor.point;
            // Indoor stages shorten the approach to the space available below the roof.
            float height = Physics.Raycast(landing + Vector3.up, Vector3.up, out var roof,
                30f, UnityHost.Mask, QueryTriggerInteraction.Ignore) ? Mathf.Max(1f, roof.distance - 6f) : 30f;
            var start = landing + Vector3.up * height;
            var pos = UnityHost.ToSim(driver.Arriving ? start : landing);
            int id = dolphins.TryGetValue(captain, out var old) ? old.id
                : Native.pik_sim_add_ship(sim, ref pos, UnityHost.Yaw(body.transform.forward));
            if (id < 0) { driver.Arriving = false; if (bringSquad) BringPikmin(captain, driver); return; }
            // Ship Repair restores the shared ledger; other modes use a repaired Dolphin.
            if (!dolphins.ContainsKey(captain))
            {
                if (ShipRepairMode.Active)
                {
                    if (ShipRepairSession.Instance)
                        foreach (int part in ShipRepairSession.Instance.PartsFor(UserForCaptain(captain))) Native.pik_sim_give_ship_part(sim, (uint)id, (uint)part);
                }
                else for (uint part = 0; part < 30; part++) Native.pik_sim_give_ship_part(sim, (uint)id, part);
            }
            dolphins[captain] = new DolphinFlight { id = id, captain = captain, start = start, landing = landing, bringSquad = bringSquad };
            if (ShipRepairMode.Active && ShipRepairSession.Instance && ShipRepairSession.Instance.Competitive && UserForCaptain(captain) != 0)
                Native.pik_sim_set_ship_part_owner(sim, (uint)id, (uint)captain);
            if (driver.Arriving)
            {
                Native.pik_sim_set_onion_pose(sim, (uint)id, ref pos, 1);
                Native.pik_sim_set_onion_flight_motion(sim, (uint)id, 1, 0f);
            }
            if (onionOf.TryGetValue(captain, out var onion))
            {
                onion.following = onion.airborne = true;
                onion.transitionDuration = 0f;
                onion.position = start + Vector3.right * 3f + Vector3.up * 4f;
                WriteFlight(onion);
            }
            if (!driver.Arriving) FinishArrival(dolphins[captain], driver);
        }

        // Timings of the arrival, in seconds from the Dolphin's descent: the Onion starts down as the
        // ship nears the ground and takes OnionLanding to land beside it, its light cone opens, and
        // Olimar beams in over BeamSeconds with the squad coming out of the Onion.
        const float DescentSeconds = 3.5f, OnionLandsAt = 1.6f, OnionLanding = 2f, BeamDelay = 0.5f,
            BeamSeconds = 0.9f, SquadOutAt = 0.3f;

        void MoveDolphins()
        {
            foreach (var ship in dolphins.Values)
            {
                if (ship.complete) continue;
                if (ship.relocating)
                {
                    ship.relocationAge += Time.fixedDeltaTime;
                    float age = ship.relocationAge;
                    Vector3 destination = ship.dock.Value;
                    Vector3 atDock;
                    if (age < 2f) atDock = ship.relocationStart + Vector3.up * (Mathf.SmoothStep(0f, 1f, age / 2f) * 20f);
                    else if (age < 5f) atDock = Vector3.Lerp(ship.relocationStart, destination, Mathf.SmoothStep(0f, 1f, (age - 2f) / 3f)) + Vector3.up * 20f;
                    else atDock = destination + Vector3.up * ((1f - Mathf.SmoothStep(0f, 1f, (age - 5f) / 3f)) * 20f);
                    var dockPose = UnityHost.ToSim(atDock);
                    Native.pik_sim_set_onion_pose(sim, (uint)ship.id, ref dockPose, age < 8f ? (byte)1 : (byte)0);
                    if (age < 8f) Native.pik_sim_set_onion_flight_motion(sim, (uint)ship.id, age >= 5f ? (byte)1 : (byte)0, age >= 5f ? (age - 5f) / 3f : Mathf.Clamp01(age / 2f));
                    else { ship.landing = destination; ship.relocating = false; ship.complete = true; Plugin.Log.LogInfo($"Dolphin {ship.id} docked near the teleporter."); }
                    continue;
                }
                if (!driverOf.TryGetValue(ship.captain, out var driver) || !driver) continue;
                ship.elapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(ship.elapsed / DescentSeconds);
                // Decelerate into a short landing. The Onion releases Pikmin beside the
                // Dolphin; Olimar's materialization starts at the ship's hatch.
                float eased = 1f - (1f - t) * (1f - t);
                var at = Vector3.Lerp(ship.start, ship.landing, eased);
                var pos = UnityHost.ToSim(at);
                Native.pik_sim_set_onion_pose(sim, (uint)ship.id, ref pos, t < 1f ? (byte)1 : (byte)0);
                if (t < 1f) Native.pik_sim_set_onion_flight_motion(sim, (uint)ship.id, 1, t);
                onionOf.TryGetValue(ship.captain, out var onion);
                if (onion != null && !ship.onionDown)
                {
                    if (ship.elapsed >= OnionLandsAt)
                    {
                        ship.onionDown = true;
                        onion.following = false;
                        // Where Olimar stands (the Dolphin is behind him).
                        onion.landing = OnionSpot(driver);
                        onion.transitionStart = onion.position;
                        onion.transitionElapsed = 0f;
                        onion.transitionDuration = OnionLanding;
                        onion.airborne = true;
                    }
                    else onion.position = at + Vector3.right * 3f + Vector3.up * 4f;
                    WriteFlight(onion);
                }
                if (!driver.Arriving) { FinishArrival(ship, driver); continue; }
                if (onion == null)
                {
                    if (t >= 1f) FinishArrival(ship, driver);
                    continue;
                }
                // Landed: its cone opens over 0.8 s, then Olimar appears in the beam.
                if (ship.onionDown && !onion.airborne) ship.landedFor += Time.fixedDeltaTime;
                if (ship.beam < 0f && (ship.landedFor >= BeamDelay || ship.elapsed > DescentSeconds + OnionLanding + 3f))
                {
                    ship.beam = 0f;
                    StoreSquadInOnion(ship.captain, driver);
                }
                if (ship.beam >= 0f)
                {
                    ship.beam += Time.fixedDeltaTime / BeamSeconds;
                    if (!ship.squadOut && ship.beam >= SquadOutAt / BeamSeconds)
                    {
                        ship.squadOut = true;
                        CallSquadOut(ship.captain, driver, onion);
                    }
                    if (ship.beam >= 1f) FinishArrival(ship, driver);
                }
            }
        }

        /// Where the Onion lands for the arrival: to Olimar's right, on the ground, so he forms by the
        /// Dolphin and the squad comes out of the Onion to him.
        static Vector3 OnionSpot(OlimarDriver driver)
        {
            var feet = driver.Body.footPosition;
            var right = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(driver.Body.transform.forward, Vector3.up)).normalized;
            if (right.sqrMagnitude < 0.5f) return feet;
            var at = feet + right * 4.5f;
            return Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var floor, 12f, UnityHost.Mask,
                QueryTriggerInteraction.Ignore) && floor.normal.y > 0.5f ? floor.point : feet;
        }

        /// Olimar's beam-in progress, 0..1 while it lasts, else -1 (the presenter draws him as a column of
        /// light settling into himself).
        public float BeamProgress(uint captain) => ClientMode
            ? remoteInfo.TryGetValue((int)captain, out var info) && info.IsArriving && info.beam >= 0f ? Mathf.Clamp01(info.beam) : -1f
            : dolphins.TryGetValue((int)captain, out var s) && !s.complete && s.beam >= 0f ? Mathf.Clamp01(s.beam) : -1f;

        void FinishArrival(DolphinFlight ship, OlimarDriver driver)
        {
            var pos = UnityHost.ToSim(ship.landing);
            Native.pik_sim_set_onion_pose(sim, (uint)ship.id, ref pos, 0);
            ship.complete = true;
            ship.beam = -1f;
            driver.Arriving = false;
            if (ShipRepairMode.Active && driver.Body && driver.Body.isPlayerControlled)
            {
                // The Dolphin can't fly to the teleporter without its Main Engine.
                var session = ShipRepairSession.Instance;
                if (session && !session.HasEngine(UserForCaptain(ship.captain))) ship.waitingForEngine = true;
                else if (TryRepairDock(ship, driver, out var dock))
                {
                    ship.dock = dock; ship.relocationStart = ship.landing;
                    ship.relocating = true; ship.relocationAge = 0f; ship.complete = false;
                }
            }
            // Arriving by the Onion, the squad is already out of it; otherwise (a respawn, an aborted
            // arrival) they appear around Olimar.
            if (ship.bringSquad && !ship.squadOut) BringPikmin(ship.captain, driver);
            Plugin.Log.LogInfo($"Dolphin landed for captain {ship.captain}; " + (ship.onionDown ? "squad out of the Onion, which stays planted." : "Onion following."));
        }

        bool TryRepairDock(DolphinFlight ship, OlimarDriver driver, out Vector3 point) =>
            TryRepairDockFrom(ship, driver.Body.footPosition, out point);

        /// The Main Engine is fitted: the Dolphins that were waiting for it (`user`'s, or all in the shared repair)
        /// fly to the teleporter.
        internal void EngineFitted(uint user)
        {
            if (ClientMode || sim == IntPtr.Zero) return;
            foreach (var ship in dolphins.Values.ToArray())
            {
                if (!ship.waitingForEngine) continue;
                uint owner = UserForCaptain(ship.captain);
                if (user != 0 && owner != user) continue;
                ship.waitingForEngine = false;
                var from = driverOf.TryGetValue(ship.captain, out var driver) && driver && driver.Body ? driver.Body.footPosition
                    : ShipRepairGuests.BodyFor(owner) is CharacterBody guest ? guest.footPosition : ship.landing;
                if (!TryRepairDockFrom(ship, from, out var dock)) continue;
                ship.dock = dock; ship.relocationStart = ship.landing;
                ship.relocating = true; ship.relocationAge = 0f; ship.complete = false;
                Plugin.Log.LogInfo($"Ship Repair: the Main Engine is fitted; Dolphin {ship.id} flies to the teleporter.");
            }
        }

        bool TryRepairDockFrom(DolphinFlight ship, Vector3 from, out Vector3 point)
        {
            point = default;
            var teleporter = TeleporterInteraction.instance;
            if (!teleporter) return false;
            var route = new List<Vector3>();
            // Grounded spawn-style checks: clear hull volume, gentle floor and a
            // no-jump route. Reserve separated docks so later arrivals cannot stack.
            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = (ship.captain * 137.5f + attempt * 22.5f) * Mathf.Deg2Rad;
                float radius = 16f + (attempt / 16) * 8f;
                var candidate = teleporter.transform.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                if (!Physics.Raycast(candidate + Vector3.up * 12f, Vector3.down, out var hit, 24f, UnityHost.Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
                candidate = hit.point;
                if (Physics.CheckCapsule(candidate + Vector3.up * 3.2f, candidate + Vector3.up * 8f, 3f, UnityHost.Mask, QueryTriggerInteraction.Ignore)) continue;
                if (dolphins.Values.Any(other => other != ship && other.dock.HasValue && Vector3.ProjectOnPlane(candidate - other.dock.Value, Vector3.up).sqrMagnitude < 14f * 14f)) continue;
                if (Native.pik_sim_onion_goal(sim, (uint)ship.id, out var currentGoal) == 0) continue;
                var intake = candidate + UnityHost.ToUnity(currentGoal) - ship.landing;
                if (!ShipPartNavigation.TryPath(from, intake, route)) continue;
                point = candidate; return true;
            }
            Plugin.Log.LogWarning("Ship Repair: no clear, reachable teleporter dock; Dolphin remains at its safe arrival site.");
            return false;
        }

        /// Arriving and not yet beamed in: Olimar isn't drawn.
        public bool CaptainHidden(uint captain) => CaptainArriving(captain) && BeamProgress(captain) < 0f;

        public bool CaptainArriving(uint captain) => ClientMode
            ? remoteInfo.TryGetValue((int)captain, out var info) && info.IsArriving
            : driverOf.TryGetValue((int)captain, out var d) && d && d.Arriving;

        void WriteFlight(OnionFlight flight)
        {
            if (flight.dormant) return;
            var pos = UnityHost.ToSim(flight.position);
            Native.pik_sim_set_onion_pose(sim, (uint)flight.id, ref pos, flight.airborne ? (byte)1 : (byte)0);
            if (flight.airborne && flight.transitionDuration > 0f)
                Native.pik_sim_set_onion_flight_motion(sim, (uint)flight.id, flight.following ? (byte)0 : (byte)1,
                    Mathf.Clamp01(flight.transitionElapsed / flight.transitionDuration));
            if (onionMinions.TryGetValue(flight.id, out var minion))
                // goal.mod's live pod centre is about 110 sim units (4.4 m) high;
                // Commando's hidden body has its hurtbox about a metre above its feet.
                minion.Place(flight.position + Vector3.up * 3.5f, Quaternion.identity, flight.airborne, !flight.airborne);
        }

        // libpikmin's dormancy: it folds shut and waits the configured time (at least its 1.5 s
        // fold), then plays a 3 s revival.
        static float DormantSeconds => Mathf.Max(Plugin.OnionReviveSeconds.Value, 1.5f);
        const float ReviveSeconds = 3f;

        /// An Onion's life gauge: where it floats and how full it is.
        public struct OnionGauge
        {
            public object key;
            public Vector3 position;
            public float ratio;
            /// Stays up however full or empty it is (a dormant Onion's, for its whole wait).
            public bool keep;
        }

        /// The Onions that show a Pikmin life gauge: a live one below half health, and a dormant
        /// one refilling with its revival timer (empty as it falls, full as it wakes).
        public void OnionGauges(List<OnionGauge> into)
        {
            foreach (var f in onionOf.Values)
            {
                if (f.dormant)
                {
                    float total = DormantSeconds + ReviveSeconds;
                    float elapsed = f.reviving ? DormantSeconds + Mathf.Min(f.reviveAge, ReviveSeconds) : Mathf.Min(f.dormantAge, DormantSeconds);
                    into.Add(new OnionGauge { key = f, position = f.position + Vector3.up * 5f, ratio = Mathf.Clamp01(elapsed / total), keep = true });
                }
                else if (onionMinions.TryGetValue(f.id, out var minion) && minion.Alive)
                {
                    float ratio = minion.Body.healthComponent.combinedHealthFraction;
                    if (ratio < 0.5f)
                        into.Add(new OnionGauge { key = minion.Body.healthComponent, position = f.position + Vector3.up * 7f, ratio = ratio });
                }
            }
        }

        void MoveOnions()
        {
            foreach (var flight in onionOf.Values)
            {
                if (flight.recallUntil > 0f) RecallToOnion(flight);
                if (flight.dormant && flight.ownerDeath && OwnerAlive(flight))
                {
                    flight.ownerDeath = false;
                    Native.pik_sim_set_onion_dormancy_seconds(sim, (uint)flight.id, Mathf.Max(0f, Plugin.OnionReviveSeconds.Value));
                    Plugin.Log.LogInfo($"Onion {flight.id}: its owner is alive again; reviving in {Plugin.OnionReviveSeconds.Value:0.#} seconds.");
                }
                if (flight.dormant && onionMinions.TryGetValue(flight.id, out var stale))
                {
                    // A dormant or recovering Onion has no body for enemies to find.
                    stale.Release();
                    onionMinions.Remove(flight.id);
                    ForgetOnionBody(flight.id);
                }
                if (flight.dormant)
                {
                    bool disabled = true;
                    byte anim = 0;
                    for (int i = 0; i < OnionCount; i++)
                        if (Onions[i].id == flight.id) { anim = Onions[i].anim; disabled = anim >= 7; }
                    if (disabled)
                    {
                        // The sim's clock, for the gauge: it folds shut and waits, then revives.
                        if (anim == 8)
                        {
                            if (!flight.reviving) { flight.reviving = true; flight.dormantAge = DormantSeconds; flight.reviveAge = 0f; }
                            flight.reviveAge += Time.fixedDeltaTime;
                        }
                        else flight.dormantAge += Time.fixedDeltaTime;
                        continue;
                    }
                    flight.dormant = false;
                    if (driverOf.TryGetValue(flight.captain, out var owner) && owner && owner.Body)
                        AddOnionMinion(flight.id, flight.position + Vector3.up * 3.5f, owner.Body);
                    Plugin.Log.LogInfo($"Onion {flight.id} revived; its stored Pikmin are available again.");
                }
                if (flight.releaseWhenReady)
                {
                    // A newly raised Onion may still be in TakeOff. Wait for Hover so the
                    // native exit queue can dispense the Pikmin down its legs.
                    for (int oi = 0; oi < OnionCount; oi++)
                        if (Onions[oi].id == flight.id && (Onions[oi].anim == 6 || !flight.airborne))
                        {
                            flight.releaseWhenReady = false;
                            ApplyOnionTransfer(flight.captain, flight.id, OnionCommandPacket.ReleaseAll, 0);
                            break;
                        }
                }
                if (dolphins.TryGetValue(flight.captain, out var ship) && !ship.complete && !ship.onionDown) continue;
                if (flight.following && UpdateOnionPickup(flight)) { WriteFlight(flight); continue; }
                if (flight.following && driverOf.TryGetValue(flight.captain, out var d) && d && d.Body)
                {
                    var at = d.Body.footPosition;
                    // Keep the feet clear of terrain even when following uphill.
                    var forward = d.HasState ? UnityHost.Forward(d.State.face) : d.Body.transform.forward;
                    var target = at - forward * 3f + Vector3.up * 5f;
                    target.y = Mathf.Max(target.y, UnityHost.Snap(target).y + 3f);
                    if (flight.transitionDuration > 0f)
                    {
                        flight.transitionElapsed += Time.fixedDeltaTime;
                        float t = Mathf.Clamp01(flight.transitionElapsed / flight.transitionDuration);
                        flight.position = Vector3.Lerp(flight.transitionStart, target, Mathf.SmoothStep(0f, 1f, t));
                        if (t >= 1f) flight.transitionDuration = 0f;
                    }
                    else
                    {
                        // Trail the squad, slowing into the hover point instead of chasing
                        // Olimar at a flat 12+ m/s. Still catch up after speed items or teleports.
                        float distance = Vector3.Distance(target, flight.position);
                        float eased = distance * (1f - Mathf.Exp(-Time.fixedDeltaTime / 0.65f));
                        float travel = Mathf.Min(eased, Mathf.Max(5f, d.Body.moveSpeed * 1.15f) * Time.fixedDeltaTime);
                        flight.position = distance > 40f ? target : Vector3.MoveTowards(flight.position, target, travel);
                    }
                }
                else if (flight.airborne && !flight.following)
                {
                    flight.transitionElapsed += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(flight.transitionElapsed / Mathf.Max(0.01f, flight.transitionDuration));
                    flight.position = Vector3.Lerp(flight.transitionStart, flight.landing, Mathf.SmoothStep(0f, 1f, t));
                    flight.airborne = t < 1f;
                    if (!flight.airborne) flight.transitionDuration = 0f;
                }
                WriteFlight(flight);
            }
        }

        // How far above the ground a flying Onion stops to suck up a loose pellet (it was 1.5,
        // then 0.75). Zero is where a landed Onion stands (`landing = hit.point`), so the bottom
        // of its beam touches the ground.
        const float PickupHeight = 0f;

        // Flying Onions must descend and dwell before sucking up a loose pellet.
        // Carry teams can still hand theirs off normally once the Onion is low enough.
        bool UpdateOnionPickup(OnionFlight flight)
        {
            int pi = -1;
            float best = 6f * 6f;
            for (int i = 0; i < PelletCount; i++)
            {
                var p = Pellets[i];
                if (!OnionPickupPolicy.CanTrack(p, flight.id)) continue;
                if (flight.pickup == (int)p.id) { pi = i; break; }
                if (flight.pickup >= 0 || p.state > 1) continue;
                var at = UnityHost.ToUnity(p.pos);
                var flat = Vector3.ProjectOnPlane(at - flight.position, Vector3.up);
                if (flat.sqrMagnitude < best && Mathf.Abs(at.y - flight.position.y) < 10f)
                { best = flat.sqrMagnitude; pi = i; }
            }
            if (pi < 0)
            {
                flight.pickup = -1;
                flight.pickupDwell = 0f;
                return false;
            }
            var pellet = Pellets[pi];
            flight.pickup = (int)pellet.id;
            if (pellet.state == 2) return true; // Hold still through suction.
            var atPellet = UnityHost.ToUnity(pellet.pos);
            if (!Physics.Raycast(atPellet + Vector3.up * 2f, Vector3.down, out var floor,
                30f, UnityHost.Mask, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.5f)
            { flight.pickup = -1; flight.pickupDwell = 0f; return false; }
            var ground = floor.point;
            var target = ground + Vector3.up * PickupHeight;
            if (Physics.Linecast(flight.position + Vector3.up * 4.4f, target + Vector3.up * 4.4f,
                UnityHost.Mask, QueryTriggerInteraction.Ignore))
            { flight.pickup = -1; flight.pickupDwell = 0f; return false; }
            flight.position = Vector3.MoveTowards(flight.position, target, Time.fixedDeltaTime * 3f);
            flight.transitionDuration = 0f;
            if (Vector3.Distance(flight.position, target) < 0.1f)
            {
                flight.pickupDwell += Time.fixedDeltaTime;
                if (flight.pickupDwell >= 0.75f && pellet.state == 0)
                    Native.pik_sim_collect_ground_pellet(sim, (uint)flight.id, pellet.id);
            }
            else flight.pickupDwell = 0f;
            return true;
        }

        bool OnionDisabled(int id)
        {
            foreach (var f in onionOf.Values) if (f.id == id && f.dormant) return true;
            for (int i = 0; i < OnionCount; i++) if (Onions[i].id == id) return Onions[i].anim >= 7;
            return false;
        }

        public bool OnionFlying(int captain) => ClientMode
            ? remoteInfo.TryGetValue(captain, out var info) && info.OnionAirborne
            : onionOf.TryGetValue(captain, out var f) && f.airborne;
        public uint OnionColour(int onion)
        {
            foreach (var f in onionOf.Values)
                if (f.id == onion && cargo.TryGetValue(f.owner, out var saved)) return saved.colour;
            return Native.Red;
        }
        float relocationNoteAt;

        public bool CommandOnion(int captain, bool follow)
        {
            if (ClientMode)
            {
                if (!moverDriver) return false;
                moverDriver.SendOnion(follow ? OnionCommandPacket.Follow : OnionCommandPacket.Land, 0, 0);
                return true;
            }
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d)
            {
                if (Plugin.LogEvents.Value) Plugin.Log.LogInfo($"captain {captain}: no Onion to {(follow ? "raise" : "lower")}");
                return false;
            }
            if (f.dormant) return false;
            f.pickup = -1;
            f.pickupDwell = 0f;
            if (follow == f.following && (follow || f.airborne)) return true;
            if (!follow)
            {
                // Reject placement over a void instead of dropping the Onion out of the stage.
                if (!Physics.Raycast(d.Body.footPosition + Vector3.up * 2f, Vector3.down, out var hit,
                    30f, UnityHost.Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.5f)
                {
                    if (Plugin.LogEvents.Value) Plugin.Log.LogInfo($"captain {captain}: nowhere flat enough below him to lower the Onion");
                    return false;
                }
                f.landing = hit.point;
            }
            // Each lift and each landing takes a utility charge (Hardlight adds more).
            var slot = d.Body.skillLocator ? d.Body.skillLocator.utility : null;
            if (slot && !NativeSkills.Spend(slot, OlimarSkills.Relocate))
            {
                if (Plugin.LogEvents.Value && Time.unscaledTime >= relocationNoteAt)
                {
                    relocationNoteAt = Time.unscaledTime + 1f;
                    Plugin.Log.LogInfo($"captain {captain}: no Onion relocation charge yet (utility slot {(slot.skillDef ? slot.skillDef.skillName : "empty")}, " +
                        $"{slot.stock}/{slot.maxStock} charges, {slot.cooldownRemaining:0.0} s to the next)");
                }
                return false;
            }
            if (Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"captain {captain}: Onion {(follow ? "raised to follow him" : "lowered here")}");
            f.transitionStart = f.position;
            f.transitionElapsed = 0f;
            f.transitionDuration = follow ? 1.8f : 2.5f;
            f.following = follow;
            f.airborne = true;
            WriteFlight(f);
            return true;
        }

        public bool ReleaseFlyingOnion(int captain)
        {
            if (ClientMode)
            {
                if (!moverDriver || !OnionFlying(captain)) return false;
                moverDriver.SendOnion(OnionCommandPacket.ReleaseFlyingAll, 0, 0);
                return true;
            }
            if (!onionOf.TryGetValue(captain, out var flight) || flight.dormant || !flight.airborne)
                return false;
            flight.releaseWhenReady = true;
            return true;
        }

        void AddOnion(Vector3 at, uint colours, CharacterBody owner)
        {
            var p = UnityHost.ToSim(UnityHost.Snap(at));
            int id = Native.pik_sim_add_onion(sim, ref p, colours);
            if (id < 0) return;
            onionIds.Add(id);
            AddOnionMinion(id, UnityHost.ToUnity(p) + Vector3.up * 3.5f, owner);
        }

        // The Onion's body as the owner's minion, where the sim has the Onion.
        void AddOnionMinion(int id, Vector3 at, CharacterBody owner)
        {
            var m = Minions.Spawn(Content.OnionMinion, owner, at, Quaternion.identity);
            if (m != null)
            {
                onionMinions[id] = m;
                if (m.Body) onionBodyIds[m.Body] = id;
            }
            else Plugin.Log.LogWarning($"Onion {id}'s minion body didn't spawn; it won't get items");
        }

        // The squad and Onion stock from the last stage, or the configured start of a run.
        /// The squad he starts a stage with: what he carried out of the last, or the starting counts
        /// (Blue, Red, Yellow).
        int[] StartingSquad(OlimarDriver d)
        {
            var saved = cargo[d.Body.master];
            return saved.saved ? saved.squad : new[] { Plugin.StartBlue.Value, Plugin.StartRed.Value, Plugin.StartYellow.Value };
        }

        /// Puts his squad, what his Onion held and its plucker's Pikmin into the landed Onion, for it to let
        /// the squad out ([`CallSquadOut`]).
        void StoreSquadInOnion(int captain, OlimarDriver d)
        {
            var saved = cargo[d.Body.master];
            int home = onionOf[captain].id;
            if (home < 0) return;
            var squad = StartingSquad(d);
            for (uint c = 0; c < 3; c++)
                if (squad[c] > 0) Native.pik_sim_store_in_onion(sim, (uint)home, c, 0, (uint)squad[c]);
            if (saved.saved)
                for (uint c = 0; c < 3; c++)
                    if (saved.stored[c] > 0) Native.pik_sim_store_in_onion(sim, (uint)home, c, 0, (uint)saved.stored[c]);
            // An Onion never starts empty: its plucker (Items.AutoPluckDelay) is a Pikmin inside it.
            int inside = saved.saved ? saved.stored[0] + saved.stored[1] + saved.stored[2] : 0;
            if (inside == 0) Native.pik_sim_store_in_onion(sim, (uint)home, Native.Red, 0, 1);
        }

        /// The Onion lets his squad out, to join him. Any it can't (a field limit) are placed around him.
        void CallSquadOut(int captain, OlimarDriver d, OnionFlight onion)
        {
            var squad = StartingSquad(d);
            var feet = d.Body.footPosition;
            var back = -Vector3.ProjectOnPlane(d.Body.transform.forward, Vector3.up).normalized;
            int n = 0;
            for (uint c = 0; c < 3; c++)
            {
                int left = squad[c] - (int)CallOut(captain, onion.id, c, (uint)squad[c]);
                for (; left > 0; left--, n++)
                {
                    var off = Quaternion.Euler(0f, (n % 8 - 3.5f) * 20f, 0f) * back * (1.2f + 0.35f * (n / 8));
                    var p = UnityHost.ToSim(UnityHost.Snap(feet + off));
                    Native.pik_sim_spawn_piki(sim, c, ref p, captain);
                }
            }
        }

        void BringPikmin(int captain, OlimarDriver d)
        {
            var saved = cargo[d.Body.master];
            int[] squad = saved.saved
                ? saved.squad
                : new[] { Plugin.StartBlue.Value, Plugin.StartRed.Value, Plugin.StartYellow.Value };
            var feet = d.Body.footPosition;
            var back = -Vector3.ProjectOnPlane(d.Body.transform.forward, Vector3.up).normalized;
            int n = 0;
            for (uint c = 0; c < 3; c++)
                for (int i = 0; i < squad[c]; i++, n++)
                {
                    // A loose crowd behind him; the sim's formation sorts them out.
                    float ring = 1.2f + 0.35f * (n / 8);
                    var off = Quaternion.Euler(0f, (n % 8 - 3.5f) * 20f, 0f) * back * ring;
                    var p = UnityHost.ToSim(UnityHost.Snap(feet + off));
                    Native.pik_sim_spawn_piki(sim, c, ref p, captain);
                }
            if (saved.saved)
                for (uint c = 0; c < 3; c++)
                {
                    int onion = onionOf[captain].id;
                    if (onion >= 0 && saved.stored[c] > 0)
                        Native.pik_sim_store_in_onion(sim, (uint)onion, c, 0, (uint)saved.stored[c]);
                }
            // An Onion never starts empty: its plucker (Items.AutoPluckDelay) is a Pikmin inside it.
            int home = onionOf[captain].id;
            int inside = saved.saved ? saved.stored[0] + saved.stored[1] + saved.stored[2] : 0;
            if (home >= 0 && inside == 0)
                Native.pik_sim_store_in_onion(sim, (uint)home, Native.Red, 0, 1);
        }

        // ------------------------------------------------------------------------------------
        // The Onion menu, for the driver's UI.

        /// `captain`'s squad by colour (formed up, walking to his hand or held), into `counts`
        /// (indexed by Native colour).
        public void SquadCounts(int captain, int[] counts)
        {
            Array.Clear(counts, 0, counts.Length);
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive != 0 && p.captain == captain && p.color < counts.Length && InSquad(p.state))
                    counts[p.color]++;
            }
        }

        /// The colour `captain` throws next, or -1 for the nearest (he hasn't picked one).
        public int ThrowColour(int captain) =>
            sim == IntPtr.Zero || captain < 0 ? -1 : Native.pik_sim_captain_throw_color(sim, (uint)captain);

        public int OnionInReach(int captain)
        {
            if (ClientMode)
            {
                // The server's word on his Onion, and where it stands.
                if (sim == IntPtr.Zero || !moverDriver || !moverDriver.Body || !remoteInfo.TryGetValue(captain, out var info) || info.onion < 0)
                    return -1;
                for (int i = 0; i < OnionCount; i++)
                    if (Onions[i].id == info.onion)
                        return InOnionBeam(moverDriver, moverSim, 0, UnityHost.ToUnity(Onions[i].pos),
                            info.OnionAirborne, info.OnionFollowing, OnionDisabled(info.onion)) ? info.onion : -1;
                return -1;
            }
            if (sim == IntPtr.Zero || !onionOf.TryGetValue(captain, out var f)
                || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body) return -1;
            return InOnionBeam(d, sim, captain, f.position, f.airborne, f.following, f.dormant) ? f.id : -1;
        }

        static bool InOnionBeam(OlimarDriver driver, IntPtr captainSim, int captain, Vector3 position,
            bool airborne, bool following, bool dormant)
        {
            if (!driver.Active || captainSim == IntPtr.Zero
                || Native.pik_sim_captain(captainSim, (uint)captain, out var state) == 0) return false;
            var offset = (driver.Body.footPosition - position) / UnityHost.Scale;
            return OnionMenuPolicy.InBeam(airborne, following, dormant, state.on_ground != 0,
                offset.x, offset.y, offset.z);
        }

        public bool OnionCounts(int onion, int captain, uint colour, out Native.OnionCounts counts)
        {
            counts = default;
            return sim != IntPtr.Zero && Native.pik_sim_onion_counts(sim, (uint)onion, (uint)captain, colour, out counts) != 0;
        }

        public bool OnionTotals(int onion, int captain, out Native.OnionCounts total)
        {
            total = default;
            for (uint colour = 0; colour < 3; colour++)
            {
                if (!OnionCounts(onion, captain, colour, out var n)) return false;
                total.in_onion += n.in_onion;
                total.in_squad += n.in_squad;
                total.on_field = n.on_field;
                total.max_on_field = n.max_on_field;
            }
            return true;
        }

        public float HealingProgress(uint captain)
        {
            if (ClientMode) return remoteInfo.TryGetValue((int)captain, out var info) && info.IsHealing ? info.beam : -1f;
            if (!healing.TryGetValue((int)captain, out var h)) return -1f;
            float progress = (Time.time - h.at) / HealingSeconds;
            return progress < 1f ? Mathf.Clamp01(progress) : -1f;
        }

        public byte HealingCount(uint captain) => ClientMode
            ? remoteInfo.TryGetValue((int)captain, out var info) && info.IsHealing ? info.healingCount : (byte)0
            : healing.TryGetValue((int)captain, out var h) ? h.count : (byte)0;

        public bool HealFromOnion(int captain)
        {
            int onion = OnionInReach(captain);
            if (onion < 0) return false;
            if (ClientMode)
            {
                if (!moverDriver) return false;
                moverDriver.SendOnion(OnionCommandPacket.Heal, 0, 0);
                return true;
            }
            if (!driverOf.TryGetValue(captain, out var driver) || !driver || !driver.Active || !driver.Body) return false;
            var hp = driver.Body.healthComponent;
            if (!hp || !hp.alive || hp.fullHealth <= 0f) return false;
            float deficit = Mathf.Clamp01((hp.fullHealth - hp.health) / hp.fullHealth);
            float fraction = Native.pik_sim_sacrifice_onion(sim, (uint)onion, deficit, out uint count);
            if (count == 0) return false;
            hp.Heal(fraction * hp.fullHealth, default, true);
            healing[captain] = (Time.time, (byte)Math.Min(count, 255u));
            return true;
        }

        public bool TriggerOnionPlucker(int captain)
        {
            int onion = OnionInReach(captain);
            if (onion < 0) return false;
            if (ClientMode)
            {
                if (!moverDriver) return false;
                moverDriver.SendOnion(OnionCommandPacket.TriggerPlucker, 0, 0);
                return true;
            }
            return sim != IntPtr.Zero && Native.pik_sim_trigger_plucker(sim, (uint)onion) != 0;
        }

        public bool RecallStuckCarriers(int captain)
        {
            if (OnionInReach(captain) < 0) return false;
            if (ClientMode)
            {
                if (!moverDriver) return false;
                moverDriver.SendOnion(OnionCommandPacket.RecallStuck, 0, 0);
                PlaySound(6, moverDriver.Body.footPosition);
                return true;
            }
            if (sim == IntPtr.Zero) return false;
            uint recalled = Native.pik_sim_recall_stuck_carriers(sim, (uint)captain);
            if (driverOf.TryGetValue(captain, out var driver) && driver && driver.Body)
                PlaySound(6, driver.Body.footPosition);
            return recalled != 0;
        }

        // Resolve bulk commands on the server, against current stock, not a client's snapshot.
        // All menu commands require standing in the landed beam, including remote requests.
        public bool ManageOnion(int captain, byte kind, uint count = 0)
        {
            int onion = OnionInReach(captain);
            if (onion < 0 || kind < OnionCommandPacket.CallOutAny || kind > OnionCommandPacket.ReturnAll) return false;
            if (ClientMode)
            {
                if (!moverDriver) return false;
                moverDriver.SendOnion(kind, 0, count);
                return true;
            }
            ApplyOnionTransfer(captain, onion, kind, count);
            return true;
        }

        void ApplyOnionTransfer(int captain, int onion, byte kind, uint count)
        {
            if (!OnionTotals(onion, captain, out var total)) return;
            bool release = kind == OnionCommandPacket.CallOutAny || kind == OnionCommandPacket.ReleaseAll;
            uint room = total.max_on_field > total.on_field ? total.max_on_field - total.on_field : 0;
            uint amount = kind == OnionCommandPacket.ReleaseAll ? OnionMenuPolicy.ReleaseCount(total.in_onion, room)
                : kind == OnionCommandPacket.ReturnAll ? total.in_squad
                : Math.Min(count, release ? Math.Min(total.in_onion, room) : total.in_squad);
            var available = new uint[3];
            for (uint colour = 0; colour < 3; colour++)
            {
                OnionCounts(onion, captain, colour, out var n);
                available[colour] = release ? n.in_onion : n.in_squad;
            }
            var plan = OnionMenuPolicy.Distribute(amount, available[0], available[1], available[2]);
            for (uint colour = 0; colour < 3; colour++)
            {
                if (plan[colour] == 0) continue;
                if (release) CallOut(captain, onion, colour, plan[colour]);
                else PutAway(captain, onion, colour, plan[colour]);
            }
        }

        public uint CallOut(int captain, int onion, uint colour, uint n)
        {
            if (ClientMode)
            {
                if (moverDriver) moverDriver.SendOnion(OnionCommandPacket.CallOut, colour, n);
                return n;
            }
            return sim == IntPtr.Zero ? 0 : Native.pik_sim_onion_call_out(sim, (uint)captain, (uint)onion, colour, n);
        }

        public uint PutAway(int captain, int onion, uint colour, uint n)
        {
            if (ClientMode)
            {
                if (moverDriver) moverDriver.SendOnion(OnionCommandPacket.PutAway, colour, n);
                return n;
            }
            return sim == IntPtr.Zero ? 0 : Native.pik_sim_onion_put_away(sim, (uint)captain, (uint)onion, colour, n);
        }

        public uint OnionColours(int onion)
        {
            for (int i = 0; i < OnionCount; i++)
                if (Onions[i].id == onion) return Onions[i].colors;
            return 0;
        }

        // ------------------------------------------------------------------------------------
        // The step

        void FixedUpdate()
        {
            using var timing = OlimarPerformance.Measure(OlimarPerformance.Part.Fixed);
            if (ClientMode)
            {
                ClientFixed();
                return;
            }
            if (sim == IntPtr.Zero) return;
            if (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal)
            {
                ReadBack();
                view.Snapshot();
                SendSnapshot(1);
                return;
            }
            if (Stage.instance != simStage)
            {
                // The stage is gone; wait for an Olimar to register in the next one.
                if (driverOf.Count == 0 || AllGone()) NewStage();
                return;
            }
            if (Native.pik_sim_poisoned(sim) != 0)
            {
                if (!reportedPoison)
                    Plugin.Log.LogError("libpikmin's sim hit a bug and stopped; the Pikmin will stand still " +
                                        "and RoR2 moves Olimar until the next stage");
                reportedPoison = true;
                return;
            }

            bool anyone = false;
            foreach (var kv in driverOf)
            {
                var input = default(Native.Input);
                if (kv.Value && kv.Value.Arriving) anyone = true;
                if (kv.Value && kv.Value.Arriving && kv.Value.Body)
                {
                    // RoR2 holds the arrival body in place. Hold the native captain at the
                    // same feet too; letting gravity run independently can bury his model.
                    input.use_pose = 1;
                    input.pose_pos = UnityHost.ToSim(kv.Value.Body.footPosition);
                    input.pose_face = UnityHost.Yaw(kv.Value.Body.transform.forward);
                }
                if (kv.Value && kv.Value.Active)
                {
                    input = kv.Value.IsRemote ? kv.Value.RemoteInput() : kv.Value.BuildInput();
                    anyone = true;
                }
                Native.pik_sim_set_input(sim, (uint)kv.Key, ref input);
                // His move speed items (Goat Hoof, Energy Drink...) speed up Navi's run.
                if (kv.Value && kv.Value.Body)
                {
                    var body = kv.Value.Body;
                    float speed = MinionStats.Of(body).moveSpeed;
                    if (body.isSprinting) speed *= body.sprintingSpeedMultiplier;
                    Native.pik_sim_set_captain_speed(sim, (uint)kv.Key, speed);
                }
                Native.pik_sim_set_captain_reach(sim, (uint)kv.Key, Plugin.ThrowReach.Value);
                if (kv.Value && kv.Value.Body && kv.Value.Active) kv.Value.Items.BeforeStep(sim, (uint)kv.Key);
            }
            // With no Olimar about (all dead, or mid-load) the Pikmin wait; a knockdown that's
            // still playing keeps the clock going.
            if (!anyone && Time.fixedTime > stepUntil) return;

            Cooldowns();
            HealInWards();

            MoveDolphins();
            MoveOnions();
            // The sim ticks at 30 Hz and RoR2 steps at 50: most steps carry no tick, and then the enemies
            // the sim was given last step stand. Gathering them again would be the slow part of a weak machine.
            if (alphaAtFixed + Time.fixedDeltaTime * tickHz >= 0.95f || !creaturesGathered)
            {
                GatherCreatures();
                Native.pik_sim_set_creatures(sim, creatures, (uint)creatureCount, parts, (uint)PartCount);
                creaturesGathered = true;
            }
            stepping = true;
            uint ticks;
            try
            {
                Native.pik_sim_set_networked(sim, (byte)(Mp.Networked ? 1 : 0));
                ticks = Native.pik_sim_advance(sim, ref host, Time.fixedDeltaTime);
            }
            finally
            {
                stepping = false;
            }
            alphaAtFixed = Native.pik_sim_alpha(sim);
            serverTicks += ticks;
            OlimarPerformance.Ticks(ticks);
            RunDeferred();
            if (ticks == 0) return;

            ReadBack();
            foreach (var kv in driverOf)
                if (kv.Value && Native.pik_sim_captain(sim, (uint)kv.Key, out var cap) != 0)
                {
                    kv.Value.Ticked(cap);
                    // Pulling a sprout leaves him open: nothing can hurt him until he has it out.
                    if (cap.plucking != 0 && kv.Value.Body && kv.Value.Body.healthComponent && kv.Value.Body.healthComponent.alive)
                        kv.Value.Body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, 0.2f);
                }
            KillFallen();
            DrainEvents();
            view.Snapshot();
            SendSnapshot((int)ticks);
        }

        // ---- the server's snapshots ----------------------------------------------------------

        /// Every few ticks, with other players about, the sim's world goes to them.
        void SendSnapshot(int ticks)
        {
            if (!NetworkServer.active || !Mp.Networked) return;
            ticksSinceSend += ticks;
            int every = Mathf.Max(1, Mathf.RoundToInt(tickHz / Mathf.Max(1f, Plugin.SnapshotHz.Value)));
            if (ticksSinceSend < every) return;
            ticksSinceSend = 0;
            if (snapshotBuffer == null) snapshotBuffer = new byte[128 * 1024];
            uint n = Native.pik_sim_snapshot(sim, snapshotBuffer, (uint)snapshotBuffer.Length);
            while (n == 0 && snapshotBuffer.Length < 16 * 1024 * 1024)
            {
                Array.Resize(ref snapshotBuffer, snapshotBuffer.Length * 2);
                n = Native.pik_sim_snapshot(sim, snapshotBuffer, (uint)snapshotBuffer.Length);
            }
            if (n == 0)
            {
                if (!warnedSize)
                    Plugin.Log.LogWarning("The Pikmin world is too big to send to other players in one snapshot; they won't see it.");
                warnedSize = true;
                return;
            }
            sendInfos.Clear();
            foreach (var kv in driverOf)
            {
                var d = kv.Value;
                if (!d || !d.Body) continue;
                byte flags = 0;
                if (tempCaptains.Contains(kv.Key)) flags |= CaptainInfo.Temporary;
                if (d.Arriving) flags |= CaptainInfo.Arriving;
                short onion = -1;
                if (onionOf.TryGetValue(kv.Key, out var flight))
                {
                    onion = (short)flight.id;
                    if (flight.following) flags |= CaptainInfo.Following;
                    if (flight.airborne) flags |= CaptainInfo.Airborne;
                }
                float heal = HealingProgress((uint)kv.Key);
                if (heal >= 0f) flags |= CaptainInfo.Healing;
                sendInfos.Add(new CaptainInfo { id = (byte)kv.Key, netId = d.Body.netId.Value, flags = flags,
                    beam = heal >= 0f ? heal : BeamProgress((uint)kv.Key),
                    healingCount = heal >= 0f ? HealingCount((uint)kv.Key) : (byte)0, onion = onion });
            }
            var payload = PikminEnvelope.Write(epoch, sendInfos, snapshotBuffer, (int)n);
            if (payload.Length > Fragments.MaxPayload)
            {
                if (!warnedSize) Plugin.Log.LogWarning("Pikmin snapshot exceeds multiplayer transport capacity; remote players may not see this swarm.");
                warnedSize = true;
                return;
            }
            Mp.SendToAll(PikminNet.SnapshotMsg, payload, Plugin.ReliableSnapshots.Value);
            if (Plugin.LogNet.Value)
            {
                bytesSent += payload.Length;
                if (Time.unscaledTime >= reportAt)
                {
                    if (reportAt > 0f) Plugin.Log.LogInfo($"Pikmin snapshots: {payload.Length} bytes each, {bytesSent / 10f / 1024f:F1} KiB/s");
                    bytesSent = 0;
                    reportAt = Time.unscaledTime + 10f;
                }
            }
        }

        /// A snapshot from the server arrived (any machine but the server).
        internal static void ReceiveSnapshot(uint epoch, List<CaptainInfo> captains, byte[] snapshot)
        {
            if (NetworkServer.active || RunAudio.Ended) return;
            // Without the Pikmin disc's assets there is nothing to draw it with.
            if (!Plugin.AssetsReady)
            {
                if (!warnedAssets && Plugin.AssetsSettled)
                    Plugin.Log.LogWarning("The host is playing Olimar, but this machine has no Pikmin assets yet " +
                                          "(set Paths.Disc to your own disc): you won't see their Pikmin.");
                warnedAssets = true;
                return;
            }
            if (!Stage.instance) return;
            EnsureClient().ApplyRemote(epoch, captains, snapshot);
        }

        void ApplyRemote(uint theirEpoch, List<CaptainInfo> captains, byte[] snapshot)
        {
            // A late packet from a stage we've left, or a sim that's since been replaced.
            if (theirEpoch <= ignoreThrough) return;
            if (sim != IntPtr.Zero && theirEpoch != epoch)
            {
                FreeSim();
            }
            if (sim == IntPtr.Zero)
            {
                sim = Native.pik_replica_new(Native.CString(Plugin.AssetsDir));
                if (sim == IntPtr.Zero)
                {
                    Plugin.Log.LogError("libpikmin's replica didn't start");
                    return;
                }
                epoch = theirEpoch;
                simStage = Stage.instance;
                reportedPoison = false;
                Plugin.Log.LogInfo($"Showing the host's Pikmin ({Native.Utf8(Native.pik_sim_params_source(sim))})");
                view.StartModels();
            }
            if (Native.pik_replica_apply(sim, snapshot, (uint)snapshot.Length) == 0) return;
            remoteInfo.Clear();
            foreach (var c in captains) remoteInfo[c.id] = c;
            foreach (var d in OlimarDriver.All)
            {
                if (!d || !d.Body) continue;
                int id = -1;
                foreach (var c in captains)
                    if (c.netId != 0 && c.netId == d.Body.netId.Value)
                    {
                        id = c.id;
                        break;
                    }
                d.SetRemoteCaptain(id);
                // The server decides when his arrival is over; this machine's body follows.
                if (id >= 0 && d.IsLocalClient && d.Arriving && remoteInfo.TryGetValue(id, out var mine) && !mine.IsArriving)
                    d.Arriving = false;
            }
        }

        void Update()
        {
            if (!ClientMode || sim == IntPtr.Zero) return;
            // A new stage: the old world is gone, and so are its snapshots.
            if (!Stage.instance || Stage.instance != simStage)
            {
                ignoreThrough = epoch;
                FreeMover();
                FreeSim();
                return;
            }
            using var timing = OlimarPerformance.Measure(OlimarPerformance.Part.Client);
            Native.pik_replica_update(sim, Time.deltaTime);
            // His own Olimar is drawn where his local sim and smoothed body have him, not where the server's
            // captain was a round trip ago.
            var mine = moverDriver;
            if (moverSim != IntPtr.Zero && mine && mine.Body && mine.ViewCaptain >= 0 && mine.HasState)
            {
                var feet = UnityHost.ToSim(mine.SmoothedFeet);
                Native.pik_replica_overlay_captain(sim, (uint)mine.ViewCaptain, moverSim, 0, ref feet, mine.State.face);
            }
            ReadBack();
            view.Snapshot();
        }

        void RunDeferred()
        {
            for (int i = 0; i < afterStep.Count; i++)
            {
                try { afterStep[i](); }
                catch (Exception e) { Plugin.Log.LogError(e); }
            }
            afterStep.Clear();
        }

        // Which captain an event is his, for his items: the thrower, puncher, whistler,
        // jumper or stomper; a Pikmin's captain; an Onion's owner.
        int EventCaptain(in Native.Event e)
        {
            switch (e.kind)
            {
                case Native.EventKind.Thrown:
                case Native.EventKind.PikiStomped:
                    return e.b;
                case Native.EventKind.Punched:
                case Native.EventKind.Whistle:
                case Native.EventKind.CaptainJumped:
                case Native.EventKind.CaptainStomped:
                case Native.EventKind.CaptainLanded:
                case Native.EventKind.StrikeStarted:
                    return (int)e.a;
                case Native.EventKind.PikiHurt:
                    return e.a < PikiCount ? Pikis[e.a].captain : -1;
                case Native.EventKind.PluckerOut:
                case Native.EventKind.PelletDelivered:
                    foreach (var f in onionOf.Values)
                        if (f.id == e.b) return f.captain;
                    return -1;
                default:
                    return -1;
            }
        }

        // Cooldown items shorten the sim's timers: the fastest Olimar's boost counts for the
        // shared ones (growth, regrouping); the plucker and emergency Pikmin follow it too.
        void Cooldowns()
        {
            float boost = 1f;
            foreach (var d in driverOf.Values)
                if (d && d.Body) boost = Mathf.Max(boost, d.Items.CooldownBoost);
            Native.pik_sim_set_growth(sim, boost, 0.5f / boost);
            float pluck = Plugin.AutoPluckDelay.Value;
            Native.pik_sim_set_auto_pluck(sim, pluck >= 0f ? pluck / boost : -1f);
            Native.pik_sim_set_emergency(sim, Mathf.Max(0f, Plugin.EmergencySeconds.Value) / boost,
                (uint)Mathf.Max(0, Plugin.EmergencyCount.Value));
        }

        // Pikmin standing in a healing ward (Bustling Fungus about a landed Onion or a still
        // Olimar, and the like) heal at its rate, scaled; N'kuhana's Opinion counts it for the
        // nearest Olimar.
        void HealInWards()
        {
            if (Time.fixedTime - wardsFoundAt > 1f)
            {
                wardsFoundAt = Time.fixedTime;
                wards = FindObjectsOfType<HealingWard>();
            }
            if (wards.Length == 0 || PikiCount == 0) return;
            if (!wardLooked)
            {
                wardLooked = true;
                wardInterval = ByName.Field(typeof(HealingWard), "interval");
            }
            float scale = Mathf.Max(0f, Plugin.MinionHealScale.Value);
            foreach (var w in wards)
            {
                if (!w || !w.isActiveAndEnabled || w.healFraction <= 0f) continue;
                var team = w.GetComponent<TeamFilter>();
                if (team && team.teamIndex != TeamIndex.Player) continue;
                float interval = wardInterval?.GetValue(w) is float iv && iv > 0f ? iv : 1f;
                float amount = w.healFraction * 100f / interval * scale * Time.fixedDeltaTime;
                var centre = UnityHost.ToSim(w.transform.position);
                float healed = Native.pik_sim_heal_pikis_in_sphere(sim, ref centre, w.radius / UnityHost.Scale, amount);
                if (healed > 0f) NearestOlimar(w.transform.position)?.Items.PikminHealed(healed);
            }
        }

        OlimarDriver NearestOlimar(Vector3 at)
        {
            OlimarDriver best = null;
            float bestD = float.MaxValue;
            foreach (var d in driverOf.Values)
            {
                if (!d || !d.Body) continue;
                float dist = (d.Body.corePosition - at).sqrMagnitude;
                if (dist < bestD)
                {
                    bestD = dist;
                    best = d;
                }
            }
            return best;
        }

        bool AllGone()
        {
            foreach (var d in driverOf.Values)
                if (d) return false;
            return true;
        }

        int PartCount;

        bool creaturesGathered;

        void GatherCreatures()
        {
            using var timing = OlimarPerformance.Measure(OlimarPerformance.Part.Gather);
            creatureCount = 0;
            PartCount = 0;
            bodies.Clear();
            creatureIndex.Clear();
            if (lastCore.Count > 512) lastCore.Clear();
            attackWatched.RemoveWhere(Gone);
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                if (!b || !b.healthComponent || !b.healthComponent.alive) continue;
                var team = b.teamComponent ? b.teamComponent.teamIndex : TeamIndex.None;
                if (team == TeamIndex.Player || team == TeamIndex.None) continue;

                ulong id = Id(b);
                if (creatureCount == creatures.Length) Array.Resize(ref creatures, creatures.Length * 2);
                int start = PartCount;
                partStart = start;
                partShift = Lead(id, b);
                var group = b.hurtBoxGroup;
                if (group && group.hurtBoxes != null)
                    foreach (var hb in group.hurtBoxes)
                    {
                        if (PartCount - start >= MaxPartsPerCreature) break;
                        if (!hb || !hb.gameObject.activeInHierarchy) continue;
                        var col = hb.collider;
                        if (!col || !col.enabled) continue;
                        // Spheres that sit on the collider's real shape (HurtParts).
                        HurtParts.For(col, partAdder ?? (partAdder = AddPartLimited));
                    }
                if (PartCount == start)
                    AddPart(b.corePosition, Mathf.Max(0.3f, b.bestFitRadius));

                bodies[id] = b;
                if (attackWatched.Add(b)) b.onSkillActivatedServer += EnemyAttacked;
                creatureIndex[id] = creatureCount;
                creatures[creatureCount++] = new Native.Creature
                {
                    id = id,
                    pos = UnityHost.ToSim(b.footPosition + partShift),
                    yaw = UnityHost.Yaw(b.transform.forward),
                    alive = 1,
                    flying = (byte)(b.isFlying ? 1 : 0),
                    parts_start = (uint)start,
                    parts_len = (uint)(PartCount - start),
                };
            }
        }

        // Adds a sphere unless this creature already has the most the sim is given.
        void AddPartLimited(Vector3 centre, float radius)
        {
            if (PartCount - partStart < MaxPartsPerCreature) AddPart(centre, radius);
        }

        int partStart;
        HurtParts.Add partAdder;
        // How far this enemy's parts are moved ahead for the sim (Lead).
        Vector3 partShift;

        // The sim is drawn one tick behind real time but an enemy's model is not, so a fast enemy runs away from the
        // Pikmin latched to it: hand the sim where it will be a tick on (by its speed, Combat.LatchLead).
        readonly Dictionary<ulong, (Vector3 pos, float time)> lastCore = new Dictionary<ulong, (Vector3, float)>();

        Vector3 Lead(ulong id, CharacterBody b)
        {
            var core = b.corePosition;
            var shift = Vector3.zero;
            float lead = Plugin.LatchLead.Value;
            if (lead > 0f && lastCore.TryGetValue(id, out var last) && Time.fixedTime > last.time)
            {
                var v = (core - last.pos) / (Time.fixedTime - last.time);
                // A teleport or a snap isn't a speed.
                if (v.sqrMagnitude < 40f * 40f) shift = v * (lead / Mathf.Max(1u, tickHz));
            }
            lastCore[id] = (core, Time.fixedTime);
            return shift;
        }

        void AddPart(Vector3 centre, float radius)
        {
            if (PartCount == parts.Length) Array.Resize(ref parts, parts.Length * 2);
            parts[PartCount++] = new Native.Part
            {
                centre = UnityHost.ToSim(centre + partShift),
                radius = radius / UnityHost.Scale,
                stickable = 1,
            };
        }

        static ulong Id(CharacterBody b) => unchecked((ulong)(uint)b.GetInstanceID());

        uint[] pikiGenerations = Array.Empty<uint>();
        Func<Native.Piki[], uint, uint> readPikis;
        Func<Native.Pellet[], uint, uint> readPellets;
        Func<Native.Sprout[], uint, uint> readSprouts;
        Func<Native.Onion[], uint, uint> readOnions;

        void ReadBack()
        {
            PikiCount = Read(ref Pikis, readPikis ??= (a, n) => Native.pik_sim_pikis(sim, a, n));
            if (pikiGenerations.Length < Pikis.Length) Array.Resize(ref pikiGenerations, Pikis.Length);
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (pikiGenerations[i] == p.generation) continue;
                pikiGenerations[i] = p.generation;
                affixes.Forget(p.id);
                thrownAt.Remove(p.id);
                if (p.id < procReadyAt.Length) procReadyAt[p.id] = 0f;
            }
            PelletCount = Read(ref Pellets, readPellets ??= (a, n) => Native.pik_sim_pellets(sim, a, n));
            ReadOnions();
            SproutCount = Read(ref Sprouts, readSprouts ??= (a, n) => Native.pik_sim_sprouts(sim, a, n));
        }

        void ReadOnions() => OnionCount = Read(ref Onions, readOnions ??= (a, n) => Native.pik_sim_onions(sim, a, n));

        /// Fills `buf` (growing it to fit) and returns the count.
        static int Read<T>(ref T[] buf, Func<T[], uint, uint> fetch)
        {
            uint total = fetch(buf, (uint)buf.Length);
            if (total > buf.Length)
            {
                buf = new T[Mathf.NextPowerOfTwo((int)total)];
                total = fetch(buf, (uint)buf.Length);
            }
            return (int)Math.Min(total, (uint)buf.Length);
        }

        // RoR2 maps have voids; a Pikmin that falls well below every captain is gone.
        void KillFallen()
        {
            float lowest = float.MaxValue;
            foreach (var d in driverOf.Values)
                if (d && d.Body) lowest = Mathf.Min(lowest, d.Body.footPosition.y);
            if (lowest == float.MaxValue) return;
            float floor = (lowest - FallKillMetres) / UnityHost.Scale;
            for (int i = 0; i < PikiCount; i++)
                if (Pikis[i].alive != 0 && Pikis[i].pos.y < floor)
                    Native.pik_sim_kill_piki(sim, Pikis[i].id);
        }

        void DrainEvents()
        {
            uint n;
            do
            {
                n = Native.pik_sim_events(sim, events, (uint)events.Length);
                for (int i = 0; i < n; i++)
                {
                    var e = events[i];
                    if (e.kind == Native.EventKind.ShipPartCollected) ShipRepairSession.Instance?.Receipt(this, e);
                    if (e.kind == Native.EventKind.PelletDelivered && onionMinions.TryGetValue(e.b, out var onion)
                        && onion.Alive && !OnionDisabled(e.b))
                    {
                        var hp = onion.Body.healthComponent;
                        float fraction = Mathf.Clamp01(Plugin.OnionPelletHeal.Value + 0.02f * Mathf.Max(0f, e.value));
                        hp.Heal(hp.fullHealth * fraction, default(ProcChainMask), true);
                    }
                    int captain = EventCaptain(e);
                    if (captain >= 0 && driverOf.TryGetValue(captain, out var d) && d && d.Body)
                        d.Items.OnEvent(e, this);
                    if (Plugin.LogEvents.Value) LogEvent(e);
                    if (Plugin.LogLatches.Value && e.kind == Native.EventKind.Latched) AuditLatch(e);
                }
            } while (n == events.Length);
        }

        static readonly string[] ColourNames = { "Blue", "Red", "Yellow" };

        static string Colour(int c) => c >= 0 && c < 3 ? ColourNames[c] : "?";

        // Where each thrown Pikmin was aimed (the captain's cursor when it left his hand), to log
        // throws that land well away from it.
        readonly Dictionary<uint, Vector3> thrownAt = new Dictionary<uint, Vector3>();

        // A thrown Pikmin's distance from where it was aimed, flat, in metres; -1 if unknown.
        float OffAim(uint piki, out Vector3 aim, out Vector3 at)
        {
            at = Vector3.zero;
            if (!thrownAt.TryGetValue(piki, out aim)) return -1f;
            thrownAt.Remove(piki);
            for (int i = 0; i < PikiCount; i++)
                if (Pikis[i].id == piki)
                {
                    at = UnityHost.ToUnity(Pikis[i].pos);
                    var d = at - aim;
                    d.y = 0f;
                    return d.magnitude;
                }
            return -1f;
        }

        void LogEvent(in Native.Event e)
        {
            string what;
            switch (e.kind)
            {
                case Native.EventKind.Thrown:
                    if (driverOf.TryGetValue(e.b, out var thrower) && thrower && thrower.HasState)
                        thrownAt[e.a] = UnityHost.ToUnity(thrower.State.cursor_world);
                    return;
                case Native.EventKind.Landed:
                {
                    float off = OffAim(e.a, out var aim, out var at);
                    if (off < 1.5f) return;
                    what = $"Pikmin {e.a} landed {off:F1} m from the cursor (aimed {aim}, landed {at})";
                    break;
                }
                case Native.EventKind.Latched:
                {
                    float off = OffAim(e.a, out _, out _);
                    what = $"Pikmin {e.a} latched onto {Name(e)}" + (off >= 0f ? $", {off:F1} m from the cursor" : "");
                    break;
                }
                case Native.EventKind.KnockedOff: what = $"Pikmin {e.a} shaken off {Name(e)}"; break;
                case Native.EventKind.PikiDied: what = $"Pikmin {e.a} died ({(new[] { "attacked", "crushed", "eaten", "killed" })[Mathf.Clamp(e.c, 0, 3)]})"; break;
                case Native.EventKind.Swallowed: what = $"Pikmin {e.a} swallowed by {Name(e)}"; break;
                case Native.EventKind.PelletLifted: what = $"corpse {e.a} lifted, heading for Onion {e.b}"; break;
                case Native.EventKind.PelletDropped: what = $"corpse {e.a} dropped"; break;
                case Native.EventKind.PelletDelivered: what = $"corpse {e.a} delivered: {e.value} {Colour(e.c)} seeds"; break;
                case Native.EventKind.Plucked: what = $"captain {e.b} plucked Pikmin {e.a}"; break;
                case Native.EventKind.Bloomed: what = $"Pikmin {e.a} flowered"; break;
                case Native.EventKind.PikiGrew: what = $"Pikmin {e.a} grew to a {(e.c == 2 ? "flower" : "bud")} from kills"; break;
                case Native.EventKind.SproutGrew: what = $"sprout {e.a} grew to a {(e.c == 2 ? "flower" : "bud")}"; break;
                case Native.EventKind.CaptainDown: what = $"captain {e.a} is down"; break;
                case Native.EventKind.Dismissed: what = $"captain {e.a} dismissed his squad"; break;
                case Native.EventKind.StrikeStarted: what = $"captain {e.a} struck"; break;
                case Native.EventKind.EmergencyPikmin: what = $"Onion {e.b} sent captain {e.a} {e.c} emergency Pikmin"; break;
                case Native.EventKind.PluckerOut: what = $"Pikmin {e.a} came out of Onion {e.b} to pluck forgotten sprouts"; break;
                case Native.EventKind.PluckerPulled: what = $"the Onion's plucker {e.c} plucked Pikmin {e.a}" + (e.b >= 0 ? $" for captain {e.b}" : ""); break;
                case Native.EventKind.CaptainStomped: what = $"captain {e.a} stomped from {e.value * UnityHost.Scale:F1} m"; break;
                default: return;
            }
            Plugin.Log.LogInfo(what);
        }

        // Where a Pikmin that just latched is against the enemy's real hurtboxes, to find latches in the air.
        void AuditLatch(in Native.Event e)
        {
            if (e.has_creature == 0 || !bodies.TryGetValue(e.creature, out var body) || !body) return;
            int pi = -1;
            for (int i = 0; i < PikiCount; i++)
                if (Pikis[i].id == e.a) { pi = i; break; }
            if (pi < 0) return;
            var at = UnityHost.ToUnity(Pikis[pi].pos);
            float best = float.MaxValue;
            Collider nearest = null;
            var group = body.hurtBoxGroup;
            if (group && group.hurtBoxes != null)
                foreach (var hb in group.hurtBoxes)
                {
                    var col = hb ? hb.collider : null;
                    if (!col || !col.enabled) continue;
                    float d = Vector3.Distance(col.ClosestPoint(at), at);
                    if (d < best) { best = d; nearest = col; }
                }
            if (!nearest) return;
            var ext = nearest.bounds.extents;
            Plugin.Log.LogInfo($"Latch audit: Pikmin {e.a} on {body.GetDisplayName()} is {best:F2} m from its nearest hurtbox " +
                               $"({nearest.GetType().Name} '{nearest.name}', box extents {ext.x:F2}/{ext.y:F2}/{ext.z:F2})");
        }

        string Name(in Native.Event e) =>
            e.has_creature != 0 && bodies.TryGetValue(e.creature, out var b) && b ? b.GetDisplayName() : "something";

        // ------------------------------------------------------------------------------------
        // RoR2 → sim

        static byte Damage(IntPtr user, ulong creature, float amount, int part, uint source, uint sourceId)
        {
            try
            {
                return Instance ? Instance.Hit(creature, amount, part, source, sourceId) : (byte)0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Pikmin hit: {e}");
                return 0;
            }
        }

        byte Hit(ulong creature, float amount, int part, uint source, uint sourceId)
        {
            if (!NetworkServer.active) return 0;
            if (!bodies.TryGetValue(creature, out var victim) || !victim) return 0;
            var health = victim.healthComponent;
            if (!health || !health.alive) return 0;

            var attacker = AttackerFor(source, sourceId);
            NativeBody.MarkCombat(attacker);
            var at = victim.corePosition;
            if (part >= 0 && creatureIndex.TryGetValue(creature, out int ci))
            {
                var c = creatures[ci];
                if (part < c.parts_len) at = UnityHost.ToUnity(parts[c.parts_start + part].centre);
            }
            bool piki = source == Native.SourcePiki && sourceId < PikiCount;
            bool strike = source == Native.SourceStrike;
            // Each Pikmin procs at most once per Items.ProcInterval, on its own clock.
            float proc = Plugin.ProcCoefficient.Value;
            if (piki && !ProcReady(sourceId)) proc = 0f;
            // The strike staggers: RoR2's one-second stun (bosses shrug it off).
            DamageTypeCombo type = strike && Plugin.StrikeStun.Value ? DamageType.Stun1s : DamageType.Generic;
            // Which skill it came from, for the items that only answer skill hits (Breaching Fin,
            // Luminous Shot, Infernal Mark, Nightmare Demon, Vile Censer): Pikmin and his punch
            // are his primary (the throw), the strike his special.
            type.damageSource = strike ? DamageSource.Special : DamageSource.Primary;
            var spec = new NativeHitSpec
            {
                coefficient = amount * Plugin.DamageCoefficient.Value,
                position = at,
                procCoefficient = proc,
                damageType = type,
            };
            // A Pikmin's hit is Olimar's, measured from the Pikmin (Focus Crystal's range);
            // his own punch is his.
            bool landed = piki
                ? MinionHit.Deal(attacker, MinionOf(Pikis[sourceId]), health, spec)
                : NativeHit.Deal(attacker, health, spec);
            if (landed && piki && proc > 0f) affixes.OnHit(sourceId, attacker, health, proc);
            // A kill with the strike gives it back.
            if (landed && strike && !health.alive && Plugin.StrikeKillReset.Value
                && driverOf.TryGetValue((int)sourceId, out var striker) && striker)
                striker.Items.StrikeKilled();
            return landed ? (byte)1 : (byte)0;
        }

        static MinionState MinionOf(in Native.Piki p) => new MinionState
        {
            position = UnityHost.ToUnity(p.pos),
            moving = p.vel.x * p.vel.x + p.vel.z * p.vel.z > 1f,
            grounded = p.on_ground != 0,
            healthFraction = Mathf.Clamp01(p.health / 100f),
        };

        bool ProcReady(uint piki)
        {
            if (piki >= procReadyAt.Length)
                Array.Resize(ref procReadyAt, Mathf.NextPowerOfTwo((int)piki + 1));
            float now = Time.fixedTime;
            if (now < procReadyAt[piki]) return false;
            procReadyAt[piki] = now + Mathf.Max(0f, Plugin.ProcInterval.Value);
            return true;
        }

        /// The Olimar a Pikmin's (or captain's) hit counts as coming from.
        CharacterBody AttackerFor(uint source, uint id)
        {
            int captain = (int)id;
            if (source == Native.SourcePiki)
                captain = id < PikiCount ? Pikis[id].captain : -1;
            if (captain >= 0 && driverOf.TryGetValue(captain, out var d) && d && d.Body)
                return d.Body;
            foreach (var other in driverOf.Values)
                if (other && other.Body) return other.Body;
            return null;
        }

        float NearestOlimarHealth(Vector3 at)
        {
            float best = float.MaxValue, health = 110f;
            foreach (var d in driverOf.Values)
            {
                if (!d || !d.Body) continue;
                float dist = (d.Body.corePosition - at).sqrMagnitude;
                if (dist < best)
                {
                    best = dist;
                    health = d.Body.maxHealth;
                }
            }
            return health;
        }

        // An enemy's hit on anything also hurts the Pikmin around where it landed.
        void OnDamageDealt(DamageReport r)
        {
            if (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal) return;
            if (sim == IntPtr.Zero || !NetworkServer.active || r?.damageInfo == null) return;
            if (stepping)
            {
                afterStep.Add(() => OnDamageDealt(r));
                return;
            }
            if (r.attackerTeamIndex == TeamIndex.Player || r.attackerTeamIndex == TeamIndex.None) return;
            // A damage-over-time tick is the victim's own debuff, not a blow landing near Pikmin.
            if ((r.damageInfo.damageType & DamageType.DoT) != 0) return;
            var at = r.damageInfo.position;
            if (at == Vector3.zero) return;
            if ((r.damageInfo.damageType & DamageType.Freeze2s) != 0)
                Freeze(at, Plugin.PikminHurtRadius.Value, r.damageInfo.procCoefficient);
            var centre = UnityHost.ToSim(at);
            // A Pikmin has PikminHealth x the nearest Olimar's max health; the sim counts its
            // health out of 100 (piki_max_health's code default).
            float full = Mathf.Max(1f, Plugin.PikminHealth.Value * NearestOlimarHealth(at));
            Native.pik_sim_attack_pikis_in_sphere(sim, ref centre,
                Plugin.PikminHurtRadius.Value / UnityHost.Scale, r.damageDealt / full * 100f);
        }

        // A freezing hit or blast from an enemy (Freeze2s: a Glacial elite's death burst) freezes
        // the Pikmin within `radiusMetres` of `at` as RoR2 freezes bodies: two seconds times the
        // proc coefficient.
        void Freeze(Vector3 at, float radiusMetres, float procCoefficient)
        {
            if (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal) return;
            if (sim == IntPtr.Zero || !NetworkServer.active) return;
            if (stepping)
            {
                afterStep.Add(() => Freeze(at, radiusMetres, procCoefficient));
                return;
            }
            var centre = UnityHost.ToSim(at);
            float seconds = 2f * (procCoefficient > 0f ? procCoefficient : 1f);
            uint n = Native.pik_sim_freeze_pikis_in_sphere(sim, ref centre, radiusMetres / UnityHost.Scale, seconds);
            if (n > 0 && Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"{n} Pikmin frozen for {seconds:0.#} s");
        }

        static Hook blastHook;

        // RoR2 freezes only bodies, so its freezing blasts (BlastAttack.Fire with Freeze2s from a
        // non-player team) are watched here to freeze the Pikmin inside them too.
        static void HookBlasts()
        {
            if (blastHook != null) return;
            try
            {
                var fire = typeof(BlastAttack).GetMethod("Fire", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                blastHook = new Hook(fire, (Func<Func<BlastAttack, BlastAttack.Result>, BlastAttack, BlastAttack.Result>)BlastFired);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Freezing blasts: hook failed ({e.GetType().Name}: {e.Message}); Pikmin freeze only from direct hits");
            }
        }

        static BlastAttack.Result BlastFired(Func<BlastAttack, BlastAttack.Result> orig, BlastAttack self)
        {
            var result = orig(self);
            try
            {
                if (Instance && self != null && (self.damageType & DamageType.Freeze2s) != 0
                    && self.teamIndex != TeamIndex.Player && self.teamIndex != TeamIndex.None)
                    Instance.Freeze(self.position, self.radius, self.procCoefficient);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Freezing blast: {e}");
            }
            return result;
        }

        // Enemies whose attacks are watched (EnemyAttacked).
        readonly HashSet<CharacterBody> attackWatched = new HashSet<CharacterBody>();
        static readonly Predicate<CharacterBody> Gone = b => !b;

        // An enemy starting an attack shakes off its Pikmin (Pikmin 1's flick, InteractFlick).
        // RoR2's enemies have no shake-off move, so any combat skill they use counts. Pikmin
        // latched for less than Pikmin.ShakeProtection hold on (the sim checks each one's
        // latch time); the next attack after that can take them.
        void EnemyAttacked(GenericSkill skill)
        {
            if (sim == IntPtr.Zero || !NetworkServer.active || !skill || !skill.isCombatSkill) return;
            if (stepping)
            {
                afterStep.Add(() => EnemyAttacked(skill));
                return;
            }
            var b = skill.characterBody;
            if (!b) return;
            ulong id = Id(b);
            int on = 0;
            for (int i = 0; i < PikiCount; i++)
                if (Pikis[i].state == Native.PikiState.Attack && Pikis[i].has_creature != 0 && Pikis[i].creature == id)
                    on++;
            if (on == 0) return;
            if (Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"{b.GetDisplayName()} attacks ({(skill.skillDef ? skill.skillDef.skillName : "?")}) with {on} Pikmin fighting it");
            Native.pik_sim_shake_off(sim, id);
        }

        // An Onion goes dormant, from damage (it revives on its own timer) or because its owner died (it
        // waits for him). Its body is removed, so nothing on the enemy side can see or target it until it
        // has fully recovered.
        void GoDormant(OnionFlight f, float seconds, bool ownerDied)
        {
            f.dormant = true;
            f.ownerDeath = ownerDied;
            f.recallUntil = 0f;
            f.dormantAge = f.reviveAge = 0f;
            f.reviving = false;
            f.following = f.airborne = false;
            f.pickup = -1;
            f.transitionDuration = 0f;
            f.position = UnityHost.Snap(f.position);
            var ground = UnityHost.ToSim(f.position);
            Native.pik_sim_make_onion_dormant(sim, (uint)f.id, seconds, ref ground);
            if (onionMinions.TryGetValue(f.id, out var minion))
            {
                minion.Release();
                onionMinions.Remove(f.id);
            }
            ForgetOnionBody(f.id);
        }

        void ForgetOnionBody(int onion)
        {
            var gone = new List<CharacterBody>();
            foreach (var kv in onionBodyIds)
                if (kv.Value == onion || !kv.Key) gone.Add(kv.Key);
            foreach (var key in gone) onionBodyIds.Remove(key);
        }

        static bool OwnerAlive(OnionFlight f) =>
            f.owner && f.owner.GetBody() is CharacterBody b && b.healthComponent && b.healthComponent.alive;

        // Olimar died with a friend still alive. His Onion cannot go on without him: if the friend is another
        // Olimar the orphaned Pikmin try to join his squad and the Onion sleeps at once; otherwise the Onion
        // lands where it is, the Pikmin walk home to it, and it sleeps when they have had their time (those
        // that did not make it stand about, and the enemies see to them).
        const float RecallSeconds = 20f;

        void OnOlimarDied(OlimarDriver dead)
        {
            if (!onionOf.TryGetValue(dead.Captain, out var f) || f.dormant) return;
            bool friendAlive = false;
            var bodies = CharacterBody.readOnlyInstancesList;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] && bodies[i].isPlayerControlled && bodies[i] != dead.Body && bodies[i].healthComponent && bodies[i].healthComponent.alive)
                    friendAlive = true;
            if (!friendAlive) return; // the run is over
            OlimarDriver heir = null;
            foreach (var d in driverOf.Values)
                if (d && d != dead && d.Captain >= 0 && d.Body && d.Body.isPlayerControlled && d.Body.healthComponent && d.Body.healthComponent.alive && d.Active)
                { heir = d; break; }
            if (heir != null)
            {
                uint joined = Native.pik_sim_adopt_orphans(sim, (uint)dead.Captain, (uint)heir.Captain);
                Plugin.Log.LogInfo($"Olimar {dead.Captain} died: {joined} Pikmin join Olimar {heir.Captain}; Onion {f.id} goes dormant.");
                GoDormant(f, 99999f, true);
                return;
            }
            Plugin.Log.LogInfo($"Olimar {dead.Captain} died: his Onion {f.id} lands and calls his Pikmin home before it goes dormant.");
            bool wasUp = f.airborne || f.following;
            f.following = false;
            f.recallUntil = Time.fixedTime + RecallSeconds;
            f.recallNext = 0f;
            if (wasUp) LandOnionNow(f);
        }

        void LandOnionNow(OnionFlight f)
        {
            var ground = Physics.Raycast(f.position + Vector3.up * 2f, Vector3.down, out var hit, 60f, UnityHost.Mask,
                QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.5f ? hit.point : UnityHost.Snap(f.position);
            f.landing = ground;
            f.transitionStart = f.position;
            f.transitionElapsed = 0f;
            f.transitionDuration = 2.5f;
            f.following = false;
            f.airborne = true;
            WriteFlight(f);
        }

        // Each physics step while a dead owner's Onion waits for his Pikmin: once it is down, send the ones
        // outside (every few seconds, for stragglers); at the end of the time, or when none are left, sleep.
        void RecallToOnion(OnionFlight f)
        {
            if (f.dormant) { f.recallUntil = 0f; return; }
            if (!f.airborne && Time.fixedTime >= f.recallNext)
            {
                f.recallNext = Time.fixedTime + 2f;
                Native.pik_sim_send_orphans_home(sim, (uint)f.captain, (uint)f.id);
            }
            float started = f.recallUntil - RecallSeconds;
            bool none = !f.airborne && Time.fixedTime >= started + 3f && Native.pik_sim_captain_piki_count(sim, (uint)f.captain) == 0;
            if (Time.fixedTime >= f.recallUntil || none) GoDormant(f, 99999f, true);
        }

        // A dead enemy leaves a corpse for the Pikmin to carry home (BTeki::dieSoon →
        // becomePellet): a pellet in the sim, drawn as the enemy's own body (Corpses).
        void OnDeath(DamageReport r)
        {
            if (sim == IntPtr.Zero || !NetworkServer.active || r == null) return;
            if (stepping)
            {
                afterStep.Add(() => OnDeath(r));
                return;
            }
            var v = r.victimBody;
            foreach (var f in onionOf.Values)
                if (v && onionBodyIds.TryGetValue(v, out int onionId) && onionId == f.id
                    && !f.dormant && onionMinions.TryGetValue(f.id, out _))
                {
                    GoDormant(f, Mathf.Max(0f, Plugin.OnionReviveSeconds.Value), false);
                    Plugin.Log.LogInfo($"Onion {f.id} became dormant; revival starts in {Plugin.OnionReviveSeconds.Value:0.#} seconds.");
                    return;
                }
            if (v && v.GetComponent<OlimarDriver>() is OlimarDriver olimar && olimar.Captain >= 0)
            {
                olimar.PlayDeathSound();
                // His knockdown (ODead) in place of RoR2's ragdoll, which is on his hidden
                // Commando model; RoR2's own death state still runs.
                Native.pik_sim_down_captain(sim, (uint)olimar.Captain, 1);
                stepUntil = Time.fixedTime + 3f;
                OnOlimarDied(olimar);
                return;
            }
            if (!v || r.victimTeamIndex == TeamIndex.Player) return;
            if (ShipRepairMode.Active && ShipRepairSession.Instance) ShipRepairSession.Instance.EnemyDefeated(this, r);
            if (v.isElite) affixes.EliteDied(v, r.attackerBody, Pikis, PikiCount, Id(v));
            // The Pikmin that fought it earn the kill (growth), whoever struck the last blow.
            Native.pik_sim_credit_kill(sim, Id(v));
            // A Pikmin enemy leaves the disc's own corpse (TekiBodies).
            if (v.GetComponent<TekiBody>() is TekiBody teki && TekiBodies.Died(sim, v, teki)) return;
            int lying = 0;
            for (int i = 0; i < PelletCount; i++)
                if (Pellets[i].state != 3) lying++;
            if (lying >= Plugin.MaxPellets.Value) return;

            float r_m = Mathf.Max(0.3f, v.bestFitRadius);
            int weight = Mathf.Clamp(Mathf.RoundToInt(r_m * Plugin.CarriersPerMetre.Value), 1, 20);
            var at = UnityHost.ToSim(UnityHost.Snap(v.footPosition));
            float yaw = UnityHost.Yaw(v.transform.forward);
            if (!Plugin.CarryBodies.Value)
            {
                // A numbered pellet, as Pikmin 1 drops: 1, 5, 10 or 20 by the enemy's size, in a
                // random colour, carried and sized as on the disc.
                int colour = UnityEngine.Random.Range(0, 3);
                int pellet = Native.pik_sim_spawn_numbered_pellet(sim, (uint)weight, (uint)colour, ref at, yaw);
                if (pellet >= 0) Native.pik_sim_recruit_to_pellet(sim, (uint)pellet, Id(v), 8f / UnityHost.Scale);
                if (Plugin.LogEvents.Value && pellet >= 0)
                {
                    int n = weight >= 20 ? 20 : weight >= 10 ? 10 : weight >= 5 ? 5 : 1;
                    Plugin.Log.LogInfo($"{v.GetDisplayName()} left a {Colour(colour)} {n} pellet ({pellet})");
                }
                return;
            }
            float radius = Mathf.Clamp(r_m / UnityHost.Scale, 8f, 150f);
            var desc = new Native.PelletDesc
            {
                pos = at,
                yaw = yaw,
                radius = radius,
                height = radius * 0.5f,
                min_carriers = (uint)weight,
                max_carriers = (uint)(weight * 2),
                seeds_matching = Mathf.Max(1, Mathf.RoundToInt(weight * 1.5f)),
                seeds_other = Mathf.Max(1, weight / 2),
                color = -1,
            };
            int id = Native.pik_sim_spawn_pellet(sim, ref desc);
            if (id >= 0) Native.pik_sim_recruit_to_pellet(sim, (uint)id, Id(v), 8f / UnityHost.Scale);
            if (id >= 0) Corpses.Claim((uint)id, v);
            if (Plugin.LogEvents.Value && id >= 0)
                Plugin.Log.LogInfo($"{v.GetDisplayName()} left corpse {id} (needs {weight} Pikmin)");
        }
    }
}
