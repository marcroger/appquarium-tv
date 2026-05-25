using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Entry point for the TvScene (WebGL Cast receiver build).
///
/// v2 (Addressables): on INIT, parses needed asset keys from state,
/// loads them on demand via Addressables, then initializes the aquarium.
/// Only the assets active in the user's tank are downloaded.
/// </summary>
public class TvSceneBootstrap : MonoBehaviour
{
    public static TvSceneBootstrap Instance { get; private set; }

    [Header("TV Scene")]
    public bool alwaysAmbient = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb != null)
        {
            amb.alwaysAmbient      = alwaysAmbient;
            amb.autoFollowRealTime = false;
        }

        var uiGo = GameObject.Find("UIManager");
        if (uiGo != null) uiGo.SetActive(false);

        Debug.Log("[TvSceneBootstrap] ✅ TV scene ready — waiting for Cast INIT.");
    }

    // ── Public API (called from CastReceiver) ─────────────────────────────────

    public void InitializeFromState(TvAquariumState state)
    {
        if (state == null) { Debug.LogWarning("[TvScene] INIT received null state."); return; }
        Debug.Log($"[TvScene] INIT — fish:{state.activeFish?.Count ?? 0} bg:{state.bgId}");
        StartCoroutine(LoadAndInitializeCoroutine(state));
    }

    public void ApplyUpdate(TvUpdateMessage upd)
    {
        if (upd == null) return;
        Debug.Log($"[TvScene] UPDATE type={upd.type} value={upd.value}");

        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        switch (upd.type)
        {
            case "ambient": ApplyAmbientMode(upd.value); break;

            case "speed":
                if (float.TryParse(upd.value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float spd))
                    mgr.FishSpeedMultiplier = spd;
                break;

            case "feed":
                var bounds = mgr.tankController.GetTankBounds();
                FoodManager.Instance?.SpawnFood(
                    new Vector3(bounds.center.x, bounds.max.y - 0.5f, 0f));
                break;

            case "refresh":
                Debug.Log("[TvScene] Refresh requested — waiting for new INIT.");
                break;
        }
    }

    // ── Addressables loading coroutine ────────────────────────────────────────

    private IEnumerator LoadAndInitializeCoroutine(TvAquariumState state)
    {
        // ── 1. Collect keys ──────────────────────────────────────────────────
        var fishKeys = new HashSet<string>();
        if (state.activeFish != null)
            foreach (var f in state.activeFish)
                if (!string.IsNullOrEmpty(f.speciesId)) fishKeys.Add(f.speciesId);

        var decoKeys = ParseDecoItemIds(state.decoJson);

        Debug.Log($"[TvScene] Loading assets — fish:{fishKeys.Count} decos:{decoKeys.Count}");

        // ── 2. Launch all loads in parallel ──────────────────────────────────
        var fishHandles = new List<AsyncOperationHandle<FishData>>();
        foreach (var key in fishKeys)
            fishHandles.Add(Addressables.LoadAssetAsync<FishData>(key));

        var decoHandles = new List<AsyncOperationHandle<DecorationData>>();
        foreach (var key in decoKeys)
            decoHandles.Add(Addressables.LoadAssetAsync<DecorationData>(key));

        // ── 3. Wait for all to complete ───────────────────────────────────────
        foreach (var h in fishHandles) yield return h;
        foreach (var h in decoHandles) yield return h;

        // ── 4. Collect results ────────────────────────────────────────────────
        var fishData = new List<FishData>();
        foreach (var h in fishHandles)
        {
            if (h.Status == AsyncOperationStatus.Succeeded)
                fishData.Add(h.Result);
            else
                Debug.LogWarning($"[TvScene] Failed to load FishData: {h.DebugName}");
        }

        var decoData = new List<DecorationData>();
        foreach (var h in decoHandles)
        {
            if (h.Status == AsyncOperationStatus.Succeeded)
                decoData.Add(h.Result);
            else
                Debug.LogWarning($"[TvScene] Failed to load DecoData: {h.DebugName}");
        }

        Debug.Log($"[TvScene] Assets loaded — fish:{fishData.Count} decos:{decoData.Count}");

        // ── 5. Initialize aquarium with loaded data ───────────────────────────
        var mgr = AquariumManager.Instance;
        if (mgr == null) { Debug.LogError("[TvScene] AquariumManager not found."); yield break; }

        mgr.InitializeFromCastState(state, fishData, decoData);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HashSet<string> ParseDecoItemIds(string decoJson)
    {
        var keys = new HashSet<string>();
        if (string.IsNullOrEmpty(decoJson) || decoJson == "{}") return keys;
        try
        {
            var wrapper = JsonUtility.FromJson<DecoPlacementList>(decoJson);
            if (wrapper?.items == null) return keys;
            foreach (var p in wrapper.items)
            {
                if (string.IsNullOrEmpty(p.instanceId)) continue;
                // instanceId = "itemId_n" → strip the trailing "_n"
                var lastUnderscore = p.instanceId.LastIndexOf('_');
                var itemId = lastUnderscore > 0
                    ? p.instanceId.Substring(0, lastUnderscore)
                    : p.instanceId;
                if (!string.IsNullOrEmpty(itemId)) keys.Add(itemId);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[TvScene] Deco JSON parse error: {e.Message}");
        }
        return keys;
    }

    private void ApplyAmbientMode(string mode)
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb == null) return;
        switch (mode)
        {
            case "day":    amb.SetDay();    break;
            case "sunset": amb.SetSunset(); break;
            case "night":  amb.SetNight();  break;
        }
    }
}
