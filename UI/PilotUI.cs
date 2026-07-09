using BepInEx.Configuration;
using CruiseAssistPlus;
using UnityEngine;

namespace AutoPilotPlus.UI
{
    /// <summary>AutoPilot status + quick controls. Drawn inside CruiseAssistPlus's scaled GUI pass.
    /// Supports collapse, close (✕, reopen from the CruiseAssist+ HUD), and auto-hide when not in space.</summary>
    public static class PilotUI
    {
        private const int WinId = 68301;
        public static Rect Rect = new Rect(100, 470, 300, 230);
        public static bool Visible = true;
        public static bool Collapsed = false;

        private static ConfigEntry<float> _left, _top;
        private static ConfigEntry<bool> _collapsed;

        public static void LoadFromConfig(ConfigFile cfg)
        {
            _left = cfg.Bind("Window", "PilotLeft", 100f);
            _top = cfg.Bind("Window", "PilotTop", 470f);
            _collapsed = cfg.Bind("Window", "PilotCollapsed", false);
            Rect.x = _left.Value; Rect.y = _top.Value; Collapsed = _collapsed.Value;
            PilotConfigUI.LoadFromConfig(cfg);
        }

        public static void OnGUI()
        {
            PilotConfigUI.OnGUI();

            if (!Visible) return;
            if (AutoPilotPlusPlugin.HidePanelWhenNotInSpace.Value && !CruiseAssistPlusPlugin.InSpace) return;

            Rect.height = Collapsed ? 40 : 230;
            Rect = GUILayout.Window(WinId, Rect, Draw, "AutoPilot+");
            if (_left != null) { _left.Value = Rect.x; _top.Value = Rect.y; _collapsed.Value = Collapsed; }
        }

        private static void Header()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Collapsed ? "+" : "–", GUILayout.Width(26))) Collapsed = !Collapsed;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Config", GUILayout.Width(60))) PilotConfigUI.Visible = !PilotConfigUI.Visible;
            if (GUILayout.Button("✕", GUILayout.Width(26))) Visible = false;
            GUILayout.EndHorizontal();
        }

        private static void Draw(int id)
        {
            GUILayout.BeginVertical();
            Header();

            if (!Collapsed)
            {
                bool armed = PilotExtension.State == PilotExtension.PState.Active;

                var stateStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                stateStyle.normal.textColor = armed ? Color.green : Color.gray;
                GUILayout.Label(armed ? "● ARMED" : "○ Inactive", stateStyle);

                if (!string.IsNullOrEmpty(PilotExtension.LaunchStatus))
                    GUILayout.Label($"Status: {PilotExtension.LaunchStatus}");
                GUILayout.Label($"Energy: {PilotExtension.EnergyPer:0}%   Speed: {PilotExtension.Speed:0}");
                GUILayout.Label($"Warper: {(PilotExtension.HasWarper ? "yes" : "no")}   Warp: {PilotExtension.LastWarpReason}");

                GUILayout.BeginHorizontal();
                bool master = AutoPilotPlusPlugin.MasterEnabled.Value;
                if (GUILayout.Button(master ? "On" : "Off")) AutoPilotPlusPlugin.MasterEnabled.Value = !master;
                if (GUILayout.Button(armed ? "Disarm" : "Arm")) PilotExtension.ToggleArmed();
                GUILayout.EndHorizontal();

                AutoPilotPlusPlugin.AutoStart.Value = GUILayout.Toggle(AutoPilotPlusPlugin.AutoStart.Value, "Auto-arm on target select");
                AutoPilotPlusPlugin.AutoLaunch.Value = GUILayout.Toggle(AutoPilotPlusPlugin.AutoLaunch.Value, "Auto-launch from ground");
                AutoPilotPlusPlugin.AutoLand.Value = GUILayout.Toggle(AutoPilotPlusPlugin.AutoLand.Value, "Auto-land on arrival");
                AutoPilotPlusPlugin.IgnoreGravity.Value = GUILayout.Toggle(AutoPilotPlusPlugin.IgnoreGravity.Value, "Ignore gravity");
                AutoPilotPlusPlugin.LocalWarp.Value = GUILayout.Toggle(AutoPilotPlusPlugin.LocalWarp.Value, "Allow local warp");
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
