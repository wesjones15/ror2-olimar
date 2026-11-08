# ror2-olimar

C# host for [libpikmin](https://github.com/wesjones15/libpikmin), registering **Olimar** as a Risk of Rain 2 survivor. Lead a squad, throw Pikmin onto enemies, whistle them out of danger, and carry pellets back to the Onion to grow replacements.

The native library runs the squad and captain simulation. This plugin connects it to Unity terrain, RoR2 enemies, damage, items, controls, UI and multiplayer. GameCube assets are optional: without them the mod uses its existing stand-ins, a generic captain, default gameplay tuning and a standard RoR2 drop pod. Ship Repair objectives have generic carry definitions. Original models, animation, sound and effects can be prepared from the player's own disc image.

The `modern/1.5.0` branch targets the project's **RoR2 DLC4 build, Unity 2021.3 and BepInEx 5**. The `legacy/1.2.4.4` branch is frozen. For packaged downloads and dependency/install instructions, start with [ror2-characters](https://github.com/wesjones15/ror2-characters).

## Squad and resources

The default opening squad is **10 Red, 5 Yellow and 5 Blue**, with a field cap of 100. Throw Pikmin onto enemies to latch and attack, use swarm commands to direct the formation, and whistle to regroup. The Dismiss Strike combines a close-range hit with a squad charge. Olimar can jump, and followers repeat his takeoff points and jump-pad arcs.

Enemy kills produce pellets. Carry teams bring them to the Onion, which produces seeds; sprouts grow from leaf to bud to flower and can be plucked into the squad. Nectar blooms existing Pikmin. The active squad and stored stock carry between stages; units left outside those groups can be left behind.

Candypop Buds around the map let Olimar obtain Purple and White Pikmin when the optional data is available. Louie starts with 3 Purple and 5 White by default. Color roles matter: Reds resist fire, Yellows gain bonuses against airborne/mechanical targets, Blues benefit in water, Whites poison enemies that eat them, and Purples carry heavy loads and can bring fliers down.

Pikmin are vulnerable to enemy attacks, explosions and shake-offs. Their health and attacks scale through the host's rules and Olimar's stats. Current defaults also let dead Pikmin return as buried leaf sprouts at a configured chance; `Pikmin.RebirthChance` below zero disables that extension.

## Controls

The default flat-screen scheme uses physical gamepad positions with Xbox-style names. Keyboard/mouse actions below refer to **your RoR2 bindings**, except where a literal key is shown. A controls page is also available in the pause menu.

| Gamepad | Keyboard/mouse | Action |
| --- | --- | --- |
| Left stick | Movement | Walk; a light stick tilt adjusts aim. |
| A | Primary | Grab/throw; pluck beside a sprout. |
| B | Secondary | Jump. |
| Hold RT + B | Hold Jump + Secondary | Whistle; the modifier prevents jumping. |
| Hold LT | — | Lock onto an enemy in the default Orbit camera. LT + A throws rapidly; LT + RB cycles targets. |
| RB | `F` | Dismiss Strike / squad charge. |
| RT + right stick | Hold Special | Swarm. |
| D-pad left/right | Mouse wheel | Cycle the color selected for throwing, or Any. |
| Hold X for 0.6 seconds | Hold Utility | Lower/raise the Onion when no interaction prompt owns the press. |
| RT + X | `V` | Release a flying Onion's stock. |
| RT + Y | `B` | Put the squad into a flying Onion. |
| Hold RT + fresh LT press | `Z` | Call the Onion and board it; repeat to cancel the call. |
| RT + RB | Hold `Z` + press `F` | Call the Onion to heal Olimar using stored stock. |
| LB | Jump | Recenter the camera. |
| Right stick click | `C` | Cycle zoom. |
| X / Y | Interact / Equipment | Normal RoR2 actions outside Onion shortcuts. |
| RoR2 sprint | Sprint | Activate sprint behavior and related items. |

The right stick pans the Orbit camera normally and directs the swarm while RT is held. Gamepad LT lock-on requires line of sight and defaults to a 40 m range. To summon the Onion, hold RT and then make a fresh LT press; release an existing LT lock first.

### Onion shortcuts and riding

Interact beneath a ready Onion to access its menu. Beam/menu shortcuts take priority over the corresponding field actions:

| Gamepad | Keyboard/mouse | Onion action |
| --- | --- | --- |
| X | Utility | Release stored Pikmin. |
| Y | Equipment | Store the squad. |
| RB | `F` | Heal Olimar by spending stored Pikmin. |
| LB | `G` | Call stragglers. |
| A | Primary | Send an Onion plucker. |
| LT | Jump | Board: Onion Ride. |

**Onion Ride** is the Utility skill. Board in the beam or summon it from afar; the squad climbs aboard with Olimar. Steer with movement, click the left stick (keyboard: Sprint) for a two-second boost, and press A/B/X/Y (keyboard: Primary/Secondary/Utility/Equipment) to exit. Other field actions are unavailable while riding.

The ride has a short boarding grace followed by a ten-second flight clock. Its default recharge is 30 seconds, starting when the ride ends. The Onion takes damage while Olimar and the boarded squad are protected. Raising/lowering it is a separate free command. See [ride and control details](DOCS/onion-ride.md).

