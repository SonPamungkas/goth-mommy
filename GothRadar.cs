using UnityEngine;
namespace GroundOverTheHorizon
{
    public static class GothFormula
    {
        public const float EarthRadius = 6371000f;
        public const float DefaultRefractionFactor = 1.3333333f;
        public static float CalculateEffectiveRadius(float refractionFactor)
        {
            return EarthRadius * (refractionFactor > 0.1f ? refractionFactor : DefaultRefractionFactor);
        }
        public static float CalculateHorizonDistance(float effectiveRadius, float height)
        {
            if (height <= 0f) return 0f;
            return Mathf.Sqrt(2f * effectiveRadius * height + height * height);
        }
        public static bool CanDetectOverHorizon(
            float effectiveRadius,
            float radarAlt,
            float targetAlt,
            float horizontalDistance,
            float rcs,
            float sensitivityFactor)
        {
            float horizonRadar = CalculateHorizonDistance(effectiveRadius, radarAlt);
            float horizonTarget = CalculateHorizonDistance(effectiveRadius, targetAlt);
            float totalHorizon = horizonRadar + horizonTarget;
            if (horizontalDistance <= totalHorizon)
            {
                return true;
            }
            float clampedRcs = Mathf.Clamp(rcs, 0.05f, 3.95f);
            float slope = (4.0f - clampedRcs) * sensitivityFactor;
            float requiredHeight = (horizontalDistance - totalHorizon) * slope;
            return targetAlt >= requiredHeight;
        }
    }
}