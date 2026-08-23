using System;
using CruiseAssistPlus;
using CruiseAssistPlus.Api;
using CruiseAssistPlus.Core;
using UnityEngine;

namespace AutoPilotPlus
{
    /// <summary>
    /// The flight automation. CruiseAssistPlus calls the Operate* hooks each physics tick with the
    /// live movement instance. Walk/Drift/Fly handle take-off; Sail handles boost, warp and the
    /// planetary approach / auto-land. Returning true tells CruiseAssistPlus "I steered this tick".
    /// </summary>
    public class PilotExtension : INavigatorExtension
    {
        public enum PState { Inactive, Active }

        public static PState State = PState.Inactive;
        public static bool InputSailSpeedUp;   // read by VFInputPatch to hold the boost key
        public static double EnergyPer;
        public static double Speed;
        public static bool HasWarper;
        public static string LastWarpReason = "";
        private static bool _energyStalled;   // latched true when core drains below MinEnergyPer (see EnergyReadyToBoost)

        private static bool Armed =>
            AutoPilotPlusPlugin.MasterEnabled != null &&
            AutoPilotPlusPlugin.MasterEnabled.Value &&
            State == PState.Active;

        // Check the SELECTION (set the instant you pick a target, available on the ground) as well as the
        // resolved target (only populated during the Sail tick). Using only the resolved target meant
        // HasTarget was false on the ground, so ground-launch never armed.
        private static bool HasTarget =>
            CruiseAssistPlusPlugin.SelectTargetStar != null ||
            CruiseAssistPlusPlugin.SelectTargetPlanet != null ||
            CruiseAssistPlusPlugin.SelectTargetHive != null ||
            CruiseAssistPlusPlugin.SelectTargetEnemyId != 0 ||
            CruiseAssistPlusPlugin.SelectTargetMsgId != 0 ||
            CruiseAssistPlusPlugin.TargetStar != null ||
            CruiseAssistPlusPlugin.TargetPlanet != null ||
            CruiseAssistPlusPlugin.TargetKind == NavTargetKind.Hive ||
            CruiseAssistPlusPlugin.TargetKind == NavTargetKind.Enemy ||
            CruiseAssistPlusPlugin.TargetKind == NavTargetKind.Message;

        // ---------------- lifecycle ----------------

        /// <summary>True while we're launching to orbit around the very planet the mecha is standing on
        /// (target == local planet). Drives a straight-up climb-and-hold instead of flying off to a target,
        /// and tells CruiseAssist to suppress its auto-arrival clear until we've reached orbit.</summary>
        private static bool _orbitLaunch;

        public void OnTargetChanged(int astroId)
        {
            // Dark Fog seed/communicator selections notify with astroId 0 (they live on the enemy/msg
            // indicator fields, not indicatorAstroId) — don't let that disarm us; arm off the DF selection.
            // Hive selections carry a real astroId (> 1,000,000), so they arm via the normal path below.
            bool dfSelection = CruiseAssistPlusPlugin.SelectTargetKind == NavTargetKind.Enemy ||
                               CruiseAssistPlusPlugin.SelectTargetKind == NavTargetKind.Message;
            if (astroId == 0 && !dfSelection)
            {
                State = PState.Inactive; InputSailSpeedUp = false;
                _orbitLaunch = false; CruiseAssistPlusPlugin.SuppressAutoArrival = false;
            }
            else
            {
                State = AutoPilotPlusPlugin.AutoStart.Value ? PState.Active : PState.Inactive;
                // "Launch to orbit": you picked the planet you're currently standing on. Climb into its
                // orbit and hold, rather than treating it as an instant arrival (which would disarm us).
                // Never applies to Dark Fog targets.
                var lp = GameMain.localPlanet;
                var player = GameMain.mainPlayer;
                bool onThisPlanet = !dfSelection && lp != null && CruiseAssistPlusPlugin.SelectTargetPlanet != null &&
                                    CruiseAssistPlusPlugin.SelectTargetPlanet.id == lp.id &&
                                    (player == null || !player.sailing);
                _orbitLaunch = onThisPlanet;
                CruiseAssistPlusPlugin.SuppressAutoArrival = onThisPlanet;
            }
            AutoPilotPlusPlugin.Dbg($"target changed astroId={astroId} -> state={State} orbitLaunch={_orbitLaunch}");
        }

