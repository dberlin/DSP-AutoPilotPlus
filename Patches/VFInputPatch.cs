using HarmonyLib;

namespace AutoPilotPlus.Patches
{
    /// <summary>
    /// Forces the "sail speed up" (boost) input on while the autopilot wants to accelerate. This is
    /// the same mechanism the original AutoPilot used — a postfix on the VFInput._sailSpeedUp getter.
    /// </summary>
    public static class VFInputPatch
    {
        [HarmonyPatch(typeof(VFInput), "_sailSpeedUp", MethodType.Getter)]
        [HarmonyPostfix]
        public static void SailSpeedUpPostfix(ref bool __result)
        {
            if (AutoPilotPlusPlugin.MasterEnabled != null &&
                AutoPilotPlusPlugin.MasterEnabled.Value &&
                PilotExtension.State == PilotExtension.PState.Active &&
                PilotExtension.InputSailSpeedUp)
            {
                __result = true;
            }
        }
    }
}
