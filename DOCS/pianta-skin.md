# Olimar's Pianta alt-skin

A Sunshine Pianta (from the user's own Sunshine disc) drawn in place of Olimar. His sim, Pikmin,
whistle and controls stay Olimar's; only the body, its animation source and its voice change.
Started from codex's viewer demo (`libsms/docs/peach-demo.md`, "Pianta and Piantissimo"), which posed
a Pianta on Mario's motion in the libsms viewer only.

## Skin numbering

Native ids (`libpikmin_present::pianta`, `OlimarSkin`): **0 Olimar, 1 reserved for Louie, 2 Pianta**.
The loadout index is the position in the registered list, so until Louie exists the Pianta is the
second entry (index 1); his native id will not change when Louie arrives, but a saved loadout index
may shift. Commando's placeholder skins (inherited from the cloned body) are gone: `OlimarSkin.Register`
replaces the `ModelSkinController`'s list.

## Availability and the unlock

- Offered only when the Sunshine cache is found: config `Skins.SunshineAssets`, else `$SMS_ASSETS`,
  else beside the plugin (`../MarioTest/assets`, `assets`) or the Mario plugin's `assets_dir.txt`.
  Needs `npc/montem`, `npc/montemcommon`, `bmd/ma_mdl1.bmd` and a few `bck/ma_*.bck`.
- `Skins.PiantaUnlocked` (default **true**) keeps him open for testing. Off, he gets an
  `UnlockableDef` ("Defeat Mithrix and escape the planet.") that **nothing grants yet**: the Mithrix
  trigger is not written.

## Which clip poses him (`libpikmin-present/src/pianta.rs`)

| Captain is | Clip |
|---|---|
| standing | the Pianta's own idle (`mom_wait`) |
| walking, running, fleeing | Mario's `ma_walk` / `ma_run2`, phase taken from Olimar's walk loop |
| jumping | Mario's `ma_jump`; a jump restarted in the air plays `ma_2jmp1/2` then, the next time, `ma_tjmp1/2` |
| throw, pluck, whistle, dismiss, punch, damage, knockdown | Olimar's own clip, retargeted (`OLIMAR_ALIASES`) |

The palm (`yashi_jnt`), its leaves and the nose always play the Pianta's own clip (idle, or his walk and
run while moving), since Olimar and Mario have nothing for them. No FLUDD. His lowest skinned vertex is put
on the ground each frame. Olimar's rig is 12 unnamed joints with no elbows or knees, so the forearms,
hands, shins and feet follow their upper limb rigidly in Olimar-clip poses.

The straw skirt (`mino_null`) is not animated by any Pianta clip, and nothing in the decomp moves it; it
follows the hips and stays stiff, as in the game.

## Height

`Skins.PiantaHeight` (metres to the top of the head, default 1.75; Mario and Commando are 1.82, Olimar is
`OlimarHeight` x that). The host converts to sim units and calls `pik_view_set_pianta_height`. His hitbox is
still Olimar's.

## Voice

For a Pianta captain Olimar's throw and hurt cries are muted and Sunshine's male Pianta lines play instead:
jump `MONTM_PROUD`, throw `MONTM_INVITN`, hurt `MONTM_SURPRS`, down `MONTM_REGRET` (`pianta::Voice`). Chosen by
name, not heard. The whistle, dismiss, pluck and Pikmin sounds are unchanged. It needs the Sunshine sound
data (`AudioRes`) and a view with sound; its own output stream starts when a captain first wears him.
`libsms-audio/tests/pianta_voices.rs` checks the four cues exist and are audible.

## ABI

libpikmin ABI 47: `pik_view_load_pianta(view, sunshineDir, female, colour)` appends the model to the table
(call before the host reads it; -1 when the files are missing), `pik_view_set_captain_skin(view, captain, skin)`,
`pik_view_set_pianta_height(view, units)`. The female Pianta (`montew`) loads but the host does not offer her.

## Not done / not verified

- **Never played or looked at in game.** Checked: libsms-present `body` tests (bone lengths across Mario
  clips), libpikmin-present `pianta` tests (his arms point where Olimar's do across eight Olimar clips), an
  ABI test through the real C functions (feet on the ground, 20-60 units tall while standing, walking, running,
  jumping and whistling), and `NativeCheck` against the built DLL.
- Pikmin do not copy his jumps or play Mario clips; his acrobatics are visual only (Olimar's jump is
  unchanged). Real Mario acrobatics (triple jump, dive, wall kick) as gameplay would be a separate feature.
- The 2jmp/tjmp clip pairing is by name; the double and triple jump poses are unseen.
- Lobby and portrait views load the Pianta too (one extra bake per view).
- Multiplayer: other players' skins follow `body.skinIndex`; not tried with two players.
- The Mithrix unlock trigger.
