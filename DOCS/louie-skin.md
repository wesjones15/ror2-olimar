# Olimar's Louie alt-skin

Louie from Pikmin 2, as skin 1 in Olimar's loadout (Olimar 0, Louie 1, Pianta 2). Optional: offered only when
the plugin has Pikmin 2's player files, which it extracts once from the user's own Pikmin 2 disc.

## What is built (phase 1: the body)

- **Extraction** (`libsms::extract::p2`, `pik_p2_extract`): `user/Kando/piki/pikis.szs` and `texts.szs` plus
  `naviParms.txt` / `pikiParms.txt` go to `<AssetsDir>/p2`. The plugin does it at startup (a few small archives)
  when `Skins.Pikmin2Disc` names the image, or one is found beside the plugin or the Pikmin assets. No disc, no
  Louie. Manual: `cargo run -p libsms --example p2_extract -- <image> <folder>`.
- **Model**: `NaviMgr::load` / `loadResources_float` (naviMgr.cpp) shows Olimar is `orima_model/orima1.bmd` and
  **Louie is `orima3.bmd`** (the President, `syatyou.bmd`, after the debt). Same J3D family as Sunshine's models, so
  `libsms-present::body::Louie` bakes it like the Pianta.
- **Motions**: Pikmin 2's `motion/*.bca` (`J3D1bca1`, `ANF1`, one value per frame; `Clip::parse_bca`). They are
  made for his rig, so they pose him directly. Which one plays follows Olimar's motion (`libpikmin-present/src/louie.rs`):
  wait, asibumi, walk, run2, throw, trwwait, nuku, fue, punch, damage, dead, jump, rolljmp; progress is where
  Olimar's clip is in its key range, a jump runs on the jump's own seconds. Not read: `animmgr.txt`'s per-motion
  event keys.
- **Same as Olimar**: sim, jump, whistle, throws, Pikmin. He stands as tall as Olimar (`Skins.LouieHeight` multiplies it).
- ABI 48: `pik_view_load_louie`, `pik_view_set_louie_height`, `pik_p2_complete`, `pik_p2_extract`, `pik_p2_find_disc`.
  Tested: libsms-present `body::louie_tests`, `libpikmin-ffi` `the_louie_skin_draws_through_the_abi`, and `NativeCheck`
  against the built DLL. Never seen in game.

## Purple and White Pikmin in his squad (ABI 50)

`PikiColor` has five colours (Blue 0, Red 1, Yellow 2, Purple 3, White 4); `PikiColor::ALL` is still Pikmin 1's three and
`from_index` still cycles them, `EVERY` and `from_id` take all five. Tuning is `Params::p2` (`Pikmin2Params`), read from
`<assets>/p2/parms/pikiParms.txt` by `pik_sim_new` when the Pikmin 2 files are there (code defaults otherwise):

| | Purple | White |
|---|---|---|
| attack (`P003`/`P004`) | 20 | 10 |
| run speed x (`P002`/`P001`) | 0.8 | 1.2 |
| carry power (`P005`/`P006`) | 0.6 | 3.0 |
| lifts as | ten carriers (`getColorTransportScale`) | one |

A bud adds `P020` (0.5) and a flower `P021` (1.0) to a carrier's power. Purple: a thrown one **pounds** where it comes
down, on a creature's part or the ground (`PikiHipDropState`): `P022` damage to what it touched (20) and every flying
creature within `P017` (60) is **brought down** (`Event::Grounded`; the host stuns it and pushes it to the floor for
`Pikmin.PoundGroundSeconds`). The throw itself is Olimar's: Pikmin 2 shortens a Purple's arc and stops it at the peak,
which would break far throws and lock-ons, so it is not done. White: an eaten one poisons its eater
(`Event::PoisonedEater`; the host hits the eater for `Pikmin.WhitePoisonDamage`, 300 as in Pikmin 2). Snapshot events
47-49; `PikOnion.stored` and `pik_sim_attackers` are five wide.

Bodies: `piki_p2_black.bmd` / `piki_p2_white.bmd` on Pikmin 2's own motions (`wait`, `walk`, `run2`, `nuku`...: the Pikmin 1
motion the animator picked names the clip, its progress picks the frame; `libpikmin-present/src/pikmin2.rs`), the leaf on
`happajnt3`, sized to a blue Pikmin times `P000`. `pik_view_load_pikmin2`; without the files they draw as tinted Pikmin 1 bodies.

Host: only a Louie captain starts with them (`Pikmin.StartPurple` 3, `Pikmin.StartWhite` 5) and his Onion takes all five
colours; the HUD, carry gauge, corpse colours and Onion stock count five. Not played in game. Not done: Pikmin 2's other
Purple/White rules (wind, gas and astonish immunities, digging), Candypop Buds, and the Onion menu icons for the two (the
panel shows the generic icon).

## Asked for, not built yet (what each needs)

- **His own voice and effects.** Pikmin 2 sound is JAudio 2 (`AudioRes/PSound.aaf`, `Banks/wScene_*.aw`,
  `Seqs/Seq.arc`, `Conductor.arc`), not the JAudio 1 that `libpikmin-audio` and `libsms-audio` port; effects are
  `user/Kando/effect/game.jpc` and `Ebisawa/effect/game.jpc` (JPA2 containers; `libsms-assets::jpa` reads Sunshine's
  JPA1). Each is a new reader plus player. Louie keeps Olimar's sounds and effects until then.
- **His own spaceship.** The Hocotate Ship is `user/Kando/ufo/arc.szs` (J3D, `ufo.bmd`, `ufo.bck`, `ufo.brk`, plus
  `ufoAnimMgr.txt`, `ufoColl.txt`; `ufo_gold` the gold one). It would replace the Dolphin for his arrival, healing and
  Ship Repair; libpikmin's ship model and its 30 parts are Pikmin 1's `.mod`, so it is a second ship path.
