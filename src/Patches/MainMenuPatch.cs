using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

namespace FFAArenaLite.Patches
{
    [HarmonyPatch]
    public static class MainMenuPatch
    {
        // Tracks if host selected an FFA map via our buttons: 1=Medium, 0=Large
        private static int? selectedFFAMapinfo;

        // Expose selection state for other patches in this file
        internal static bool IsFFASelected()
        {
            return selectedFFAMapinfo.HasValue;
        }

        // Helper: decide if we should hide team-related UI for FFA on this client/host
        public static bool ShouldHideTeamUI(object mainMenuInstance)
        {
            try
            {
                // If we've positively detected FFA or selection, that's enough
                if (FFAArenaLite.Modules.FFAMode.IsActive() || IsFFASelected()) return true;

                // Otherwise, look for markers in InGameLobby or globally which indicate FFA context
                var mmType = mainMenuInstance?.GetType();
                GameObject inGameLobbyGo = null;
                try
                {
                    var inGameLobbyField = AccessTools.Field(mmType, "InGameLobby");
                    inGameLobbyGo = inGameLobbyField?.GetValue(mainMenuInstance) as GameObject;
                }
                catch { }

                bool MarkerInTransformTree(Transform root)
                {
                    if (root == null) return false;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null) continue;
                        var n = t.name;
                        if (string.IsNullOrEmpty(n)) continue;
                        if (n == "FFA_Medium_Button" || n == "FFA_Large_Button" || n == "FreeForAllLabel_Auto" || n == "EditTeamFlag")
                            return true;
                    }
                    return false;
                }

                if (inGameLobbyGo != null && MarkerInTransformTree(inGameLobbyGo.transform)) return true;

