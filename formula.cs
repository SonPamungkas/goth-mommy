using UnityEngine;

public interface IRadarTarget
{
    float Altitude { get; }
    float RadarCrossSection { get; }
    Vector3 Position { get; }
}

public class RadarSensor : MonoBehaviour
{
    [SerializeField] private float antennaHeight = 10f;
    private const float EarthRadius = 6371000f;
    private const float RefractionFactor = 1.3333f;
    private const float EffectiveRadius = EarthRadius * RefractionFactor;
    private const float SensitivityFactor = 1000f;

    public bool CanDetect(IRadarTarget target)
    {
        float targetHeight = target.Altitude;
        float distance = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), 
                                          new Vector3(target.Position.x, 0, target.Position.z));

        float horizonRadar = Mathf.Sqrt(2 * EffectiveRadius * antennaHeight + (antennaHeight * antennaHeight));
        float horizonTarget = Mathf.Sqrt(2 * EffectiveRadius * targetHeight + (targetHeight * targetHeight));
        float totalHorizon = horizonRadar + horizonTarget;

        if (distance <= totalHorizon)
        {
            return true;
        }

        float slope = (4.0f - Mathf.Clamp(target.RadarCrossSection, 0, 4)) * SensitivityFactor;
        float requiredHeight = (distance - totalHorizon) * slope;

        return targetHeight >= requiredHeight;
    }
}