        public void SetInactive()
        {
            State = PState.Inactive; InputSailSpeedUp = false;
            _orbitLaunch = false; CruiseAssistPlusPlugin.SuppressAutoArrival = false;
        }

        public static void ToggleArmed()
        {
            State = State == PState.Active ? PState.Inactive : PState.Active;
            if (State == PState.Inactive) InputSailSpeedUp = false;
        }

        // ---------------- take-off (ground launch: Walk -> Fly -> Sail) ----------------

        public static string LaunchStatus = "";

        public bool OperateWalk(PlayerMove_Walk m)
        {
            // The game ticks ALL four PlayerMove_X.GameTick every frame (each early-returns unless it's the
            // active state), and our prefixes run before that guard. So gate on the real movement state —
            // otherwise e.g. OperateFly runs while sailing and its ResetSailState() wipes our cruise velocity
            // every frame ("nothing holds W"; travels fine only with AutoPilot off).
            if (m.player == null || m.player.movementState != EMovementState.Walk) return false;
            if (!LaunchAllowed(m.player)) return false;
            ArmForLaunch();
            InputSailSpeedUp = false;
            if (m.mecha == null || m.mecha.thrusterLevel < 1)
            {
                LaunchStatus = "need Thruster tech to launch";
                if (GameMain.gameTick % 30 == 0)
                    AutoPilotPlusPlugin.Dbg($"launch blocked: thrusterLevel={(m.mecha != null ? m.mecha.thrusterLevel : -1)}");
                return false;
            }
            LaunchStatus = "launching…";
            if (GameMain.gameTick % 30 == 0) AutoPilotPlusPlugin.Dbg("launch: Walk -> SwitchToFly");
            m.SwitchToFly();   // Walk -> Fly (requires thrusterLevel >= 1)
            return true;
        }

        public bool OperateDrift(PlayerMove_Drift m)
        {
            if (m.player == null || m.player.movementState != EMovementState.Drift) return false;
            if (!LaunchAllowed(m.player)) return false;
            ArmForLaunch();
            InputSailSpeedUp = false;
            // Same gate as OperateWalk: PlayerMove_Drift.SwitchToFly silently does nothing below
            // thrusterLevel 1, so without this we'd report "launching…" forever
            // while the mecha just sat there.
            if (m.mecha == null || m.mecha.thrusterLevel < 1)
            {
                LaunchStatus = "need Thruster tech to launch";
                if (GameMain.gameTick % 30 == 0)
                    AutoPilotPlusPlugin.Dbg($"launch blocked: thrusterLevel={(m.mecha != null ? m.mecha.thrusterLevel : -1)}");
                return false;
            }
            m.controller.input0.z = 1f;   // jump edge -> PlayerMove_Drift.UpdateJump promotes Drift -> Fly
            LaunchStatus = "launching…";
            return true;
        }

