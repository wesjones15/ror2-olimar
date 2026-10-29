using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace OlimarTest
{
    // Enemy projectiles that can be shot down (they carry a hurt box and a health component of their own) are creatures to
    // the sim: a Pikmin thrown at one latches to it, and a single Pikmin on it weighs it out of the air (libpikmin's
    // weigh.rs, PROJECTILE = 1). Projectiles without a hurt box are not targetable in RoR2 either and are left alone.
    internal partial class PikminWorld
    {
        sealed class ShotTarget
        {
            public ProjectileController controller;
            public HurtBox hurtBox;
            public HealthComponent health;
            public ulong id;
        }

        /// Creature ids of shots start here, clear of the bodies' (instance ids fit in 32 bits).
        const ulong ShotIdBase = 1UL << 40;

        readonly Dictionary<ulong, ShotTarget> shots = new Dictionary<ulong, ShotTarget>();
        readonly List<ShotTarget> shotList = new List<ShotTarget>();
        float shotSearchAt;

        /// Looks for new targetable shots a few times a second (a scene search, so not every tick).
        void FindShots()
        {
            if (Time.fixedTime < shotSearchAt) return;
            shotSearchAt = Time.fixedTime + 0.25f;
            foreach (var pc in FindObjectsOfType<ProjectileController>())
            {
                if (!pc) continue;
                ulong id = ShotIdBase + (uint)pc.GetInstanceID();
                if (shots.ContainsKey(id)) continue;
                var team = pc.teamFilter ? pc.teamFilter.teamIndex : TeamIndex.None;
                if (team == TeamIndex.Player || team == TeamIndex.None) continue;
                var hurt = pc.GetComponentInChildren<HurtBox>();
                if (!hurt || !hurt.healthComponent) continue;
                var target = new ShotTarget { controller = pc, hurtBox = hurt, health = hurt.healthComponent, id = id };
                shots[id] = target;
                shotList.Add(target);
            }
        }

        /// Adds the live shots to the creatures the sim is shown this tick, and drops the ones that are gone.
        void GatherShots()
        {
            if (!Plugin.WeighDownFliers.Value) { shots.Clear(); shotList.Clear(); return; }
            try
            {
                FindShots();
                for (int i = shotList.Count - 1; i >= 0; i--)
                {
                    var s = shotList[i];
                    if (!s.controller || !s.hurtBox || !s.health || !s.health.alive)
                    {
                        shots.Remove(s.id);
                        shotList.RemoveAt(i);
                        continue;
                    }
                    var col = s.hurtBox.collider;
                    var centre = col ? col.bounds.center : s.controller.transform.position;
                    float radius = col ? Mathf.Max(0.25f, col.bounds.extents.magnitude * 0.7f) : 0.5f;
                    int start = PartCount;
                    AddPart(centre, radius);
                    if (creatureCount == creatures.Length) Array.Resize(ref creatures, creatures.Length * 2);
                    creatures[creatureCount++] = new Native.Creature
                    {
                        id = s.id,
                        pos = UnityHost.ToSim(centre),
                        yaw = UnityHost.Yaw(s.controller.transform.forward),
                        alive = 1,
                        flying = 1,
                        parts_start = (uint)start,
                        parts_len = (uint)(PartCount - start),
                    };
                    Native.pik_sim_set_ground_need(sim, s.id, Native.pik_weigh_need(3));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Shootable projectiles: {e.Message}");
            }
        }

        /// A Pikmin's hit, or its weight, on a shot: it is destroyed. True when `creature` is one.
        bool HitShot(ulong creature, uint source, uint sourceId)
        {
            if (!shots.TryGetValue(creature, out var s)) return false;
            if (!s.health || !s.health.alive) return true;
            var attacker = AttackerFor(source, sourceId);
            s.health.TakeDamage(new DamageInfo
            {
                damage = s.health.fullCombinedHealth * 2f + 1f,
                attacker = attacker ? attacker.gameObject : null,
                position = s.hurtBox.transform.position,
                procCoefficient = 0f,
                damageType = DamageType.Generic,
            });
            return true;
        }
    }
}
