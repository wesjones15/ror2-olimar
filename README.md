> feature/ship-part-mode: Olimar 0.3.4 preview for RiskOfRain2_DLC4 (Unity 2021.3.45f2), native ABI 33 / repair protocol 3. Ship Repair shows only Olimar in the survivor picker and selects him when entering the mode; changing modes restores the normal roster. It defaults to competitive Dolphin races; `[ShipRepair] CompetitiveMode = false` enables shared repair. Ten map parts per stage (0.3.9; was two), one stage-boss part, seven other enemy drops per player (plus a 4% common-enemy chance) and a 20% bonus-drop chance after the quota. Ordinary enemy drops have independent 30-second player cooldowns (`EnemyPartIntervalSeconds`) and 25-metre clear-site spacing. Duplicate parts feed the owner's Onion for seeds and return for rivals. Dolphins leave after drop-off and dock near the teleporter, with separated multiplayer landing sites. Impact Site plays on stage one, seeded Pikmin stage music later, original enemy-proximity layers respond to RoR2 enemies, and teleporter fights retain RoR2 boss music. Build/API validation passes; in-game validation is pending. Legacy instructions below describe the previous release.

# ror2-olimar

Olimar's alternate skins unlock permanently on your profile, at any difficulty:
Pianta after stage 1's teleporter event, Captain Pikachu after stage 2's teleporter
event, and Louie after recovering all 30 unique ship parts in one Ship Repair run.
Co-op crew parts count for everyone; competitive races use each player's own parts.
Dead teammates still qualify. The old `Skins.PiantaUnlocked` testing override is
retired. Skins appear only when their required Sunshine, Melee or Pikmin 2 assets
are available; you can earn their unlocks before installing those assets.

Olimar's five approved skill portraits render in Unity after first-run disc extraction
and persist as 256x256 BMPs under the resolved `assets/skill-icons/approved-v3/` folder.
Later starts reuse them; missing or corrupt cards regenerate. The sprout is buried in
a shallow soil mound on purple, the original airborne throw is close on light blue,
whistle effects use navy, the flying Onion uses blue, and the close dismiss pose has
twelve charging Pikmin on grass green. All use the native game models and effects.

Native scene/ABI checks: `dotnet run --project tests/PortraitCheck -c Release --
../libpikmin/target/release/pikmin.dll ../libpikmin/assets`. These verify the sprout and
leaf, airborne throw, whistle/Onion particles and charging squad. Generated images
and extracted artwork stay outside Git and releases. Unity appearance still needs
manual in-game review.

Cache revision v3 regenerates cards affected by another plugin's render objects.
The shared capture now excludes every renderer outside the active portrait hierarchy,
including Mario's spray awaiting frame-end destruction; whistle's native effects are
unchanged. The isolation regression passes; corrected in-game appearance is pending.

Modern 0.2.4: native-control fall recovery now uses common Unstuck.FellEarly at
Mario's TOO BAD onset: more than 20m below last safe ground with no floor within
2000m, or a valid stage return zone. Lower platforms remain valid landing targets.
Olimar and his squad return together; his Dolphin/Onion arrival stays unchanged.
Build/API and common detector validation pass; in-game validation is pending.
All future deploys stage under ../target/BepInEx/plugins/OlimarTest/; the user
copies files to their game installation. Older direct-install examples below
are historical.

Risk of Rain 2 host for [libpikmin](../libpikmin): Olimar and his Pikmin in RoR2.
Targets the 2023 build (1.2.4.4, Unity 2019.4.37, BepInEx 5). Compiles against the reference
copy in `../Risk Of Rain 2 2023` and installs into the Steam game at
`D:\SteamLibrary\steamapps\common\Risk of Rain 2` (falling back to the reference copy if absent).
Modelled on [ror2-mario](../ror2-mario); the two can be installed side by side (different
DLLs, GUIDs and tokens). Both compile in [ror2-common](../ror2-common)'s shared C# (model
drawing, the character select display, prefab cloning, content registration, world probes),
which must sit next to this folder to build.

## OlimarTest (first test character)

A survivor called **Olimar**: Commando's body and HUD leading a squad of Pikmin from
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
  sim. Each Onion is his targetable minion with his items (`OnionItems`),
  and items that work while you stand still (Bustling Fungus) work around it. Tesla Coil and
  Mired Urn are left off it.
