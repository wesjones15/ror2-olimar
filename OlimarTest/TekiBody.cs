using System.Collections.Generic;
using Ror2Common;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // A Pikmin enemy's RoR2 body (a cloned Beetle or Beetle Guard, see TekiBodies). RoR2 runs its AI, skills
    // and health; its own model is switched off and PikminView draws the disc's Bulborb at the body
    // instead, in the animation this picks from what the body is doing. Runs on every machine, from
    // observable state (position, health, the skill state machines), so clients need nothing sent.
    // Its hurt boxes are the vanilla ones, so Pikmin latch to it and Mario stomps it like any monster.
    public sealed class TekiBody : MonoBehaviour
    {
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
            }
        }

        void OnDestroy() => All.Remove(this);

        // The vanilla model stays (its hurt boxes ride its bones) but isn't drawn. Items add renderers
        // as they go, so this repeats.
        void LateUpdate()
        {
            shownFor += Time.deltaTime;
            if (shownFor > 0.5f && (Time.frameCount + (int)id) % 15 != 0) return;
            var model = body && body.modelLocator ? body.modelLocator.modelTransform : null;
            if (model) GameSettings.HideRenderers(model, true);
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

        public static bool Any => TekiBody.All.Count > 0 || TekiCorpses.Count > 0;

        public static int Collect(PikminWorld world, float dt, out TekiNative.Instance[] instances)
        {
            list.Clear();
            foreach (var t in TekiBody.All)
                if (t && t.Sample(dt, out var inst)) list.Add(inst);
            TekiCorpses.Collect(world, list);
            if (array.Length < list.Count) array = new TekiNative.Instance[Mathf.NextPowerOfTwo(list.Count)];
            list.CopyTo(array);
            instances = array;
            return list.Count;
        }
    }
}