        public bool OperateFly(PlayerMove_Fly m)
        {
            // Gate on the real state: Fly.GameTick is ticked every frame even while sailing, and our
            // ResetSailState() below would otherwise wipe the sail velocity every frame (see OperateWalk).
            if (m.player == null || m.player.movementState != EMovementState.Fly) return false;
            // A target is enough to start climbing, whatever got us airborne. This used to also require a
            // "Launching" flag that only OperateWalk and OperateDrift ever set, so picking a target while
            // already hovering left the mecha hanging there indefinitely. The flag was there to stop us
            // fighting the game's landing descent (it drops Sail->Fly near the surface), but by then
            // CruiseAssist has cleared the target, so LaunchAllowed is false and we stand down anyway.
            if (!LaunchAllowed(m.player)) return false;
            ArmForLaunch();
            InputSailSpeedUp = false;

            // Stand the game's own auto-fly down for this tick. It is NOT switched on by picking a
            // CruiseAssist target — PlayerNavigation.indicatorAstroId's setter only touches the indicator
            // fields, and `navigating` is set solely by PlayerNavigation.Start(). But if the player did
            // order a navigate-to, PlayerMove_Fly.GameTick would zero BOTH our horizontal input (so
            // horzSpeed never reaches the 12.5 the vanilla Fly->Sail gate wants) AND our vertical thrust
            // (so targetAltitude decays and the mecha sinks back to Walk), which would stall the launch.
            if (m.navigation != null) m.navigation.navigating = false;

            // Drive climb + horizontal run-up. Pushing targetAltitude past 50 is REQUIRED: the game only
            // runs its climb-to-50-and-promote-to-Sail branch when targetAltitude >= 50 (it then clamps it
            // back to 50). Left at the default ~15 the mecha just hovers at ~14 m and circles the planet.
            // Every OperateWalk->SwitchToFly resets targetAltitude to 15, so we re-assert it each Fly tick.
            m.targetAltitude = Mathf.Max(m.targetAltitude, 60f);
            m.controller.input1.y = 1f;   // vertical thrust -> climb toward the 50 m ceiling
            m.controller.input0.y = 1f;   // forward -> build horzSpeed for the Fly->Sail gate
            LaunchStatus = $"launching (alt {m.currentAltitude:0} m)";
            if (GameMain.gameTick % 30 == 0)
                AutoPilotPlusPlugin.Dbg($"launch climb: alt={m.currentAltitude:0} horz={m.controller.horzSpeed:0.0} " +
                    $"thruster={(m.mecha != null ? m.mecha.thrusterLevel : -1)}");

            // Hand-promote once we've cleared the climb. We do this ourselves (rather than relying on the
            // game's horzSpeed>12.5 gate, which a low walk-speed mecha may never reach) using the game's full
            // transition sequence, but we keep the game's OWN altitude gate of 49 m. That number is not
            // arbitrary: PlayerMove_Sail.GameTick drops Sail->Fly again whenever altitude < 46 m at low
            // speed, so promoting at 45 m handed the mecha straight back to Fly and flapped between the two
            // states forever. currentAltitude converges on the 50 m targetAltitude cap, so 49 is reachable.
            if (m.currentAltitude > 49f && m.mecha != null && m.mecha.thrusterLevel >= 2)
            {
                // The game clears an in-flight build command before entering Sail; we were skipping this
                // step, so launching mid-build carried a live build command into sail mode.
                if (m.controller.cmd.type == ECommand.Build)
                {
                    m.controller.cmd.SetNoneCommand();
                    m.controller.actionBuild.blueprintMode = EBlueprintMode.None;
                }
                m.controller.movementStateInFrame = EMovementState.Sail;
                m.controller.actionSail.ResetSailState();
                GameCamera.instance.SyncForSailMode();
                GameMain.gameScenario?.NotifyOnSailModeEnter();
                AutoPilotPlusPlugin.Dbg($"launch: Fly -> Sail (alt={m.currentAltitude:0} horz={m.controller.horzSpeed:0.0})");
            }
            return true;
        }

        // Launch is allowed when the mod is enabled, auto-launch is on, and a target is selected — it does
        // NOT require the separate "armed" toggle (selecting a planet from the ground should just launch).
        private static bool LaunchAllowed(Player player) =>
            player != null &&
            AutoPilotPlusPlugin.MasterEnabled.Value &&
            AutoPilotPlusPlugin.AutoLaunch.Value &&
            HasTarget;

        private static void ArmForLaunch()
        {
            if (State != PState.Active)
            {
                State = PState.Active;
                AutoPilotPlusPlugin.Dbg("ground launch -> armed");
            }
        }

        /// <summary>Cancel the gravity pull while sailing. SAIL ONLY, deliberately.
        ///
        /// PlayerController.GameTick runs ApplyGravity() — which both fills in universalGravity/localGravity
        /// AND does the AddLocalForce(localGravity) — *before* it ticks the movement actions, so a prefix
        /// zeroing those fields is already too late to cancel anything on the ground. All it achieved in
        /// Walk/Drift/Fly was to strip the `+ universalGravity.magnitude` gravity-compensation term out of
        /// PlayerMove_Fly's thruster force, making the launch climb sag below its target altitude — the
        /// exact opposite of the "cleaner take-off" it was meant to give.
        ///
        /// Sail is different: ApplyGravity skips AddLocalForce while sailing, and PlayerMove_Sail.GameTick
        /// reads controller.universalGravity into uForce itself — after our prefix — so zeroing it there
        /// really does remove the pull.</summary>
        private static void ZeroGravity(PlayerController controller)
        {
            if (controller != null && AutoPilotPlusPlugin.IgnoreGravity.Value)
            {
                controller.universalGravity = VectorLF3.zero;
                controller.localGravity = Vector3.zero;
            }
        }

        // ---------------- sail (boost / warp / approach) ----------------

