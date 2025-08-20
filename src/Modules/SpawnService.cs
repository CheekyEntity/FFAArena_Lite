using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FFAArenaLite.Modules
{
    public class SpawnService
    {
        private readonly List<Transform> _spawns = new();
        private static GameObject _runtimeRoot;
        private static bool PresetsOnly = true; // Use only provided coordinates for FFA

        public IReadOnlyList<Transform> GetSpawns() => _spawns;

        // Collect FFA-specific spawns based on map size
        // mapinfo: 1 = Medium, 0 = Large
        public void CollectFFASpawnsByMapInfo(int mapinfo)
        {
            _spawns.Clear();
            // 0) Always seed with preset coordinates to guarantee availability
            SeedPresetSpawns(mapinfo);

            // Presets-only: do not add scene objects; ensure minimum count and return
            if (PresetsOnly)
            {
                if (_spawns.Count > 0)
                {
                    while (_spawns.Count < 8)
                        _spawns.Add(_spawns[_spawns.Count - 1]);
                }
                return;
            }

            bool wantMedium = mapinfo == 1;
            // Accept multiple naming schemes just in case
            string[] nameHints = wantMedium
                ? new[] { "FFA_Medium_Spawn", "FFA_Med_Spawn", "FFA_Medium", "FFAMed" }
                : new[] { "FFA_Large_Spawn", "FFA_Lrg_Spawn", "FFA_Large", "FFALarge" };

            // 1) Try tag-based lookup if tags are defined (robust to missing tags)
            foreach (var tag in nameHints)
            {
                try
                {
                    var tagged = GameObject.FindGameObjectsWithTag(tag);
                    if (tagged != null)
                    {
                        foreach (var go in tagged)
                            if (go != null && go.transform != null)
                                _spawns.Add(go.transform);
                    }
                }
                catch (UnityException) { /* tag may not exist */ }
            }

            // 2) Look for explicit parent groups
            string parentName = wantMedium ? "FFA_Medium_Spawns" : "FFA_Large_Spawns";
#pragma warning disable CS0618
            var allTransforms = Object.FindObjectsOfType<Transform>(true);
#pragma warning restore CS0618
            Transform group = null;
            foreach (var tr in allTransforms)
            {
                if (tr != null && string.Equals(tr.name, parentName, System.StringComparison.OrdinalIgnoreCase))
                {
                    group = tr;
                    break;
                }
            }
            if (group != null)
            {
                foreach (Transform child in group)
                {
                    if (child != null)
                        _spawns.Add(child);
                }
            }

            // 3) Additionally scan all transforms for name hints (adds to list)
            if (_spawns.Count == 0)
            {
                foreach (var tr in allTransforms)
                {
                    if (tr == null) continue;
                    var n = tr.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    var lower = n.ToLowerInvariant();
                    foreach (var hint in nameHints)
                    {
                        if (lower.Contains(hint.ToLowerInvariant()))
                        {
                            _spawns.Add(tr);
                            break;
                        }
                    }
                }
            }

            // 4) Ensure a sensible minimum
            if (_spawns.Count > 0)
            {
                while (_spawns.Count < 8) // prefer at least 8 slots for FFA
                    _spawns.Add(_spawns[_spawns.Count - 1]);
            }
        }

        private static void EnsureRuntimeRoot()
        {
            if (_runtimeRoot != null && _runtimeRoot != null)
                return;
            _runtimeRoot = new GameObject("FFA_Runtime_SpawnContainer");
            _runtimeRoot.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_runtimeRoot);
            // Invalidate FFA detection cache so patches can activate
            FFAMode.InvalidateDetection();
        }

        // Public entry to pre-create the runtime container so FFAMode.IsActive() can detect FFA state earlier
        public static void EnsureRuntimeContainer()
        {
            EnsureRuntimeRoot();
        }

        // Public cleanup to destroy the runtime container and reset detection
        public static void DestroyRuntimeContainer()
        {
            try
            {
                if (_runtimeRoot == null)
                {
                    // Best-effort: find by name in case handle was lost
                    var found = GameObject.Find("FFA_Runtime_SpawnContainer");
                    if (found != null) _runtimeRoot = found;
                }
                if (_runtimeRoot != null)
                {
                    // Destroy all children then the container itself
                    try
                    {
                        var trs = _runtimeRoot.GetComponentsInChildren<Transform>(true);
                        for (int i = trs.Length - 1; i >= 0; i--)
                        {
                            var t = trs[i];
                            if (t == null) continue;
                            if (t.gameObject == _runtimeRoot) continue;
                            Object.Destroy(t.gameObject);
                        }
                    }
                    catch { }
                    Object.Destroy(_runtimeRoot);
                    _runtimeRoot = null;
                }
            }
            catch { }
            finally
            {
                // Reset detection cache so FFA patches deactivate
                FFAMode.InvalidateDetection();
            }
        }

        private void SeedPresetSpawns(int mapinfo)
        {
            EnsureRuntimeRoot();
            var coords = GetPresetCoords(mapinfo);
            if (coords == null || coords.Length == 0) return;
            var wormholeType = AccessTools.TypeByName("RespawnWormhole");
            foreach (var v in coords)
            {
                var go = new GameObject("FFA_Spawn_Preset");
                go.transform.SetParent(_runtimeRoot.transform, false);
                go.transform.position = v;
                // Attach RespawnWormhole if available so teamSpawns (RespawnWormhole[]) can be populated
                try
                {
                    if (wormholeType != null)
                    {
                        var comp = go.GetComponent(wormholeType) ?? go.AddComponent(wormholeType);
                        // Ensure required field 'spawnpos' exists and points to a child transform at our coordinate
                        var spawnField = AccessTools.Field(wormholeType, "spawnpos");
                        if (spawnField != null)
                        {
                            var child = new GameObject("spawnpos");
                            child.transform.SetParent(go.transform, false);
                            child.transform.position = v;
                            spawnField.SetValue(comp, child.transform);
                        }
                    }
                }
                catch { }
                _spawns.Add(go.transform);
            }
        }

        private static Vector3[] GetPresetCoords(int mapinfo)
        {
            // 1 = Medium, 0 = Large
            if (mapinfo == 0)
            {
                return new Vector3[]
                {
                    new Vector3(154.405f, 53.9165f, 336.938f),
                    new Vector3(-10.8466f, 54.1795f, 337.3333f),
                    new Vector3(74.6446f, 52.7551f, 289.8891f),
                    new Vector3(74.6011f, 52.755f, 208.2289f),
                    new Vector3(1.568f, 52.755f, 170.449f),
                    new Vector3(-78.6697f, 54.0691f, 215.85f),
                    new Vector3(147.9836f, 52.755f, 171.7271f),
                    new Vector3(227.6423f, 55.0681f, 215.5072f),
                    new Vector3(228.5723f, 53.9948f, 44.2243f),
                    new Vector3(145.7533f, 52.755f, 92.5877f),
                    new Vector3(73.8109f, 52.755f, 50.5495f),
                    new Vector3(2.4575f, 52.755f, 89.7126f),
                    new Vector3(-78.8257f, 54.0165f, 44.2668f),
                    new Vector3(-7.5792f, 54.3077f, -74.9209f),
                    new Vector3(76.0913f, 52.755f, -27.8133f),
                    new Vector3(156.3583f, 54.1386f, -73.316f),
                };
            }
            else
            {
                return new Vector3[]
                {
                    new Vector3(74.8029f, 56.0144f, -39.1491f),
                    new Vector3(74.3871f, 52.74f, 52.3592f),
                    new Vector3(3.0559f, 52.74f, 91.6606f),
                    new Vector3(-78.5371f, 54.1309f, 46.0337f),
                    new Vector3(4.1548f, 52.7401f, 171.8462f),
                    new Vector3(-78.5159f, 54.4974f, 217.7527f),
                    new Vector3(74.676f, 52.7401f, 209.497f),
                    new Vector3(73.4692f, 54.7709f, 303.5255f),
                    new Vector3(142.9044f, 52.7401f, 172.1282f),
                    new Vector3(228.8374f, 55.1237f, 216.3495f),
                    new Vector3(144.7876f, 52.74f, 90.2711f),
                    new Vector3(228.4908f, 54.1321f, 43.9164f),
                };
            }
        }

        public void CollectNeutralSpawns()
        {
            _spawns.Clear();
            // 1) Try tag-based lookup if the tag exists
            try
            {
                var tagged = GameObject.FindGameObjectsWithTag("Spawn");
                if (tagged != null)
                {
                    foreach (var go in tagged)
                    {
                        if (go != null && go.transform != null)
                            _spawns.Add(go.transform);
                    }
                }
            }
            catch (UnityException)
            {
                // Tag not defined; fall through to name-based fallback
            }

            // 2) Fallback: scan all transforms (including inactive) and match by common spawn names
            if (_spawns.Count == 0)
            {
                // Using legacy API for broad compatibility; suppress obsolete warning.
                #pragma warning disable CS0618
                var all = Object.FindObjectsOfType<Transform>(true);
                #pragma warning restore CS0618
                foreach (var tr in all)
                {
                    if (tr == null) continue;
                    var n = tr.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    var lower = n.ToLowerInvariant();
                    if (lower.Contains("spawn") || lower.Contains("redspawn") || lower.Contains("bluespawn"))
                    {
                        _spawns.Add(tr);
                    }
                }
            }
            // Fallback: duplicate first valid spawn to ensure minimum length
            if (_spawns.Count > 0)
            {
                while (_spawns.Count < 4)
                    _spawns.Add(_spawns[0]);
            }
        }

        public Transform SelectSpawnFarFrom(Vector3[] playerPositions)
        {
            if (_spawns.Count == 0)
                CollectNeutralSpawns();

            if (_spawns.Count == 0)
                return null;

            float BestScore(Transform t)
            {
                if (playerPositions == null || playerPositions.Length == 0)
                    return 0f;
                float min = float.MaxValue;
                foreach (var p in playerPositions)
                {
                    var d = Vector3.Distance(t.position, p);
                    if (d < min) min = d;
                }
                return min;
            }

            Transform best = _spawns[0];
            float bestScore = BestScore(best);
            for (int i = 1; i < _spawns.Count; i++)
            {
                var tr = _spawns[i];
                var s = BestScore(tr);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = tr;
                }
            }
            return best;
        }

        public Transform[] HydrateTeamSpawnsArray(int length)
        {
            if (_spawns.Count == 0)
                CollectNeutralSpawns();

            if (length < 4) length = 4;
            var arr = new Transform[length];
            for (int i = 0; i < length; i++)
                arr[i] = _spawns.Count > 0 ? _spawns[i % _spawns.Count] : null;
            return arr;
        }
    }
}
