# AutoPilot+

Full flight automation for Dyson Sphere Program, built as an extension of **CruiseAssist+**. Pick a
target in CruiseAssist+ and AutoPilot+ flies you there: it takes off, boosts the sail, engages warp,
crosses interstellar space, and (new) settles onto the destination planet.

Fresh rebuild of the original **AutoPilot** (tanu, continued by appuns) for current DSP
(0.10.34+, Unity 2022.3).

> ⚠️ **Dependency — this mod requires [CruiseAssistPlus](https://github.com/Living-Instinkt/DSP-CruiseAssistPlus).**
> AutoPilot+ is an extension of CruiseAssist+ and will not function without it. Install it from
> [Thunderstore](https://dsp.thunderstore.io/package/LivingInstinkt/CruiseAssistPlus/)
> (r2modman/Thunderstore Mod Manager install it automatically as a dependency) or from
> [GitHub](https://github.com/Living-Instinkt/DSP-CruiseAssistPlus).

## How it works

- Select a destination in the CruiseAssist+ star list. With **Auto-arm** on, AutoPilot+ engages.
- Take-off: climbs Walk->Fly->Sail off the surface (optionally zeroing gravity) and steers straight out
  of the gravity well **without boosting**, so the mecha reaches space with a full core instead of
  burning it fighting gravity. It arcs toward the target as it climbs.
- Sail: once above `SpaceAltitude`, holds the boost key up to `MaxSpeed`. If the core drains below
  `MinEnergyPer` it stops boosting and waits until it recharges to `ResumeEnergyPer` before resuming —
  this avoids a stall/boost loop that would leave you frozen and drifting. (If the core never recovers,
  the mecha is out of fuel.)
- Warp: engages when far enough (`WarpMinRangeAU`), fast enough (`SpeedToWarp`), you have a Space
  Warper, and the core has enough energy — mirroring the game's own warp rule.
- Approach / **Auto-land**: near the destination it caps speed, climbs clear if the target is behind
  the planet, then bleeds speed as it descends and hands control back to the game to land.
- **Dark Fog targets**: when you pick a Dark Fog hive or seed in CruiseAssist+, AutoPilot+ warps toward it
  too (including within the current system, since a seed can be most of a system away) and brakes on the
  approach so it doesn't overshoot the small, fast target. Braking distance is `Tuning/SeedBrakeRange`
  (default 400 km).

## Improvements over the original

- Rebuilt against DSP 0.10.34 / Unity 2022.3 (the old mod loaded but did nothing).
- No dependency on a drifting extension API — CruiseAssist+ and AutoPilot+ share one stable, versioned interface.
- **Auto-land** on arrival (the original only pointed at the planet).
- Every tuning constant is config-exposed: approach speed cap, min clearance, slerp constants,
  units-per-AU, warper item id, plus all the flight thresholds.
- Verbose debug logging that states exactly why warp did or didn't fire.

## Controls

The **AutoPilot+** window shows state, energy, speed, warper and the last warp decision, with
On/Off, Arm/Disarm and quick toggles. Its title bar carries **–** (collapse), **⚙** (full settings:
automation, warp, flight energy/speed, space altitude, UI and debug) and **✕** (close — reopen from the
CruiseAssist+ HUD). Movement state, altitude and horizontal speed are shown in the CruiseAssist+ debug
overlay.

Both windows are drawn in CruiseAssist+'s game-style skin, and clicks on them stop at the window instead
of also reaching a building, the camera or a DSP panel behind — see `UI/BlockClickThrough` in
CruiseAssist+'s config.

## Bugs, feature requests & discussion

Please report bugs, request features, or start a discussion on GitHub:
<https://github.com/Living-Instinkt/DSP-AutoPilotPlus>

- **Bugs / feature requests** — open an [issue](https://github.com/Living-Instinkt/DSP-AutoPilotPlus/issues).
- **Questions & general discussion** — use the [Discussions](https://github.com/Living-Instinkt/DSP-AutoPilotPlus/discussions) tab.

## Credits

AutoPilot+ is a fresh reimplementation of the original **AutoPilot**. Full credit and thanks to the
developers whose work it builds on:

- **Tanu** — original author of [AutoPilot](https://dsp.thunderstore.io/package/tanu/AutoPilot/).
- **appuns** — continued the mod as [DSPAutoPilot](https://github.com/appuns/DSPAutoPilot).

This rebuild is an independent clean-room reimplementation against the current game API, not a copy of the
original DLL.
