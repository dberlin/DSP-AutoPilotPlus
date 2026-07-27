using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CruiseAssistPlus;
using HarmonyLib;

namespace AutoPilotPlus
{
    /// <summary>
    /// Full flight automation built on top of CruiseAssistPlus: automatic take-off, sail boost,
    /// warp engagement, planetary approach and (new) auto-landing. Rebuild of tanu/appuns AutoPilot.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(CruiseAssistPlusPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public class AutoPilotPlusPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.livinginstinkt.dsp.autopilotplus";
        public const string Name = "AutoPilotPlus";
        public const string Version = "0.3.3";

        internal static ManualLogSource Log;
        internal static AutoPilotPlusPlugin Instance;

        // General
        internal static ConfigEntry<bool> MasterEnabled;
        internal static ConfigEntry<bool> AutoStart;

        // Flight
        internal static ConfigEntry<int> MinEnergyPer;
        internal static ConfigEntry<int> ResumeEnergyPer;
        internal static ConfigEntry<int> LaunchClimbSpeed;
        internal static ConfigEntry<int> MaxSpeed;
        internal static ConfigEntry<int> WarpMinRangeAU;
        internal static ConfigEntry<int> SpeedToWarp;
        internal static ConfigEntry<bool> LocalWarp;
        internal static ConfigEntry<bool> IgnoreGravity;
        internal static ConfigEntry<bool> AutoLand;
        internal static ConfigEntry<bool> AutoLaunch;

        // UI
        internal static ConfigEntry<bool> HidePanelWhenNotInSpace;

        // Tuning (exposed magic numbers)
        internal static ConfigEntry<float> ApproachSpeedCap;
        internal static ConfigEntry<float> MinClearance;
        internal static ConfigEntry<float> ApproachTurnRate;
        internal static ConfigEntry<float> ApproachMinAngle;
        internal static ConfigEntry<float> UnitsPerAU;
        internal static ConfigEntry<int> WarperItemId;
        internal static ConfigEntry<float> SpaceAltitude;
        internal static ConfigEntry<float> ApproachBrakeRange;
        internal static ConfigEntry<float> SeedBrakeRange;
        internal static ConfigEntry<float> OrbitAltitude;

        // Debug
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> DebugWindow;

        private Harmony _harmony;
        private PilotExtension _extension;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            MasterEnabled = Config.Bind("General", "Enabled", true, "Master switch for AutoPilotPlus.");
            AutoStart = Config.Bind("General", "AutoStart", true,
                "Arm autopilot automatically when a target is selected. When off, arm it from the AutoPilot window.");

            MinEnergyPer = Config.Bind("Flight", "MinEnergyPer", 20,
                new ConfigDescription("Stop boosting below this mecha core energy %.", new AcceptableValueRange<int>(0, 100)));
            LaunchClimbSpeed = Config.Bind("Flight", "LaunchClimbSpeed", 250,
                new ConfigDescription("Top speed (units/s) the ground-launch uses to climb straight out of the " +
                    "gravity well to space — driven directly, WITHOUT boost, so it's as fast as possible on thrust " +
                    "alone. Ramps up to this as altitude builds.", new AcceptableValueRange<int>(40, 2000)));
            ResumeEnergyPer = Config.Bind("Flight", "ResumeEnergyPer", 50,
                new ConfigDescription("After energy drops below MinEnergyPer, don't boost again until it recharges to " +
                    "this %. Prevents a stall/boost loop where boost restarts the instant energy crosses MinEnergyPer " +
                    "and immediately drains back to empty without building speed (leaves you frozen, drifting).",
                    new AcceptableValueRange<int>(0, 100)));
            MaxSpeed = Config.Bind("Flight", "MaxSpeed", 2000,
                new ConfigDescription("Sail boost speed cap.", new AcceptableValueRange<int>(100, 5000)));
            WarpMinRangeAU = Config.Bind("Flight", "WarpMinRangeAU", 2,
                new ConfigDescription("Minimum distance (AU) to the target before warp is allowed.", new AcceptableValueRange<int>(1, 60)));
            SpeedToWarp = Config.Bind("Flight", "SpeedToWarp", 1200,
                new ConfigDescription("Minimum sail speed before warp engages.", new AcceptableValueRange<int>(100, 5000)));
            LocalWarp = Config.Bind("Flight", "LocalWarp", false, "Allow warp within the current star system.");
            IgnoreGravity = Config.Bind("Flight", "IgnoreGravity", true, "Zero gravity while automating (cleaner take-off).");
            AutoLand = Config.Bind("Flight", "AutoLand", true, "Decelerate and settle onto the destination planet on arrival.");
            AutoLaunch = Config.Bind("Flight", "AutoLaunch", true,
                "When armed with a target while on the ground, auto-launch into orbit (Walk->Fly->Sail) and cruise to it. Needs Thruster tech.");

