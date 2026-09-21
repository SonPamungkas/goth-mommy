using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace GroundOverTheHorizon
{
    public enum FoxCategory { Uncapped, Fox1_SARH, Fox2_IR, Fox3_ARH }
    public class TargetSeekerSlots
    {
        public PersistentID Fox1Missile = PersistentID.None, Fox2Missile = PersistentID.None, Fox3Missile = PersistentID.None;
        private const float AIRCRAFT_SURVIVOR_COOLDOWN = 10f;
        public float Fox1CooldownUntil, Fox2CooldownUntil, Fox3CooldownUntil;
        private bool IsCooldownActive(FoxCategory cat)
        {
            float until = cat == FoxCategory.Fox1_SARH ? Fox1CooldownUntil : cat == FoxCategory.Fox2_IR ? Fox2CooldownUntil : cat == FoxCategory.Fox3_ARH ? Fox3CooldownUntil : 0f;
            return Time.timeSinceLevelLoad < until;
        }
        public void SetAircraftCooldown(FoxCategory cat)
        {
            float until = Time.timeSinceLevelLoad + AIRCRAFT_SURVIVOR_COOLDOWN;
            if (cat == FoxCategory.Fox1_SARH) Fox1CooldownUntil = until; else if (cat == FoxCategory.Fox2_IR) Fox2CooldownUntil = until; else if (cat == FoxCategory.Fox3_ARH) Fox3CooldownUntil = until;
        }
        public void ApplyReleaseCooldownIfAircraftSurvived(FoxCategory cat, PersistentID targetId)
        {
            if (UnitRegistry.TryGetUnit(targetId, out Unit target) && target != null && !target.disabled && target is Aircraft) SetAircraftCooldown(cat);
        }
        public bool IsSlotFilled(FoxCategory cat, PersistentID targetId)
        {
            if (cat == FoxCategory.Uncapped) return false;
            if (IsCooldownActive(cat)) return true;
            PersistentID missileId = cat == FoxCategory.Fox1_SARH ? Fox1Missile : cat == FoxCategory.Fox2_IR ? Fox2Missile : cat == FoxCategory.Fox3_ARH ? Fox3Missile : PersistentID.None;
            if (!missileId.IsValid) return false;
            bool found = UnitRegistry.TryGetUnit(missileId, out var unit);
            if (found && !unit.disabled && unit is Missile m && m.targetID == targetId) return true;
            if (Plugin.SamDbg) GothLog.Throttled("release." + cat + "." + targetId, $"SLOT RELEASED ({cat}): {(!found ? "bound missile not in UnitRegistry (despawned)" : (unit == null || unit.disabled) ? "bound missile disabled" : !(unit is Missile) ? "bound id is not a Missile" : "bound missile ALIVE but lost lock (targetID no longer matches)")}. Quota reopens; a replacement can now be fired.");
            ApplyReleaseCooldownIfAircraftSurvived(cat, targetId);
            ClearSlot(cat); return false;
        }
        public void SetSlot(FoxCategory cat, PersistentID missileId) { if (cat == FoxCategory.Fox1_SARH) Fox1Missile = missileId; else if (cat == FoxCategory.Fox2_IR) Fox2Missile = missileId; else if (cat == FoxCategory.Fox3_ARH) Fox3Missile = missileId; }
        public void ClearSlot(FoxCategory cat) { if (cat == FoxCategory.Fox1_SARH) Fox1Missile = PersistentID.None; else if (cat == FoxCategory.Fox2_IR) Fox2Missile = PersistentID.None; else if (cat == FoxCategory.Fox3_ARH) Fox3Missile = PersistentID.None; }
        public bool ClearMissileIfMatches(PersistentID missileId)
        {
            bool cleared = false;
            if (Fox1Missile == missileId) { Fox1Missile = PersistentID.None; cleared = true; }
            if (Fox2Missile == missileId) { Fox2Missile = PersistentID.None; cleared = true; }
            if (Fox3Missile == missileId) { Fox3Missile = PersistentID.None; cleared = true; }
            return cleared;
        }
    }
    public static class SeekerSlotCoordinator
    {
        private static readonly Dictionary<PersistentID, TargetSeekerSlots> _targetSlots = new Dictionary<PersistentID, TargetSeekerSlots>();
        private static readonly Dictionary<WeaponInfo, FoxCategory> _foxCategoryCache = new Dictionary<WeaponInfo, FoxCategory>();
        private static readonly Dictionary<PersistentID, PersistentID> _missileToTarget = new Dictionary<PersistentID, PersistentID>(), _missileToOwner = new Dictionary<PersistentID, PersistentID>();
        private static readonly List<PersistentID> _pruneKeys = new List<PersistentID>();
        private static float _lastPruneTime;
        private static float _lastSeenTime;
        private static void CheckLevelReset() { float now = Time.timeSinceLevelLoad; if (now < _lastSeenTime - 1f) { Reset(); Plugin.Log?.LogInfo(GothLog.Tag + " Level reload detected (SeekerSlotCoordinator). Seeker-slot/missile-tracking caches cleared."); } _lastSeenTime = now; }
        public static void Reset() { _targetSlots.Clear(); _missileToTarget.Clear(); _missileToOwner.Clear(); _lastPruneTime = 0f; }
        public static bool IsSeekerSlotFilled(PersistentID targetId, FoxCategory cat) { CheckLevelReset(); PruneStaleEntries(); return cat != FoxCategory.Uncapped && targetId.IsValid && _targetSlots.TryGetValue(targetId, out var slots) && slots.IsSlotFilled(cat, targetId); }
        public static void MarkSeekerSlotFilled(PersistentID targetId, FoxCategory cat, PersistentID missileId) { if (cat == FoxCategory.Uncapped || !targetId.IsValid || !missileId.IsValid) return; if (!_targetSlots.TryGetValue(targetId, out var slots)) _targetSlots[targetId] = slots = new TargetSeekerSlots(); slots.SetSlot(cat, missileId); }
        public static bool IsTargetEngagedByAnyMissile(PersistentID targetId) => targetId.IsValid && _targetSlots.TryGetValue(targetId, out var slots) && (slots.IsSlotFilled(FoxCategory.Fox1_SARH, targetId) || slots.IsSlotFilled(FoxCategory.Fox2_IR, targetId) || slots.IsSlotFilled(FoxCategory.Fox3_ARH, targetId));
        public static void OnMissileSpawned(Missile missile, Unit target, Unit owner)
        {
            CheckLevelReset();
            PruneStaleEntries();
            if (missile == null || target == null || owner == null) { if (Plugin.SamDbg) GothLog.Line($"SPAWN IGNORED: missile={(missile == null ? "null" : missile.name)} target={GothLog.Name(target)} owner={GothLog.Name(owner)}"); return; }
            FoxCategory cat = GetFoxCategory(missile);
            if (cat == FoxCategory.Uncapped)
            {
                if (Plugin.SamDbg)
                {
                    string colour = GothLog.Classify(missile, target);
                    GothLog.Line($"SPAWN {colour}: '{missile.name}' from '{GothLog.Name(owner)}' -> '{GothLog.Name(target)}' | cat=Uncapped, NO SLOT BOUND. This launcher gets no quota enforcement.");
                    GothLog.Once("uncapped." + missile.name, $"SEEKER UNRESOLVED: '{missile.name}' never resolves to Fox 1/2/3. Every shot from it is uncapped.");
                }
                return;
            }
            MarkSeekerSlotFilled(target.persistentID, cat, missile.persistentID);
            _missileToTarget[missile.persistentID] = target.persistentID; _missileToOwner[missile.persistentID] = owner.persistentID;
            if (Plugin.SamDbg) GothLog.Line($"SPAWN {GothLog.Classify(missile, target)}: '{missile.name}' from '{GothLog.Name(owner)}' -> '{GothLog.Name(target)}' | cat={cat}| slot bound | {DescribeSlots()}");
        }
        public static string DescribeSlots()
        {
            int f1 = 0, f2 = 0, f3 = 0, targets = 0;
            foreach (var kv in _targetSlots)
            {
                TargetSeekerSlots s = kv.Value; if (s == null) continue; bool any = false;
                if (s.IsSlotFilled(FoxCategory.Fox1_SARH, kv.Key)) { f1++; any = true; }
                if (s.IsSlotFilled(FoxCategory.Fox2_IR, kv.Key)) { f2++; any = true; }
                if (s.IsSlotFilled(FoxCategory.Fox3_ARH, kv.Key)) { f3++; any = true; }
                if (any) targets++;
            }
            return $"slots: {targets} target(s) engaged (Fox1={f1} Fox2={f2} Fox3={f3})";
        }
        public static void OnMissileTerminated(Missile missile)
        {
            if (missile == null) return;
            PersistentID mId = missile.persistentID; bool freed = false;
            FoxCategory cat = GetFoxCategory(missile);
            if (_missileToTarget.TryGetValue(mId, out PersistentID targetId))
            {
                _missileToTarget.Remove(mId);
                if (_targetSlots.TryGetValue(targetId, out var slots) && slots.ClearMissileIfMatches(mId)) { freed = true; slots.ApplyReleaseCooldownIfAircraftSurvived(cat, targetId); }
            }
            _missileToOwner.Remove(mId);
            if (missile.targetID.IsValid && _targetSlots.TryGetValue(missile.targetID, out var tSlots) && tSlots.ClearMissileIfMatches(mId)) { freed = true; tSlots.ApplyReleaseCooldownIfAircraftSurvived(cat, missile.targetID); }
            if (Plugin.SamDbg) GothLog.Line($"END '{missile.name}' -> slot {(freed ? "FREED" : "was not held by it")} | {DescribeSlots()}");
        }
        public static void OnMissileTargetChanged(Missile missile, PersistentID oldTargetId, PersistentID newTargetId)
        {
            if (missile == null) return;
            FoxCategory cat = GetFoxCategory(missile); if (cat == FoxCategory.Uncapped) return;
            bool releasedOld = oldTargetId.IsValid && _targetSlots.TryGetValue(oldTargetId, out var oldSlots) && oldSlots.ClearMissileIfMatches(missile.persistentID);
            if (newTargetId.IsValid) { MarkSeekerSlotFilled(newTargetId, cat, missile.persistentID); _missileToTarget[missile.persistentID] = newTargetId; }
            else _missileToTarget.Remove(missile.persistentID);
            if (releasedOld && oldTargetId.IsValid && Plugin.SamDbg)
            {
                UnitRegistry.TryGetUnit(oldTargetId, out Unit oldT); UnitRegistry.TryGetUnit(newTargetId, out Unit newT);
                GothLog.Line($"RETARGET '{missile.name}' ({cat}): '{GothLog.Name(oldT)}' [{GothLog.Classify(missile, oldT)}] -> '{GothLog.Name(newT)}' [{GothLog.Classify(missile, newT)}]. Old slot released mid-flight; a replacement may now be fired at it.");
            }
        }
        public static void PruneStaleEntries()
        {
            float now = Time.timeSinceLevelLoad; if (now - _lastPruneTime < 5.0f) return; _lastPruneTime = now;
            _pruneKeys.Clear();
            foreach (var kvp in _targetSlots) if (!UnitRegistry.TryGetUnit(kvp.Key, out var u) || u.disabled) _pruneKeys.Add(kvp.Key);
            for (int i = 0; i < _pruneKeys.Count; i++) _targetSlots.Remove(_pruneKeys[i]);
        }
        public static FoxCategory GetFoxCategory(WeaponStation ws) => ws == null ? FoxCategory.Uncapped : (ws.Weapons != null && ws.Weapons.Count > 0 && ws.Weapons[0] is MissileLauncher ml) ? GetFoxCategory(ml) : ws.WeaponInfo != null ? GetFoxCategory(ws.WeaponInfo) : FoxCategory.Uncapped;
        public static FoxCategory GetFoxCategory(MissileLauncher ml)
        {
            if (ml == null) return FoxCategory.Uncapped;
            if (ml.missile != null && ml.missile.unitPrefab != null)
            {
                FoxCategory fromPrefab = FromSeeker(ml.missile.unitPrefab.GetComponent<MissileSeeker>() ?? ml.missile.unitPrefab.GetComponent<Missile>()?.GetComponent<MissileSeeker>());
                if (fromPrefab != FoxCategory.Uncapped) return fromPrefab;
            }
            return ml.info != null ? GetFoxCategory(ml.info) : FoxCategory.Uncapped;
        }
        public static FoxCategory GetFoxCategory(WeaponInfo wi) { if (wi == null) return FoxCategory.Uncapped; if (_foxCategoryCache.TryGetValue(wi, out var cat)) return cat; return _foxCategoryCache[wi] = ResolveFoxCategory(wi); }
        public static FoxCategory GetFoxCategory(Missile missile)
        {
            if (missile == null) return FoxCategory.Uncapped;
            FoxCategory fromMissile = FromSeeker(missile.GetComponent<MissileSeeker>());
            if (fromMissile != FoxCategory.Uncapped) return fromMissile;
            WeaponInfo wi = missile.GetWeaponInfo(); return wi != null ? GetFoxCategory(wi) : FoxCategory.Uncapped;
        }
        private static FoxCategory ResolveFoxCategory(WeaponInfo wi)
        {
            if (!wi.missile || wi.weaponPrefab == null) return FoxCategory.Uncapped;
            return FromSeeker(wi.weaponPrefab.GetComponent<MissileSeeker>() ?? wi.weaponPrefab.GetComponent<Missile>()?.GetComponent<MissileSeeker>());
        }
        private static FoxCategory FromSeeker(MissileSeeker seeker) =>
            seeker is SARHSeeker ? FoxCategory.Fox1_SARH : seeker is IRSeeker ? FoxCategory.Fox2_IR : seeker is ARHSeeker ? FoxCategory.Fox3_ARH : FoxCategory.Uncapped;
    }
    [HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    public static class Spawner_SpawnMissile_SAM_Patch { [HarmonyPostfix] public static void Postfix(Missile __result, Unit target, Unit owner) { if (Plugin.EnableGoth.Value && Plugin.SAM_EnableCoordinator.Value && __result != null && target != null && owner != null) SeekerSlotCoordinator.OnMissileSpawned(__result, target, owner); } }
    [HarmonyPatch(typeof(Missile), "TargetIDChanged")]
    public static class Missile_TargetIDChanged_SAM_Patch { [HarmonyPostfix] public static void Postfix(Missile __instance, PersistentID oldValue, PersistentID newValue) { if (Plugin.EnableGoth.Value && Plugin.SAM_EnableCoordinator.Value) SeekerSlotCoordinator.OnMissileTargetChanged(__instance, oldValue, newValue); } }
    [HarmonyPatch(typeof(Missile), "Detonate", new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    public static class Missile_Detonate_SAM_Patch { [HarmonyPrefix] public static void Prefix(Missile __instance) { if (Plugin.EnableGoth.Value && Plugin.SAM_EnableCoordinator.Value) SeekerSlotCoordinator.OnMissileTerminated(__instance); } }
    [HarmonyPatch(typeof(Missile), "UnitDisabled", new Type[] { typeof(bool), typeof(bool) })]
    public static class Missile_UnitDisabled_SAM_Patch { [HarmonyPostfix] public static void Postfix(Missile __instance, bool oldState, bool newState) { if (Plugin.EnableGoth.Value && Plugin.SAM_EnableCoordinator.Value && newState) SeekerSlotCoordinator.OnMissileTerminated(__instance); } }
}