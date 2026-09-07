using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public enum FoxCategory
    {
        Uncapped,
        Fox1_SARH,
        Fox2_IR,
        Fox3_ARH
    }
    public class TargetSeekerSlots
    {
        public PersistentID Fox1Missile = PersistentID.None;
        public float Fox1FiredTime = -999f;
        public PersistentID Fox2Missile = PersistentID.None;
        public float Fox2FiredTime = -999f;
        public PersistentID Fox3Missile = PersistentID.None;
        public float Fox3FiredTime = -999f;
        public bool IsSlotFilled(FoxCategory cat, PersistentID targetId, float now)
        {
            PersistentID missileId;
            float firedTime;
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    missileId = Fox1Missile;
                    firedTime = Fox1FiredTime;
                    break;
                case FoxCategory.Fox2_IR:
                    missileId = Fox2Missile;
                    firedTime = Fox2FiredTime;
                    break;
                case FoxCategory.Fox3_ARH:
                    missileId = Fox3Missile;
                    firedTime = Fox3FiredTime;
                    break;
                default:
                    return false;
            }
            if (missileId.IsValid)
            {
                if (UnitRegistry.TryGetUnit(missileId, out var unit) && !unit.disabled && unit is Missile m)
                {
                    if (m.targetID == targetId)
                    {
                        return true;
                    }
                }
                ClearSlot(cat);
                return false;
            }
            if (now - firedTime < 30.0f)
            {
                return true;
            }
            return false;
        }
        public void SetSlot(FoxCategory cat, PersistentID missileId, float now)
        {
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    Fox1Missile = missileId;
                    Fox1FiredTime = now;
                    break;
                case FoxCategory.Fox2_IR:
                    Fox2Missile = missileId;
                    Fox2FiredTime = now;
                    break;
                case FoxCategory.Fox3_ARH:
                    Fox3Missile = missileId;
                    Fox3FiredTime = now;
                    break;
            }
        }
        public void ClearSlot(FoxCategory cat)
        {
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    Fox1Missile = PersistentID.None;
                    Fox1FiredTime = -999f;
                    break;
                case FoxCategory.Fox2_IR:
                    Fox2Missile = PersistentID.None;
                    Fox2FiredTime = -999f;
                    break;
                case FoxCategory.Fox3_ARH:
                    Fox3Missile = PersistentID.None;
                    Fox3FiredTime = -999f;
                    break;
            }
        }
        public bool ClearMissileIfMatches(PersistentID missileId)
        {
            bool cleared = false;
            if (Fox1Missile == missileId) { Fox1Missile = PersistentID.None; Fox1FiredTime = -999f; cleared = true; }
            if (Fox2Missile == missileId) { Fox2Missile = PersistentID.None; Fox2FiredTime = -999f; cleared = true; }
            if (Fox3Missile == missileId) { Fox3Missile = PersistentID.None; Fox3FiredTime = -999f; cleared = true; }
            return cleared;
        }
    }
    public static class SAMSalvoCoordinator
    {
        private static readonly Dictionary<PersistentID, TargetSeekerSlots> _targetSlots = new Dictionary<PersistentID, TargetSeekerSlots>();
        private static readonly Dictionary<WeaponInfo, FoxCategory> _foxCategoryCache = new Dictionary<WeaponInfo, FoxCategory>();
        private static readonly Dictionary<PersistentID, float> _platformLastFiredTime = new Dictionary<PersistentID, float>();
        private static readonly Dictionary<PersistentID, PersistentID> _missileToTarget = new Dictionary<PersistentID, PersistentID>();
        private static readonly Dictionary<PersistentID, PersistentID> _missileToOwner = new Dictionary<PersistentID, PersistentID>();
        private static readonly List<PersistentID> _pruneKeys = new List<PersistentID>();
        private static float _lastPruneTime;
        private struct ThreatFrameCache
        {
            public int frame;
            public bool threatened;
        }
        private static readonly Dictionary<PersistentID, ThreatFrameCache> _threatCache = new Dictionary<PersistentID, ThreatFrameCache>(16);
        internal static readonly AccessTools.FieldRef<Weapon, float> WeaponLastFiredRef =
            AccessTools.FieldRefAccess<Weapon, float>("lastFired");
        internal static readonly AccessTools.FieldRef<MissileLauncher, float> MissileLauncherFireIntervalRef =
            AccessTools.FieldRefAccess<MissileLauncher, float>("fireInterval");
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretDisabledRef =
            AccessTools.FieldRefAccess<Turret, bool>("disabled");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation> TurretCurrentWeaponStationRef =
            AccessTools.FieldRefAccess<Turret, WeaponStation>("currentWeaponStation");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretLockTimeRef =
            AccessTools.FieldRefAccess<Turret, float>("lockTime");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretTimeOnTargetRef =
            AccessTools.FieldRefAccess<Turret, float>("timeOnTarget");
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretNewTargetSearchAfterFireRef =
            AccessTools.FieldRefAccess<Turret, bool>("newTargetSearchAfterFire");
        public static bool IsSeekerSlotFilled(PersistentID targetId, FoxCategory cat)
        {
            if (cat == FoxCategory.Uncapped || !targetId.IsValid) return false;
            if (!_targetSlots.TryGetValue(targetId, out var slots)) return false;
            return slots.IsSlotFilled(cat, targetId, Time.timeSinceLevelLoad);
        }
        public static void MarkSeekerSlotFilled(PersistentID targetId, FoxCategory cat, PersistentID missileId = default)
        {
            if (cat == FoxCategory.Uncapped || !targetId.IsValid) return;
            if (!_targetSlots.TryGetValue(targetId, out var slots))
            {
                slots = new TargetSeekerSlots();
                _targetSlots[targetId] = slots;
            }
            slots.SetSlot(cat, missileId, Time.timeSinceLevelLoad);
        }
        public static bool IsTargetEngagedByAnyMissile(PersistentID targetId)
        {
            if (!targetId.IsValid) return false;
            if (!_targetSlots.TryGetValue(targetId, out var slots)) return false;
            float now = Time.timeSinceLevelLoad;
            return slots.IsSlotFilled(FoxCategory.Fox1_SARH, targetId, now)
                || slots.IsSlotFilled(FoxCategory.Fox2_IR, targetId, now)
                || slots.IsSlotFilled(FoxCategory.Fox3_ARH, targetId, now);
        }
        public static void OnMissileSpawned(Missile missile, Unit target, Unit owner)
        {
            if (missile == null || target == null || owner == null) return;
            FoxCategory cat = GetFoxCategory(missile);
            if (cat == FoxCategory.Uncapped) return;
            float now = Time.timeSinceLevelLoad;
            MarkSeekerSlotFilled(target.persistentID, cat, missile.persistentID);
            _platformLastFiredTime[owner.persistentID] = now;
            _missileToTarget[missile.persistentID] = target.persistentID;
            _missileToOwner[missile.persistentID] = owner.persistentID;
            if (Plugin.SAM_DebugLog.Value)
            {
                Plugin.Log.LogInfo($"[SAM] Missile Spawned: {missile.name} (cat {cat}) tracking {target.name}. Bound to slot.");
            }
        }
        public static void OnMissileTerminated(Missile missile)
        {
            if (missile == null) return;
            PersistentID mId = missile.persistentID;
            if (_missileToTarget.TryGetValue(mId, out PersistentID targetId))
            {
                _missileToTarget.Remove(mId);
                if (_targetSlots.TryGetValue(targetId, out var slots))
                {
                    slots.ClearMissileIfMatches(mId);
                }
            }
            if (_missileToOwner.ContainsKey(mId))
            {
                _missileToOwner.Remove(mId);
            }
            if (missile.targetID.IsValid && _targetSlots.TryGetValue(missile.targetID, out var tSlots))
            {
                tSlots.ClearMissileIfMatches(mId);
            }
        }
        public static void OnMissileTargetChanged(Missile missile, PersistentID oldTargetId, PersistentID newTargetId)
        {
            if (missile == null) return;
            FoxCategory cat = GetFoxCategory(missile);
            if (cat == FoxCategory.Uncapped) return;
            if (oldTargetId.IsValid && _targetSlots.TryGetValue(oldTargetId, out var oldSlots))
            {
                oldSlots.ClearMissileIfMatches(missile.persistentID);
            }
            if (newTargetId.IsValid)
            {
                MarkSeekerSlotFilled(newTargetId, cat, missile.persistentID);
                _missileToTarget[missile.persistentID] = newTargetId;
            }
            else
            {
                _missileToTarget.Remove(missile.persistentID);
            }
        }
        public static float GetPlatformLastFiredTime(PersistentID unitId)
        {
            if (_platformLastFiredTime.TryGetValue(unitId, out float t))
            {
                return t;
            }
            return -999f;
        }
        public static void RecordPlatformFired(PersistentID unitId, float time)
        {
            _platformLastFiredTime[unitId] = time;
        }
        public static void PruneStaleEntries()
        {
            float now = Time.timeSinceLevelLoad;
            if (now - _lastPruneTime < 5.0f) return;
            _lastPruneTime = now;
            _threatCache.Clear();
            _pruneKeys.Clear();
            foreach (var kvp in _targetSlots)
            {
                if (!UnitRegistry.TryGetUnit(kvp.Key, out var u) || u.disabled)
                {
                    _pruneKeys.Add(kvp.Key);
                }
            }
            for (int i = 0; i < _pruneKeys.Count; i++)
            {
                _targetSlots.Remove(_pruneKeys[i]);
            }
            _pruneKeys.Clear();
            foreach (var kvp in _platformLastFiredTime)
            {
                if (!UnitRegistry.TryGetUnit(kvp.Key, out var u) || u.disabled)
                {
                    _pruneKeys.Add(kvp.Key);
                }
            }
            for (int i = 0; i < _pruneKeys.Count; i++)
            {
                _platformLastFiredTime.Remove(_pruneKeys[i]);
            }
        }
        public static bool IsMissileTrajectoryThreateningPosition(Missile enemyMissile, Vector3 targetPos, float maxCpa = 100f)
        {
            if (enemyMissile == null || enemyMissile.disabled) return false;
            Vector3 missilePos = enemyMissile.transform.position;
            Vector3 toTarget = targetPos - missilePos;
            float distSq = toTarget.sqrMagnitude;
            if (distSq > 225000000f) return false;
            float maxCpaSq = maxCpa * maxCpa;
            if (distSq <= maxCpaSq)
            {
                return true;
            }
            Vector3 missileVel = (enemyMissile.rb != null && !enemyMissile.rb.isKinematic)
                ? enemyMissile.rb.velocity
                : enemyMissile.transform.forward * Mathf.Max(enemyMissile.speed, 50f);
            float speedSq = missileVel.sqrMagnitude;
            if (speedSq < 100f)
            {
                missileVel = enemyMissile.transform.forward * 50f;
                speedSq = 2500f;
            }
            float dot = Vector3.Dot(toTarget, missileVel);
            if (dot <= 0f)
            {
                return false; 
            }
            float cpaSq = distSq - (dot * dot / speedSq);
            return cpaSq <= maxCpaSq;
        }
        public static bool IsActualThreat(Missile enemyMissile, Unit firingUnit, float buddyRadius = 100f)
        {
            if (enemyMissile == null || enemyMissile.disabled || firingUnit == null || firingUnit.disabled) return false;
            if (enemyMissile.owner == firingUnit) return false;
            if (enemyMissile.NetworkHQ != null && firingUnit.NetworkHQ != null && enemyMissile.NetworkHQ == firingUnit.NetworkHQ) return false;
            if (enemyMissile.targetID.IsValid)
            {
                if (enemyMissile.targetID == firingUnit.persistentID)
                    return true;
                GlobalPosition myPos = firingUnit.GlobalPosition();
                foreach (Unit ally in BattlefieldGrid.GetUnitsInRangeEnumerable(myPos, buddyRadius))
                {
                    if (ally == null || ally.disabled || ally == firingUnit) continue;
                    if (ally.NetworkHQ != firingUnit.NetworkHQ) continue;
                    if (!IsSurfaceOrNaval(ally)) continue;
                    if (enemyMissile.targetID == ally.persistentID)
                        return true;
                }
                return false;
            }
            MissileSeeker seeker = enemyMissile.GetComponent<MissileSeeker>();
            if (seeker is ARHSeeker || seeker is SARHSeeker || seeker is IRSeeker)
            {
                return false;
            }
            if (IsMissileTrajectoryThreateningPosition(enemyMissile, firingUnit.transform.position, buddyRadius))
                return true;
            GlobalPosition myPos2 = firingUnit.GlobalPosition();
            foreach (Unit ally in BattlefieldGrid.GetUnitsInRangeEnumerable(myPos2, buddyRadius))
            {
                if (ally == null || ally.disabled || ally == firingUnit) continue;
                if (ally.NetworkHQ != firingUnit.NetworkHQ) continue;
                if (!IsSurfaceOrNaval(ally)) continue;
                if (IsMissileTrajectoryThreateningPosition(enemyMissile, ally.transform.position, buddyRadius))
                    return true;
            }
            return false;
        }
        public static bool IsUnitOrBuddyThreatened(Unit firingUnit, float buddyRadius = 100f)
        {
            if (firingUnit == null || firingUnit.disabled) return false;
            if (Plugin.SAM_RapidFire_SurfaceAndNavalOnly.Value && !IsSurfaceOrNaval(firingUnit)) return false;
            int currentFrame = Time.frameCount;
            if (_threatCache.TryGetValue(firingUnit.persistentID, out var cached) && cached.frame == currentFrame)
            {
                return cached.threatened;
            }
            bool threatened = false;
            List<Unit> allUnits = UnitRegistry.allUnits;
            int count = allUnits.Count;
            for (int i = 0; i < count; i++)
            {
                Unit u = allUnits[i];
                if (u == null || u.disabled || !(u is Missile m)) continue;
                if (IsActualThreat(m, firingUnit, buddyRadius))
                {
                    threatened = true;
                    break;
                }
            }
            _threatCache[firingUnit.persistentID] = new ThreatFrameCache { frame = currentFrame, threatened = threatened };
            return threatened;
        }
        public static bool IsSalvoQuotaSaturated(Unit firingUnit, float buddyRadius = 100f)
        {
            if (firingUnit == null || firingUnit.disabled) return false;
            int totalThreats = 0;
            int unengagedThreats = 0;
            List<Unit> allUnits = UnitRegistry.allUnits;
            int count = allUnits.Count;
            for (int i = 0; i < count; i++)
            {
                Unit u = allUnits[i];
                if (u == null || u.disabled || !(u is Missile m)) continue;
                if (IsActualThreat(m, firingUnit, buddyRadius))
                {
                    totalThreats++;
                    if (!IsTargetEngagedByAnyMissile(m.persistentID))
                    {
                        unengagedThreats++;
                    }
                }
            }
            if (totalThreats == 0) return false;
            return unengagedThreats == 0;
        }
        public static bool TryFindUnengagedThreat(Unit firingUnit, WeaponStation ws, FoxCategory cat, out Unit threat)
        {
            threat = null;
            if (firingUnit == null || ws == null || ws.WeaponInfo == null) return false;
            GlobalPosition myPos = firingUnit.GlobalPosition();
            float minRange = ws.WeaponInfo.targetRequirements.minRange;
            float maxRange = ws.WeaponInfo.targetRequirements.maxRange;
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            List<Unit> allUnits = UnitRegistry.allUnits;
            int count = allUnits.Count;
            float bestDist = float.MaxValue;
            Unit bestTarget = null;
            for (int i = 0; i < count; i++)
            {
                Unit u = allUnits[i];
                if (u == null || u.disabled || !(u is Missile enemyMissile)) continue;
                if (!IsActualThreat(enemyMissile, firingUnit, buddyRadius)) continue;
                if (IsTargetEngagedByAnyMissile(enemyMissile.persistentID)) continue;
                if (IsSeekerSlotFilled(enemyMissile.persistentID, cat)) continue;
                GlobalPosition missilePos = enemyMissile.GlobalPosition();
                float dist = FastMath.Distance(myPos, missilePos);
                if (dist < minRange || dist > maxRange) continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = enemyMissile;
                }
            }
            if (bestTarget != null)
            {
                threat = bestTarget;
                return true;
            }
            return false;
        }
        public static bool IsSurfaceOrNaval(Unit unit)
        {
            if (unit == null) return false;
            return unit is Ship || unit is GroundVehicle || unit is Building;
        }
        public static bool IsPlayerUnit(Unit unit)
        {
            if (unit == null) return false;
            return SceneSingleton<CombatHUD>.i != null && SceneSingleton<CombatHUD>.i.aircraft == unit;
        }
        public static FoxCategory GetFoxCategory(WeaponStation ws)
        {
            if (ws == null || ws.WeaponInfo == null) return FoxCategory.Uncapped;
            return GetFoxCategory(ws.WeaponInfo);
        }
        public static FoxCategory GetFoxCategory(MissileLauncher ml)
        {
            if (ml == null || ml.info == null) return FoxCategory.Uncapped;
            return GetFoxCategory(ml.info);
        }
        public static FoxCategory GetFoxCategory(WeaponInfo wi)
        {
            if (wi == null) return FoxCategory.Uncapped;
            if (_foxCategoryCache.TryGetValue(wi, out var cat)) return cat;
            cat = ResolveFoxCategory(wi);
            _foxCategoryCache[wi] = cat;
            return cat;
        }
        public static FoxCategory GetFoxCategory(Missile missile)
        {
            if (missile == null) return FoxCategory.Uncapped;
            WeaponInfo wi = missile.GetWeaponInfo();
            if (wi != null) return GetFoxCategory(wi);
            MissileSeeker s = missile.GetComponent<MissileSeeker>();
            if (s is SARHSeeker) return FoxCategory.Fox1_SARH;
            if (s is IRSeeker) return FoxCategory.Fox2_IR;
            if (s is ARHSeeker) return FoxCategory.Fox3_ARH;
            return FoxCategory.Uncapped;
        }
        private static FoxCategory ResolveFoxCategory(WeaponInfo wi)
        {
            if (!wi.missile || wi.weaponPrefab == null) return FoxCategory.Uncapped;
            Missile missile = wi.weaponPrefab.GetComponent<Missile>();
            if (missile == null) return FoxCategory.Uncapped;
            MissileSeeker seeker = wi.weaponPrefab.GetComponent<MissileSeeker>();
            if (seeker == null) seeker = missile.GetComponent<MissileSeeker>();
            if (seeker is SARHSeeker) return FoxCategory.Fox1_SARH;
            if (seeker is IRSeeker) return FoxCategory.Fox2_IR;
            if (seeker is ARHSeeker) return FoxCategory.Fox3_ARH;
            return FoxCategory.Uncapped;
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "Ready")]
    public static class WeaponStation_Ready_SAM_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(WeaponStation __instance, ref bool __result)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (__instance == null || __instance.Ammo <= 0 || __instance.WeaponInfo == null) return true;
            Unit attachedUnit = (__instance.Weapons != null && __instance.Weapons.Count > 0 && __instance.Weapons[0] != null) ? __instance.Weapons[0].attachedUnit : null;
            if (attachedUnit == null) return true;
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius))
            {
                if (SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius))
                {
                    __result = false;
                    return false;
                }
                float fastInterval = Plugin.SAM_MissileSalvoInterval != null ? Plugin.SAM_MissileSalvoInterval.Value : 0.25f;
                float elapsed = Time.timeSinceLevelLoad - SAMSalvoCoordinator.GetPlatformLastFiredTime(attachedUnit.persistentID);
                if (elapsed >= fastInterval)
                {
                    __result = __instance.GetReloadStatusMin() <= 0f;
                    return false;
                }
                __result = false;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(MissileLauncher), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    public static class MissileLauncher_Fire_SAM_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MissileLauncher __instance, Unit owner, ref Unit target, Vector3 inheritedVelocity, WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (__instance == null || __instance.ammo <= 0) return true;
            Unit unit = owner ?? __instance.attachedUnit;
            if (unit == null || SAMSalvoCoordinator.IsPlayerUnit(unit)) return true;
            FoxCategory cat = SAMSalvoCoordinator.GetFoxCategory(__instance);
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            bool isThreatened = SAMSalvoCoordinator.IsUnitOrBuddyThreatened(unit, buddyRadius);
            float now = Time.timeSinceLevelLoad;
            if (isThreatened)
            {
                if (SAMSalvoCoordinator.IsSalvoQuotaSaturated(unit, buddyRadius))
                {
                    return false;
                }
                float fastInterval = Plugin.SAM_MissileSalvoInterval != null ? Plugin.SAM_MissileSalvoInterval.Value : 0.25f;
                float lastFired = SAMSalvoCoordinator.GetPlatformLastFiredTime(unit.persistentID);
                if (now - lastFired < fastInterval)
                {
                    return false;
                }
                if (target == null || target.disabled || SAMSalvoCoordinator.IsTargetEngagedByAnyMissile(target.persistentID))
                {
                    if (SAMSalvoCoordinator.TryFindUnengagedThreat(unit, weaponStation, cat, out Unit altThreat))
                    {
                        target = altThreat;
                    }
                    else
                    {
                        return false;
                    }
                }
                SAMSalvoCoordinator.RecordPlatformFired(unit.persistentID, now);
                SAMSalvoCoordinator.MarkSeekerSlotFilled(target.persistentID, cat);
                Turret turret = (weaponStation != null && weaponStation.HasTurret() && weaponStation.Turrets.Count > 0) ? weaponStation.Turrets[0] : null;
                if (turret != null)
                {
                    turret.SetTarget(target.persistentID, weaponStation.Number);
                    float lockTime = SAMSalvoCoordinator.TurretLockTimeRef(turret);
                    SAMSalvoCoordinator.TurretTimeOnTargetRef(turret) = lockTime + 0.1f;
                }
            }
            else
            {
                if (cat != FoxCategory.Uncapped && target != null && !target.disabled && target.NetworkHQ != unit.NetworkHQ)
                {
                    if (SAMSalvoCoordinator.IsSeekerSlotFilled(target.persistentID, cat))
                    {
                        if (Plugin.SAM_DebugLog.Value)
                        {
                            Plugin.Log.LogInfo($"[SAM] Suppressed MissileLauncher: Target {target.name} slot {cat} already filled.");
                        }
                        return false;
                    }
                    SAMSalvoCoordinator.MarkSeekerSlotFilled(target.persistentID, cat);
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "Fire", new Type[] { typeof(Unit), typeof(Unit) })]
    public static class WeaponStation_Fire_SAM_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(WeaponStation __instance, Unit owner, ref Unit target)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (owner == null || SAMSalvoCoordinator.IsPlayerUnit(owner)) return true;
            if (__instance.WeaponInfo == null || !__instance.WeaponInfo.missile) return true;
            FoxCategory cat = SAMSalvoCoordinator.GetFoxCategory(__instance);
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            bool isThreatened = SAMSalvoCoordinator.IsUnitOrBuddyThreatened(owner, buddyRadius);
            if (isThreatened)
            {
                if (SAMSalvoCoordinator.IsSalvoQuotaSaturated(owner, buddyRadius))
                {
                    return false;
                }
                if (target == null || target.disabled || SAMSalvoCoordinator.IsTargetEngagedByAnyMissile(target.persistentID))
                {
                    if (SAMSalvoCoordinator.TryFindUnengagedThreat(owner, __instance, cat, out Unit altTarget))
                    {
                        target = altTarget;
                        Turret turret = (__instance.HasTurret() && __instance.Turrets.Count > 0) ? __instance.Turrets[0] : null;
                        if (turret != null)
                        {
                            turret.SetTarget(altTarget.persistentID, __instance.Number);
                            float lockTime = SAMSalvoCoordinator.TurretLockTimeRef(turret);
                            SAMSalvoCoordinator.TurretTimeOnTargetRef(turret) = lockTime + 0.1f;
                        }
                        return true;
                    }
                    return false;
                }
            }
            else
            {
                if (cat != FoxCategory.Uncapped && target != null && !target.disabled && target.NetworkHQ != owner.NetworkHQ)
                {
                    if (SAMSalvoCoordinator.IsSeekerSlotFilled(target.persistentID, cat))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    public static class Spawner_SpawnMissile_SAM_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Missile __result, Unit target, Unit owner)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (__result != null && target != null && owner != null)
            {
                SAMSalvoCoordinator.OnMissileSpawned(__result, target, owner);
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class Turret_FixedUpdate_SAM_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (__instance == null) return;
            Unit attachedUnit = __instance.GetAttachedUnit();
            if (attachedUnit == null || attachedUnit.disabled) return;
            if (SAMSalvoCoordinator.TurretDisabledRef(__instance)) return;
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (!SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius)) return;
            if (SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius)) return;
            WeaponStation ws = SAMSalvoCoordinator.TurretCurrentWeaponStationRef(__instance) ?? __instance.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.missile || ws.Ammo <= 0) return;
            FoxCategory cat = SAMSalvoCoordinator.GetFoxCategory(ws);
            Unit currentTarget = __instance.GetTarget();
            bool needsTarget = (currentTarget == null || currentTarget.disabled || SAMSalvoCoordinator.IsTargetEngagedByAnyMissile(currentTarget.persistentID));
            if (needsTarget)
            {
                if (SAMSalvoCoordinator.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit nextThreat))
                {
                    __instance.SetTarget(nextThreat.persistentID, ws.Number);
                    __instance.enabled = true;
                    currentTarget = nextThreat;
                }
            }
            if (currentTarget != null && !currentTarget.disabled && currentTarget is Missile m)
            {
                if (SAMSalvoCoordinator.IsActualThreat(m, attachedUnit, buddyRadius))
                {
                    float lockTime = SAMSalvoCoordinator.TurretLockTimeRef(__instance);
                    if (SAMSalvoCoordinator.TurretTimeOnTargetRef(__instance) <= lockTime)
                    {
                        SAMSalvoCoordinator.TurretTimeOnTargetRef(__instance) = lockTime + 0.1f;
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "ChooseTarget")]
    public static class Turret_ChooseTarget_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (__instance == null || __instance.GetAttachedUnit() == null || __instance.GetAttachedUnit().disabled) return;
            Unit attachedUnit = __instance.GetAttachedUnit();
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (!SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius)) return;
            if (SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius)) return;
            WeaponStation ws = SAMSalvoCoordinator.TurretCurrentWeaponStationRef(__instance) ?? __instance.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.missile) return;
            FoxCategory cat = SAMSalvoCoordinator.GetFoxCategory(ws);
            Unit currentTarget = __instance.GetTarget();
            if (currentTarget == null || currentTarget.disabled || SAMSalvoCoordinator.IsTargetEngagedByAnyMissile(currentTarget.persistentID))
            {
                if (SAMSalvoCoordinator.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit fallbackThreat))
                {
                    __instance.SetTarget(fallbackThreat.persistentID, ws.Number);
                    __instance.enabled = true;
                }
            }
        }
    }
    [HarmonyPatch(typeof(Turret), "Turret_OnInitialize")]
    public static class Turret_OnInitialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (__instance == null) return;
            WeaponStation ws = __instance.GetWeaponStation();
            if (ws != null && ws.WeaponInfo != null && ws.WeaponInfo.missile)
            {
                SAMSalvoCoordinator.TurretNewTargetSearchAfterFireRef(__instance) = true;
            }
        }
    }
    [HarmonyPatch(typeof(Missile), "TargetIDChanged")]
    public static class Missile_TargetIDChanged_SAM_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Missile __instance, PersistentID oldValue, PersistentID newValue)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            SAMSalvoCoordinator.OnMissileTargetChanged(__instance, oldValue, newValue);
        }
    }
    [HarmonyPatch(typeof(Missile), "Detonate", new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    public static class Missile_Detonate_SAM_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Missile __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            SAMSalvoCoordinator.OnMissileTerminated(__instance);
        }
    }
    [HarmonyPatch(typeof(Missile), "UnitDisabled", new Type[] { typeof(bool), typeof(bool) })]
    public static class Missile_UnitDisabled_SAM_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Missile __instance, bool oldState, bool newState)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (newState)
            {
                SAMSalvoCoordinator.OnMissileTerminated(__instance);
            }
        }
    }
}