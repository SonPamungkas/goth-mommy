using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public enum BombardmentType
    {
        None,
        Railgun,
        NavalCannon,
        GuidedShell
    }
    public static class NavalBombardment
    {
        public const float BombardmentMaxRange = 120000f; 
        private static readonly Dictionary<WeaponInfo, BombardmentType> _bombardmentTypeCache = new Dictionary<WeaponInfo, BombardmentType>();
        private static readonly Dictionary<Turret, bool> _bombardmentTurretCache = new Dictionary<Turret, bool>();
        private static readonly Dictionary<WeaponInfo, float> _originalMuzzleVelocities = new Dictionary<WeaponInfo, float>();
        internal static readonly AccessTools.FieldRef<Gun, MissileDefinition> GunGuidedProjectileRef =
            AccessTools.FieldRefAccess<Gun, MissileDefinition>("guidedProjectile");
        internal static readonly AccessTools.FieldRef<Gun, float> GunMuzzleVelocityRef =
            AccessTools.FieldRefAccess<Gun, float>("muzzleVelocity");
        internal static readonly AccessTools.FieldRef<Gun, float> GunBulletSelfDestructRef =
            AccessTools.FieldRefAccess<Gun, float>("bulletSelfDestruct");
        internal static readonly AccessTools.FieldRef<Gun, Unit> GunProximityFuseTargetRef =
            AccessTools.FieldRefAccess<Gun, Unit>("proximityFuseTarget");
        internal static readonly AccessTools.FieldRef<Weapon, Unit> WeaponCurrentTargetRef =
            AccessTools.FieldRefAccess<Weapon, Unit>("currentTarget");
        internal static readonly AccessTools.FieldRef<Weapon, WeaponStation> WeaponStationRef =
            AccessTools.FieldRefAccess<Weapon, WeaponStation>("weaponStation");
        internal static readonly AccessTools.FieldRef<BulletSim, WeaponInfo> BulletSimWeaponInfoRef =
            AccessTools.FieldRefAccess<BulletSim, WeaponInfo>("weaponInfo");
        internal static readonly AccessTools.FieldRef<Turret, int> TurretAcquisitionModeRef =
            AccessTools.FieldRefAccess<Turret, int>("targetAcquisitionMode");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretMaxRangeRef =
            AccessTools.FieldRefAccess<Turret, float>("maxRange");
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretFiresWithoutAimingRef =
            AccessTools.FieldRefAccess<Turret, bool>("firesWithoutAiming");
        internal static readonly AccessTools.FieldRef<Turret, Unit> TurretAttachedUnitRef =
            AccessTools.FieldRefAccess<Turret, Unit>("attachedUnit");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation[]> TurretWeaponStationsRef =
            AccessTools.FieldRefAccess<Turret, WeaponStation[]>("weaponStations");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretMaxElevationRef =
            AccessTools.FieldRefAccess<Turret, float>("maxElevation");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretMinElevationRef =
            AccessTools.FieldRefAccess<Turret, float>("minElevation");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation> TurretCurrentWeaponStationRef =
            AccessTools.FieldRefAccess<Turret, WeaponStation>("currentWeaponStation");
        internal static readonly AccessTools.FieldRef<Turret, Unit> TurretTargetRef =
            AccessTools.FieldRefAccess<Turret, Unit>("target");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretTargetRangeRef =
            AccessTools.FieldRefAccess<Turret, float>("targetRange");
        internal static readonly AccessTools.FieldRef<AimSolver, Unit> AimSolverAttachedUnitRef =
            AccessTools.FieldRefAccess<AimSolver, Unit>("attachedUnit");
        internal static readonly AccessTools.FieldRef<AimSolver, Unit> AimSolverCurrentTargetRef =
            AccessTools.FieldRefAccess<AimSolver, Unit>("currentTarget");
        internal static readonly AccessTools.FieldRef<AimSolver, Transform> AimSolverFiringTransformRef =
            AccessTools.FieldRefAccess<AimSolver, Transform>("firingTransform");
        internal static readonly AccessTools.FieldRef<AimSolver, WeaponInfo> AimSolverWeaponInfoRef =
            AccessTools.FieldRefAccess<AimSolver, WeaponInfo>("weaponInfo");
        public static BombardmentType GetBombardmentType(WeaponInfo wi, Transform firingTransform = null)
        {
            if (wi == null) return BombardmentType.None;
            if (_bombardmentTypeCache.TryGetValue(wi, out var cachedType) && cachedType != BombardmentType.None)
            {
                return cachedType;
            }
            BombardmentType result = BombardmentType.None;
            string name = wi.name;
            string wName = wi.weaponName;
            string sName = wi.shortName;
            if ((name != null && name.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (wName != null && wName.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (sName != null && sName.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (wi.gun && wi.muzzleVelocity >= 1800f))
            {
                result = BombardmentType.Railgun;
            }
            if (result == BombardmentType.None && firingTransform != null)
            {
                string ftName = firingTransform.name;
                if (ftName != null && ftName.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = BombardmentType.Railgun;
                }
            }
            if (result == BombardmentType.None && firingTransform != null)
            {
                Gun gun = firingTransform.GetComponent<Gun>() ??
                          firingTransform.GetComponentInParent<Gun>() ??
                          firingTransform.GetComponentInChildren<Gun>();
                if (gun != null)
                {
                    MissileDefinition guidedProj = GunGuidedProjectileRef(gun);
                    if (guidedProj != null)
                    {
                        result = BombardmentType.GuidedShell;
                    }
                }
            }
            if (result == BombardmentType.None && wi.weaponPrefab != null)
            {
                Gun gun = wi.weaponPrefab.GetComponent<Gun>() ??
                          wi.weaponPrefab.GetComponentInChildren<Gun>();
                if (gun != null)
                {
                    MissileDefinition guidedProj = GunGuidedProjectileRef(gun);
                    if (guidedProj != null)
                    {
                        result = BombardmentType.GuidedShell;
                    }
                }
            }
            if (result == BombardmentType.None && wi.weaponPrefab != null)
            {
                Missile missile = wi.weaponPrefab.GetComponent<Missile>() ??
                                  wi.weaponPrefab.GetComponentInChildren<Missile>();
                if (missile != null)
                {
                    MissileSeeker seeker = missile.GetComponent<MissileSeeker>();
                    if (seeker is OpticalSeekerShell || seeker is InertialSeekerShell ||
                        missile.GetComponent<OpticalSeekerShell>() != null ||
                        missile.GetComponent<InertialSeekerShell>() != null)
                    {
                        result = BombardmentType.GuidedShell;
                    }
                }
                if (result == BombardmentType.None)
                {
                    if (wi.weaponPrefab.GetComponentInChildren<OpticalSeekerShell>() != null ||
                        wi.weaponPrefab.GetComponentInChildren<InertialSeekerShell>() != null)
                    {
                        result = BombardmentType.GuidedShell;
                    }
                }
            }
            if (result == BombardmentType.None)
            {
                bool hasShellKeyword = (name != null && (name.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Cannon", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Artillery", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                       (wName != null && (wName.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0 || wName.IndexOf("Cannon", StringComparison.OrdinalIgnoreCase) >= 0 || wName.IndexOf("Artillery", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                       (sName != null && (sName.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Cannon", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Artillery", StringComparison.OrdinalIgnoreCase) >= 0));
                bool hasGuidedKeyword = (name != null && (name.IndexOf("Guided", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Seeker", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Smart", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                        (wName != null && (wName.IndexOf("Guided", StringComparison.OrdinalIgnoreCase) >= 0 || wName.IndexOf("Seeker", StringComparison.OrdinalIgnoreCase) >= 0 || wName.IndexOf("Smart", StringComparison.OrdinalIgnoreCase) >= 0));
                if (hasShellKeyword && hasGuidedKeyword)
                {
                    result = BombardmentType.GuidedShell;
                }
            }
            if (result == BombardmentType.None)
            {
                bool hasCannonKeyword = (name != null && (name.IndexOf("Aryx_NavalGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("203mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("127mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("130mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("76mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("Deckgun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("Deck Gun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("NavalGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          name.IndexOf("Naval Gun", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                        (wName != null && (wName.IndexOf("Aryx_NavalGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("203mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("127mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("130mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("76mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("Deckgun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("Deck Gun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("NavalGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           wName.IndexOf("Naval Gun", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                        (sName != null && (sName.IndexOf("203mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           sName.IndexOf("127mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           sName.IndexOf("130mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           sName.IndexOf("76mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           sName.IndexOf("Deckgun", StringComparison.OrdinalIgnoreCase) >= 0));
                if (hasCannonKeyword || (firingTransform != null && (firingTransform.name.IndexOf("Deckgun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                    firingTransform.name.IndexOf("NavalGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                    firingTransform.name.IndexOf("203", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                    firingTransform.name.IndexOf("127", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                    firingTransform.name.IndexOf("76", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                    (wi.gun && wi.overHorizon))
                {
                    result = BombardmentType.NavalCannon;
                }
            }
            if (result != BombardmentType.None)
            {
                CacheOriginalMuzzleVelocity(wi);
                if (wi.targetRequirements.maxRange < BombardmentMaxRange || wi.targetRequirements.lineOfSight)
                {
                    if (IsEligibleForBombardmentAndVelocity(wi, firingTransform, null, null))
                    {
                        ConfigureWeaponInfo(wi, null, firingTransform, null);
                    }
                }
            }
            if (result != BombardmentType.None || firingTransform != null)
            {
                _bombardmentTypeCache[wi] = result;
            }
            return result;
        }
        private static readonly Regex CaliberMmRegex =
            new Regex(@"(?<![a-zA-Z0-9])(\d+(?:\.\d+)?)\s*[-_]?\s*mm(?![a-zA-Z])", RegexOptions.IgnoreCase);
        private static readonly Regex CaliberUnderscoreRegex =
            new Regex(@"(?:Shell|Artillery|Gun|Mortar|Cannon|Deckgun|Turret)[-_](\d{2,3})(?:[-_]|$)", RegexOptions.IgnoreCase);
        public static float ExtractCaliberMillimeters(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1f;
            Match match = CaliberMmRegex.Match(text);
            if (match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mmVal))
            {
                if (mmVal > 0f) return mmVal;
            }
            Match matchUnderscore = CaliberUnderscoreRegex.Match(text);
            if (matchUnderscore.Success && float.TryParse(matchUnderscore.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float numVal))
            {
                if (numVal > 0f) return numVal;
            }
            return -1f;
        }
        public static float ScanCaliberMillimeters(WeaponInfo wi, Transform firingTransform = null, Turret turret = null, Unit unit = null, Gun gun = null)
        {
            float cal = -1f;
            if (wi != null)
            {
                cal = ExtractCaliberMillimeters(wi.weaponName);
                if (cal > 0f) return cal;
                cal = ExtractCaliberMillimeters(wi.shortName);
                if (cal > 0f) return cal;
                cal = ExtractCaliberMillimeters(wi.name);
                if (cal > 0f) return cal;
                if (wi.weaponPrefab != null)
                {
                    cal = ExtractCaliberMillimeters(wi.weaponPrefab.name);
                    if (cal > 0f) return cal;
                    var transforms = wi.weaponPrefab.GetComponentsInChildren<Transform>(true);
                    if (transforms != null)
                    {
                        for (int i = 0; i < transforms.Length && i < 20; i++)
                        {
                            cal = ExtractCaliberMillimeters(transforms[i].name);
                            if (cal > 0f) return cal;
                        }
                    }
                }
            }
            if (gun == null && firingTransform != null)
            {
                gun = firingTransform.GetComponent<Gun>() ??
                      firingTransform.GetComponentInParent<Gun>() ??
                      firingTransform.GetComponentInChildren<Gun>();
            }
            if (gun != null)
            {
                cal = ExtractCaliberMillimeters(gun.name);
                if (cal > 0f) return cal;
                MissileDefinition guided = GunGuidedProjectileRef(gun);
                if (guided != null)
                {
                    cal = ExtractCaliberMillimeters(guided.name);
                    if (cal > 0f) return cal;
                    cal = ExtractCaliberMillimeters(guided.unitName);
                    if (cal > 0f) return cal;
                    if (guided.unitPrefab != null)
                    {
                        cal = ExtractCaliberMillimeters(guided.unitPrefab.name);
                        if (cal > 0f) return cal;
                    }
                }
            }
            if (firingTransform != null)
            {
                Transform curr = firingTransform;
                int depth = 0;
                while (curr != null && depth++ < 15)
                {
                    cal = ExtractCaliberMillimeters(curr.name);
                    if (cal > 0f) return cal;
                    curr = curr.parent;
                }
            }
            if (turret == null && firingTransform != null)
            {
                turret = firingTransform.GetComponentInParent<Turret>();
            }
            if (turret != null)
            {
                cal = ExtractCaliberMillimeters(turret.name);
                if (cal > 0f) return cal;
                WeaponStation[] stations = TurretWeaponStationsRef(turret);
                if (stations != null)
                {
                    for (int i = 0; i < stations.Length; i++)
                    {
                        if (stations[i] != null && stations[i].WeaponInfo != null)
                        {
                            cal = ExtractCaliberMillimeters(stations[i].WeaponInfo.weaponName);
                            if (cal > 0f) return cal;
                            cal = ExtractCaliberMillimeters(stations[i].WeaponInfo.shortName);
                            if (cal > 0f) return cal;
                            cal = ExtractCaliberMillimeters(stations[i].WeaponInfo.name);
                            if (cal > 0f) return cal;
                        }
                    }
                }
            }
            if (unit == null && turret != null)
            {
                unit = TurretAttachedUnitRef(turret) ?? turret.GetComponentInParent<Unit>();
            }
            if (unit == null && firingTransform != null)
            {
                unit = firingTransform.GetComponentInParent<Unit>();
            }
            if (unit != null)
            {
                cal = ExtractCaliberMillimeters(unit.name);
                if (cal > 0f) return cal;
                if (unit.definition != null)
                {
                    cal = ExtractCaliberMillimeters(unit.definition.name);
                    if (cal > 0f) return cal;
                    cal = ExtractCaliberMillimeters(unit.definition.unitName);
                    if (cal > 0f) return cal;
                }
            }
            return -1f;
        }
        public static float GetCaliberMillimeters(WeaponInfo wi, Transform firingTransform = null, Turret turret = null, Unit unit = null, Gun gun = null)
        {
            float scannedCal = ScanCaliberMillimeters(wi, firingTransform, turret, unit, gun);
            if (scannedCal > 0f)
            {
                return scannedCal;
            }
            if (gun == null && firingTransform != null)
            {
                gun = firingTransform.GetComponent<Gun>() ??
                      firingTransform.GetComponentInParent<Gun>() ??
                      firingTransform.GetComponentInChildren<Gun>();
            }
            if (gun != null)
            {
                MissileDefinition def = GunGuidedProjectileRef(gun);
                if (def != null)
                {
                    if (def.width > 0f) return def.width * 1000f;
                    if (def.height > 0f) return def.height * 1000f;
                }
            }
            if (wi != null && wi.weaponPrefab != null)
            {
                Gun prefabGun = wi.weaponPrefab.GetComponent<Gun>() ?? wi.weaponPrefab.GetComponentInChildren<Gun>();
                if (prefabGun != null)
                {
                    MissileDefinition def = GunGuidedProjectileRef(prefabGun);
                    if (def != null)
                    {
                        if (def.width > 0f) return def.width * 1000f;
                        if (def.height > 0f) return def.height * 1000f;
                    }
                }
                Unit prefabUnit = wi.weaponPrefab.GetComponent<Unit>() ?? wi.weaponPrefab.GetComponentInChildren<Unit>();
                if (prefabUnit != null && prefabUnit.definition != null)
                {
                    if (prefabUnit.definition.width > 0f) return prefabUnit.definition.width * 1000f;
                    if (prefabUnit.definition.height > 0f) return prefabUnit.definition.height * 1000f;
                }
                CapsuleCollider capsule = wi.weaponPrefab.GetComponent<CapsuleCollider>() ?? wi.weaponPrefab.GetComponentInChildren<CapsuleCollider>();
                if (capsule != null && capsule.radius > 0f)
                {
                    return capsule.radius * 2000f;
                }
                SphereCollider sphere = wi.weaponPrefab.GetComponent<SphereCollider>() ?? wi.weaponPrefab.GetComponentInChildren<SphereCollider>();
                if (sphere != null && sphere.radius > 0f)
                {
                    return sphere.radius * 2000f;
                }
            }
            if (IsMortar(wi, firingTransform, turret, unit))
            {
                return 81f; 
            }
            if (IsHeavyArtillery(wi, firingTransform, turret, unit))
            {
                return 155f; 
            }
            return -1f;
        }
        public static float GetShellDiameter(WeaponInfo wi, Transform firingTransform = null, Turret turret = null, Unit unit = null)
        {
            float calMm = GetCaliberMillimeters(wi, firingTransform, turret, unit);
            return calMm > 0f ? calMm / 1000f : -1f;
        }
        private static bool ContainsKeyword(string text, string keyword)
        {
            return text != null && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static bool IsMortar(WeaponInfo wi, Transform firingTransform = null, Turret turret = null, Unit unit = null)
        {
            if (wi != null)
            {
                if (ContainsKeyword(wi.weaponName, "Mortar") ||
                    ContainsKeyword(wi.shortName, "Mortar") ||
                    ContainsKeyword(wi.name, "Mortar"))
                {
                    return true;
                }
                if (wi.weaponPrefab != null && ContainsKeyword(wi.weaponPrefab.name, "Mortar"))
                {
                    return true;
                }
            }
            if (firingTransform != null)
            {
                Transform curr = firingTransform;
                int depth = 0;
                while (curr != null && depth++ < 10)
                {
                    if (ContainsKeyword(curr.name, "Mortar")) return true;
                    curr = curr.parent;
                }
            }
            if (turret != null)
            {
                if (ContainsKeyword(turret.name, "Mortar") || ContainsKeyword(turret.gameObject.name, "Mortar"))
                {
                    return true;
                }
            }
            if (unit != null)
            {
                if (ContainsKeyword(unit.name, "Mortar") || ContainsKeyword(unit.gameObject.name, "Mortar"))
                {
                    return true;
                }
                if (unit.definition != null && (ContainsKeyword(unit.definition.name, "Mortar") || ContainsKeyword(unit.definition.unitName, "Mortar")))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsHeavyArtillery(WeaponInfo wi, Transform firingTransform = null, Turret turret = null, Unit unit = null)
        {
            if (wi != null)
            {
                if (ContainsKeyword(wi.weaponName, "Howitzer") || ContainsKeyword(wi.weaponName, "SPG") || ContainsKeyword(wi.weaponName, "Artillery") ||
                    ContainsKeyword(wi.shortName, "Howitzer") || ContainsKeyword(wi.shortName, "SPG") || ContainsKeyword(wi.shortName, "Artillery") ||
                    ContainsKeyword(wi.name, "Howitzer") || ContainsKeyword(wi.name, "SPG") || ContainsKeyword(wi.name, "Artillery"))
                {
                    return true;
                }
            }
            if (firingTransform != null)
            {
                Transform curr = firingTransform;
                int depth = 0;
                while (curr != null && depth++ < 10)
                {
                    if (ContainsKeyword(curr.name, "Howitzer") || ContainsKeyword(curr.name, "SPG") || ContainsKeyword(curr.name, "Artillery")) return true;
                    curr = curr.parent;
                }
            }
            if (turret != null)
            {
                if (ContainsKeyword(turret.name, "Howitzer") || ContainsKeyword(turret.name, "SPG") || ContainsKeyword(turret.name, "Artillery"))
                {
                    return true;
                }
            }
            if (unit != null)
            {
                if (ContainsKeyword(unit.name, "Howitzer") || ContainsKeyword(unit.name, "SPG") || ContainsKeyword(unit.name, "Artillery") || ContainsKeyword(unit.name, "MArt"))
                {
                    return true;
                }
                if (unit.definition != null && (ContainsKeyword(unit.definition.name, "Howitzer") || ContainsKeyword(unit.definition.name, "SPG") || ContainsKeyword(unit.definition.name, "Artillery")))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsNavalWeapon(WeaponInfo wi, Transform firingTransform = null)
        {
            if (wi != null)
            {
                string n = wi.name;
                string wn = wi.weaponName;
                string sn = wi.shortName;
                string pn = wi.weaponPrefab != null ? wi.weaponPrefab.name : null;
                if (ContainsKeyword(n, "Deckgun") || ContainsKeyword(n, "Deck Gun") ||
                    ContainsKeyword(n, "NavalGun") || ContainsKeyword(n, "Naval Gun") ||
                    ContainsKeyword(n, "Aryx_NavalGun") || ContainsKeyword(n, "Naval") ||
                    ContainsKeyword(n, "Shell_76mm") || ContainsKeyword(n, "Railgun") ||
                    ContainsKeyword(wn, "Deckgun") || ContainsKeyword(wn, "Deck Gun") ||
                    ContainsKeyword(wn, "NavalGun") || ContainsKeyword(wn, "Naval Gun") ||
                    ContainsKeyword(wn, "Aryx_NavalGun") || ContainsKeyword(wn, "Naval") ||
                    ContainsKeyword(wn, "Shell_76mm") || ContainsKeyword(wn, "Railgun") ||
                    ContainsKeyword(sn, "Deckgun") || ContainsKeyword(sn, "Naval") ||
                    ContainsKeyword(sn, "Shell_76mm") || ContainsKeyword(sn, "Railgun") ||
                    ContainsKeyword(pn, "Deckgun") || ContainsKeyword(pn, "Naval") ||
                    ContainsKeyword(pn, "Shell_76mm") || ContainsKeyword(pn, "Railgun"))
                {
                    return true;
                }
            }
            if (firingTransform != null)
            {
                Transform curr = firingTransform;
                int depth = 0;
                while (curr != null && depth++ < 10)
                {
                    string cn = curr.name;
                    if (ContainsKeyword(cn, "Deckgun") || ContainsKeyword(cn, "NavalGun") ||
                        ContainsKeyword(cn, "Naval") || ContainsKeyword(cn, "Ship"))
                    {
                        return true;
                    }
                    curr = curr.parent;
                }
            }
            return false;
        }
        public static bool IsShipPlatform(Transform firingTransform, Turret turret, Unit unit, WeaponInfo wi = null)
        {
            if (unit is Ship) return true;
            if (turret != null)
            {
                Unit u = NavalBombardment.TurretAttachedUnitRef(turret) ?? turret.GetComponentInParent<Unit>();
                if (u is Ship) return true;
                if (turret.GetComponentInParent<Ship>() != null) return true;
            }
            if (firingTransform != null)
            {
                if (firingTransform.GetComponentInParent<Ship>() != null) return true;
                Unit u = firingTransform.GetComponentInParent<Unit>();
                if (u is Ship) return true;
            }
            if (unit == null && IsNavalWeapon(wi, firingTransform))
            {
                return true;
            }
            return false;
        }
        public static void CacheOriginalMuzzleVelocity(WeaponInfo wi)
        {
            if (wi != null && !_originalMuzzleVelocities.ContainsKey(wi) && wi.muzzleVelocity > 0f)
            {
                _originalMuzzleVelocities[wi] = wi.muzzleVelocity;
            }
        }
        public static bool TryGetOriginalMuzzleVelocity(WeaponInfo wi, out float orig)
        {
            if (wi != null && _originalMuzzleVelocities.TryGetValue(wi, out orig))
            {
                return true;
            }
            orig = 0f;
            return false;
        }
        public static bool TryGetTargetDistance(Gun gun, out float distance, out Unit target)
        {
            distance = 0f;
            target = null;
            if (gun == null) return false;
            target = WeaponCurrentTargetRef(gun);
            if (target != null && !target.disabled)
            {
                distance = FastMath.Distance(gun.transform.position, target.transform.position);
                return true;
            }
            target = GunProximityFuseTargetRef(gun);
            if (target != null && !target.disabled)
            {
                distance = FastMath.Distance(gun.transform.position, target.transform.position);
                return true;
            }
            WeaponStation ws = WeaponStationRef(gun);
            Turret turret = (ws != null) ? ws.GetTurret() : gun.GetComponentInParent<Turret>();
            if (turret != null)
            {
                target = TurretTargetRef(turret);
                if (target != null && !target.disabled)
                {
                    distance = FastMath.Distance(gun.transform.position, target.transform.position);
                    return true;
                }
                float tRange = TurretTargetRangeRef(turret);
                if (tRange > 0f)
                {
                    distance = tRange;
                    return true;
                }
            }
            return false;
        }
        public static bool IsDirectAttackWeapon(WeaponInfo wi, Transform firingTransform = null)
        {
            if (wi != null)
            {
                if (ContainsKeyword(wi.weaponName, "Tank") || ContainsKeyword(wi.weaponName, "MBT") ||
                    ContainsKeyword(wi.shortName, "Tank") || ContainsKeyword(wi.shortName, "MBT") ||
                    ContainsKeyword(wi.name, "Tank") || ContainsKeyword(wi.name, "MBT"))
                {
                    return true;
                }
            }
            if (firingTransform != null)
            {
                Transform curr = firingTransform;
                int depth = 0;
                while (curr != null && depth++ < 10)
                {
                    if (ContainsKeyword(curr.name, "Tank") || ContainsKeyword(curr.name, "MBT")) return true;
                    curr = curr.parent;
                }
            }
            return false;
        }
        public static bool IsArtillerySemanticName(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return ContainsKeyword(text, "Artillery") ||
                   ContainsKeyword(text, "SPG") ||
                   ContainsKeyword(text, "Howitzer") ||
                   ContainsKeyword(text, "MArt") ||
                   ContainsKeyword(text, "_ART") ||
                   ContainsKeyword(text, "-ART");
        }
        public static bool IsSurfaceUnitArtilleryDesignated(Unit unit, Turret turret = null, WeaponInfo wi = null, Transform firingTransform = null)
        {
            if (unit != null)
            {
                if (unit.definition is VehicleDefinition vDef)
                {
                    if (vDef.vehicleType == VehicleType.ART)
                    {
                        return true;
                    }
                    return false;
                }
                if (unit.GetComponent<MobileArtilleryAI>() != null || unit.GetComponentInChildren<MobileArtilleryAI>() != null)
                {
                    return true;
                }
                if (unit.definition != null)
                {
                    string code = unit.definition.code;
                    if (code != null && (code.Equals("ART", StringComparison.OrdinalIgnoreCase) || code.IndexOf("ART", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                    if (IsArtillerySemanticName(unit.definition.unitName) || IsArtillerySemanticName(unit.definition.name))
                    {
                        return true;
                    }
                }
                if (IsArtillerySemanticName(unit.name))
                {
                    return true;
                }
            }
            if (turret != null)
            {
                if (IsArtillerySemanticName(turret.name))
                {
                    return true;
                }
            }
            if (unit == null)
            {
                if (IsDirectAttackWeapon(wi, firingTransform))
                {
                    return false;
                }
                if (IsHeavyArtillery(wi, firingTransform, turret, null))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsEligibleForBombardmentAndVelocity(WeaponInfo wi, Transform firingTransform = null, Unit unit = null, Turret turret = null)
        {
            if (wi == null) return false;
            CacheOriginalMuzzleVelocity(wi);
            bool isShip = IsShipPlatform(firingTransform, turret, unit, wi);
            if (isShip) return true;
            Unit targetUnit = unit;
            if (targetUnit == null && turret != null)
            {
                targetUnit = TurretAttachedUnitRef(turret) ?? turret.GetComponentInParent<Unit>();
            }
            if (targetUnit == null && firingTransform != null)
            {
                targetUnit = firingTransform.GetComponentInParent<Unit>();
            }
            if (!IsSurfaceUnitArtilleryDesignated(targetUnit, turret, wi, firingTransform))
            {
                return false;
            }
            if (IsMortar(wi, firingTransform, turret, targetUnit))
            {
                return false;
            }
            float caliberMm = GetCaliberMillimeters(wi, firingTransform, turret, targetUnit);
            float minCaliberMm = (Plugin.Bombardment_MinCaliberNonNaval != null ? Plugin.Bombardment_MinCaliberNonNaval.Value : 0.1f) * 1000f; 
            if (caliberMm > 0f)
            {
                return caliberMm >= minCaliberMm;
            }
            if (IsHeavyArtillery(wi, firingTransform, turret, targetUnit))
            {
                return true;
            }
            return false;
        }
        public static bool IsBombardmentTurret(Turret turret)
        {
            if (turret == null) return false;
            if (_bombardmentTurretCache.TryGetValue(turret, out bool cached)) return cached;
            Unit attachedUnit = TurretAttachedUnitRef(turret) ?? turret.GetComponentInParent<Unit>();
            bool isBombardment = false;
            WeaponStation[] stations = TurretWeaponStationsRef(turret);
            if (stations != null)
            {
                for (int i = 0; i < stations.Length; i++)
                {
                    WeaponStation ws = stations[i];
                    if (ws != null)
                    {
                        WeaponInfo wi = ws.WeaponInfo ?? (ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].info : null);
                        if (wi != null)
                        {
                            Transform firingT = ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].transform : null;
                            if (GetBombardmentType(wi, firingT) != BombardmentType.None &&
                                IsEligibleForBombardmentAndVelocity(wi, firingT, attachedUnit, turret))
                            {
                                isBombardment = true;
                                break;
                            }
                        }
                    }
                }
            }
            if (!isBombardment)
            {
                WeaponStation ws = turret.GetWeaponStation() ?? TurretCurrentWeaponStationRef(turret);
                if (ws != null)
                {
                    WeaponInfo wi = ws.WeaponInfo ?? (ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].info : null);
                    if (wi != null)
                    {
                        Transform firingT = ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].transform : null;
                        if (GetBombardmentType(wi, firingT) != BombardmentType.None &&
                            IsEligibleForBombardmentAndVelocity(wi, firingT, attachedUnit, turret))
                        {
                            isBombardment = true;
                        }
                    }
                }
            }
            _bombardmentTurretCache[turret] = isBombardment;
            return isBombardment;
        }
        public static float GetGroundBallisticRange(Turret turret, Unit attachedUnit)
        {
            if (turret == null) return BombardmentMaxRange;
            float maxElev = TurretMaxElevationRef(turret);
            float elevAngle = (maxElev > 0f) ? Mathf.Min(maxElev, 45f) : 45f;
            float sin2Theta = Mathf.Sin(2f * elevAngle * Mathf.Deg2Rad);
            float muzzleV = Plugin.Bombardment_ShipExitVelocity.Value;
            if (muzzleV <= 10f) return BombardmentMaxRange;
            float maxRange = (muzzleV * muzzleV / 9.81f) * sin2Theta;
            return Mathf.Clamp(maxRange, 1000f, BombardmentMaxRange);
        }
        public static void ConfigureWeaponInfo(WeaponInfo wi, Unit unit = null, Transform firingTransform = null, Turret turret = null)
        {
            if (wi == null) return;
            CacheOriginalMuzzleVelocity(wi);
            if (!IsEligibleForBombardmentAndVelocity(wi, firingTransform, unit, turret)) return;
            if (Plugin.Bombardment_UncappedRange.Value)
            {
                var req = wi.targetRequirements;
                req.maxRange = Mathf.Max(req.maxRange, BombardmentMaxRange);
                req.lineOfSight = false;
                req.minAltitude = 0f;
                req.maxAltitude = Mathf.Min(req.maxAltitude > 0f ? req.maxAltitude : 500f, 500f);
                req.maxSpeed = Mathf.Min(req.maxSpeed > 0f ? req.maxSpeed : 80f, 80f);
                wi.targetRequirements = req;
                wi.overHorizon = true;
                wi.strategic = true;
                var eff = wi.effectiveness;
                eff.antiSurface = Mathf.Max(eff.antiSurface, 0.85f);
                wi.effectiveness = eff;
            }
        }
        public static void UnpatchQolTargetFilter()
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Assembly qolAssembly = null;
                for (int i = 0; i < assemblies.Length; i++)
                {
                    if (assemblies[i].GetName().Name == "qol")
                    {
                        qolAssembly = assemblies[i];
                        break;
                    }
                }
                if (qolAssembly == null) return;
                Type qolPatchType = qolAssembly.GetType("qol.TurretTargetFilterPatch2");
                if (qolPatchType == null) return;
                MethodInfo qolPrefix = AccessTools.Method(qolPatchType, "FilterTargets_Prefix");
                MethodInfo qolPostfix = AccessTools.Method(qolPatchType, "FilterTargets_Postfix");
                MethodInfo targetMethod = AccessTools.Method(typeof(Turret), "AssessTargetPriority");
                if (targetMethod == null) return;
                var harmony = new Harmony("neutral.gothmommy.qoloverride");
                if (qolPrefix != null) harmony.Unpatch(targetMethod, qolPrefix);
                if (qolPostfix != null) harmony.Unpatch(targetMethod, qolPostfix);
                Plugin.Log.LogInfo("[NavalBombardment] Unpatched QoL target filter for turret bombardment compatibility.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NavalBombardment] QoL unpatch notice: " + ex.Message);
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")]
    public static class Turret_OnInitialize_NavalBombardment_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            Unit attachedUnit = NavalBombardment.TurretAttachedUnitRef(__instance);
            if (attachedUnit == null || attachedUnit is Aircraft) return;
            WeaponStation[] stations = NavalBombardment.TurretWeaponStationsRef(__instance);
            if (stations == null || stations.Length == 0) return;
            bool hasBombardmentWeapon = false;
            for (int i = 0; i < stations.Length; i++)
            {
                WeaponStation ws = stations[i];
                if (ws != null)
                {
                    WeaponInfo wi = ws.WeaponInfo ?? (ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].info : null);
                    if (wi != null)
                    {
                        ws.WeaponInfo = wi;
                        Transform firingT = ws.Weapons != null && ws.Weapons.Count > 0 ? ws.Weapons[0].transform : null;
                        if (NavalBombardment.GetBombardmentType(wi, firingT) != BombardmentType.None)
                        {
                            bool eligible = NavalBombardment.IsEligibleForBombardmentAndVelocity(wi, firingT, attachedUnit, __instance);
                            if (eligible)
                            {
                                hasBombardmentWeapon = true;
                                NavalBombardment.ConfigureWeaponInfo(wi, attachedUnit, firingT, __instance);
                                ws.TypeLookup?.Clear();
                            }
                        }
                    }
                }
            }
            if (hasBombardmentWeapon)
            {
                NavalBombardment.TurretAcquisitionModeRef(__instance) = 3;
                float maxRange = NavalBombardment.BombardmentMaxRange;
                if (!(attachedUnit is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value)
                {
                    maxRange = NavalBombardment.GetGroundBallisticRange(__instance, attachedUnit);
                }
                NavalBombardment.TurretMaxRangeRef(__instance) = maxRange;
                NavalBombardment.TurretFiresWithoutAimingRef(__instance) = false;
                if (Plugin.Bombardment_DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[NavalBombardment] Turret '{__instance.name}' on '{attachedUnit.name}' initialized to Datalink OTH Bombardment (MaxRange: {maxRange:F0}m).");
                }
            }
        }
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            if (NavalBombardment.IsBombardmentTurret(__instance))
            {
                Unit attachedUnit = NavalBombardment.TurretAttachedUnitRef(__instance);
                float maxRange = NavalBombardment.BombardmentMaxRange;
                if (attachedUnit != null && !(attachedUnit is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value)
                {
                    maxRange = NavalBombardment.GetGroundBallisticRange(__instance, attachedUnit);
                }
                NavalBombardment.TurretAcquisitionModeRef(__instance) = 3;
                NavalBombardment.TurretMaxRangeRef(__instance) = maxRange;
                NavalBombardment.TurretFiresWithoutAimingRef(__instance) = false;
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    public static class Turret_AssessTargetPriority_Bombardment_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance, Unit targetCandidate, ref float priorityThreshold)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || !Plugin.Bombardment_PrioritizeSurface.Value) return true;
            if (__instance == null || targetCandidate == null || targetCandidate.disabled) return true;
            if (NavalBombardment.IsBombardmentTurret(__instance))
            {
                if (targetCandidate is Aircraft || targetCandidate is Missile)
                {
                    return false;
                }
                if (targetCandidate.radarAlt > 20f && !(targetCandidate is Building))
                {
                    return false;
                }
                Unit attachedUnit = NavalBombardment.TurretAttachedUnitRef(__instance);
                if (attachedUnit != null)
                {
                    float dist = FastMath.Distance(attachedUnit.GlobalPosition(), targetCandidate.GlobalPosition());
                    float maxAllowed = NavalBombardment.BombardmentMaxRange;
                    if (!(attachedUnit is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value)
                    {
                        maxAllowed = NavalBombardment.GetGroundBallisticRange(__instance, attachedUnit);
                    }
                    if (dist > maxAllowed)
                    {
                        if (Plugin.Ground_DebugLog.Value && !(attachedUnit is Ship))
                        {
                            Plugin.Log.LogInfo($"[GroundBallistics] Rejected target '{targetCandidate.name}' at {dist:F0}m (> {maxAllowed:F0}m limit) for '{attachedUnit.name}'.");
                        }
                        return false;
                    }
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Turret), "SetTarget")]
    public static class Turret_SetTarget_Bombardment_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance, PersistentID id, byte stationIndex)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return true;
            if (!NavalBombardment.IsBombardmentTurret(__instance)) return true;
            if (id != PersistentID.None && UnitRegistry.TryGetUnit(id, out Unit candidate))
            {
                if (candidate is Aircraft || candidate is Missile)
                {
                    return false;
                }
                Unit attachedUnit = NavalBombardment.TurretAttachedUnitRef(__instance);
                if (attachedUnit != null)
                {
                    float dist = FastMath.Distance(attachedUnit.GlobalPosition(), candidate.GlobalPosition());
                    float maxAllowed = NavalBombardment.BombardmentMaxRange;
                    if (!(attachedUnit is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value)
                    {
                        maxAllowed = NavalBombardment.GetGroundBallisticRange(__instance, attachedUnit);
                    }
                    if (dist > maxAllowed)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            if (!NavalBombardment.IsBombardmentTurret(__instance)) return;
            Unit attachedUnit = NavalBombardment.TurretAttachedUnitRef(__instance);
            float maxRange = NavalBombardment.BombardmentMaxRange;
            if (attachedUnit != null && !(attachedUnit is Ship) && Plugin.Ground_BallisticRangeCap_Enable.Value)
            {
                maxRange = NavalBombardment.GetGroundBallisticRange(__instance, attachedUnit);
            }
            NavalBombardment.TurretMaxRangeRef(__instance) = maxRange;
        }
    }
    [HarmonyPatch(typeof(Unit), "Awake")]
    public static class Unit_Awake_NavalBombardment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Unit __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value) return;
            if (__instance == null || !(__instance is Ship ship)) return;
            if (__instance.definition != null)
            {
                var role = __instance.definition.roleIdentity;
                role.antiSurface = Mathf.Max(role.antiSurface, 0.85f);
                __instance.definition.roleIdentity = role;
            }
        }
    }
    [HarmonyPatch(typeof(Encyclopedia), "SortByValue")]
    public static class Encyclopedia_NavalBombardment_Patch
    {
        private static bool _initialized;
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Encyclopedia __instance)
        {
            if (_initialized) return;
            _initialized = true;
            NavalBombardment.UnpatchQolTargetFilter();
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value) return;
            try
            {
                var allWeaponInfos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (allWeaponInfos != null)
                {
                    for (int i = 0; i < allWeaponInfos.Length; i++)
                    {
                        WeaponInfo wi = allWeaponInfos[i];
                        if (wi == null) continue;
                        BombardmentType bType = NavalBombardment.GetBombardmentType(wi);
                        if (bType != BombardmentType.None)
                        {
                            if (NavalBombardment.IsEligibleForBombardmentAndVelocity(wi, null, null, null))
                            {
                                NavalBombardment.ConfigureWeaponInfo(wi, null, null, null);
                            }
                        }
                    }
                }
                if (__instance.ships != null)
                {
                    for (int i = 0; i < __instance.ships.Count; i++)
                    {
                        UnitDefinition shipDef = __instance.ships[i];
                        if (shipDef != null)
                        {
                            var role = shipDef.roleIdentity;
                            role.antiSurface = Mathf.Max(role.antiSurface, 0.85f);
                            shipDef.roleIdentity = role;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[NavalBombardment] Encyclopedia configuration error: " + ex);
            }
        }
    }
    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    public static class Gun_SpawnBullet_Patch
    {
        public struct GunSpawnState
        {
            public float OrigMuzzleVelocity;
            public float OrigSelfDestruct;
            public Unit OrigProximityTarget;
            public bool Modified;
        }
        [HarmonyPrefix]
        public static void Prefix(Gun __instance, out GunSpawnState __state)
        {
            __state = new GunSpawnState
            {
                OrigMuzzleVelocity = NavalBombardment.GunMuzzleVelocityRef(__instance),
                OrigSelfDestruct = NavalBombardment.GunBulletSelfDestructRef(__instance),
                OrigProximityTarget = NavalBombardment.GunProximityFuseTargetRef(__instance),
                Modified = false
            };
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            WeaponInfo wi = __instance.info;
            if (wi != null)
            {
                NavalBombardment.CacheOriginalMuzzleVelocity(wi);
            }
            BombardmentType bType = NavalBombardment.GetBombardmentType(wi, __instance.transform);
            Unit shooter = __instance.attachedUnit ?? __instance.GetComponentInParent<Unit>();
            Turret turret = __instance.GetComponentInParent<Turret>();
            bool eligibleForVelocity = NavalBombardment.IsEligibleForBombardmentAndVelocity(wi, __instance.transform, shooter, turret);
            bool hasTarget = NavalBombardment.TryGetTargetDistance(__instance, out float targetDist, out Unit targetUnit);
            float threshold = Plugin.Bombardment_GuidedShell45Threshold.Value; 
            bool applyLongRangeBuff = eligibleForVelocity && hasTarget && (targetDist >= threshold);
            if (applyLongRangeBuff)
            {
                if (Plugin.Bombardment_UnifyShipVelocity.Value)
                {
                    float targetV = Plugin.Bombardment_ShipExitVelocity.Value; 
                    NavalBombardment.GunMuzzleVelocityRef(__instance) = targetV;
                    __state.Modified = true;
                }
                if (bType != BombardmentType.None || (wi != null && wi.overHorizon))
                {
                    float uncapVal = Plugin.Bombardment_SelfDestructUncap.Value;
                    if (__state.OrigSelfDestruct < uncapVal)
                    {
                        NavalBombardment.GunBulletSelfDestructRef(__instance) = uncapVal;
                        __state.Modified = true;
                    }
                    if (Plugin.Bombardment_SuppressKineticProximityFuse.Value && wi != null && wi.pierceDamage >= 1000f)
                    {
                        NavalBombardment.GunProximityFuseTargetRef(__instance) = null;
                        __state.Modified = true;
                    }
                }
                if (Plugin.Bombardment_DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[NavalBombardment] Fired 2000 m/s shell from '{shooter?.name}' at target '{targetUnit?.name}' (distance: {targetDist:F0}m >= {threshold:F0}m).");
                }
            }
            else
            {
                if (wi != null && NavalBombardment.TryGetOriginalMuzzleVelocity(wi, out float origV) && origV > 0f)
                {
                    if (NavalBombardment.GunMuzzleVelocityRef(__instance) != origV)
                    {
                        NavalBombardment.GunMuzzleVelocityRef(__instance) = origV;
                        __state.Modified = true;
                    }
                }
                if (Plugin.Bombardment_DebugLog.Value && hasTarget)
                {
                    Plugin.Log.LogInfo($"[NavalBombardment] Fired VANILLA shell from '{shooter?.name}' at target '{targetUnit?.name}' (distance: {targetDist:F0}m < {threshold:F0}m; CRAM engagement enabled).");
                }
            }
        }
        [HarmonyPostfix]
        public static void Postfix(Gun __instance, GunSpawnState __state)
        {
            if (__instance == null || !__state.Modified) return;
            NavalBombardment.GunMuzzleVelocityRef(__instance) = __state.OrigMuzzleVelocity;
            NavalBombardment.GunBulletSelfDestructRef(__instance) = __state.OrigSelfDestruct;
            NavalBombardment.GunProximityFuseTargetRef(__instance) = __state.OrigProximityTarget;
        }
    }
    [HarmonyPatch(typeof(BulletSim), "AddBullet")]
    public static class BulletSim_AddBullet_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BulletSim __instance, ref float destructTimer, ref Unit target)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null) return;
            WeaponInfo wi = NavalBombardment.BulletSimWeaponInfoRef(__instance);
            if (wi != null && (wi.overHorizon || wi.pierceDamage >= 1000f || NavalBombardment.GetBombardmentType(wi) != BombardmentType.None))
            {
                if (!NavalBombardment.IsEligibleForBombardmentAndVelocity(wi, null, null, null))
                {
                    return;
                }
                float uncapVal = Plugin.Bombardment_SelfDestructUncap.Value;
                if (destructTimer < uncapVal)
                {
                    destructTimer = uncapVal;
                }
                if (Plugin.Bombardment_SuppressKineticProximityFuse.Value && wi.pierceDamage >= 1000f)
                {
                    target = null;
                }
            }
        }
    }
    [HarmonyPatch(typeof(AimSolver), "GetAimVector")]
    public static class AimSolver_GetAimVector_GuidedShell45_Patch
    {
        public struct AimSolverState
        {
            public float OrigMuzzleVelocity;
            public float OrigMaxSpeed;
            public bool Modified;
        }
        [HarmonyPrefix]
        public static bool Prefix(AimSolver __instance, ref float targetRange, ref Vector3 __result, out AimSolverState __state)
        {
            __state = new AimSolverState
            {
                OrigMuzzleVelocity = 0f,
                OrigMaxSpeed = -1f,
                Modified = false
            };
            if (!Plugin.EnableGoth.Value || !Plugin.Bombardment_Enable.Value || __instance == null)
            {
                return true;
            }
            Unit currentTarget = NavalBombardment.AimSolverCurrentTargetRef(__instance);
            if (currentTarget == null || currentTarget.disabled || currentTarget is Aircraft || currentTarget is Missile)
            {
                return true;
            }
            Transform firingTransform = NavalBombardment.AimSolverFiringTransformRef(__instance);
            if (firingTransform == null)
            {
                return true;
            }
            WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance);
            if (wi == null || wi.missile || !wi.gun)
            {
                return true;
            }
            Unit attachedUnit = NavalBombardment.AimSolverAttachedUnitRef(__instance) ?? firingTransform.GetComponentInParent<Unit>();
            if (attachedUnit == null || attachedUnit is Aircraft)
            {
                return true;
            }
            GlobalPosition muzzlePos = firingTransform.GlobalPosition();
            GlobalPosition targetPos = currentTarget.GlobalPosition();
            float dist = FastMath.Distance(targetPos, muzzlePos);
            float threshold = Plugin.Bombardment_GuidedShell45Threshold.Value; 
            if (dist < threshold)
            {
                return true;
            }
            bool isShip = NavalBombardment.IsShipPlatform(firingTransform, null, attachedUnit, wi);
            Turret turret = firingTransform.GetComponentInParent<Turret>();
            bool eligible = NavalBombardment.IsEligibleForBombardmentAndVelocity(wi, firingTransform, attachedUnit, turret);
            if (!eligible)
            {
                return true; 
            }
            float caliberMm = NavalBombardment.GetCaliberMillimeters(wi, firingTransform, turret, attachedUnit);
            float maxCaliberMm = Plugin.Bombardment_GuidedShellMaxDiameterFor45.Value * 1000f; 
            if (isShip && caliberMm > 0f && caliberMm < maxCaliberMm && Plugin.Bombardment_GuidedShell45Elevation.Value)
            {
                targetRange = dist;
                Vector3 targetVel = (currentTarget.speed < 1f) ? Vector3.zero : currentTarget.rb.velocity;
                Vector3 shooterVel = (attachedUnit.speed < 1f) ? Vector3.zero : attachedUnit.rb.velocity;
                Vector3 relVel = targetVel - shooterVel;
                float muzzleV = Plugin.Bombardment_ShipExitVelocity.Value;
                float horizSpeed = Mathf.Max(500f, muzzleV * 0.7071068f);
                float estFlightTime = dist / horizSpeed;
                Vector3 dPos = targetPos - muzzlePos;
                Vector3 horizVec = new Vector3(dPos.x + relVel.x * estFlightTime, 0f, dPos.z + relVel.z * estFlightTime);
                if (horizVec.sqrMagnitude < 1f)
                {
                    horizVec = new Vector3(dPos.x, 0f, dPos.z);
                }
                Vector3 horizDir = horizVec.normalized;
                float targetElevDeg = 45f;
                if (turret != null)
                {
                    float maxElev = NavalBombardment.TurretMaxElevationRef(turret);
                    if (maxElev > 0f && maxElev < 45f)
                    {
                        targetElevDeg = maxElev;
                    }
                }
                float elevRad = targetElevDeg * Mathf.Deg2Rad;
                Vector3 aimDir = horizDir * Mathf.Cos(elevRad) + Vector3.up * Mathf.Sin(elevRad);
                __result = aimDir * dist;
                if (Plugin.Bombardment_DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[NavalBombardment] Forced 45 deg elevation on '{attachedUnit.name}' {wi.weaponName} (caliber: {caliberMm:F0}mm < {maxCaliberMm:F0}mm) for target '{currentTarget.name}' at {dist:F0}m (> {threshold:F0}m).");
                }
                return false; 
            }
            if (Plugin.Bombardment_UnifyShipVelocity.Value)
            {
                NavalBombardment.CacheOriginalMuzzleVelocity(wi);
                __state.OrigMuzzleVelocity = wi.muzzleVelocity;
                __state.OrigMaxSpeed = wi.maxSpeed;
                __state.Modified = true;
                wi.muzzleVelocity = Plugin.Bombardment_ShipExitVelocity.Value; 
                wi.maxSpeed = Plugin.Bombardment_ShipExitVelocity.Value;
            }
            return true; 
        }
        [HarmonyPostfix]
        public static void Postfix(AimSolver __instance, AimSolverState __state)
        {
            if (!__state.Modified || __instance == null) return;
            WeaponInfo wi = NavalBombardment.AimSolverWeaponInfoRef(__instance);
            if (wi != null)
            {
                wi.muzzleVelocity = __state.OrigMuzzleVelocity;
                wi.maxSpeed = __state.OrigMaxSpeed;
            }
        }
    }
}