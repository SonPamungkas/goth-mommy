using System;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public static class GothArtilleryCalc
    {
        public const float Gravity = 9.81f;
        private const float MaxElevationRad = 1.553343f;
        public static float SolveAnalyticalElevation(float displacementH, float displacementV, float muzzleVelocity, bool highTrajectory = false, float gravity = Gravity)
        {
            if (displacementH < 0.1f) return displacementV >= 0f ? (float)(Math.PI * 0.5) : (float)(-Math.PI * 0.5);
            if (muzzleVelocity <= 1f) return (float)(Math.PI * 0.25);
            float v2 = muzzleVelocity * muzzleVelocity, v4 = v2 * v2, gx = gravity * displacementH;
            float term = gx * gx + 2f * displacementV * v2 * gravity, discriminant = v4 - term;
            if (discriminant < 0f) return Mathf.Clamp(0.25f * Mathf.PI + 0.5f * Mathf.Atan2(displacementV, displacementH), 0.1f, (float)(Math.PI * 0.25));
            float sqrtD = Mathf.Sqrt(discriminant), tanTheta = highTrajectory ? (v2 + sqrtD) / gx : (v2 - sqrtD) / gx;
            return Mathf.Clamp(Mathf.Atan(tanTheta), -MaxElevationRad, MaxElevationRad);
        }
        public static float GetElevation(float displacementH, float displacementV, float muzzleVelocity, int maxIterations, bool highTrajectory = false)
        {
            if (displacementH < 0.1f || muzzleVelocity < 1f) return (float)(Math.PI * 0.25);
            float theta = SolveAnalyticalElevation(displacementH, displacementV, muzzleVelocity, highTrajectory);
            if (maxIterations <= 0) return theta;
            float learningRate = 0.0001f, error = float.MaxValue;
            int iteration = 0;
            while (Mathf.Abs(error) > 5f && iteration < maxIterations)
            {
                iteration++;
                float vx = muzzleVelocity * Mathf.Cos(theta), vy = muzzleVelocity * Mathf.Sin(theta);
                if (vx <= 0.01f) break;
                float tApex = vy / Gravity, hFall = ((vy * vy) / (2f * Gravity)) - displacementV;
                if (hFall < 0f) { theta += 0.05f; continue; }
                error = (vx * (tApex + Mathf.Sqrt(2f * hFall / Gravity))) - displacementH;
                theta = Mathf.Clamp(theta - learningRate * error, 0.01f, MaxElevationRad);
            }
            return theta;
        }
    }
    [HarmonyPatch(typeof(ArtilleryCalc), "GetElevation", new Type[] { typeof(float), typeof(float), typeof(float), typeof(int) })]
    public static class ArtilleryCalc_GetElevation_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(float displacementH, float displacementV, float muzzleVelocity, int maxIterations, ref float __result)
        {
            if (!Plugin.EnableGoth.Value) return true;
            __result = GothArtilleryCalc.GetElevation(displacementH, displacementV, muzzleVelocity, maxIterations, highTrajectory: false);
            return false;
        }
    }
    [HarmonyPatch(typeof(Unit), "Awake")]
    public static class Unit_RCS_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Unit __instance)
        {
            if (Plugin.EnableGoth.Value && __instance?.definition != null && __instance is Missile && __instance.definition.radarSize <= 0f && __instance.definition.width > 0f)
            {
                __instance.definition.radarSize = __instance.definition.width * 0.25f;
                if (Plugin.SamDbg) Plugin.Log?.LogInfo($"[RCS Increaser] Fixed radarSize for {__instance.definition.unitName} to {__instance.definition.radarSize}");
            }
        }
    }
}