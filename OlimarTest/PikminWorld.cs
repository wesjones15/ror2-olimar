using System;
using System.Collections.Generic;
using RoR2;
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

        // What survives a stage change: per colour, squad Pikmin and Onion stock.
        readonly int[] carrySquad = new int[3];
        readonly int[] carryStored = new int[3];
        bool carryValid;

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
            bodies.Clear();
            creatureIndex.Clear();
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
            if (first)
            {
                PlaceOnions(d);
                BringPikmin(id, d);
            }
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
            Plugin.Log.LogInfo($"Pikmin sim started ({Native.Utf8(Native.pik_sim_params_source(sim))})");
            view.StartModels();
            return true;
        }

        // A new stage: note who's coming along, then start over. The next Olimar to register
        // starts the new stage's sim.
        void NewStage()
        {
            Array.Clear(carrySquad, 0, 3);
            Array.Clear(carryStored, 0, 3);
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive != 0 && p.captain >= 0 && p.color < 3 && InSquad(p.state))
                    carrySquad[p.color]++;
            }
            for (int i = 0; i < OnionCount; i++)
                for (uint c = 0; c < 3; c++)
                    carryStored[c] += (int)Onions[i].Stored(c);
            carryValid = true;
            Plugin.Log.LogInfo($"to the next stage: squad r{carrySquad[Native.Red]} y{carrySquad[Native.Yellow]} " +
                               $"b{carrySquad[Native.Blue]}, Onion r{carryStored[Native.Red]} " +
                               $"y{carryStored[Native.Yellow]} b{carryStored[Native.Blue]}");
            FreeSim();
        }

        static bool InSquad(byte s) =>
            s == Native.PikiState.Squad || s == Native.PikiState.Held || s == Native.PikiState.GoHang;

        void PlaceOnions(OlimarDriver d)
        {
            var b = d.Body;
            var fwd = Vector3.ProjectOnPlane(b.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            if (Plugin.OnionPerColour.Value)
            {
                uint[] colours = { Native.Red, Native.Yellow, Native.Blue };
                for (int i = 0; i < 3; i++)
                {
                    var dir = Quaternion.Euler(0f, (i - 1) * 40f, 0f) * fwd;
                    AddOnion(b.footPosition + dir * 7f, 1u << (int)colours[i]);
                }
            }
            else
            {
                AddOnion(b.footPosition + fwd * 6f, 0b111);
            }
        }

        void AddOnion(Vector3 at, uint colours)
        {
            var p = UnityHost.ToSim(UnityHost.Snap(at));
            int id = Native.pik_sim_add_onion(sim, ref p, colours);
            if (id >= 0) onionIds.Add(id);
        }

        // The squad and Onion stock from the last stage, or the configured start of a run.
        void BringPikmin(int captain, OlimarDriver d)
        {
            int[] squad = carryValid
                ? (int[])carrySquad.Clone()
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
            if (carryValid)
                for (uint c = 0; c < 3; c++)
                {
                    int onion = OnionFor(c);
                    if (onion >= 0 && carryStored[c] > 0)
                        Native.pik_sim_store_in_onion(sim, (uint)onion, c, 0, (uint)carryStored[c]);
                }
        }

        int OnionFor(uint colour)
        {
            ReadOnions();
            for (int i = 0; i < OnionCount; i++)
                if ((Onions[i].colors & (1u << (int)colour)) != 0)
                    return (int)Onions[i].id;
            return -1;
        }

        // ------------------------------------------------------------------------------------
        // The Onion menu, for the driver's UI.

        public int OnionInReach(int captain) =>
            sim == IntPtr.Zero ? -1 : Native.pik_sim_onion_in_reach(sim, (uint)captain);

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
                if (kv.Value && kv.Value.Active)
                {
                    input = kv.Value.BuildInput();
                    anyone = true;
                }
                Native.pik_sim_set_input(sim, (uint)kv.Key, ref input);
            }
            // With no Olimar about (all dead, or mid-load) the Pikmin wait.
            if (!anyone) return;

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
            var info = new DamageInfo
            {
                damage = amount * Plugin.DamageCoefficient.Value * (attacker ? attacker.damage : 12f),
                crit = attacker && attacker.RollCrit(),
                attacker = attacker ? attacker.gameObject : null,
                inflictor = attacker ? attacker.gameObject : null,
                position = at,
                force = Vector3.zero,
                procChainMask = default,
                procCoefficient = Plugin.ProcCoefficient.Value,
                damageType = DamageType.Generic,
                damageColorIndex = DamageColorIndex.Default,
            };
            health.TakeDamage(info);
            if (attacker && GlobalEventManager.instance)
            {
                GlobalEventManager.instance.OnHitEnemy(info, victim.gameObject);
                GlobalEventManager.instance.OnHitAll(info, victim.gameObject);
            }
            return 1;
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
            if (id >= 0) Corpses.Claim((uint)id, v);
            if (Plugin.LogEvents.Value && id >= 0)
                Plugin.Log.LogInfo($"{v.GetDisplayName()} left corpse {id} (needs {weight} Pikmin)");
        }
    }
}
