using System.Collections.Generic;
using System.Linq;
using RoR2;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    internal partial class PikminWorld
    {
        // Bound AI/physics cost independently of army size: at most four occupied
        // six-metre cells per captain, refreshed twice a second. Native Pikmin stay
        // the source of truth; RoR2 chooses these bullseyes like other player allies.
        sealed class PikiGroup
        {
            public Minion minion;
            public Vector3 position;
        }
        readonly Dictionary<(int captain, int slot), PikiGroup> pikiGroups = new Dictionary<(int, int), PikiGroup>();
        readonly Dictionary<ulong, float> nextShake = new Dictionary<ulong, float>();
        float groupsAt;

        void ClearPikiGroups()
        {
            foreach (var group in pikiGroups.Values) group.minion?.Release();
            pikiGroups.Clear();
            nextShake.Clear();
            groupsAt = 0f;
        }

        void UpdatePikiGroups()
        {
            if (Time.fixedTime < groupsAt) return;
            groupsAt = Time.fixedTime + 0.5f;
            var cells = new Dictionary<(int captain, int x, int z), (Vector3 position, int count)>();
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive == 0 || p.captain < 0 || p.on_ground == 0 || p.state == Native.PikiState.Dying) continue;
                var at = UnityHost.ToUnity(p.pos);
                var key = (p.captain, Mathf.FloorToInt(at.x / 6f), Mathf.FloorToInt(at.z / 6f));
                cells.TryGetValue(key, out var cell);
                // Use a real member's position, keeping the bullseye on the ground and
                // out of walls rather than averaging across different platforms.
                cells[key] = (at, cell.count + 1);
            }
            var keep = new HashSet<(int, int)>();
            foreach (var captain in cells.Where(c => c.Value.count >= 3).GroupBy(c => c.Key.captain))
            {
                if (!driverOf.TryGetValue(captain.Key, out var driver) || !driver || !driver.Body) continue;
                int slot = 0;
                foreach (var cell in captain.OrderByDescending(c => c.Value.count).Take(4))
                {
                    var key = (captain.Key, slot++);
                    if (!pikiGroups.TryGetValue(key, out var group)) pikiGroups[key] = group = new PikiGroup();
                    if (group.minion == null || !group.minion.Alive)
                    {
                        group.minion?.Release();
                        group.minion = Minions.Spawn(Content.PikiGroupMinion, driver.Body, cell.Value.position, Quaternion.identity);
                        if (group.minion?.Body)
                        {
                            // Keep hurtboxes for real bullet/melee hits, without a solid
                            // Commando capsule obstructing Olimar's sprint jump.
                            var body = group.minion.Body;
                            foreach (var collider in body.GetComponentsInChildren<Collider>())
                                if (!collider.GetComponent<HurtBox>()) collider.enabled = false;
                            var model = body.modelLocator ? body.modelLocator.modelTransform : null;
                            if (model) model.localScale *= 0.45f;
                        }
                    }
                    if (group.minion == null) continue;
                    bool moving = (group.position - cell.Value.position).sqrMagnitude > 0.01f;
                    group.position = cell.Value.position;
                    group.minion.Place(group.position, Quaternion.identity, moving, true);
                    keep.Add(key);
                }
            }
            foreach (var key in pikiGroups.Keys.Where(k => !keep.Contains(k)).ToArray())
            {
                pikiGroups[key].minion?.Release();
                pikiGroups.Remove(key);
            }
        }

        void TimedShakeOffs()
        {
            var fighting = new HashSet<ulong>();
            for (int i = 0; i < PikiCount; i++)
            {
                var p = Pikis[i];
                if (p.alive != 0 && p.state == Native.PikiState.Attack && p.has_creature != 0) fighting.Add(p.creature);
            }
            foreach (var id in fighting)
            {
                if (!bodies.TryGetValue(id, out var body) || !body || body.GetComponent<TekiBody>()) continue;
                // Pikmin enemies already have their decomp's flick strategy. RoR2
                // enemies also flick on a clock, including pauses between boss skills.
                if (nextShake.TryGetValue(id, out float at) && Time.fixedTime < at) continue;
                Native.pik_sim_shake_off(sim, id);
                nextShake[id] = Time.fixedTime + (body.isChampion ? 0.25f : 1f);
            }
            foreach (var id in nextShake.Keys.Where(id => !fighting.Contains(id)).ToArray()) nextShake.Remove(id);
        }
    }
}
