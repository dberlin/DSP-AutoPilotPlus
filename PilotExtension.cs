using System;
using CruiseAssistPlus;
using CruiseAssistPlus.Api;
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
            CruiseAssistPlusPlugin.TargetStar != null ||
            CruiseAssistPlusPlugin.TargetPlanet != null;

        /// <summary>True while an active ground-launch is in progress (Walk/Drift/Fly climbing to space).
        /// Prevents OperateFly from fighting the game's landing descent (Sail-&gt;Fly near the surface).</summary>
        public static bool Launching;

        // ---------------- lifecycle ----------------

        public void OnTargetChanged(int astroId)
        {
            if (astroId == 0) { State = PState.Inactive; InputSailSpeedUp = false; }
            else State = AutoPilotPlusPlugin.AutoStart.Value ? PState.Active : PState.Inactive;
            AutoPilotPlusPlugin.Dbg($"target changed astroId={astroId} -> state={State}");
        }

        public void SetInactive() { State = PState.Inactive; InputSailSpeedUp = false; Launching = false; }

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
            ZeroGravity(m.controller);
            InputSailSpeedUp = false;
            if (m.mecha == null || m.mecha.thrusterLevel < 1)
            {
                LaunchStatus = "need Thruster tech to launch";
                if (GameMain.gameTick % 30 == 0)
                    AutoPilotPlusPlugin.Dbg($"launch blocked: thrusterLevel={(m.mecha != null ? m.mecha.thrusterLevel : -1)}");
                return false;
            }
            Launching = true;
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
            ZeroGravity(m.controller);
            InputSailSpeedUp = false;
            Launching = true;
            m.controller.input0.z = 1f;   // jump edge -> game promotes Drift -> Fly
            LaunchStatus = "launching…";
            return true;
        }

        public bool OperateFly(PlayerMove_Fly m)
        {
            // Gate on the real state: Fly.GameTick is ticked every frame even while sailing, and our
            // ResetSailState() below would otherwise wipe the sail velocity every frame (see OperateWalk).
            if (m.player == null || m.player.movementState != EMovementState.Fly) return false;
            // Only drive a climb when we're actually launching from the ground. During landing the game
            // switches Sail->Fly near the surface; pushing up there would fight the descent.
            if (!Launching || !LaunchAllowed(m.player)) return false;
            ArmForLaunch();
            ZeroGravity(m.controller);
            InputSailSpeedUp = false;

            // The game's OWN navigation auto-fly (navigation.navigating) is switched on the instant a target
            // is selected (we set navigation.indicatorAstroId). While it's on, PlayerMove_Fly.GameTick zeroes
            // BOTH our horizontal input (so horzSpeed never reaches the 12.5 needed to promote Fly->Sail) AND
            // our vertical thrust (so targetAltitude decays and the mecha sinks back to Walk). Suppress it for
            // this tick so our climb inputs actually drive the launch.
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
            // transition sequence. currentAltitude can't exceed ~50 m (targetAltitude cap), so key off 45 m.
            if (m.currentAltitude > 45f && m.mecha != null && m.mecha.thrusterLevel >= 2)
            {
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
            if (player == null || mecha == null) { Launching = false; return false; }
            // Sail.GameTick is ticked every frame even while walking/flying — only act when actually sailing,
            // else we'd fight the ground-launch (setting boost/velocity) before the mecha is even airborne.
            if (!player.sailing) return false;

            EnergyPer = mecha.coreEnergyCap > 0 ? mecha.coreEnergy / mecha.coreEnergyCap * 100.0 : 0.0;
            Speed = move.visual_uvel.magnitude;
            HasWarper = mecha.HasWarper();

            if (player.warping) { Launching = false; return false; } // game drives heading during warp
            if (!HasTarget) { InputSailSpeedUp = false; Launching = false; return false; }

            if (AutoPilotPlusPlugin.IgnoreGravity.Value)
            {
                player.controller.universalGravity = VectorLF3.zero;
                player.controller.localGravity = Vector3.zero;
            }

            var localPlanet = GameMain.localPlanet;
            double altitude = localPlanet == null ? double.MaxValue
                : (player.uPosition - localPlanet.uPosition).magnitude - localPlanet.realRadius;
            bool inSpace = localPlanet == null || altitude > AutoPilotPlusPlugin.SpaceAltitude.Value;

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
                    Launching = true; // keep re-climbing if the game briefly bounces us back to Fly
                    return ClimbToSpace(player, localPlanet, altitude);
                }
            }

            Launching = false; // in space (or landing on the destination) -> launch phase complete

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
                TryWarp(move, player, mecha);
                // Steer the heading ourselves so guidance is unconditional (not subject to CruiseAssist's
                // manual-input guard) and pairs with boost — otherwise the ship just coasts and tail-chases
                // an orbiting planet at constant speed, never arriving.
                SteerToward(player, CruiseAssistPlusPlugin.TargetUPos);
                return true;
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
            player.uVelocity = steer * climbSpeed;

            if (GameMain.gameTick % 60 == 0)
                AutoPilotPlusPlugin.Dbg($"climb-out alt={altitude:0} climbSpeed={climbSpeed:0} " +
                    $"climbFrac={climbFrac:0.00} energy={EnergyPer:0}%");
            return true;
        }

        private void TryWarp(PlayerMove_Sail move, Player player, Mecha mecha)
        {
            var targetStar = CruiseAssistPlusPlugin.TargetStar;
            var localStar = GameMain.localStar;

            bool canWarpHere = AutoPilotPlusPlugin.LocalWarp.Value || localStar == null ||
                               (targetStar != null && targetStar.id != localStar.id);
            if (!canWarpHere) { LastWarpReason = "same-system"; return; }

            double rangeThreshold = AutoPilotPlusPlugin.WarpMinRangeAU.Value * AutoPilotPlusPlugin.UnitsPerAU.Value;
            if (CruiseAssistPlusPlugin.TargetRange < rangeThreshold) { LastWarpReason = "too close"; return; }
            if (Speed < AutoPilotPlusPlugin.SpeedToWarp.Value) { LastWarpReason = "too slow"; return; }
            if (!HasWarper) { LastWarpReason = "no warper"; return; }

            if (mecha.coreEnergy <= mecha.warpStartPowerPerSpeed * move.maxWarpSpeed)
            {
                LastWarpReason = "low energy";
                return;
            }

            if (mecha.UseWarper())
            {
                player.warpCommand = true;
                VFAudio.Create("warp-begin", player.transform, Vector3.zero, true, 0, -1, -1L);
                LastWarpReason = "WARP!";
                AutoPilotPlusPlugin.Dbg("engaged warp");
            }
        }

        /// <summary>Redirect the mecha's velocity toward a universe position (heading only; speed comes from boost).</summary>
        private static void SteerToward(Player player, VectorLF3 targetUPos)
        {
            double speed = ((VectorLF3)player.uVelocity).magnitude;
            if (speed < 1.0) return; // nothing to redirect yet; boost will build speed
            VectorLF3 dir = targetUPos - player.uPosition;
            float angle = Vector3.Angle(dir, player.uVelocity);
            float t = AutoPilotPlusPlugin.ApproachTurnRate.Value / Mathf.Max(AutoPilotPlusPlugin.ApproachMinAngle.Value, angle);
            player.uVelocity = Vector3.Slerp(player.uVelocity, dir.normalized * speed, t);
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
                SteerToward(player, CruiseAssistPlusPlugin.TargetUPos);
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

            float angle = Vector3.Angle(steer, player.uVelocity);
            float t = AutoPilotPlusPlugin.ApproachTurnRate.Value / Mathf.Max(AutoPilotPlusPlugin.ApproachMinAngle.Value, angle);
            player.uVelocity = Vector3.Slerp(player.uVelocity, steer.normalized * speedCap, t);
            return true;
        }

        // ---------------- GUI ----------------

        public void OnGUI() => UI.PilotUI.OnGUI();
        public string PanelLabel => "AutoPilot";
        public void TogglePanel() => UI.PilotUI.Visible = !UI.PilotUI.Visible;
    }
}
