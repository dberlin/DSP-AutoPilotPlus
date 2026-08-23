# Changelog

## 0.3.5
- **Ground launch works again.** Taking off from a planet did nothing useful: the mecha bobbed around the
  surface and never reached space, so the mod only ever appeared to work if you engaged it while already
  sailing. Below 600 m the game does not read `uVelocity` as an absolute universe velocity — it reads it as
  velocity relative to the ground *plus* the ground's own motion, faded in over the 600 m -> 150 m band. The
  climb-out set it absolutely, so what the game actually saw was the mecha keeping station with the star
  while the planet orbited out from under it: a few hundred m/s of drift in a direction unrelated to "up",
  which regularly pushed it back down through the altitude floor where the game drops Sail -> Fly. Climb-out,
  launch-to-orbit, the approach and the auto-land descent now all convert into the planet's frame. Above
  600 m nothing changes.
- **The Fly -> Sail hand-off no longer flaps.** It promoted at 49 m rather than 45 m. The game demotes
  Sail -> Fly again below 46 m at low speed, so the old threshold entered sail mode inside the window the
  game immediately reverses, and the two states fought each other. 49 m is the game's own altitude gate.
- The near-planet approach and the auto-land descent are corrected by the same change. They shared the
  reference-frame error, which left the mecha carrying the planet's orbital velocity as an apparent
  few-hundred-m/s drift, so the "slow enough to hand control back to the game" check could never pass.
- **`IgnoreGravity` no longer fights the launch.** It is a sail-only setting now, which is the only place it
  ever worked: the game applies ground gravity before mods get a look in, so on the ground it managed only
  to strip the gravity compensation out of the thruster force and let the climb sag short of its target.
- Launching from a drift now reports "need Thruster tech to launch" instead of claiming to launch forever —
  the drift take-off silently does nothing without the tech, same as walking.
- An in-progress build command is cleared when the autopilot enters sail mode, matching what the game does
  on its own take-off; launching mid-build used to carry the command into flight.
- Warp honours the mecha's auto-replenish warper setting, as the game does. With an empty warper slot the
  panel used to read "no warper" indefinitely even though warping by hand worked.

## 0.3.4
- **Clicks no longer pass through the AutoPilot+ windows** onto a building, the terrain, the camera or a
  DSP panel behind them. Both windows register the area they cover with CruiseAssist+'s click blocker,
  which raises the game's own "cursor is on the interface" state while you're over one.
- Both windows pick up CruiseAssist+'s **game-style skin and font**: dark translucent panel, cyan-steel
  edging, a real title bar, and checkboxes that read clearly whether a setting is on or off.
- Collapse, config and close moved into the AutoPilot+ title bar, freeing the row they used to take. A
  collapsed panel keeps its title bar.
- Requires CruiseAssistPlus 0.3.7.

## 0.3.1
- The AutoPilot+ window (and its config window) now clamp on-screen, so a saved position combined with a
  higher CruiseAssist+ UI scale can no longer strand the window off the edge where it couldn't be opened.
  Requires CruiseAssistPlus 0.3.2.

## 0.3.0
- **Dark Fog targets.** AutoPilot now arms for CruiseAssist+ Dark Fog selections (hives, seeds,
  communicators) and warps toward hives/seeds — including intra-system, since a seed can be most of a system
  away.
- **Seed-approach braking.** New `Tuning/SeedBrakeRange` (default 400 km): when approaching a Dark Fog seed
  the mecha stops boosting and bleeds speed proportional to remaining range so it doesn't overshoot the
  small, fast-moving target. Heading stays CruiseAssist's job.

## 0.2.9
- Fixed crashing into the destination instead of settling into orbit. The approach now brakes on the way in:
  within `ApproachBrakeRange` of the destination surface it stops boosting and bleeds speed proportional to
  the remaining distance, so the mecha arrives slow in a high orbit instead of coasting in at cruise speed
  and slamming into the ground. Heading stays CruiseAssist's job, so manual steering still works on approach.
- New: **launch to orbit**. Selecting the planet you're currently standing on now climbs straight out to
  `OrbitAltitude` and holds there, instead of instantly counting as "already arrived" and doing nothing.
- New tuning: `ApproachBrakeRange` (default 6000 m) and `OrbitAltitude` (default 1500 m).
- Requires CruiseAssistPlus 0.2.3 (adds the `SuppressAutoArrival` hand-off used by launch-to-orbit).

## 0.2.8
- Restored manual steering during interplanetary/interstellar cruise. AutoPilot now owns only boost, warp
  and forward speed — it no longer rotates the velocity vector itself while cruising in open space. Heading
  is handed back to CruiseAssist, so you can steer left/right mid-flight and releasing re-aligns with the
  target (the classic behaviour), governed by `CruiseAssist › RespectManualInput`. Previously AutoPilot
  overwrote the velocity every tick and returned "handled", bypassing CruiseAssist's manual-input guard, so
  any manual course change was undone on the next tick. Ground-launch climb-out and near-planet
  approach/auto-land are unchanged (CruiseAssist has released its target by the time you land).

