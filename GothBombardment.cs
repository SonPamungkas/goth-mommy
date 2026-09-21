using System; using System.Collections.Generic; using System.Reflection; using System.Text.RegularExpressions; using HarmonyLib; using UnityEngine;
namespace GroundOverTheHorizon {
    public static class NavalBombardment {
        public static readonly Dictionary<WeaponInfo, float> OrigMuzzleVelocities = new Dictionary<WeaponInfo, float>();
        public static readonly Dictionary<Turret, bool> BombardmentTurrets = new Dictionary<Turret, bool>();
        internal static readonly AccessTools.FieldRef<Gun, MissileDefinition> GunGuidedProjectileRef = AccessTools.FieldRefAccess<Gun, MissileDefinition>("guidedProjectile");
        internal static readonly AccessTools.FieldRef<Gun, float> GunMuzzleVelocityRef = AccessTools.FieldRefAccess<Gun, float>("muzzleVelocity"), GunBulletSelfDestructRef = AccessTools.FieldRefAccess<Gun, float>("bulletSelfDestruct"), GunFireIntervalRef = AccessTools.FieldRefAccess<Gun, float>("fireInterval");
        internal static readonly AccessTools.FieldRef<Gun, Unit> GunProximityFuseTargetRef = AccessTools.FieldRefAccess<Gun, Unit>("proximityFuseTarget");
        internal static readonly AccessTools.FieldRef<Weapon, Unit> WeaponCurrentTargetRef = AccessTools.FieldRefAccess<Weapon, Unit>("currentTarget");
        internal static readonly AccessTools.FieldRef<Weapon, WeaponStation> WeaponStationRef = AccessTools.FieldRefAccess<Weapon, WeaponStation>("weaponStation");
        internal static readonly AccessTools.FieldRef<BulletSim, WeaponInfo> BulletSimWeaponInfoRef = AccessTools.FieldRefAccess<BulletSim, WeaponInfo>("weaponInfo");
        internal static readonly AccessTools.FieldRef<Turret, int> TurretAcqModeRef = AccessTools.FieldRefAccess<Turret, int>("targetAcquisitionMode");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretMaxRangeRef = AccessTools.FieldRefAccess<Turret, float>("maxRange"), TurretMaxElevRef = AccessTools.FieldRefAccess<Turret, float>("maxElevation"), TurretTargetRangeRef = AccessTools.FieldRefAccess<Turret, float>("targetRange"), TurretTargetAssessmentIntervalRef = AccessTools.FieldRefAccess<Turret, float>("targetAssessmentInterval");
        internal static readonly AccessTools.FieldRef<Turret, AimSolver> TurretAimSolverRef = AccessTools.FieldRefAccess<Turret, AimSolver>("aimSolver");
        internal static readonly AccessTools.FieldRef<AimSolver, float> AimSolverSimulationIntervalRef = AccessTools.FieldRefAccess<AimSolver, float>("simulationInterval");
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretFiresWithoutAimingRef = AccessTools.FieldRefAccess<Turret, bool>("firesWithoutAiming");
        internal static readonly AccessTools.FieldRef<Turret, Unit> TurretAttachedUnitRef = AccessTools.FieldRefAccess<Turret, Unit>("attachedUnit"), TurretTargetRef = AccessTools.FieldRefAccess<Turret, Unit>("target");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation[]> TurretWeaponStationsRef = AccessTools.FieldRefAccess<Turret, WeaponStation[]>("weaponStations");
        internal static readonly AccessTools.FieldRef<AimSolver, Unit> AimSolverAttachedUnitRef = AccessTools.FieldRefAccess<AimSolver, Unit>("attachedUnit"), AimSolverCurrentTargetRef = AccessTools.FieldRefAccess<AimSolver, Unit>("currentTarget");
        internal static readonly AccessTools.FieldRef<AimSolver, Transform> AimSolverFiringTransformRef = AccessTools.FieldRefAccess<AimSolver, Transform>("firingTransform");
        internal static readonly AccessTools.FieldRef<AimSolver, WeaponInfo> AimSolverWeaponInfoRef = AccessTools.FieldRefAccess<AimSolver, WeaponInfo>("weaponInfo");
        private static readonly Regex CaliberMmRegex = new Regex(@"(?<![a-zA-Z0-9])(\d+(?:\.\d+)?)\s*[-_]?\s*mm(?![a-zA-Z])", RegexOptions.IgnoreCase), CaliberUnderscoreRegex = new Regex(@"(?:Shell|Artillery|Gun|Mortar|Cannon|Deckgun|Turret)[-_](\d{2,3})(?:[-_]|$)", RegexOptions.IgnoreCase);
        public static int ExtractCaliberFromString(string text) {
            if (string.IsNullOrEmpty(text)) return 0;
            Match m = CaliberMmRegex.Match(text); if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mm) && mm > 0f) return (int)Math.Round(mm);
            m = CaliberUnderscoreRegex.Match(text); if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float num) && num > 0f) return (int)Math.Round(num);
            return ExtractCaliberFromUnitName(text);
        }
        public static int GetGuidedShellCaliber(Unit u) {
            if (u?.weaponStations == null) return 0;
            for (int i = 0; i < u.weaponStations.Count; i++) if (u.weaponStations[i]?.Weapons?.Count > 0 && u.weaponStations[i].Weapons[0] is Gun g) { var gp = GunGuidedProjectileRef(g); if (gp != null && !string.IsNullOrEmpty(gp.unitName)) { int c = ExtractCaliberFromString(gp.unitName); if (c > 0) return c; } } return 0;
        }
        public static int ExtractCaliberFromUnitName(string name) {
            if (string.IsNullOrEmpty(name)) return 0;
            string[] words = name.Split(new[] { ' ', '\t', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++) { int idx = words[i].IndexOf("mm", StringComparison.OrdinalIgnoreCase); if (idx >= 0) { if (int.TryParse(words[i].Substring(0, idx), out int cal)) return cal; if (idx == 0 && i > 0 && int.TryParse(words[i - 1], out int pCal)) return pCal; } } return 0;
        }
        public static bool TryGetGuidedShellCaliber(Gun g, Unit u, out int c) {
            c = 0;
            if (g != null) { var gp = GunGuidedProjectileRef(g); if (gp != null) { c = ExtractCaliberFromString(gp.unitName); if (c == 0) c = ExtractCaliberFromString(gp.name); if (c == 0) c = GetGuidedShellCaliber(u); return true; } }
            if (u != null) { c = GetGuidedShellCaliber(u); if (c > 0) return true; } return false;
        }
        public static bool IsShipPlatform(Transform t, Unit u) => u is Ship || (t != null && (t.GetComponentInParent<Ship>() != null || t.GetComponentInParent<Unit>() is Ship));
        public static bool IsArtilleryDesignated(Unit u) => u != null && ((u.definition is VehicleDefinition v && v.vehicleType == VehicleType.ART) || u.GetComponent<MobileArtilleryAI>() != null || u.GetComponentInChildren<MobileArtilleryAI>() != null || (u.definition?.code?.IndexOf("ART", StringComparison.OrdinalIgnoreCase) >= 0));
        public static bool IsCiwsPointDefense(Gun g, WeaponInfo wi) {
            if (g == null || wi == null) return false;
            float fi = GunFireIntervalRef(g);
            return fi > 0f && fi < 0.5f && (wi.effectiveness.antiAir > 0f || wi.effectiveness.antiMissile > 0f);
        }
        public struct CaliberCache { public int cal; public bool isGuided; public bool isValid; }
        private static readonly Dictionary<WeaponInfo, CaliberCache> _calCache = new Dictionary<WeaponInfo, CaliberCache>();
        public static bool TryGetGunOrShellCaliber(Gun g, WeaponInfo wi, Unit u, Transform t, out int c, out bool isGuided) {
            c = 0; isGuided = false;
            bool hasSpawnedProjectile = g != null && GunGuidedProjectileRef(g) != null;
            if (!hasSpawnedProjectile && IsCiwsPointDefense(g, wi)) { if (wi != null) _calCache[wi] = new CaliberCache { cal = 0, isGuided = false, isValid = false }; return false; }
            if (wi != null && _calCache.TryGetValue(wi, out var cached)) { c = cached.cal; isGuided = cached.isGuided; return cached.isValid; }
            if (g != null) { var gp = GunGuidedProjectileRef(g); if (gp != null) { isGuided = true; c = ExtractCaliberFromString(gp.unitName); if (c == 0) c = ExtractCaliberFromString(gp.name); if (c == 0) c = gp.width > 0f ? (int)Math.Round(gp.width * 1000f) : (gp.height > 0f ? (int)Math.Round(gp.height * 1000f) : 0); } }
            if (!isGuided && wi?.weaponPrefab != null) {
                Gun pg = wi.weaponPrefab.GetComponent<Gun>() ?? wi.weaponPrefab.GetComponentInChildren<Gun>();
                if (pg != null) { var gp = GunGuidedProjectileRef(pg); if (gp != null) { isGuided = true; c = ExtractCaliberFromString(gp.unitName); if (c == 0) c = ExtractCaliberFromString(gp.name); if (c == 0) c = gp.width > 0f ? (int)Math.Round(gp.width * 1000f) : (gp.height > 0f ? (int)Math.Round(gp.height * 1000f) : 0); } }
                if (!isGuided) { Missile m = wi.weaponPrefab.GetComponent<Missile>() ?? wi.weaponPrefab.GetComponentInChildren<Missile>(); if (m != null) { var s = m.GetComponent<MissileSeeker>(); if (s is OpticalSeekerShell || s is InertialSeekerShell || m.GetComponent<OpticalSeekerShell>() != null || m.GetComponent<InertialSeekerShell>() != null) isGuided = true; } if (!isGuided && (wi.weaponPrefab.GetComponentInChildren<OpticalSeekerShell>() != null || wi.weaponPrefab.GetComponentInChildren<InertialSeekerShell>() != null)) isGuided = true; }
            }
            if (!isGuided && u != null && TryGetGuidedShellCaliber(g, u, out int gc)) { c = gc; isGuided = true; }
            if (c == 0 && wi != null) { c = ExtractCaliberFromString(wi.weaponName); if (c == 0) c = ExtractCaliberFromString(wi.shortName); if (c == 0) c = ExtractCaliberFromString(wi.name); if (c == 0 && wi.weaponPrefab != null) { c = ExtractCaliberFromString(wi.weaponPrefab.name); if (c == 0) { var ts = wi.weaponPrefab.GetComponentsInChildren<Transform>(true); if (ts != null) for (int i = 0; i < ts.Length && i < 20; i++) { c = ExtractCaliberFromString(ts[i].name); if (c > 0) break; } } } }
            if (c == 0 && g != null) c = ExtractCaliberFromString(g.name);
            if (c == 0 && t != null) { Transform curr = t; int depth = 0; while (curr != null && depth++ < 15) { c = ExtractCaliberFromString(curr.name); if (c > 0) break; curr = curr.parent; } }
            if (c == 0 && u?.weaponStations != null) for (int i = 0; i < u.weaponStations.Count; i++) { var ws = u.weaponStations[i]; if (ws == null) continue; var wswi = ws.WeaponInfo ?? (ws.Weapons?.Count > 0 ? ws.Weapons[0].info : null); if (wswi != null) { int x = ExtractCaliberFromString(wswi.weaponName); if (x == 0) x = ExtractCaliberFromString(wswi.shortName); if (x == 0) x = ExtractCaliberFromString(wswi.name); if (x > 0) { c = x; break; } } }
            if (c == 0 && wi?.weaponPrefab != null) { var cap = wi.weaponPrefab.GetComponent<CapsuleCollider>() ?? wi.weaponPrefab.GetComponentInChildren<CapsuleCollider>(); if (cap != null && cap.radius > 0f) c = (int)Math.Round(cap.radius * 2000f); else { var sph = wi.weaponPrefab.GetComponent<SphereCollider>() ?? wi.weaponPrefab.GetComponentInChildren<SphereCollider>(); if (sph != null && sph.radius > 0f) c = (int)Math.Round(sph.radius * 2000f); } }
            bool isShip = IsShipPlatform(t, u), isArt = IsArtilleryDesignated(u), isRailgun = wi != null && wi.gun && wi.muzzleVelocity >= 1800f;
            bool isValid = false;
            if (isGuided || isRailgun) isValid = true;
            else if (wi != null && wi.gun && !wi.missile) { if (isShip || wi.overHorizon) isValid = true; else if (isArt) isValid = c == 0 || c >= (Plugin.Bombardment_MinCaliberNonNaval?.Value ?? 0.1f) * 1000f; else if (c >= 76 || wi.muzzleVelocity >= 500f) isValid = true; }
            if (wi != null) _calCache[wi] = new CaliberCache { cal = c, isGuided = isGuided, isValid = isValid };
            return isValid;
        }
        public static float GetGroundBallisticRange(Turret t, Unit u) { if (t == null) return 120000f; float v = Plugin.Bombardment_ShipExitVelocity.Value; if (v <= 10f) return 120000f; float e = TurretMaxElevRef(t); return Mathf.Clamp((v * v / 9.81f) * Mathf.Sin(2f * (e > 0f ? Mathf.Min(e, 45f) : 45f) * Mathf.Deg2Rad), 1000f, 120000f); }
        public static float GetOriginalMuzzleVelocity(WeaponInfo wi) { if (wi == null) return 1000f; if (!OrigMuzzleVelocities.TryGetValue(wi, out float v)) OrigMuzzleVelocities[wi] = v = wi.muzzleVelocity > 0f ? wi.muzzleVelocity : 1000f; return v; }
        public static void CacheOriginalMuzzleVelocity(WeaponInfo wi) { if (wi != null && wi.muzzleVelocity > 0f && !OrigMuzzleVelocities.ContainsKey(wi)) OrigMuzzleVelocities[wi] = wi.muzzleVelocity; }
        public static bool TryGetOriginalMuzzleVelocity(WeaponInfo wi, out float orig) { orig = 0f; if (wi != null && OrigMuzzleVelocities.TryGetValue(wi, out orig)) return true; return false; }
        public static bool TryGetTargetDistance(Gun g, out float d, out Unit t) {
            d = 0f; t = null; if (g == null) return false;
            if ((t = WeaponCurrentTargetRef(g)) != null && !t.disabled) { d = FastMath.Distance(g.transform.position, t.transform.position); return true; }
            if ((t = GunProximityFuseTargetRef(g)) != null && !t.disabled) { d = FastMath.Distance(g.transform.position, t.transform.position); return true; }
            var ws = WeaponStationRef(g); Turret tr = ws != null ? ws.GetTurret() : g.GetComponentInParent<Turret>();
            if (tr != null) { if ((t = TurretTargetRef(tr)) != null && !t.disabled) { d = FastMath.Distance(g.transform.position, t.transform.position); return true; } float trRange = TurretTargetRangeRef(tr); if (trRange > 0f) { d = trRange; return true; } }
            return false;
        }
        public static void UnpatchQolTargetFilter() {
            try {
                Assembly qol = null; foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == "qol") { qol = a; break; }
                Type pt = qol?.GetType("qol.TurretTargetFilterPatch2"); if (pt == null) return;
                var h = new Harmony("neutral.gothmommy.qoloverride"); MethodInfo pre = AccessTools.Method(pt, "FilterTargets_Prefix"), post = AccessTools.Method(pt, "FilterTargets_Postfix"), tgt = AccessTools.Method(typeof(Turret), "AssessTargetPriority");
                if (tgt != null && pre != null) h.Unpatch(tgt, pre); if (tgt != null && post != null) h.Unpatch(tgt, post); Plugin.Log?.LogInfo("[NavalBombardment] Unpatched QoL target filter.");
            } catch (Exception ex) { Plugin.Log?.LogWarning("[NavalBombardment] QoL unpatch error: " + ex.Message); }
        }
    }
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")] public static class Turret_OnInit_Bombardment_Patch {
        [HarmonyPrefix] public static void Prefix(Turret __instance) {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            Unit u = NavalBombardment.TurretAttachedUnitRef(__instance); WeaponStation[] ws = NavalBombardment.TurretWeaponStationsRef(__instance);
            if (u == null || u is Aircraft || ws == null || ws.Length == 0) return;
            bool isBombardment = false; float maxR = 0f;
            for (int i = 0; i < ws.Length; i++) {
                if (ws[i]?.Weapons?.Count > 0 && ws[i].Weapons[0] is Gun g) {
                    WeaponInfo wi = ws[i].WeaponInfo ?? g.info;
                    if (wi != null && NavalBombardment.TryGetGunOrShellCaliber(g, wi, u, g.transform, out int cal, out bool isG)) {
                        isBombardment = true; ws[i].WeaponInfo = wi; ws[i].TypeLookup?.Clear();
                        float r = 120000f; var req = wi.targetRequirements; req.maxRange = Mathf.Max(req.maxRange, r); req.lineOfSight = false; wi.targetRequirements = req; wi.overHorizon = wi.strategic = true;
                        var eff = wi.effectiveness; eff.antiSurface = Mathf.Max(eff.antiSurface, 0.85f); wi.effectiveness = eff;
                        if (r > maxR) maxR = r;
                        float sd = Plugin.Bombardment_SelfDestructUncap.Value; if (NavalBombardment.GunBulletSelfDestructRef(g) < sd) NavalBombardment.GunBulletSelfDestructRef(g) = sd;
                    }
                }
            }
            if (isBombardment) { NavalBombardment.TurretAcqModeRef(__instance) = 3; NavalBombardment.TurretMaxRangeRef(__instance) = (!(u is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value) ? Mathf.Min(maxR, NavalBombardment.GetGroundBallisticRange(__instance, u)) : maxR; NavalBombardment.TurretFiresWithoutAimingRef(__instance) = false; NavalBombardment.BombardmentTurrets[__instance] = true; }
        }
        [HarmonyPostfix] public static void Postfix(Turret __instance) {
            if (Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && __instance != null && NavalBombardment.BombardmentTurrets.ContainsKey(__instance)) {
                Unit u = NavalBombardment.TurretAttachedUnitRef(__instance);
                NavalBombardment.TurretAcqModeRef(__instance) = 3; NavalBombardment.TurretMaxRangeRef(__instance) = (u != null && !(u is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value) ? Mathf.Min(120000f, NavalBombardment.GetGroundBallisticRange(__instance, u)) : 120000f; NavalBombardment.TurretFiresWithoutAimingRef(__instance) = false;
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")] public static class Turret_AssessTarget_Bombardment_Patch {
        [HarmonyPrefix] public static bool Prefix(Turret __instance, Unit targetCandidate) => !(Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && Plugin.Bombardment_PrioritizeSurface.Value && __instance != null && targetCandidate != null && !targetCandidate.disabled && NavalBombardment.BombardmentTurrets.ContainsKey(__instance) && (targetCandidate is Aircraft || targetCandidate is Missile || (targetCandidate.radarAlt > 20f && !(targetCandidate is Building))));
    }
    [HarmonyPatch(typeof(Turret), "SetTarget")] public static class Turret_SetTarget_Bombardment_Patch {
        [HarmonyPrefix] public static bool Prefix(Turret __instance, PersistentID id) => !(Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && __instance != null && NavalBombardment.BombardmentTurrets.ContainsKey(__instance) && id.IsValid && UnitRegistry.TryGetUnit(id, out Unit c) && (c is Aircraft || c is Missile));
        [HarmonyPostfix] public static void Postfix(Turret __instance) { if (Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && __instance != null && NavalBombardment.BombardmentTurrets.ContainsKey(__instance)) { Unit u = NavalBombardment.TurretAttachedUnitRef(__instance); NavalBombardment.TurretMaxRangeRef(__instance) = (u != null && !(u is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value) ? Mathf.Min(120000f, NavalBombardment.GetGroundBallisticRange(__instance, u)) : 120000f; } }
    }
    [HarmonyPatch(typeof(AimSolver), "GetAimVector")] public static class AimSolver_GetAimVector_Patch {
        public struct State { public float OrigMuzzleVelocity, OrigMaxSpeed; public bool Modified; }
        [HarmonyPrefix] public static bool Prefix(AimSolver __instance, ref float targetRange, ref Vector3 __result, out State __state) {
            __state = new State { Modified = false };
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return true;
            Unit t = NavalBombardment.AimSolverCurrentTargetRef(__instance), u = NavalBombardment.AimSolverAttachedUnitRef(__instance); Transform ft = NavalBombardment.AimSolverFiringTransformRef(__instance); WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance);
            if (t == null || t.disabled || t is Aircraft || t is Missile || ft == null || wi == null || wi.missile || !wi.gun || (u = u ?? ft.GetComponentInParent<Unit>()) == null || u is Aircraft) return true;
            if (!NavalBombardment.TryGetGunOrShellCaliber(ft.GetComponent<Gun>() ?? ft.GetComponentInParent<Gun>(), wi, u, ft, out int cal, out bool isG)) return true;
            GlobalPosition mPos = ft.GlobalPosition(), tPos = t.GlobalPosition(); float dist = FastMath.Distance(tPos, mPos);
            if (dist < 40000f) return true;
            float ov = NavalBombardment.GetOriginalMuzzleVelocity(wi); Turret tr = ft.GetComponentInParent<Turret>();
            if (dist < 80000f) {
                if (cal > 0 && cal < 100 && Plugin.Bombardment_GuidedShell45Elevation.Value) { __state = new State { OrigMuzzleVelocity = wi.muzzleVelocity, OrigMaxSpeed = wi.maxSpeed, Modified = true }; wi.muzzleVelocity = wi.maxSpeed = ov * 2f; Calc45(mPos, tPos, t, u, tr, wi.muzzleVelocity, dist, out __result, out targetRange); return false; }
                __state = new State { OrigMuzzleVelocity = wi.muzzleVelocity, OrigMaxSpeed = wi.maxSpeed, Modified = true }; wi.muzzleVelocity = wi.maxSpeed = ov * 1.5f; return true;
            }
            if (cal > 0 && cal < 100 && Plugin.Bombardment_GuidedShell45Elevation.Value) { __state = new State { OrigMuzzleVelocity = wi.muzzleVelocity, OrigMaxSpeed = wi.maxSpeed, Modified = true }; wi.muzzleVelocity = wi.maxSpeed = ov * 3f; Calc45(mPos, tPos, t, u, tr, wi.muzzleVelocity, dist, out __result, out targetRange); return false; }
            __state = new State { OrigMuzzleVelocity = wi.muzzleVelocity, OrigMaxSpeed = wi.maxSpeed, Modified = true }; wi.muzzleVelocity = wi.maxSpeed = ov * 2f;
            return true;
        }
        [HarmonyPostfix] public static void Postfix(AimSolver __instance, State __state) { if (__state.Modified && __instance != null) { WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance); if (wi != null) { wi.muzzleVelocity = __state.OrigMuzzleVelocity; wi.maxSpeed = __state.OrigMaxSpeed; } } }
        private static void Calc45(GlobalPosition mp, GlobalPosition tp, Unit t, Unit u, Turret tr, float v, float d, out Vector3 res, out float tRange) {
            tRange = d; Vector3 relV = (t.speed < 1f ? Vector3.zero : t.rb.velocity) - (u.speed < 1f ? Vector3.zero : u.rb.velocity);
            float ft = d / Mathf.Max(200f, v * 0.7071068f); Vector3 dp = tp - mp, hv = new Vector3(dp.x + relV.x * ft, 0f, dp.z + relV.z * ft);
            if (hv.sqrMagnitude < 1f) hv = new Vector3(dp.x, 0f, dp.z);
            float e = 45f; if (tr != null) { float me = NavalBombardment.TurretMaxElevRef(tr); if (me > 0f && me < 45f) e = me; }
            float er = e * Mathf.Deg2Rad; res = (hv.normalized * Mathf.Cos(er) + Vector3.up * Mathf.Sin(er)) * d;
        }
    }
    [HarmonyPatch(typeof(Gun), "SpawnBullet")] public static class Gun_SpawnBullet_Patch {
        public struct State { public float OrigMuzzleVelocity, OrigSelfDestruct; public bool Modified; }
        [HarmonyPrefix] public static void Prefix(Gun __instance, out State __state) {
            __state = new State { OrigMuzzleVelocity = NavalBombardment.GunMuzzleVelocityRef(__instance), OrigSelfDestruct = NavalBombardment.GunBulletSelfDestructRef(__instance), Modified = false };
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            WeaponInfo wi = __instance.info; if (wi != null) NavalBombardment.CacheOriginalMuzzleVelocity(wi);
            Unit u = __instance.attachedUnit ?? __instance.GetComponentInParent<Unit>(); if (u != null && u is Aircraft) return;
            bool el = NavalBombardment.TryGetGunOrShellCaliber(__instance, wi, u, __instance.transform, out int cal, out bool ig);
            float sd = Plugin.Bombardment_SelfDestructUncap.Value; if (__state.OrigSelfDestruct < sd) { NavalBombardment.GunBulletSelfDestructRef(__instance) = sd; __state.Modified = true; }
            if (el && NavalBombardment.TryGetTargetDistance(__instance, out float td, out Unit tu)) {
                float vm = td >= 80000f ? (cal > 0 && cal < 100 ? 3f : 2f) : (td >= 40000f ? (cal > 0 && cal < 100 ? 2f : 1.5f) : 1f);
                if (vm > 1f) { NavalBombardment.GunMuzzleVelocityRef(__instance) = (NavalBombardment.TryGetOriginalMuzzleVelocity(wi, out float ov) && ov > 0f ? ov : (__state.OrigMuzzleVelocity > 0f ? __state.OrigMuzzleVelocity : 1000f)) * vm; __state.Modified = true; }
            }
        }
        [HarmonyPostfix] public static void Postfix(Gun __instance, State __state) { if (__state.Modified && __instance != null) { NavalBombardment.GunMuzzleVelocityRef(__instance) = __state.OrigMuzzleVelocity; NavalBombardment.GunBulletSelfDestructRef(__instance) = __state.OrigSelfDestruct; } }
    }
    [HarmonyPatch(typeof(BulletSim), "AddBullet")] public static class BulletSim_AddBullet_Patch {
        [HarmonyPrefix] public static void Prefix(BulletSim __instance, ref float destructTimer) { if (Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && __instance != null && destructTimer < Plugin.Bombardment_SelfDestructUncap.Value) destructTimer = Plugin.Bombardment_SelfDestructUncap.Value; }
    }
    [HarmonyPatch(typeof(FactionHQ), "IsTargetBeingTracked")] public static class FactionHQ_IsTargetBeingTracked_Patch {
        [HarmonyPrefix] public static bool Prefix(Unit target, ref bool __result) { if (target == null) { __result = false; return false; } return true; }
    }
    [HarmonyPatch(typeof(Unit), "Awake")] public static class Unit_Awake_Bombardment_Patch {
        [HarmonyPostfix] public static void Postfix(Unit __instance) { if (Plugin.EnableGoth.Value && Plugin.Bombardment_Enable.Value && __instance is Ship && __instance.definition != null) { var r = __instance.definition.roleIdentity; r.antiSurface = Mathf.Max(r.antiSurface, 0.85f); __instance.definition.roleIdentity = r; } }
    }
    [HarmonyPatch(typeof(Encyclopedia), "SortByValue")] public static class Encyclopedia_Bombardment_Patch {
        private static bool _init; [HarmonyPostfix, HarmonyPriority(Priority.Last)] public static void Postfix() { if (!_init) { _init = true; NavalBombardment.UnpatchQolTargetFilter(); } }
    }
}