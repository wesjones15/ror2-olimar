# Olimar's Pianta alt-skin

A Sunshine Pianta (from the user's own Sunshine disc) drawn in place of Olimar. His sim, Pikmin,
whistle and controls stay Olimar's; only the body, its animation source and its voice change.
Started from codex's viewer demo (`libsms/docs/peach-demo.md`, "Pianta and Piantissimo"), which posed
a Pianta on Mario's motion in the libsms viewer only.

## Skin numbering

Native ids (`OlimarSkin`): **0 Olimar, 1 Louie (`louie-skin.md`), 2 Pianta, 3 Captain Pikachu (`pikachu-skin.md`)**.
The loadout lists the ones that are available, so the Pianta's loadout index depends on whether Louie is offered; his
native id does not. Commando's placeholder skins (inherited from the cloned body) are gone: `OlimarSkin.Register`
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
| walking, running, fleeing | the Pianta's own `mom_walk` / `mom_run`, phase taken from Olimar's walk loop |
| jumping | Mario's `ma_jump`; a jump started at 75% of top speed or more plays `ma_broad_jump` (the long jump); a jump restarted in the air plays `ma_2jmp1/2` then, the next time, `ma_tjmp1/2` |
| ground pound (stomp) | Mario's `hipsr` (wind-up) then `hipat` (fall) |
| dismiss strike | Mario's `sldct` (the dive) |
| landing after more than 0.25 s in the air, when not running on | `jmped`, `2jmed` after a double jump, `hiped` after a pound |
| off a ledge | the end of `ma_jump` held |
| throw, pluck, whistle, dismiss, punch, damage, knockdown | Olimar's own clip, retargeted (`OLIMAR_ALIASES`) |

The palm (`yashi_jnt`), its leaves and the nose always play the Pianta's own clip (idle, or his walk and
run while moving), since Olimar and Mario have nothing for them. No FLUDD. His lowest skinned vertex is put
on the ground each frame. Olimar's rig is 12 unnamed joints with no elbows or knees, so the forearms,
hands, shins and feet follow their upper limb rigidly in Olimar-clip poses.

The straw skirt deforms through Sunshine's runtime `TMtxTimeLag` callback on
`koshi_null`, the parent of `mino_null`. The earlier claim that it follows the
hips stiffly in the game was incorrect: checking the BCK rotation alone missed
this callback. Rust now reads the disc's `params_npc/montem/mtxeffect1.prm`,
tracks the waist's world position and rotation independently per captain, and
applies the correction before envelope skinning. The invented rigid sway is removed.

While locked onto a live enemy, the palm and leaves blend into Sunshine's
`mom_talk.bck` tracks, perking up as during conversation. The body keeps its
current action. Unlocking or losing the enemy blends the plant back; pellet
locks do not trigger it. The blend duration comes from `npcnormal.prm`.
See `libsms/docs/pianta-waist.md` for source citations and the headless viewer proof.

## Sounds he makes by himself

His walk and run clips carry sound keys (`bas/mom_walk.bas`, `mom_run.bas`: `MSD_SE_NPC_MONTE_M_WALK`, the footstep, twice a
cycle); they play as the stride passes them (`PiantaSkin::cues`, `pik_view_update` plays them on his Sunshine sound output).
Standing still he has no sound keys, so there is no idle chatter; his other clips' keys (happy, mad, throw...) are unused.

## Height and colour

`Skins.PiantaHeightRatio` (default 1.25): his height to the top of the palm as a multiple of Olimar's 25.1 sim units; the host
calls `pik_view_set_pianta_height`. (It replaces `Skins.PiantaHeight` in metres, whose default left a stale value in older
config files.) `Skins.PiantaColour` (default 1): the male body colour table index; 1 bakes blue (periwinkle), 0 teal, 8 pale
blue, 7 the game's usual first Pianta, -1 also picks that. His hitbox is still Olimar's.

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
