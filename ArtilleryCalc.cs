using System;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public static class GothArtilleryCalc
    {
        public const float Gravity = 9.81f;
        private const float MaxElevationRad = 1.553343f; 
        public static float SolveAnalyticalElevation(
            float horizontalDistance,
            float verticalDisplacement,
            float muzzleVelocity,
            bool highTrajectory = false,
            float gravity = Gravity)
        {
            if (horizontalDistance < 0.1f)
            {
                return verticalDisplacement >= 0f ? (float)(Math.PI * 0.5) : (float)(-Math.PI * 0.5);
            }
            if (muzzleVelocity <= 1f)
            {
                return (float)(Math.PI * 0.25); 
            }
            float v2 = muzzleVelocity * muzzleVelocity;
            float v4 = v2 * v2;
            float gx = gravity * horizontalDistance;
            float term = gx * gx + 2f * verticalDisplacement * v2 * gravity;
            float discriminant = v4 - term;
            if (discriminant < 0f)
            {
                float maxRangeAngle = 0.25f * Mathf.PI + 0.5f * Mathf.Atan2(verticalDisplacement, horizontalDistance);
                return Mathf.Clamp(maxRangeAngle, 0.1f, (float)(Math.PI * 0.25));
            }
            float sqrtD = Mathf.Sqrt(discriminant);
            float tanTheta = highTrajectory ? (v2 + sqrtD) / gx : (v2 - sqrtD) / gx;
            float theta = Mathf.Atan(tanTheta);
            return Mathf.Clamp(theta, -MaxElevationRad, MaxElevationRad);
        }
        public static float GetElevation(
            float displacementH,
            float displacementV,
            float muzzleVelocity,
            int maxIterations,
            bool highTrajectory = false)
        {
            if (displacementH < 0.1f || muzzleVelocity < 1f)
            {
                return (float)(Math.PI * 0.25);
            }
            float theta = SolveAnalyticalElevation(displacementH, displacementV, muzzleVelocity, highTrajectory);
            if (maxIterations <= 0)
            {
                return theta;
            }
            float learningRate = 0.0001f;
            int iteration = 0;
            float error = float.MaxValue;
            while (Mathf.Abs(error) > 5f && iteration < maxIterations)
            {
                iteration++;
                float vx = muzzleVelocity * Mathf.Cos(theta);
                float vy = muzzleVelocity * Mathf.Sin(theta);
                if (vx <= 0.01f) break;
                float tApex = vy / Gravity;
                float hApex = (vy * vy) / (2f * Gravity); 
                float hFall = hApex - displacementV;
                if (hFall < 0f)
                {
                    theta += 0.05f;
                    continue;
                }
                float tFall = Mathf.Sqrt(2f * hFall / Gravity);
                float totalTime = tApex + tFall;
                float predictedX = vx * totalTime;
                error = predictedX - displacementH;
                theta -= learningRate * error;
                theta = Mathf.Clamp(theta, 0.01f, MaxElevationRad);
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
}