        public bool OperateSail(PlayerMove_Sail move)
        {
            if (!Armed) { InputSailSpeedUp = false; return false; }
            var player = move.player;
            var mecha = move.mecha ?? player?.mecha;
            if (player == null || mecha == null) return false;
            // Sail.GameTick is ticked every frame even while walking/flying — only act when actually sailing,
            // else we'd fight the ground-launch (setting boost/velocity) before the mecha is even airborne.
            if (!player.sailing) return false;

            EnergyPer = mecha.coreEnergyCap > 0 ? mecha.coreEnergy / mecha.coreEnergyCap * 100.0 : 0.0;
            Speed = move.visual_uvel.magnitude;
            HasWarper = mecha.HasWarper();

            if (player.warping) return false; // game drives heading during warp
            if (!HasTarget) { InputSailSpeedUp = false; return false; }

            ZeroGravity(player.controller);

            var localPlanet = GameMain.localPlanet;
            double altitude = localPlanet == null ? double.MaxValue
                : (player.uPosition - localPlanet.uPosition).magnitude - localPlanet.realRadius;
            bool inSpace = localPlanet == null || altitude > AutoPilotPlusPlugin.SpaceAltitude.Value;

            // Launch-to-orbit: you selected the planet you're standing on. Climb straight out to orbit
            // altitude and hold, instead of flying off toward a distant target or auto-landing back down.
            if (_orbitLaunch)
                return HandleOrbitLaunch(player, localPlanet, altitude);

            // Climb-out: the game promotes Fly->Sail at only ~50 m, but boost is gated behind SpaceAltitude
            // (600 m). Between the two the ship used to sit at speed ~1 while the game bounced it back to Fly
            // ("cruise ended" spam). So while we're still inside a NON-destination planet's gravity well,
            // actively boost straight out until we're genuinely in space. (When this IS the destination we
            // fall through to ApproachOrDepart so auto-land can descend instead.)
            if (localPlanet != null && !inSpace)
            {
                bool destinationHere = CruiseAssistPlusPlugin.TargetPlanet != null &&
                                       CruiseAssistPlusPlugin.TargetPlanet.id == localPlanet.id;
                if (!destinationHere)
                {
                    return ClimbToSpace(player, localPlanet, altitude);
                }
            }

            // Boost only once clear of the gravity well — thrusting low over a planet burns core energy
            // fighting gravity, which used to leave you stranded slow in space with an empty core.
            bool energyOk = EnergyReadyToBoost();
            InputSailSpeedUp = inSpace && energyOk && Speed < AutoPilotPlusPlugin.MaxSpeed.Value;
            LaunchStatus = !energyOk ? $"low energy ({EnergyPer:0}%) — recharging"
                         : inSpace ? "cruising" : "approaching…";

            if (GameMain.gameTick % 60 == 0)
                AutoPilotPlusPlugin.Dbg($"sail inSpace={inSpace} boost={InputSailSpeedUp} energy={EnergyPer:0}% " +
                    $"speed={Speed:0} range={CruiseAssistPlusPlugin.TargetRange:0} onPlanet={localPlanet != null}");

            if (localPlanet == null)
            {
                // Approaching a destination PLANET from open space: bleed speed based on how far we still
                // have to go so we settle into a slow high orbit instead of coasting in at full cruise speed
                // and crashing into the surface. We only scale the speed (brake) — heading stays
                // CruiseAssist's job — so this keeps the manual-steering behaviour intact.
                var tp = CruiseAssistPlusPlugin.TargetPlanet;
                if (tp != null)
                {
                    double rangeToSurface = CruiseAssistPlusPlugin.TargetRange - tp.realRadius;
                    float brakeStart = AutoPilotPlusPlugin.ApproachBrakeRange.Value;
                    if (rangeToSurface < brakeStart)
                    {
                        InputSailSpeedUp = false; // stop accelerating; we're arriving
                        float minCap = AutoPilotPlusPlugin.ApproachSpeedCap.Value;
                        float cap = Mathf.Clamp((float)(rangeToSurface / brakeStart) * AutoPilotPlusPlugin.MaxSpeed.Value,
                                                minCap, AutoPilotPlusPlugin.MaxSpeed.Value);
                        double sp = ((VectorLF3)player.uVelocity).magnitude;
                        if (sp > cap) player.uVelocity = (Vector3)player.uVelocity * (float)(cap / sp);
                        LaunchStatus = $"arriving — braking ({rangeToSurface:0} m)";
                        if (GameMain.gameTick % 30 == 0)
                            AutoPilotPlusPlugin.Dbg($"approach brake range={rangeToSurface:0} cap={cap:0} speed={sp:0}");
                        return false; // CruiseAssist still steers the heading toward the planet
                    }
                }

                // Approaching a Dark Fog SEED: like the planet approach, but seeds are small, fast-moving
                // points with no realRadius, so brake off the raw range and start much farther out
                // (SeedBrakeRange). Heading stays CruiseAssist's job; we only bleed speed so we don't
                // overshoot the moving seed at full cruise.
                if (CruiseAssistPlusPlugin.TargetEnemyId != 0)
                {
                    double range = CruiseAssistPlusPlugin.TargetRange;
                    float brakeStart = AutoPilotPlusPlugin.SeedBrakeRange.Value;
                    if (range < brakeStart)
                    {
                        InputSailSpeedUp = false;
                        float minCap = AutoPilotPlusPlugin.ApproachSpeedCap.Value;
                        float cap = Mathf.Clamp((float)(range / brakeStart) * AutoPilotPlusPlugin.MaxSpeed.Value,
                                                minCap, AutoPilotPlusPlugin.MaxSpeed.Value);
                        double sp = ((VectorLF3)player.uVelocity).magnitude;
                        if (sp > cap) player.uVelocity = (Vector3)player.uVelocity * (float)(cap / sp);
                        LaunchStatus = $"approaching seed — braking ({range:0} m)";
                        if (GameMain.gameTick % 30 == 0)
                            AutoPilotPlusPlugin.Dbg($"seed brake range={range:0} cap={cap:0} speed={sp:0}");
                        return false; // CruiseAssist keeps steering the heading toward the seed
                    }
                }

                // Open-space cruise between planets/stars. AutoPilot's job here is ONLY boost, warp and
                // forward speed — NOT heading. Hand alignment back to CruiseAssist by returning false: its
                // CruiseTick then slerps the velocity toward the target AND honours its RespectManualInput
                // guard, so you can steer left/right mid-cruise and releasing re-aligns with the target
                // (the classic behaviour). Boost stays on via InputSailSpeedUp, so you keep accelerating in
                // whatever direction you're pointed — manual or auto.
                TryWarp(move, player, mecha);
                return false;
            }
            return ApproachOrDepart(player, localPlanet);
        }

