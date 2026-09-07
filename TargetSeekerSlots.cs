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
        public PersistentID Fox2Missile = PersistentID.None;
        public PersistentID Fox3Missile = PersistentID.None;
        public bool IsSlotFilled(FoxCategory cat, PersistentID targetId)
        {
            PersistentID missileId;
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    missileId = Fox1Missile;
                    break;
                case FoxCategory.Fox2_IR:
                    missileId = Fox2Missile;
                    break;
                case FoxCategory.Fox3_ARH:
                    missileId = Fox3Missile;
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
            return false;
        }
        public void SetSlot(FoxCategory cat, PersistentID missileId)
        {
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    Fox1Missile = missileId;
                    break;
                case FoxCategory.Fox2_IR:
                    Fox2Missile = missileId;
                    break;
                case FoxCategory.Fox3_ARH:
                    Fox3Missile = missileId;
                    break;
            }
        }
        public void ClearSlot(FoxCategory cat)
        {
            switch (cat)
            {
                case FoxCategory.Fox1_SARH:
                    Fox1Missile = PersistentID.None;
                    break;
                case FoxCategory.Fox2_IR:
                    Fox2Missile = PersistentID.None;
                    break;
                case FoxCategory.Fox3_ARH:
                    Fox3Missile = PersistentID.None;
                    break;
            }
        }
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
        private static readonly Dictionary<PersistentID, PersistentID> _missileToTarget = new Dictionary<PersistentID, PersistentID>();
        private static readonly Dictionary<PersistentID, PersistentID> _missileToOwner = new Dictionary<PersistentID, PersistentID>();
        private static readonly List<PersistentID> _pruneKeys = new List<PersistentID>();
        private static float _lastPruneTime;
        public static bool IsSeekerSlotFilled(PersistentID targetId, FoxCategory cat)
        {
            if (cat == FoxCategory.Uncapped || !targetId.IsValid) return false;
            if (!_targetSlots.TryGetValue(targetId, out var slots)) return false;
            return slots.IsSlotFilled(cat, targetId);
        }
        public static void MarkSeekerSlotFilled(PersistentID targetId, FoxCategory cat, PersistentID missileId)
        {
            if (cat == FoxCategory.Uncapped || !targetId.IsValid || !missileId.IsValid) return;
            if (!_targetSlots.TryGetValue(targetId, out var slots))
            {
                slots = new TargetSeekerSlots();
                _targetSlots[targetId] = slots;
            }
            slots.SetSlot(cat, missileId);
        }
        public static bool IsTargetEngagedByAnyMissile(PersistentID targetId)
        {
            if (!targetId.IsValid) return false;
            if (!_targetSlots.TryGetValue(targetId, out var slots)) return false;
            return slots.IsSlotFilled(FoxCategory.Fox1_SARH, targetId)
                || slots.IsSlotFilled(FoxCategory.Fox2_IR, targetId)
                || slots.IsSlotFilled(FoxCategory.Fox3_ARH, targetId);
        }
        public static void OnMissileSpawned(Missile missile, Unit target, Unit owner)
        {
            if (missile == null || target == null || owner == null) return;
            FoxCategory cat = GetFoxCategory(missile);
            if (cat == FoxCategory.Uncapped) return;
            MarkSeekerSlotFilled(target.persistentID, cat, missile.persistentID);
            SAMSalvoCoordinator.RecordPlatformFired(owner.persistentID, Time.timeSinceLevelLoad);
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
        public static void PruneStaleEntries()
        {
            float now = Time.timeSinceLevelLoad;
            if (now - _lastPruneTime < 5.0f) return;
            _lastPruneTime = now;
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
        }
        public static FoxCategory GetFoxCategory(WeaponStation ws)
        {
            if (ws == null) return FoxCategory.Uncapped;
            if (ws.Weapons != null && ws.Weapons.Count > 0 && ws.Weapons[0] is MissileLauncher ml)
            {
                return GetFoxCategory(ml);
            }
            if (ws.WeaponInfo != null) return GetFoxCategory(ws.WeaponInfo);
            return FoxCategory.Uncapped;
        }
        public static FoxCategory GetFoxCategory(MissileLauncher ml)
        {
            if (ml == null) return FoxCategory.Uncapped;
            if (ml.missile != null && ml.missile.unitPrefab != null)
            {
                MissileSeeker seeker = ml.missile.unitPrefab.GetComponent<MissileSeeker>();
                if (seeker == null)
                {
                    Missile m = ml.missile.unitPrefab.GetComponent<Missile>();
                    if (m != null) seeker = m.GetComponent<MissileSeeker>();
                }
                if (seeker is SARHSeeker) return FoxCategory.Fox1_SARH;
                if (seeker is IRSeeker) return FoxCategory.Fox2_IR;
                if (seeker is ARHSeeker) return FoxCategory.Fox3_ARH;
            }
            if (ml.info != null) return GetFoxCategory(ml.info);
            return FoxCategory.Uncapped;
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
            MissileSeeker s = missile.GetComponent<MissileSeeker>();
            if (s is SARHSeeker) return FoxCategory.Fox1_SARH;
            if (s is IRSeeker) return FoxCategory.Fox2_IR;
            if (s is ARHSeeker) return FoxCategory.Fox3_ARH;
            WeaponInfo wi = missile.GetWeaponInfo();
            if (wi != null) return GetFoxCategory(wi);
            return FoxCategory.Uncapped;
        }
        private static FoxCategory ResolveFoxCategory(WeaponInfo wi)
        {
            if (!wi.missile || wi.weaponPrefab == null) return FoxCategory.Uncapped;
            MissileSeeker seeker = wi.weaponPrefab.GetComponent<MissileSeeker>();
            if (seeker == null)
            {
                Missile missile = wi.weaponPrefab.GetComponent<Missile>();
                if (missile != null) seeker = missile.GetComponent<MissileSeeker>();
            }
            if (seeker is SARHSeeker) return FoxCategory.Fox1_SARH;
            if (seeker is IRSeeker) return FoxCategory.Fox2_IR;
            if (seeker is ARHSeeker) return FoxCategory.Fox3_ARH;
            return FoxCategory.Uncapped;
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
                SeekerSlotCoordinator.OnMissileSpawned(__result, target, owner);
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
            SeekerSlotCoordinator.OnMissileTargetChanged(__instance, oldValue, newValue);
        }
    }
    [HarmonyPatch(typeof(Missile), "Detonate", new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    public static class Missile_Detonate_SAM_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Missile __instance)
        {
            if (!Plugin.EnableGoth.Value || !Plugin.SAM_EnableCoordinator.Value) return;
            SeekerSlotCoordinator.OnMissileTerminated(__instance);
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
                SeekerSlotCoordinator.OnMissileTerminated(__instance);
            }
        }
    }
}