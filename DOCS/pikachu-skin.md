# Olimar's Captain Pikachu skin

Skin 3 in Olimar's loadout (Olimar 0, Louie 1, Pianta 2, Pikachu 3): Melee's Pikachu leads, and each of his
Pikmin is an Ice Climber on the Pikmin's own motion. The sim, jump, whistle and throws stay Olimar's; Pikmin
still behave, carry and fight as Pikmin.

## What is built

- **Models** from the user's own Melee disc (GALE01 Rev 2): `PlPkNr.dat` (Pikachu) and **all eight Ice Climber
  costumes** (Popo Nr/Re/Or/Gr, Nana Nr/Ye/Aq/Wh), loaded with `libssbm_assets::model::Model::load_costume`. Each Pikmin
  colour has a family and a hash of the Pikmin's id picks one, so the army is mixed but a colour still reads: Blue is Popo's
  blue, Nana's aqua and white; Red is Popo's red, Nana's pink and Popo's orange; Yellow is Nana's yellow and Popo's green
  (`pikachu::FAMILIES`). The Ice Climbers are not in libssbm's roster, so
  `libssbm_assets::disc::extract_ice_climbers` (`ICE_CLIMBER_ASSETS`: those costumes, `PlPk.dat`, `PlCo.dat`) adds
  them to the Melee cache. The plugin does it once at startup when the cache lacks them (`Skins.MeleeDisc`, or a Melee
  image beside the plugin or the Pikmin assets). Manual: `cargo run -p libssbm-assets --example extract_ice_climbers -- <image> <cache>`.
- **Motion**: the Pikmin share Olimar's 12-joint skeleton (one motion table plays on both in Pikmin 1), so one retarget
  serves Pikachu and the squad (`libpikmin-present/src/pikachu.rs`). Mapped joints hand over their world rotation change
  from the bind pose; the bones are found the way the game finds them in any fighter, through PlCo.dat's
  FighterPartsTable (`ftParts_GetBoneIndex`): HipN, WaistN, HeadN, an upper arm and a thigh per side. Pikachu is kind 12,
  Popo 10, Nana 11. The Melee shoulders, forearms and hands follow their upper arm, the shins and feet their thigh.
  Pikmin leaves/buds/flowers are still drawn at the Pikmin's head joint.
- **Size**: Pikachu is as tall as Olimar (`Skins.PikachuHeight`, a multiple of him); a climber is 0.42 of Olimar
  (`Skins.IceClimberHeight`), about a Pikmin's size. The lowest skinned vertex is put on the ground each frame.
- **Swarm cost**: a climber is hundreds of vertices more than a Pikmin; skinning works out each joint's matrices once per
  batch, and far climbers use the host's pose budget (they may keep their last mesh, `RETAIN_POSE`, like Pikmin).
- ABI 49: `pik_view_load_pikachu`, `pik_view_set_pikachu_ratios`, `pik_ssbm_ice_climbers_complete`,
  `pik_ssbm_extract_ice_climbers`, `pik_ssbm_find_disc`. Tested: `pikachu::tests` (all four bodies stand at the asked
  height on Olimar's bind pose), `the_pikachu_skin_draws_pikachu_and_his_climbers_through_the_abi`, `NativeCheck`.

## Not done / not verified

- **Never seen in game.** Only numbers were checked, not how the motions read on these bodies: the Pikmin and Olimar
  have no elbows or knees, so Pikachu's and the climbers' limbs move as stiff units, and Pikachu's ears, tail and the
  climbers' hoods follow their parent.
- Melee's lighting is lifted 1.5x like the Luigi and Melee hosts; no colour tint when frozen.
- No Pikachu or Ice Climber voices or effects: the sounds stay Olimar's and the Pikmin's.
- 100 climbers' frame cost has not been measured in game.
