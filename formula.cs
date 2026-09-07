using UnityEngine;
namespace GroundOverTheHorizon
{
    public interface IRadarTarget
    {
        float Altitude { get; }
        float RadarCrossSection { get; }
        Vector3 Position { get; }
    }
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
    public class RadarSensor : MonoBehaviour
    {
        [SerializeField] private float antennaHeight = 10f;
        [SerializeField] private float refractionFactor = GothFormula.DefaultRefractionFactor;
        [SerializeField] private float sensitivityFactor = 0.03f;
        public bool CanDetect(IRadarTarget target)
        {
            if (target == null) return false;
            float targetHeight = target.Altitude;
            float distance = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(target.Position.x, 0, target.Position.z)
            );
            float effectiveRadius = GothFormula.CalculateEffectiveRadius(refractionFactor);
            return GothFormula.CanDetectOverHorizon(
                effectiveRadius,
                antennaHeight,
                targetHeight,
                distance,
                target.RadarCrossSection,
                sensitivityFactor
            );
        }
    }
}