- **Getting hurt**: an enemy's hit on anything also hurts the Pikmin around where it landed.
  A Pikmin's health is `PikminHealth` (half) of Olimar's max health, so it grows with his
  level and health items. A melee swing catches at most `PikminMeleeMaxVictims` (2, +2 for
  champions and elites), nearest the swing first, so the Pikmin on the attacker take it and
  the squad behind them does not; each takes `PikminMeleeDamageScale` (0.6) of the swing and
  is then immune to other swipes for `PikminMeleeIFrames` (0.3 s). Pikmin also regenerate like
  a survivor: `PikminRegen` (3%/s of their health) always, at `PikminRegenOutOfCombat` (2x)
  after `PikminRegenDelay` (7 s) with no Pikmin hurt, plus a share of Olimar's regeneration
  (Cautious Slug, Titanic Knurl, level) and doubled by Rejuvenation Rack. Olimar has twice
  Commando's health (`OlimarHealth`, `OlimarHealthPerLevel`). Pikmin that fall into a void are lost.
- **Dismiss strike** (`Combat.Strike*`): RoR2 has no captain puzzles, so the dismiss button no longer sends the squad away. It
  plays Olimar's dismissal motion and sound and hits every enemy around him (reach `StrikeReachMetres`, 4 m) for
  `StrikeDamage` (4) times his punch (about 4.5 times his damage stat: two strikes kill a Beetle at the start; the punch, `PunchDamage` 22.5, is about 1.1 times it), then cools down for `StrikeCooldown` (4 s). It takes Olimar's items and attack
  speed like his punch does. It also staggers what it hits (`StrikeStun`), sends the squad swarming the way he struck for
  `StrikeChargeSeconds` (3; at the enemy he is locked onto, else the way he faces, and the right stick overrides it), and a kill
  with it gives its charge straight back (`StrikeKillReset`). Set `StrikeDamage` to 0 to get Pikmin 1's dismissal back.
- **Pikmin pace** (`Pikmin.Pace*`): a Pikmin's top speed is a multiple of Olimar's full-tilt speed, his items and sprint
  included: flower 1.2 (so it stays in reach for a throw), bud 0.97, leaf 0.93. Set any to 0 for the disc's speeds.
- **Fall damage** (`Items.FallDamage`): the sim moves Olimar, so RoR2's motor never sees him land.
  The sim reports each landing with how far he fell from his highest point; the plugin turns that into
  the speed RoR2's gravity would give (`sqrt(2gh)`) and hands it to RoR2's own landing hook, which
  charges the excess over `jumpPower + 20` as a share of max health and applies fall immunity
  (H3AD-5T), armour, Frailty and Eclipse. Stomps are safe. Teleports and VR/aim-scheme play (RoR2
  moves him) are untouched.
- **Growth** (`Growth.*`): a planted sprout is a leaf, then a bud after `SecondsPerStage` (20 s), then a
  flower after another 20 s, and a flower wilts after `FlowerSeconds` (300 s). The timer starts when it
  can be plucked and restarts at each stage, so the Onion's plucker (60 s) finds a flower. Growth-rate
  (cooldown) items speed the stages up. Pikmin also grow a stage per `KillsPerStage` (2) kills, up to a
  flower: when any enemy dies, each living Pikmin that attacked it earns one credit, whoever struck
  the last blow. Sprouts, and Pikmin that stood by, don't. The decomp's growth sparkles, burst and
  sounds play for both, and the petals when a knocked-down Pikmin gets up a stage lower.
- **Lock-on cursor**: while locked on, the cursor stands up in front of the enemy, on its surface toward
  Olimar and clear of its other parts, facing him and turning slowly. It is drawn up to 6x larger on
  big enemies. Throws and the whistle still go to the part's centre.
- **Trumpet** (`Sound.TrumpetGain`, 1.4): the swarming trumpet is louder than the disc's level so it
  carries over combat.
- **Damage numbers** (`Look.PikminDamageNumbers`, on): the ten HUD digits from your Pikmin disc replace
  RoR2's floating damage digits. Its colours, critical-hit effects, movement and K/M/B suffixes stay.
  Set it to false to restore RoR2's font, including during play. It applies only while you play
  Olimar (every number you see then, whoever dealt it); other survivors keep RoR2's font. Missing
  digits fall back to the original font.
  Requires the matching ABI 27 DLLs; no font textures ship with the plugin or go into git.
