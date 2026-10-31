# Pellet Posies

## Exact assets and behavior

Pikmin 1 `plugPikiNakata/tekimgr.cpp` names the plant `palm` (original TEKI_Palm = 7).
The shared port assigns **kind 23**. Files: `tekis/palm/palm.mod`, `palm.anm`,
`tekikeys/palm.key`, `tekipara/palm.bin`.

`taipalm.cpp::TaiPalmStrategy::translateMotionIndex` proves the animation sets:
size 0 dead/hit/idle = 0/1/2; size 1 = 3/4/5; size 2 = 6/7/8; growth = 9/10.
`TaiPalmChangingColorAction` advances blue/red/yellow; `tekibteki.cpp::BTeki::refresh`
animates the material at `_3BC` for animation type 1. The native view now receives that
material frame, including on multiplayer replicas.

These are **particle emitters**, not effect models or Melee cues:
`TaiPalmStrategy::createEffect` index 0 -> **70** (`gpel_a_1.pcr`), index 1 -> **71**
(`gpel_a_2.pcr`), index 2 -> **72 + 73** (`gpel_b.pcr`, `gpel_c.pcr`). A1/A2/C use
the plant's facing; B keeps its authored direction. `EffectMgr.h` confirms the IDs.

`TaiPalmSoundTable` indexes five sounds from `SoundID.h`:
**0x2E** SE_FLOWER_DOWN, **0x2F** SE_FLOWER_CONVULSION,
**0x30–0x32** SE_FLOWER_GROW0/GROW1/GROW2. The disc animation keys trigger these.

`TaiPalmSettingPelletAction` suppresses drops below growth stage 2;
`TaiPalmDyingAction::act` drops after the death motion ends. The model bears a 1 pellet.
The port previously dropped a 5 immediately; it now releases one matching-colour 1 pellet
at animation end. Natural growth periods and colour-change periods come from the disc.

## Viewer evidence

Before host changes, `crates/viewer/examples/pellet_posy.rs` rendered the exact model,
growth/hit/death motions and the identified growth effects. Labels contain the IDs and citations.
Workspace captures: `output/pellet-posy-models.png`, `pellet-posy-growth.png`,
`pellet-posy-drop.png`; `pellet-posy-colours.png` shows all three mature colours.
The drop log records effect 70/71/72/73 and all five sound-table
indices, then pellet index 0 after the fall. Reproduce from `libpikmin`:

```powershell
cargo run --release -p viewer --example pellet_posy -- ../output/pellet-posy-growth.png 94
cargo run --release -p viewer --example pellet_posy -- ../output/pellet-posy-drop.png 212
```

## Host rules

Ship Repair: target 6 per player, cap 24, harvest replacements every 20 seconds.
Classic: target 2 per living Olimar, cap 8, replacements every 60 seconds; no Olimar,
no new Posies. Initial placement begins six seconds into a stage. Ready plants alternate
with some buds and seedlings to retain natural growth. Ground and clearance checks use
the existing fixture placement code, with four metres between plants.

New settings: `Enemies.PelletPosiesShipPerPlayer` and `PelletPosiesClassicPerOlimar`.
The old `PelletPosiesPerPlayer` no longer controls population. `Enemies.Enabled` disables
the feature with the other Pikmin creatures. Only Posies extend to Classic; dangerous
fixtures and other director cards stay in Ship Repair.

RoR2's Buddha body flag holds the plant at its last HP while its native death plays;
the lethal hit is forwarded to the strategy, its hurtboxes deactivate, and its body is
removed when the native drop completes. Local RoR2 `HealthComponent.TakeDamageProcess`
confirms this flag clamps health to 1. This preserves the plant's fall rather than letting
the borrowed Beetle death state remove it after a few ticks.

Native ABI layouts stay unchanged: enemy-state flags bits 8–9 carry the Posy palette;
draw-kind low byte remains kind and its next byte carries optional material frame + 1.
The enemy-state network message preserves 16 flag bits so clients receive this palette.

Validation covers growth, exact effects/sound keys, delayed 1-pellet harvesting, no immature
drops, distinct rendered palette textures, audible PCM for every sound, native palette
transport, and mode/population/refill policies. The `libpikmin-teki`,
`libpikmin-present` and `libpikmin-ffi` release suites pass, as do ShipRepairCheck
and NativeCheck against the staged DLL. `build.ps1` staged both DLLs and the asset
pointer under `target/BepInEx/plugins/OlimarTest`. Not yet played in RoR2.