        /// <summary>Boost-energy gate with hysteresis. Once the core drains below MinEnergyPer we stop boosting
        /// and DON'T resume until it recharges to ResumeEnergyPer — otherwise boost restarts the instant energy
        /// ticks past MinEnergyPer and immediately drains back to empty without ever building speed, leaving the
        /// mecha frozen and drifting. If the core never reaches ResumeEnergyPer, the mecha is genuinely out of
        /// fuel and needs refuelling — no boost strategy can move it.</summary>
        private static bool EnergyReadyToBoost()
        {
            if (EnergyPer < AutoPilotPlusPlugin.MinEnergyPer.Value) _energyStalled = true;
            else if (EnergyPer >= AutoPilotPlusPlugin.ResumeEnergyPer.Value) _energyStalled = false;
            return !_energyStalled;
        }

        /// <summary>The planet's own universe velocity, blended exactly the way the game blends it.
        ///
        /// Below 600 m the game does NOT treat <c>player.uVelocity</c> as an absolute universe velocity.
        /// PlayerMove_Sail.GameTick reads <c>visual_uvel = player.uVelocity - planetVelAtPoint * blend</c>
        /// and writes velocity back as <c>relative + planetVelAtPoint * blend</c>, where
        /// <c>blend = clamp01((600 - altitude) / 450)</c> — full below 150 m, fading to zero at 600 m.
        /// So near a planet, uVelocity is "velocity relative to the ground, plus the ground's own motion".
        ///
        /// Anything that assigns uVelocity inside that band and forgets the offset is really asking for
        /// "move at V relative to the STAR", which the game then reads as "move at V minus the planet's
        /// orbital velocity relative to the GROUND" — a few hundred m/s in a direction that has nothing to
        /// do with the intended heading. That is what made ground launches sling the mecha sideways and
        /// drop it back through the Sail-retain floor, so it never actually left the planet.
        ///
        /// The 600/450 constants mirror PlayerMove_Sail.GameTick and are deliberately NOT the SpaceAltitude
        /// config: they describe the game's frame blend, not our idea of where space starts.</summary>
        private static VectorLF3 PlanetFrameVelocity(Player player, PlanetData localPlanet, double altitude)
        {
            if (localPlanet == null) return VectorLF3.zero;
            double blend = (600.0 - altitude) / 450.0;
            if (blend <= 0.0) return VectorLF3.zero;
            if (blend > 1.0) blend = 1.0;
            // player.position is the planet-local position — the same argument the game itself passes
            // from PlayerMove_Sail.ResetSailState.
            return localPlanet.GetUniversalVelocityAtLocalPoint(GameMain.gameTime, player.position) * blend;
        }

