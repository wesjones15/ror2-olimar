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

## Asked for, not built yet (what each needs)

- **His own voice and effects.** Pikmin 2 sound is JAudio 2 (`AudioRes/PSound.aaf`, `Banks/wScene_*.aw`,
  `Seqs/Seq.arc`, `Conductor.arc`), not the JAudio 1 that `libpikmin-audio` and `libsms-audio` port; effects are
  `user/Kando/effect/game.jpc` and `Ebisawa/effect/game.jpc` (JPA2 containers; `libsms-assets::jpa` reads Sunshine's
  JPA1). Each is a new reader plus player. Louie keeps Olimar's sounds and effects until then.
- **His own spaceship.** The Hocotate Ship is `user/Kando/ufo/arc.szs` (J3D, `ufo.bmd`, `ufo.bck`, `ufo.brk`, plus
  `ufoAnimMgr.txt`, `ufoColl.txt`; `ufo_gold` the gold one). It would replace the Dolphin for his arrival, healing and
  Ship Repair; libpikmin's ship model and its 30 parts are Pikmin 1's `.mod`, so it is a second ship path.
- **Purple and White Pikmin in his squad.** libpikmin's `PikiColor` is Blue, Red, Yellow only. Purple is `piki_p2_black.bmd`
  and White `piki_p2_white.bmd` in `pikis.szs` (already extracted). Adding the two colours touches the core (carry
  weights, damage, poison, the vanilla values in `pikiParms.txt`), the snapshot format, Onions and seeds, the FFI
  structs, and the host's colours, HUD and items. Louie's squad would draw from five colours.
