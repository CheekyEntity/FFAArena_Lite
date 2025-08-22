using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FFAArenaLite.Modules
{
    // Lightweight, reflection-free store keyed by local GameObject instanceID.
    // Each process (server or client) maintains its own view; server is authoritative via ServerRpc hook.
    internal static class FFALastStand
    {
        // Default lives if no config is present.
        public static int DefaultLives = 3;

        // Local-process storage keyed by GameObject.GetInstanceID().
        private static readonly Dictionary<int, int> _lives = new Dictionary<int, int>();
        private static readonly HashSet<int> _eliminated = new HashSet<int>();
        private static readonly HashSet<int> _participants = new HashSet<int>();
        private static bool _ended;
        private static bool _endGameRequested;

        public static void Reset()
        {
            _lives.Clear();
            _eliminated.Clear();
            _participants.Clear();
            _ended = false;
            _endGameRequested = false;
        }

        public static bool IsEnded() => _ended;

        public static int GetLives(GameObject go)
        {
            if (go == null) return 0;
            var id = go.GetInstanceID();
            if (!_lives.TryGetValue(id, out var v))
            {
                v = DefaultLives;
                _lives[id] = v;
            }
            return v;
        }

        public static void EnsureInitialized(GameObject go)
        {
            if (go == null) return;
            var id = go.GetInstanceID();
            if (!_lives.ContainsKey(id)) _lives[id] = DefaultLives;
            _participants.Add(id);
        }

        public static int DecrementLife(GameObject go)
        {
            if (go == null) return 0;
            var id = go.GetInstanceID();
            if (!_lives.TryGetValue(id, out var v)) v = DefaultLives;
            v = Math.Max(0, v - 1);
            _lives[id] = v;
            if (v <= 0) _eliminated.Add(id);
            return v;
        }

        public static bool IsEliminated(GameObject go)
        {
            if (go == null) return true;
            return _eliminated.Contains(go.GetInstanceID());
        }

        public static void MarkEliminated(GameObject go)
        {
            if (go == null) return;
            _eliminated.Add(go.GetInstanceID());
        }

        public static int CountRemaining()
        {
            // Count PlayerMovement which are not eliminated (by local view)
            var players = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            int remaining = 0;
            foreach (var mb in players)
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (t.Name == "PlayerMovement")
                {
                    var go = mb.gameObject;
                    if (!IsEliminated(go)) remaining++;
                }
            }
            return remaining;
        }

        public static GameObject FindWinner()
        {
            GameObject winner = null;
            var players = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in players)
            {
                if (mb == null) continue;
                if (mb.GetType().Name == "PlayerMovement")
                {
                    var go = mb.gameObject;
                    if (!IsEliminated(go))
                    {
                        if (winner == null) winner = go;
                        else return null; // More than one remains
                    }
                }
            }
            return winner;
        }

        public static void MaybeEndMatch()
        {
            if (_ended) return;
            // Require at least two participants before ending logic engages to avoid single-player freeze.
            if (_participants.Count < 2)
            {
                // Fallback: detect actual players in scene
                int playersDetected = 0;
                try
                {
                    var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                    foreach (var mb in all)
                    {
                        if (mb != null && mb.GetType().Name == "PlayerMovement") playersDetected++;
                    }
                }
                catch { }
                if (playersDetected < 2)
                {
                    return;
                }
                try { FFAArenaLite.Plugin.Log?.LogDebug($"FFA: MaybeEndMatch proceeding with scene-detected players={playersDetected} despite participants={_participants.Count}."); } catch { }
            }
            int remaining = CountRemaining();
            if (remaining <= 1)
            {
                _ended = true;
                FreezeAllPlayers();
                // Try to use base game's end screen + stats via PlayerRespawnManager.
                // If this fails (no manager found), fall back to local overlay.
                try { FFAArenaLite.Plugin.Log?.LogInfo($"FFA: End condition met. participants={_participants.Count} remaining={remaining}. Attempting base end game..."); } catch { }
                if (!TriggerBaseEndGame())
                {
                    try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: Base end game trigger failed; showing fallback winner overlay."); } catch { }
                    ShowWinOverlay();
                }
            }
        }

        // Attempt to reassign teams (winner=1, others=0) then invoke PlayerRespawnManager.ServerEndGame(0)
        // Returns true if the base end game was successfully invoked (locally requested). Guarded to run only once per process.
        private static bool TriggerBaseEndGame()
        {
            if (_endGameRequested) return false;
            _endGameRequested = true;
            try
            {
                var winner = FindWinner();
                if (winner == null)
                {
                    try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame aborted — no single winner found."); } catch { }
                    return false;
                }

                // Find PlayerRespawnManager and ensure we're on the server before mutating teams or ending the game
                object prm = null;
                Type prmType = null;
                try
                {
                    prmType = AccessTools.TypeByName("PlayerRespawnManager");
                    if (prmType == null)
                    {
                        try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame failed — PlayerRespawnManager type not found."); } catch { }
                        return false;
                    }
                    var findMethod = AccessTools.Method(typeof(UnityEngine.Object), "FindObjectOfType", new Type[] { typeof(Type) });
                    prm = findMethod?.Invoke(null, new object[] { prmType });
                }
                catch { }
                if (prm == null)
                {
                    try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame failed — PlayerRespawnManager instance not found."); } catch { }
                    return false;
                }

                // Check IsServerInitialized to ensure only host performs team reassignment and triggers end game
                try
                {
                    var isServerProp = AccessTools.Property(prm.GetType(), "IsServerInitialized");
                    var isServer = (bool?)(isServerProp?.GetValue(prm)) ?? false;
                    if (!isServer)
                    {
                        try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame skipped — not server side."); } catch { }
                        return false;
                    }
                }
                catch { try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame failed — IsServerInitialized check errored."); } catch { } return false; }

                // Reassign teams to map FFA win -> base game's team-based victory UI.
                try
                {
                    var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                    foreach (var mb in all)
                    {
                        if (mb == null) continue;
                        if (mb.GetType().Name != "PlayerMovement") continue;
                        var f = AccessTools.Field(mb.GetType(), "playerTeam");
                        if (f != null && f.FieldType == typeof(int))
                        {
                            // Winner must be on team 0 because we call ServerEndGame(0)
                            int team = (mb.gameObject == winner) ? 0 : 1;
                            f.SetValue(mb, team);
                        }
                    }
                    try { FFAArenaLite.Plugin.Log?.LogInfo("FFA: Reassigned teams for base end game (winner=team 0, others=team 1). Calling ServerEndGame(0)"); } catch { }
                }
                catch { }

                // Call ServerEndGame(0) on server
                try
                {
                    var serverEnd = AccessTools.Method(prm.GetType(), "ServerEndGame", new Type[] { typeof(int) });
                    if (serverEnd == null)
                    {
                        try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: TriggerBaseEndGame failed — ServerEndGame(int) not found."); } catch { }
                        return false;
                    }
                    serverEnd.Invoke(prm, new object[] { 0 }); // 0 = losing team, so winner (team 1) gets victory
                    try { FFAArenaLite.Plugin.Log?.LogInfo("FFA: Invoked PlayerRespawnManager.ServerEndGame(0)."); } catch { }
                    return true;
                }
                catch { }
            }
            catch { }
            return false;
        }

        private static void FreezeAllPlayers()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var mb in all)
                {
                    if (mb == null) continue;
                    if (mb.GetType().Name == "PlayerMovement")
                    {
                        TrySetBool(mb, "canMove", false);
                        TrySetBool(mb, "canJump", false);
                        TrySetBool(mb, "canMoveCamera", false);
                        try
                        {
                            var inv = mb.GetComponent("PlayerInventory");
                            if (inv != null)
                            {
                                var f = AccessTools.Field(inv.GetType(), "canSwapItem");
                                f?.SetValue(inv, false);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void TrySetBool(object obj, string fieldName, bool val)
        {
            try
            {
                var f = AccessTools.Field(obj.GetType(), fieldName);
                if (f != null && f.FieldType == typeof(bool)) f.SetValue(obj, val);
            }
            catch { }
        }

        private static void ShowWinOverlay()
        {
            try
            {
                var winner = FindWinner();
                string name = "Nobody";
                if (winner != null)
                {
                    name = GetDisplayName(winner);
                }
                // Reuse existing death message system if available for a simple broadcast.
                try
                {
                    var prmType = AccessTools.TypeByName("PlayerRespawnManager");
                    if (prmType != null)
                    {
                        // Find any instance of PlayerRespawnManager safely using non-generic API
                        object prm = null;
                        try
                        {
                            var method = AccessTools.Method(typeof(UnityEngine.Object), "FindObjectOfType", new Type[] { typeof(Type) });
                            prm = method?.Invoke(null, new object[] { prmType });
                        }
                        catch { }
                        if (prm != null)
                        {
                            var m = AccessTools.Method(prm.GetType(), "summonDeathMessage");
                            m?.Invoke(prm, new object[] { name, "winner", "" });
                            try { FFAArenaLite.Plugin.Log?.LogInfo($"FFA: Fallback winner message shown via summonDeathMessage: {name}"); } catch { }
                        }
                        else { try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: ShowWinOverlay — PlayerRespawnManager instance not found."); } catch { } }
                    }
                    else { try { FFAArenaLite.Plugin.Log?.LogWarning("FFA: ShowWinOverlay — PlayerRespawnManager type not found."); } catch { } }
                }
                catch { }
                // TODO: Optional: spawn a simple Canvas overlay for FFA victory, if desired.
            }
            catch { }
        }

        private static string GetDisplayName(GameObject go)
        {
            if (go == null) return string.Empty;
            try
            {
                var pm = go.GetComponent("PlayerMovement");
                if (pm != null)
                {
                    var nameField = AccessTools.Field(pm.GetType(), "playername");
                    var name = nameField?.GetValue(pm) as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }
            return string.Empty;
        }

        // Build standings as tuples: (name, lives, eliminated)
        private static List<(string name, int lives, bool eliminated)> BuildStandings()
        {
            var list = new List<(string name, int lives, bool eliminated)>();
            try
            {
                var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var mb in all)
                {
                    if (mb == null) continue;
                    if (mb.GetType().Name != "PlayerMovement") continue;
                    var go = mb.gameObject;
                    string name = GetDisplayName(go);
                    int lives = GetLives(go);
                    bool elim = IsEliminated(go);
                    list.Add((name, lives, elim));
                }
                // Sort: not eliminated first, then by lives descending, then name
                list.Sort((a, b) =>
                {
                    int c = a.eliminated.CompareTo(b.eliminated); // false<true => winners first
                    if (c != 0) return c;
                    c = b.lives.CompareTo(a.lives);
                    if (c != 0) return c;
                    return string.Compare(a.name, b.name, StringComparison.Ordinal);
                });
            }
            catch { }
            return list;
        }
    }
}
