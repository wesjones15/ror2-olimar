# Pikmin melee balance and regeneration: revised proposal (2026-11-01)

**Status:** implemented 2026-11-01. The defaults below are the live defaults
(2/4 victims, 0.6 damage scale, 0.3 s window, 3%/s regen, 7 s delay, 2x out of
danger). Not yet played.

The Oct-31 survivability pass is correct hit detection with the wrong
consequence, and no way to recover from the consequence. This revision keeps
positional melee, caps how much of a swing the squad eats, and gives Pikmin the
same shape of regeneration a survivor has.

## The problem, in one paragraph

`PikminWorld.MobMelee.cs::EnemyMelee` (commit `40efbda`) gives every Pikmin
inside an `OverlapAttack`'s active volume the full hit. A Pikmin has
`PikminHealth (0.5) x Olimar max health` = 110 at level 1, so a tier-1 bite
(12) is 10.9% each; one swing covering 8 Pikmin removes 87% of a Pikmin's
health from the squad. Two facts compound it: the group proxy has 1,000,000
health and regeneration (`Plugin.cs`, so enemies camp it and keep swinging) and
field Pikmin have no meaningful regeneration, so every point of damage is
permanent.

## Part 1 - melee

Keep the positional test; limit what one swing reaches and how hard it lands.
Victims are picked **nearest the swing volume first**, so the Pikmin latched on
the attacker take it and the squad behind them does not.

| Setting | Section | Current | Proposed |
| --- | --- | --- | --- |
| `PikminMeleeMaxVictims` (new) | Combat | all in the volume (5-15) | **2** (+2 for champions/elites) |
| `PikminMeleeDamageScale` (new) | Combat | 1.0 | **0.6** |
| `PikminMeleeIFrames` (new) | Combat | 0 | **0.3 s** |
| `PikminHealth` | Combat | 0.5 | 0.5 (unchanged) |

### Worked numbers (level 1, Olimar 220, Pikmin 110)

| Swinger | Damage | Current, per swing | Proposed, per swing |
| --- | --- | --- | --- |
| Tier-1 melee | 12 | 10.9% of every Pikmin in the arc | 6.5% to at most 2 |
| Elite (x3) | 36 | 32.7% of every Pikmin in the arc | 19.6% to at most 4 |
| Boss (x5) | 60 | 54.5% of every Pikmin in the arc | 32.7% to at most 4 |

With 8 Pikmin in a swing: 87% of one Pikmin per tier-1 swing today versus 13%
proposed. A boss swing goes from 436% (four Pikmin erased) to 131% (one Pikmin
wounded), so a 20-Pikmin pile costs roughly one Pikmin per punch instead of the
squad in two punches.

The 0.3 s immunity window is the part that stops five mobs focus-firing the
same front pair in one frame. It is a per-Pikmin deadline, not a running timer
or a sim change: the hook records `until = now + 0.3` and compares on the next
hit, exactly like `procReadyAt` (`PikminWorld.cs`) and `nextShake`
(`PikminWorld.Combat.cs`).

## Part 2 - survivor-style regeneration

### What survivors actually have

- **Base regen**: Commando has `BaseHealthRegen = 1`, `ScalingHealthRegen =
  0.2` (RoR2 wiki survivor data). That is 1 hp/s at level 1, +0.2 per level.
  On 110 health it is ~0.9%/s: slow on its own.
- **Cautious Slug** (`HealWhileSafe`): +3 hp/s (+3 per stack, +0.6/stack per
  level) after 7 s out of danger. It is *passive regeneration*, so it lands in
  `body.regen`; Corpsebloom does not affect it.
- **Titanic Knurl, level regen, regen buffs**: also in `body.regen`.
- **Rejuvenation Rack** (`IncreaseHealing`): +100% per stack to all healing and
  passive regeneration, including the base. It multiplies the regen stat.
- **Corpsebloom**: +100% healing but caps at 10%/s; it converts direct heals,
  not the regen stat.

