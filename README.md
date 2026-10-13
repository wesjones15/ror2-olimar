# ror2-olimar

Risk of Rain 2 host for [libpikmin](../libpikmin): Olimar and his Pikmin in RoR2.
Targets the 2023 build (1.2.4.4, Unity 2019.4.37, BepInEx 5). Compiles against the reference
copy in `../Risk Of Rain 2 2023` and installs into the Steam game at
`D:\SteamLibrary\steamapps\common\Risk of Rain 2` (falling back to the reference copy if absent).
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
  Pikmin that die. A landed Onion stands in its light cone (it closes as the Onion lifts off
  and opens after it lands); standing at it lights its rings with the game's chime, and a
  pellet arriving draws light up into it. Swarming plays Olimar's formation trumpet. The
  game's sound effects play for whistles, throws, Pikmin and the Onion at
  RoR2's sound effects volume (`Sound.Enabled`, `Sound.Volume`). A short Impact Site sting plays at Dolphin touchdown; RoR2 keeps its soundtrack.
  On the character select screen Olimar stands idle in place of Commando, with a red, a
  yellow and a blue Pikmin, and his portrait is rendered from that model (once your disc's
  files are extracted).
- **Corpses**: a dead enemy leaves a 1, 5, 10 or 20 pellet by its size (`CarriersPerMetre`),
  red, yellow or blue at random, with the disc's size, carriers and seeds. Pikmin carry it to the
  Onion, which turns it into seeds; sprouts grow and are plucked with the throw button.
  `Corpses.CarryBodies` (experimental, off) has them carry the enemy's own body instead.
- **Onion (phase 8)**: one Onion per Olimar stores all three types. Its Red, Yellow or
  Blue appearance is chosen once per run and kept across stages; it has no effect on storage.
  It arrives airborne alongside the Dolphin and follows Olimar. X/interact places it when
  flying; once landed, interact opens its menu. D-pad up / Tab also opens the menu, including
  during flight. Left/right selects Red, Yellow, Blue, **Follow Olimar**, or **Land Here**;
  up/down adjusts transfers and A/left click confirms. Transfers and deliveries wait until
  landed. Chests and other RoR2 interactions have priority. The old `OnionPerColour` setting
  is ignored. `OnionItems` retains filtered owner-item inheritance: hovering is explicitly
  moving, so Bustling Fungus only activates after the Onion has landed and stood still for
  one second, regardless of whether Olimar is moving.
- **Dolphin arrival (phase 7)**: Olimar's drop pod is replaced by a 3.5-second S.S. Dolphin
  landing using the extracted ship model. The Onion descends beside it, then follows Olimar;
  Olimar and his squad appear when it lands. Olimar is protected and controls are held during
  arrival. Indoor stages shorten the descent; stage changes and respawns reuse the same
  lifecycle without duplicating an owner's Onion. This is independent of the future optional
  ship-repair feature: no ship parts or repair objective are required.
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
| B                | secondary           | jump                         |
| LB (held) + B    | jump (held) + secondary | whistle; modifier blocks jumping |
| X                | utility             | land flying Onion; otherwise dismiss |
| d-pad left/right | mouse wheel         | next colour to throw         |
| Y                | equipment           | RoR2's equipment, as usual   |
| right stick      | special (held)      | swarm (toward the cursor)    |
| LT / left trigger | jump               | swing the camera behind him  |
| R                | C                   | zoom (three levels)          |
| RoR2 sprint bind | sprint              | sprint until movement stops  |
| right bumper (Z) |                     | top view                     |
| X under an Onion | interact            | land / open the Onion window |
| d-pad up         | Tab                 | Onion menu (including in flight) |

The camera is a stand-in for Pikmin's: it looks down from behind and only turns when told
to. Pikmin's own camera (cameraMgr.cpp) isn't ported.

With the default Orbit camera, the right stick pans and LB + right stick swarms;
LT recentres, RT locks onto an enemy, and RB cycles locked targets (or zooms when unlocked).
B jumps in both camera modes. Holding LB prevents Olimar from jumping; releasing LB
with B still held does not jump. B still cancels the Onion menu and does not jump on closing it.

A stationary squad jumps in place with Olimar. Moving followers remember each takeoff
point, run to it, and jump there in order. Pikmin can also hop over low obstacles and short
gaps while heading to a formation slot, enemy, hand, or carrying slot. They cannot jump
while lifting or holding an item. Jump height is 40 sim units, using the disc's gravity.
Routes expire after 15 seconds and are cleared when a Pikmin leaves its squad.

Jump pads use RoR2's full launch velocity and gravity. Squad Pikmin run to Olimar's
takeoff point and repeat the same arc; ordinary knockback does not launch the squad.
The flying Onion follows in the air, easing toward a point 3 metres behind Olimar
with a 0.65-second response and a speed limit slightly above his run speed.

Jump presentation uses Olimar's geyser `OCARRY` motion (`NaviGeyzerState::init`) and
Pikmin's airborne `ROLLJMP` motion (`PikiFlyingState::init`). Both are inside the shared
`pikis/bluModel.anm` bundle and `pikis/animMgr.bin`, copied by the existing first-run
extractor from the user's disc. Existing complete extractions already contain them;
no animation data is shipped with the mod. Normal locomotion resumes on landing.

**Aim** (for VR): RoR2 moves Olimar as usual and the Pikmin cursor is wherever you aim.
Primary throws, secondary whistles, utility dismisses, special swarms toward the cursor.

### How it works

- `pikmin.dll` is `libpikmin/crates/libpikmin-ffi`, the sim behind a C ABI (`pik_*`, ABI 11).
  `Native.Load` refuses a DLL whose ABI version or struct sizes don't match.
