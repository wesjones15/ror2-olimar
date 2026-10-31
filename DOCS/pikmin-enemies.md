# Pikmin enemies in Ship Repair

Every Pikmin 1 enemy with usable files on the disc runs the decomp's own strategy (`libpikmin/crates/libpikmin-teki`,
a port of `plugPikiNakata/Tai*` and `plugPikiYamashita/TAI*`) inside a RoR2 body. The borrowed monster's AI is removed; its
body keeps the health and hurt boxes (so Pikmin latch on, Mario can stomp it, items work) and follows where the strategy
puts it. The disc's model, animations, sounds and particle effects are drawn on it (`TekiBody`, `TekiDraw`, `TekiAi`).

## What is ported

| Kind | Enemy | Strategy | In Ship Repair |
|---|---|---|---|
| 0, 2 | Dwarf Bulborb / Bulbear | `TaiChappyStrategy` | director, small |
| 1, 3 | Spotty Bulborb / Bulbear | `TaiSwallowStrategy` | director, big |
| 4, 5 | Wollywog, Yellow Wollywog | `TaiOtimotiStrategy` | director, big |
| 6 | Armored Cannon Beetle | `TAIbeatleStrategy` | director, big; fires Rolling Boulders |
| 7 | Fiery Blowhog | `TAItankStrategy` | director, big |
| 8 | Swooping Snitchbug | `TaiNapkidStrategy` | director, small, flying |
| 9, 10, 11 | Female/Male Sheargrub, Shearwig | `TAIkabekuiA/B/C` | director, small |
| 12 | Wogpole | `TAIotamaStrategy` | placed in the stage's water |
| 13 | Puffstool | `TaiKinokoStrategy` | director, big |
| 14 | Puffy Blowhog | `TAImarStrategy` | director, big, flying |
| 15 | Mamuta | `TAImiurinStrategy` | director, big |
| 16 | Smoky Progg | `TAIdororoStrategy` | director, big; also hatches from eggs |
| 17 | Water Dumple | chappy states | placed in the stage's water |
| 18 | Honeywisp | `TaiMizinkoStrategy` | placed; a hit drops nectar |
| 19 | Breadbug | `TaiCollecStrategy` | first stage: one placed, always drops a ship part |
| 20 | Rolling Boulder | `TaiIwagonStrategy` | made by the Cannon Beetle |
| 21 | Giant Egg | `TAItamagoStrategy` | director; bursts into a Smoky Progg when a player is near |
| 22 | Fire Geyser | `TAIhibaAStrategy` | placed (`FireGeysersPerStage`), unkillable |
| 23 | Pellet Posy | `TaiPalmStrategy` | common in Ship Repair (`PelletPosiesShipPerPlayer`), fewer near Classic Olimars (`PelletPosiesClassicPerOlimar`); grown plants drop a matching 1 pellet |
| 24, 25, 26 | Pearly Clamclamp, pearl, treasure pearl | `TaiShell/PearlStrategy` | one per stage after the first; its pearl is a ship part |

Not ported: the Iwagen (unused; its parameter file is an older format), the Breadbug nest (`Hollec`, no usable parameter
file) and the Honeywisp spawner (`Mizigen`; Ship Repair places Honeywisps itself).

## Where the port differs from the decomp

- No route graphs, Onions or work objects (bridges): wandering uses random points in the territory, the Smoky Progg heads
  for the nearest player (or a goal the host sets), the Sheargrubs' bridge-eating states are empty.
- Spores of the Puffstool hurt Pikmin; they do not turn them into Puffmin.
- The Mamuta's punch buries (`InteractBury`): a Pikmin becomes a flower sprout thrown a little way and landing to take root; a
  player takes the punch's damage and is rooted (`Entangle`) for 4 seconds instead of wiggling free, and a buried player calms it
  (`TAIAcheckSatisfyMiurin`, `Navi::isBuried`).
- A Puffy Blowhog's wind pushes with `flick_pikis_in_sphere` inside the cone; Pikmin are not blown by particles.
- Footstep dust for the walkers (`TAIAeffCloudOfDust` / `TaiJointEffectAction`) is a timer, not a foot-sink test, except
  where noted in the Rust file.
- Continuous particle generators that follow a joint (Smoky Progg's smoke and eyes, the shell's twinkle) are emitted
  afresh a few times a second.
- When a RoR2 body dies the strategy is given lethal damage and a few ticks to play its dying (a Posy's pellet, an egg's
  burst), then dropped. Effects the death animation would play later (keyed effects past the first ticks) do not play.

## Not verified in game

Built and staged, with headless tests on the Rust side (`cargo test -p libpikmin-teki`) and `ShipRepairCheck` on the C#
side. Nobody has played any of this: how the bodies follow the strategies up and down (jumps, flights), whether the
effects and particles look right, the named sounds, hatching, the boulder and its hit on players, burn damage, the
placement of geysers, posies and the shell, and two-player replication of the new messages (`0x4F23` named sounds,
`0x4F24` directed effects).