The repo already has both halves of this pattern: `MinionStats.cs` reads
`regenFraction = body.regen / body.maxHealth`, and Mario's `RegenWater()`
isolates the item part with `body.regen - (baseRegen + levelRegen*(level-1))`.

### Why Pikmin currently heal about nothing

`OlimarItems.BeforeStep` already calls `pik_sim_heal_squad(sim, captain,
stats.regenFraction * 100 * MinionHealScale * dt)`. At level 1 that is
`1/220 * 100 * 0.5` = 0.23 sim points per second on a 100-point Pikmin, or
~430 seconds to heal from zero. It is invisible. This is not a missing call; it
is a missing scale.

### The model

Give Pikmin their own survivor-shaped base, then let his healing items add to
it exactly as they add to his. The base is 3%/s, about 3x a survivor's own
base fraction (1/110 = 0.9%/s): a Pikmin has no items of its own and has to
recover inside a 15-30 s gap between packs, not over a whole stage.

```
Pikmin base:      PikminRegen (0.03 = 3%/s of a Pikmin's max health)
                  x (1 + Rejuvenation Rack stacks)     // Rack doubles a survivor's base too
His items:        body.regen / body.maxHealth x MinionHealScale (0.5)
                  // body.regen already includes Cautious Slug, Knurl, level, Rack
Out of danger:    whole rate x PikminRegenOutOfCombat (2) after PikminRegenDelay (7 s)
Applied:          pik_sim_heal_squad(captain, rate * 100 * dt)  // existing call
```

`heal_squad` already skips dying and dead Pikmin and heals every Pikmin
assigned to the captain, including latched ones, so a Pikmin on a boss heals
during the fight the way a survivor does.

| State (level 1, no items) | Rate | Time to full |
| --- | --- | --- |
| In danger | 3.0% + 0.23% = 3.2%/s | ~31 s |
| Out of danger (7 s) | 6.5%/s | ~15 s |
| + 1 Cautious Slug | 3.0% + 0.9% = 3.9%/s / 7.8%/s out | ~13 s out |
| + 1 Rejuvenation Rack | 6.0% + 0.45% = 6.5%/s / 12.9%/s out | ~8 s out |

The rack is the item that makes it feel like a survivor: it doubles the base,
not just his item share.

### What it does to a fight

| Case | Today | Revised |
| --- | --- | --- |
| 5 Beetles vs 10 Pikmin (~4 s) | ~2.2 Pikmin/s, wipe | 0-1 lost; front pair ~25%, full again in ~19 s |
| Elite or champion | wipe | 2-4 lost, heals between packs |
| Boss, 20 latched, ~7 punches | ~4.4 Pikmin/punch, wipe | ~1.1/punch, ~7-9 lost over the fight |

Trash becomes "certain damage that heals back"; a boss still costs Pikmin.
If packs arrive faster than the ~19 s heal, damage accumulates and the front
line dies - which is the intended tempo.

The existing Onion loop still matters and is unchanged: stored Pikmin are
counts, and `spawn_piki` brings them out at full health, so dumping and
recalling the squad is still a free full heal at the cost of a retreat. Pellets
and seeds still replace deaths. Regeneration is what makes the *field* squad
recover without a retreat; it does not replace the Onion.

## Settings summary

| Setting | Section | Default | Meaning |
| --- | --- | --- | --- |
| `PikminMeleeMaxVictims` | Combat | 2 | Most Pikmin one swing can hurt; +2 for champions/elites. 0 = no cap. |
| `PikminMeleeDamageScale` | Combat | 0.6 | Share of the swing's damage each victim takes. 1 = current. |
| `PikminMeleeIFrames` | Combat | 0.3 | Seconds a Pikmin cannot be hit by another swipe. 0 = current. |
| `PikminRegen` | Combat | 0.03 | Pikmin base regen, share of their max health per second. |
| `PikminRegenDelay` | Combat | 7 | Seconds without a Pikmin hurt before the out-of-danger rate. |
| `PikminRegenOutOfCombat` | Combat | 2 | Multiplier on the whole rate out of danger. 1 = off. |
| `PikminHealth` | Combat | 0.5 | Unchanged; the global pool stays where it is. |
| `MinionHealScale` | Items | 0.5 | Unchanged; scales his item regen into the Pikmin rate. |

