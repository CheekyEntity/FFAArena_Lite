using System;
using HarmonyLib;
using UnityEngine;

namespace FFAArenaLite.Patches
{
    [HarmonyPatch]
    public static class EnableMeshRenderersPatch
    {
        // Dynamically resolve EnableMeshRenderers.enableRenderers
        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("EnableMeshRenderers");
            if (t == null) return null;
            return AccessTools.Method(t, "enableRenderers");
        }

        // Only intervene for FFA mode/selection; otherwise let vanilla proceed
        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            bool isFFA = FFAArenaLite.Modules.FFAMode.IsActive();
            try
            {
                // Also treat pre-start selection as active to catch early calls
                if (!isFFA)
                {
                    try { isFFA = MainMenuPatch.IsFFASelected(); } catch { }
                }
            }
            catch { }

            if (!isFFA) return true; // run original

            try
            {
                var comp = __instance as Component;
                if (comp == null) return true; // fallback to original if unexpected

                // Mirror original behavior but filter out flag/pole/banner renderers
                int enabled = 0, skipped = 0;
                var mrs = comp.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                foreach (var mr in mrs)
                {
                    if (mr == null) continue;
                    if (ShouldSkipObject(mr.gameObject, mr.sharedMaterials)) { skipped++; continue; }
                    mr.enabled = true;
                    enabled++;
                }

                // Also handle SkinnedMeshRenderers like vanilla
                var smrs = comp.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
                foreach (var sr in smrs)
                {
                    if (sr == null) continue;
                    if (ShouldSkipObject(sr.gameObject, sr.sharedMaterials)) { skipped++; continue; }
                    sr.enabled = true;
                    enabled++;
                }

                if (skipped > 0)
                {
                    FFAArenaLite.Plugin.Log?.LogInfo($"EnableMeshRenderersPatch: enabled={enabled}, skipped(flag/pole/banner)={skipped} under '{comp.gameObject.name}'.");
                }

                // Skip original since we handled enabling
                return false;
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"EnableMeshRenderersPatch failed, falling back to original: {e}");
                return true;
            }
        }

        private static bool ShouldSkipObject(GameObject go, Material[] mats)
        {
            try
            {
                if (go == null) return false;

                // 1) Skip anything under a FlagController hierarchy (castle/flag logic owner)
                var flagControllerType = AccessTools.TypeByName("FlagController");
                if (flagControllerType != null)
                {
                    var getInParent = typeof(Component).GetMethod("GetComponentInParent", new Type[] { typeof(Type) });
                    if (getInParent != null)
                    {
                        var fc = getInParent.Invoke(go.transform, new object[] { flagControllerType }) as Component;
                        if (fc != null) return true;
                    }
                }

                // 2) Name heuristics on objects up the chain
                if (NameMatchesHeuristic(go.transform)) return true;

                // 3) Material name heuristic
                try
                {
                    if (mats != null)
                    {
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m == null) continue;
                            if (StringMatchesHeuristic(m.name)) return true;
                        }
                    }
                }
                catch { }
            }
            catch { }
            return false;
        }

        private static bool NameMatchesHeuristic(Transform t)
        {
            const int depth = 5; // check a few ancestors only
            int steps = 0;
            while (t != null && steps++ < depth)
            {
                if (StringMatchesHeuristic(t.name)) return true;
                t = t.parent;
            }
            return false;
        }

        private static bool StringMatchesHeuristic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.ToLowerInvariant();
            // Common keywords seen in scenes: flag, pole, banner, pennant
            return s.Contains("flag") || s.Contains("pole") || s.Contains("banner") || s.Contains("pennant");
        }
    }
}