- `PikminWorld` owns the sim on the server, one per stage. Each `FixedUpdate` it passes every
  captain's input and RoR2's enemies, advances the 30 Hz clock, and reads back Pikmin,
  pellets, Onions, sprouts and events. RoR2 events raised during a sim step (a Pikmin hit
  that kills) wait until the step is done.
- `OlimarDriver` (on the body) is a captain. In the Pikmin scheme it turns off the
  KinematicCharacterMotor and puts the body where the sim has Olimar, interpolated between
  ticks with ror2-common's `StepSmoother`, before RoR2 updates its camera. After a sim tick
  the body stays at the previous position until that frame's blend, so the camera never
  reads ahead of the drawn Olimar. RoR2's own network transform shows that to others.
  The native view receives `ViewFrame.lag = StepSmoother.LagTicks(RenderAlpha, 1)` on
  the same 30 Hz clock: positions, facing and sim-driven animation frames blend together.
  This prevents camera stutter across the scene and model ghosting at high refresh rates.
  The lobby supplies its own sim fraction. Install both DLLs together; older ABIs are incompatible.
- In the Pikmin scheme, `NativeBody` imports external velocity, applies roots/stuns,
  manages sprint state, and reports native throws/punches for combat and primary-use items.
  Native attacks also refresh the attacker's combat clock. Aim/VR retains RoR2's motor.
- Goobo Jr.'s Olimar (an AI master that only walks him: ror2-common `SurvivorAi.Drivers`) has a
  temporary squad and is drawn in RoR2's gummy clone material (`Look.GummyClones`; the squad
  isn't). `OlimarBrain` works his controls like MarioTest's `MarioBrain`: within throwing range
  and in sight he locks onto the enemy (the sim homes thrown Pikmin on it) and throws one Pikmin
  at a time, punching once close if none are left; with no enemy he whistles at his nearest
  idle Pikmin to call them back, and never while there is one.
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
Use `-RoR2Dir` for different compilation references, `-InstallDir` for another deployment
folder, and `-NoInstall` to only build into `dist/`. The reference copy and the game you
launch can be different folders; check the installed DLLs and the launched game's log.

`NativeCheck/` exercises `pikmin.dll` through the plugin's own `Native.cs` outside the game
(struct layouts, callbacks, arrays):

```
dotnet run -c Release --project NativeCheck -- ..\libpikmin\target\release\pikmin.dll ..\libpikmin\assets
```

This includes render-clock checks at 60, 120 and 165 Hz, facing interpolation, a paused
frame and invalid lag. In-game verification is manual: walk, jump and turn while watching
Olimar, Pikmin, enemies and stationary scenery, then check pause/resume and a stage change.

The log, `BepInEx/LogOutput.log`, should show `libpikmin ABI 11`, `Olimar (test) registered`,
and in a run `Pikmin sim started (disc (10 files))`, `Pikmin models: 19 models; sound 48000 Hz,
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
- Enemies do not directly target Pikmin; attacks shake eligible latched Pikmin off.
- Hazards (fire, water, electricity), so colours only differ in attack and carrying.
- Attack speed items don't change how fast Pikmin swing.
- Carry paths are straight lines; corpses get stuck on walls between them and the Onion.
- The Onion window doesn't pause the game (Olimar just stands still while it's open), and it's
  RoR2's IMGUI, not Pikmin's own window art. It only opens for the host's Olimar.
- The baked materials, see-through helmet, carried corpses and particles haven't been run in
  the game yet.

Offline / friends-only co-op only.

### Phase 8 / phase 7 validation

Automated checks cover flight blocking transfers, preserving all three stored colours,
resuming transfers after landing, cosmetic colour selection, and drawing the Dolphin and
Onion both airborne and landed without ship parts. `NativeCheck` exercises the new ABI from
C#. Both survivor plugins compile against the installed RoR2 1.2.4.4 assemblies.

In-game acceptance still needs a play session: start Olimar with Bustling Fungus, confirm
Dolphin arrival and one airborne Onion; stop Olimar and confirm no flying healing field;
land the Onion and move away to confirm its field appears; resume follow and confirm the
field disappears. Test each colour's deposit/withdrawal, interact at a chest while following,
and move to the next stage / respawn to check retained stock, colour and entity counts.
Presentation and the Onion menu retain the existing host/single-player limitation.

### Extracted flight animations (2026-10-08)

Follow mode loops an airborne section of the Onion's `c_blue_t`, `c_red_t`, or `c_yell_t`
clip, with a short blend at the loop seam. Landing uses its corresponding `pm_blu_d`,
`pm_red_d`, or `pm_yel_d` clip over 2.5 seconds; follow from the ground plays a 1.8-second
takeoff. The native animation supplies articulated poses while the host owns position,
preventing the cinematic root movement from adding hundreds of metres to the follow path.

The Dolphin's `pm_st_l1` rocket motion plays backward over the 3.5-second arrival, using
`ShipAnimator`'s landing support, engine flame effects, rocket audio, and extracted landing
dust. The normal Dolphin animation set has takeoff clips rather than a separate landing
clip. Models, motions and effects come from the existing extracted user-disc assets.

Regression checks verify changing vertex poses for all Onion colours and the Dolphin,
finite geometry through the follow-loop seam, bounded cinematic displacement, rocket
particles, and rocket sound start/stop. In-game visual acceptance is still manual.

## Arrival music

Phase 6: the first 3.5 seconds of Impact Site (tutorial.jam) play once per local body when DolphinArrival ends. Stage arrivals and respawns can play it again. SpawnMusic.Enabled and SpawnMusic.Volume control it; master and music volume also apply. The build synthesizes spawn.wav from your extracted disc audio into the ignored dist folder. Rebuild after extracting assets if the clip is missing. No music data is committed.
