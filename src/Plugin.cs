using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using FFAArenaLite.Modules;

namespace FFAArenaLite
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("MageArena.exe")]
    [BepInDependency("com.magearena.modsync", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.cheekyentity.ffaarena.lite";
        public const string PluginName = "FFA Arena Lite";
        public const string PluginVersion = "1.0.4";

        // This mod requires both client and host to have it (ModSync)
        public static string modsync = "all";

        private Harmony _harmony;
        internal static ManualLogSource Log;
        internal static ConfigEntry<int> InitialLives;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");
            // Configs
            InitialLives = Config.Bind("FFA", "InitialLives", 3, "Default lives per player in FFA last-player-standing mode.");
            // Apply config to FFA module defaults
            try { FFALastStand.DefaultLives = InitialLives.Value; } catch { }
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo("Harmony patches applied.");
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch { }
            Log?.LogInfo("Harmony patches removed. Goodbye.");
        }
    }
}
