# Onion Ride and the controller rework (2026-10-31)

Built from the spec the user pasted (written without the code); where the code or the user disagreed, the
decisions below win. Nothing here has been played in game.

## Controls

On foot: A throw, B jump, RB dismiss strike. **LB** locks on (LB + RB cycles, LB + A throws rapidly at the
target). **RT** is the alternate modifier: RT + B whistle, RT + right stick swarm, RT + X dumps and RT + Y fills a
*flying* Onion (same reach as the old double-tap X, which is gone). Hold X 0.6 s raises or lowers the Onion
and costs nothing. RT + X / RT + Y also keep RoR2's interact and equipment presses to themselves
(`OnionMenuEquipment`: hooks on `EquipmentSlot.MyFixedUpdate` and `InteractionDriver.FixedUpdate`).

In an Onion's beam, beam commands win: X dump, Y fill, RB heal, LB call stragglers, A send the plucker,
**LT board** (Onion Ride; LT is the camera recentre elsewhere, and B stays jump in the beam). Riding: left stick steers, its click boosts, right stick is the camera, A/B/X/Y exit.

**RT + LT** calls the Onion to Olimar from anywhere: it flies over like a raised Onion does (the fast 1.8 s glide, then it eases in), comes down its beam, and boards him; RT + LT again calls it off
(`PikminWorld.ToggleSummon`; refused if the Onion is dormant or the ride has no charge). Holding RT stops LT recentring the camera.

Keyboard (not in the spec; made up): V dump, B fill, Z call the Onion, Utility hold relocate, Jump in the beam boards,
Sprint boosts, any of Primary/Secondary/Utility/Equipment exits.

## Ride rules

- Utility slot = `OlimarSkills.Ride`, 1 charge, 30 s (`Items.RideRecharge`). The charge is spent at boarding
  (`NativeSkills.Spend`); the recharge is held at zero while he rides (`PikminWorld.HoldRecharge`) so it starts
  when the ride ends. A charge already coming back from an earlier ride keeps recharging; only once that one is
  home is the stopwatch held for this ride's charge.
- Speed 2x his walk speed, 3x for 2 s after a stick click (`OnionRidePolicy`). Grace 5 s or 10 m on the ground
  plane, then 10 s; none of it pauses. Tests: `tests/OnionMenuCheck`.
- Height follows the ground like the following Onion, held over pits. The ride never drops to the Fill
  Onion's low, legs-out hover (its old 0.6 m boarding height): the core's hover entry jumps Pikmin to a
  flying leg's midpoint and climbs from there, so the ride keeps the flying model and animation throughout.
- Boarding records the Onion's stored count per colour. At the end the excess is the ride's squad and comes out
  with `pik_sim_onion_burst_out` (a third per leg, all in one step); `pik_sim_onion_cancel_entries` sends Pikmin
  still climbing back to the squad. Onion death (`GoDormant`) runs the same end first.
- Olimar is hidden, `HiddenInvincibility`, hurtboxes off. The Onion keeps taking damage.
- Fall recovery: `Unstuck.SetSafe` (ror2-common) is given the last ground the ride crossed.
- The lift into the Onion is `cinemas/demo74/navi.anm` (frames 465-552 of the bad ending, played at 2x); the
  extractor tops existing caches up with that file. Without it he just vanishes.

## Multiplayer (network protocol 11, ABI 57)

Client boards with `OnionCommandPacket.Board` and calls the Onion with `Summon`; steering is in `PikminInputPacket.swarmX/Y` with the
`Boost`/`Exit` bits; the snapshot's `CaptainInfo.Riding` flag tells everyone; `RideEndPacket` (message 0x4F04)
places the client. Same build required on all machines.

## Not verified

Any in-game behaviour; two-machine play; the `InteractionDriver.FixedUpdate` hook name (logs a warning if
missing); wall sliding of the Onion; whether Pikmin over a pit are all recovered by the squad teleport.
