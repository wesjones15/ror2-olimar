# Pikmin 1 Flint Beetle and HUD numbers

## Exact source and assets

`pikmin-main/src/plugPikiNishimura/BossMgr.cpp::constructBoss` loads
`bosses/kogane/kogane.mod`; `BossShapeObj.cpp::BossShapeObject` loads
`bosses/kogane/anims.bin` and its `kogane.anm` bundle. Embedded model textures
are used unchanged. New port kind **56** preserves all existing P1/P2 IDs.
The P2 Flint Beetle remains ID 27 for compatibility but no longer has a director card.

`KoganeAi.cpp` and `Kogane.cpp` define the P1 creature, not P2's three-flip Kogane.
Motion 6 walks, 2 idles, 1 flips. `KoganeProp::read` reads three tagged parameter
blocks; emergence radius/rates, speed, turning, target ranges, lifetime, idle times
and model scale come from the user's `bosses/kogane/parms.bin`.

`KoganeAi::initCreate` identifies particle emitters **89** (`kgp_ht.pcr`, forward),
**88** (`kgp_kr.pcr`, right), **87** (`kgp_kl.pcr`, left).
`initAppear`, `keyAction1` and `dieState` identify **120** (`kch_kem2.pcr`).
`setMapAttribute`/`keyAction0` use **19** (`nav_asi0.pcr`) on ordinary solid ground.
These are Pikmin particle emitters, not effect models or Melee cue IDs;
`include/EffectMgr.h` proves their numeric IDs.
`koganeSE` and `SoundID.h` identify **0x76 SE_KOGANE_WALK** and
**0x77 SE_KOGANE_DAMAGE**. The original walk animation keys and pellet-drop
sound calls drive them.

## Behavior and adapter

`Kogane::init` sets invincibility; `collisionCallback` accepts a flying Pikmin.
The shared strategy ignores damage of any size. In RoR2, Buddha prevents lethal
host damage and its health resets each native step. A thrown Pikmin's latch/landing
event sends the shared press event, then requests the existing harmless shake-off.
Ordinary player attacks and latched bites cannot farm loot.

`KoganeAi::createPellet` gives eight successful flips:
**1 pellet, two nectar blobs, two nectar blobs, 5 pellet**, repeated twice.
The flip's ACTION0 key releases the loot. Nectar uses `birthItemWater`'s 0.4π fan,
50 horizontal / 200 vertical launch through the common nectar simulation.
The ordinary pellet adapter places numbered pellets beside the beetle; it does
not reproduce the original pellet's upward launch. Movement uses local random
goals because foreign stages do not contain Pikmin's route graph. Ground effects
use the source's solid-ground choice; RoR2 does not supply Pikmin terrain codes.

Nectar presentation is shared too: `itemMgr.cpp` lines 477–482 identify the exact
`objects/fl_water/fl_water.mod` falling drop and `objects/water/water.mod` puddle,
with their `.anm`/`anims.bin` clips. The old viewer already drew these; the mod's
shared view now draws leave/fall/splash and appear/idle/touch/drain states as well.
`FallWaterAI::CollideInit` supplies `SE_KURIONE_WATER` (0x8B), and
`pikiState.cpp` supplies `SEF_PIKI_DRINK` (0x103). Both now reach shared audio.

It hides until a captain or Pikmin approaches, emerges, walks/pauses, flips, and
shrinks underground on timeout. Requested adaptation: it also burrows immediately
after finishing the eighth flip, rather than waiting out the remaining lifetime.
Departure destroys the host fixture without a kill, corpse, gold or ship-part reward.

Ship Repair rolls once per player per stage at **50%**, other modes once per living
Olimar at **12%**. At most two encounters are placed per stage, using the existing
ground/clearance sampler; they never refill. Settings:
`Enemies.FlintBeetleShipChance` / `FlintBeetleOtherChance`. No Olimar outside
Ship Repair means no new beetles. The enemy-state packet now preserves 16 flag
bits, including the Posy's palette previously lost by its one-byte serialization.

## HUD source and proof

`pikmin-main/include/zen/Number.h::NumberPicCallBack::invoke` supplies the scale:
half a second, `1 ± (cos(2τt)+1)(1-t)0.3`. Increases expand, decreases contract;
only changed decimal places retrigger. The initial display is calm, and absolute
time avoids double advancement by IMGUI Layout/Repaint. Rust presentation owns
the formula, exposed by `pik_number_change_scale`; C# owns per-display digit clocks.
FREE, SQUAD and TOTAL use it with the original `NumberTex` digit textures.

Before C# adapter edits, the shared viewer rendered the exact beetle, several
animation frames, its impact effects, and the source HUD pulse. Captures:
`output/flint-walk.png`, `flint-flip.png`, `flint-nectar.png`, `flint-burrow.png`.
The first line labels the model path/kind; the second names the source/effect IDs.

```powershell
cargo run --release -p viewer --example pellet_posy -- ../output/flint-flip.png 31 --flint
cargo run --release -p viewer --example pellet_posy -- ../output/flint-nectar.png 110 --flint
cargo run --release -p viewer --example pellet_posy -- ../output/flint-burrow.png 503 --flint
```

Validation: shared core/enemy/presentation/FFI release suites, extracted-asset
integration checks, ShipRepairCheck and NativeCheck. The beetle tests verify
immortality, no loot from damage, exactly four pellets/eight blobs, eight flips,
timeout, burrow shrink and exact effect IDs. PCM tests verify both sounds resolve
and are audible. HUD tests cover source scale, sign, settling, digit carries and
rapid changes. Assets are extracted from the user's own disc, never packaged.
The asset crate's full unit suite is blocked by pre-existing duplicate test modules
in `sound/adpcm.rs` and `sound/stream.rs`; those unrelated edits were preserved.
Build staged under `target/BepInEx/plugins/OlimarTest`. Not yet played in RoR2.
