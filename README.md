# ror2-olimar

Risk of Rain 2 host for [libpikmin](../libpikmin): Olimar and his Pikmin in RoR2.
Targets the 2023 build (1.2.4.4, Unity 2019.4.37, BepInEx 5) in `../Risk Of Rain 2 2023`.
Modelled on [ror2-mario](../ror2-mario); the two can be installed side by side (different
DLLs, GUIDs and tokens). Both compile in [ror2-common](../ror2-common)'s shared C# (model
drawing, the character select display, prefab cloning, content registration, world probes),
which must sit next to this folder to build.

## OlimarTest (first test character)

A survivor called **Olimar (test)**: Commando's body and HUD leading a squad of Pikmin from
libpikmin, drawn with Pikmin's own models and animations from your disc. Commando's skills
never fire; Olimar fights with Pikmin.

What works:
- **Squad**: you start a run with 10 Red, 5 Yellow and 5 Blue Pikmin (configurable) that
  follow in Pikmin's formation, get whistled, thrown and dismissed.
- **Fighting**: RoR2's enemies are creatures to the sim, their hurtboxes the parts Pikmin
  latch onto. Pikmin hits are RoR2 damage from Olimar: attack power x `DamageCoefficient`
  x Olimar's damage stat, with on-hit items at a low proc coefficient. A Pikmin's hit is
  measured from the Pikmin, not Olimar (Focus Crystal's range).
- **Items**: Olimar's move speed items (Goat Hoof, Energy Drink...) speed up his run in the
  sim. Each Onion is his minion with his items (`OnionItems`): it can't be hurt or targeted,
  and items that work while you stand still (Bustling Fungus) work around it. Tesla Coil and
  Mired Urn are left off it.
- **Getting hurt**: an enemy's hit on anything also hurts the Pikmin around where it landed.
  A Pikmin's health is `PikminHealth` (half) of Olimar's max health, so it grows with his
  level and health items. Olimar has twice Commando's health (`OlimarHealth`,
  `OlimarHealthPerLevel`). Pikmin that fall into a void are lost.
- **Looks and sounds**: Olimar, the Pikmin (with leaf, bud or flower), the Onion, pellets,
  sprouts and the cursor are the game's models, posed by its animations and lit by RoR2
  (`HGStandard`), in place of Commando's model, with the whistle's sparkles and the ghosts of
  Pikmin that die. The game's sound effects play for whistles, throws, Pikmin and the Onion at
  RoR2's sound effects volume (`Sound.Enabled`, `Sound.Volume`). Pikmin's music never plays.
  On the character select screen Olimar stands idle in place of Commando, with a red, a
  yellow and a blue Pikmin, and his portrait is rendered from that model (once your disc's
  files are extracted).
- **Corpses**: a dead enemy leaves a 1, 5, 10 or 20 pellet by its size (`CarriersPerMetre`),
  red, yellow or blue at random, with the disc's size, carriers and seeds. Pikmin carry it to the
  Onion, which turns it into seeds; sprouts grow and are plucked with the throw button.
  `Corpses.CarryBodies` (experimental, off) has them carry the enemy's own body instead.
- **Onion**: one Onion for all three colours is placed where Olimar lands (or one per colour,
  `OnionPerColour`). Under it RoR2's interact prompt says "Open the Onion"; interact opens
  its window: up/down sets how many Pikmin come out (or go in), left/right picks the colour of
  a shared Onion, A (left click) confirms, B (right click) cancels.
- **Size**: Olimar is two thirds of Commando's height (`Scale.OlimarHeight`; try 0.75), and
  the Pikmin, Onion and pellets scale with him. His body's collision capsule and hurtboxes
  shrink to match.
- **Throwing**: throws reach twice as far as in Pikmin (`Controls.ThrowReach`). D-pad left
  or right (or the mouse wheel) picks the next colour to throw, Red, Yellow then Blue, skipping colours not
  in the squad, as in Pikmin 2. The chips above the skill bar show the squad by colour, the
  picked one outlined; until you pick, he throws the nearest Pikmin, as in Pikmin 1.
- **Rejoining**: Pikmin that get separated (done carrying or fighting, thrown with nothing
  to do, left behind) run back to Olimar by themselves (`Pikmin.AutoRejoin`). Dismissed
  Pikmin still wait to be whistled.
- **Shake-off**: when an enemy starts an attack (any of its combat skills), the Pikmin on it
  fly off, fall, get up and rejoin; Pikmin latched for less than 2 s hold on
  (`Pikmin.ShakeProtection`) and stay on until a later attack. RoR2's enemies have no
  shake-off move, so their attacks stand in for Pikmin 1's flick.
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
| d-pad left/right | mouse wheel         | next colour to throw         |
| Y                | equipment           | RoR2's equipment, as usual   |
| right stick      | special (held)      | swarm (toward the cursor)    |
| L / left bumper  | jump                | swing the camera behind him  |
| R                | sprint              | zoom (three levels)          |
| right bumper (Z) |                     | top view                     |
| X under an Onion | interact            | open the Onion's window      |

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
- `PikminModels` draws with libpikmin's view (`pik_view_*`, from `libpikmin-present`) through
  ror2-common's `ModelView`: one mesh per model built at load, then each frame an object per
  posed instance with the vertices libpikmin skinned for it, and per batch a texture libpikmin baked by running that material's
  TEV stages on the CPU per texel (a leaf's green is a second texture; a pellet gets one per
  colour). See-through materials (Olimar's helmet) use RoR2's Cloud Remap effect shader, as
  HGStandard is deferred. `Particles` (Pikmin's own, so not in ror2-common) draws libpikmin's
  camera-facing quads, each effect's GX colour mode split into additive texture x colour
  layers, also with Cloud Remap. `OlimarDisplay` is the character select screen's Olimar:
  ror2-common's `DisplayModel` on Commando's display, drawing a silent view of a sim with just
  Olimar standing idle on a flat floor, no cursor.
  `Corpses` keeps a dead enemy's model from RoR2's corpse clean-up and, once its pellet is
  lifted, freezes the ragdoll and moves it with the pellet. The same view plays the sounds: RoR2's own audio
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
- Olimar's footsteps and voice, and particle effects other than the whistle and ghosts.
- With `CarryBodies`, clients see a corpse lie where it fell, not carried.
- Enemies don't target Pikmin or shake them off; only hits that land on something hurt them.
- Hazards (fire, water, electricity), so colours only differ in attack and carrying.
- Attack speed items don't change how fast Pikmin swing.
- Carry paths are straight lines; corpses get stuck on walls between them and the Onion.
- The Onion window doesn't pause the game (Olimar just stands still while it's open), and it's
  RoR2's IMGUI, not Pikmin's own window art. It only opens for the host's Olimar.
- The baked materials, see-through helmet, carried corpses and particles haven't been run in
  the game yet.

Offline / friends-only co-op only.
