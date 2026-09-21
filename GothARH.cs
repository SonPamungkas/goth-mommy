using HarmonyLib;
namespace GroundOverTheHorizon
{
    public static class ARHbuff
    {
        internal static readonly AccessTools.FieldRef<ARHSeeker, RadarParams> RadarParametersRef =
            AccessTools.FieldRefAccess<ARHSeeker, RadarParams>("radarParameters");
        internal static readonly AccessTools.FieldRef<ARHSeeker, float> TerminalRangeRef =
            AccessTools.FieldRefAccess<ARHSeeker, float>("terminalRange");
    }
    [HarmonyPatch(typeof(ARHSeeker), "Initialize")]
    public static class ARHSeeker_Initialize_ARHBuff_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ARHSeeker __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return;
            float clutterScale = Plugin.ARH_ClutterFactorScale != null ? Plugin.ARH_ClutterFactorScale.Value : 0.5f;
            RadarParams rp = ARHbuff.RadarParametersRef(__instance);
            rp.clutterFactor *= clutterScale;
            ARHbuff.RadarParametersRef(__instance) = rp;
            float rangeMultiplier = Plugin.ARH_TerminalRangeMultiplier != null ? Plugin.ARH_TerminalRangeMultiplier.Value : 2.0f;
            ARHbuff.TerminalRangeRef(__instance) *= rangeMultiplier;
        }
    }
}