## 0.2.7
- Ground-launch now climbs out of the gravity well as fast as possible without boost. The old code nudged
  the velocity with a soft slerp, but the game holds unboosted sail speed near zero so it only crept up at
  ~7 m/s. It now drives the climb velocity directly and ramps up to the new `LaunchClimbSpeed` cap
  (default 250, configurable). Still no boost or warp until clear of the well.

## 0.2.6
- Fixed a CruiseAssist/AutoPilot conflict that stalled the launch at ~52-62 m and made sailing feel like
  "nothing is holding W". DSP ticks all four `PlayerMove_X.GameTick` every frame (each early-returns unless
  it's the active state) and our Harmony prefixes ran before that guard — so while sailing, `OperateFly`
  fired every frame and its `ResetSailState()` wiped the velocity the climb-out/cruise had just set. Each
  `Operate*` hook now gates on the real `movementState`, so it only acts in its own state. (Explains why it
  travelled fine with AutoPilot toggled off.)

## 0.2.5
- Launch climb-out no longer boosts. Climbing out of the gravity well is driven purely by steering the
  velocity vector (free), so the mecha reaches space with a full core instead of burning it fighting
  gravity. Boost is used only once above `SpaceAltitude`.
- Requires CruiseAssistPlus 0.2.2.

## 0.2.4
- Fixed freeze-and-drift in space caused by a stall/boost loop: once the core drained below `MinEnergyPer`
  boost restarted the instant energy ticked past it and immediately drained back to empty without building
  speed. Boost now waits until the core recharges to the new `ResumeEnergyPer` (default 50%) before
  resuming. Panel shows a `low energy — recharging` status. If the core never recovers, the mecha is out of
  fuel and needs refuelling.

## 0.2.3
- Fixed launch stalling at ~14 m and circling the planet. The game only runs its climb-to-50-and-promote
  branch when `targetAltitude >= 50`; a 0.2.2 cleanup dropped the line that raises it, so the mecha hovered
  at the default ~15 m. Restored and re-asserted each Fly tick.

## 0.2.2
- Rewrote ground-launch. The game's own navigation (turned on when a target is selected) was zeroing our
  climb and steering inputs during the Fly phase, so the mecha never built the horizontal speed the game
  needs to promote Fly->Sail. We now suppress `navigation.navigating` during the launch climb and
  hand-promote Fly->Sail via the game's full transition sequence.
- New `ClimbToSpace` phase steers out of the gravity well and arcs progressively toward the target so the
  ship is already heading at the destination by the time it reaches space.
- Added an AutoPilot+ Config window (opened from a `Config` button on the panel) exposing every setting.
- Debug overlay now includes altitude, movement state and horizontal speed.
- Requires CruiseAssistPlus 0.2.2.

## 0.2.1
- Fixed ground-launch never firing — it was gated on the Sail-only resolved target, which is empty on
  the ground; it now uses the live selection. Launch triggers on Enabled + Auto-launch + a target
  selected (auto-arms) and no longer needs the separate Arm toggle. (Reaching space needs Thruster ≥ 2.)
- When armed, AutoPilot now steers the heading itself (unconditional guidance + boost) instead of
  coasting at constant speed and tail-chasing an orbiting planet.
- Added a launch phase flag so the climb no longer fights the game's landing descent.
- More diagnostic logging (`ground launch -> armed`, `Walk -> SwitchToFly`, `launch climb …`, throttled
  `sail inSpace=… boost=… energy=… speed=…`).
- Requires CruiseAssistPlus 0.2.1.

## 0.2.0
- New: **auto-launch from the ground** — with a target selected and autopilot armed while standing on a
  planet, the mecha climbs Walk->Fly->Sail into orbit and cruises to the target. Needs Thruster tech.
- Fixed energy drain: boost/thrust is now only used once actually in space (above `SpaceAltitude`), so you
  no longer burn the core dry climbing out of orbit and crawl through space.
- Window can be collapsed, closed (✕), and auto-hidden when not in space; reopen it from the CruiseAssist+ HUD.
- Requires CruiseAssistPlus 0.2.0.

## 0.1.0
- Initial release. Clean rebuild of AutoPilot for DSP 0.10.34+ / Unity 2022.3, as a CruiseAssistPlus extension.
- Automated take-off, sail boost, warp engagement and planetary approach.
- New: auto-land on arrival at the destination planet.
- All flight thresholds and tuning constants exposed in config.
- Verbose debug logging (states the reason warp did/didn't fire).
