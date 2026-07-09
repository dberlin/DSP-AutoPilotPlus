# Changelog

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