- **RoR2 health bar** (`Look.HideRoR2HealthBar`, on): hidden while you play Olimar, since the Pikmin HUD row
  covers where it sits and shows his life as the circle. Other survivors keep it; can change during play.
- **Onion shortcut popup**: one uGUI panel (background, then text, so the glyphs aren't dimmed), small and on the
  right of the screen so Olimar stays in view. It lists X dump, Y fill, RB heal, LB call stragglers, A send the plucker and LT board (Onion Ride).
- **Jump pads**: each Pikmin that takes off with Olimar cries `SE_PIKI_FLY`, as when thrown; the squad repeats the
  pad's throw once, however many ticks the pad reports it, and follows him after landing.
- **Pellet pickup**: a flying Onion descends to the ground (its beam's bottom touches it) before sucking up a pellet.
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
- **Pellet Posies**: the disc's plant model, three growth stages, cycling blue/red/yellow flower,
  hit/fall animations, growth particles and five sounds. Grown plants release their matching 1 pellet
  after falling; immature plants do not. Ship Repair keeps six per player (24 cap), replacing harvested
  plants every 20 seconds. Classic keeps two per living Olimar (8 cap), replenishing every 60 seconds;
  no Olimar means no new plants. Some spawn as buds or seedlings and grow at the disc's pace.
  Configure `Enemies.PelletPosiesShipPerPlayer` / `PelletPosiesClassicPerOlimar`; these replace the old sparse `PelletPosiesPerPlayer` setting.
- **Iridescent Flint Beetle**: Pikmin 1's invulnerable, scuttling loot bug, with its original model,
  textures, walk/flip animations, hit/dust particles and sounds. Landing thrown Pikmin flips it;
  its eight drops repeat 1 pellet, two nectar blobs, two nectar blobs, 5 pellet. It burrows when
  exhausted or its appearance timer expires. One stage roll per player: 50% in Ship Repair,
  12% per living Olimar in other modes, at most two encounters and no refills. Configure
  `Enemies.FlintBeetleShipChance` / `FlintBeetleOtherChance`.
  Falling nectar and drinkable puddles now render in the mod with their original models/animations.
- **HUD numbers**: FREE, SQUAD and TOTAL use Pikmin's half-second per-digit change bounce:
  changed digits expand on increases and contract on decreases, including decimal carries.
- **Corpses**: small enemies leave 1 pellets, medium/large 5, tough/elite 10, and bosses 20 (the largest numbered pellet),
  red, yellow or blue at random, with the disc's size, carriers and seeds. Pikmin carry it to the
  Onion, which turns it into seeds; sprouts grow and are plucked with the throw button.
  Plucking plays the Pikmin's full normal/fast extraction animation, the original dirt
  cloud and debris, and all three pull sound cues. Onion pluckers share this presentation.
  `Corpses.CarryBodies` (experimental, off) has them carry the enemy's own body instead.
- **Onion (phase 8)**: one Onion per Olimar stores all three types. Its Red, Yellow or
  Blue appearance is chosen once per run and kept across stages; it has no effect on storage.
  It arrives airborne alongside the Dolphin, lands to release the squad, and stays planted until raised. With the Pikmin controls holding X for 0.6 seconds lowers a
  flying Onion where he stands and raises a landed one to follow him (free: no charge or cooldown),
  but only when RoR2 shows no interact prompt: under the Onion X is interact and opens
  its menu, and at a chest it opens the chest. The menu only opens while Olimar stands in the
  central beam of his landed Onion, and closes if he leaves it or the Onion takes off.
  The menu is a single page for all colours: up/down chooses an exact number to call out
  or put away, A/left click confirms, and LT boards the Onion (Onion Ride). Counts stop at their limits without wrapping.
  **X** in the menu releases all stored Pikmin except one (subject to the field cap); press X twice
  under the Onion to open and release. **Y** in the menu returns the whole squad and consumes that
  equipment-button press. On keyboard these shortcuts use Utility and Equipment.
  **RB** (keyboard F) heals Olimar using only Pikmin stored in the Onion, preferring leaves (5% max
  health), then buds (10%), then flowers (20%). It consumes enough to fill his health or leave one
  unreserved stored Pikmin, leaves the squad and queued exits untouched, and closes the menu.
  Dolphin recovery effects play at Olimar for two seconds with one Pikmin ghost per sacrifice;
  the effect and ghost count are replicated to other players.
  Raise/lower commands stay on the utility button outside interact prompts, with no movement pages in the menu.
  RT + X dumps a flying Onion's Pikmin from anywhere and RT + Y fills it with the squad (it comes down low to take them). A hovering Onion descends its beam when Olimar stands beneath it and offers the menu;
  Y fills it with the squad, who jump to each leg's midpoint and climb its upper half. Taking-off and landing Onions do not offer the menu.
  Native flying call-outs still slide halfway down a leg, then let go and fall. A hovering Onion
  descends towards nearby pellets before collecting them, and the Pikmin they make stay inside it as leaves instead of being planted. Loose ground pellets are eligible even with no Pikmin alive. Chests and other RoR2 interactions have priority. The old `OnionPerColour` setting
  is ignored. `OnionItems` retains filtered owner-item inheritance: hovering is explicitly
  moving, so Bustling Fungus only activates after the Onion has landed and stood still for
  one second, regardless of whether Olimar is moving.
- **Arrival**: the repaired S.S. Dolphin (all 30 parts, its lights and effects; `Dolphin.ShipPartsMode` is the future
  ship-repair mode and is off) lands 4 m behind him (its goal spot, ringed by its own effect, is where he forms) while his Onion lands to one side. Olimar beams out of the Dolphin's
  side in the ship's healing effects (the recover burst and sparkles), and the squad comes out of the Onion's legs to him; the Onion then lifts to follow him.
- **Dolphin arrival (phase 7)**: Olimar's drop pod is replaced by a 3.5-second S.S. Dolphin
  landing using the extracted ship model. The Onion descends beside it, then follows Olimar;
  Olimar and his squad appear when it lands. Olimar is protected and controls are held during
  arrival. Indoor stages shorten the descent; stage changes and respawns reuse the same
  lifecycle without duplicating an owner's Onion. This is independent of the future optional
  ship-repair feature: no ship parts or repair objective are required.
- **Size**: Olimar is two thirds of Commando's height (`Scale.OlimarHeight`; try 0.75), and
  the Pikmin, Onion and pellets scale with him. His body's collision capsule and hurtboxes
  shrink to match.
  His jump reaches at least Commando's unmodified world-space jump height, with a small clearance
  margin, regardless of this model scale. Jump-power increases raise it further. Pikmin use the
  same jump height, including when following him and jumping over obstacles.
- **Throwing**: throws reach twice as far as in Pikmin (`Controls.ThrowReach`).
  During an enemy lock, Pikmin reach his hand in one simulation tick for rapid
  repeated throws, like feeding the hand with C-stick up; A still presses to grab and releases
  to throw, and projectile flight time stays the same.
  D-pad left or right (or the mouse wheel) picks the next colour to throw, Red, Yellow then Blue, skipping colours not
  in the squad, as in Pikmin 2. The chips above the skill bar show the squad by colour, the
  picked one outlined; until you pick, he throws the nearest Pikmin, as in Pikmin 1.
- **Rejoining**: Pikmin that get separated (done carrying or fighting, thrown with nothing
  to do, left behind) run back to Olimar by themselves (`Pikmin.AutoRejoin`). Dismissed
  Pikmin still wait to be whistled.
- **Shake-off**: when an enemy starts an attack (any of its combat skills), the Pikmin on it
  fly off, fall, get up and rejoin; Pikmin latched for less than 2 s hold on
  (`Pikmin.ShakeProtection`). RoR2 enemies also attempt shakes every second, bosses every 0.25 seconds,
  including gaps between skills. Pikmin enemies keep their own strategies. Nearby groups of at least three ground Pikmin
  expose targets to RoR2 AI; hits on these groups hurt the real Pikmin through the existing area-damage path.
  Enemy explosions also damage each Pikmin in the actual blast area, including projectiles
  that hit terrain without hitting a RoR2 body. Damage follows the blast's distance falloff
  and optional terrain line of sight. One explosion applies once per Pikmin, even if several
  RoR2 bodies report hits. Mob swipes and punches using `OverlapAttack` also test their
  active, rotated box or sphere hitboxes against individual Pikmin bodies, even when no
  RoR2 body was hit. One swing catches at most `PikminMeleeMaxVictims` of the Pikmin in it
  (nearest first), each takes `PikminMeleeDamageScale` of its damage, and a Pikmin that was
  just hit is immune for `PikminMeleeIFrames`; each Pikmin can also be hit only once per
  swing, or at the attack's own retrigger interval, and resetting the attack starts a fresh
  swing. Other direct hits use group targets and the nearby-impact path.
- **Stages**: the squad and the Onion's stock keep their colour and leaf/bud/flower stage.
  If Olimar dies while a teammate is alive, his Pikmin abandon their jobs and return to his Onion, which goes dormant.
  Living returners blocked by terrain are stored after 20 seconds; a stage transition also preserves returners inside the Onion.
  Other Pikmin left outside the squad/Onion stay behind, as at sunset. The arrival camera starts 90 degrees around Olimar.
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
| RT (held) + B    | jump (held) + secondary | whistle; modifier blocks jumping |
| LT (held)        |                     | lock on to an enemy (LT + RB cycles, LT + A throws rapidly at it) |
| X (hold 0.6 s)   | utility             | lower the Onion here / raise it to follow (free; when no interact prompt is up) |
| RT + X           | V                   | dump a flying Onion's Pikmin |
| RT + Y           | B                   | fill a flying Onion with the squad |
| LT under an Onion | jump               | board the Onion: Onion Ride (below) |
| hold RT + tap LT | Z                   | call the Onion to you, lower it and board (again to call it off) |
| RB               | F                   | dismiss strike: a melee blow all around him, 4 s cooldown (see below) |
| d-pad left/right | mouse wheel         | colour to throw: Any (nearest), Red, Yellow, Blue |
| Y                | equipment           | RoR2's equipment, as usual   |
| RT + right stick | special (held)     | swarm (toward the cursor)    |
| LB / left bumper | jump                | swing the camera behind him  |
| right stick click | C                  | zoom (three levels)          |
| RoR2 sprint bind | sprint              | sprint until movement stops  |
| X under an Onion | interact            | open the Onion window        |

The camera is a stand-in for Pikmin's: it looks down from behind and only turns when told
to. Pikmin's own camera (cameraMgr.cpp) isn't ported.

With the default Orbit camera, the right stick pans (up and down tilts it from the top view to `Controls.LookUpDegrees`, 70, above level, as far up as RoR2's own camera looks) and RT + right stick swarms;
LB recentres, LT locks onto an enemy, and LT + RB cycles locked targets (RB alone strikes). The lock reaches `Controls.LockRange` (40 m, past the 24 m throw) with a clear line.
A thrown Pikmin is steered in flight onto the cursor (`Combat.CursorHoming`, on), the way a lock-on steers it onto its enemy,
so it lands where the cursor was whatever Olimar's speed or the cursor's height; the arc then no longer varies with how long the throw was held.
Olimar jumps with the Pikmin's own jump animation (`Pikmin.OlimarUsesPikminJump`, on; off gives his geyser-ride pose).
B jumps in both camera modes. Holding RT prevents Olimar from jumping; releasing RT
with B still held does not jump. B still jumps in an Onion's beam; LT boards it (Onion Ride).

The HUD's skill icons show the pad buttons the Pikmin controls use (ror2-common `GlyphOverride`): A for
the throw, RT + B for the whistle, LT in the Onion's beam for Onion Ride (its charges and cooldown), RB for the Dismiss Strike (its recharge is the
Special slot's, so cooldown items shorten it). The Onion Pluckers are the character's passive, listed
with the four skills on the character select screen but with no icon in the HUD. The Aim scheme and
keyboard keep RoR2's binds. The skills' names and descriptions on the character select screen give the
same buttons.

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

### Onion Ride

The Utility slot is **Onion Ride**, Olimar's escape and repositioning tool. Stand in the beam of a live Onion
(landed or hovering) and press **LT** to board, or hold **RT** and tap **LT** from anywhere: the Onion flies to you the way a raised Onion does (the fast glide, then it eases in), comes down, and boards you.
LB is the camera recentre. LT locks on, boards in the beam and, tapped fresh while RT is held, calls the Onion; if LT is already held when RT goes down the call does not fire (RT's other commands still work), so release LT and tap it again. It spends a charge; the 30 s
cooldown (`Items.RideRecharge`; cooldown items and Hardlight's extra charges apply) starts when the ride ends.
Olimar lifts into the Onion with Pikmin 1's bad-ending motion (`cinemas/demo74/navi.anm`, extracted from your
disc like the rest), the Onion takes off if it was landed, and his squad goes in through its legs as Fill does.

- **Steering**: left stick, at twice his walking speed; clicking the stick boosts to three times for 2 s.
  The right stick is the camera. Nothing else works: no throwing, whistle, swarm, lock-on, strike, Onion
  commands, equipment or interaction.
- **Height**: the Onion keeps the ground-relative height it flies at when following him, held over pits, and
  hovers low while Pikmin are still climbing aboard.
- **Clock**: up to 5 s of grace (ends early if the Onion goes more than 10 m from where he boarded, measured
  on the ground plane), then 10 s. Neither pauses or resets.
- **Ending**: A, B, X or Y (a press that ends a ride cannot start the next), the clock, or the Onion's death.
  Olimar drops out of the beam and the Pikmin that went aboard during the ride come out together, a third
  down each leg; the Onion's reserve stays put and Pikmin still climbing drop back to the field.
  If the Onion is destroyed the same happens just before it goes dormant.
- **Danger**: the Onion is a normal target and takes the damage; Olimar and the Pikmin are not hurt while aboard.
- **Falling**: if he is dropped over a pit, fall recovery returns him to the last ground the ride crossed
  (the boarding spot if none), with his squad.
- **Multiplayer**: a client boards with a command, the host flies the Onion from the stick, boost and exit bits
  in his input packet, and tells him where he was dropped (network protocol 10).

Raising and lowering the Onion (hold X 0.6 s) is separate, free, and never touches the ride's charges.

**Aim** (for VR): RoR2 moves Olimar as usual and the Pikmin cursor is wherever you aim.
Primary throws, secondary whistles, utility lifts or lands the Onion (and strikes, when there is no Onion press to make), special swarms toward the cursor.

### How it works

The incoming-explosion regression checks run headless:

```powershell
dotnet run -c Release --project ../ror2-common/tests/BlastDamageCheck
dotnet run -c Release --project ../ror2-common/tests/OverlapDamageCheck
dotnet run -c Release --project tests/IncomingBlastCheck -- ../libpikmin/target/release/pikmin.dll ../libpikmin/assets
```

`IncomingBlastCheck` compiles the actual adapter method and `Native.cs`, loads the disc's
30-HP Pikmin, and verifies that terrain explosions hurt and kill an idle squad while Olimar
is outside the blast. It also checks overlapping Pikmin, repeated victim reports, terrain
occlusion, out-of-range survivors and damage deferred during a native sim step. Both DLLs
must be updated together (ABI 58).
The same check now exercises the actual mob-melee adapter: repeated active frames and
overlapping hand hitboxes cannot multiply damage, successive swings kill an idle squad,
out-of-range Pikmin survive, and queued hits retain the active geometry and damage.
`build.ps1` checks the game's melee Fire/reset signatures and ignore-list lifecycle before building.

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
  Locally simulated captains and their attached items use those same smoothed feet through
  the shared `ModelView` placement hook. The native view's separate 30 Hz position blend
  can finish early and pause; using it against the smoothly moving camera caused a stutter step.
  The native view still receives `ViewFrame.lag = StepSmoother.LagTicks(RenderAlpha, 1)`
  for facing and sim-driven animation. Arrival, Onion rides, Aim/VR and remote captains
  retain their own placement. Viewer reproduction: `captain_smoothing` (model 0, `pikis/nv3Model.mod`).
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

The log, `BepInEx/LogOutput.log`, should show `libpikmin ABI 11`, `Olimar registered`,
and in a run `Pikmin sim started (disc (10 files))`, `Pikmin models: 19 models; sound 48000 Hz,
2 channels` and `captain 0 is ...`. Latches, deaths, growth, corpses and deliveries are logged
as they happen only with `Debug.LogEvents` on (off by default; it floods the log in a fight).

### Not there yet

- **Multiplayer** is implemented but has never been run in a two-player game: the host's sim is
  the only real one and other machines draw and hear a replica of it from snapshots; a client
  playing Olimar moves on his own machine (so he feels no lag) and the host's captain follows his
  pose. Every player needs the same plugin build and their own extracted `assets/`. See
  [../ror2-common/MULTIPLAYER.md](../ror2-common/MULTIPLAYER.md) for how it works and the test plan.
- Olimar's footsteps and voice, and particle effects other than the whistle and ghosts.
- With `CarryBodies`, clients see a corpse lie where it fell, not carried.
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

The first 3.5 seconds of Impact Site (tutorial.jam) play once per run when Olimar first
emerges on the first stage. SpawnMusic.Enabled and SpawnMusic.Volume control it; master
and music volume also apply. Like Mario, the first-run asset worker renders the intro
from your extracted disc banks into `AssetsDir/ror2_audio/spawn.wav`, with a versioned
cache. Later starts reuse it without a disc or rebuild, and missing or outdated clips
are regenerated. Only DLLs and the assets pointer are deployed; no music ships with them.

Olimar's death plays the full vanilla sundown bell. ABI 28 renders it from your disc into
memory when the sound view loads, then plays it through the death-sting player so game-over
cleanup cannot silence it. `Sound.Enabled`, `Sound.Volume`, the game's SFX volume and the
shared `Ror2Common.Audio.cfg` gain apply; the shared default is -4 dB. Leaving the run stops
the bell. Install the matching `OlimarTest.dll` and `pikmin.dll` together.

## Onion health and recovery (2026-10-14)

An Onion starts with 400 health (`Pikmin.OnionHealth`), gains 30% per level, and can be
hit and targeted by enemies. Each delivered pellet heals 25% of its maximum health plus
2% per seed, capped at full health (`Pikmin.OnionPelletHeal` adjusts the base fraction).
Its hurtbox follows the model's pod, and its optional inherited items still run on its own body.

In follow mode it approaches pellets within six metres, descends at three metres per second
to 0.75 metres above the ground (`PickupHeight`, half of what it was), and dwells for 0.75 seconds before taking a loose pellet.
A carry team can hand off once it is within suction reach. It holds still through suction,
then resumes following. A blocked approach or missing floor prevents pickup.

An Onion below half health shows Pikmin 1's life gauge wheel (the one enemies get, `PikminHealthGauges`)
above its pod. A dormant Onion shows the wheel empty, filling with its revival timer: the configured
wait and the fold, then the three-second revival, full as it wakes. The host's Onions only; a client
sees none.

At zero health it settles on the ground and folds into the disc's dormant Onion pose.
Its colour-matched dormant material is the odd frame of goal.mod's original palette;
the model and startup animations are already in objects/goal. Stock survives but cannot
be withdrawn, and Pikmin ignore pellets without another live destination. Existing carriers
are released; interrupted suction leaves the pellet available for later.

After 30 seconds (`Pikmin.OnionReviveSeconds`), the corresponding startup motion (3-5)
plays over three seconds, with its original particle event keys and FindOnyon sound commands.
The extractor now copies demo4/demo5's timed commands out of main.dol into the ignored
SndData/onion_boot.bin. The Onion revives planted at full health with its stock intact.
All discs and extracted models, textures, sounds and command tables stay outside git.

Olimar's native pose is held at RoR2's arrival feet to prevent falling through during the
spawn state. He materializes from the Dolphin hatch with the ship recovery effects; only
the Pikmin exit the Onion. ABI 27 and snapshot version 3 carry the new Onion lifecycle.
Install the matching DLL pair together. The Onion trial is staged separately in dist/OnionBalanceTrial;
in-game acceptance of arrival, enemy targeting, healing and revival remains manual.

Ship Repair now mixes four directed Pikmin 2 overworld scores into its stage soundtrack: 20% on stages 1-2, 35% on stages 3-5 and 50% later. Enemy proximity, carrying work, nearby ship parts and squad presence drive the disc's music layers. P2 data failures fall back to P1. Enemy presentation uses libsms's common JParticle TEV/alpha/sampler runtime and ror2-common's refraction material. Source IDs, viewer commands and verification limits are documented in ../libpikmin/docs/p2-presentations.md. Not yet played in-game.



