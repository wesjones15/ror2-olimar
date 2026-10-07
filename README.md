# ror2-olimar

Risk of Rain 2 host for [libpikmin](../libpikmin): Olimar and his Pikmin in RoR2.
Targets the 2023 build (1.2.4.4, Unity 2019.4.37, BepInEx 5) in `../Risk Of Rain 2 2023`.
Modelled on [ror2-mario](../ror2-mario); the two can be installed side by side (different
DLLs, GUIDs and tokens).

## OlimarTest (first test character)

A survivor called **Olimar (test)**: Commando's body and HUD leading a squad of Pikmin from
libpikmin, drawn with Pikmin's own models and animations from your disc. Commando's skills
never fire; Olimar fights with Pikmin.

What works:
- **Squad**: you start a run with 10 Red, 5 Yellow and 5 Blue Pikmin (configurable) that
  follow in Pikmin's formation, get whistled, thrown and dismissed.
- **Fighting**: RoR2's enemies are creatures to the sim, their hurtboxes the parts Pikmin
  latch onto. Pikmin hits are RoR2 damage from Olimar: attack power x `DamageCoefficient`
  x Olimar's damage stat, with on-hit items at a low proc coefficient.
- **Getting hurt**: an enemy's hit on anything also hurts the Pikmin around where it landed.
  A Pikmin's health is `PikminHealth` (half) of Olimar's max health, so it grows with his
  level and health items. Olimar has twice Commando's health (`OlimarHealth`,
  `OlimarHealthPerLevel`). Pikmin that fall into a void are lost.
- **Looks and sounds**: Olimar, the Pikmin (with leaf, bud or flower), the Onion, pellets,
  sprouts and the cursor are the game's models, posed by its animations and lit by RoR2
  (`HGStandard`), in place of Commando's model. The game's sound effects play for whistles,
  throws, Pikmin and the Onion at RoR2's sound effects volume (`Sound.Enabled`,
  `Sound.Volume`). Pikmin's music never plays.
- **Corpses**: a dead enemy leaves a stand-in pellet sized to it. Pikmin carry it to the
  Onion, which turns it into seeds; sprouts grow and are plucked with the throw button.
- **Onion**: one Onion for all three colours is placed where Olimar lands (or one per colour,
  `OnionPerColour`). Stand under it for its menu: d-pad / arrow keys up calls 5 Pikmin out,
  down puts 5 away, left/right picks the colour.
- **Stages**: the squad and the Onion's stock come along to the next stage; Pikmin left
  anywhere else stay behind, as at sunset.
- **First run**: if `assets/` isn't there, the plugin builds it from your own disc
  (`Paths.Disc` in the config). Tuning comes from the disc; without it the sim falls back to
  code defaults and says so in the log.

### Controls

`Controls.Scheme` picks one. `Auto` uses Aim in VR (VRMod running in VR), Pikmin otherwise.

**Pikmin** (default): the sim moves Olimar, as in Pikmin. The pad is read by button
position, so your RoR2 binds don't change it:

| Pad              | Keyboard and mouse  | Does                         |
|------------------|---------------------|------------------------------|
| left stick       | move                | walk; a light tilt aims      |
| A                | primary             | throw, pluck                 |
| B                | secondary           | whistle                      |
| X                | utility             | dismiss                      |
| right stick      | special (held)      | swarm (toward the cursor)    |
| L / left bumper  | jump                | swing the camera behind him  |
| R                | sprint              | zoom (three levels)          |
| right bumper (Z) |                     | top view                     |
| d-pad            | arrow keys          | Onion menu                   |

The camera is a stand-in for Pikmin's: it looks down from behind and only turns when told
to. Pikmin's own camera (cameraMgr.cpp) isn't ported.

**Aim** (for VR): RoR2 moves Olimar as usual and the Pikmin cursor is wherever you aim.
Primary throws, secondary whistles, utility dismisses, special swarms toward the cursor.

### How it works

- `pikmin.dll` is `libpikmin/crates/libpikmin-ffi`, the sim behind a C ABI (`pik_*`, ABI 1).
  `Native.Load` refuses a DLL whose ABI version or struct sizes don't match.
- `PikminWorld` owns the sim on the server, one per stage. Each `FixedUpdate` it passes every
  captain's input and RoR2's enemies, advances the 30 Hz clock, and reads back Pikmin,
  pellets, Onions, sprouts and events. RoR2 events raised during a sim step (a Pikmin hit
  that kills) wait until the step is done.
- `OlimarDriver` (on the body) is a captain. In the Pikmin scheme it turns off the
  KinematicCharacterMotor and puts the body where the sim has Olimar, interpolated between
  ticks, the way MarioTest does for Mario. RoR2's own network transform shows that to others.
- `UnityHost` answers the sim's ground and wall queries with raycasts against RoR2's world
  layer. 1 Pikmin unit = 4 cm (`Sim.MetresPerUnit`): Olimar runs at 6.4 m/s.
- `ModelView` draws with libpikmin's view (`pik_view_*`, from `libpikmin-present`): one mesh
  per model built at load, then each frame an object per posed instance with the vertices
  libpikmin skinned for it, and per batch the colour libpikmin got by running that material's
  TEV stages on the CPU (an Onion's colour is a register animation). Translucent materials are
  drawn as cutouts, as HGStandard is deferred. The same view plays the sounds: RoR2's own audio
  is Wwise (Unity's audio is off), so libpikmin mixes them and plays them through Windows on a
  thread of its own. Without the assets, `PikminView` falls back to Unity primitives.

### Build and install

Needs the Rust toolchain, the .NET SDK, and libpikmin's `assets/` (or your Pikmin disc for
the plugin to extract from on first run).

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script builds `pikmin.dll` and `OlimarTest.dll`, then copies them, plus an
`assets_dir.txt` pointing at `libpikmin/assets`, into `<game>/BepInEx/plugins/OlimarTest/`.
Use `-RoR2Dir` for another install and `-NoInstall` to only build into `dist/`.

`NativeCheck/` exercises `pikmin.dll` through the plugin's own `Native.cs` outside the game
(struct layouts, callbacks, arrays):

```
dotnet run -c Release --project NativeCheck -- ..\libpikmin\target\release\pikmin.dll ..\libpikmin\assets
```

The log, `BepInEx/LogOutput.log`, should show `libpikmin ABI 2`, `Olimar (test) registered`,
and in a run `Pikmin sim started (disc (10 files))`, `Pikmin models: 15 models; sound 48000 Hz,
2 channels`, `captain 0 is ...`, then latches, deaths, corpses and deliveries as they happen
(turn off with `Debug.LogEvents`).

### Not there yet

- **Multiplayer**: the Pikmin only exist on the host. A client playing Olimar walks like
  Commando without skills, and clients don't see the host's Pikmin. Every player needs the
  plugin installed anyway (it adds a survivor).
- Clients see Commando, not Olimar's model, and hear none of Pikmin's sounds (the view runs
  where the sim does).
- Pikmin's particle effects (whistle sparkles, the ghost of a Pikmin that died) and Olimar's
  footsteps and voice.
- Enemies don't target Pikmin or shake them off; only hits that land on something hurt them.
- Hazards (fire, water, electricity), so colours only differ in attack and carrying.
- Carry paths are straight lines; corpses get stuck on walls between them and the Onion.
- The Onion menu works in steps of 5 and doesn't pause the game.
- The models and sounds haven't been run in the game yet.

Offline / friends-only co-op only.
