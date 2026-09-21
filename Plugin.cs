using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;
namespace GroundOverTheHorizon
{
    [BepInPlugin("neutral.gothmommy", "GOTH MOMMY", "2.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        private const string S_GENERAL = "General";
        private const string S_RADAR = "Radar";
        private const string S_OPTICS = "Optics";
        private const string S_NAVAL_BOMBARDMENT = "NavalBombardment";
        private const string S_GROUND_BALLISTICS = "GroundBallistics";
        private const string S_SAM_DEFENSE = "SAMDefense";
        private const string S_ARH_BUFF = "ARHBuff";
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
        public static ConfigEntry<float> Bombardment_ShipExitVelocity;
        public static ConfigEntry<bool> Bombardment_PrioritizeSurface;
        public static ConfigEntry<float> Bombardment_SelfDestructUncap;
        public static ConfigEntry<bool> Bombardment_GuidedShell45Elevation;
        public static ConfigEntry<float> Bombardment_MinCaliberNonNaval;
        public static ConfigEntry<bool> Ground_BallisticRangeCap_Enable;
        public static ConfigEntry<bool> SAM_EnableCoordinator;
        public static ConfigEntry<float> SAM_MissileSalvoInterval;
        public static ConfigEntry<float> SAM_BuddyDefenseRadius_Ship;
        public static ConfigEntry<float> SAM_BuddyDefenseRadius_VehicleOrBuilding;
        public static ConfigEntry<float> SAM_ThreatReengageCooldown;
        public static ConfigEntry<bool> SAM_RapidFire_SurfaceAndNavalOnly;
        public static ConfigEntry<float> SAM_Ripple_LockTime;
        public static ConfigEntry<bool> SAM_DebugLog;
        public static ConfigEntry<bool> SAM_TraceEveryCall;
        public static ConfigEntry<float> SAM_TraceThrottle;
        public static ConfigEntry<float> ARH_ClutterFactorScale;
        public static ConfigEntry<float> ARH_TerminalRangeMultiplier;
        public static bool SamDbg => SAM_DebugLog != null && SAM_DebugLog.Value;
        public static bool SamTrace => SAM_TraceEveryCall != null && SAM_TraceEveryCall.Value;
        private void Awake()
        {
            Log = Logger;
            EnableGoth = Config.Bind(S_GENERAL, "Enable", true,
                "Master switch for GOTH MOMMY radar and horizon overhaul.");
            VerboseLogging = Config.Bind(S_GENERAL, "VerboseLogging", false,
                "Enable verbose diagnostic logging for radar calculations (rate-limited).");
            Radar_EnableOTH = Config.Bind(S_RADAR, "EnableOTH", true,
                "Enables over-the-horizon tropospheric refraction (4/3 effective Earth radius) and RCS diffraction detection.");
            RefractionFactor = Config.Bind(S_RADAR, "RefractionFactor", 1.3333333f,
                "Tropospheric atmospheric refraction multiplier for effective Earth radius (standard 4/3 Earth radius = 1.3333).");
            SensitivityFactor = Config.Bind(S_RADAR, "SensitivityFactor", 0.03f,
                "Over-the-horizon diffraction slope sensitivity based on target RCS.");
            MaxRadarRangeCap = Config.Bind(S_RADAR, "MaxRadarRangeCap", 250000f,
                "Maximum broad spatial search radius for radars in meters (250km).");
            MOMMY_OpticsUpgrade_Enable = Config.Bind(S_OPTICS, "OpticsUpgrade_Enable", true,
                "Inject upgraded visual detection optics into surface radar and ship units.");
            MOMMY_VisualRange = Config.Bind(S_OPTICS, "VisualRange", 50000f,
                "Visual range applied to radar and ship units.");
            MOMMY_Magnification = Config.Bind(S_OPTICS, "Magnification", 8f,
                "Magnification applied to radar and ship units.");
            MOMMY_MaxSpeed = Config.Bind(S_OPTICS, "MaxSpeed", 1f,
                "Max speed threshold applied to radar and ship units.");
            Bombardment_Enable = Config.Bind(S_NAVAL_BOMBARDMENT, "Enable", true,
                "Enable over-the-horizon naval bombardment for railguns and guided-shell ship cannons.");
            Bombardment_ShipExitVelocity = Config.Bind(S_NAVAL_BOMBARDMENT, "ShipExitVelocity", 2000f,
                "Unified exit velocity in m/s for all ship-launched guided shells and naval bombardment cannons (default 2000 m/s).");
            Bombardment_PrioritizeSurface = Config.Bind(S_NAVAL_BOMBARDMENT, "PrioritizeSurface", true,
                "Prevent main bombardment turrets from targeting fast incoming missiles, keeping them focused on surface ships and ground targets.");
            Bombardment_SelfDestructUncap = Config.Bind(S_NAVAL_BOMBARDMENT, "SelfDestructUncap", 300f,
                "Overrides the bullet self-destruct timer on bombardment cannons to allow long-range flight without mid-air airbursts.");
            Bombardment_GuidedShell45Elevation = Config.Bind(S_NAVAL_BOMBARDMENT, "GuidedShell45Elevation", true,
                "Force 45 degree elevation for ship-launched guided shells and naval cannons when target is beyond 50km for maximum ballistic reach.");
            Bombardment_MinCaliberNonNaval = Config.Bind(S_NAVAL_BOMBARDMENT, "MinCaliberNonNaval", 0.1f,
                "Minimum projectile caliber in meters (default 0.1 = 100mm) required for non-naval (ground) units to receive the 2000 m/s velocity buff and 120km engagement boost. Prevents light ground mortars (< 100mm) from firing at Mach 5 while allowing heavy SPGs and artillery to receive full buffs.");
            Ground_BallisticRangeCap_Enable = Config.Bind(S_GROUND_BALLISTICS, "BallisticRangeCap_Enable", true,
                "Restricts ground artillery, howitzers, and direct-fire vehicles to their true physical ballistic limit instead of 120km.");
            SAM_EnableCoordinator = Config.Bind(S_SAM_DEFENSE, "EnableCoordinator", true,
                "Master switch for smart air defense salvo allocation, fleet-wide deconfliction, and threat pacing.");
            SAM_MissileSalvoInterval = Config.Bind(S_SAM_DEFENSE, "MissileSalvoInterval", 0.1f,
                "Minimum fire interval in seconds between successive launches when engaging incoming threat missiles (default 0.1s).");
            SAM_ThreatReengageCooldown = Config.Bind(S_SAM_DEFENSE, "ThreatReengageCooldown", 3.0f,
                "Minimum seconds before the same incoming threat may be engaged again. Mirrors the per-threat cooldown in the ImprovedAI peer mod. Stops a threat being re-bought every time slot bookkeeping blinks. Zero disables it.");
            SAM_BuddyDefenseRadius_Ship = Config.Bind(S_SAM_DEFENSE, "BuddyDefenseRadius_Ship", 10000f,
                "Defense radius in meters around a Ship air defense platform (default 10000m) to defend allied surface units and trigger emergency multilock SAM salvo mode when incoming missiles are detected.");
            SAM_BuddyDefenseRadius_VehicleOrBuilding = Config.Bind(S_SAM_DEFENSE, "BuddyDefenseRadius_VehicleOrBuilding", 50000f,
                "Defense radius in meters around a GroundVehicle/Building air defense platform (default 50000m) to defend allied surface units and trigger emergency multilock SAM salvo mode when incoming missiles are detected.");
            SAM_RapidFire_SurfaceAndNavalOnly = Config.Bind(S_SAM_DEFENSE, "RapidFire_SurfaceAndNavalOnly", true,
                "Restricts SAM salvo pacing exclusively to surface units and naval warships (aircraft dogfighters remain vanilla).");
            SAM_Ripple_LockTime = Config.Bind(S_SAM_DEFENSE, "Ripple_LockTime", 0.1f,
                "Turret.lockTime (seconds of tracking required before a SAM missile turret may fire) while its unit is threatened. Vanilla lockTime is otherwise untouched by RippleSam's other speed-ups. CIWS guns get their own unconditional lockTime reduction independently, via GothAirDefense.cs. Reverts to vanilla lockTime the instant the threat clears.");
            SAM_DebugLog = Config.Bind(S_SAM_DEFENSE, "DebugLog", false,
                "Log smart SAM salvo allocation, threat pacing events, and target tuning. Every gate reports why it allowed or refused, and every missile is logged with its SLine colour class so the log can be counted against the map.");
            SAM_TraceEveryCall = Config.Bind(S_SAM_DEFENSE, "TraceEveryCall", false,
                "FIREHOSE. Logs every call to the per-tick gates in Turret.FixedUpdate and WeaponStation.Ready, for every turret, every physics tick. Writes hundreds of MB to Player.log and costs frame rate. Intended for flights of a few seconds when a gate cannot be explained any other way. Requires DebugLog.");
            SAM_TraceThrottle = Config.Bind(S_SAM_DEFENSE, "TraceThrottle", 1.0f,
                "Minimum seconds between repeats of the same throttled diagnostic line on the per-tick paths. State changes always log immediately regardless of this. Zero logs every evaluation.");
            ARH_ClutterFactorScale = Config.Bind(S_ARH_BUFF, "ClutterFactorScale", 0.5f,
                "Scales ARHSeeker's radar clutterFactor down by this fraction at missile Initialize (default 0.5 = half strength), softening the ground-clutter signal penalty that causes lock loss against low-altitude targets.");
            ARH_TerminalRangeMultiplier = Config.Bind(S_ARH_BUFF, "TerminalRangeMultiplier", 2.0f,
                "Multiplies ARHSeeker's terminalRange (the range at which it switches from datalink to active radar terminal homing) at missile Initialize (default 2.0 = double, e.g. 12000m -> 24000m).");
            Log.LogInfo("Initializing GOTH MOMMY 2.0 (Multi Orbital Mapping & Monitoring Yield)...");
            var harmony = new Harmony("neutral.gothmommy");
            try
            {
                harmony.PatchAll();
                Log.LogInfo("GOTH MOMMY 2.0 patched successfully. Zero-allocation non-mutating pipeline active.");
                ReportPatchStatus(harmony);
            }
            catch (System.Exception e)
            {
                Log.LogError($"GOTH MOMMY failed to patch: {e}");
            }
        }
        private static void ReportPatchStatus(Harmony harmony)
        {
            string[] expected =
            {
                "Turret.FixedUpdate", "Turret.AssessTargetPriority", "Turret.ChooseTarget",
                "MissileLauncher.Fire",
                "Spawner.SpawnMissile", "Missile.TargetIDChanged",
                "Missile.Detonate", "Missile.UnitDisabled"
            };
            var found = new HashSet<string>();
            foreach (var m in harmony.GetPatchedMethods())
            {
                if (m == null || m.DeclaringType == null) continue;
                found.Add(m.DeclaringType.Name + "." + m.Name);
            }
            int ok = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                if (found.Contains(expected[i])) ok++;
                else Log.LogWarning($"{GothLog.Tag} PATCH MISSING: {expected[i]} is NOT patched. That patch is inert this session.");
            }
            Log.LogInfo($"{GothLog.Tag} Patch status: {ok}/{expected.Length} SAM targets bound. Total methods patched by this plugin: {found.Count}.");
            if (ok == expected.Length)
            {
                Log.LogInfo(GothLog.Tag + " All SAM patches bound. If the battery still does nothing, the cause is a gate, not a patch.");
            }
            Log.LogInfo($"{GothLog.Tag} DebugLog={SamDbg} TraceEveryCall={SamTrace} EnableGoth={(EnableGoth != null && EnableGoth.Value)} EnableCoordinator={(SAM_EnableCoordinator != null && SAM_EnableCoordinator.Value)}");
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
        private static readonly AccessTools.FieldRef<Radar, float> RadarConeRef = AccessTools.FieldRefAccess<Radar, float>("radarCone");
        private static readonly AccessTools.FieldRef<TargetDetector, Unit> AttachedUnitRef = AccessTools.FieldRefAccess<TargetDetector, Unit>("attachedUnit");
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
        private static readonly AccessTools.FieldRef<TargetDetector, float> VisualRangeRef = AccessTools.FieldRefAccess<TargetDetector, float>("visualRange");
        private static readonly AccessTools.FieldRef<TargetDetector, float> MagnificationRef = AccessTools.FieldRefAccess<TargetDetector, float>("magnification");
        private static readonly AccessTools.FieldRef<TargetDetector, float> MaxSpeedRef = AccessTools.FieldRefAccess<TargetDetector, float>("maxSpeed");
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