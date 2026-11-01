# Pikmin melee balance: a happy medium (2026-11-01)

The Pikmin survivability pass went too far. This is a proposal to keep the new
positional melee without letting one swing delete a squad.

## What changed, and where

| Date | Commit | Effect |
| --- | --- | --- |
| 10-30 | `ba06747` | Ground groups of 3+ Pikmin become unkillable RoR2 minions, so enemy AI can target and swing at them (`PikminWorld.Combat.cs::UpdatePikiGroups`, `Plugin.cs::PikiGroupMinion`). |
| 10-31 | `c9ccca4` | Every Pikmin inside an enemy explosion's real blast takes damage (`PikminWorld.Incoming.cs::EnemyBlast`). |
| 10-31 | `40efbda` | Every Pikmin inside an `OverlapAttack`'s active box/sphere takes the full swipe, even when no RoR2 body was hit (`PikminWorld.MobMelee.cs::EnemyMelee`, `OverlapDamage.cs`). |

The last one is the regression. It is correct hit detection with the wrong
consequence.

## Why it overshot

1. **Spread, not magnitude.** `EnemyMelee` calls `pik_sim_attack_piki` for every
   Pikmin whose body touches the swing volume. A Pikmin has
   `PikminHealth (0.5) x Olimar max health` = 110 at level 1, so a tier-1 bite
   (12, the README's own example) is 10.9% each. One swing that covers 8 Pikmin
   removes 87% of a Pikmin's health from the squad; two swings erase one Pikmin
   across the board. Before this commit only Pikmin within
   `PikminHurtRadius` (1.5 m) of an *actual RoR2 hit position* were hurt, so the
   blast radius, not the attack arc, limited the damage.
2. **No reaction.** `Sim::attack_piki` only refuses `Knocked`/`Pressed`
   (`damage.rs`), and the melee path never knocks. Pikmin stand in the arc,
   keep biting, and take every swing with no stagger, no scatter, and no
   per-Pikmin immunity window. Pikmin 1's `InteractFlick` (interactBattle.cpp)
   is exactly the missing behaviour: damage, knockback, and immunity while down.
3. **The proxy never dies.** `PikiGroupMinion` has 1,000,000 health and
   regeneration (`Plugin.cs`), so an enemy that acquires a group camps it and
   keeps swinging. The squad cannot "win" the target and cannot make the enemy
   retarget Olimar.
4. **Health is already generous.** 110 health against a 12-damage bite is
   ~9 bites per Pikmin. The old complaint was "one bite killed every Pikmin"
   (`281e875`); that was fixed. The new problem is that a bite hits *everyone*.

So the dials are: how many Pikmin a swing catches, how much of it each takes,
how often one Pikmin can be caught, and whether enemies seek the squad at all.

## The medium

**Principle:** a swipe should cost the Pikmin *biting the attacker*, not the
squad standing behind them. Latching onto a boss should be a trade, not a
free win and not a squad wipe. Pikmin 1 delivers attacks to the collision part
they touch, not to a screen-wide sphere; the new path should behave the same.

Recommended profile (new keys default to these; set them to
`0 / 1 / 0` to get today's behaviour back):

| Setting | Section | Current | Proposed | Why |
| --- | --- | --- | --- | --- |
| `PikminMeleeMaxVictims` (new) | Combat | all in the volume (5-15) | **2** (+2 for champions/elites) | one swing wounds a handful, not the squad |
| `PikminMeleeDamageScale` (new) | Combat | 1.0 | **0.5** | a swipe costs real health but is not a one-shot |
| `PikminMeleeIFrames` (new) | Combat | 0 | **0.5 s** | one Pikmin cannot be focus-fired by five mobs in the same frame |
| `PikminGroupsTargetable` (new) | Combat | on | **on** | keep the Oct-30 counterplay: enemies fight back |
| `PikminHealth` | Combat | 0.5 | **0.5** | leave the global dial alone; fix melee only |

Victims are chosen **nearest to the swing volume first**. Latched Pikmin sit at
distance zero, so the two biting the attacker take the hit and the squad behind
them does not. That is the intended trade.

### Worked numbers (level 1, Olimar 220, Pikmin 110)

| Swinger | Damage | Current, per swing | Proposed, per swing |
| --- | --- | --- | --- |
| Tier-1 melee | 12 | 10.9% of every Pikmin in the arc | 5.5% to at most 2 |
| Elite (x3) | 36 | 32.7% of every Pikmin in the arc | 16.4% to at most 4 |
| Boss (x5) | 60 | 54.5% of every Pikmin in the arc | 27.3% to at most 4 |

With 8 Pikmin in a swing, that is 87% of one Pikmin removed per tier-1 swing
today versus 11% proposed; a boss swing goes from 436% (four Pikmin erased) to
109% (one Pikmin wounded). The 20-Pikmin pile on a boss then costs roughly one
Pikmin per punch instead of the whole squad in two punches.

### Target feel

- A normal trash pack costs **0-2 Pikmin**.
- An elite or champion costs **2-5**.
- A boss costs **5-12** over the fight, and whistling the squad out stops the
  bleed.
- No single swing should ever remove more than about one Pikmin's worth of
  health from the squad.

If a pack still costs more than that, lower `PikminMeleeDamageScale` before
touching health. If Pikmin now shrug everything off, raise the cap first, then
the scale.

## Dials, in order

1. **Still too harsh** (squad still melts):
   - `PikminMeleeMaxVictims` 2 -> 1 for ordinary enemies (keep champion bonus).
   - `PikminMeleeDamageScale` 0.5 -> 0.35.
   - `PikminGroupsTargetable` off: enemies stop seeking the squad (the pre-10-30
     behaviour), so only Pikmin caught in a swing aimed at Olimar are hurt.
2. **Too soft** (Pikmin ignore bosses again):
   - `PikminMeleeMaxVictims` 3, `PikminMeleeDamageScale` 0.75,
     `PikminMeleeIFrames` 0.25.
   - Keep group aggro on; that is the part that makes bosses fight back.
3. **Deaths still feel punishing** (recovery, not immunity):
   - `Combat.PikminSquadRegen` (new, fraction of max health per second) 0.02,
     applied through the existing `pik_sim_heal_squad` call in
     `OlimarItems.cs`, gated on no enemy hit for `PikminRegenDelay` (5 s).
   - Raise the flower seed chance (`Params.piki.piki_leave_seed_chance`, disc
     0.8 = 20%) to 0.5, so half of flower deaths leave a pluckable sprout.
     This needs a small FFI setter; it is the cleanest way to make losses
     recoverable without making Pikmin immortal.
4. **"They used to take care of close targets"**:
   - Turn on `Params.ai.auto_attack` (disc `aiconst.bin` p01 is 0; the code
     default is 1). Idle/squad Pikmin then engage a creature within
     `idle_attack_search_range` (~4 m) by themselves, using the game's own
     "Pikmin: auto attack" flag. Needs an FFI setter, like the seed chance.
   - This pairs with the cap: the squad fights back while the enemy's swipes
     cost it a couple of Pikmin instead of all of them.

## Implementation sketch

All host-side except the two optional params.

- `ror2-olimar/OlimarTest/PikminWorld.MobMelee.cs`, `EnemyMelee`:
  gather candidates into `(distance, Piki)`, sort, take
  `max(0, MaxVictims) + (champion or elite ? 2 : 0)`, check a
  `Dictionary<ulong, float> meleeImmuneUntil` keyed by
  `(generation << 32) | id` against `sample.Now`, then call
  `pik_sim_attack_piki` with `damage * scale`. Set the immunity stamp on a
  landed hit. Clear the dictionary in `FreeSim`, next to `incomingMeleeWorld`.
- `ror2-common/src/OverlapDamage.cs`, `Sample`: add
  `float DistanceTo(Vector3 point)` returning the minimum distance to any
  `Volume` (0 inside), reusing the existing box/sphere maths.
- `ror2-olimar/OlimarTest/Plugin.cs`: five new `Combat.*` binds; defaults as
  in the table. `PikminGroupsTargetable` is read in `UpdatePikiGroups` when a
  group spawns: clear `HurtBox.isBullseye` (keep colliders) when off, so a
  swing aimed at Olimar still catches the squad but AI does not seek it.
- `ror2-olimar/tests/IncomingBlastCheck/MeleeChecks.cs`: assert a wide volume
  over six Pikmin damages only the nearest two; a second swing inside
  `PikminMeleeIFrames` damages none; after the window it damages again; the
  scale lands the expected health. Keep the existing "successive swings kill
  an idle squad" test but drive it past the i-frame window.
- Optional FFI (ABI bump, `libpikmin-ffi` + `Native.cs` together):
  `pik_sim_set_seed_chance`, `pik_sim_set_auto_attack`.

No change to `DamageCoefficient`, `ProcCoefficient`, Pikmin attack power or
`StrikeDamage`: offence is not the problem, and buffing it would just shorten
fights.

## Not proposed

- Reverting the `OverlapAttack` hook. Positional hits on native Pikmin are
  correct and testable; the cap and scale are what is wrong, not the detection.
- Making Pikmin immune again, or removing group aggro. Both restore the
  original complaint (a squad that cannot be fought).
- Changing explosion damage in the same pass. Explosions are telegraphed and
  rarer; if they still delete squads, add `PikminBlastDamageScale` later.
  Keep one variable moving at a time.

## Playtest

1. One run against tier-1 melee only (no elites). Count Pikmin lost per pack.
2. One champion/elite encounter. Confirm the squad loses a few, not all.
3. One boss. Confirm latching costs Pikmin and whistling stops the loss.
4. Confirm the squad still cannot stand in a boss's face indefinitely.

Not yet played. The numbers above are the proposal, not a verified result.