            HidePanelWhenNotInSpace = Config.Bind("UI", "HidePanelWhenNotInSpace", false,
                "Auto-hide the AutoPilot+ window while walking/building on a planet; show it in space.");

            ApproachSpeedCap = Config.Bind("Tuning", "ApproachSpeedCap", 120f, "Speed cap while manoeuvring near a planet.");
            MinClearance = Config.Bind("Tuning", "MinClearance", 800f, "Altitude (units above surface) considered 'clear' of a planet.");
            ApproachTurnRate = Config.Bind("Tuning", "ApproachTurnRate", 1.6f, "Slerp numerator for approach steering.");
            ApproachMinAngle = Config.Bind("Tuning", "ApproachMinAngle", 10f, "Slerp angle floor for approach steering.");
            UnitsPerAU = Config.Bind("Tuning", "UnitsPerAU", 40000f, "Game units per AU (used for the warp distance check).");
            WarperItemId = Config.Bind("Tuning", "WarperItemId", 1210, "Item id of the Space Warper.");
            SpaceAltitude = Config.Bind("Tuning", "SpaceAltitude", 600f,
                "Altitude (m above surface) above which the mecha is considered 'in space' and may boost/thrust. Below this it coasts out on launch momentum to save energy.");
            ApproachBrakeRange = Config.Bind("Tuning", "ApproachBrakeRange", 6000f,
                new ConfigDescription("Distance (m above the destination surface) at which the approach starts braking. " +
                    "Inside this range the mecha bleeds speed proportional to remaining distance so it settles into a slow " +
                    "high orbit instead of coasting in at cruise speed and crashing.", new AcceptableValueRange<float>(1000f, 40000f)));
            SeedBrakeRange = Config.Bind("Tuning", "SeedBrakeRange", 400000f,
                new ConfigDescription("Distance (m) from a Dark Fog seed at which to start braking. Seeds are small, " +
                    "fast-moving points, so braking starts much farther out than a planet approach to avoid overshooting.",
                    new AcceptableValueRange<float>(10000f, 2000000f)));
            OrbitAltitude = Config.Bind("Tuning", "OrbitAltitude", 1500f,
                new ConfigDescription("Target altitude (m above surface) for 'launch to orbit' — selecting the planet you're " +
                    "standing on climbs to roughly this altitude and holds there instead of flying off to another target.",
                    new AcceptableValueRange<float>(600f, 20000f)));

            DebugLog = Config.Bind("Debug", "DebugLog", false, "Verbose per-tick autopilot logging (why warp did/didn't fire, etc).");
            DebugWindow = Config.Bind("Debug", "DebugWindow", false, "Show the AutoPilot debug overlay.");

            UI.PilotUI.LoadFromConfig(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Patches.VFInputPatch));

            _extension = new PilotExtension();
            CruiseAssistPlusPlugin.RegisterExtension(_extension);

            Log.LogInfo($"{Name} v{Version} loaded.");
        }

        private void OnDestroy()
        {
            CruiseAssistPlusPlugin.UnregisterExtension(typeof(PilotExtension));
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static void Dbg(string msg)
        {
            if (DebugLog != null && DebugLog.Value) Log.LogInfo("[ap] " + msg);
        }
    }
}
