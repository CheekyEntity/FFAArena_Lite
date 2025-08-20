using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using FFAArenaLite.Modules;

namespace FFAArenaLite.Patches
{
    [HarmonyPatch]
    public static class WispController_PlayerSetup_Postfix
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("WispController");
            if (t == null) return null;
            // PlayerSetup(GameObject ownerobj, Vector3 fwdVector, int level)
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "PlayerSetup")
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 3 && ps[0].ParameterType == typeof(GameObject) && ps[1].ParameterType == typeof(Vector3))
                        return m;
                }
            }
            return null;
        }

        static void Postfix(object __instance, GameObject ownerobj)
        {
            try
            {
                if (!FFAMode.IsActive()) return;
                if (ownerobj == null) return;
                var t = __instance.GetType();
                // Fields we need
                var targetField = AccessTools.Field(t, "target"); // Transform
                var playerMaskObj = AccessTools.Field(t, "player")?.GetValue(__instance); // LayerMask or int
                int playerMask = playerMaskObj is LayerMask lm ? lm.value : (playerMaskObj is int i ? i : -1);

                // If target already set, ensure it's not owner; if null or owner, reselect ignoring team
                var target = targetField?.GetValue(__instance) as Transform;
                if (target != null)
                {
                    if (target.gameObject == ownerobj)
                    {
                        target = null;
                    }
                    else
                    {
                        return; // keep existing non-owner target
                    }
                }

                // Global search similar to original but without team checks
                Collider[] hits = Physics.OverlapSphere(Vector3.zero, 10000f, playerMask);
                float best = float.MaxValue;
                Transform bestTf = null;
                foreach (var col in hits)
                {
                    if (col == null) continue;
                    var go = col.gameObject;
                    if (!go.CompareTag("Player")) continue;
                    if (go == ownerobj) continue;
                    float d = (go.transform.position - Vector3.zero).sqrMagnitude; // effectively closest-to-origin; original uses global scan
                    if (d < best)
                    {
                        best = d;
                        bestTf = go.transform;
                    }
                }
                if (bestTf != null)
                {
                    targetField?.SetValue(__instance, bestTf);
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogDebug($"Wisp FFA target selection error: {e}");
            }
        }
    }
}
