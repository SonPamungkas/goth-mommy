using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")]
    public static class Turret_OnInit_AirDefenseAux_Patch
    {
        private const float BuffedCiwsMaxRange = 12000f;
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return;
            WeaponStation[] stations = NavalBombardment.TurretWeaponStationsRef(__instance);
            if (stations == null || stations.Length == 0) return;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i]?.Weapons?.Count > 0 && stations[i].Weapons[0] is Gun g)
                {
                    WeaponInfo wi = stations[i].WeaponInfo ?? g.info;
                    if (wi != null && NavalBombardment.IsCiwsPointDefense(g, wi))
                    {
                        var req = wi.targetRequirements;
                        req.maxRange = Mathf.Max(req.maxRange, BuffedCiwsMaxRange);
                        wi.targetRequirements = req;
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "SetTarget")]
    public static class Turret_SetTarget_AirDefenseAux_Patch
    {
        private const float BuffedCiwsMaxRange = 12000f;
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return;
            WeaponStation ws = __instance.GetWeaponStation();
            if (ws?.Weapons == null || ws.Weapons.Count == 0) return;
            if (ws.Weapons[0] is Gun g)
            {
                WeaponInfo wi = ws.WeaponInfo ?? g.info;
                if (NavalBombardment.IsCiwsPointDefense(g, wi)) NavalBombardment.TurretMaxRangeRef(__instance) = BuffedCiwsMaxRange;
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class SARH_FixedUpdate_LaneCheck_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return true;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation _, out FoxCategory cat)) return true;
            if (cat != FoxCategory.Fox1_SARH) return true;
            Unit currentTarget = __instance.GetTarget();
            if (currentTarget is Missile m && SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID))
            {
                GothLog.Throttled("sarh.lane." + __instance.GetInstanceID(), $"TURRET SKIP (SARH): '{GothLog.Name(__instance.GetAttachedUnit())}' target '{GothLog.Name(m)}' already engaged -> withholding shot, lane respected.");
                return false; 
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class SARH_FixedUpdate_Cadence_Patch
    {
        private const float FastInterval = 1.0f; 
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat)) return;
            if (cat != FoxCategory.Fox1_SARH) return;
            NavalBombardment.TurretTargetAssessmentIntervalRef(__instance) = FastInterval;
            AimSolver aimSolver = NavalBombardment.TurretAimSolverRef(__instance);
            if (aimSolver != null) NavalBombardment.AimSolverSimulationIntervalRef(aimSolver) = FastInterval;
            List<TargetDetector> detectors = Turret_FixedUpdate_SAM_Patch.TurretTargetDetectorsRef(__instance);
            if (detectors != null)
            {
                for (int i = 0; i < detectors.Count; i++)
                {
                    TargetDetector d = detectors[i];
                    if (d == null || d is Radar) continue; 
                    Turret_FixedUpdate_SAM_Patch.DetectorCheckIntervalRef(d) = FastInterval;
                    Turret_FixedUpdate_SAM_Patch.DetectorAlertCheckIntervalRef(d) = FastInterval;
                }
            }
            if (ws.WeaponInfo != null) ws.WeaponInfo.fireInterval = FastInterval;
            if (ws.Weapons != null && ws.Weapons.Count > 0 && ws.Weapons[0] is MissileLauncher ml) RippleSam.MissileLauncherFireIntervalRef(ml) = FastInterval;
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class CIWS_FixedUpdate_Cadence_Patch
    {
        private const float FastInterval = 0.1f;
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || __instance == null) return;
            WeaponStation ws = __instance.GetWeaponStation();
            if (ws?.Weapons == null || ws.Weapons.Count == 0 || !(ws.Weapons[0] is Gun g)) return;
            WeaponInfo wi = ws.WeaponInfo ?? g.info;
            if (!NavalBombardment.IsCiwsPointDefense(g, wi)) return;
            RippleSam.TurretLockTimeRef(__instance) = FastInterval;
            NavalBombardment.TurretTargetAssessmentIntervalRef(__instance) = FastInterval;
            AimSolver aimSolver = NavalBombardment.TurretAimSolverRef(__instance);
            if (aimSolver != null) NavalBombardment.AimSolverSimulationIntervalRef(aimSolver) = FastInterval;
        }
    }
}