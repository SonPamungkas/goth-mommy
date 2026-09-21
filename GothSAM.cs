using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public static class RippleSam
    {
        private struct ThreatFrameCache { public int frame; public bool threatened; }
        private static readonly Dictionary<PersistentID, ThreatFrameCache> _threatCache = new Dictionary<PersistentID, ThreatFrameCache>(16);
        private static readonly Dictionary<PersistentID, float> _lastFiredAtThreat = new Dictionary<PersistentID, float>(32);
        private static float _lastSeenTime;
        private static void CheckLevelReset() { float now = Time.timeSinceLevelLoad; if (now < _lastSeenTime - 1f) { Reset(); Plugin.Log?.LogInfo(GothLog.Tag + " Level reload detected (RippleSam). Threat/cooldown/platform caches cleared."); } _lastSeenTime = now; }
        public static void Reset() { _threatCache.Clear(); _lastFiredAtThreat.Clear(); Turret_FixedUpdate_SAM_Patch.ClearOnLevelReset(); }
        internal static readonly AccessTools.FieldRef<Weapon, float> WeaponLastFiredRef = AccessTools.FieldRefAccess<Weapon, float>("lastFired");
        internal static readonly AccessTools.FieldRef<MissileLauncher, float> MissileLauncherFireIntervalRef = AccessTools.FieldRefAccess<MissileLauncher, float>("fireInterval");
        internal static readonly AccessTools.FieldRef<Turret, bool> TurretDisabledRef = AccessTools.FieldRefAccess<Turret, bool>("disabled");
        internal static readonly AccessTools.FieldRef<Turret, WeaponStation> TurretCurrentWeaponStationRef = AccessTools.FieldRefAccess<Turret, WeaponStation>("currentWeaponStation");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretLockTimeRef = AccessTools.FieldRefAccess<Turret, float>("lockTime");
        internal static readonly AccessTools.FieldRef<Turret, float> TurretTimeOnTargetRef = AccessTools.FieldRefAccess<Turret, float>("timeOnTarget");
        internal static readonly AccessTools.FieldRef<Turret, Unit> TurretTargetRef = AccessTools.FieldRefAccess<Turret, Unit>("target");
        internal static readonly AccessTools.FieldRef<Turret, FiringCone[]> TurretFiringConesRef = AccessTools.FieldRefAccess<Turret, FiringCone[]>("firingCones");
        internal static readonly AccessTools.FieldRef<Turret, List<Unit>> TurretPotentialTargetsRef = AccessTools.FieldRefAccess<Turret, List<Unit>>("potentialTargets");
        internal static readonly Func<Turret, WeaponStation, bool> TurretAimTurretDelegate = AccessTools.MethodDelegate<Func<Turret, WeaponStation, bool>>(AccessTools.Method(typeof(Turret), "AimTurret", new Type[] { typeof(WeaponStation) }));
        public static bool AlreadyEngagedByFleet(Unit firingUnit, Unit threat)
        {
            if (firingUnit == null || threat == null) return false;
            TrackingInfo ti = firingUnit.NetworkHQ?.GetTrackingData(threat.persistentID);
            if (ti == null || ti.missileAttacks <= 0) return false;
            if (Plugin.SamDbg) GothLog.Throttled("fleet." + threat.persistentID, $"SKIP '{GothLog.Name(threat)}': fleet already has {ti.missileAttacks} interceptor(s) allocated (vanilla missileAttacks).");
            return true;
        }
        public static bool ThreatOnCooldown(Unit threat)
        {
            float cd = Plugin.SAM_ThreatReengageCooldown != null ? Plugin.SAM_ThreatReengageCooldown.Value : 3f;
            if (threat == null || cd <= 0f || !_lastFiredAtThreat.TryGetValue(threat.persistentID, out float last) || Time.timeSinceLevelLoad - last >= cd) return false;
            if (Plugin.SamDbg) GothLog.Throttled("cooldown." + threat.persistentID, $"SKIP '{GothLog.Name(threat)}': re-engagement cooldown, {Time.timeSinceLevelLoad - last:F2}s of {cd:F1}s elapsed.");
            return true;
        }
        public static void RecordThreatEngaged(Unit threat) { if (threat != null) _lastFiredAtThreat[threat.persistentID] = Time.timeSinceLevelLoad; }
        public static bool CannotEngage(Unit firingUnit, Unit threat) => AlreadyEngagedByFleet(firingUnit, threat) || ThreatOnCooldown(threat);
        public static bool IsMissileComingTowards(Missile enemyMissile, Vector3 targetPos, float maxCpa = 250f)
        {
            if (enemyMissile == null || enemyMissile.disabled) return false;
            Vector3 toTarget = targetPos - enemyMissile.transform.position; float distSq = toTarget.sqrMagnitude, maxCpaSq = maxCpa * maxCpa;
            if (distSq > 225000000f) return false;
            if (distSq <= maxCpaSq) return true;
            Vector3 vel = (enemyMissile.rb != null && !enemyMissile.rb.isKinematic) ? enemyMissile.rb.velocity : enemyMissile.transform.forward * Mathf.Max(enemyMissile.speed, 50f);
            float speedSq = vel.sqrMagnitude; if (speedSq < 100f) { vel = enemyMissile.transform.forward * 50f; speedSq = 2500f; }
            float dot = Vector3.Dot(toTarget, vel); if (dot <= 0f) return false; 
            return distSq - (dot * dot / speedSq) <= maxCpaSq; 
        }
        public static bool IsActualThreat(Missile enemyMissile, Unit firingUnit, float buddyRadius = 100f)
        {
            if (enemyMissile == null || enemyMissile.disabled || firingUnit == null || firingUnit.disabled || enemyMissile.owner == firingUnit) return false;
            if (enemyMissile.NetworkHQ != null && firingUnit.NetworkHQ != null && enemyMissile.NetworkHQ == firingUnit.NetworkHQ) return false;
            if (enemyMissile.targetID.IsValid)
            {
                if (enemyMissile.targetID == firingUnit.persistentID) return true;
                foreach (Unit ally in BattlefieldGrid.GetUnitsInRangeEnumerable(firingUnit.GlobalPosition(), buddyRadius)) if (ally != null && !ally.disabled && ally != firingUnit && ally.NetworkHQ == firingUnit.NetworkHQ && IsSurfaceOrNaval(ally) && enemyMissile.targetID == ally.persistentID) return true;
                return false;
            }
            MissileSeeker seeker = enemyMissile.GetComponent<MissileSeeker>();
            if (seeker is ARHSeeker || seeker is SARHSeeker || seeker is IRSeeker) return false;
            if (IsMissileComingTowards(enemyMissile, firingUnit.transform.position, buddyRadius)) return true;
            foreach (Unit ally in BattlefieldGrid.GetUnitsInRangeEnumerable(firingUnit.GlobalPosition(), buddyRadius)) if (ally != null && !ally.disabled && ally != firingUnit && ally.NetworkHQ == firingUnit.NetworkHQ && IsSurfaceOrNaval(ally) && IsMissileComingTowards(enemyMissile, ally.transform.position, buddyRadius)) return true;
            return false;
        }
        public static bool IsUnitOrBuddyThreatened(Unit firingUnit, float buddyRadius = 100f)
        {
            CheckLevelReset();
            if (firingUnit == null || firingUnit.disabled || (Plugin.SAM_RapidFire_SurfaceAndNavalOnly.Value && !IsSurfaceOrNaval(firingUnit))) return false;
            int frame = Time.frameCount;
            if (_threatCache.TryGetValue(firingUnit.persistentID, out var cached) && cached.frame == frame) return cached.threatened;
            bool threatened = false; List<Unit> allUnits = UnitRegistry.allUnits;
            for (int i = 0; i < allUnits.Count; i++) { Unit u = allUnits[i]; if (u != null && !u.disabled && u is Missile m && IsActualThreat(m, firingUnit, buddyRadius)) { threatened = true; break; } }
            _threatCache[firingUnit.persistentID] = new ThreatFrameCache { frame = frame, threatened = threatened };
            return threatened;
        }
        public static bool IsSalvoQuotaSaturated(Unit firingUnit, float buddyRadius = 100f)
        {
            if (firingUnit == null || firingUnit.disabled) return false;
            int totalThreats = 0, unengagedThreats = 0; List<Unit> allUnits = UnitRegistry.allUnits;
            for (int i = 0; i < allUnits.Count; i++) { Unit u = allUnits[i]; if (u == null || u.disabled || !(u is Missile m) || !IsActualThreat(m, firingUnit, buddyRadius)) continue; totalThreats++; if (!SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID)) unengagedThreats++; }
            if (totalThreats == 0) { if (Plugin.SamDbg) GothLog.Throttled("quota.none." + firingUnit.persistentID, $"QUOTA: '{GothLog.Name(firingUnit)}' sees 0 threats within {buddyRadius:F0}m. Not saturated, but nothing to shoot at either."); return false; }
            bool saturated = unengagedThreats == 0;
            if (Plugin.SamDbg) GothLog.Throttled($"quota.{firingUnit.persistentID}.{totalThreats}.{unengagedThreats}", $"QUOTA: '{GothLog.Name(firingUnit)}' threats={totalThreats} unengaged={unengagedThreats} -> saturated={saturated}");
            return saturated;
        }
        public static bool TryFindUnengagedThreat(Unit firingUnit, WeaponStation ws, FoxCategory cat, out Unit threat)
        {
            threat = null;
            if (firingUnit == null || ws == null || ws.WeaponInfo == null) return false;
            GlobalPosition myPos = firingUnit.GlobalPosition(); float minRange = ws.WeaponInfo.targetRequirements.minRange, maxRange = ws.WeaponInfo.targetRequirements.maxRange, buddyRadius = GetBuddyRadius(firingUnit), bestDist = float.MaxValue;
            List<Unit> allUnits = UnitRegistry.allUnits; Unit bestTarget = null;
            for (int i = 0; i < allUnits.Count; i++)
            {
                Unit u = allUnits[i]; if (u == null || u.disabled || !(u is Missile enemyMissile)) continue;
                if (!IsActualThreat(enemyMissile, firingUnit, buddyRadius) || SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(enemyMissile.persistentID) || CannotEngage(firingUnit, enemyMissile) || SeekerSlotCoordinator.IsSeekerSlotFilled(enemyMissile.persistentID, cat)) continue;
                if (ws.WeaponInfo.targetRequirements.minIR > 0f && !enemyMissile.HasIRSignature()) continue;
                if (ws.WeaponInfo.targetRequirements.minRadar > 0f && !enemyMissile.HasRadarEmission()) continue;
                float dist = FastMath.Distance(myPos, enemyMissile.GlobalPosition()); if (dist < minRange || dist > maxRange) continue;
                if (dist < bestDist) { bestDist = dist; bestTarget = enemyMissile; }
            }
            if (bestTarget == null) return false;
            threat = bestTarget; return true;
        }
        public static void ForceChangeTarget(Turret turret, WeaponStation ws, FoxCategory cat)
        {
            if (turret == null || ws == null) return;
            Unit attachedUnit = turret.GetAttachedUnit(); if (attachedUnit == null || attachedUnit.disabled) return;
            float buddyRadius = GetBuddyRadius(attachedUnit);
            if (IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) && !IsSalvoQuotaSaturated(attachedUnit, buddyRadius) && TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit threat))
            {
                turret.SetTarget(threat.persistentID, ws.Number); turret.enabled = true;
                if (ws.Weapons != null) for (int i = 0; i < ws.Weapons.Count; i++) ws.Weapons[i].SetTarget(threat);
                if (Plugin.SamDbg) GothLog.Throttled("fct.threat." + turret.GetInstanceID(), $"FORCE CHANGE TARGET: '{GothLog.Name(attachedUnit)}' -> unengaged threat '{GothLog.Name(threat)}' ({cat}).");
                return;
            }
            Unit bestTarget = null; float bestScore = -1f; GlobalPosition myPos = attachedUnit.GlobalPosition();
            float minRange = ws.WeaponInfo != null ? ws.WeaponInfo.targetRequirements.minRange : 0f, maxRange = ws.WeaponInfo != null ? ws.WeaponInfo.targetRequirements.maxRange : 30000f;
            HashSet<Unit> evaluated = new HashSet<Unit>();
            List<Unit> candidates = TurretPotentialTargetsRef(turret);
            if (candidates != null) for (int i = 0; i < candidates.Count; i++) { Unit u = candidates[i]; if (u != null && evaluated.Add(u)) EvaluateCandidate(turret, ws, cat, u, myPos, minRange, maxRange, ref bestTarget, ref bestScore); }
            if (attachedUnit.NetworkHQ?.trackingDatabase != null) foreach (var kvp in attachedUnit.NetworkHQ.trackingDatabase) if (kvp.Value.TryGetUnit(out Unit u) && u != null && evaluated.Add(u)) EvaluateCandidate(turret, ws, cat, u, myPos, minRange, maxRange, ref bestTarget, ref bestScore);
            if (bestTarget != null)
            {
                turret.SetTarget(bestTarget.persistentID, ws.Number); turret.enabled = true;
                if (ws.Weapons != null) for (int i = 0; i < ws.Weapons.Count; i++) ws.Weapons[i].SetTarget(bestTarget);
                if (Plugin.SamDbg) GothLog.Throttled("fct.best." + turret.GetInstanceID(), $"FORCE CHANGE TARGET: '{GothLog.Name(attachedUnit)}' -> '{GothLog.Name(bestTarget)}' ({cat}, score {bestScore:F2}).");
                return;
            }
            turret.SetTarget(PersistentID.None, ws.Number);
            if (ws.Weapons != null) for (int i = 0; i < ws.Weapons.Count; i++) ws.Weapons[i].SetTarget(null);
            if (Plugin.SamDbg) GothLog.Throttled("fct.none." + turret.GetInstanceID(), $"FORCE CHANGE TARGET: '{GothLog.Name(attachedUnit)}' found NO eligible target with a free {cat} slot. Turret standing by (asleep until re-targeted).");
        }
        private static void EvaluateCandidate(Turret turret, WeaponStation ws, FoxCategory cat, Unit candidate, GlobalPosition myPos, float minRange, float maxRange, ref Unit bestTarget, ref float bestScore)
        {
            if (candidate == null || candidate.disabled || candidate.NetworkHQ == turret.GetAttachedUnit().NetworkHQ || !(candidate is Aircraft || candidate is Missile)) return;
            if (candidate is Missile m) { float buddyRadius = GetBuddyRadius(turret.GetAttachedUnit()); if (!IsActualThreat(m, turret.GetAttachedUnit(), buddyRadius) || SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID) || CannotEngage(turret.GetAttachedUnit(), m)) return; }
            else if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(candidate.persistentID)) return;
            if (ws.WeaponInfo != null)
            {
                if (ws.WeaponInfo.targetRequirements.minIR > 0f && !candidate.HasIRSignature()) return;
                if (ws.WeaponInfo.targetRequirements.minRadar > 0f && !candidate.HasRadarEmission()) return;
            }
            FiringCone[] cones = TurretFiringConesRef(turret);
            if (cones != null && cones.Length > 0 && !FiringConeChecker.VectorWithinFiringCones(cones, candidate.transform.position - turret.transform.position, out var _)) return;
            float dist = FastMath.Distance(myPos, candidate.GlobalPosition()); if (dist < minRange || dist > maxRange) return;
            float score = (maxRange - dist) / Mathf.Max(maxRange, 1f) + (candidate is Missile ? 10f : 0f); 
            if (score > bestScore) { bestScore = score; bestTarget = candidate; }
        }
        private static readonly Dictionary<string, int> _gateRejects = new Dictionary<string, int>(16);
        private static int _gateAccepts;
        private static void GateReject(Turret turret, string reason)
        {
            _gateRejects.TryGetValue(reason, out int n); _gateRejects[reason] = n + 1;
            if (Plugin.SamDbg) GothLog.Throttled("gate.reject." + (turret != null ? turret.GetInstanceID() : 0), $"ROLE GATE REJECT: '{GothLog.Name(turret?.GetAttachedUnit())}' -> {reason}");
        }
        public static bool IsAirDefenseTurret(Turret turret, out WeaponStation ws, out FoxCategory cat)
        {
            ws = null; cat = FoxCategory.Uncapped;
            if (turret == null) return false;
            if (TurretDisabledRef(turret)) { GateReject(turret, "turret disabled"); return false; }
            Unit attachedUnit = turret.GetAttachedUnit();
            if (attachedUnit == null || attachedUnit.disabled) { GateReject(turret, "no attached unit or unit disabled"); return false; }
            if (IsPlayerUnit(attachedUnit)) { GateReject(turret, "player-controlled unit"); return false; }
            if (Plugin.SAM_RapidFire_SurfaceAndNavalOnly.Value && !IsSurfaceOrNaval(attachedUnit)) { GateReject(turret, $"not surface/naval ({attachedUnit.GetType().Name}) and RapidFire_SurfaceAndNavalOnly is on"); return false; }
            ws = TurretCurrentWeaponStationRef(turret) ?? turret.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.missile || ws.Ammo <= 0)
            {
                GateReject(turret, ws == null ? "no weapon station" : ws.WeaponInfo == null ? "station has no WeaponInfo" : !ws.WeaponInfo.missile ? $"station '{ws.WeaponInfo.weaponName}' is not a missile station (gun={ws.WeaponInfo.gun})" : $"station '{ws.WeaponInfo.weaponName}' is out of ammo ({ws.Ammo})");
                return false;
            }
            RoleIdentity eff = ws.WeaponInfo.effectiveness; 
            if (eff.antiAir <= 0f && eff.antiMissile <= 0f) { GateReject(turret, $"'{ws.WeaponInfo.weaponName}' has no anti-air role (antiAir={eff.antiAir:F2} antiMissile={eff.antiMissile:F2})"); return false; }
            cat = SeekerSlotCoordinator.GetFoxCategory(ws); 
            if (cat == FoxCategory.Uncapped) { GateReject(turret, $"'{ws.WeaponInfo.weaponName}' seeker did not resolve to Fox 1/2/3 (Uncapped) - this launcher gets NO quota enforcement"); return false; }
            _gateAccepts++;
            if (Plugin.SamDbg) GothLog.Once("gate.accept." + GothLog.Name(attachedUnit) + "." + ws.WeaponInfo.weaponName, $"ROLE GATE ACCEPT: '{GothLog.Name(attachedUnit)}' station '{ws.WeaponInfo.weaponName}' cat={cat} ammo={ws.Ammo} antiAir={eff.antiAir:F2} antiMissile={eff.antiMissile:F2}"); 
            return true;
        }
        public static bool IsSurfaceOrNaval(Unit unit) => unit != null && (unit is Ship || unit is GroundVehicle || unit is Building);
        internal static readonly AccessTools.FieldRef<Turret, float> TurretMinElevationRef = AccessTools.FieldRefAccess<Turret, float>("minElevation");
        public static bool IsFixedMountVls(Turret t) => t != null && TurretMinElevationRef(t) == NavalBombardment.TurretMaxElevRef(t);
        public static float GetBuddyRadius(Unit u) => u is Ship ? (Plugin.SAM_BuddyDefenseRadius_Ship != null ? Plugin.SAM_BuddyDefenseRadius_Ship.Value : 10000f) : (Plugin.SAM_BuddyDefenseRadius_VehicleOrBuilding != null ? Plugin.SAM_BuddyDefenseRadius_VehicleOrBuilding.Value : 50000f);
        public static bool IsPlayerUnit(Unit unit) => unit != null && SceneSingleton<CombatHUD>.i != null && SceneSingleton<CombatHUD>.i.aircraft == unit;
    }
    [HarmonyPatch(typeof(MissileLauncher), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    public static class MissileLauncher_Fire_SAM_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(MissileLauncher __instance, Unit owner, Unit target, WeaponStation weaponStation)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value || __instance == null || __instance.ammo <= 0) return;
            if (SeekerSlotCoordinator.GetFoxCategory(__instance) == FoxCategory.Fox1_SARH) return; 
            Unit unit = owner ?? __instance.attachedUnit; if (unit == null) return;
            float buddyRadius = RippleSam.GetBuddyRadius(unit);
            if (!RippleSam.IsUnitOrBuddyThreatened(unit, buddyRadius) || RippleSam.CannotEngage(unit, target)) return;
            Turret turret = weaponStation?.GetTurret();
            if (turret != null && RippleSam.IsFixedMountVls(turret)) return; 
            float fastInterval = Plugin.SAM_MissileSalvoInterval != null ? Plugin.SAM_MissileSalvoInterval.Value : 0.25f, lastFired = RippleSam.WeaponLastFiredRef(__instance), elapsed = Time.timeSinceLevelLoad - lastFired, origInterval = RippleSam.MissileLauncherFireIntervalRef(__instance);
            if (elapsed < fastInterval || elapsed >= origInterval) return;
            RippleSam.WeaponLastFiredRef(__instance) = Time.timeSinceLevelLoad - origInterval - 0.001f;
            if (weaponStation != null && Time.timeSinceLevelLoad - weaponStation.LastFiredTime >= fastInterval) weaponStation.LastFiredTime = Time.timeSinceLevelLoad - weaponStation.WeaponInfo.fireInterval - 0.001f;
            if (Plugin.SamDbg) GothLog.Throttled("unlock." + unit.persistentID, $"RIPPLE UNLOCK '{GothLog.Name(unit)}': reload clock back-dated (elapsed={elapsed:F2}s, weapon fireInterval={origInterval:F2}s, ripple={fastInterval:F2}s).");
        }
    }
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class Turret_FixedUpdate_SAM_Patch
    {
        private struct SavedOriginals { public float lockTime, targetAssessmentInterval, simulationInterval; public bool hadSolver; public List<(int idx, float check, float alert)> detectors; public bool hadWeaponInfo; public float weaponInfoFireInterval; }
        private static readonly Dictionary<int, SavedOriginals> _saved = new Dictionary<int, SavedOriginals>(16);
        private static readonly Dictionary<int, bool> _wasThreatened = new Dictionary<int, bool>(16);
        private static readonly Dictionary<int, int> _preAmmo = new Dictionary<int, int>(16);
        private static readonly Dictionary<int, Unit> _preTarget = new Dictionary<int, Unit>(16);
        internal static readonly Dictionary<int, (PersistentID target, float until)> _recentlyLostTarget = new Dictionary<int, (PersistentID, float)>(16);
        internal const float RecentlyLostTargetCooldown = 2f;
        internal static void ClearOnLevelReset() { _saved.Clear(); _wasThreatened.Clear(); _preAmmo.Clear(); _preTarget.Clear(); _recentlyLostTarget.Clear(); }
        internal static readonly AccessTools.FieldRef<Turret, List<TargetDetector>> TurretTargetDetectorsRef = AccessTools.FieldRefAccess<Turret, List<TargetDetector>>("targetDetectors");
        internal static readonly AccessTools.FieldRef<TargetDetector, float> DetectorCheckIntervalRef = AccessTools.FieldRefAccess<TargetDetector, float>("checkInterval");
        internal static readonly AccessTools.FieldRef<TargetDetector, float> DetectorAlertCheckIntervalRef = AccessTools.FieldRefAccess<TargetDetector, float>("alertCheckInterval");
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return true;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat)) return true; 
            if (cat == FoxCategory.Fox1_SARH) return true; 
            int tk = __instance.GetInstanceID();
            Unit attachedUnit = __instance.GetAttachedUnit(); float buddyRadius = RippleSam.GetBuddyRadius(attachedUnit);
            bool isTh = RippleSam.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius);
            bool wasTh = _wasThreatened.TryGetValue(tk, out bool prevTh) && prevTh;
            _wasThreatened[tk] = isTh;
            if (!isTh)
            {
                if (wasTh && _saved.TryGetValue(tk, out var orig)) RevertSpeedUp(__instance, ws, orig, tk);
                if (Plugin.SamDbg && Plugin.SamTrace) GothLog.Trace($"PEACETIME passthrough: {GothLog.Name(attachedUnit)} not threatened, 100% vanilla.");
                return true;
            }
            ApplySpeedUp(__instance, ws, tk, wasTh);
            _preAmmo[tk] = ws.Ammo; Unit currentTarget = __instance.GetTarget(); _preTarget[tk] = currentTarget;
            if (currentTarget == null || currentTarget.disabled)
            {
                if (!RippleSam.IsSalvoQuotaSaturated(attachedUnit, buddyRadius) && RippleSam.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit nextThreat))
                {
                    __instance.SetTarget(nextThreat.persistentID, ws.Number); __instance.enabled = true;
                    if (ws.Weapons != null) for (int i = 0; i < ws.Weapons.Count; i++) ws.Weapons[i].SetTarget(nextThreat);
                    _preTarget[tk] = nextThreat;
                    if (Plugin.SamDbg) GothLog.Throttled("fu.acquire." + tk, $"TURRET ACQUIRE: '{GothLog.Name(attachedUnit)}' had no target -> '{GothLog.Name(nextThreat)}' ({cat}).");
                }
                return true;
            }
            if (currentTarget is Missile m)
            {
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(m.persistentID))
                {
                    _recentlyLostTarget[tk] = (m.persistentID, Time.timeSinceLevelLoad + RecentlyLostTargetCooldown);
                    if (Plugin.SamDbg) GothLog.Throttled("fu.engaged." + tk, $"TURRET SKIP: '{GothLog.Name(attachedUnit)}' target '{GothLog.Name(m)}' already engaged -> forcing target change.");
                    RippleSam.ForceChangeTarget(__instance, ws, cat); return false; 
                }
                if (!RippleSam.IsActualThreat(m, attachedUnit, buddyRadius))
                {
                    if (Plugin.SamDbg) GothLog.Throttled("fu.notathreat." + tk, $"TURRET SKIP: '{GothLog.Name(attachedUnit)}' target '{GothLog.Name(m)}' no longer a threat -> forcing target change.");
                    RippleSam.ForceChangeTarget(__instance, ws, cat); return false; 
                }
                return true; 
            }
            if (!RippleSam.IsSalvoQuotaSaturated(attachedUnit, buddyRadius) && RippleSam.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit emergencyThreat))
            {
                __instance.SetTarget(emergencyThreat.persistentID, ws.Number); __instance.enabled = true;
                if (ws.Weapons != null) for (int i = 0; i < ws.Weapons.Count; i++) ws.Weapons[i].SetTarget(emergencyThreat);
                _preTarget[tk] = emergencyThreat;
                if (Plugin.SamDbg) GothLog.Throttled("fu.emergency." + tk, $"TURRET PRIORITISE: '{GothLog.Name(attachedUnit)}' dropped '{GothLog.Name(currentTarget)}' for incoming threat '{GothLog.Name(emergencyThreat)}'.");
                return true;
            }
            if (SeekerSlotCoordinator.IsSeekerSlotFilled(currentTarget.persistentID, cat))
            {
                if (Plugin.SamDbg) GothLog.Throttled("fu.slotfilled." + tk, $"TURRET SKIP: '{GothLog.Name(attachedUnit)}' target '{GothLog.Name(currentTarget)}' already has a {cat} inbound -> forcing target change.");
                RippleSam.ForceChangeTarget(__instance, ws, cat); return false;
            }
            return true;
        }
        private static void ApplySpeedUp(Turret t, WeaponStation ws, int tk, bool wasTh)
        {
            float fast = Plugin.SAM_Ripple_LockTime != null ? Plugin.SAM_Ripple_LockTime.Value : 0.1f;
            bool isVls = RippleSam.IsFixedMountVls(t);
            if (!wasTh)
            {
                AimSolver solver = NavalBombardment.TurretAimSolverRef(t);
                List<TargetDetector> dets = TurretTargetDetectorsRef(t);
                var detSnap = new List<(int, float, float)>();
                if (!isVls && dets != null) for (int i = 0; i < dets.Count; i++) { var d = dets[i]; if (d == null || d is Radar) continue; detSnap.Add((i, DetectorCheckIntervalRef(d), DetectorAlertCheckIntervalRef(d))); }
                bool hadWi = ws.WeaponInfo != null;
                _saved[tk] = new SavedOriginals { lockTime = RippleSam.TurretLockTimeRef(t), targetAssessmentInterval = NavalBombardment.TurretTargetAssessmentIntervalRef(t), simulationInterval = solver != null ? NavalBombardment.AimSolverSimulationIntervalRef(solver) : 0f, hadSolver = solver != null, detectors = detSnap, hadWeaponInfo = hadWi, weaponInfoFireInterval = hadWi ? ws.WeaponInfo.fireInterval : 0f };
                if (Plugin.SamDbg) GothLog.Once("speedup.enter." + tk, $"SAM LOCK SPEED ENTER: '{GothLog.Name(t.GetAttachedUnit())}' vls={isVls} minElev={RippleSam.TurretMinElevationRef(t):F1} maxElev={NavalBombardment.TurretMaxElevRef(t):F1} fast={fast:F2} origLockTime={_saved[tk].lockTime:F2} origAssessInterval={_saved[tk].targetAssessmentInterval:F2} origFireInterval={_saved[tk].weaponInfoFireInterval:F2}");
            }
            if (!isVls)
            {
                RippleSam.TurretLockTimeRef(t) = fast;
                NavalBombardment.TurretTargetAssessmentIntervalRef(t) = fast;
                AimSolver aimSolver = NavalBombardment.TurretAimSolverRef(t);
                if (aimSolver != null) NavalBombardment.AimSolverSimulationIntervalRef(aimSolver) = fast;
                List<TargetDetector> detectors = TurretTargetDetectorsRef(t);
                if (detectors != null) for (int i = 0; i < detectors.Count; i++) { var d = detectors[i]; if (d == null || d is Radar) continue; DetectorCheckIntervalRef(d) = fast; DetectorAlertCheckIntervalRef(d) = fast; }
            }
            if (ws.WeaponInfo != null) {
                ws.WeaponInfo.fireInterval = fast;
                if (Plugin.SamDbg) GothLog.Throttled("speedup.fireinterval." + tk, $"SAM LOCK SPEED: '{GothLog.Name(t.GetAttachedUnit())}' vls={isVls} WeaponInfo.fireInterval->{fast:F2}");
            }
        }
        private static void RevertSpeedUp(Turret t, WeaponStation ws, SavedOriginals orig, int tk)
        {
            bool isVls = RippleSam.IsFixedMountVls(t);
            if (!isVls)
            {
                RippleSam.TurretLockTimeRef(t) = orig.lockTime;
                NavalBombardment.TurretTargetAssessmentIntervalRef(t) = orig.targetAssessmentInterval;
                if (orig.hadSolver) { AimSolver solver = NavalBombardment.TurretAimSolverRef(t); if (solver != null) NavalBombardment.AimSolverSimulationIntervalRef(solver) = orig.simulationInterval; }
                List<TargetDetector> dets = TurretTargetDetectorsRef(t);
                if (dets != null && orig.detectors != null) foreach (var (idx, check, alert) in orig.detectors) if (idx < dets.Count && dets[idx] != null) { DetectorCheckIntervalRef(dets[idx]) = check; DetectorAlertCheckIntervalRef(dets[idx]) = alert; }
            }
            if (orig.hadWeaponInfo && ws != null && ws.WeaponInfo != null) ws.WeaponInfo.fireInterval = orig.weaponInfoFireInterval;
            if (Plugin.SamDbg) GothLog.Throttled("speedup.exit." + tk, $"SAM LOCK SPEED EXIT: '{GothLog.Name(t.GetAttachedUnit())}' reverted lockTime->{orig.lockTime:F2} assessInterval->{orig.targetAssessmentInterval:F2} fireInterval reverted={orig.hadWeaponInfo}");
            _saved.Remove(tk);
        }
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value || !RippleSam.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat)) return;
            if (cat == FoxCategory.Fox1_SARH) return; 
            if (!RippleSam.IsUnitOrBuddyThreatened(__instance.GetAttachedUnit(), RippleSam.GetBuddyRadius(__instance.GetAttachedUnit()))) return; 
            Unit attachedUnit = __instance.GetAttachedUnit();
            int tk = __instance.GetInstanceID();
            if (!_preAmmo.TryGetValue(tk, out int preAmmo) || ws.Ammo >= preAmmo) return; 
            _preTarget.TryGetValue(tk, out Unit preTarget);
            RippleSam.RecordThreatEngaged(preTarget);
            if (Plugin.SamDbg) GothLog.Line($"TURRET FIRED '{GothLog.Name(attachedUnit)}' ({cat}) at '{GothLog.Name(preTarget)}'; ammo {preAmmo} -> {ws.Ammo}. Moving off target now.");
            RippleSam.ForceChangeTarget(__instance, ws, cat);
        }
    }
    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    public static class Turret_AssessTargetPriority_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Turret __instance, Unit targetCandidate, ref float priorityThreshold)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value || __instance == null || targetCandidate == null || targetCandidate.disabled) return true;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation ws, out FoxCategory cat)) return true;
            if (cat == FoxCategory.Fox1_SARH) return true; 
            if (!(targetCandidate is Aircraft || targetCandidate is Missile)) return false; 
            if (ws.WeaponInfo != null)
            {
                if (ws.WeaponInfo.targetRequirements.minIR > 0f && !targetCandidate.HasIRSignature()) return false;
                if (ws.WeaponInfo.targetRequirements.minRadar > 0f && !targetCandidate.HasRadarEmission()) return false;
            }
            if (targetCandidate is Missile missileCandidate)
            {
                if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(missileCandidate.persistentID)) return false;
            }
            else if (SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(targetCandidate.persistentID)) return false; 
            if (!RippleSam.IsUnitOrBuddyThreatened(__instance.GetAttachedUnit(), RippleSam.GetBuddyRadius(__instance.GetAttachedUnit()))) return true; 
            if (targetCandidate is Missile m)
            {
                Unit attachedUnit = __instance.GetAttachedUnit(); float buddyRadius = RippleSam.GetBuddyRadius(attachedUnit);
                if (!RippleSam.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) || !RippleSam.IsActualThreat(m, attachedUnit, buddyRadius)) return false;
                int tk = __instance.GetInstanceID();
                if (Turret_FixedUpdate_SAM_Patch._recentlyLostTarget.TryGetValue(tk, out var lost) && lost.target == m.persistentID && Time.timeSinceLevelLoad < lost.until) return false; 
                priorityThreshold = 10000f;
                __instance.SetTarget(targetCandidate.persistentID, ws.Number);
                RippleSam.TurretCurrentWeaponStationRef(__instance) = ws;
                if (Plugin.SamDbg) GothLog.Throttled("atp.promote." + __instance.GetInstanceID(), $"PRIORITISE: '{GothLog.Name(attachedUnit)}' promoted unengaged threat '{GothLog.Name(m)}' ({cat}).");
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Turret), "ChooseTarget")]
    public static class Turret_ChooseTarget_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Turret __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value || __instance == null || __instance.GetAttachedUnit() == null || __instance.GetAttachedUnit().disabled) return;
            if (!RippleSam.IsAirDefenseTurret(__instance, out WeaponStation _gws, out FoxCategory _gcat)) return; 
            if (_gcat == FoxCategory.Fox1_SARH) return; 
            Unit attachedUnit = __instance.GetAttachedUnit(); float buddyRadius = RippleSam.GetBuddyRadius(attachedUnit);
            if (!RippleSam.IsUnitOrBuddyThreatened(attachedUnit, buddyRadius) || RippleSam.IsSalvoQuotaSaturated(attachedUnit, buddyRadius)) return; 
            WeaponStation ws = RippleSam.TurretCurrentWeaponStationRef(__instance) ?? __instance.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.missile) return;
            FoxCategory cat = SeekerSlotCoordinator.GetFoxCategory(ws); Unit currentTarget = __instance.GetTarget();
            if ((currentTarget == null || currentTarget.disabled || SeekerSlotCoordinator.IsTargetEngagedByAnyMissile(currentTarget.persistentID)) && RippleSam.TryFindUnengagedThreat(attachedUnit, ws, cat, out Unit fallbackThreat)) { __instance.SetTarget(fallbackThreat.persistentID, ws.Number); __instance.enabled = true; }
        }
    }
}