        /// <summary>Launch-to-orbit handler: climb straight outward from the planet we're standing on up to
        /// OrbitAltitude, then brake to a gentle drift and clear the target (which disarms us) — leaving the
        /// mecha holding in high orbit. Used when the selected target IS the local planet, so there is no
        /// "somewhere else" to steer toward; we climb along the outward (up) vector only.</summary>
        private bool HandleOrbitLaunch(Player player, PlanetData localPlanet, double altitude)
        {
            CruiseAssistPlusPlugin.SuppressAutoArrival = true; // keep CruiseAssist from clearing mid-climb

            // Planet unloaded (climbed far enough) -> treat as reached orbit.
            bool reached = localPlanet == null || altitude >= AutoPilotPlusPlugin.OrbitAltitude.Value;
            if (!reached)
            {
                InputSailSpeedUp = false; // climb on thrust/steering alone, conserve core (as ClimbToSpace)
                Vector3 outward = ((Vector3)(player.uPosition - localPlanet.uPosition)).normalized;
                float cap = AutoPilotPlusPlugin.LaunchClimbSpeed.Value;
                float frac = Mathf.Clamp01((float)(altitude / Mathf.Max(1f, AutoPilotPlusPlugin.OrbitAltitude.Value)));
                float climbSpeed = cap * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(frac / 0.5f));
                // Same planet-frame correction as ClimbToSpace: this leg starts on the surface, well
                // inside the band where the game reads uVelocity relative to the moving planet.
                VectorLF3 climbVel = outward * climbSpeed;
                player.uVelocity = climbVel + PlanetFrameVelocity(player, localPlanet, altitude);
                LaunchStatus = $"launching to orbit… ({altitude:0} m)";
                if (GameMain.gameTick % 30 == 0)
                    AutoPilotPlusPlugin.Dbg($"orbit climb alt={altitude:0} climbSpeed={climbSpeed:0}");
                return true;
            }

            // Reached orbit altitude: brake to a slow drift, then release + disarm so we hold in high orbit.
            InputSailSpeedUp = false;
            double sp = ((VectorLF3)player.uVelocity).magnitude;
            if (sp > 20) player.uVelocity = (Vector3)player.uVelocity * (float)(20.0 / sp);
            LaunchStatus = "in orbit";
            AutoPilotPlusPlugin.Dbg($"orbit launch complete alt={altitude:0} -> hold + clear");
            _orbitLaunch = false;
            CruiseAssistPlusPlugin.SuppressAutoArrival = false;
            CruiseAssistPlusPlugin.ClearSelection(); // goal reached; disarms us via SetInactive
            return true;
        }

