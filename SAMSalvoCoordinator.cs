using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public static class SAMSalvoCoordinator
    {
        private static readonly Dictionary<PersistentID, float> _platformLastFiredTime = new Dictionary<PersistentID, float>();
        private static readonly List<PersistentID> _pruneKeys = new List<PersistentID>();
        private static float _lastPruneTime;
        private struct ThreatFrameCache
        {
            public int frame;
            public bool threatened;
        }
        private static readonly Dictionary<PersistentID, ThreatFrameCache> _threatCache = new Dictionary<PersistentID, ThreatFrameCache>(16);
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretDisabledRef =
            AccessTools.FieldRefAccess<Turret, bool>("disabled");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation> TurretCurrentWeaponStationRef =
            AccessTools.FieldRefAccess<Turret, WeaponStation>("currentWeaponStation");
        internal static readonly AccessTools.FieldRef<Turret, Unit> TurretTargetRef =
            AccessTools.FieldRefAccess<Turret, Unit>("target");
        internal static readonly AccessTools.FieldRef<Turret, FiringCone[]> TurretFiringConesRef =
            AccessTools.FieldRefAccess<Turret, FiringCone[]>("firingCones");
        internal static readonly AccessTools.FieldRef<Turret, List<Unit>> TurretPotentialTargetsRef =
            AccessTools.FieldRefAccess<Turret, List<Unit>>("potentialTargets");
        internal static readonly Func<Turret, WeaponStation, bool> TurretAimTurretDelegate =
            AccessTools.MethodDelegate<Func<Turret, WeaponStation, bool>>(AccessTools.Method(typeof(Turret), "AimTurret", new Type[] { typeof(WeaponStation) }));
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
            SeekerSlotCoordinator.PruneStaleEntries();
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
                    if (!SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID))
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
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(enemyMissile.persistentID)) continue;
                if (SeekerSlotCoordinator.IsSeekerSlotFilled(enemyMissile.persistentID, cat)) continue;
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
        public static void ForceChangeTarget(Turret turret, WeaponStation ws, FoxCategory cat)
        {
            if (turret == null || ws == null) return;
            Unit attachedUnit = turret.GetAttachedUnit();
            if (attachedUnit == null || attachedUnit.disabled) return;
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) && !IsSalvoQuotaSaturated(attachedUnit, buddyRadius))
            {
                if (TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit threat))
                {
                    turret.SetTarget(threat.persistentID, ws.Number);
                    turret.enabled = true;
                    if (ws.Weapons != null)
                    {
                        for (int i = 0; i < ws.Weapons.Count; i++)
                        {
                            ws.Weapons[i].SetTarget(threat);
                        }
                    }
                    if (Plugin.SAM_DebugLog.Value)
                    {
                        Plugin.Log.LogInfo($"[SAM] ForceChangeTarget: {attachedUnit.name} switched to threat {threat.name}");
                    }
                    return;
                }
            }
            Unit bestTarget = null;
            float bestScore = -1f;
            GlobalPosition myPos = attachedUnit.GlobalPosition();
            float minRange = ws.WeaponInfo != null ? ws.WeaponInfo.targetRequirements.minRange : 0f;
            float maxRange = ws.WeaponInfo != null ? ws.WeaponInfo.targetRequirements.maxRange : 30000f;
            HashSet<Unit> evaluated = new HashSet<Unit>();
            List<Unit> candidates = TurretPotentialTargetsRef(turret);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Unit u = candidates[i];
                    if (u != null && !evaluated.Contains(u))
                    {
                        evaluated.Add(u);
                        EvaluateCandidate(turret, ws, cat, u, myPos, minRange, maxRange, ref bestTarget, ref bestScore);
                    }
                }
            }
            if (attachedUnit.NetworkHQ != null && attachedUnit.NetworkHQ.trackingDatabase != null)
            {
                foreach (var kvp in attachedUnit.NetworkHQ.trackingDatabase)
                {
                    if (kvp.Value.TryGetUnit(out Unit u) && u != null && !evaluated.Contains(u))
                    {
                        evaluated.Add(u);
                        EvaluateCandidate(turret, ws, cat, u, myPos, minRange, maxRange, ref bestTarget, ref bestScore);
                    }
                }
            }
            if (bestTarget != null)
            {
                turret.SetTarget(bestTarget.persistentID, ws.Number);
                turret.enabled = true;
                if (ws.Weapons != null)
                {
                    for (int i = 0; i < ws.Weapons.Count; i++)
                    {
                        ws.Weapons[i].SetTarget(bestTarget);
                    }
                }
                if (Plugin.SAM_DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[SAM] ForceChangeTarget: {attachedUnit.name} switched to {bestTarget.name} (cat: {cat})");
                }
            }
            else
            {
                turret.SetTarget(PersistentID.None, ws.Number);
                if (ws.Weapons != null)
                {
                    for (int i = 0; i < ws.Weapons.Count; i++)
                    {
                        ws.Weapons[i].SetTarget(null);
                    }
                }
                if (Plugin.SAM_DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[SAM] ForceChangeTarget: {attachedUnit.name} found no eligible target with free slot {cat}. Turret standing by.");
                }
            }
        }
        private static void EvaluateCandidate(Turret turret, WeaponStation ws, FoxCategory cat, Unit candidate, GlobalPosition myPos, float minRange, float maxRange, ref Unit bestTarget, ref float bestScore)
        {
            if (candidate == null || candidate.disabled) return;
            Unit attachedUnit = turret.GetAttachedUnit();
            if (candidate.NetworkHQ == attachedUnit.NetworkHQ) return;
            if (!(candidate is Aircraft || candidate is Missile)) return;
            if (candidate is Missile m)
            {
                float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
                if (!IsActualThreat(m, attachedUnit, buddyRadius)) return;
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID)) return;
            }
            else
            {
                if (cat != FoxCategory.Uncapped && SeekerSlotCoordinator.IsSeekerSlotFilled(candidate.persistentID, cat))
                {
                    return;
                }
            }
            Vector3 targetVector = candidate.transform.position - turret.transform.position;
            FiringCone[] cones = TurretFiringConesRef(turret);
            if (cones != null && cones.Length > 0 && !FiringConeChecker.VectorWithinFiringCones(cones, targetVector, out var _))
            {
                return;
            }
            float dist = FastMath.Distance(myPos, candidate.GlobalPosition());
            if (dist < minRange || dist > maxRange) return;
            float score = (maxRange - dist) / Mathf.Max(maxRange, 1f);
            if (candidate is Missile) score += 10f;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }
        public static bool IsAirDefenseTurret(Turret turret, out WeaponStation ws, out FoxCategory cat)
        {
            ws = null;
            cat = FoxCategory.Uncapped;
            if (turret == null) return false;
            if (TurretDisabledRef(turret)) return false;
            if (NavalBombardment.IsBombardmentTurret(turret)) return false;
            Unit attachedUnit = turret.GetAttachedUnit();
            if (attachedUnit == null || attachedUnit.disabled) return false;
            if (IsPlayerUnit(attachedUnit)) return false;
            if (Plugin.SAM_RapidFire_SurfaceAndNavalOnly.Value && !IsSurfaceOrNaval(attachedUnit)) return false;
            ws = TurretCurrentWeaponStationRef(turret) ?? turret.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.missile || ws.Ammo <= 0) return false;
            RoleIdentity eff = ws.WeaponInfo.effectiveness;
            if (eff.antiAir <= 0f && eff.antiMissile <= 0f) return false;
            cat = SeekerSlotCoordinator.GetFoxCategory(ws);
            if (cat == FoxCategory.Uncapped) return false;
            return true;
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
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class Turret_FixedUpdate_SAM_Patch
    {
        private static int _preAmmo;
        private static Unit _preTarget;
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (!SAMSalvoCoordinator.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat))
            {
                return true; 
            }
            Unit attachedUnit = __instance.GetAttachedUnit();
            _preAmmo = ws.Ammo;
            _preTarget = __instance.GetTarget();
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            Unit currentTarget = _preTarget;
            if (currentTarget == null || currentTarget.disabled)
            {
                if (SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) && !SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius))
                {
                    if (SAMSalvoCoordinator.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit nextThreat))
                    {
                        __instance.SetTarget(nextThreat.persistentID, ws.Number);
                        __instance.enabled = true;
                        _preTarget = nextThreat;
                        return true;
                    }
                }
                return true;
            }
            if (currentTarget is Missile m)
            {
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID) || !SAMSalvoCoordinator.IsActualThreat(m, attachedUnit, buddyRadius))
                {
                    SAMSalvoCoordinator.ForceChangeTarget(__instance, ws, cat);
                    return false;
                }
                float fastInterval = Plugin.SAM_MissileSalvoInterval != null ? Plugin.SAM_MissileSalvoInterval.Value : 0.25f;
                float elapsed = Time.timeSinceLevelLoad - SAMSalvoCoordinator.GetPlatformLastFiredTime(attachedUnit.persistentID);
                if (elapsed < fastInterval)
                {
                    SAMSalvoCoordinator.TurretAimTurretDelegate(__instance, ws);
                    return false;
                }
                return true;
            }
            if (SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) && !SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius))
            {
                if (SAMSalvoCoordinator.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit emergencyThreat))
                {
                    __instance.SetTarget(emergencyThreat.persistentID, ws.Number);
                    __instance.enabled = true;
                    _preTarget = emergencyThreat;
                    return true;
                }
            }
            if (SeekerSlotCoordinator.IsSeekerSlotFilled(currentTarget.persistentID, cat))
            {
                SAMSalvoCoordinator.ForceChangeTarget(__instance, ws, cat);
                return false;
            }
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            if (!SAMSalvoCoordinator.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat))
            {
                return; 
            }
            Unit attachedUnit = __instance.GetAttachedUnit();
            bool justFired = (ws.Ammo < _preAmmo);
            if (!justFired) return;
            SAMSalvoCoordinator.RecordPlatformFired(attachedUnit.persistentID, Time.timeSinceLevelLoad);
            SAMSalvoCoordinator.ForceChangeTarget(__instance, ws, cat);
        }
    }
    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    public static class Turret_AssessTargetPriority_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance, Unit targetCandidate, ref float priorityThreshold)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (__instance == null || targetCandidate == null || targetCandidate.disabled) return true;
            if (!SAMSalvoCoordinator.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat))
            {
                return true;
            }
            if (!(targetCandidate is Aircraft || targetCandidate is Missile))
            {
                return false; 
            }
            Unit attachedUnit = __instance.GetAttachedUnit();
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (targetCandidate is Missile m)
            {
                if (!SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius)) return false;
                if (!SAMSalvoCoordinator.IsActualThreat(m, attachedUnit, buddyRadius)) return false;
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID)) return false;
                priorityThreshold = 10000f;
                SAMSalvoCoordinator.TurretTargetRef(__instance) = targetCandidate;
                SAMSalvoCoordinator.TurretCurrentWeaponStationRef(__instance) = ws;
                return false;
            }
            if (SeekerSlotCoordinator.IsSeekerSlotFilled(targetCandidate.persistentID, cat))
            {
                return false; 
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Turret), "ChooseTarget", new Type[] { typeof(bool) })]
    public static class Turret_ChooseTarget_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (__instance == null) return true;
            if (!SAMSalvoCoordinator.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat))
            {
                return true;
            }
            Unit attachedUnit = __instance.GetAttachedUnit();
            float buddyRadius = Plugin.SAM_BuddyDefenseRadius != null ? Plugin.SAM_BuddyDefenseRadius.Value : 100f;
            if (SAMSalvoCoordinator.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) && !SAMSalvoCoordinator.IsSalvoQuotaSaturated(attachedUnit, buddyRadius))
            {
                if (SAMSalvoCoordinator.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit threat))
                {
                    __instance.SetTarget(threat.persistentID, ws.Number);
                    __instance.enabled = true;
                    if (ws.Weapons != null)
                    {
                        for (int i = 0; i < ws.Weapons.Count; i++)
                        {
                            ws.Weapons[i].SetTarget(threat);
                        }
                    }
                    return false; 
                }
            }
            return true;
        }
    }
}