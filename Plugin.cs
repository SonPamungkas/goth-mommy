using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace GroundOverTheHorizon
{
    [BepInPlugin("com.goth.mommy", "GOTH", "1.2")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<float> MOMMY_VisualRange;
        public static ConfigEntry<float> MOMMY_Magnification;
        public static ConfigEntry<float> MOMMY_MaxSpeed;

        private void Awake()
        {
            Log = Logger;
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false, "Enable extreme logging for radar math (will impact performance).");
            MOMMY_VisualRange = Config.Bind("MOMMY", "VisualRange", 50000f, "Visual range applied to all radar and ship units.");
            MOMMY_Magnification = Config.Bind("MOMMY", "Magnification", 8f, "Magnification applied to all radar and ship units.");
            MOMMY_MaxSpeed = Config.Bind("MOMMY", "MaxSpeed", 1f, "Max speed applied to all radar and ship units.");

            Log.LogInfo("Initializing GOTH Radar Logic (Multi Orbital Mapping & Monitoring Yield - MOMMY sub-system initialized)...");
            var harmony = new Harmony("com.goth.mommy");
            
            try
            {
                harmony.PatchAll();
                Log.LogInfo("GOTH 1.2 patched successfully. VerboseLogging is " + (VerboseLogging.Value ? "ENABLED" : "DISABLED") + ".");
            }
            catch (System.Exception e)
            {
                Log.LogError($"GOTH failed to patch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Radar), "CanSeeRadarReturn")]
    public static class Radar_CanSeeRadarReturn_Patch
    {
        // Zero-allocation FieldRef to directly access and modify the RadarParams struct inside the Radar
        private static readonly AccessTools.FieldRef<Radar, RadarParams> RadarParamsRef =
            AccessTools.FieldRefAccess<Radar, RadarParams>("RadarParameters");
            
        // FieldRef for TargetDetector.visualRange
        private static readonly AccessTools.FieldRef<TargetDetector, float> VisualRangeRef =
            AccessTools.FieldRefAccess<TargetDetector, float>("visualRange");
            
        private static FieldInfo _targetUnitField;

        // 1. PREFIX: Runs BEFORE the vanilla math. We inject our multipliers here.
        [HarmonyPrefix]
        public static bool Prefix(Radar __instance, object[] __args)
        {
            // Cheap Reject: Safely extract the target unit
            if (__args == null || __args.Length == 0 || __args[0] == null) return true;
            
            Unit target = __args[0] as Unit;
            if (target == null)
            {
                if (_targetUnitField == null) 
                {
                    _targetUnitField = AccessTools.Field(__args[0].GetType(), "unit");
                }
                
                if (_targetUnitField != null)
                {
                    target = _targetUnitField.GetValue(__args[0]) as Unit;
                }
            }

            if (target == null) return true;

            // Target Stats
            float targetAlt = Mathf.Max(0f, target.transform.position.y);
            float rcs = target.RCS;

            // The Scary Formula Injection: Base 1.0 multiplier. Every 1 meter of altitude adds (0.001 * RCS) to the multiplier.
            float detectionMult = 1.0f + (targetAlt * 0.001f * rcs);
            
            // Track and lock multiplier (1.0 + RCS)
            float lockMult = 1.0f + rcs;

            // Access the original values directly via reference, NO boxing!
            ref RadarParams p = ref RadarParamsRef(__instance);
            
            float origMaxRange = p.maxRange;
            float origMinSignal = p.minSignal;

            // 1. Boost the maximum range using target altitude, RCS, and emitter altitude
            float emitterAlt = Mathf.Max(0f, __instance.transform.position.y);
            p.maxRange = origMaxRange * detectionMult * lockMult * (1.0f + Mathf.Log10(emitterAlt + 1f));
            
            // 2. Further reduce MinSignal based on high-altitude multiplier
            p.minSignal = origMinSignal / lockMult;

            // THE LOGGER: Gated behind verbose config to prevent massive IO lag and string allocations
            if (Plugin.VerboseLogging.Value)
            {
                float distance = Vector3.Distance(__instance.transform.position, target.transform.position);
                float modifiedVanillaSignal = (p.maxRange / Mathf.Max(1f, distance)) * Mathf.Pow(rcs, 0.25f);

                string rName = __instance.gameObject.name;
                string tName = target.gameObject.name;

                Plugin.Log.LogInfo($"[MOMMY] {rName} -> {tName} | Dist: {distance:F0}m | Alt: {targetAlt:F0}m | RCS: {rcs:F4} | Rng: {origMaxRange:F0}->{p.maxRange:F0} | MinSig: {origMinSignal:F4}->{p.minSignal:F4} | Mult: {detectionMult:F2}x | Score: {modifiedVanillaSignal:F4} (> {p.minSignal:F4}?)");
            }

            // Return true allows the vanilla method to run, naturally applying Clutter, Doppler, and Jamming
            return true; 
        }
    }

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new System.Type[0])]
    public static class EncyclopediaAfterLoadPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            Plugin.Log.LogInfo("[MOMMY] Applying permanent visual detection stats to all radar and ship units...");
            
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                // Only look at root prefabs
                if (go.scene.IsValid() || go.transform.parent != null) continue;
                
                // Check if the prefab is a radar-emitting unit or a ship
                bool isRadarOrShip = go.GetComponentInChildren<Radar>(true) != null || go.GetComponent("Ship") != null || go.GetComponentInChildren(AccessTools.TypeByName("Ship"), true) != null;
                
                if (isRadarOrShip)
                {
                    var targetDetectors = go.GetComponentsInChildren<TargetDetector>(true);
                    bool upgraded = false;
                    
                    foreach (var td in targetDetectors)
                    {
                        var type = td.GetType();
                        
                        var vrField = AccessTools.Field(type, "visualRange");
                        if (vrField != null) { vrField.SetValue(td, Plugin.MOMMY_VisualRange.Value); upgraded = true; }
                        
                        var magField = AccessTools.Field(type, "magnification");
                        if (magField != null) { magField.SetValue(td, Plugin.MOMMY_Magnification.Value); upgraded = true; }
                        
                        var msField = AccessTools.Field(type, "maxSpeed");
                        if (msField != null) { msField.SetValue(td, Plugin.MOMMY_MaxSpeed.Value); upgraded = true; }
                    }
                    
                    if (upgraded)
                    {
                        Plugin.Log.LogInfo($"[MOMMY] Successfully upgraded optics for {go.name}.");
                    }
                }
            }
        }
    }
}