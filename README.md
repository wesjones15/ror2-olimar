# ror2-olimar

C# host for [libpikmin](https://github.com/wesjones15/libpikmin), registering **Olimar** as a Risk of Rain 2 survivor. Lead a squad, throw Pikmin onto enemies, whistle them out of danger, and carry pellets back to the Onion to grow replacements.

The native library runs the squad and captain simulation. This plugin connects it to Unity terrain, RoR2 enemies, damage, items, controls, UI and multiplayer. Models, animation, sound and other runtime assets are extracted locally from the player's own disc image; they are not included in the source or releases.

The `modern/1.5.0` branch targets the project's **RoR2 DLC4 build, Unity 2021.3 and BepInEx 5**. The `legacy/1.2.4.4` branch is frozen. For packaged downloads and dependency/install instructions, start with [ror2-characters](https://github.com/wesjones15/ror2-characters).

## Squad and resources

The default opening squad is **10 Red, 5 Yellow and 5 Blue**, with a field cap of 100. Throw Pikmin onto enemies to latch and attack, use swarm commands to direct the formation, and whistle to regroup. The Dismiss Strike combines a close-range hit with a squad charge. Olimar can jump, and followers repeat his takeoff points and jump-pad arcs.

Enemy kills produce pellets. Carry teams bring them to the Onion, which produces seeds; sprouts grow from leaf to bud to flower and can be plucked into the squad. Nectar blooms existing Pikmin. The active squad and stored stock carry between stages; units left outside those groups can be left behind.

**Olimar can obtain Purple and White Pikmin from Candypop Buds around the map**, with the optional data available. They are not restricted to Louie: Louie additionally starts with 3 Purple and 5 White by default. Color roles matter: Reds resist fire, Yellows gain bonuses against airborne/mechanical targets, Blues benefit in water, Whites poison enemies that eat them, and Purples carry heavy loads and can bring fliers down.

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

The right stick pans the Orbit camera normally and directs the swarm while RT is held. LT lock-on requires line of sight and defaults to a 40 m range. Keyboard does not have a separate lock-on control in this scheme. Release LT and tap it again after holding RT to summon the Onion; pressing RT while LT is already held does not summon it.

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

Onion pluckers are the passive, not the Special skill. Cooldown items also accelerate growth, seeding, regrouping and support timers. Attack-speed and movement items feed into squad behavior; Backup Magazine adds seeds from deliveries; Hardlight adds ride stock. Pikmin hits use Olimar's attacker identity, stats and applicable item hooks, with a default proc coefficient of **0.2** and a per-Pikmin proc interval.

These are adapter rules, with configurable scaling and limits rather than a promise that every item has been playtested. Current defaults are in [`OlimarTest/Plugin.cs`](OlimarTest/Plugin.cs).

## Ship Repair and alternate captains

Select **Ship Repair** from the game-mode menu to recover **30 unique ship parts** and deliver them to the Dolphin. It defaults to competitive races; set `ShipRepair.CompetitiveMode = false` for shared crew progress. Additional enemies, parts, optional treasures and stage music support that mode. The old `Dolphin.ShipPartsMode` switch is retired.

Alternate captains unlock on the player's profile and are offered when their required local assets are available:

| Skin | Unlock | Additional data |
| --- | --- | --- |
| Pianta | Complete a stage 1 or later teleporter event as Olimar. | Sunshine cache prepared by the Mario plugin. |
| Captain Pikachu | Complete a stage 2 or later teleporter event as Olimar. | Melee USA revision 1.02; this plugin extracts the required files, without needing the Melee plugin. |
| Louie | Recover all 30 unique parts in one Ship Repair run as Olimar. | Pikmin 2 disc; also supplies optional squad/enemy content. |

Shared crew parts count for everyone in co-op; competitive progress is per player. Unlocks can be earned before the extra assets are installed. See the [Pianta](DOCS/pianta-skin.md), [captain skin](DOCS/pikachu-skin.md) and [Louie](DOCS/louie-skin.md) notes for implementation details; older path instructions in those notes are historical.

## First run and configuration

Install the character package and **wesleyasaurus-SharedFiles** with the release's required BepInEx dependencies. Then:

1. Put your **Pikmin 1 ISO/GCM/CISO** in `BepInEx/plugins/wesleyasaurus-SharedFiles/put_games_here/`. Filenames are arbitrary, and one level of subfolders is supported. Convert other image containers to a supported format first.
2. Launch RoR2 and let the background extraction finish. The shared cache is `wesleyasaurus-SharedFiles/assets/pikmin/`.
3. If the lobby display has not refreshed, select another survivor and then Olimar again.

