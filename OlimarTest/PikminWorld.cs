using System;
using System.Collections.Generic;
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
    //   behind, as at sunset in Pikmin).
    internal class PikminWorld : MonoBehaviour
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

        // Captains: one per Olimar's CharacterMaster, so a respawned body keeps his Pikmin.
        readonly Dictionary<CharacterMaster, int> captainOf = new Dictionary<CharacterMaster, int>();
        readonly Dictionary<int, OlimarDriver> driverOf = new Dictionary<int, OlimarDriver>();
        readonly List<int> onionIds = new List<int>();
        // Each Onion's RoR2 interactable (the Onion menu's prompt).
        readonly Dictionary<int, InteractTarget> onionTargets = new Dictionary<int, InteractTarget>();
        // Each Onion's RoR2 body, as its Olimar's minion: his items, its own position.
        readonly Dictionary<int, Minion> onionMinions = new Dictionary<int, Minion>();

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
        }
        readonly Dictionary<CharacterMaster, Cargo> cargo = new Dictionary<CharacterMaster, Cargo>();
        sealed class DolphinFlight
        {
            public int id, captain;
            public Vector3 start, landing;
            public float elapsed;
            public bool complete, bringSquad;
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

        // RoR2 events raised while the sim is stepping (a Pikmin hit kills an enemy, whose
        // death would spawn a pellet) wait here: calling back into the sim mid-step isn't
        // allowed.
        bool stepping;
        readonly List<Action> afterStep = new List<Action>();

        public bool Running => sim != IntPtr.Zero;

        internal IntPtr SimHandle => sim;

        /// Running and not stopped by a bug in the sim.
        public bool Healthy => sim != IntPtr.Zero && !reportedPoison;

        /// How far the clock is between the last tick and the next, for drawing.
        public float RenderAlpha =>
            Mathf.Clamp01(alphaAtFixed + (Time.time - Time.fixedTime) * tickHz);

        public static void Hook()
        {
            Run.onRunDestroyGlobal += _ =>
            {
                if (Instance) Destroy(Instance.gameObject);
            };
            GlobalEventManager.onCharacterDeathGlobal += r =>
            {
                if (Instance) Instance.OnDeath(r);
            };
            GlobalEventManager.onServerDamageDealt += r =>
            {
                if (Instance) Instance.OnDamageDealt(r);
            };
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

        void Awake()
        {
            host = UnityHost.Table(damageFn);
            tickHz = Native.pik_tick_hz();
        }

        void OnDestroy()
        {
            FreeSim();
            if (Instance == this) Instance = null;
        }

        void FreeSim()
        {
            if (sim != IntPtr.Zero)
            {
                Native.pik_sim_free(sim);
                sim = IntPtr.Zero;
            }
            captainOf.Clear();
            driverOf.Clear();
            onionIds.Clear();
            onionOf.Clear();
            dolphins.Clear();
            foreach (var t in onionTargets.Values)
                if (t) Destroy(t.gameObject);
            onionTargets.Clear();
            foreach (var m in onionMinions.Values) m.Release();
            onionMinions.Clear();
            bodies.Clear();
            creatureIndex.Clear();
            foreach (var b in attackWatched)
                if (b) b.onSkillActivatedServer -= EnemyAttacked;
            attackWatched.Clear();
            thrownAt.Clear();
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
                SpawnDolphin(known, d, false);
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
            PlaceOnions(id, d);
            SpawnDolphin(id, d);
            Plugin.Log.LogInfo($"captain {id} is {d.Body.GetDisplayName()} at {d.Body.footPosition}");
            return id;
        }

        public void Unregister(OlimarDriver d)
        {
            foreach (var kv in driverOf)
                if (kv.Value == d)
                {
                    driverOf.Remove(kv.Key);
                    break;
                }
        }

        bool StartSim()
        {
            sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), (ulong)DateTime.Now.Ticks);
            if (sim == IntPtr.Zero)
            {
                Plugin.Log.LogError("libpikmin's sim didn't start");
                return false;
            }
            simStage = Stage.instance;
            reportedPoison = false;
            Native.pik_sim_set_auto_rejoin(sim, Plugin.AutoRejoin.Value ? (byte)1 : (byte)0);
            Native.pik_sim_set_shake_protection(sim, Plugin.ShakeProtection.Value);
            Plugin.Log.LogInfo($"Pikmin sim started ({Native.Utf8(Native.pik_sim_params_source(sim))})");
            view.StartModels();
            return true;
        }

        // A new stage: note who's coming along, then start over. The next Olimar to register
        // starts the new stage's sim.
        void NewStage()
        {
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
            var flight = new OnionFlight { id = id, captain = captain, owner = owner,
                position = d.Body.footPosition + Vector3.up * 5f };
            onionOf[captain] = flight;
            WriteFlight(flight);
        }

        // The ship is a presentation/carry goal only; no parts or repair objective spawn.
        void SpawnDolphin(int captain, OlimarDriver driver, bool bringSquad = true)
        {
            var body = driver.Body;
            var back = -Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized;
            if (back.sqrMagnitude < 0.5f) back = Vector3.back;
            var landing = body.footPosition;
            if (Physics.Raycast(landing + back * 4f + Vector3.up * 2f, Vector3.down, out var floor,
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
            dolphins[captain] = new DolphinFlight { id = id, captain = captain, start = start, landing = landing, bringSquad = bringSquad };
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

        void MoveDolphins()
        {
            foreach (var ship in dolphins.Values)
            {
                if (ship.complete || !driverOf.TryGetValue(ship.captain, out var driver) || !driver) continue;
                ship.elapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(ship.elapsed / 3.5f);
                // Decelerate into a short landing, then release Olimar beside the ship.
                float eased = 1f - (1f - t) * (1f - t);
                var at = Vector3.Lerp(ship.start, ship.landing, eased);
                var pos = UnityHost.ToSim(at);
                Native.pik_sim_set_onion_pose(sim, (uint)ship.id, ref pos, t < 1f ? (byte)1 : (byte)0);
                if (t < 1f) Native.pik_sim_set_onion_flight_motion(sim, (uint)ship.id, 1, t);
                if (onionOf.TryGetValue(ship.captain, out var onion))
                {
                    onion.position = at + Vector3.right * 3f + Vector3.up * 4f;
                    WriteFlight(onion);
                }
                if (t >= 1f || !driver.Arriving) FinishArrival(ship, driver);
            }
        }

        void FinishArrival(DolphinFlight ship, OlimarDriver driver)
        {
            var pos = UnityHost.ToSim(ship.landing);
            Native.pik_sim_set_onion_pose(sim, (uint)ship.id, ref pos, 0);
            ship.complete = true;
            driver.Arriving = false;
            if (ship.bringSquad) BringPikmin(ship.captain, driver);
            Plugin.Log.LogInfo($"Dolphin landed for captain {ship.captain}; Onion following.");
        }

        public bool CaptainArriving(uint captain) => driverOf.TryGetValue((int)captain, out var d) && d && d.Arriving;

        void WriteFlight(OnionFlight flight)
        {
            var pos = UnityHost.ToSim(flight.position);
            Native.pik_sim_set_onion_pose(sim, (uint)flight.id, ref pos, flight.airborne ? (byte)1 : (byte)0);
            if (flight.airborne && flight.transitionDuration > 0f)
                Native.pik_sim_set_onion_flight_motion(sim, (uint)flight.id, flight.following ? (byte)0 : (byte)1,
                    Mathf.Clamp01(flight.transitionElapsed / flight.transitionDuration));
            if (onionTargets.TryGetValue(flight.id, out var target) && target)
            {
                target.context = flight.following ? "Land Onion Here" : flight.airborne ? "Onion landing" : "Open the Onion";
                target.transform.position = flight.airborne && driverOf.TryGetValue(flight.captain, out var d) && d
                    ? d.Body.footPosition : flight.position;
            }
            if (onionMinions.TryGetValue(flight.id, out var minion))
                minion.Place(flight.position, Quaternion.identity, flight.airborne, !flight.airborne);
        }

        void MoveOnions()
        {
            foreach (var flight in onionOf.Values)
            {
                if (dolphins.TryGetValue(flight.captain, out var ship) && !ship.complete) continue;
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

        public bool OnionFlying(int captain) => onionOf.TryGetValue(captain, out var f) && f.airborne;
        public uint OnionColour(int onion)
        {
            foreach (var f in onionOf.Values)
                if (f.id == onion && cargo.TryGetValue(f.owner, out var saved)) return saved.colour;
            return Native.Red;
        }
        public bool CommandOnion(int captain, bool follow)
        {
            if (!onionOf.TryGetValue(captain, out var f) || !driverOf.TryGetValue(captain, out var d) || !d) return false;
            if (follow == f.following && (follow || f.airborne)) return true;
            if (!follow)
            {
                // Reject placement over a void instead of dropping the Onion out of the stage.
                if (!Physics.Raycast(d.Body.footPosition + Vector3.up * 2f, Vector3.down, out var hit,
                    30f, UnityHost.Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.5f) return false;
                f.landing = hit.point;
            }
            f.transitionStart = f.position;
            f.transitionElapsed = 0f;
            f.transitionDuration = follow ? 1.8f : 2.5f;
            f.following = follow;
            f.airborne = true;
            WriteFlight(f);
            return true;
        }

        void AddOnion(Vector3 at, uint colours, CharacterBody owner)
        {
            var p = UnityHost.ToSim(UnityHost.Snap(at));
            int id = Native.pik_sim_add_onion(sim, ref p, colours);
            if (id < 0) return;
            onionIds.Add(id);
            // Standing under it shows RoR2's prompt; the button opens the Onion menu
            // (NaviContainerState, naviState.cpp: the action button within reach of the
            // Onion's 'cont' sphere). Opening it isn't a chest: no on-interact items.
            onionTargets[id] = InteractTarget.Make($"Onion {id} (interact)", transform, UnityHost.ToUnity(p),
                "Open the Onion",
                who => Driver(who) is OlimarDriver d && d.CanOpenOnion(id),
                who => {
                    if (Driver(who) is OlimarDriver d)
                    {
                        if (OnionFlying(d.Captain)) CommandOnion(d.Captain, false);
                        else d.OpenOnionMenu(id);
                    }
                });
            AddOnionMinion(id, UnityHost.ToUnity(p), owner);
        }

        // The Onion's body as the owner's minion, where the sim has the Onion.
        void AddOnionMinion(int id, Vector3 at, CharacterBody owner)
        {
            if (!Plugin.OnionItems.Value) return;
            var m = Minions.Spawn(Content.OnionMinion, owner, at, Quaternion.identity);
            if (m != null) onionMinions[id] = m;
            else Plugin.Log.LogWarning($"Onion {id}'s minion body didn't spawn; it won't get items");
        }

        static OlimarDriver Driver(Interactor who) => who ? who.GetComponent<OlimarDriver>() : null;

        /// Onion `onion`'s interactable, for the interact prompt; null if there's none.
        public InteractTarget OnionTarget(int onion) =>
            onionTargets.TryGetValue(onion, out var t) && t ? t : null;

        // The squad and Onion stock from the last stage, or the configured start of a run.
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
            sim == IntPtr.Zero ? -1 : Native.pik_sim_captain_throw_color(sim, (uint)captain);

        public int OnionInReach(int captain)
        {
            if (sim == IntPtr.Zero || !onionOf.TryGetValue(captain, out var f)
                || !driverOf.TryGetValue(captain, out var d) || !d || !d.Body) return -1;
            return f.following || Vector3.Distance(d.Body.footPosition, f.airborne ? f.landing : f.position)
                < 70f * UnityHost.Scale ? f.id : -1;
        }

        public bool OnionCounts(int onion, int captain, uint colour, out Native.OnionCounts counts)
        {
            counts = default;
            return sim != IntPtr.Zero && Native.pik_sim_onion_counts(sim, (uint)onion, (uint)captain, colour, out counts) != 0;
        }

        public uint CallOut(int captain, int onion, uint colour, uint n) =>
            sim == IntPtr.Zero ? 0 : Native.pik_sim_onion_call_out(sim, (uint)captain, (uint)onion, colour, n);

        public uint PutAway(int captain, int onion, uint colour, uint n) =>
            sim == IntPtr.Zero ? 0 : Native.pik_sim_onion_put_away(sim, (uint)captain, (uint)onion, colour, n);

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
            if (sim == IntPtr.Zero) return;
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
                if (kv.Value && kv.Value.Active)
                {
                    input = kv.Value.BuildInput();
                    anyone = true;
                }
                Native.pik_sim_set_input(sim, (uint)kv.Key, ref input);
                // His move speed items (Goat Hoof, Energy Drink...) speed up Navi's run.
                if (kv.Value && kv.Value.Body)
                    Native.pik_sim_set_captain_speed(sim, (uint)kv.Key, MinionStats.Of(kv.Value.Body).moveSpeed);
                Native.pik_sim_set_captain_reach(sim, (uint)kv.Key, Plugin.ThrowReach.Value);
            }
            // With no Olimar about (all dead, or mid-load) the Pikmin wait.
            if (!anyone) return;

            MoveDolphins();
            MoveOnions();
            GatherCreatures();
            Native.pik_sim_set_creatures(sim, creatures, (uint)creatureCount, parts, (uint)PartCount);
            stepping = true;
            uint ticks;
            try
            {
                ticks = Native.pik_sim_advance(sim, ref host, Time.fixedDeltaTime);
            }
            finally
            {
                stepping = false;
            }
            alphaAtFixed = Native.pik_sim_alpha(sim);
            RunDeferred();
            if (ticks == 0) return;

            ReadBack();
            foreach (var kv in driverOf)
                if (kv.Value && Native.pik_sim_captain(sim, (uint)kv.Key, out var cap) != 0)
                    kv.Value.Ticked(cap);
            KillFallen();
            DrainEvents();
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

        bool AllGone()
        {
            foreach (var d in driverOf.Values)
                if (d) return false;
            return true;
        }

        int PartCount;

        void GatherCreatures()
        {
            creatureCount = 0;
            PartCount = 0;
            bodies.Clear();
            creatureIndex.Clear();
            attackWatched.RemoveWhere(Gone);
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                if (!b || !b.healthComponent || !b.healthComponent.alive) continue;
                var team = b.teamComponent ? b.teamComponent.teamIndex : TeamIndex.None;
                if (team == TeamIndex.Player || team == TeamIndex.None) continue;

                ulong id = Id(b);
                if (creatureCount == creatures.Length) Array.Resize(ref creatures, creatures.Length * 2);
                int start = PartCount;
                var group = b.hurtBoxGroup;
                if (group && group.hurtBoxes != null)
                    foreach (var hb in group.hurtBoxes)
                    {
                        if (PartCount - start >= MaxPartsPerCreature) break;
                        if (!hb || !hb.gameObject.activeInHierarchy) continue;
                        var col = hb.collider;
                        if (!col || !col.enabled) continue;
                        var bounds = col.bounds;
                        var e = bounds.extents;
                        AddPart(bounds.center, Mathf.Max(e.x, Mathf.Max(e.y, e.z)));
                    }
                if (PartCount == start)
                    AddPart(b.corePosition, Mathf.Max(0.3f, b.bestFitRadius));

                bodies[id] = b;
                if (attackWatched.Add(b)) b.onSkillActivatedServer += EnemyAttacked;
                creatureIndex[id] = creatureCount;
                creatures[creatureCount++] = new Native.Creature
                {
                    id = id,
                    pos = UnityHost.ToSim(b.footPosition),
                    yaw = UnityHost.Yaw(b.transform.forward),
                    alive = 1,
                    flying = (byte)(b.isFlying ? 1 : 0),
                    parts_start = (uint)start,
                    parts_len = (uint)(PartCount - start),
                };
            }
        }

        void AddPart(Vector3 centre, float radius)
        {
            if (PartCount == parts.Length) Array.Resize(ref parts, parts.Length * 2);
            parts[PartCount++] = new Native.Part
            {
                centre = UnityHost.ToSim(centre),
                radius = radius / UnityHost.Scale,
                stickable = 1,
            };
        }

        static ulong Id(CharacterBody b) => unchecked((ulong)(uint)b.GetInstanceID());

        void ReadBack()
        {
            PikiCount = Read(ref Pikis, (a, n) => Native.pik_sim_pikis(sim, a, n));
            PelletCount = Read(ref Pellets, (a, n) => Native.pik_sim_pellets(sim, a, n));
            ReadOnions();
            SproutCount = Read(ref Sprouts, (a, n) => Native.pik_sim_sprouts(sim, a, n));
        }

        void ReadOnions() => OnionCount = Read(ref Onions, (a, n) => Native.pik_sim_onions(sim, a, n));

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
                if (Plugin.LogEvents.Value)
                    for (int i = 0; i < n; i++)
                        LogEvent(events[i]);
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
                case Native.EventKind.CaptainDown: what = $"captain {e.a} is down"; break;
                case Native.EventKind.Dismissed: what = $"captain {e.a} dismissed his squad"; break;
                default: return;
            }
            Plugin.Log.LogInfo(what);
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
            var at = victim.corePosition;
            if (part >= 0 && creatureIndex.TryGetValue(creature, out int ci))
            {
                var c = creatures[ci];
                if (part < c.parts_len) at = UnityHost.ToUnity(parts[c.parts_start + part].centre);
            }
            var spec = new NativeHitSpec
            {
                coefficient = amount * Plugin.DamageCoefficient.Value,
                position = at,
                procCoefficient = Plugin.ProcCoefficient.Value,
            };
            // A Pikmin's hit is Olimar's, measured from the Pikmin (Focus Crystal's range);
            // his own punch is his.
            bool landed = source == Native.SourcePiki && sourceId < PikiCount
                ? MinionHit.Deal(attacker, MinionOf(Pikis[sourceId]), health, spec)
                : NativeHit.Deal(attacker, health, spec);
            return landed ? (byte)1 : (byte)0;
        }

        static MinionState MinionOf(in Native.Piki p) => new MinionState
        {
            position = UnityHost.ToUnity(p.pos),
            moving = p.vel.x * p.vel.x + p.vel.z * p.vel.z > 1f,
            grounded = p.on_ground != 0,
            healthFraction = 1f,
        };

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
            if (sim == IntPtr.Zero || !NetworkServer.active || r?.damageInfo == null) return;
            if (stepping)
            {
                afterStep.Add(() => OnDamageDealt(r));
                return;
            }
            if (r.attackerTeamIndex == TeamIndex.Player || r.attackerTeamIndex == TeamIndex.None) return;
            var at = r.damageInfo.position;
            if (at == Vector3.zero) return;
            var centre = UnityHost.ToSim(at);
            // A Pikmin has PikminHealth x the nearest Olimar's max health; the sim counts its
            // health out of 100 (piki_max_health's code default).
            float full = Mathf.Max(1f, Plugin.PikminHealth.Value * NearestOlimarHealth(at));
            Native.pik_sim_attack_pikis_in_sphere(sim, ref centre,
                Plugin.PikminHurtRadius.Value / UnityHost.Scale, r.damageDealt / full * 100f);
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
            if (!v || r.victimTeamIndex == TeamIndex.Player) return;
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
