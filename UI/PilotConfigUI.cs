using BepInEx.Configuration;
using CruiseAssistPlus.Core;
using UnityEngine;

namespace AutoPilotPlus.UI
{
    /// <summary>AutoPilot+ settings window. Opened from the "Config" button on the AutoPilot+ panel.
    /// Mirrors CruiseAssist+'s ConfigUI so every AutoPilot config entry is toggleable in-game.</summary>
    public static class PilotConfigUI
    {
        private const int WinId = 68302;
        public static Rect Rect = new Rect(410, 470, 340, 460);
        public static bool Visible = false;

        private static ConfigEntry<float> _left, _top;

        public static void LoadFromConfig(ConfigFile cfg)
        {
            _left = cfg.Bind("Window", "PilotConfigLeft", 410f);
            _top = cfg.Bind("Window", "PilotConfigTop", 470f);
            Rect.x = _left.Value; Rect.y = _top.Value;
        }

        public static void OnGUI()
        {
            if (!Visible) return;
            Rect = GUILayout.Window(WinId, Rect, Draw, "AutoPilot+ Config");
            UIUtil.Settle(ref Rect);
            if (_left != null) { _left.Value = Rect.x; _top.Value = Rect.y; }
        }

        private static void Draw(int id)
        {
            if (UIUtil.TitleButton(Rect, 0, DspSkin.Glyph("✕", "X"))) Visible = false;
            GUILayout.BeginVertical();

            AutoPilotPlusPlugin.MasterEnabled.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.MasterEnabled.Value, "Enabled (master)");

            GUILayout.Space(4);
            GUILayout.Label("Automation", DspSkin.Header);
            AutoPilotPlusPlugin.AutoStart.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.AutoStart.Value, "Auto-arm on target select");
            AutoPilotPlusPlugin.AutoLaunch.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.AutoLaunch.Value, "Auto-launch from ground");
            AutoPilotPlusPlugin.AutoLand.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.AutoLand.Value, "Auto-land on arrival");
            AutoPilotPlusPlugin.IgnoreGravity.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.IgnoreGravity.Value, "Ignore gravity while automating");

            GUILayout.Space(4);
            GUILayout.Label("Warp", DspSkin.Header);
            AutoPilotPlusPlugin.LocalWarp.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.LocalWarp.Value, "Allow warp within the current system");
            GUILayout.Label($"Min warp range: {AutoPilotPlusPlugin.WarpMinRangeAU.Value} AU");
            AutoPilotPlusPlugin.WarpMinRangeAU.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.WarpMinRangeAU.Value, 1, 60));
            GUILayout.Label($"Speed to warp: {AutoPilotPlusPlugin.SpeedToWarp.Value}");
            AutoPilotPlusPlugin.SpeedToWarp.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.SpeedToWarp.Value, 100, 5000) / 50f) * 50;

            GUILayout.Space(4);
            GUILayout.Label("Flight", DspSkin.Header);
            GUILayout.Label($"Min core energy to boost: {AutoPilotPlusPlugin.MinEnergyPer.Value}%");
            AutoPilotPlusPlugin.MinEnergyPer.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.MinEnergyPer.Value, 0, 100));
            GUILayout.Label($"Resume-boost energy: {AutoPilotPlusPlugin.ResumeEnergyPer.Value}%");
            AutoPilotPlusPlugin.ResumeEnergyPer.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.ResumeEnergyPer.Value, 0, 100));
            GUILayout.Label($"Max sail speed: {AutoPilotPlusPlugin.MaxSpeed.Value}");
            AutoPilotPlusPlugin.MaxSpeed.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.MaxSpeed.Value, 100, 5000) / 50f) * 50;
            GUILayout.Label($"Launch climb speed (no boost): {AutoPilotPlusPlugin.LaunchClimbSpeed.Value}");
            AutoPilotPlusPlugin.LaunchClimbSpeed.Value =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.LaunchClimbSpeed.Value, 40, 2000) / 10f) * 10;
            GUILayout.Label($"Space altitude (climb-out target): {AutoPilotPlusPlugin.SpaceAltitude.Value:0} m");
            AutoPilotPlusPlugin.SpaceAltitude.Value =
                Mathf.Round(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.SpaceAltitude.Value, 100f, 2000f) / 50f) * 50f;
            GUILayout.Label($"Approach brake range: {AutoPilotPlusPlugin.ApproachBrakeRange.Value:0} m");
            AutoPilotPlusPlugin.ApproachBrakeRange.Value =
                Mathf.Round(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.ApproachBrakeRange.Value, 1000f, 40000f) / 500f) * 500f;
            GUILayout.Label($"Orbit altitude (launch-to-orbit): {AutoPilotPlusPlugin.OrbitAltitude.Value:0} m");
            AutoPilotPlusPlugin.OrbitAltitude.Value =
                Mathf.Round(GUILayout.HorizontalSlider(AutoPilotPlusPlugin.OrbitAltitude.Value, 600f, 20000f) / 100f) * 100f;

            GUILayout.Space(4);
            GUILayout.Label("UI", DspSkin.Header);
            AutoPilotPlusPlugin.HidePanelWhenNotInSpace.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.HidePanelWhenNotInSpace.Value, "Hide panel when not in space");

            GUILayout.Space(4);
            GUILayout.Label("Debug", DspSkin.Header);
            AutoPilotPlusPlugin.DebugLog.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.DebugLog.Value, "Verbose debug logging");
            AutoPilotPlusPlugin.DebugWindow.Value =
                UIUtil.Toggle(AutoPilotPlusPlugin.DebugWindow.Value, "Show debug overlay");

            GUILayout.EndVertical();
            UIUtil.DragBar(Rect);
        }

    }
}
