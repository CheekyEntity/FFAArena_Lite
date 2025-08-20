using UnityEngine;

namespace FFAArenaLite.Modules
{
    public static class FFAMode
    {
        private static bool? _cached;

        public static bool IsActive()
        {
            if (_cached.HasValue)
                return _cached.Value;

            // Heuristic: presence of our runtime spawn container created by SpawnService
            try
            {
                var go = GameObject.Find("FFA_Runtime_SpawnContainer");
                if (go != null)
                {
                    _cached = true;
                    return true;
                }
            }
            catch { }

            _cached = false;
            return _cached.Value;
        }

        public static void InvalidateDetection()
        {
            _cached = null;
        }

        // Removed GetManager() to avoid a hard dependency on FFAManager component.
    }
}