## Implementation sketch

- `ror2-olimar/OlimarTest/PikminWorld.MobMelee.cs`, `EnemyMelee`: gather
  candidates as `(distance to the swing volume, Piki)`, sort, take the cap
  (`MaxVictims + 2` when `attacker.isChampion || attacker.isElite`), skip a
  Pikmin whose `meleeImmuneUntil[id]` is still in the future, then call
  `pik_sim_attack_piki(damage * scale)` and set `meleeImmuneUntil[id] = now +
  iFrames` on a landed hit. Use the same `float[]` grown with `Array.Resize` /
  `NextPowerOfTwo` as `procReadyAt`, and read `now` from the damage path's own
  clock (`sample.Now`, `Time.realtimeSinceStartup`, the clock `Swing.TryHit`
  already uses; switch both to `Time.fixedTime` together if pause-scaled
  windows are wanted). Clear the array in `FreeSim`.
  This window gates server-side damage only: clients see the resulting Pikmin
  health in the snapshot, so it needs no wire-format or replica change.
- `ror2-common/src/OverlapDamage.cs`, `Sample`: add
  `float DistanceTo(Vector3 point)` (minimum over the active volumes, 0 inside),
  reusing the existing box/sphere maths.
- `ror2-olimar/OlimarTest/Plugin.cs`: the six new `Combat.*` binds above.
- `ror2-olimar/OlimarTest/OlimarItems.cs`, `BeforeStep`: replace the single
  `stats.regenFraction` term with the formula above; add
  `lastPikminHurtAt` (set in `PikiHurt` and in `EnemyMelee`/`EnemyBlast` when
  they land) for the out-of-danger gate.
- `ror2-olimar/tests/IncomingBlastCheck/MeleeChecks.cs`: a wide volume over six
  Pikmin hurts only the nearest two; a second swing inside the immunity window
  hurts none; after it, it hurts again; the scale lands the expected health.
  Keep the "successive swings kill an idle squad" test, driving it past the
  window.
- `ror2-olimar/tests/NativeCheck` (or a small host test): a wounded squad at
  3.2%/s heals to full in ~31 s; with a Rack, ~15 s; while latched, it heals at
  the in-danger rate.

No libpikmin or ABI change is required for either part.

## Playtest

1. Tier-1 pack (5 Beetles/Lemurians): expect 0-1 lost and the front pair
   visibly wounded, then healed by the next pack. If the pack costs 0 and
   nothing is wounded, lower `PikminMeleeMaxVictims` to 1 or the scale to 0.5.
2. Elite/champion: 2-4 lost, not a wipe.
3. Boss with the full pile: ~1 Pikmin per punch, 5-12 over the fight; whistling
   out stops the bleed.
4. Idle a wounded squad out of danger: full health in ~20 s. With a Rack,
   about half that.
5. Confirm the squad still cannot facetank a boss indefinitely.

Not yet played. The numbers are the proposal, not a verified result.

## Not in scope

- Reverting the `OverlapAttack` hook. Positional hits are correct; the cap,
  scale and immunity are the fix.
- Making Pikmin immune or removing group aggro; both restore the original
  "cannot be fought" problem.
- On-hit heals (Medkit, Monster Tooth, Leeching Seed, Harvester's Scythe) for
  Pikmin. Survivors get those through `HealthComponent.Heal`; extending them to
  a bodiless sim is a separate item pass. Start with regen; add on-hit heals
  only if the squad still feels starved.
- Explosion damage. Keep one variable moving; add `PikminBlastDamageScale`
  later if blasts still delete squads.
