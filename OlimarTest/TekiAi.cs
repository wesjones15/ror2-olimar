using System;
using System.Collections.Generic;
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
        const short StateMessage = 0x4F22, EffectMessage = 0x4F21;
        static IntPtr handle, handleSim;
        static TekiAiNative.Navi[] navis = new TekiAiNative.Navi[8];
        static TekiAiNative.Out[] outs = new TekiAiNative.Out[64];
        static readonly Dictionary<uint, CharacterBody> bodiesByNet = new Dictionary<uint, CharacterBody>();
        static float sentAt;

        public static void Register()
        {
            Mp.Register(EffectMessage, null, (from, reader) =>
            {
                int id = reader.U16();
                var at = new Native.Vec3(reader.F32(), reader.F32(), reader.F32());
                if (!reader.Failed && !NetworkServer.active) TekiDraw.Queue(id, at);
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
                        flags = reader.U8(),
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

        /// A body is gone (it died, or was removed): its strategy stops.
        public static void Forget(TekiBody b)
        {
            if (handle != IntPtr.Zero && b.aiId >= 0) TekiAiNative.pik_tekis_remove(handle, (uint)b.aiId);
            b.aiId = -1;
            b.aiHave = false;
        }

        static ulong CreatureId(CharacterBody b) => unchecked((ulong)(uint)b.GetInstanceID());

        /// One RoR2 step that ran `ticks` sim ticks (server).
        public static void Step(IntPtr sim, int ticks)
        {
            if (!NetworkServer.active || sim == IntPtr.Zero || ticks <= 0 || !ShipRepairMode.Active) return;
            bool any = false;
            foreach (var b in TekiBody.All)
                if (b && b.Ai) { any = true; break; }
            if (!any) return;
            var h = Ensure(sim);
            if (h == IntPtr.Zero) return;

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
                };
            }

            // Bodies in, hurt and placed.
            foreach (var b in TekiBody.All)
            {
                if (!b || !b.Ai || !b.Body || !b.Body.healthComponent) continue;
                var body = b.Body;
                if (!body.healthComponent.alive)
                {
                    Forget(b);
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
                    if (TekiRoster.Of(b.kind).Ephemeral)
                    {
                        // The Honeywisp flies its path out from where it was placed.
                        float territory = 420f;
                        var far = new Native.Vec3(foot.x + Mathf.Sin(yaw) * territory, foot.y, foot.z + Mathf.Cos(yaw) * territory);
                        TekiAiNative.pik_tekis_set_territory(h, (uint)b.aiId, territory, ref far);
                    }
                }
                float lost = b.aiLastHealth - body.healthComponent.health;
                b.aiLastHealth = body.healthComponent.health;
                if (lost > 0f && TekiCatalog.Has(b.kind))
                {
                    float amount = lost / Mathf.Max(1f, body.maxHealth) * TekiCatalog.Get(b.kind).life;
                    TekiAiNative.pik_tekis_damage(h, (uint)b.aiId, Mathf.Max(amount, 0.5f), -1);
                }
                // Where the body really is, when it was held back.
                if (TekiAiNative.pik_tekis_state(h, (uint)b.aiId, out var s) != 0)
                {
                    var rust = UnityHost.ToUnity(s.pos);
                    if (!s.Flying && Vector3.ProjectOnPlane(rust - body.footPosition, Vector3.up).magnitude > 1.5f)
                        TekiAiNative.pik_tekis_set_position(h, (uint)b.aiId, ref foot);
                }
            }

            uint n = TekiAiNative.pik_tekis_update(h, sim, navis, (uint)count, (uint)ticks);
            if (n > 0)
            {
                if (outs.Length < n) outs = new TekiAiNative.Out[(int)n * 2];
                n = TekiAiNative.pik_tekis_outs(h, outs, (uint)outs.Length);
                for (int i = 0; i < n; i++) Apply(outs[i]);
            }

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
                case 1: // an effect, here and on the clients
                    TekiDraw.Queue((int)o.a, o.pos);
                    var w = new ByteWriter();
                    w.U16((ushort)o.a);
                    w.F32(o.pos.x); w.F32(o.pos.y); w.F32(o.pos.z);
                    Mp.SendToAll(EffectMessage, w.ToArray(), true);
                    break;
                case 4: // a bite on a player
                    if (bodiesByNet.TryGetValue(o.a, out var victim)) Hit(o, victim);
                    break;
                case 5: // finished: a Honeywisp that flew away
                    foreach (var b in TekiBody.All)
                        if (b && b.aiId == (int)o.teki)
                        {
                            b.aiId = -1;
                            if (TekiRoster.Of(b.kind).Ephemeral) TekiSpawner.Remove(b);
                            break;
                        }
                    break;
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
                w.U8((byte)s.flags);
            }
            Mp.SendToAll(StateMessage, w.ToArray(), false);
        }
    }
}
