using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;
namespace GroundOverTheHorizon
{
    [BepInPlugin("neutral.gothmommy", "GOTH MOMMY", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> EnableGoth;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> Radar_EnableOTH;
        public static ConfigEntry<float> RefractionFactor;
        public static ConfigEntry<float> SensitivityFactor;
        public static ConfigEntry<float> MaxRadarRangeCap;
        public static ConfigEntry<bool> MOMMY_OpticsUpgrade_Enable;
        public static ConfigEntry<float> MOMMY_VisualRange;
        public static ConfigEntry<float> MOMMY_Magnification;
        public static ConfigEntry<float> MOMMY_MaxSpeed;
        public static ConfigEntry<bool> Bombardment_Enable;
        public static ConfigEntry<bool> Bombardment_UncappedRange;
        public static ConfigEntry<bool> Bombardment_UnifyShipVelocity;
        public static ConfigEntry<float> Bombardment_ShipExitVelocity;
        public static ConfigEntry<bool> Bombardment_PrioritizeSurface;
        public static ConfigEntry<float> Bombardment_SelfDestructUncap;
        public static ConfigEntry<bool> Bombardment_SuppressKineticProximityFuse;
        public static ConfigEntry<bool> Bombardment_GuidedShell45Elevation;
        public static ConfigEntry<float> Bombardment_GuidedShell45Threshold;
        public static ConfigEntry<float> Bombardment_GuidedShellMaxDiameterFor45;
        public static ConfigEntry<float> Bombardment_MinCaliberNonNaval;
        public static ConfigEntry<bool> Bombardment_DebugLog;
        public static ConfigEntry<bool> Ground_BallisticRangeCap_Enable;
        public static ConfigEntry<bool> Ground_DebugLog;
        public static ConfigEntry<bool> SAM_EnableCoordinator;
        public static ConfigEntry<bool> SAM_CapFox1_SARH;
        public static ConfigEntry<bool> SAM_CapFox2_IR;
        public static ConfigEntry<bool> SAM_CapFox3_ARH;
        public static ConfigEntry<float> SAM_AircraftCooldownSeconds;
        public static ConfigEntry<float> SAM_MissileCooldownSeconds;
        public static ConfigEntry<float> SAM_TerminalSelfDefenseDistance;
        public static ConfigEntry<bool> SAM_RapidFire_Fox2Fox3_Enable;
        public static ConfigEntry<float> SAM_Fox2_FireInterval;
        public static ConfigEntry<float> SAM_Fox3_FireInterval;
        public static ConfigEntry<float> SAM_MissileSalvoInterval;
        public static ConfigEntry<float> SAM_BuddyDefenseRadius;
        public static ConfigEntry<bool> SAM_RapidFire_SurfaceAndNavalOnly;
        public static ConfigEntry<float> SAM_FireControl_AirDefenseSalvoInterval;
        public static ConfigEntry<float> SAM_FireControl_AirDefenseAssessmentInterval;
        public static ConfigEntry<bool> SAM_DebugLog;
        public static ConfigEntry<bool> Bombardment_DoubleMuzzleVelocity => Bombardment_UnifyShipVelocity;
        public static ConfigEntry<float> Bombardment_MuzzleVelocityMultiplier => Bombardment_ShipExitVelocity;
        private void Awake()
        {
            Log = Logger;
            EnableGoth = Config.Bind("General", "Enable", true,
                "Master switch for GOTH MOMMY radar and horizon overhaul.");
            VerboseLogging = Config.Bind("General", "VerboseLogging", false,
                "Enable verbose diagnostic logging for radar calculations (rate-limited).");
            Radar_EnableOTH = Config.Bind("Radar", "EnableOTH", true,
                "Enables over-the-horizon tropospheric refraction (4/3 effective Earth radius) and RCS diffraction detection.");
            RefractionFactor = Config.Bind("Radar", "RefractionFactor", 1.3333333f,
                "Tropospheric atmospheric refraction multiplier for effective Earth radius (standard 4/3 Earth radius = 1.3333).");
            SensitivityFactor = Config.Bind("Radar", "SensitivityFactor", 0.03f,
                "Over-the-horizon diffraction slope sensitivity based on target RCS.");
            MaxRadarRangeCap = Config.Bind("Radar", "MaxRadarRangeCap", 250000f,
                "Maximum broad spatial search radius for radars in meters (250km).");
            MOMMY_OpticsUpgrade_Enable = Config.Bind("Optics", "OpticsUpgrade_Enable", true,
                "Inject upgraded visual detection optics into surface radar and ship units.");
            MOMMY_VisualRange = Config.Bind("Optics", "VisualRange", 50000f,
                "Visual range applied to radar and ship units.");
            MOMMY_Magnification = Config.Bind("Optics", "Magnification", 8f,
                "Magnification applied to radar and ship units.");
            MOMMY_MaxSpeed = Config.Bind("Optics", "MaxSpeed", 1f,
                "Max speed threshold applied to radar and ship units.");
            Bombardment_Enable = Config.Bind("NavalBombardment", "Enable", true,
                "Enable over-the-horizon naval bombardment for railguns and guided-shell ship cannons.");
            Bombardment_UncappedRange = Config.Bind("NavalBombardment", "UncappedRange", true,
                "Extend maximum targeting range to 120km and remove line-of-sight requirements on bombardment weapons.");
            Bombardment_UnifyShipVelocity = Config.Bind("NavalBombardment", "UnifyShipVelocity", true,
                "Unify all ship-launched guided shell and naval cannon exit velocities to a single standard (2000 m/s).");
            Bombardment_ShipExitVelocity = Config.Bind("NavalBombardment", "ShipExitVelocity", 2000f,
                "Unified exit velocity in m/s for all ship-launched guided shells and naval bombardment cannons (default 2000 m/s).");
            Bombardment_PrioritizeSurface = Config.Bind("NavalBombardment", "PrioritizeSurface", true,
                "Prevent main bombardment turrets from targeting fast incoming missiles, keeping them focused on surface ships and ground targets.");
            Bombardment_SelfDestructUncap = Config.Bind("NavalBombardment", "SelfDestructUncap", 300f,
                "Overrides the bullet self-destruct timer on bombardment cannons to allow long-range flight without mid-air airbursts.");
            Bombardment_SuppressKineticProximityFuse = Config.Bind("NavalBombardment", "SuppressKineticProximityFuse", true,
                "Disables proximity airburst fuses on heavy armor-piercing naval shells, forcing direct impact.");
            Bombardment_GuidedShell45Elevation = Config.Bind("NavalBombardment", "GuidedShell45Elevation", true,
                "Force 45 degree elevation for ship-launched guided shells and naval cannons when target is beyond 50km for maximum ballistic reach.");
            Bombardment_GuidedShell45Threshold = Config.Bind("NavalBombardment", "GuidedShell45Threshold", 50000f,
                "Distance threshold in meters (default 50000 = 50km) beyond which ship guided shells are forced to 45 degree elevation.");
            Bombardment_GuidedShellMaxDiameterFor45 = Config.Bind("NavalBombardment", "GuidedShellMaxDiameterFor45", 0.1f,
                "Maximum shell diameter in meters (default 0.1 = 100mm) to which the 45 degree elevation capping is applied. Shells with diameter >= this threshold remain completely vanilla.");
            Bombardment_MinCaliberNonNaval = Config.Bind("NavalBombardment", "MinCaliberNonNaval", 0.1f,
                "Minimum projectile caliber in meters (default 0.1 = 100mm) required for non-naval (ground) units to receive the 2000 m/s velocity buff and 120km engagement boost. Prevents light ground mortars (< 100mm) from firing at Mach 5 while allowing heavy SPGs and artillery to receive full buffs.");
            Bombardment_DebugLog = Config.Bind("NavalBombardment", "DebugLog", false,
                "Log naval bombardment targeting and firing solutions.");
            Ground_BallisticRangeCap_Enable = Config.Bind("GroundBallistics", "BallisticRangeCap_Enable", true,
                "Restricts ground artillery, howitzers, and direct-fire vehicles to their true physical ballistic limit instead of 120km.");
            Ground_DebugLog = Config.Bind("GroundBallistics", "DebugLog", false,
                "Log ground artillery ballistic envelope evaluations.");
            SAM_EnableCoordinator = Config.Bind("SAMDefense", "EnableCoordinator", true,
                "Master switch for smart air defense salvo allocation, fleet-wide deconfliction, and rapid fire.");
            SAM_CapFox1_SARH = Config.Bind("SAMDefense", "CapFox1_SARH", true,
                "Gate: Limits incoming Fox-1 (SARH) missiles to at most 1 per aerial target across the fleet.");
            SAM_CapFox2_IR = Config.Bind("SAMDefense", "CapFox2_IR", true,
                "Gate: Limits incoming Fox-2 (IR) missiles to at most 1 per aerial target across the fleet (prevents fleet IRM dumping).");
            SAM_CapFox3_ARH = Config.Bind("SAMDefense", "CapFox3_ARH", true,
                "Gate: Limits incoming Fox-3 (ARH) missiles to at most 1 per aerial target across the fleet.");
            SAM_AircraftCooldownSeconds = Config.Bind("SAMDefense", "AircraftCooldownSeconds", 3.0f,
                "Battle damage assessment cooldown in seconds after missile termination/flaring before an aircraft can be targeted again by that seeker type (bypassed < terminal self-defense distance).");
            SAM_MissileCooldownSeconds = Config.Bind("SAMDefense", "MissileCooldownSeconds", 0.0f,
                "Cooldown in seconds after missile termination before a threat missile target can be re-engaged (0 = instant re-engagement).");
            SAM_TerminalSelfDefenseDistance = Config.Bind("SAMDefense", "TerminalSelfDefenseDistance", 10000f,
                "Distance in meters (default 10km) within which aircraft BDA cooldown is bypassed for emergency point defense.");
            SAM_RapidFire_Fox2Fox3_Enable = Config.Bind("SAMDefense", "RapidFire_Fox2Fox3_Enable", true,
                "Increases fire rate of surface and naval Fox-2 (IR) and Fox-3 (ARH) multi-cell launchers to counter threat salvos.");
            SAM_Fox2_FireInterval = Config.Bind("SAMDefense", "Fox2_FireInterval", 0.25f,
                "Minimum fire interval in seconds between successive launches from surface/naval Fox-2 (IR) launchers (e.g., Andromeda WS0 IRM-S2).");
            SAM_Fox3_FireInterval = Config.Bind("SAMDefense", "Fox3_FireInterval", 0.35f,
                "Minimum fire interval in seconds between successive launches from surface/naval Fox-3 (ARH) launchers (e.g., NL-98, MRM-S4).");
            SAM_MissileSalvoInterval = Config.Bind("SAMDefense", "MissileSalvoInterval", 0.25f,
                "Minimum fire interval in seconds between successive launches when engaging incoming threat missiles (default 0.25s).");
            SAM_BuddyDefenseRadius = Config.Bind("SAMDefense", "BuddyDefenseRadius", 100f,
                "Defense radius in meters around a surface/naval air defense platform (default 100m) to defend allied surface units and trigger emergency multilock SAM salvo mode when incoming missiles are detected.");
            SAM_RapidFire_SurfaceAndNavalOnly = Config.Bind("SAMDefense", "RapidFire_SurfaceAndNavalOnly", true,
                "Restricts rapid fire rate exclusively to surface units and naval warships (aircraft dogfighters remain vanilla).");
            SAM_FireControl_AirDefenseSalvoInterval = Config.Bind("SAMDefense", "FireControl_AirDefenseSalvoInterval", 0.25f,
                "Overrides FireControl.salvoInterval for air defense salvos containing Fox-2/Fox-3 missiles, ensuring rapid sequential launch.");
            SAM_FireControl_AirDefenseAssessmentInterval = Config.Bind("SAMDefense", "FireControl_AirDefenseAssessmentInterval", 1.5f,
                "Reduces targetAssessmentInterval from vanilla 30s to 1.5s when air defense launchers are present.");
            SAM_DebugLog = Config.Bind("SAMDefense", "DebugLog", false,
                "Log smart SAM salvo allocation, rapid fire events, and target tuning.");
            Log.LogInfo("Initializing GOTH MOMMY 2.0 (Multi Orbital Mapping & Monitoring Yield)...");
            var harmony = new Harmony("neutral.gothmommy");
            try
            {
                harmony.PatchAll();
                Log.LogInfo("GOTH MOMMY 2.0 patched successfully. Zero-allocation non-mutating pipeline active.");
            }
            catch (System.Exception e)
            {
                Log.LogError($"GOTH MOMMY failed to patch: {e}");
            }
        }
    }
    [HarmonyPatch(typeof(DetectorManager), "RequestRadarCheck")]
    public static class DetectorManager_RequestRadarCheck_Patch
    {
        private static readonly AccessTools.FieldRef<DetectorManager, List<DetectionRequest>> LoSRequestsRef =
            AccessTools.FieldRefAccess<DetectorManager, List<DetectionRequest>>("LoSRequests");
        [HarmonyPrefix]
        public static bool Prefix(TargetDetector detector, Unit target, IRadarReturn radarReturn)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Radar_EnableOTH.Value) return true;
            if (detector == null || target == null || radarReturn == null) return true;
            var jobManager = SceneSingleton<JobManager>.i;
            if (jobManager == null || jobManager.detector == null) return true;
            Transform scanPoint = detector.GetScanPoint();
            if (scanPoint == null) return true;
            GlobalPosition globalPosition = scanPoint.GlobalPosition();
            GlobalPosition globalPosition2 = target.GlobalPosition();
            Vector3 vector = globalPosition2 - globalPosition;
            vector.y = 0f;
            float distance3D = FastMath.Distance(globalPosition, globalPosition2);
            float horizontalDist = vector.magnitude;
            float effectiveRadius = GothFormula.CalculateEffectiveRadius(Plugin.RefractionFactor.Value);
            float radarAlt = Mathf.Max(1f, globalPosition.y);
            float targetAlt = Mathf.Max(0f, globalPosition2.y);
            float rcs = target.RCS;
            bool canDetect = GothFormula.CanDetectOverHorizon(
                effectiveRadius,
                radarAlt,
                targetAlt,
                horizontalDist,
                rcs,
                Plugin.SensitivityFactor.Value
            );
            if (canDetect)
            {
                float num2 = Mathf.Sqrt(2f * effectiveRadius * radarAlt);
                float clutterFactor = 0f;
                if (horizontalDist < num2 && globalPosition2.y < globalPosition.y * (1f - horizontalDist / num2))
                {
                    float targetRadarAlt = Mathf.Max(0.1f, target.radarAlt);
                    float altDiff = Mathf.Max(0.1f, globalPosition.y - globalPosition2.y);
                    float num5 = distance3D * targetRadarAlt / altDiff;
                    clutterFactor += Mathf.Min(distance3D, 1000f) / Mathf.Max(0.1f, num5);
                }
                float safeRadarAlt = Mathf.Max(0.5f, target.radarAlt);
                clutterFactor += (target.maxRadius * target.maxRadius * 2f) / (safeRadarAlt * safeRadarAlt);
                List<DetectionRequest> requests = LoSRequestsRef(jobManager.detector);
                requests.Add(new DetectionRequest(detector, target, radarReturn, distance3D, clutterFactor));
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(Radar), "RadarCheck")]
    public static class Radar_RadarCheck_Patch
    {
        private static readonly AccessTools.FieldRef<Radar, float> RadarConeRef =
            AccessTools.FieldRefAccess<Radar, float>("radarCone");
        private static readonly AccessTools.FieldRef<TargetDetector, Unit> AttachedUnitRef =
            AccessTools.FieldRefAccess<TargetDetector, Unit>("attachedUnit");
        [HarmonyPrefix]
        public static bool Prefix(Radar __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Radar_EnableOTH.Value) return true;
            Unit attachedUnit = AttachedUnitRef(__instance);
            if (attachedUnit == null || attachedUnit.NetworkHQ == null) return true;
            Transform scanner = __instance.GetScanPoint();
            if (scanner == null) return true;
            GlobalPosition scannerPos = scanner.GlobalPosition();
            float radarCone = RadarConeRef(__instance);
            float searchRadius = Mathf.Max(__instance.RadarParameters.maxRange * 2f, Plugin.MaxRadarRangeCap.Value);
            foreach (FactionHQ allHQ in FactionRegistry.GetAllHQs())
            {
                if (allHQ == null || attachedUnit.NetworkHQ == allHQ) continue;
                var returns = allHQ.factionRadarReturn;
                int count = returns.Count;
                for (int i = 0; i < count; i++)
                {
                    if (UnitRegistry.TryGetUnit(returns[i], out var unit))
                    {
                        if (unit == null || unit.disabled) continue;
                        IRadarReturn radarReturn = unit as IRadarReturn;
                        if (radarReturn == null) continue;
                        if (FastMath.InRange(unit.GlobalPosition(), scannerPos, searchRadius))
                        {
                            if (radarCone > 0f)
                            {
                                Vector3 dirToTarget = unit.transform.position - scanner.position;
                                if (Vector3.Angle(dirToTarget, scanner.forward) > radarCone)
                                    continue;
                            }
                            DetectorManager.RequestRadarCheck(__instance, unit, radarReturn);
                        }
                    }
                }
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(Radar), "CanSeeRadarReturn")]
    public static class Radar_CanSeeRadarReturn_Patch
    {
        private static readonly AccessTools.FieldRef<TargetDetector, Unit> AttachedUnitRef =
            AccessTools.FieldRefAccess<TargetDetector, Unit>("attachedUnit");
        private static float _lastLogTime;
        [HarmonyPrefix]
        public static bool Prefix(Radar __instance, IRadarReturn radarReturn, float dist, float clutterFactor, ref bool __result)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Radar_EnableOTH.Value) return true;
            if (radarReturn == null || __instance == null) return true;
            Unit target = radarReturn as Unit;
            RadarParams baseParams = __instance.RadarParameters;
            float targetAlt = 0f;
            float rcs = 1.0f;
            if (target != null)
            {
                targetAlt = Mathf.Max(0f, target.transform.position.y);
                rcs = target.RCS;
            }
            float emitterAlt = Mathf.Max(0f, __instance.transform.position.y);
            float detectionMult = 1.0f + (targetAlt * 0.0005f * rcs);
            float lockMult = 1.0f + Mathf.Min(rcs, 3.0f);
            float boostedMaxRange = baseParams.maxRange * detectionMult * (1.0f + 0.15f * Mathf.Log10(emitterAlt + 1.0f));
            float reducedMinSignal = baseParams.minSignal / lockMult;
            RadarParams evalParams = new RadarParams(
                boostedMaxRange,
                baseParams.maxSignal,
                reducedMinSignal,
                baseParams.clutterFactor,
                baseParams.dopplerFactor
            );
            Transform scanPoint = __instance.GetScanPoint();
            Vector3 scannerPos = scanPoint != null ? scanPoint.position : __instance.transform.position;
            Unit attachedUnit = AttachedUnitRef(__instance);
            float returnSignal = radarReturn.GetRadarReturn(scannerPos, __instance, attachedUnit, dist, clutterFactor, evalParams, triggerWarning: true);
            if (returnSignal >= evalParams.minSignal)
            {
                __result = !__instance.IsJammed();
            }
            else
            {
                __result = false;
            }
            if (Plugin.VerboseLogging.Value && Time.time - _lastLogTime > 0.5f)
            {
                _lastLogTime = Time.time;
                string rName = __instance.name;
                string tName = target != null ? target.name : "Target";
                Plugin.Log.LogInfo($"[MOMMY] {rName} -> {tName} | Dist: {dist:F0}m | Alt: {targetAlt:F0}m | RCS: {rcs:F2} | Rng: {baseParams.maxRange:F0}->{evalParams.maxRange:F0} | MinSig: {baseParams.minSignal:F3}->{evalParams.minSignal:F3} | Score: {returnSignal:F3} (Detected: {__result})");
            }
            return false; 
        }
    }
    [HarmonyPatch(typeof(TargetDetector), "Awake")]
    public static class TargetDetector_Awake_Patch
    {
        private static readonly AccessTools.FieldRef<TargetDetector, float> VisualRangeRef =
            AccessTools.FieldRefAccess<TargetDetector, float>("visualRange");
        private static readonly AccessTools.FieldRef<TargetDetector, float> MagnificationRef =
            AccessTools.FieldRefAccess<TargetDetector, float>("magnification");
        private static readonly AccessTools.FieldRef<TargetDetector, float> MaxSpeedRef =
            AccessTools.FieldRefAccess<TargetDetector, float>("maxSpeed");
        [HarmonyPostfix]
        public static void Postfix(TargetDetector __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.MOMMY_OpticsUpgrade_Enable.Value) return;
            if (__instance == null) return;
            bool isRadarOrShip = (__instance is Radar) || (__instance.GetComponentInParent<Ship>() != null);
            if (isRadarOrShip)
            {
                VisualRangeRef(__instance) = Plugin.MOMMY_VisualRange.Value;
                MagnificationRef(__instance) = Plugin.MOMMY_Magnification.Value;
                MaxSpeedRef(__instance) = Plugin.MOMMY_MaxSpeed.Value;
            }
        }
    }
}