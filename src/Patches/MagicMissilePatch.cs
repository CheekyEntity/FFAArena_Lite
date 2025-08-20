using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FFAArenaLite.Modules;

namespace FFAArenaLite.Patches
{
    [HarmonyPatch]
    public static class MagicMissleController_Update_Prefix
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MagicMissleController");
            return t == null ? null : AccessTools.Method(t, "Update");
        }

        // If MissleTarget is null, the original Update() searches for a target while filtering by team.
        // In FFA we want to ignore team and target any other player except the owner, while preserving other filters.
        static void Prefix(object __instance)
        {
            try
            {
                if (!FFAMode.IsActive()) return;
                var instType = __instance.GetType();
                var missleTargetField = AccessTools.Field(instType, "MissleTarget");
                var target = missleTargetField?.GetValue(__instance) as GameObject;
                if (target != null) return; // Respect existing target

                // Fetch needed fields
                var owner = AccessTools.Field(instType, "playerOwner")?.GetValue(__instance) as GameObject;
                if (owner == null) return;
                var layerMaskObj = AccessTools.Field(instType, "playerLayer")?.GetValue(__instance);
                int layerMask = layerMaskObj is LayerMask lm ? lm.value : (layerMaskObj is int i ? i : -1);
                var forwardVectorObj = AccessTools.Field(instType, "forwardVector")?.GetValue(__instance);
                Vector3 forwardVector = forwardVectorObj is Vector3 v ? v : Vector3.forward;
                bool shotByAi = false;
                try { var f = AccessTools.Field(instType, "shotByAi"); if (f != null) shotByAi = (bool)f.GetValue(__instance); } catch { }

                // Scan like the original but ignore team comparisons
                Collider[] hits = Physics.OverlapSphere(((Component)__instance).transform.position, 30f, layerMask);
                float bestScore = float.MaxValue;
                GameObject best = null;
                foreach (var col in hits)
                {
                    if (col == null) continue;
                    var go = col.gameObject;
                    // Exclude self/owner
                    if (go == owner) continue;
                    if (go.TryGetComponent<GetPlayerGameobject>(out var gpo) && gpo.player == owner) continue;
                    // Preserve AI exclusions
                    if (shotByAi && (go.CompareTag("PlayerNpc") || go.CompareTag("Ignorable"))) continue;
                    // Angle and combined score like original
                    Vector3 vec = col.transform.position - ((Component)__instance).transform.position;
                    float dist = vec.magnitude;
                    float angle = Vector3.Angle(forwardVector, vec.normalized);
                    if (angle > 90f) continue;
                    float score = dist + angle * 0.5f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = go;
                    }
                }

                if (best != null)
                {
                    missleTargetField?.SetValue(__instance, best);
                }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogDebug($"MagicMissile FFA target selection error: {e}");
            }
        }
    }
}