        /// <summary>Climb out of a planet's gravity well until we clear SpaceAltitude. Starts pointing
        /// straight up (to climb out fast without fighting gravity sideways) and arcs progressively toward
        /// the target as altitude builds — so the ship is already heading at the destination by the time it
        /// reaches space, instead of snapping toward it only after clearing SpaceAltitude. Ramps speed so we
        /// don't fall below the game's sail-retain threshold (which would bounce us back to Fly).
        /// Deliberately does NOT boost: the climb is driven by directly steering uVelocity (free), so core
        /// energy is conserved for once we're actually in space — boosting low in the well burns the core
        /// fighting gravity and used to strand the mecha slow with an empty core.</summary>
        private bool ClimbToSpace(Player player, PlanetData localPlanet, double altitude)
        {
            LaunchStatus = $"climbing to space… ({altitude:0} m)";
            InputSailSpeedUp = false; // no boost until we're clear of the well / at space altitude

            Vector3 outward = ((Vector3)(player.uPosition - localPlanet.uPosition)).normalized;
            Vector3 toTarget = ((Vector3)(CruiseAssistPlusPlugin.TargetUPos - player.uPosition)).normalized;

            // Blend up->target by how far through the climb we are (0 at surface, ~0.75 near SpaceAltitude).
            float climbFrac = Mathf.Clamp01((float)(altitude / Mathf.Max(1f, AutoPilotPlusPlugin.SpaceAltitude.Value)));
            Vector3 steer = Vector3.Slerp(outward, toTarget, climbFrac * 0.75f);
            // Never let the heading dip toward/into the planet — keep a positive outward (climbing) component.
            if (Vector3.Dot(steer, outward) < 0.2f) steer = (steer + outward).normalized;

            // Drive the velocity DIRECTLY (not a soft slerp): the game holds unboosted sail speed near zero,
            // so nudging toward the target only crept up ~7 m/s. Set it outright and ramp up to the cap so we
            // climb out as fast as possible on thrust alone — no boost, no warp.
            float cap = AutoPilotPlusPlugin.LaunchClimbSpeed.Value;
            float climbSpeed = cap * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(climbFrac / 0.5f));
            // climbSpeed is the speed we want relative to the GROUND, so hand the game the planet-frame
            // offset it expects (see PlanetFrameVelocity). Without it the whole climb is computed in the
            // star's frame and the mecha is left behind by the orbiting planet instead of climbing.
            VectorLF3 climbVel = steer * climbSpeed;
            player.uVelocity = climbVel + PlanetFrameVelocity(player, localPlanet, altitude);

