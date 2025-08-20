using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using System.Linq;

namespace FFAArenaLite.Patches
{
    // Disable FlagController behavior in FFA to hide flags and prevent capture logic.
    [HarmonyPatch]
    public static class FlagController_OnStartClient_FFAPatch
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("FlagController");
            return t != null ? AccessTools.Method(t, "OnStartClient") : null;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                if (!(FFAArenaLite.Modules.FFAMode.IsActive() || FFAArenaLite.Patches.MainMenuPatch.IsFFASelected()))
                    return true;

                if (__instance is Component comp && comp != null)
                {
                    // Disable this behaviour to stop logic
                    if (comp is Behaviour beh)
                        beh.enabled = false;

                    // Disable player trigger to prevent captures
                    var col = comp.GetComponent<Collider>();
                    if (col != null) col.enabled = false;

                    // Hide flag visuals/audio/particles but keep the castle object active
                    try
                    {
                        var t = comp.GetType();
                        var flagVisualField = AccessTools.Field(t, "flagvisual");
                        var flagAniField = AccessTools.Field(t, "FlagAni");
                        var particlesField = AccessTools.Field(t, "particles");
                        var audioField = AccessTools.Field(t, "FlagAudio");
                        if (flagVisualField?.GetValue(__instance) is Renderer rend) rend.enabled = false;
                        if (flagAniField?.GetValue(__instance) is Behaviour animBeh) animBeh.enabled = false;
                        if (particlesField?.GetValue(__instance) is ParticleSystem[] psArr)
                        {
                            foreach (var ps in psArr) { if (ps == null) continue; ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var r = ps.GetComponent<Renderer>(); if (r) r.enabled = false; }
                        }
                        if (audioField?.GetValue(__instance) is AudioSource aus) { aus.Stop(); aus.enabled = false; }

                        // Proactively hide any child renderers that look like flag/pole/banner
                        foreach (var r in comp.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r == null) continue;
                            string rn = r.name.ToLowerInvariant();
                            bool match = rn.Contains("flag") || rn.Contains("pole") || rn.Contains("banner");
                            if (!match)
                            {
                                // check material names
                                try
                                {
                                    var mats = r.sharedMaterials;
                                    for (int i = 0; i < mats.Length && !match; i++)
                                    {
                                        var m = mats[i];
                                        if (m == null) continue;
                                        string mn = m.name.ToLowerInvariant();
                                        if (mn.Contains("flag") || mn.Contains("pole") || mn.Contains("banner")) match = true;
                                    }
                                }
                                catch { }
                            }
                            if (match) r.enabled = false;
                        }

                        
                    }
                    catch { }

                    try { var n = comp.gameObject != null ? comp.gameObject.name : "<null>"; FFAArenaLite.Plugin.Log?.LogInfo($"FFA: Neutralized FlagController in OnStartClient on '{n}'."); } catch { }
                }
                return false; // skip original OnStartClient
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA: FlagController_OnStartClient_FFAPatch failed: {e}");
                // Fail open to avoid breaking game if something unexpected happens
                return true;
            }
        }
    }

    // Early guard in case Awake does work before OnStartClient
    [HarmonyPatch]
    public static class FlagController_Awake_FFAPatch
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("FlagController");
            return t != null ? AccessTools.Method(t, "Awake") : null;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                if (!(FFAArenaLite.Modules.FFAMode.IsActive() || FFAArenaLite.Patches.MainMenuPatch.IsFFASelected()))
                    return true;

                if (__instance is Component comp && comp != null)
                {
                    // Same neutralization as OnStartClient for early lifecycle
                    if (comp is Behaviour beh)
                        beh.enabled = false;
                    var col = comp.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                    try
                    {
                        var t = comp.GetType();
                        var flagVisualField = AccessTools.Field(t, "flagvisual");
                        var flagAniField = AccessTools.Field(t, "FlagAni");
                        var particlesField = AccessTools.Field(t, "particles");
                        var audioField = AccessTools.Field(t, "FlagAudio");
                        if (flagVisualField?.GetValue(__instance) is Renderer rend) rend.enabled = false;
                        if (flagAniField?.GetValue(__instance) is Behaviour animBeh) animBeh.enabled = false;
                        if (particlesField?.GetValue(__instance) is ParticleSystem[] psArr)
                        {
                            foreach (var ps in psArr) { if (ps == null) continue; ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var r = ps.GetComponent<Renderer>(); if (r) r.enabled = false; }
                        }
                        if (audioField?.GetValue(__instance) is AudioSource aus) { aus.Stop(); aus.enabled = false; }

                        // Proactively hide any child renderers that look like flag/pole/banner
                        foreach (var r in comp.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r == null) continue;
                            string rn = r.name.ToLowerInvariant();
                            bool match = rn.Contains("flag") || rn.Contains("pole") || rn.Contains("banner");
                            if (!match)
                            {
                                try
                                {
                                    var mats = r.sharedMaterials;
                                    for (int i = 0; i < mats.Length && !match; i++)
                                    {
                                        var m = mats[i];
                                        if (m == null) continue;
                                        string mn = m.name.ToLowerInvariant();
                                        if (mn.Contains("flag") || mn.Contains("pole") || mn.Contains("banner")) match = true;
                                    }
                                }
                                catch { }
                            }
                            if (match) r.enabled = false;
                        }

                        
                    }
                    catch { }
                    try { var n = comp.gameObject != null ? comp.gameObject.name : "<null>"; FFAArenaLite.Plugin.Log?.LogInfo($"FFA: Neutralized FlagController in Awake on '{n}'."); } catch { }
                }
                return false; // skip original Awake
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA: FlagController_Awake_FFAPatch failed: {e}");
                return true;
            }
        }
    }

    // Prevent capture alert UI from running during FFA
    [HarmonyPatch]
    public static class CastleFlagCapturedNotifier_Start_FFAPatch
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("CastleFlagCapturedNotifier");
            return t != null ? AccessTools.Method(t, "StartFlagsShit") : null;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                if (!FFAArenaLite.Modules.FFAMode.IsActive())
                    return true;

                if (__instance is Component comp && comp != null)
                {
                    if (comp is Behaviour beh)
                        beh.enabled = false;
                    FFAArenaLite.Plugin.Log?.LogInfo("FFA: Disabled CastleFlagCapturedNotifier component.");
                }
                return false; // skip notifier startup
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA: CastleFlagCapturedNotifier_Start_FFAPatch failed: {e}");
                return true;
            }
        }
    }
}