`Controls.Scheme` also offers **Aim**, where RoR2 moves the body and aiming places the cursor. `Auto` chooses the native scheme for flat play and the motion-control layout in supported VR; in VR with `VR.Controls` disabled, it selects Aim. The table above describes the default flat scheme.

## The Onion and items

Each Olimar has an Onion that follows in the air or lands as a base. It stores the squad's colors and growth stages, collects pellets, provides healing by consuming stock, and supports automatic plucking. It is a targetable minion with filtered copies of its owner's items. When defeated it goes dormant, preserving stock, and revives after the configured delay.

The four moves use real RoR2 skill slots so item hooks can observe them:

| Slot | Move |
| --- | --- |
| Primary | Throw / punch. |
| Secondary | Whistle. |
| Utility | Onion Ride. |
| Special | Dismiss Strike. |

Onion pluckers are the passive. Cooldown items also accelerate growth, seeding, regrouping and support timers. Attack-speed and movement items feed into squad behavior; Backup Magazine adds seeds from deliveries; Hardlight adds ride stock. Pikmin hits use Olimar's attacker identity, stats and applicable item hooks, with a default proc coefficient of **0.2** and a per-Pikmin proc interval.

Scaling, limits and current defaults are configured in [`OlimarTest/Plugin.cs`](OlimarTest/Plugin.cs).

## Ship Repair and alternate captains

Select **Ship Repair** from the game-mode menu to recover **30 unique ship parts** and deliver them to the Dolphin. It defaults to competitive races; set `ShipRepair.CompetitiveMode = false` for shared crew progress. Additional enemies, parts, optional treasures and stage music support that mode.

Alternate captains unlock on the player's profile and are offered when their required local assets are available:

| Skin | Unlock | Additional data |
| --- | --- | --- |
| Pianta | Complete a stage 1 or later teleporter event as Olimar. | Sunshine cache prepared by the Mario plugin. |
| Captain Pikachu | Complete a stage 2 or later teleporter event as Olimar. | Melee USA revision 1.02; this plugin handles the extraction. |
| Louie | Recover all 30 unique parts in one Ship Repair run as Olimar. | Pikmin 2 disc; also supplies optional squad/enemy content. |

Shared crew parts count for everyone in co-op; competitive progress is per player. Unlocks can be earned before the extra assets are installed. See the [Pianta](DOCS/pianta-skin.md), [captain skin](DOCS/pikachu-skin.md) and [Louie](DOCS/louie-skin.md) notes for implementation details. Use the setup below for current asset paths.

## First run and configuration

Install the character package with the release's required BepInEx dependencies and play. No disc or asset setup is required. To add the optional original presentation, use **wesleyasaurus-SharedFiles** and:

1. Put your **Pikmin 1 ISO/GCM/CISO** in `BepInEx/plugins/wesleyasaurus-SharedFiles/put_games_here/`. Filenames are arbitrary, and one level of subfolders is supported. Convert other image containers to a supported format first.
2. Launch RoR2 and let the background extraction finish. The shared cache is `wesleyasaurus-SharedFiles/assets/pikmin/`.
3. If the lobby display has not refreshed, select another survivor and then Olimar again.

Put optional Pikmin 2 or Melee images in the same disc folder for the plugin's additional extraction sets. Pianta uses an existing Sunshine cache prepared by the Mario plugin. Complete caches are reused on later launches; keep discs available for extraction refreshes when updating.

The plugin finds SharedFiles automatically. See the [SharedFiles guide](https://github.com/wesjones15/ror2-common/blob/modern/1.5.0/SHARED-FILES.md) for folder layout and extraction behavior.

Configuration is in `BepInEx/config/com.libpikmin.olimartest.cfg`. Main groups cover input/camera (`Controls`), squad and Onion behavior (`Pikmin`), damage and survivability (`Combat`), item rules (`Items`), optional conversion buds (`Candypop`), Ship Repair and presentation/VR. For missing assets or a stand-in model, inspect `BepInEx/LogOutput.log`. Use matching plugin/native DLLs.

## VR and multiplayer

Optional VR integration provides pointer-directed commands, controller arms and a wrist HUD with a compatible active VR mod. The motion layout uses right trigger to throw, right grip to whistle, left grip to swarm, left trigger to lock/board, A to jump and B to strike. The pause sheet shows the full contextual layout; left-handed mode mirrors it. Recent headset behavior still needs manual verification.

The server owns the squad simulation. Clients submit commands, move their own captain locally, and display the world through native snapshot replicas. Peers need matching builds, compatible unfinished-content settings and their own local assets. Use offline play or private friends-only co-op.
## Current status

Manual checks remain for recent optional enemies/treasures, movement/VR changes, extraction/lobby behavior and co-op changes.

Source overview and contributor instructions: [Development](https://github.com/wesjones15/ror2-olimar/blob/modern/1.5.0/DOCS/Development.md).

## License and credits

Original contributions by **wesleyasaurus** are dedicated to the public domain under
[CC0 1.0 Universal](LICENSE), to the extent wesleyasaurus owns the relevant rights.
Attribution for those contributions is appreciated but not required. Third-party software
and original game content retain their respective rights and terms.
See [CREDITS.md](CREDITS.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
