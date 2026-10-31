using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    // The ported Pikmin 1 strategies (libpikmin-teki) running RoR2's bodies. The server steps them with the
    // sim each tick: it gives each enemy the damage its body took and the players near it, and applies what they
    // do (bites on players, effects, an enemy that is finished). Each frame a body follows the place its strategy
    // wants it at, and shows the motion and frame it is in. The clients get those states over the network.
    internal static class TekiAi
    {
        const short StateMessage = 0x4F22, EffectMessage = 0x4F21, SoundMessage = 0x4F23, DirEffectMessage = 0x4F24, FollowEffectMessage = 0x4F25;
        static IntPtr handle, handleSim;
        static TekiAiNative.Navi[] navis = new TekiAiNative.Navi[8];
        static TekiAiNative.Out[] outs = new TekiAiNative.Out[64];
        static readonly Dictionary<uint, CharacterBody> bodiesByNet = new Dictionary<uint, CharacterBody>();
        static float sentAt;
        static readonly Dictionary<uint, float> burnedAt = new Dictionary<uint, float>();
        // Players a Mamuta buried, and until when (`InteractBury`: they dig themselves out, here after a few seconds).
        static readonly Dictionary<uint, float> buriedUntil = new Dictionary<uint, float>();
        const float BuriedSeconds = 4f;
        static IntPtr currentSim;
        static readonly List<TekiBody> dying = new List<TekiBody>();

        public static void Register()
        {
            Mp.Register(EffectMessage, null, (from, reader) =>
            {
                int id = reader.U16();
                var at = new Native.Vec3(reader.F32(), reader.F32(), reader.F32());
                if (!reader.Failed && !NetworkServer.active) TekiDraw.Queue(id, at);
            });
            Mp.Register(DirEffectMessage, null, (from, reader) =>
            {
                int id = reader.U16();
                var at = new Native.Vec3(reader.F32(), reader.F32(), reader.F32());
                var dir = new Native.Vec3(reader.F32(), reader.F32(), reader.F32());
                if (!reader.Failed && !NetworkServer.active) TekiDraw.Queue(id, at, dir);
            });
            Mp.Register(SoundMessage, null, (from, reader) =>
            {
                string name = reader.Str();
                var at = new Native.Vec3(reader.F32(), reader.F32(), reader.F32());
                if (!reader.Failed && !NetworkServer.active) TekiDraw.QueueSound(name, at);
            });
            Mp.Register(FollowEffectMessage,null,(from,reader)=> {
                uint key=reader.U32();int id=reader.U16();bool on=reader.U8()!=0;float scale=reader.F32();
                var at=new Native.Vec3(reader.F32(),reader.F32(),reader.F32());
                var dir=new Native.Vec3(reader.F32(),reader.F32(),reader.F32());
                if (!reader.Failed && !NetworkServer.active) TekiDraw.QueueFollow(key,id,at,dir,scale,on);
            });
            Mp.Register(StateMessage, null, (from, reader) =>
            {
                if (NetworkServer.active) return;
                int n = reader.U16();
                for (int i = 0; i < n && !reader.Failed; i++)
                {
                    uint net = reader.U32();
                    var s = new TekiAiNative.State
                    {
                        motion = reader.U16(),
                        frame = reader.F32(),
                        flash = reader.F32(),
                        fade = reader.U8() / 255f,
                        flags = reader.U16(),
                    };
                    Remote[net] = (s, Time.unscaledTime);
                }
            });
        }

        /// What the server last told this client about each enemy, by its body's network id.
        internal static readonly Dictionary<uint, (TekiAiNative.State state, float at)> Remote = new Dictionary<uint, (TekiAiNative.State, float)>();

        static IntPtr Ensure(IntPtr sim)
        {
            if (handle != IntPtr.Zero && handleSim == sim) return handle;
            Release();
            if (!TekiCatalog.Ready) return IntPtr.Zero;
            handle = TekiAiNative.pik_tekis_new(Native.CString(Plugin.AssetsDir), (ulong)DateTime.Now.Ticks);
            handleSim = sim;
            foreach (var b in TekiBody.All)
                if (b) b.aiId = -1;
            return handle;
        }

        public static void Release()
        {
            if (handle != IntPtr.Zero) TekiAiNative.pik_tekis_free(handle);
            handle = handleSim = IntPtr.Zero;
            Remote.Clear();
            foreach (var b in TekiBody.All)
                if (b) b.aiId = -1;
        }

        static bool legsChecked, legsOk;

        /// A Long Legs boss's legs as the view draws them (server): false for any other enemy, or if the library's struct is not ours.
        internal static bool TryLegs(TekiBody b, out TekiAiNative.Legs legs)
        {
            legs = default;
            if (handle == IntPtr.Zero || b.aiId < 0 || !b.aiHave) return false;
            if (!legsChecked)
            {
                legsChecked = true;
                legsOk = TekiAiNative.pik_legs_size() == TekiAiNative.LegsSize && Marshal.SizeOf(typeof(TekiAiNative.Legs)) == TekiAiNative.LegsSize;
                if (!legsOk) Plugin.Log.LogError("Pikmin enemies: TekiAiNative.Legs doesn't match pikmin.dll's struct; the Long Legs are drawn without their steps.");
            }
            return legsOk && TekiAiNative.pik_tekis_legs(handle, (uint)b.aiId, out legs) != 0;
        }

        /// A body is gone (it died, or was removed): its strategy stops.
        public static void Forget(TekiBody b)
        {
            if (handle != IntPtr.Zero && b.aiId >= 0) TekiAiNative.pik_tekis_remove(handle, (uint)b.aiId);
            b.aiId = -1;
            b.aiHave = false;
        }

        static ulong CreatureId(CharacterBody b) => unchecked((ulong)(uint)b.GetInstanceID());

        internal static bool FlintLanded(CharacterBody body)
        {
            var b = body ? body.GetComponent<TekiBody>() : null;
            if (!b || b.kind != TekiCatalog.FlintBeetle) return false;
            if (handle != IntPtr.Zero && b.aiId >= 0) TekiAiNative.pik_tekis_press(handle, (uint)b.aiId);
            return true;
        }

        /// One RoR2 step that ran `ticks` sim ticks (server).
        public static void Step(IntPtr sim, int ticks)
        {
            if (!NetworkServer.active || sim == IntPtr.Zero || ticks <= 0) return;
            bool any = false;
            foreach (var b in TekiBody.All)
                if (b && b.Ai) { any = true; break; }
            if (!any) return;
            var h = Ensure(sim);
            if (h == IntPtr.Zero) return;
            currentSim = sim;

            // The players, as the enemies' Navis.
            int count = 0;
            bodiesByNet.Clear();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var body = pc && pc.master ? pc.master.GetBody() : null;
                if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                if (count == navis.Length) Array.Resize(ref navis, count * 2);
                uint net = body.netId.Value;
                bodiesByNet[net] = body;
                navis[count++] = new TekiAiNative.Navi
                {
                    id = net, pos = UnityHost.ToSim(body.footPosition), size = Mathf.Max(0.3f, body.radius) / UnityHost.Scale, alive = 1,
                    buried = (byte)(buriedUntil.TryGetValue(net, out float until) && until > Time.time ? 1 : 0),
                };
            }

            // Bodies in, hurt and placed.
            foreach (var b in TekiBody.All)
            {
                if (!b || !b.Ai || !b.Body || !b.Body.healthComponent) continue;
                var body = b.Body;
                if (!body.healthComponent.alive)
                {
                    // The strategy is told once, and gets a few ticks to play its dying (a posy gives its pellet, an
                    // egg bursts, the death smoke goes up) before it is let go.
                    if (b.aiId >= 0 && !b.aiDeathSent && !TekiRoster.Of(b.kind).Ephemeral)
                    {
                        b.aiDeathSent = true;
                        TekiAiNative.pik_tekis_damage(h, (uint)b.aiId, 1e6f, -1);
                        dying.Add(b);
                    }
                    else if (!dying.Contains(b)) Forget(b);
                    continue;
                }
                var foot = UnityHost.ToSim(body.footPosition);
                if (b.aiId < 0)
                {
                    var info = TekiCatalog.Has(b.kind) ? TekiCatalog.Get(b.kind) : default;
                    float yaw = UnityHost.Yaw(body.transform.forward);
                    b.aiId = TekiAiNative.pik_tekis_spawn(h, b.kind, CreatureId(body), ref foot, yaw);
                    if (b.aiId < 0) continue;
                    b.aiLastHealth = body.healthComponent.health;
                    if (b.spawnVelocity.HasValue)
                    {
                        var v = b.spawnVelocity.Value;
                        TekiAiNative.pik_tekis_set_spawn_velocity(h, (uint)b.aiId, ref v);
                    }
                    if (b.count0 >= 0) TekiAiNative.pik_tekis_set_count(h, (uint)b.aiId, 0, b.count0);
                    if (b.goal.HasValue)
                    {
                        var g = b.goal.Value;
                        TekiAiNative.pik_tekis_set_goal(h, (uint)b.aiId, ref g);
                    }
                    if (TekiRoster.Of(b.kind).Ephemeral && b.kind == TekiCatalog.Honeywisp)
                    {
                        // The Honeywisp flies its path out from where it was placed.
                        float territory = 420f;
                        var far = new Native.Vec3(foot.x + Mathf.Sin(yaw) * territory, foot.y, foot.z + Mathf.Cos(yaw) * territory);
                        TekiAiNative.pik_tekis_set_territory(h, (uint)b.aiId, territory, ref far);
                    }
                }
                float lost = b.aiLastHealth - body.healthComponent.health;
                // Kogane is truly invulnerable, including lethal host hits. Buddha
                // holds the last HP; damage is discarded and health resets. Only
                // FlintLanded (a thrown Pikmin collision) triggers a loot flip.
                if (b.kind == TekiCatalog.FlintBeetle && body.healthComponent.health < body.maxHealth)
                {
                    lost = Mathf.Max(lost, .5f);
                    body.healthComponent.Networkhealth = body.maxHealth;
                }
                b.aiLastHealth = body.healthComponent.health;
                if ((lost > 0f || b.kind == TekiCatalog.PelletPosy && body.healthComponent.health <= 1f && !b.aiDeathSent) && TekiCatalog.Has(b.kind))
                {
                    float amount = lost / Mathf.Max(1f, body.maxHealth) * TekiCatalog.Get(b.kind).life;
                    if (b.kind == TekiCatalog.PelletPosy && body.healthComponent.health <= 1f && !b.aiDeathSent)
                    {
                        b.aiDeathSent = true;
                        TekiAiNative.pik_tekis_damage(h, (uint)b.aiId, 1e6f, -1);
                    }
                    else if (!b.aiDeathSent && b.kind != TekiCatalog.FlintBeetle) TekiAiNative.pik_tekis_damage(h, (uint)b.aiId, Mathf.Max(amount, 0.5f), -1);
                }
                // The ground and water under the body, which the strategies jump, fall and land on.
                {
                    float groundSim = foot.y;
                    bool water = false, haveGround = Physics.Raycast(body.footPosition + Vector3.up * 1.5f, Vector3.down, out var gh, 60f, UnityHost.Mask, QueryTriggerInteraction.Ignore);
                    if (haveGround) groundSim = gh.point.y / UnityHost.Scale;
                    if (Physics.Raycast(body.footPosition + Vector3.up * 2f, Vector3.down, out var wh, 10f, LayerIndex.water.mask, QueryTriggerInteraction.Collide))
                        water = !haveGround || wh.point.y >= gh.point.y - 0.2f;
                    TekiAiNative.pik_tekis_set_ground(h, (uint)b.aiId, groundSim, (byte)(water ? 1 : 0));
                }
                // Where the body really is, when it was held back.
                if (TekiAiNative.pik_tekis_state(h, (uint)b.aiId, out var s) != 0)
                {
                    var rust = UnityHost.ToUnity(s.pos);
                    if (!s.Flying && !s.Airborne && Vector3.ProjectOnPlane(rust - body.footPosition, Vector3.up).magnitude > 1.5f)
                        TekiAiNative.pik_tekis_set_position(h, (uint)b.aiId, ref foot);
                }
            }

            uint n = TekiAiNative.pik_tekis_update(h, sim, navis, (uint)count, (uint)(dying.Count > 0 ? Mathf.Max(ticks, 3) : ticks));
            if (n > 0)
            {
                if (outs.Length < n) outs = new TekiAiNative.Out[(int)n * 2];
                n = TekiAiNative.pik_tekis_outs(h, outs, (uint)outs.Length);
                for (int i = 0; i < n; i++) Apply(outs[i]);
            }

            foreach (var b in dying)
                if (b) Forget(b);
            dying.Clear();

            foreach (var b in TekiBody.All)
            {
                if (!b || !b.Ai || b.aiId < 0) continue;
                b.aiHave = TekiAiNative.pik_tekis_state(h, (uint)b.aiId, out b.aiState) != 0;
                if (!b.aiHave) b.aiId = -1;
            }
            if (Time.unscaledTime - sentAt > 0.1f)
            {
                sentAt = Time.unscaledTime;
                Broadcast();
            }
        }

        static void Apply(in TekiAiNative.Out o)
        {
            switch (o.kind)
            {
                case 11: // stable source emitter moved with its owner
                case 12: // stop emission, retaining live particles for their fade
                {
                    bool on=o.kind==11;
                    TekiDraw.QueueFollow(o.b,(int)o.a,o.pos,o.vel,o.x,on);
                    var fw=new ByteWriter();fw.U32(o.b);fw.U16((ushort)o.a);fw.U8(on?(byte)1:(byte)0);fw.F32(o.x);
                    fw.F32(o.pos.x);fw.F32(o.pos.y);fw.F32(o.pos.z);fw.F32(o.vel.x);fw.F32(o.vel.y);fw.F32(o.vel.z);
                    Mp.SendToAll(FollowEffectMessage,fw.ToArray(),!on);break;
                }
                case 1: // an effect, here and on the clients
                    TekiDraw.Queue((int)o.a, o.pos);
                    var w = new ByteWriter();
                    w.U16((ushort)o.a);
                    w.F32(o.pos.x); w.F32(o.pos.y); w.F32(o.pos.z);
                    Mp.SendToAll(EffectMessage, w.ToArray(), true);
                    break;
                case 2: // a named sound
                {
                    string name = o.Text();
                    if (name.Length == 0) break;
                    TekiDraw.QueueSound(name, o.pos);
                    var sw = new ByteWriter();
                    sw.Str(name);
                    sw.F32(o.pos.x); sw.F32(o.pos.y); sw.F32(o.pos.z);
                    Mp.SendToAll(SoundMessage, sw.ToArray(), false);
                    break;
                }
                case 3: // an enemy makes another (a boulder from a cannon, a Progg from an egg, a pearl from a shell)
                {
                    var made = TekiBodies.Spawn((byte)o.a, UnityHost.ToUnity(o.pos), o.x * Mathf.Rad2Deg);
                    if (made)
                    {
                        if (o.vel.x != 0f || o.vel.y != 0f || o.vel.z != 0f) made.spawnVelocity = o.vel;
                        // The shell's pearl holds a ship part when the shell was made with one.
                        if (o.a == TekiCatalog.Rocpe) made.guaranteedPart = true;
                        // A leader's Mitite fellows come up with it and do not call more.
                        if (o.a == TekiCatalog.Mitite) made.count0 = 1;
                    }
                    break;
                }
                case 4: // a bite, a punch or a burn on a player
                    if (bodiesByNet.TryGetValue(o.a, out var victim)) Hit(o, victim);
                    break;
                case 5: // finished: a Honeywisp that flew away, a boulder that stopped, an egg that hatched
                    foreach (var b in TekiBody.All)
                        if (b && b.aiId == (int)o.teki)
                        {
                            b.aiId = -1;
                            var spec = TekiRoster.Of(b.kind);
                            if (spec.Ephemeral || spec.NoCorpse) TekiSpawner.Remove(b);
                            break;
                        }
                    break;
                case 8: // pellets an enemy gives up (a Pellet Posy's flower, a pearl's pellet)
                {
                    uint number = o.a == 0 ? 1u : o.a == 1 ? 5u : o.a == 2 ? 10u : 20u;
                    for (uint i = 0; i < Mathf.Max(1, (int)o.b); i++)
                    {
                        float angle = i * Mathf.PI * 2f / Mathf.Max(1, (int)o.b);
                        var at = new Native.Vec3(o.pos.x + Mathf.Sin(angle) * 14f * (o.b > 1 ? 1f : 0f), o.pos.y + 6f, o.pos.z + Mathf.Cos(angle) * 14f * (o.b > 1 ? 1f : 0f));
                        if (currentSim != IntPtr.Zero) Native.pik_sim_spawn_numbered_pellet(currentSim, number, (uint)o.y, ref at, angle);
                    }
                    break;
                }
                case 10: // an effect along a direction (a flame out of a mouth, a gust)
                {
                    TekiDraw.Queue((int)o.a, o.pos, o.vel);
                    var dw = new ByteWriter();
                    dw.U16((ushort)o.a);
                    dw.F32(o.pos.x); dw.F32(o.pos.y); dw.F32(o.pos.z);
                    dw.F32(o.vel.x); dw.F32(o.vel.y); dw.F32(o.vel.z);
                    Mp.SendToAll(DirEffectMessage, dw.ToArray(), true);
                    break;
                }
            }
        }

        // `InteractAttack` on a Navi: RoR2 damage from the enemy's body, in proportion to its bite.
        static void Hit(in TekiAiNative.Out o, CharacterBody victim)
        {
            TekiBody from = null;
            foreach (var b in TekiBody.All)
                if (b && b.aiId == (int)o.teki) { from = b; break; }
            if (from == null || !from.Body || !victim.healthComponent) return;
            float scale = Mathf.Clamp(Mathf.Max(o.x, 10f) / 10f, 1f, 4f) * Mathf.Max(0f, TekiBodies.DamageScale.Value);
            if (o.y <= -1.5f)
            {
                // `InteractBury`: dug in to the neck and held there; the Mamuta takes that as having had its fill.
                buriedUntil[victim.netId.Value] = Time.time + BuriedSeconds;
                var root = ByName.Buff("Entangle");
                if (root != BuffIndex.None) victim.AddTimedBuff(root, BuriedSeconds);
            }
            else if (o.y < 0f)
            {
                // A burn lands every tick the victim is in the flame: hurt it a little, a few times a second.
                if (burnedAt.TryGetValue(victim.netId.Value, out float last) && Time.time - last < 0.4f) return;
                burnedAt[victim.netId.Value] = Time.time;
                scale *= 0.35f;
            }
            var dir = victim.corePosition - from.Body.corePosition;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : from.Body.transform.forward;
            victim.healthComponent.TakeDamage(new DamageInfo
            {
                attacker = from.Body.gameObject,
                inflictor = from.Body.gameObject,
                damage = from.Body.damage * scale,
                position = victim.corePosition,
                force = o.y > 0f ? (dir * 0.8f + Vector3.up * 0.5f) * Mathf.Min(o.y, 120f) * 8f : Vector3.zero,
                crit = false,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Default,
                damageType = DamageType.Generic,
            });
        }

        // The states the clients draw from.
        static void Broadcast()
        {
            if (!Mp.Networked) return;
            var w = new ByteWriter(256);
            int n = 0;
            foreach (var b in TekiBody.All)
                if (b && b.Ai && b.aiHave && b.NetId != 0) n++;
            if (n == 0) return;
            w.U16((ushort)n);
            foreach (var b in TekiBody.All)
            {
                if (!b || !b.Ai || !b.aiHave || b.NetId == 0) continue;
                var s = b.aiState;
                w.U32(b.NetId);
                w.U16((ushort)s.motion);
                w.F32(s.frame);
                w.F32(s.flash);
                w.U8((byte)Mathf.Clamp(Mathf.RoundToInt(s.fade * 255f), 0, 255));
                w.U16((ushort)s.flags);
            }
            Mp.SendToAll(StateMessage, w.ToArray(), false);
        }
    }
}
