using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
namespace GroundOverTheHorizon
{
    public static class GothLog
    {
        internal const string Tag = "[GOTH|SAM]";
        private static readonly HashSet<string> _once = new HashSet<string>();
        private static readonly Dictionary<string, float> _lastEmit = new Dictionary<string, float>(64);
        private static float _lastSeenTime;
        public static void Line(string msg) { if (Plugin.SamDbg && Plugin.Log != null) Plugin.Log.LogInfo(Tag + " " + msg); }
        public static void Once(string key, string msg) { if (!Plugin.SamDbg || Plugin.Log == null) return; CheckLevelReset(); if (_once.Add(key)) Plugin.Log.LogInfo(Tag + " " + msg); }
        public static void Throttled(string key, string msg) {
            if (!Plugin.SamDbg || Plugin.Log == null) return; CheckLevelReset();
            float now = Time.timeSinceLevelLoad, interval = Plugin.SAM_TraceThrottle?.Value ?? 1.0f;
            if (_lastEmit.TryGetValue(key, out float last) && now - last < interval) return;
            _lastEmit[key] = now; Plugin.Log.LogInfo(Tag + " " + msg);
        }
        public static void Trace(string msg) { if (Plugin.SamDbg && Plugin.SamTrace && Plugin.Log != null) Plugin.Log.LogInfo(Tag + "[T] " + msg); }
        public static string Classify(Unit striker, Unit target) {
            if (target == null) return "NONE";
            if (target is Aircraft) return (striker is Missile) ? "CYAN" : "WHITE";
            if (striker is Aircraft) return "MAGENTA";
            if (target is Missile) return "CYAN";
            if (target is Ship) return "RED";
            return "YELLOW";
        }
        public static string Name(Unit u) => u?.unitName ?? "null";
        public static void Reset() { _once.Clear(); _lastEmit.Clear(); }
        private static void CheckLevelReset() { float now = Time.timeSinceLevelLoad; if (now < _lastSeenTime - 1f) { Reset(); Plugin.Log?.LogInfo(Tag + " Level reload detected."); } _lastSeenTime = now; }
    }
}