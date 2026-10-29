using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
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

        // Projectiles with a hurt box that have just started, queued by a hook on ProjectileController.Start (main thread
        // only), so no scene is searched for them. If the hook could not be made, the scene is searched once a second instead.
        static readonly List<ProjectileController> startedShots = new List<ProjectileController>();
        static Hook shotHook;
        static bool shotHookFailed;
        float shotSearchAt;

        static void HookShots()
        {
            if (shotHook != null || shotHookFailed) return;
            try
            {
                var start = typeof(ProjectileController).GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (start == null) throw new MissingMethodException("ProjectileController.Start");
                shotHook = new Hook(start, (Action<Action<ProjectileController>, ProjectileController>)ShotStarted);
            }
            catch (Exception e)
            {
                shotHookFailed = true;
                Plugin.Log.LogWarning($"Shootable projectiles: hook failed ({e.GetType().Name}: {e.Message}); searching the scene once a second instead");
            }
        }

        static void ShotStarted(Action<ProjectileController> orig, ProjectileController self)
        {
            orig(self);
            try
            {
                // Only a projectile with a hurt box of its own can be a target; most have none.
                if (Instance && self && startedShots.Count < 256 && self.GetComponentInChildren<HurtBox>()) startedShots.Add(self);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Shootable projectiles: {e.Message}");
            }
        }

        /// Takes in the shots that started since the last tick (or, with no hook, searches the scene once a second).
        void FindShots()
        {
            if (shotHookFailed && Time.fixedTime >= shotSearchAt)
            {
                shotSearchAt = Time.fixedTime + 1f;
                startedShots.AddRange(FindObjectsOfType<ProjectileController>());
            }
            if (startedShots.Count == 0) return;
            foreach (var pc in startedShots)
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
            startedShots.Clear();
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
                    weighNeeds ??= new[] { Native.pik_weigh_need(0), Native.pik_weigh_need(1), Native.pik_weigh_need(2), Native.pik_weigh_need(3) };
                    SendNeed(s.id, weighNeeds[3]);
                }
                PruneNeeds();
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