                // Global scan as a fallback for differing client hierarchies
                try
                {
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null) continue;
                        var n = t.name;
                        if (n == "FFA_Medium_Button" || n == "FFA_Large_Button" || n == "FreeForAllLabel_Auto" || n == "EditTeamFlag")
                            return true;
                    }
                }
                catch { }
            }
            catch { }
            return false;
        }

        // Helper: detect if this local player is acting as Host/Server
        internal static bool IsLocalHostOrServer(object mainMenuInstance)
        {
            // Conservative host check: only treat as host if FishNet NM reports IsServer/IsHost true.
            try
            {
                var nmType = AccessTools.TypeByName("FishNet.Managing.NetworkManager");
                if (nmType != null)
                {
                    var instProp = nmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var nmInst = instProp?.GetValue(null, null) ?? GameObject.FindFirstObjectByType(nmType);
                    if (nmInst != null)
                    {
                        // IsServer or IsHost
                        var isServerProp = nmType.GetProperty("IsServer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                            ?? nmType.GetProperty("IsHost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (isServerProp != null)
                        {
                            var v = isServerProp.GetValue(nmInst, null);
                            if (v is bool b && b) return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        // Attempts to find an inactive Transform by a scene path like "Canvas (1)/Main/JoinLobby"
        internal static Transform FindInactiveTransform(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var parts = path.Split('/');
                if (parts.Length == 0) return null;
                // Search all transforms (includes inactive) and pick the first root that matches
                var all = Resources.FindObjectsOfTypeAll<Transform>();
                Transform current = null;
                foreach (var t in all)
                {
                    if (t.parent == null && t.name == parts[0]) { current = t; break; }
                }
                if (current == null) return null;
                for (int i = 1; i < parts.Length; i++)
                {
                    string seg = parts[i];
                    Transform next = null;
                    for (int c = 0; c < current.childCount; c++)
                    {
                        var ch = current.GetChild(c);
                        if (ch != null && ch.name == seg) { next = ch; break; }
                    }
                    if (next == null) return null;
                    current = next;
                }
                return current;
            }
            catch { return null; }
        }

        // Clone a button from an inactive scene path, retitle and rebind onClick
        internal static Button CloneButtonFromPath(string sourcePath, Transform parent, string newName, string label, Action onClick)
        {
            try
            {
                var src = FindInactiveTransform(sourcePath);
                if (src == null) return null;
                var srcBtn = src.GetComponent<Button>();
                if (srcBtn == null) return null;
                var go = UnityEngine.Object.Instantiate(srcBtn.gameObject, parent);
                go.name = newName;
                // Make sure cloned object is visible/enabled under the lobby holder
                go.SetActive(true);
                if (parent != null) go.layer = parent.gameObject.layer;
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.localScale = Vector3.one;
                    rt.anchoredPosition3D = Vector3.zero;
                }
                var cg = go.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
                var btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    if (onClick != null) btn.onClick.AddListener(() => onClick());
                }
                // Style like MageConfigurationAPI.MenuButtonPrefab: set all UnityEngine.UI.Text labels only
                var texts = go.GetComponentsInChildren<Text>(true);
                foreach (var tx in texts) tx.text = label;

                return btn;
            }
            catch { return null; }
        }

        // Postfix on MainMenuManager.ActuallyStartGameActually to ensure teamSpawns is hydrated safely.
        [HarmonyPostfix]
        private static void ActuallyStartGameActually_Postfix(object __instance)
        {
            try
            {
                var mmType = __instance.GetType();
                // Access field 'pm' (PlayerMovement) on MainMenuManager
                var pmField = AccessTools.Field(mmType, "pm");
                if (pmField == null) return;
                var pm = pmField.GetValue(__instance);
                if (pm == null) return;

                var pmType = pm.GetType();
                var teamSpawnsField = AccessTools.Field(pmType, "teamSpawns");
                if (teamSpawnsField == null) return;

                var currentValue = teamSpawnsField.GetValue(pm);
                var elemType = teamSpawnsField.FieldType.IsArray ? teamSpawnsField.FieldType.GetElementType() : null;
                if (elemType == null) return;

                // Build a minimal safe array based on actual expected element type
                var service = new Modules.SpawnService();
                int mapinfoVal = 0;
                var mapinfoField = AccessTools.Field(mmType, "mapinfo");
                if (mapinfoField != null)
                {
                    try { mapinfoVal = (int)mapinfoField.GetValue(__instance); } catch { mapinfoVal = 0; }
                }
                // Prefer FFA-specific spawns if our FFA selection flag is set
                if (selectedFFAMapinfo.HasValue)
                {
                    service.CollectFFASpawnsByMapInfo(selectedFFAMapinfo.Value);
                    FFAArenaLite.Plugin.Log?.LogInfo($"Using FFA custom spawns for mapinfo={selectedFFAMapinfo.Value}.");
                }
                else
                {
                    service.CollectNeutralSpawns();
                }
                var sourceSpawns = service.GetSpawns();

                // Prefer matching game's signature: RespawnWormhole[]
                var wormholeType = AccessTools.TypeByName("RespawnWormhole");
                bool wantWormholes = wormholeType != null && elemType == wormholeType;

                int desiredLen = 4;

                Array BuildWormholesFromTransforms()
                {
                    int count = Mathf.Max(desiredLen, sourceSpawns.Count);
                    var result = Array.CreateInstance(elemType, count);
                    // Ensure runtime container exists for proper parenting/cleanup
                    FFAArenaLite.Modules.SpawnService.EnsureRuntimeContainer();
                    GameObject runtime = null;
                    try { runtime = GameObject.Find("FFA_Runtime_SpawnContainer"); } catch { }
                    for (int i = 0; i < count; i++)
                    {
                        var tf = sourceSpawns.Count > 0 ? sourceSpawns[i % sourceSpawns.Count] : null;
                        object assign = null;
                        if (tf != null)
                        {
                            if (wantWormholes)
                            {
                                // Create or reuse a wormhole GameObject under runtime container, ensure spawnpos child
                                GameObject go = null;
                                try
                                {
                                    string name = $"FFA_Wormhole_{i}";
                                    if (runtime != null)
                                    {
                                        // Try reuse existing child by name
                                        Transform existing = null;
                                        foreach (Transform ch in runtime.transform)
                                        {
                                            if (ch != null && ch.name == name) { existing = ch; break; }
                                        }
                                        go = existing != null ? existing.gameObject : null;
                                    }
                                    if (go == null)
                                    {
                                        go = new GameObject($"FFA_Wormhole_{i}");
                                        if (runtime != null) go.transform.SetParent(runtime.transform, false);
                                    }
                                    go.transform.position = tf.position;

                                    var addComp = typeof(GameObject).GetMethod("AddComponent", new Type[] { typeof(Type) });
                                    var comp = go.GetComponent(wormholeType) as Component ?? addComp?.Invoke(go, new object[] { wormholeType }) as Component;
                                    if (comp != null)
                                    {
                                        var spawnposField = AccessTools.Field(wormholeType, "spawnpos");
                                        Transform sp = null;
                                        try
                                        {
                                            sp = spawnposField?.GetValue(comp) as Transform;
                                        }
                                        catch { }
                                        if (sp == null)
                                        {
                                            // Ensure dedicated child spawnpos exists
                                            var child = new GameObject("spawnpos");
                                            child.transform.SetParent(go.transform, false);
                                            sp = child.transform;
                                        }
                                        sp.position = tf.position;
                                        spawnposField?.SetValue(comp, sp);
                                        // Do NOT call resizeWormhole() here; teleporter logic will handle VFX
                                        assign = comp;
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                // Fallback: try to get required component type from transform
                                var comp = (tf as Component)?.GetComponent(elemType) ?? (tf as Component)?.GetComponentInChildren(elemType, true);
                                assign = comp as UnityEngine.Object;
                                if (assign == null && elemType == typeof(Transform)) assign = tf;
                            }
                        }
                        result.SetValue(assign, i);
                    }
                    // Fill nulls with first non-null
                    object first = null;
                    for (int i = 0; i < result.Length; i++) { if (result.GetValue(i) != null) { first = result.GetValue(i); break; } }
                    if (first != null)
                    {
                        for (int i = 0; i < result.Length; i++) if (result.GetValue(i) == null) result.SetValue(first, i);
                    }
                    return result;
                }

                Array newArr;
                try
                {
                    newArr = BuildWormholesFromTransforms();
                }
                catch
                {
                    // Fallback to keep existing if creation failed
                    newArr = currentValue as Array;
                }

                if (newArr != null)
                {
                    // Ensure at least 4 elements
                    if (newArr.Length < desiredLen)
                    {
                        var expanded = Array.CreateInstance(elemType, desiredLen);
                        for (int i = 0; i < desiredLen; i++)
                        {
                            var v = i < newArr.Length ? newArr.GetValue(i) : (newArr.Length > 0 ? newArr.GetValue(0) : null);
                            expanded.SetValue(v, i);
                        }
                        newArr = expanded;
                    }
                    teamSpawnsField.SetValue(pm, newArr);
                }
                // Clear FFA selection after applying so normal modes are unaffected in later games
                selectedFFAMapinfo = null;

                // Network-free mode: no FFAVictoryService. End UI is handled locally via FFALastStand.ShowWinOverlay().

                // Verification (debug): log first entry and total count
                try
                {
                    int total = newArr?.Length ?? 0;
                    if (total > 0)
                    {
                        var obj = newArr.GetValue(0) as UnityEngine.Object;
                        Vector3 pos = Vector3.zero;
                        string extra = string.Empty;
                        if (obj is Component c) pos = c.transform.position;
                        else if (obj is GameObject g) pos = g.transform.position;
                        try
                        {
                            var t = obj?.GetType();
                            if (t != null && t.Name == "RespawnWormhole")
                            {
                                var sf = HarmonyLib.AccessTools.Field(t, "spawnpos");
                                var sp = sf?.GetValue(obj) as Transform;
                                if (sp != null) extra = $", spawnpos={sp.position}";
                            }
                        }
                        catch { }
                        FFAArenaLite.Plugin.Log?.LogDebug($"teamSpawns[0/{total}] type={(obj != null ? obj.GetType().Name : "null")} pos={pos}{extra}");
                    }
                }
                catch { }
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"MainMenuPatch hydration failed: {e}");
            }
        }

        // Dynamically resolve target method to avoid compile-time dependency
        static System.Reflection.MethodBase TargetMethod()
        {
            var mm = AccessTools.TypeByName("MainMenuManager");
            if (mm == null) return null;
            return AccessTools.Method(mm, "ActuallyStartGameActually");
        }

        public static void ApplyFFASelection(object mainMenuInstance, int mapinfoValue)
        {
            try
            {
                var mmType = mainMenuInstance.GetType();
                var mapinfoField = AccessTools.Field(mmType, "mapinfo");
                mapinfoField?.SetValue(mainMenuInstance, mapinfoValue);
                selectedFFAMapinfo = mapinfoValue;

                // Mirror behavior of SmallMap/LargeMap: update lobby size
                var bmType = AccessTools.TypeByName("BootstrapManager");
                var instanceProp = AccessTools.Property(bmType, "instance");
                var bmInst = instanceProp?.GetValue(null, null);
                var changeLobbySize = AccessTools.Method(bmType, "ChangeLobbySize");
                changeLobbySize?.Invoke(bmInst, new object[] { 8 });

                // Try to toggle checkmarks to match map selection if available
                var checkmarksField = AccessTools.Field(mmType, "checkmarks");
                var checksObj = checkmarksField?.GetValue(mainMenuInstance) as GameObject[];
                if (checksObj != null && checksObj.Length >= 3)
                {
                    // mapinfo 1=Small, 0=Large, 2=Colosseum (from decompiled code)
                    checksObj[0].SetActive(mapinfoValue == 1);
                    checksObj[1].SetActive(mapinfoValue == 0);
                    checksObj[2].SetActive(false);
                }

                // Hide the EditTeamFlag button immediately after FFA selection
                try
                {
                    int hiddenFlags = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                        {
                            t.gameObject.SetActive(false);
                            hiddenFlags++;
                        }
                    }
                    if (hiddenFlags > 0)
                        FFAArenaLite.Plugin.Log?.LogInfo($"FFA: hid {hiddenFlags} EditTeamFlag object(s) on selection.");
                }
                catch { }

                FFAArenaLite.Plugin.Log?.LogInfo($"FFA selected. mapinfo={mapinfoValue}. Lobby size set to 8.");
                // Ensure the FFA runtime container exists now so FFAMode.IsActive() detects true during StartGameActual
                FFAArenaLite.Modules.SpawnService.EnsureRuntimeContainer();

                // Note: Do not auto-assign or touch team-swap gating here. Team join will happen naturally at game start.
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"ApplyFFASelection failed: {e}");
            }
        }

        // Reset selection and destroy any runtime FFA objects so state does not leak across sessions
        internal static void ClearFFAState()
        {
            try
            {
                selectedFFAMapinfo = null;
                FFAArenaLite.Modules.SpawnService.DestroyRuntimeContainer();
                FFAArenaLite.Modules.FFALastStand.Reset();
                FFAArenaLite.Plugin.Log?.LogInfo("FFA: cleared selection and destroyed runtime container.");
            }
            catch { }
        }
    }

    // Separate patch class for OpenLobby to avoid conflicting TargetMethod resolution
    [HarmonyPatch]
    public static class MainMenuOpenLobbyPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var mm = AccessTools.TypeByName("MainMenuManager");
            if (mm == null) return null;
            return AccessTools.Method(mm, "OpenLobby");
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            try
            {
                FFAArenaLite.Plugin.Log?.LogInfo("OpenLobby: Postfix start");
                // Ensure we start from a clean slate whenever Lobby UI opens
                MainMenuPatch.ClearFFAState();
                // Defensive: remove any existing FFA UI artifacts before deciding host/client
                try
                {
                    int disabledAll = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null) continue;
                        var n = t.name;
                        if (n == "FFA_Medium_Button" || n == "FFA_Large_Button" || n == "FreeForAllLabel_Auto")
                        {
                            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); disabledAll++; }
                        }
                    }
                    if (disabledAll > 0) FFAArenaLite.Plugin.Log?.LogDebug($"FFA: Pre-sanitize — hid {disabledAll} FFA UI object(s).");
                }
                catch { }
                var mmType = __instance.GetType();
                // Only the host should see and use FFA selection buttons
                if (!MainMenuPatch.IsLocalHostOrServer(__instance))
                {
                    FFAArenaLite.Plugin.Log?.LogDebug("OpenLobby: detected Client (not host) — will hide FFA UI");
                    try
                    {
                        // Attempt to find a reasonable root to search
                        Transform searchRoot = null;
                        try
                        {
                            var mapHolderField2 = AccessTools.Field(mmType, "Mapchoseholder");
                            var raw2 = mapHolderField2?.GetValue(__instance);
                            if (raw2 is GameObject mgo2) searchRoot = mgo2.transform;
                            else if (raw2 is Transform mt2) searchRoot = mt2;
                        }
                        catch { }
                        if (searchRoot == null)
                        {
                            var canvas = GameObject.FindAnyObjectByType<Canvas>();
                            if (canvas != null) searchRoot = canvas.transform;
                        }
                        int disabled = 0;
                        if (searchRoot != null)
                        {
                            foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
                            {
                                if (t == null) continue;
                                var n = t.name;
                                if (n == "FFA_Medium_Button" || n == "FFA_Large_Button" || n == "FreeForAllLabel_Auto")
                                {
                                    if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); disabled++; }
                                }
                            }
                        }
                        // Global fallback
                        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                        {
                            if (t == null) continue;
                            var n = t.name;
                            if (n == "FFA_Medium_Button" || n == "FFA_Large_Button" || n == "FreeForAllLabel_Auto")
                            {
                                if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); disabled++; }
                            }
                        }
                        if (disabled > 0) FFAArenaLite.Plugin.Log?.LogDebug($"FFA: Client view — hid {disabled} FFA selection UI objects.");
                    }
                    catch { }
                    return;
                }
                FFAArenaLite.Plugin.Log?.LogInfo("OpenLobby: detected Host — will inject FFA UI if missing");
                // 1) Inject FFA buttons under Mapchoseholder
                try
                {
                    var mapHolderField = AccessTools.Field(mmType, "Mapchoseholder");
                    Transform mapHolder = null;
                    var raw = mapHolderField?.GetValue(__instance);
                    if (raw is GameObject mgo) mapHolder = mgo.transform;
                    else if (raw is Transform mt) mapHolder = mt;

                    if (mapHolder != null)
                    {
                        // Avoid duplicate injection
                        if (mapHolder.Find("FFA_Medium_Button") == null && mapHolder.Find("FFA_Large_Button") == null)
                        {
                            FFAArenaLite.Plugin.Log?.LogDebug("OpenLobby: FFA buttons not found under Mapchoseholder — proceeding to clone");
                            // Preferred: duplicate existing map size buttons under 'mapsizecheck' to preserve visuals and hitboxes
                            Button medBtn = null;
                            Button largeBtn = null;

                            // Try to locate Lobby/mapsizecheck/SmallMap and LargeMap
                            Transform searchRoot2 = mapHolder;
                            try
                            {
                                if (searchRoot2 == null)
                                {
                                    var canvas = GameObject.FindAnyObjectByType<Canvas>();
                                    searchRoot2 = canvas != null ? canvas.transform : null;
                                }
                                Transform lobbyTf = null;
                                if (searchRoot2 != null)
                                {
                                    foreach (var t in searchRoot2.GetComponentsInChildren<Transform>(true))
                                    {
                                        if (t != null && t.name == "Lobby") { lobbyTf = t; break; }
                                    }
                                }
                                if (lobbyTf == null) lobbyTf = searchRoot2; // fallback to root
                                Transform mapsizecheckTf = null;
                                if (lobbyTf != null)
                                {
                                    foreach (var t in lobbyTf.GetComponentsInChildren<Transform>(true))
                                    {
                                        if (t != null && t.name == "mapsizecheck") { mapsizecheckTf = t; break; }
                                    }
                                }

                                if (mapsizecheckTf != null)
                                {
                                    Transform smallSrc = null, largeSrc = null;
                                    foreach (var t in mapsizecheckTf.GetComponentsInChildren<Transform>(true))
                                    {
                                        if (t != null && t.name == "SmallMap") smallSrc = t;
                                        else if (t != null && t.name == "LargeMap") largeSrc = t;
                                    }
                                    FFAArenaLite.Plugin.Log?.LogDebug($"OpenLobby: sources found — SmallMap={(smallSrc!=null)} LargeMap={(largeSrc!=null)}");

                                    void SetButtonLabel(GameObject go, string text)
                                    {
                                        var texts = go.GetComponentsInChildren<Text>(true);
                                        foreach (var tx in texts) tx.text = text;
                                    }

                                    // Determine Lobby parent (same as ConfirmStart) so our buttons sit beneath the modal
                                    Transform lobbyParent = null;
                                    try
                                    {
                                        var confirmObj = GameObject.Find("ConfirmStart");
                                        if (confirmObj != null) lobbyParent = confirmObj.transform.parent;
                                    }
                                    catch { }
                                    if (lobbyParent == null)
                                        lobbyParent = mapsizecheckTf != null ? mapsizecheckTf.parent : mapHolder;

                                    // Clone SmallMap as FFA_Medium_Button
                                    if (smallSrc != null)
                                    {
                                        var medGo = UnityEngine.Object.Instantiate(smallSrc.gameObject);
                                        medGo.name = "FFA_Medium_Button";
                                        medGo.transform.SetParent(lobbyParent, worldPositionStays: false);
                                        var medRt = medGo.GetComponent<RectTransform>();
                                        var srcRt = smallSrc.GetComponent<RectTransform>();
                                        if (medRt != null && srcRt != null)
                                        {
                                            // keep same size/anchors/pivot, offset to the right
                                            medRt.anchorMin = srcRt.anchorMin;
                                            medRt.anchorMax = srcRt.anchorMax;
                                            medRt.pivot = srcRt.pivot;
                                            medRt.sizeDelta = srcRt.sizeDelta;
                                            medRt.localScale = Vector3.one;
                                            medRt.localRotation = Quaternion.identity;
                                            medRt.anchoredPosition = srcRt.anchoredPosition + new Vector2(300f, 0f);
                                        }
                                        var medCg = medGo.GetComponent<CanvasGroup>(); if (medCg != null) { medCg.alpha = 1f; medCg.interactable = true; medCg.blocksRaycasts = true; }
                                        var csf = medGo.GetComponentInChildren<ContentSizeFitter>(true); if (csf != null) csf.enabled = false;
                                        SetButtonLabel(medGo, "Medium");
                                        medBtn = medGo.GetComponent<Button>();
                                        if (medBtn != null)
                                        {
                                            medBtn.onClick.RemoveAllListeners();
                                            medBtn.onClick.AddListener(() => MainMenuPatch.ApplyFFASelection(__instance, 1));
                                            var img = medBtn.GetComponent<Image>(); if (img != null) img.raycastTarget = true;
                                            // Place at lowest priority under Lobby so modal appears above
                                            medBtn.transform.SetSiblingIndex(0);
                                        }
                                        FFAArenaLite.Plugin.Log?.LogInfo($"OpenLobby: Created FFA_Medium_Button (btn={(medBtn!=null)})");
                                    }

                                    // Clone LargeMap as FFA_Large_Button
                                    if (largeSrc != null)
                                    {
                                        var largeGo = UnityEngine.Object.Instantiate(largeSrc.gameObject);
                                        largeGo.name = "FFA_Large_Button";
                                        largeGo.transform.SetParent(lobbyParent, worldPositionStays: false);
                                        var largeRt = largeGo.GetComponent<RectTransform>();
                                        var srcRt2 = largeSrc.GetComponent<RectTransform>();
                                        if (largeRt != null && srcRt2 != null)
                                        {
                                            largeRt.anchorMin = srcRt2.anchorMin;
                                            largeRt.anchorMax = srcRt2.anchorMax;
                                            largeRt.pivot = srcRt2.pivot;
                                            largeRt.sizeDelta = srcRt2.sizeDelta;
                                            largeRt.localScale = Vector3.one;
                                            largeRt.localRotation = Quaternion.identity;
                                            largeRt.anchoredPosition = srcRt2.anchoredPosition + new Vector2(300f, 0f);
                                        }
                                        var lgCg = largeGo.GetComponent<CanvasGroup>(); if (lgCg != null) { lgCg.alpha = 1f; lgCg.interactable = true; lgCg.blocksRaycasts = true; }
                                        var csf2 = largeGo.GetComponentInChildren<ContentSizeFitter>(true); if (csf2 != null) csf2.enabled = false;
                                        SetButtonLabel(largeGo, "Large");
                                        largeBtn = largeGo.GetComponent<Button>();
                                        if (largeBtn != null)
                                        {
                                            largeBtn.onClick.RemoveAllListeners();
                                            largeBtn.onClick.AddListener(() => MainMenuPatch.ApplyFFASelection(__instance, 0));
                                            var img2 = largeBtn.GetComponent<Image>(); if (img2 != null) img2.raycastTarget = true;
                                        }
                                        FFAArenaLite.Plugin.Log?.LogInfo($"OpenLobby: Created FFA_Large_Button (btn={(largeBtn!=null)})");
                                    }
                                }
                                }
                                catch { }

                            // If we have valid buttons, ensure reasonable sibling order when layout groups exist
                            if (medBtn != null && largeBtn != null)
                            {
                                var parent = medBtn.transform.parent;
                                // Try to place after the original source if it exists under same parent
                                Transform src = null;
                                foreach (var p in new string[]{"Canvas (1)/Main/JoinLobby","Canvas/Main/JoinLobby","Canvas (1)/Main/JoinLobbyButton","Canvas/Main/JoinLobbyButton"})
                                {
                                    src = MainMenuPatch.FindInactiveTransform(p);
                                    if (src != null && src.parent == parent) break;
                                    src = null;
                                }
                                int idx = 0;
                                if (src != null && src.parent == parent) idx = src.GetSiblingIndex();
                                medBtn.transform.SetSiblingIndex(Mathf.Min(idx + 1, parent.childCount - 1));
                                largeBtn.transform.SetSiblingIndex(Mathf.Min(idx + 2, parent.childCount - 1));
                                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parent as RectTransform);

                                var h = parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                                var v = parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                                var g = parent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                                if (h != null || v != null || g != null)
                                {
                                    // already set indexes above; just rebuild to honor layout
                                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parent as RectTransform);
                                }
                                else
                                {
                                    // No layout group: position relative to an existing sibling button
                                    RectTransform medRect = medBtn.GetComponent<RectTransform>();
                                    RectTransform largeRect = largeBtn.GetComponent<RectTransform>();
                                    RectTransform baseRect = null;
                                    foreach (var b in parent.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                                    {
                                        if (b == medBtn || b == largeBtn) continue;
                                        baseRect = b.GetComponent<RectTransform>();
                                        if (baseRect != null && b.transform.parent == parent) break;
                                        baseRect = null;
                                    }
                                    Vector2 basePos = baseRect != null ? baseRect.anchoredPosition : Vector2.zero;
                                    if (medRect != null) medRect.anchoredPosition = basePos + new Vector2(0f, -60f);
                                    if (largeRect != null) largeRect.anchoredPosition = basePos + new Vector2(0f, -120f);
                                }
                                // Force requested positions regardless of layout groups
                                var medRT = medBtn.GetComponent<RectTransform>();
                                var largeRT = largeBtn.GetComponent<RectTransform>();
                                var medLE = medBtn.GetComponent<UnityEngine.UI.LayoutElement>() ?? medBtn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                                var largeLE = largeBtn.GetComponent<UnityEngine.UI.LayoutElement>() ?? largeBtn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                                medLE.ignoreLayout = true;
                                largeLE.ignoreLayout = true;
                                if (medRT != null) medRT.anchoredPosition = new Vector2(-760f, 260f);
                                if (largeRT != null) largeRT.anchoredPosition = new Vector2(-560f, 260f);
                                FFAArenaLite.Plugin.Log?.LogInfo($"FFA: positioned buttons. Medium=(-760,260) Large=(-560,260)");
                                var mr = medBtn.GetComponent<RectTransform>();
                                var lr = largeBtn.GetComponent<RectTransform>();
                                FFAArenaLite.Plugin.Log?.LogInfo($"Injected FFA Medium/Large buttons into lobby UI. medPos={(mr!=null?mr.anchoredPosition.ToString():"-")}, largePos={(lr!=null?lr.anchoredPosition.ToString():"-")}");

                                // Create the label by duplicating Lobby/mapsizecheck/nothin (2)
                                try
                                {
                                    // Prevent duplicates across multiple calls (no LINQ)
                                    Transform existing = null;
                                    var allForExisting = Resources.FindObjectsOfTypeAll<Transform>();
                                    for (int i = 0; i < allForExisting.Length; i++)
                                    {
                                        var t0 = allForExisting[i];
                                        if (t0 != null && t0.name == "FreeForAllLabel_Auto") { existing = t0; break; }
                                    }
                                    if (existing == null)
                                    {
                                        Transform lobbyTf = null;
                                        Transform sizeCheckTf = null;
                                        Transform sourceTf = null;
                                        // Establish a search root from 'parent' (buttons' parent) or Canvas
                                        Transform searchRoot = parent;
                                        if (searchRoot == null)
                                        {
                                            var canvas = GameObject.FindAnyObjectByType<Canvas>();
                                            searchRoot = canvas != null ? canvas.transform : null;
                                        }
                                        // Find Lobby -> mapsizecheck
                                        if (searchRoot != null)
                                        {
                                            foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
                                            {
                                                if (t != null && t.name == "Lobby") { lobbyTf = t; break; }
                                            }
                                        }
                                        if (lobbyTf == null) lobbyTf = searchRoot; // fallback
                                        if (lobbyTf != null)
                                        {
                                            foreach (var t in lobbyTf.GetComponentsInChildren<Transform>(true))
                                            {
                                                if (t != null && t.name == "mapsizecheck") { sizeCheckTf = t; break; }
                                            }
                                        }
                                        if (sizeCheckTf != null)
                                        {
                                            foreach (var t in sizeCheckTf.GetComponentsInChildren<Transform>(true))
                                            {
                                                if (t != null && t.name == "nothin (2)") { sourceTf = t; break; }
                                            }
                                        }
                                        if (sourceTf != null)
                                        {
                                            var clone = UnityEngine.Object.Instantiate(sourceTf.gameObject);
                                            clone.name = "FreeForAllLabel_Auto";
                                            // Parent under mapsizecheck to preserve UI anchors
                                            clone.transform.SetParent(sizeCheckTf != null ? sizeCheckTf : (searchRoot != null ? searchRoot : parent), worldPositionStays: false);

                                            bool textSet = false;
                                            var uiText = clone.GetComponent<UnityEngine.UI.Text>();
                                            if (uiText != null) { uiText.text = "Free for all"; textSet = true; }
                                            if (!textSet)
                                            {
                                                var tmpUguiType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                                                if (tmpUguiType != null)
                                                {
                                                    var tmpU = clone.GetComponent(tmpUguiType);
                                                    if (tmpU != null)
                                                    {
                                                        AccessTools.Property(tmpUguiType, "text")?.SetValue(tmpU, "Free for all", null);
                                                        textSet = true;
                                                    }
                                                }
                                            }
                                            if (!textSet)
                                            {
                                                var tmpType = AccessTools.TypeByName("TMPro.TextMeshPro");
                                                if (tmpType != null)
                                                {
                                                    var tmp = clone.GetComponent(tmpType);
                                                    if (tmp != null)
                                                    {
                                                        AccessTools.Property(tmpType, "text")?.SetValue(tmp, "Free for all", null);
                                                        textSet = true;
                                                    }
                                                }
                                            }

                                            // Position using RectTransform if present; fall back to world position
                                            var rt = clone.GetComponent<RectTransform>();
                                            var target = new Vector3(-420f, 160f, 0f);
                                            if (rt != null)
                                            {
                                                // Use anchoredPosition3D within the UI hierarchy
                                                rt.anchoredPosition3D = target;
                                            }
                                            else
                                            {
                                                clone.transform.position = target;
                                            }
                                            FFAArenaLite.Plugin.Log?.LogInfo($"FFA: Created FreeForAllLabel_Auto under mapsizecheck. pos=(-420,160,0) textSet={textSet}");
                                        }
                                        else
                                        {
                                            FFAArenaLite.Plugin.Log?.LogWarning("FFA: Could not find Lobby/mapsizecheck/nothin (2) to duplicate for label.");
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    FFAArenaLite.Plugin.Log?.LogWarning($"FFA: FreeForAll label creation failed: {e}");
                                }
                            }
                            else
                            {
                                FFAArenaLite.Plugin.Log?.LogWarning("OpenLobby_Postfix: failed to create FFA buttons from path and template.");
                            }
                        }
                        else
                        {
                            FFAArenaLite.Plugin.Log?.LogInfo("OpenLobby: FFA buttons already exist — skipping injection");
                        }
                    }
                    else
                    {
                        FFAArenaLite.Plugin.Log?.LogWarning("OpenLobby: Mapchoseholder not found — cannot inject FFA buttons");
                    }
                }
                catch (Exception ex)
                {
                    FFAArenaLite.Plugin.Log?.LogWarning($"OpenLobby: injection failed: {ex}");
                }

                // 2) Hide team join controls and CTF overlay (do not skip panel) only if FFA is active/selected
                if (FFAArenaLite.Modules.FFAMode.IsActive() || MainMenuPatch.IsFFASelected())
                {
                    var lobbyField = AccessTools.Field(mmType, "lobbyScreen");
                    var lobbyGo = lobbyField?.GetValue(__instance) as GameObject;
                    Transform root = lobbyGo != null ? lobbyGo.transform : null;
                    if (root == null)
                    {
                        var canvas = GameObject.FindAnyObjectByType<Canvas>();
                        root = canvas != null ? canvas.transform : null;
                    }
                    if (root != null)
                    {
                        int hidden = 0;
                        try
                        {
                            var ctfoverlayField = AccessTools.Field(mmType, "ctfoverlay");
                            var ctf = ctfoverlayField?.GetValue(__instance) as CanvasGroup;
                            if (ctf != null)
                            {
                                ctf.alpha = 0f;
                                ctf.interactable = false;
                                ctf.blocksRaycasts = false;
                                ctf.gameObject.SetActive(false);
                            }
                            // Hide the specific flag edit button found in the lobby: "EditTeamFlag"
                            try
                            {
                                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                                {
                                    if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                                    {
                                        t.gameObject.SetActive(false);
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                        catch { }

                        foreach (var btn in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                        {
                            var onClick = btn.onClick;
                            if (onClick == null) continue;
                            int count = onClick.GetPersistentEventCount();
                            bool isTeamBtn = false;
                            for (int i = 0; i < count; i++)
                            {
                                string m = onClick.GetPersistentMethodName(i);
                                if (m == "JoinTeam1" || m == "JoinTeam2" || m == "JoinLowestPlayerCountTeam")
                                { isTeamBtn = true; break; }
                            }
                            if (!isTeamBtn)
                            {
                                var name = btn.gameObject.name ?? string.Empty;
                                if (name.Contains("JoinTeam", StringComparison.OrdinalIgnoreCase)) isTeamBtn = true;
                                else
                                {
                                    var t = btn.GetComponentInChildren<Text>(true);
                                    if (t != null && t.text != null && t.text.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                                    else
                                    {
                                        var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                                        if (tmpType != null)
                                        {
                                            var tmp = btn.GetComponentInChildren(tmpType, true);
                                            if (tmp != null)
                                            {
                                                var textProp = AccessTools.Property(tmpType, "text");
                                                var val = textProp?.GetValue(tmp, null) as string;
                                                if (!string.IsNullOrEmpty(val) && val.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                                            }
                                        }
                                    }
                                }
                            }
                            // Also detect the flag edit button and hide it
                            if (!isTeamBtn)
                            {
                                bool isFlagEdit = false;
                                // Check persistent listeners for EditTeamFlag
                                for (int i = 0; i < count; i++)
                                {
                                    string m = onClick.GetPersistentMethodName(i);
                                    if (m == "EditTeamFlag") { isFlagEdit = true; break; }
                                }
                                if (!isFlagEdit)
                                {
                                    var name = btn.gameObject.name ?? string.Empty;
                                    if (name.Equals("EditTeamFlag", StringComparison.OrdinalIgnoreCase)) isFlagEdit = true;
                                    else
                                    {
                                        var t = btn.GetComponentInChildren<Text>(true);
                                        if (t != null && t.text != null && t.text.IndexOf("Edit Team Flag", StringComparison.OrdinalIgnoreCase) >= 0) isFlagEdit = true;
                                        else
                                        {
                                            var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                                            if (tmpType != null)
                                            {
                                                var tmp = btn.GetComponentInChildren(tmpType, true);
                                                if (tmp != null)
                                                {
                                                    var textProp = AccessTools.Property(tmpType, "text");
                                                    var val = textProp?.GetValue(tmp, null) as string;
                                                    if (!string.IsNullOrEmpty(val) && val.IndexOf("Edit Team Flag", StringComparison.OrdinalIgnoreCase) >= 0) isFlagEdit = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (isFlagEdit)
                                {
                                    btn.gameObject.SetActive(false);
                                    hidden++;
                                    continue;
                                }
                            }

                            if (isTeamBtn)
                            {
                                btn.gameObject.SetActive(false);
                                hidden++;
                            }
                        }
                        FFAArenaLite.Plugin.Log?.LogInfo($"FFA: hid {hidden} team selection buttons.");
                    }
                }
            }
            catch (Exception ex)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"OpenLobby_Postfix hide team selection failed: {ex}");
            }
        }
    }

    // After StartGame and StartGameActual, ensure team join controls are hidden within InGameLobby (do not skip panel)
    [HarmonyPatch]
    public static class MainMenuStartGamePatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var mm = AccessTools.TypeByName("MainMenuManager");
            if (mm == null) return null;
            return AccessTools.Method(mm, "StartGame");
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            try
            {
                if (!MainMenuPatch.ShouldHideTeamUI(__instance)) return;
                var mmType = __instance.GetType();
                var inGameLobbyField = AccessTools.Field(mmType, "InGameLobby");
                var inGameLobbyGo = inGameLobbyField?.GetValue(__instance) as GameObject;
                if (inGameLobbyGo == null) return;
                int hidden = 0;
                foreach (var btn in inGameLobbyGo.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    var onClick = btn.onClick;
                    if (onClick == null) continue;
                    int count = onClick.GetPersistentEventCount();
                    bool isTeamBtn = false;
                    for (int i = 0; i < count; i++)
                    {
                        string m = onClick.GetPersistentMethodName(i);
                        if (m == "JoinTeam1" || m == "JoinTeam2" || m == "JoinLowestPlayerCountTeam")
                        { isTeamBtn = true; break; }
                    }
                    if (!isTeamBtn)
                    {
                        var name = btn.gameObject.name ?? string.Empty;
                        if (name.Contains("JoinTeam", StringComparison.OrdinalIgnoreCase)) isTeamBtn = true;
                        else
                        {
                            var t = btn.GetComponentInChildren<Text>(true);
                            if (t != null && t.text != null && t.text.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                            else
                            {
                                var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                                if (tmpType != null)
                                {
                                    var tmp = btn.GetComponentInChildren(tmpType, true);
                                    if (tmp != null)
                                    {
                                        var textProp = AccessTools.Property(tmpType, "text");
                                        var val = textProp?.GetValue(tmp, null) as string;
                                        if (!string.IsNullOrEmpty(val) && val.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                                    }
                                }
                            }
                        }
                    }
                    if (isTeamBtn)
                    {
                        btn.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                // Also hide any EditTeamFlag controls that may still be visible
                try
                {
                    int flagHidden = 0;
                    foreach (var t in inGameLobbyGo.GetComponentsInChildren<Transform>(true))
                    {
                        if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); flagHidden++; }
                        }
                    }
                    // Global sweep as fallback (clients may have different hierarchy)
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); flagHidden++; }
                        }
                    }
                    if (flagHidden > 0) hidden += flagHidden;
                }
                catch { }
                FFAArenaLite.Plugin.Log?.LogInfo($"FFA: StartGame Postfix hid {hidden} team join controls in InGameLobby.");
            }
            catch (Exception ex)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"StartGame Postfix failed: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public static class MainMenuStartGameActualPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var mm = AccessTools.TypeByName("MainMenuManager");
            if (mm == null) return null;
            return AccessTools.Method(mm, "StartGameActual");
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            try
            {
                if (!MainMenuPatch.ShouldHideTeamUI(__instance)) return;
                var mmType = __instance.GetType();
                var inGameLobbyField = AccessTools.Field(mmType, "InGameLobby");
                var inGameLobbyGo = inGameLobbyField?.GetValue(__instance) as GameObject;
                if (inGameLobbyGo == null) return;
                int hidden = 0;
                foreach (var btn in inGameLobbyGo.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    var onClick = btn.onClick;
                    if (onClick == null) continue;
                    int count = onClick.GetPersistentEventCount();
                    bool isTeamBtn = false;
                    for (int i = 0; i < count; i++)
                    {
                        string m = onClick.GetPersistentMethodName(i);
                        if (m == "JoinTeam1" || m == "JoinTeam2" || m == "JoinLowestPlayerCountTeam")
                        { isTeamBtn = true; break; }
                    }
                    if (!isTeamBtn)
                    {
                        var name = btn.gameObject.name ?? string.Empty;
                        if (name.Contains("JoinTeam", StringComparison.OrdinalIgnoreCase)) isTeamBtn = true;
                        else
                        {
                            var t = btn.GetComponentInChildren<Text>(true);
                            if (t != null && t.text != null && t.text.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                            else
                            {
                                var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                                if (tmpType != null)
                                {
                                    var tmp = btn.GetComponentInChildren(tmpType, true);
                                    if (tmp != null)
                                    {
                                        var textProp = AccessTools.Property(tmpType, "text");
                                        var val = textProp?.GetValue(tmp, null) as string;
                                        if (!string.IsNullOrEmpty(val) && val.IndexOf("Join Team", StringComparison.OrdinalIgnoreCase) >= 0) isTeamBtn = true;
                                    }
                                }
                            }
                        }
                    }
                    if (isTeamBtn)
                    {
                        btn.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                // Also hide any EditTeamFlag controls that may still be visible
                try
                {
                    int flagHidden = 0;
                    foreach (var t in inGameLobbyGo.GetComponentsInChildren<Transform>(true))
                    {
                        if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); flagHidden++; }
                        }
                    }
                    // Global sweep as fallback (clients may have different hierarchy)
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t != null && string.Equals(t.name, "EditTeamFlag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); flagHidden++; }
                        }
                    }
                    if (flagHidden > 0) hidden += flagHidden;
                }
                catch { }
                FFAArenaLite.Plugin.Log?.LogInfo($"FFA: StartGameActual Postfix hid {hidden} team join controls in InGameLobby.");

                // Additionally, deactivate any existing FlagController and CastleFlagCapturedNotifier objects for FFA
                try
                {
                    var flagType = HarmonyLib.AccessTools.TypeByName("FlagController");
                    var notifType = HarmonyLib.AccessTools.TypeByName("CastleFlagCapturedNotifier");
                    int flagsOff = 0, notifsOff = 0;
                    if (flagType != null)
                    {
                        var found = Resources.FindObjectsOfTypeAll(flagType);
                        if (found != null)
                        {
                            foreach (var o in found)
                            {
                                if (o is Component c && c != null && c.gameObject != null)
                                {
                                    // Disable behaviour
                                    if (c is Behaviour beh && beh.enabled)
                                        beh.enabled = false;
                                    // Disable collider
                                    var col = c.GetComponent<Collider>();
                                    if (col != null && col.enabled) col.enabled = false;
                                    // Try hide flag visuals/audio/particles
                                    try
                                    {
                                        var t = c.GetType();
                                        var flagVisualField = HarmonyLib.AccessTools.Field(t, "flagvisual");
                                        var flagAniField = HarmonyLib.AccessTools.Field(t, "FlagAni");
                                        var particlesField = HarmonyLib.AccessTools.Field(t, "particles");
                                        var audioField = HarmonyLib.AccessTools.Field(t, "FlagAudio");
                                        if (flagVisualField?.GetValue(o) is Renderer rend) rend.enabled = false;
                                        if (flagAniField?.GetValue(o) is Behaviour animBeh) animBeh.enabled = false;
                                        if (particlesField?.GetValue(o) is ParticleSystem[] psArr)
                                        {
                                            foreach (var ps in psArr) { if (ps == null) continue; ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var r = ps.GetComponent<Renderer>(); if (r) r.enabled = false; }
                                        }
                                        if (audioField?.GetValue(o) is AudioSource aus) { aus.Stop(); aus.enabled = false; }

                                        // Hide any child renderers that likely represent flags/poles/banners
                                        foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                                        {
                                            if (r == null) continue;
                                            string rn = r.name != null ? r.name.ToLowerInvariant() : string.Empty;
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
                                                        string mn = m.name != null ? m.name.ToLowerInvariant() : string.Empty;
                                                        if (mn.Contains("flag") || mn.Contains("pole") || mn.Contains("banner")) match = true;
                                                    }
                                                }
                                                catch { }
                                            }
                                            if (match) r.enabled = false;
                                        }
                                    }
                                    catch { }
                                    flagsOff++;
                                }
                            }
                        }
                    }
                    if (notifType != null)
                    {
                        var foundN = Resources.FindObjectsOfTypeAll(notifType);
                        if (foundN != null)
                        {
                            foreach (var o in foundN)
                            {
                                if (o is Component c && c != null && c.gameObject != null)
                                {
                                    if (c is Behaviour beh && beh.enabled)
                                        beh.enabled = false;
                                    notifsOff++;
                                }
                            }
                        }
                    }
                    FFAArenaLite.Plugin.Log?.LogInfo($"FFA: Deactivated {flagsOff} FlagController and {notifsOff} CastleFlagCapturedNotifier objects.");
                }
                catch (Exception cleanEx)
                {
                    FFAArenaLite.Plugin.Log?.LogWarning($"FFA: Flag cleanup after StartGameActual failed: {cleanEx}");
                }

                // Ensure ctfoverlay is present and get its Image target for sprite swap
                var ctfoverlayField2 = AccessTools.Field(mmType, "ctfoverlay");
                var ctf2 = ctfoverlayField2?.GetValue(__instance) as CanvasGroup;
                if (ctf2 == null)
                {
                    FFAArenaLite.Plugin.Log?.LogWarning("FFA: ctfoverlay CanvasGroup not found; cannot apply FFA overlay.");
                    return;
                }
                var overlayGo = ctf2.gameObject;
                if (overlayGo == null)
                {
                    FFAArenaLite.Plugin.Log?.LogWarning("FFA: ctfoverlay GameObject missing; cannot apply FFA overlay.");
                    return;
                }
                if (!overlayGo.activeSelf) overlayGo.SetActive(true);
                ctf2.alpha = 0f;
                ctf2.interactable = false;
                ctf2.blocksRaycasts = false;
                // Prefer to update an existing RawImage (most likely in prefab) to avoid duplicates
                var rawExisting = overlayGo.GetComponentInChildren<RawImage>(true);
                var img = overlayGo.GetComponentInChildren<Image>(true);
                if (rawExisting == null && img == null)
                {
                    // Create a child Image only if neither RawImage nor Image exists
                    var imgGo = new GameObject("FFAOverlayImage", typeof(RectTransform), typeof(Image));
                    imgGo.transform.SetParent(overlayGo.transform, false);
                    imgGo.transform.SetAsLastSibling();
                    var rtNew = imgGo.GetComponent<RectTransform>();
                    if (rtNew != null)
                    {
                        rtNew.anchorMin = Vector2.zero;
                        rtNew.anchorMax = Vector2.one;
                        rtNew.pivot = new Vector2(0.5f, 0.5f);
                        rtNew.offsetMin = Vector2.zero;
                        rtNew.offsetMax = Vector2.zero;
                        rtNew.localScale = Vector3.one;
                    }
                    img = imgGo.GetComponent<Image>();
                    if (img != null) img.raycastTarget = false;
                    FFAArenaLite.Plugin.Log?.LogInfo("FFA: Created FFAOverlayImage under ctfoverlay for overlay sprite.");
                }

                // Load embedded PNG into a Sprite
                var asm = Assembly.GetExecutingAssembly();
                string[] names = null;
                try { names = asm.GetManifestResourceNames(); } catch { names = Array.Empty<string>(); }
                string resName = null;
                foreach (var n in names)
                {
                    if (n.EndsWith(".Resources.ffaoverlay.png", StringComparison.OrdinalIgnoreCase) ||
                        n.EndsWith("ffaoverlay.png", StringComparison.OrdinalIgnoreCase))
                    { resName = n; break; }
                }
                if (string.IsNullOrEmpty(resName))
                {
                    FFAArenaLite.Plugin.Log?.LogWarning("FFA: ffaoverlay.png resource not found in assembly manifest.");
                    return;
                }
                byte[] bytes;
                using (var s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) { FFAArenaLite.Plugin.Log?.LogWarning("FFA: Failed to open ffaoverlay.png resource stream."); return; }
                    using (var ms = new System.IO.MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
                }
                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                bool ok = tex.LoadImage(bytes);
                if (!ok)
                {
                    FFAArenaLite.Plugin.Log?.LogWarning("FFA: LoadImage failed for ffaoverlay.png.");
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

                // Apply to a single existing renderer to avoid duplicates
                if (rawExisting != null)
                {
                    rawExisting.texture = tex;
                    rawExisting.color = Color.white;
                    rawExisting.raycastTarget = false;
                    var rt = rawExisting.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        rt.localScale = Vector3.one;
                    }
                }
                else if (img != null)
                {
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    img.raycastTarget = false;
                    var rt = img.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        rt.localScale = Vector3.one;
                    }
                }

                FFAArenaLite.Plugin.Log?.LogInfo("FFA: ctfoverlay sprite swapped to ffaoverlay.png and reactivated for fade.");
            }
            catch (Exception e)
            {
                FFAArenaLite.Plugin.Log?.LogWarning($"FFA: StartGameActual Postfix overlay swap failed: {e}");
            }
        }
    }
}
