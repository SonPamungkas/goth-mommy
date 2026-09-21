using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")]
    public static class Turret_OnInit_AirDefenseAux_Diagnostics
    {
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.SamDbg || __instance == null) return;
            WeaponStation[] stations = NavalBombardment.TurretWeaponStationsRef(__instance);
            if (stations == null || stations.Length == 0) return;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i]?.Weapons?.Count > 0 && stations[i].Weapons[0] is Gun g)
                {
                    WeaponInfo wi = stations[i].WeaponInfo ?? g.info;
                    if (wi == null) continue;
                    bool isCiws = NavalBombardment.IsCiwsPointDefense(g, wi);
                    Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|A1] '{GothLog.Name(NavalBombardment.TurretAttachedUnitRef(__instance))}' WS{i} '{wi.weaponName}': CIWS={isCiws} -> {(isCiws ? $"maxRange={wi.targetRequirements.maxRange:F0}" : "not CIWS, skipped")}");
                }
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class Turret_FixedUpdate_AirDefenseAux_Diagnostics
    {
        private static readonly Dictionary<int, FoxCategory> _reported = new Dictionary<int, FoxCategory>(16);
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.SamDbg || __instance == null) return;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat)) return;
            if (cat != FoxCategory.Fox1_SARH) { CheckCiws(__instance, ws); return; }
            int tk = __instance.GetInstanceID();
            if (_reported.TryGetValue(tk, out FoxCategory prev) && prev == cat) return;
            _reported[tk] = cat;
            AimSolver solver = NavalBombardment.TurretAimSolverRef(__instance);
            Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|A2] '{GothLog.Name(__instance.GetAttachedUnit())}' SARH cadence: targetAssessmentInterval={NavalBombardment.TurretTargetAssessmentIntervalRef(__instance):F2} simulationInterval={(solver != null ? NavalBombardment.AimSolverSimulationIntervalRef(solver) : -1f):F2} weaponInfo.fireInterval={(ws.WeaponInfo != null ? ws.WeaponInfo.fireInterval : -1f):F2}");
        }
        private static void CheckCiws(Turret turret, WeaponStation ws)
        {
            if (ws?.Weapons == null || ws.Weapons.Count == 0 || !(ws.Weapons[0] is Gun g)) return;
            WeaponInfo wi = ws.WeaponInfo ?? g.info;
            if (!NavalBombardment.IsCiwsPointDefense(g, wi)) return;
            int tk = turret.GetInstanceID();
            if (_reported.TryGetValue(tk, out FoxCategory prev) && prev == FoxCategory.Uncapped) return;
            _reported[tk] = FoxCategory.Uncapped; 
            AimSolver solver = NavalBombardment.TurretAimSolverRef(turret);
            Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|A2] '{GothLog.Name(turret.GetAttachedUnit())}' CIWS cadence: lockTime={RippleSam.TurretLockTimeRef(turret):F2} targetAssessmentInterval={NavalBombardment.TurretTargetAssessmentIntervalRef(turret):F2} simulationInterval={(solver != null ? NavalBombardment.AimSolverSimulationIntervalRef(solver) : -1f):F2}");
        }
    }
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")]
    public static class Turret_OnInit_Bombardment_Diagnostics
    {
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.SamDbg || __instance == null || !Plugin.Bombardment_Enable.Value) return;
            Unit u = NavalBombardment.TurretAttachedUnitRef(__instance);
            if (u == null || u is Aircraft) return;
            WeaponStation[] stations = NavalBombardment.TurretWeaponStationsRef(__instance);
            if (stations == null || stations.Length == 0) return;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i]?.Weapons?.Count > 0 && stations[i].Weapons[0] is Gun g)
                {
                    WeaponInfo wi = stations[i].WeaponInfo ?? g.info;
                    if (wi == null) continue;
                    bool qualifies = NavalBombardment.TryGetGunOrShellCaliber(g, wi, u, g.transform, out int cal, out bool isGuided);
                    string verdict = qualifies ? $"maxRange={wi.targetRequirements.maxRange:F0} bulletSelfDestruct={NavalBombardment.GunBulletSelfDestructRef(g):F0}s antiSurface={wi.effectiveness.antiSurface:F2} (caliber={cal} guided={isGuided})" : "not bombardment, skipped";
                    Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|N1] '{GothLog.Name(u)}' WS{i} '{wi.weaponName}': bombardment={qualifies} -> {verdict}");
                }
            }
        }
    }
    [HarmonyPatch(typeof(AimSolver), "GetAimVector")]
    public static class AimSolver_GetAimVector_Bombardment_Diagnostics
    {
        public struct State { public float OrigMuzzleVelocity; public bool WasBombardment; }
        [HarmonyPrefix]
        public static void Prefix(AimSolver __instance, out State __state)
        {
            __state = new State { WasBombardment = false };
            if (!Plugin.SamDbg || __instance == null) return;
            WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance);
            if (wi == null) return;
            __state = new State { OrigMuzzleVelocity = wi.muzzleVelocity, WasBombardment = true };
        }
        [HarmonyPostfix]
        public static void Postfix(AimSolver __instance, State __state)
        {
            if (!Plugin.SamDbg || !__state.WasBombardment || __instance == null) return;
            WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance);
            if (wi == null || wi.muzzleVelocity == __state.OrigMuzzleVelocity) return; 
            Unit t = NavalBombardment.AimSolverCurrentTargetRef(__instance);
            Transform ft = NavalBombardment.AimSolverFiringTransformRef(__instance);
            float dist = (t != null && ft != null) ? FastMath.Distance(ft.GlobalPosition(), t.GlobalPosition()) : -1f;
            Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|N2] '{wi.weaponName}' -> '{GothLog.Name(t)}' dist={dist:F0}m: muzzleVelocity {__state.OrigMuzzleVelocity:F0} -> {wi.muzzleVelocity:F0} (x{wi.muzzleVelocity / Mathf.Max(1f, __state.OrigMuzzleVelocity):F1})");
        }
    }
    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    public static class Gun_SpawnBullet_Bombardment_Diagnostics
    {
        public struct State { public float OrigMuzzleVelocity, OrigSelfDestruct; }
        [HarmonyPrefix]
        public static void Prefix(Gun __instance, out State __state)
        {
            __state = new State { OrigMuzzleVelocity = 0f, OrigSelfDestruct = 0f };
            if (!Plugin.SamDbg || __instance == null) return;
            __state = new State { OrigMuzzleVelocity = NavalBombardment.GunMuzzleVelocityRef(__instance), OrigSelfDestruct = NavalBombardment.GunBulletSelfDestructRef(__instance) };
        }
        [HarmonyPostfix]
        public static void Postfix(Gun __instance, State __state)
        {
            if (!Plugin.SamDbg || __instance == null) return;
            float mv = NavalBombardment.GunMuzzleVelocityRef(__instance), sd = NavalBombardment.GunBulletSelfDestructRef(__instance);
            if (mv == __state.OrigMuzzleVelocity && sd == __state.OrigSelfDestruct) return; 
            Plugin.Log.LogInfo($"{GothLog.Tag} [GOTH|N3] '{(__instance.info != null ? __instance.info.weaponName : __instance.name)}': muzzleVelocity {__state.OrigMuzzleVelocity:F0} -> {mv:F0}, bulletSelfDestruct {__state.OrigSelfDestruct:F1}s -> {sd:F1}s");
        }
    }
}