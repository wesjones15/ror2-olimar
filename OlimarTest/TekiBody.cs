using System.Collections.Generic;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // A Pikmin enemy's RoR2 body (a cloned Beetle or Beetle Guard, see TekiBodies). RoR2 runs its AI, skills
    // and health; its own model is switched off and PikminView draws the disc's Bulborb at the body
    // instead, in the animation this picks from what the body is doing. Runs on every machine, from
    // observable state (position, health, the skill state machines), so clients need nothing sent.
    // Its hurt boxes are the vanilla ones, so Pikmin latch to it and Mario stomps it like any monster.
    public sealed class TekiBody : MonoBehaviour
    {
        /// The first of Pikmin 2's enemy kinds (libpikmin-teki p2::FIRST).
        internal const byte P2FirstKind = 27;
        /// TekiCatalog.Dwarf or Spotty.
        public byte kind;
        /// A rare big enemy: always drops a corpse and a ship part for its killer.
        public bool big;

        internal static readonly List<TekiBody> All = new List<TekiBody>();
        static uint nextId;

        CharacterBody body;
        uint id;
        int slot = TekiNative.Wait;
        float frame, speed, flash, lastHealth, shownFor;
        Vector3 lastFoot;
        bool seen;
        EntityStateMachine[] machines;

        internal static bool IsBig(CharacterBody b) => b && b.GetComponent<TekiBody>() is TekiBody t && t.big;

        /// Its death always rewards a ship part: a big enemy, or one Ship Repair marked (the first stage's Breadbug).
        public bool guaranteedPart;
        internal static bool GuaranteesPart(CharacterBody b) => b && b.GetComponent<TekiBody>() is TekiBody t && (t.big || t.guaranteedPart);
        internal static bool IsEphemeral(CharacterBody b) => b && b.GetComponent<TekiBody>() is TekiBody t && TekiRoster.Of(t.kind)?.Ephemeral == true;

        /// The decomp's strategy for this kind runs it (TekiAi), rather than the borrowed monster's AI.
        internal bool Ai => TekiRoster.Of(kind)?.Ported == true;
        internal CharacterBody Body => body;
        internal uint NetId => body ? body.netId.Value : 0u;
        /// What the strategy starts with (server): the velocity a boulder was shot with, a counter (a Posy's size, a
        /// Clamclamp's part-holding pearl) and a goal.
        internal Native.Vec3? spawnVelocity, goal;
        internal int count0 = -1;
        // The strategy's id and last state (server).
        internal int aiId = -1;
        internal bool aiHave;
        internal TekiAiNative.State aiState;
        internal float aiLastHealth;
        /// The body died and the strategy was told, so that it plays its dying (effects, what it drops).
        internal bool aiDeathSent;
        int hurtDeactivated;

        void Awake()
        {
            body = GetComponent<CharacterBody>();
            id = ++nextId & 0x7FFF_FFFFu;
            if (id == 0) id = ++nextId;
            All.Add(this);
            // The disc's life and run speed (tekipara), scaled to RoR2's by the config.
            if (body && TekiCatalog.Has(kind))
            {
                var info = TekiCatalog.Get(kind);
                float health = Mathf.Max(1f, info.life * TekiBodies.HealthScale.Value);
                body.baseMaxHealth = health;
                body.levelMaxHealth = health * 0.3f;
                body.baseMoveSpeed = Mathf.Max(1f, info.runSpeed * UnityHost.Scale * TekiBodies.SpeedScale.Value);
                body.baseDamage *= Mathf.Max(0f, TekiBodies.DamageScale.Value);
                // A Honeywisp is not killed (a hit makes it drop its nectar and fly off), nor is a geyser or a shell.
                var spec = TekiRoster.Of(kind);
                if (spec != null && (spec.Ephemeral || spec.Hazard)) body.baseMaxHealth = body.levelMaxHealth = 1e6f;
            }
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (Ai && NetworkServer.active) TekiAi.Forget(this);
        }

        // Follows where its strategy wants it, a little behind it, facing as it does.
        void FixedUpdate()
        {
            if (!Ai || !body || !NetworkServer.active) return;
            if (!aiHave || !body.healthComponent || !body.healthComponent.alive) return;
            var s = aiState;
            // Hidden between trips: nothing can hit it.
            bool solid = s.Collidable && s.Visible && (kind != TekiCatalog.PelletPosy || s.Alive);
            if (!solid && hurtDeactivated == 0 && body.hurtBoxGroup)
            {
                body.hurtBoxGroup.hurtBoxesDeactivatorCounter++;
                hurtDeactivated = 1;
            }
            else if (solid && hurtDeactivated == 1 && body.hurtBoxGroup)
            {
                body.hurtBoxGroup.hurtBoxesDeactivatorCounter--;
                hurtDeactivated = 0;
            }
            var goal = UnityHost.ToUnity(s.pos);
            var now = body.footPosition;
            float follow = 1f - Mathf.Exp(-30f * Time.fixedDeltaTime);
            var motor = body.characterMotor;
            if (motor && !s.Flying && !s.Airborne)
            {
                var d = goal - now;
                d.y = 0f;
                motor.rootMotion += d * follow;
            }
            else
            {
                var root = transform.position - now;
                var to = Vector3.Lerp(now, goal, follow) + root;
                transform.position = to;
                if (body.rigidbody) { body.rigidbody.position = to; body.rigidbody.velocity = Vector3.zero; }
            }
            var look = Quaternion.Euler(0f, s.yaw * Mathf.Rad2Deg, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-20f * Time.fixedDeltaTime));
        }

        // The disc's size for the kind: the borrowed monster's model (and so its hurt boxes and hit boxes) is
        // scaled to match the Bulborb drawn on it.
        void Start()
        {
            // The strategy turns it; RoR2's own turning would fight it.
            if (Ai && NetworkServer.active && GetComponent<CharacterDirection>() is CharacterDirection direction) direction.enabled = false;
            if (!TekiBodies.FitSizes.Value || !body || !body.hurtBoxGroup || !TekiCatalog.Has(kind)) return;
            var model = body.modelLocator ? body.modelLocator.modelTransform : null;
            if (!model || body.hurtBoxGroup.hurtBoxes == null) return;
            Bounds box = default;
            bool any = false;
            foreach (var hb in body.hurtBoxGroup.hurtBoxes)
            {
                if (!hb || !hb.collider) continue;
                if (any) box.Encapsulate(hb.collider.bounds);
                else { box = hb.collider.bounds; any = true; }
            }
            if (!any || box.size.y < 0.1f) return;
            var info = TekiCatalog.Get(kind);
            float fit = Mathf.Clamp((info.hi.y - info.lo.y) * UnityHost.Scale / box.size.y, 0.4f, 4f);
            if (Mathf.Abs(fit - 1f) > 0.1f) model.localScale *= fit;
        }

        // The vanilla model stays (its hurt boxes ride its bones) but isn't drawn. Items add renderers
        // as they go, so this repeats.
        void LateUpdate()
        {
            shownFor += Time.deltaTime;
            if (shownFor > 0.5f && (Time.frameCount + (int)id) % 15 != 0) return;
            var model = body && body.modelLocator ? body.modelLocator.modelTransform : null;
            if (model) GameSettings.HideRenderers(model, true);
        }

        // The strategy's own motion and frame: from the server's state, or what it last sent.
        TekiAiNative.State remote;
        float remoteAge;

        bool SampleAi(float dt, out TekiNative.Instance instance)
        {
            instance = default;
            TekiAiNative.State s;
            if (NetworkServer.active)
            {
                if (!aiHave) return false;
                s = aiState;
            }
            else
            {
                if (!TekiAi.Remote.TryGetValue(NetId, out var r)) return false;
                if (r.at != remoteAge)
                {
                    remote = r.state;
                    remoteAge = r.at;
                }
                remote.frame += 30f * dt;
                s = remote;
            }
            if (!s.Visible) return false;
            instance = new TekiNative.Instance
            {
                id = id,
                // Low byte kind; next byte is the optional material frame + 1.
                kind = (uint)kind | (kind == TekiCatalog.PelletPosy ? (((s.flags >> 8) & 3u) + 1u) << 8 : 0u),
                pos = UnityHost.ToSim(body.footPosition),
                yaw = NetworkServer.active ? s.yaw : UnityHost.Yaw(transform.forward),
                motion = s.motion,
                frame = s.frame,
                flash = s.flash,
                // Pikmin 2's kinds report their size as the share of full (the Kogane family grows out of the ground);
                // Pikmin 1's report how solid they are.
                scale = kind >= P2FirstKind ? Mathf.Clamp01(s.fade) : 1f,
                alpha = kind >= P2FirstKind ? 1f : Mathf.Clamp01(s.fade),
            };
            return true;
        }

        // Whether a skill is running: any state machine but the body's own that isn't idle.
        bool Attacking()
        {
            machines ??= GetComponents<EntityStateMachine>();
            foreach (var esm in machines)
            {
                if (!esm || esm.customName == "Body" || esm.state == null) continue;
                if (esm.state.GetType().Name != "Idle") return true;
            }
            return false;
        }

        /// The enemy for the view this frame, or false if it is dead, hidden or its kind didn't load.
        internal bool Sample(float dt, out TekiNative.Instance instance)
        {
            instance = default;
            var health = body ? body.healthComponent : null;
            if (!health || !health.alive || !TekiCatalog.Has(kind)) return false;
            if (Ai) return SampleAi(dt, out instance);
            var info = TekiCatalog.Get(kind);
            var foot = body.footPosition;
            if (seen && dt > 0f)
            {
                var d = foot - lastFoot;
                d.y = 0f;
                speed = Mathf.Lerp(speed, d.magnitude / dt, 1f - Mathf.Exp(-12f * dt));
            }
            lastFoot = foot;
            seen = true;
            if (lastHealth > 0f && health.health < lastHealth - 0.01f) flash = 0.08f;
            lastHealth = health.health;
            flash = Mathf.Max(0f, flash - dt);

            int want = Attacking() ? TekiNative.Attack : speed > 0.7f ? TekiNative.Chase : TekiNative.Wait;
            if (want != slot)
            {
                slot = want;
                frame = 0f;
            }
            var m = info.motions[slot];
            float rate = 1f;
            if (slot == TekiNative.Chase)
                // Feet keep pace with the ground: the disc's run at the body's actual speed.
                rate = Mathf.Clamp(speed / Mathf.Max(0.1f, info.runSpeed * UnityHost.Scale), 0.5f, 3f);
            else if (slot == TekiNative.Attack)
                // A bite fits RoR2's short skill, not the disc's three seconds.
                rate = Mathf.Max(1f, m.end / (30f * TekiBodies.AttackSeconds));
            frame += 30f * dt * rate;
            if (slot == TekiNative.Attack) frame = Mathf.Min(frame, m.end);
            else
            {
                float a = m.loopStart, b = m.loopEnd;
                if (b <= a)
                {
                    a = 0f;
                    b = m.end;
                }
                if (b > a && frame >= b) frame = a + (frame - a) % (b - a);
            }
            var forward = transform.forward;
            instance = new TekiNative.Instance
            {
                id = id,
                kind = kind,
                pos = UnityHost.ToSim(foot),
                yaw = UnityHost.Yaw(forward),
                motion = m.index,
                frame = frame,
                flash = flash,
                scale = 1f,
                alpha = 1f,
            };
            return true;
        }
    }

    // Dead Pikmin enemies lying (and being carried) as the sim's corpse pellets: the pellet the sim
    // draws as a numbered pellet is hidden and the Bulborb's death pose is drawn on it instead, shrinking
    // into the Onion with it. The server notes a corpse when the enemy dies and tells the clients.
    internal static class TekiCorpses
    {
        sealed class Corpse
        {
            public byte kind;
            public float born, missingSince = -1f;
            public bool effects, soul;
        }

        const short Message = 0x4F20;
        static readonly Dictionary<uint, Corpse> byPellet = new Dictionary<uint, Corpse>();
        static readonly List<uint> gone = new List<uint>();

        public static int Count => byPellet.Count;
        public static bool Has(uint pellet) => byPellet.ContainsKey(pellet);
        public static void Clear() => byPellet.Clear();

        public static void Add(uint pellet, byte kind)
        {
            if (!byPellet.ContainsKey(pellet)) byPellet[pellet] = new Corpse { kind = kind, born = Time.time };
        }

        public static void RegisterMessages()
        {
            Mp.Register(Message, null, (from, reader) =>
            {
                uint pellet = reader.U32();
                byte kind = reader.U8();
                if (!reader.Failed && kind < TekiCatalog.Kinds) Add(pellet, kind);
            });
        }

        public static void Announce(uint pellet, byte kind)
        {
            Add(pellet, kind);
            var w = new ByteWriter();
            w.U32(pellet);
            w.U8(kind);
            Mp.SendToAll(Message, w.ToArray(), true);
        }

        public static void Collect(PikminWorld world, List<TekiNative.Instance> into)
        {
            if (byPellet.Count == 0) return;
            gone.Clear();
            foreach (var kv in byPellet)
            {
                var corpse = kv.Value;
                int i = Find(world, kv.Key);
                if (i >= 0 && world.Pellets[i].state == 3)
                {
                    gone.Add(kv.Key);
                    continue;
                }
                if (i < 0)
                {
                    // Not in this sim (yet): given a few seconds before it is forgotten.
                    if (corpse.missingSince < 0f) corpse.missingSince = Time.time;
                    else if (Time.time - corpse.missingSince > 5f) gone.Add(kv.Key);
                    continue;
                }
                corpse.missingSince = -1f;
                if (!TekiCatalog.Has(corpse.kind)) continue;
                var info = TekiCatalog.Get(corpse.kind);
                var dead = info.motions[TekiNative.Dead];
                var p = world.Pellets[i];
                // The death smoke as it falls, the soul once the dying motion is done (BTeki::dieSoon).
                var centre = p.pos;
                centre.y += (info.hi.y - info.lo.y) * 0.5f;
                if (!corpse.effects)
                {
                    corpse.effects = true;
                    // A strategy's own dying makes them (TaiStartDyingAction, createDeadEffect), here and on the clients.
                    if (TekiRoster.Of(corpse.kind)?.Ported != true)
                        foreach (int fx in TekiRules.DeathEffects(info.carcassSize)) TekiDraw.Queue(fx, centre);
                }
                if (!corpse.soul && Time.time - corpse.born >= dead.end / 30f)
                {
                    corpse.soul = true;
                    int soul = TekiRules.SoulEffect(info.carcassSize);
                    if (soul >= 0) TekiDraw.Queue(soul, centre);
                }
                // The pellet is on the body; the model's origin is the offset behind it (BTeki::dieSoon).
                float s = Mathf.Sin(p.yaw), c = Mathf.Cos(p.yaw);
                var o = info.corpseOffset;
                var at = p.pos;
                at.x -= o.x * c + o.z * s;
                at.z -= -o.x * s + o.z * c;
                into.Add(new TekiNative.Instance
                {
                    id = TekiRules.CorpseDrawId(kv.Key),
                    kind = corpse.kind,
                    pos = at,
                    yaw = p.yaw,
                    motion = dead.index,
                    frame = Mathf.Min((Time.time - corpse.born) * 30f, dead.end),
                    scale = Mathf.Clamp(p.scale, 0.01f, 1f),
                    alpha = 1f,
                });
            }
            foreach (var pellet in gone) byPellet.Remove(pellet);
        }

        static int Find(PikminWorld world, uint pellet)
        {
            for (int i = 0; i < world.PelletCount; i++)
                if (world.Pellets[i].id == pellet) return i;
            return -1;
        }
    }

    // What PikminView hands the view each frame: the live enemies and the corpses.
    internal static class TekiDraw
    {
        static readonly List<TekiNative.Instance> list = new List<TekiNative.Instance>();
        static TekiNative.Instance[] array = new TekiNative.Instance[16];
        static readonly List<TekiAiNative.Legs> legsList = new List<TekiAiNative.Legs>();
        static TekiAiNative.Legs[] legsArray = new TekiAiNative.Legs[4];

        /// The legs of the Long Legs among the enemies of the last [`Collect`] (their draw instances' ids), for the view.
        public static TekiAiNative.Legs[] Legs => legsArray;
        public static int LegsCount => legsList.Count;

        public static bool Any => TekiBody.All.Count > 0 || TekiCorpses.Count > 0;

        static readonly List<(int id, Native.Vec3 at)> effects = new List<(int, Native.Vec3)>();

        static readonly List<(int id, Native.Vec3 at, Native.Vec3 dir)> directed = new List<(int, Native.Vec3, Native.Vec3)>();
        static readonly List<(string name, Native.Vec3 at)> sounds = new List<(string, Native.Vec3)>();

        /// An effect to play at the next draw.
        public static void Queue(int id, Native.Vec3 at) => effects.Add((id, at));

        /// An effect that emits along `dir` (a flame out of a mouth).
        public static void Queue(int id, Native.Vec3 at, Native.Vec3 dir) => directed.Add((id, at, dir));

        /// A sound effect of the game's tables, by name, heard from `at`.
        public static void QueueSound(string name, Native.Vec3 at)
        {
            if (sounds.Count < 64) sounds.Add((name, at));
        }

        public static void Flush(PikminModels models)
        {
            if (effects.Count == 0 && directed.Count == 0 && sounds.Count == 0) return;
            if (models != null)
            {
                foreach (var (id, at) in effects) models.SpawnEffect(id, at);
                foreach (var (id, at, dir) in directed) models.SpawnEffect(id, at, dir);
                foreach (var (name, at) in sounds) models.PlayNamed(name, at);
            }
            effects.Clear();
            directed.Clear();
            sounds.Clear();
        }

        public static int Collect(PikminWorld world, float dt, out TekiNative.Instance[] instances)
        {
            legsList.Clear();
            list.Clear();
            foreach (var t in TekiBody.All)
            {
                if (!t || !t.Sample(dt, out var inst)) continue;
                list.Add(inst);
                // A Long Legs boss stands on its feet: the view needs them (the server has them; a client draws it without).
                if (t.Ai && TekiAi.TryLegs(t, out var legs))
                {
                    legs.id = inst.id;
                    legsList.Add(legs);
                }
            }
            TekiCorpses.Collect(world, list);
            if (array.Length < list.Count) array = new TekiNative.Instance[Mathf.NextPowerOfTwo(list.Count)];
            list.CopyTo(array);
            if (legsArray.Length < legsList.Count) legsArray = new TekiAiNative.Legs[Mathf.NextPowerOfTwo(legsList.Count)];
            legsList.CopyTo(legsArray);
            instances = array;
            return list.Count;
        }
    }
}
