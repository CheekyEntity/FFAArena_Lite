using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FFAArenaLite.Modules;

namespace FFAArenaLite.Patches
{
    // Simple log sampler to avoid chatty logs in large lobbies
    internal static class LogSampler
    {
        private static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        public static bool Every(string key, int n)
        {
            if (n <= 1) return true;
            int c;
            if (!_counts.TryGetValue(key, out c)) c = 0;
            c++;
            _counts[key] = c;
            return (c % n) == 0;
        }
    }

    [HarmonyPatch]
    public static class PlayerRespawnManager_AddToDeadList_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerRespawnManager");
            return t == null ? null : AccessTools.Method(t, "AddToDeadList");
        }

    [HarmonyPatch]
    public static class PlayerRespawnManager_IJustDied_Prefix
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerRespawnManager");
            if (t == null) return null;
            // public void IJustDied(PlayerMovement pm)
            foreach (var m in t.GetMethods(AccessTools.all))
            {
                if (m.Name == "IJustDied")
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1) return m;
                }
            }
            return null;
        }

        static bool Prefix(object __instance, object pm)
        {
            try
            {
                if (!FFAMode.IsActive()) return true;
                // Let base game handle death UI/spectate routines on both server and client.
                // Our FFA logic manages lives/elimination elsewhere. Suppressing these
                // coroutines can leave clients in a stuck state (no input/arms), so do not block.
                if (FFALastStand.IsEnded())
                {
                    try { FFAArenaLite.Plugin.Log?.LogDebug("FFA: Match ended at IJustDied; allowing base spectate flow."); } catch { }
                }
                else
                {
                    try
                    {
                        var comp = pm as Component; // PlayerMovement
                        var go = comp != null ? comp.gameObject : null;
                        if (go != null && FFALastStand.IsEliminated(go))
                        {
                            FFAArenaLite.Plugin.Log?.LogDebug("FFA: Player eliminated at IJustDied; allowing base spectate flow.");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA IJustDied prefix error: {e}");
            }
            return true;
        }
    }

        // ServerRpc path
        static void Postfix(object __instance, GameObject DeadGuy, int pteam)
        {
            try
            {
                if (!FFAMode.IsActive()) return;
                if (DeadGuy == null) return;
                // Log death details on server
                try
                {
                    string pname = DeadGuy.name;
                    try
                    {
                        var pm = DeadGuy.GetComponent("PlayerMovement");
                        if (pm != null)
                        {
                            var nameField = AccessTools.Field(pm.GetType(), "playername");
                            var n = nameField?.GetValue(pm) as string;
                            if (!string.IsNullOrEmpty(n)) pname = n;
                        }
                    }
                    catch { }
                    var before = FFALastStand.GetLives(DeadGuy);
                    FFAArenaLite.Plugin.Log?.LogDebug($"Death[Server]: {pname} team={pteam} livesBefore={before}");
                }
                catch { }
                FFALastStand.EnsureInitialized(DeadGuy);
                var livesLeft = FFALastStand.DecrementLife(DeadGuy);
                if (livesLeft <= 0)
                {
                    FFALastStand.MarkEliminated(DeadGuy);
                }
                try
                {
                    string pname = DeadGuy.name;
                    try
                    {
                        var pm = DeadGuy.GetComponent("PlayerMovement");
                        if (pm != null)
                        {
                            var nameField = AccessTools.Field(pm.GetType(), "playername");
                            var n = nameField?.GetValue(pm) as string;
                            if (!string.IsNullOrEmpty(n)) pname = n;
                        }
                    }
                    catch { }
                    FFAArenaLite.Plugin.Log?.LogDebug($"Death[Server]: {pname} team={pteam} livesAfter={livesLeft} eliminated={(livesLeft<=0)}");
                }
                catch { }
                FFALastStand.MaybeEndMatch();
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA LPS Server Postfix error: {e}");
            }
        }
    }

    [HarmonyPatch]
    public static class PlayerRespawnManager_AddToDeadListObs_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerRespawnManager");
            return t == null ? null : AccessTools.Method(t, "AddToDeadListObs");
        }

        // ObserversRpc path (all clients)
        static void Postfix(object __instance, GameObject DeadGuy, int pteam)
        {
            try
            {
                if (!FFAMode.IsActive()) return;
                // If running as host/server, Observers also run locally; skip here to avoid double decrement.
                try
                {
                    var isServerProp = AccessTools.Property(__instance.GetType(), "IsServerInitialized");
                    if (isServerProp != null)
                    {
                        var val = isServerProp.GetValue(__instance, null);
                        if (val is bool b && b) return; // host: skip client mirror
                    }
                    else
                    {
                        var isServerField = AccessTools.Field(__instance.GetType(), "IsServerInitialized");
                        if (isServerField != null)
                        {
                            var val2 = isServerField.GetValue(__instance);
                            if (val2 is bool b2 && b2) return;
                        }
                    }
                }
                catch { }
                if (DeadGuy == null) return;
                // Log death details on observers (clients)
                try
                {
                    string pname = DeadGuy.name;
                    try
                    {
                        var pm = DeadGuy.GetComponent("PlayerMovement");
                        if (pm != null)
                        {
                            var nameField = AccessTools.Field(pm.GetType(), "playername");
                            var n = nameField?.GetValue(pm) as string;
                            if (!string.IsNullOrEmpty(n)) pname = n;
                        }
                    }
                    catch { }
                    var before = FFALastStand.GetLives(DeadGuy);
                    FFAArenaLite.Plugin.Log?.LogDebug($"Death[Obs]: {pname} team={pteam} livesBefore={before}");
                }
                catch { }
                FFALastStand.EnsureInitialized(DeadGuy);
                var livesLeft = FFALastStand.DecrementLife(DeadGuy);
                if (livesLeft <= 0)
                {
                    FFALastStand.MarkEliminated(DeadGuy);
                }
                try
                {
                    string pname = DeadGuy.name;
                    try
                    {
                        var pm = DeadGuy.GetComponent("PlayerMovement");
                        if (pm != null)
                        {
                            var nameField = AccessTools.Field(pm.GetType(), "playername");
                            var n = nameField?.GetValue(pm) as string;
                            if (!string.IsNullOrEmpty(n)) pname = n;
                        }
                    }
                    catch { }
                    FFAArenaLite.Plugin.Log?.LogDebug($"Death[Obs]: {pname} team={pteam} livesAfter={livesLeft} eliminated={(livesLeft<=0)}");
                }
                catch { }
                FFALastStand.MaybeEndMatch();
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA LPS Client Postfix error: {e}");
            }
        }
    }

    [HarmonyPatch]
    public static class PlayerRespawnManager_RespawnRoutine_Prefix
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerRespawnManager");
            if (t == null) return null;
            // private IEnumerator RespawnRoutine()
            foreach (var m in t.GetMethods(AccessTools.all))
            {
                if (m.Name == "RespawnRoutine" && typeof(IEnumerator).IsAssignableFrom(m.ReturnType))
                    return m;
            }
            return null;
        }

        private static IEnumerator SkipEnumerator()
        {
            yield break;
        }

        static bool Prefix(object __instance, ref IEnumerator __result)
        {
            try
            {
                if (!FFAMode.IsActive()) return true;
                var f = AccessTools.Field(__instance.GetType(), "pmv");
                var pmv = f?.GetValue(__instance) as Component; // PlayerMovement
                if (pmv == null) return true;
                var go = pmv.gameObject;
                // If match has ended or this player is eliminated, skip respawn coroutine on both server and clients
                // to avoid getting stuck in midair or restoring control incorrectly.
                if (FFALastStand.IsEnded() || FFALastStand.IsEliminated(go))
                {
                    // skip the respawn routine entirely but return a non-null IEnumerator
                    __result = SkipEnumerator();
                    return false;
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA RespawnRoutine prefix error: {e}");
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class PlayerRespawnManager_ColiRespawnRoutine_Prefix
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerRespawnManager");
            if (t == null) return null;
            foreach (var m in t.GetMethods(AccessTools.all))
            {
                if (m.Name == "ColiRespawnRoutine" && typeof(IEnumerator).IsAssignableFrom(m.ReturnType))
                    return m;
            }
            return null;
        }

        private static IEnumerator SkipEnumerator()
        {
            yield break;
        }

        static bool Prefix(object __instance, ref IEnumerator __result)
        {
            try
            {
                if (!FFAMode.IsActive()) return true;
                var f = AccessTools.Field(__instance.GetType(), "pmv");
                var pmv = f?.GetValue(__instance) as Component; // PlayerMovement
                if (pmv == null) return true;
                var go = pmv.gameObject;
                if (FFALastStand.IsEnded() || FFALastStand.IsEliminated(go))
                {
                    __result = SkipEnumerator();
                    return false;
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA ColiRespawnRoutine prefix error: {e}");
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class PlayerMovement_RespawnPlayer_Prefix
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PlayerMovement");
            return t == null ? null : AccessTools.Method(t, "RespawnPlayer");
        }

        static bool Prefix(object __instance)
        {
            try
            {
                if (!FFAMode.IsActive()) return true;
                var comp = __instance as Component;
                if (comp == null) return true;
                var go = comp.gameObject;
                if (FFALastStand.IsEliminated(go))
                {
                    // Block respawn for eliminated players on both server and client to keep state consistent.
                    try { FFAArenaLite.Plugin.Log?.LogInfo("FFA: Blocking RespawnPlayer for eliminated player."); } catch { }
                    return false;
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA RespawnPlayer prefix error: {e}");
            }
            return true;
        }
    }
}
