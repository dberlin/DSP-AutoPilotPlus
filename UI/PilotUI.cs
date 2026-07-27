using BepInEx.Configuration;
using CruiseAssistPlus;
using CruiseAssistPlus.Core;
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

            // The window style reserves DspSkin.TitleH at the top for the title bar, so the content
            // height is measured below it.
            Rect.height = DspSkin.TitleH + (Collapsed ? 4f : 196f);
            Rect = GUILayout.Window(WinId, Rect, Draw, "AutoPilot+");
            // Never let a saved position / UI scale strand it off-screen, and claim its area so clicks
            // on it don't also land on the world behind.
            UIUtil.Settle(ref Rect);
            if (_left != null) { _left.Value = Rect.x; _top.Value = Rect.y; _collapsed.Value = Collapsed; }
        }

        /// <summary>Collapse / config / close live in the title bar, matching CruiseAssist+ and DSP's own
        /// panels. Drawn before the drag bar so they claim their own clicks.</summary>
        private static void TitleBar()
        {
            if (UIUtil.TitleButton(Rect, 0, DspSkin.Glyph("✕", "X"))) Visible = false;
            // DSP's UI font has no gear, so ⚙ rendered as a blank button — fall back through what it does have.
            if (UIUtil.TitleButton(Rect, 1, DspSkin.Glyph("⚙", "≡", "="))) PilotConfigUI.Visible = !PilotConfigUI.Visible;
            if (UIUtil.TitleButton(Rect, 2, Collapsed ? "+" : DspSkin.Glyph("–", "-"))) Collapsed = !Collapsed;
        }

        private static void Draw(int id)
        {
            TitleBar();   // absolute positioning: outside the layout flow, so it costs no content space
            GUILayout.BeginVertical();

            if (!Collapsed)
            {
                bool armed = PilotExtension.State == PilotExtension.PState.Active;

                GUILayout.Label(armed ? DspSkin.Glyph("●", "*") + " ARMED" : DspSkin.Glyph("○", "-") + " Inactive",
                    DspSkin.Tinted(DspSkin.Status, armed ? DspSkin.Ok : DspSkin.InkDim));

                if (!string.IsNullOrEmpty(PilotExtension.LaunchStatus))
                    GUILayout.Label($"Status: {PilotExtension.LaunchStatus}");
                GUILayout.Label($"Energy: {PilotExtension.EnergyPer:0}%   Speed: {PilotExtension.Speed:0}");
                GUILayout.Label($"Warper: {(PilotExtension.HasWarper ? "yes" : "no")}   Warp: {PilotExtension.LastWarpReason}");

                GUILayout.BeginHorizontal();
                bool master = AutoPilotPlusPlugin.MasterEnabled.Value;
                if (GUILayout.Button(master ? "On" : "Off")) AutoPilotPlusPlugin.MasterEnabled.Value = !master;
                if (GUILayout.Button(armed ? "Disarm" : "Arm")) PilotExtension.ToggleArmed();
                GUILayout.EndHorizontal();

                AutoPilotPlusPlugin.AutoStart.Value = UIUtil.Toggle(AutoPilotPlusPlugin.AutoStart.Value, "Auto-arm on target select");
                AutoPilotPlusPlugin.AutoLaunch.Value = UIUtil.Toggle(AutoPilotPlusPlugin.AutoLaunch.Value, "Auto-launch from ground");
                AutoPilotPlusPlugin.AutoLand.Value = UIUtil.Toggle(AutoPilotPlusPlugin.AutoLand.Value, "Auto-land on arrival");
                AutoPilotPlusPlugin.IgnoreGravity.Value = UIUtil.Toggle(AutoPilotPlusPlugin.IgnoreGravity.Value, "Ignore gravity");
                AutoPilotPlusPlugin.LocalWarp.Value = UIUtil.Toggle(AutoPilotPlusPlugin.LocalWarp.Value, "Allow local warp");
            }

            GUILayout.EndVertical();
            UIUtil.DragBar(Rect);
        }
    }
}