Put optional Pikmin 2 or Melee images in the same disc folder for the plugin's additional extraction sets. Pianta uses an existing Sunshine cache prepared by the Mario plugin. Complete caches are reused on later launches; keep discs available for extraction refreshes when updating. Original game data and generated caches are not included when sharing the mod.

Current builds find SharedFiles automatically. Older `Paths.*`, skin disc-path settings and `assets_dir.txt` instructions are superseded. See the [SharedFiles guide](https://github.com/wesjones15/ror2-common/blob/modern/1.5.0/SHARED-FILES.md).

Configuration is in `BepInEx/config/com.libpikmin.olimartest.cfg`. Main groups cover input/camera (`Controls`), squad and Onion behavior (`Pikmin`), damage and survivability (`Combat`), item rules (`Items`), optional conversion buds (`Candypop`), Ship Repair and presentation/VR. For missing assets or a stand-in model, inspect `BepInEx/LogOutput.log`. Use matching plugin/native DLLs.

## VR and multiplayer

Optional VR integration provides pointer-directed commands, controller arms and a wrist HUD with a compatible active VR mod. The motion layout uses right trigger to throw, right grip to whistle, left grip to swarm, left trigger to lock/board, A to jump and B to strike. The pause sheet shows the full contextual layout; left-handed mode mirrors it. Recent headset behavior still needs manual verification.

The server owns the squad simulation. Clients submit commands, move their own captain locally, and display the world through native snapshot replicas. Peers need matching builds, compatible unfinished-content settings and their own local assets. Headless protocol checks do not establish complete co-op behavior; use offline play or private friends-only co-op.

## Repository relationships and development

| Repository | Role |
| --- | --- |
| **ror2-olimar** (this repository) | C# survivor/mode registration and world, combat, item, input, UI and network adapters. The DLL retains its historical name, `OlimarTest.dll`. |
| [libpikmin](https://github.com/wesjones15/libpikmin) | Native simulation, enemies, extraction, presentation, audio and the `pikmin.dll` API. |
| [ror2-common](https://github.com/wesjones15/ror2-common) | Shared C# systems compiled into the plugin. |
| [libcommon](https://github.com/wesjones15/libcommon), [libsms](https://github.com/wesjones15/libsms), [libssbm](https://github.com/wesjones15/libssbm) | Native source dependencies for shared utilities and presentation/asset support. |
| [ror2-characters](https://github.com/wesjones15/ror2-characters) | Package assembly and releases. |

Build on Windows with PowerShell 5.1, Rust supporting edition 2024, a .NET SDK and local DLC4/BepInEx assemblies. The plugin targets `netstandard2.1`; console checks target .NET 6. Keep the repositories above as siblings: use `modern/1.5.0` for the host, native library, Common and other Rust dependencies, and `main` for `libssbm`.

From this repository:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -RoR2Dir "C:\path\to\Risk of Rain 2" -NoInstall
```

This builds `pikmin.dll` and `OlimarTest.dll` into `dist/OlimarTest/`, without requiring extracted assets. Without `-NoInstall`, the script also stages the plugin and SharedFiles beneath a workspace `target/BepInEx/plugins/` directory; `-InstallDir` selects the staging root. Copy staged files into the intended game/profile yourself. The default compile path is the workspace's `2026 Mod Stuff/RiskOfRain2_DLC4` copy.

Example headless checks:

```powershell
dotnet run --project tests/OnionMenuCheck -c Release
dotnet run --project tests/SkinUnlockCheck -c Release
```

Run native checks from `libpikmin/` with `cargo test --release`. Some boundary checks need the built native library and local assets. Builds and tests cover specific contracts; they do not prove balance, final appearance or in-game behavior. Recent optional enemies/treasures, movement/VR changes and extraction/lobby changes include paths still awaiting manual checks.

Further reading: [native behavior spec](https://github.com/wesjones15/libpikmin/blob/modern/1.5.0/docs/behavior-spec.md), [gameplay notes](DOCS/2026-10-30-gameplay.md), [enemy integration](DOCS/pikmin-enemies.md), and [shared multiplayer architecture](https://github.com/wesjones15/ror2-common/blob/modern/1.5.0/MULTIPLAYER.md). Historical notes can retain old defaults; current source takes precedence.

## License and credits

Original contributions by **wesleyasaurus** are dedicated to the public domain under
[CC0 1.0 Universal](LICENSE), to the extent wesleyasaurus owns the relevant rights.
Attribution for those contributions is appreciated but not required. Third-party software
and original game content retain their respective rights and terms.
See [CREDITS.md](CREDITS.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