            if (GameMain.gameTick % 60 == 0)
                AutoPilotPlusPlugin.Dbg($"climb-out alt={altitude:0} climbSpeed={climbSpeed:0} " +
                    $"climbFrac={climbFrac:0.00} energy={EnergyPer:0}%");
            return true;
        }

        private void TryWarp(PlayerMove_Sail move, Player player, Mecha mecha)
        {
            var targetStar = CruiseAssistPlusPlugin.TargetStar;
            var localStar = GameMain.localStar;

            // Dark Fog seeds and hives are legitimate intra-system warp targets (a seed can be most of a
            // system away). Allow warp toward them even without LocalWarp / a different-system star.
            bool dfWarpTarget = CruiseAssistPlusPlugin.TargetEnemyId != 0 ||
                                CruiseAssistPlusPlugin.TargetKind == NavTargetKind.Hive;
            bool canWarpHere = AutoPilotPlusPlugin.LocalWarp.Value || localStar == null || dfWarpTarget ||
                               (targetStar != null && targetStar.id != localStar.id);
            if (!canWarpHere) { LastWarpReason = "same-system"; return; }

            double rangeThreshold = AutoPilotPlusPlugin.WarpMinRangeAU.Value * AutoPilotPlusPlugin.UnitsPerAU.Value;
            if (CruiseAssistPlusPlugin.TargetRange < rangeThreshold) { LastWarpReason = "too close"; return; }
            if (Speed < AutoPilotPlusPlugin.SpeedToWarp.Value) { LastWarpReason = "too slow"; return; }

            if (mecha.coreEnergy <= mecha.warpStartPowerPerSpeed * move.maxWarpSpeed)
            {
                LastWarpReason = "low energy";
                return;
            }

            // Mirror the game's own warp start (PlayerMove_Sail.GameTick): take a warper from the
            // inventory, and fall back to the mecha's auto-replenish. We used to refuse outright on an
            // empty warper slot, so with auto-replenish on the panel just read "no warper" forever while
            // pressing the warp key manually worked fine. Vanilla ignores the second UseWarper's result;
            // we check it so a failed replenish can't leave warpCommand set with nothing consumed.
            if (!mecha.UseWarper() &&
                (!mecha.autoReplenishWarper || !mecha.AutoReplenishWarper() || !mecha.UseWarper()))
            {
                LastWarpReason = "no warper";
                return;
            }

            player.warpCommand = true;
            VFAudio.Create("warp-begin", player.transform, Vector3.zero, true, 0, -1, -1L);
            LastWarpReason = "WARP!";
            AutoPilotPlusPlugin.Dbg("engaged warp");
        }

        /// <summary>Redirect the mecha's velocity toward a universe position (heading only; speed comes from boost).</summary>
        /// <summary>Redirect the mecha's velocity toward a universe position (heading only; speed comes from
        /// boost). Steers in the planet's frame for the same reason ApproachOrDepart does — see
        /// <see cref="PlanetFrameVelocity"/>. Outside the 600 m band that offset is zero and this is plain
        /// absolute steering, which is the only case reachable at the default MinClearance of 800.</summary>
        private static void SteerToward(Player player, VectorLF3 targetUPos, PlanetData localPlanet, double altitude)
        {
            VectorLF3 frameVel = PlanetFrameVelocity(player, localPlanet, altitude);
            VectorLF3 relVelocity = player.uVelocity - frameVel;
            double speed = relVelocity.magnitude;
            if (speed < 1.0) return; // nothing to redirect yet; boost will build speed
            VectorLF3 dir = targetUPos - player.uPosition;
            float angle = Vector3.Angle(dir, relVelocity);
            float t = AutoPilotPlusPlugin.ApproachTurnRate.Value / Mathf.Max(AutoPilotPlusPlugin.ApproachMinAngle.Value, angle);
            VectorLF3 steered = Vector3.Slerp(relVelocity, dir.normalized * speed, t);
            player.uVelocity = steered + frameVel;
        }

        private bool ApproachOrDepart(Player player, PlanetData localPlanet)
        {
            VectorLF3 fromPlanet = player.uPosition - localPlanet.uPosition;
            double altitude = fromPlanet.magnitude - localPlanet.realRadius;
            float cap = AutoPilotPlusPlugin.ApproachSpeedCap.Value;
            float clearance = AutoPilotPlusPlugin.MinClearance.Value;

            // Already fast and well clear of the planet: steer straight at the target ourselves.
            if (Speed > cap && altitude > Math.Max(localPlanet.realRadius, clearance))
            {
                SteerToward(player, CruiseAssistPlusPlugin.TargetUPos, localPlanet, altitude);
                return true;
            }

            VectorLF3 targetUPos = CruiseAssistPlusPlugin.TargetUPos;
            VectorLF3 planetToTarget = targetUPos - localPlanet.uPosition;
            bool targetOnFarSide = Vector3.Angle(fromPlanet, planetToTarget) > 90f;

            VectorLF3 steer = targetOnFarSide
                ? fromPlanet                        // climb straight out before turning
                : targetUPos - player.uPosition;    // steer at the target

            double speedCap = Math.Min(Speed, cap);

            // Auto-land: destination is this planet -> bleed speed as altitude drops, then release.
            bool destinationHere = CruiseAssistPlusPlugin.TargetPlanet != null &&
                                   CruiseAssistPlusPlugin.TargetPlanet.id == localPlanet.id;
            if (destinationHere && AutoPilotPlusPlugin.AutoLand.Value)
            {
                InputSailSpeedUp = false;
                speedCap = Mathf.Clamp((float)altitude, 8f, cap);
                if (altitude < clearance * 0.25 && Speed < 20)
                {
                    AutoPilotPlusPlugin.Dbg("auto-land: releasing control to game");
                    return false; // let the game's own landing take over
                }
            }

            // Steer in the planet's frame. speedCap is derived from Speed (= visual_uvel, already
            // ground-relative), so the slerp target has to be ground-relative too — otherwise the descent
            // is computed in the star's frame and the mecha keeps the planet's orbital velocity as an
            // apparent few-hundred-m/s drift, which never lets Speed fall under the 20 hand-off above.
            // Outside the 600 m band PlanetFrameVelocity returns zero and this is the previous behaviour.
            VectorLF3 frameVel = PlanetFrameVelocity(player, localPlanet, altitude);
            Vector3 relVelocity = player.uVelocity - frameVel;
            float angle = Vector3.Angle(steer, relVelocity);
            float t = AutoPilotPlusPlugin.ApproachTurnRate.Value / Mathf.Max(AutoPilotPlusPlugin.ApproachMinAngle.Value, angle);
            VectorLF3 steered = Vector3.Slerp(relVelocity, steer.normalized * speedCap, t);
            player.uVelocity = steered + frameVel;
            return true;
        }

        // ---------------- GUI ----------------

        public void OnGUI() => UI.PilotUI.OnGUI();
        public string PanelLabel => "AutoPilot";
        public void TogglePanel() => UI.PilotUI.Visible = !UI.PilotUI.Visible;
    }
}
