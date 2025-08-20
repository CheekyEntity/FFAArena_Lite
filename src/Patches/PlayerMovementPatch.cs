using HarmonyLib;

namespace FFAArenaLite.Patches
{
    // Intentionally left without any patches. We removed TelePlayer hooks to revert to base behavior.
    // All FFA logic will ensure correct teamSpawns in MainMenuPatch instead.
    public static class PlayerMovementPatch
    {
    }
}
