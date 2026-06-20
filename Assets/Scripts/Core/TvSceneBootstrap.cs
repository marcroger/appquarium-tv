using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

/// <summary>
/// Entry point for the TvScene (WebGL Cast receiver build).
///
/// v2 (Addressables): on INIT, parses needed asset keys from state,
/// loads them on demand via Addressables, then initializes the aquarium.
/// Only the assets active in the user's tank are downloaded.
/// Shows a loading overlay (logo + spinner + progress counter) during loading.
/// </summary>
public class TvSceneBootstrap : MonoBehaviour
{
    public static TvSceneBootstrap Instance { get; private set; }

    [Header("TV Scene")]
    public bool alwaysAmbient = true;

    // Addressable handle registries — track handles so we can release memory on reconnect or remove
    private readonly List<AsyncOperationHandle<FishData>>                    _initFishHandles    = new();
    private readonly List<AsyncOperationHandle<DecorationData>>              _initDecoHandles    = new();
    private readonly Dictionary<string, AsyncOperationHandle<FishData>>      _runtimeFishHandles = new();
    private readonly Dictionary<string, AsyncOperationHandle<DecorationData>> _runtimeDecoHandles = new();

    // Loading overlay
    private CanvasGroup   _overlayGroup;
    private Text          _counterText;
    private RectTransform _spinnerRect;
    private bool          _spinning;
    private Coroutine     _fadeRoutine;

    static readonly Color C_BG     = new Color(0.024f, 0.051f, 0.102f, 1f); // #060D1A
    static readonly Color C_ACCENT = new Color(0.0f,   0.85f,  1.0f,   1f); // cyan
    static readonly Color C_MUTED  = new Color(0.6f,   0.6f,   0.6f,   1f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.AddComponent<TvLayerDebug>();
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

        Application.targetFrameRate = 30; // stable 30fps on Cast device > choppy 60fps

        // renderScale < 1 → URP renderiza a menos resolución (gran ahorro de fill-rate
        // GPU en el Mali-G31, que va a ~7fps). Se hace en runtime porque el asset URP no
        // está como fichero editable en el proyecto. 0.7 = 49% de píxeles, leve pérdida de
        // nitidez a cambio de framerate. Ajustable según lo que dé el device.
        // Lookup robusto: el asset activo puede venir del quality level o del default global.
        // Debug.Log (no JsBridge) porque esto corre muy temprano, antes de que el bridge esté listo.
        var rpAsset = QualitySettings.renderPipeline
                   ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (rpAsset is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urpAsset)
        {
            urpAsset.renderScale = 0.7f;
            Debug.Log($"[TvScene] renderScale set to {urpAsset.renderScale}");
        }
        else
        {
            Debug.Log($"[TvScene] renderScale SKIP — rp={(rpAsset == null ? "null" : rpAsset.GetType().Name)}");
        }

        BuildLoadingOverlay();

        Debug.Log("[TvSceneBootstrap] TV scene ready — waiting for Cast INIT.");
    }

    void Update()
    {
        if (_spinning && _spinnerRect != null)
            _spinnerRect.Rotate(0f, 0f, -200f * Time.deltaTime);
    }

    // ── Public API (called from CastReceiver) ─────────────────────────────────

    public void InitializeFromState(TvAquariumState state)
    {
        if (state == null) { Debug.LogWarning("[TvScene] INIT received null state."); JsBridge.Log("ERR: INIT state is null — JSON parse failed!"); return; }
        Debug.Log($"[TvScene] INIT — fish:{state.activeFish?.Count ?? 0} bg:{state.bgId}");
        JsBridge.Log($"INIT: fish={state.activeFish?.Count ?? 0} bg={state.bgId} tank={state.selectedTankId}");
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

            case "feed":    mgr.FeedAll(); break;

            case "startle":
                var sBounds = mgr.tankController.GetTankBounds();
                mgr.StartleAll(sBounds.center);
                break;

            case "refresh":
                Debug.Log("[TvScene] Refresh requested — waiting for new INIT.");
                break;

            // ── Real-time asset updates ──────────────────────────────────────
            case "add_fish":    StartCoroutine(AddFishAsync(upd.value));   break;
            case "remove_fish": RemoveFish(upd.value);                     break;
            case "add_deco":    StartCoroutine(AddDecoAsync(upd.value));   break;
            case "remove_deco": RemoveDeco(upd.value);                     break;
            case "change_bg":   ChangeBg(upd.value);                       break;
            case "change_sub":  ChangeSub(upd.value);                      break;
            case "change_light":ChangeLight(upd.value);                    break;
        }
    }

    // ── Addressables loading coroutine ────────────────────────────────────────

    private IEnumerator LoadAndInitializeCoroutine(TvAquariumState state)
    {
        ShowLoadingOverlay();

        // ── 0. Release handles from previous session (Cast reconnect) ─────────
        foreach (var h in _initFishHandles)    Addressables.Release(h);
        foreach (var h in _initDecoHandles)    Addressables.Release(h);
        foreach (var h in _runtimeFishHandles.Values) Addressables.Release(h);
        foreach (var h in _runtimeDecoHandles.Values) Addressables.Release(h);
        _initFishHandles.Clear();    _initDecoHandles.Clear();
        _runtimeFishHandles.Clear(); _runtimeDecoHandles.Clear();

        // ── 1. Collect keys ──────────────────────────────────────────────────
        var fishKeys = new HashSet<string>();
        if (state.activeFish != null)
            foreach (var f in state.activeFish)
                if (!string.IsNullOrEmpty(f.speciesId)) fishKeys.Add(f.speciesId);

        var decoKeys = ParseDecoItemIds(state.decoJson);

        Debug.Log($"[TvScene] Loading assets — fish:{fishKeys.Count} decos:{decoKeys.Count}");
        JsBridge.Log($"Loading: {fishKeys.Count} fish, {decoKeys.Count} decos");

        int total = fishKeys.Count + decoKeys.Count;
        int done  = 0;
        UpdateProgress(done, total);

        // ── 2. Launch all loads in parallel ──────────────────────────────────
        var fishHandles = new List<AsyncOperationHandle<FishData>>();
        foreach (var key in fishKeys)
            fishHandles.Add(Addressables.LoadAssetAsync<FishData>(key));

        var decoHandles = new List<AsyncOperationHandle<DecorationData>>();
        foreach (var key in decoKeys)
            decoHandles.Add(Addressables.LoadAssetAsync<DecorationData>(key));

        // ── 3. Wait for each, update progress counter ─────────────────────────
        foreach (var h in fishHandles) { yield return h; UpdateProgress(++done, total); }
        foreach (var h in decoHandles) { yield return h; UpdateProgress(++done, total); }

        // ── 4. Collect results ────────────────────────────────────────────────
        var fishData = new List<FishData>();
        foreach (var h in fishHandles)
        {
            if (h.Status == AsyncOperationStatus.Succeeded)
                fishData.Add(h.Result);
            else
            {
                Debug.LogWarning($"[TvScene] Failed to load FishData: {h.DebugName}");
                JsBridge.Log($"ERR fish load FAILED: {h.DebugName} ({h.OperationException?.Message ?? "unknown"})");
            }
        }

        var decoData = new List<DecorationData>();
        foreach (var h in decoHandles)
        {
            if (h.Status == AsyncOperationStatus.Succeeded)
                decoData.Add(h.Result);
            else
            {
                Debug.LogWarning($"[TvScene] Failed to load DecoData: {h.DebugName}");
                JsBridge.Log($"ERR deco load FAILED: {h.DebugName} ({h.OperationException?.Message ?? "unknown"})");
            }
        }

        int fishFailed = fishHandles.Count - fishData.Count;
        int decoFailed = decoHandles.Count - decoData.Count;
        Debug.Log($"[TvScene] Assets loaded — fish:{fishData.Count}/{fishHandles.Count} decos:{decoData.Count}/{decoHandles.Count}");
        JsBridge.Log($"Loaded: fish={fishData.Count}/{fishHandles.Count} decos={decoData.Count}/{decoHandles.Count}" +
            (fishFailed + decoFailed > 0 ? $" FAILED={fishFailed+decoFailed}" : " OK"));

        // Store handles so we can release them on reconnect (next INIT)
        _initFishHandles.AddRange(fishHandles);
        _initDecoHandles.AddRange(decoHandles);

        // ── 5. Initialize aquarium with loaded data ───────────────────────────
        var mgr = AquariumManager.Instance;
        if (mgr == null)
        {
            Debug.LogError("[TvScene] AquariumManager not found.");
            JsBridge.Log("ERROR: AquariumManager.Instance is null!");
            yield break;
        }

        JsBridge.Log("Calling InitializeFromCastStateAsync...");
        yield return StartCoroutine(mgr.InitializeFromCastStateAsync(state, fishData, decoData));
        JsBridge.Log($"InitDone: fish={mgr.fishSpawner?.ActiveFish?.Count ?? -1}");

        HideLoadingOverlay();
    }

    // ── Loading overlay ───────────────────────────────────────────────────────

    private void BuildLoadingOverlay()
    {
        var canvasGo = new GameObject("LoadingOverlay");

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGo.AddComponent<GraphicRaycaster>();

        _overlayGroup                  = canvasGo.AddComponent<CanvasGroup>();
        _overlayGroup.alpha            = 0f;
        _overlayGroup.interactable     = false;
        _overlayGroup.blocksRaycasts   = false;

        // Full-screen background
        var bg = MakeStretchChild(canvasGo.transform, "BG");
        bg.gameObject.AddComponent<Image>().color = C_BG;
        var content = bg.transform;

        // Logo text (no sprite asset — use text fallback)
        AddText(content, "Logo", "APPQUARIUM", 64, FontStyle.Bold, C_ACCENT,
            new Vector2(0f, 200f), new Vector2(700f, 90f));

        // Spinner: filled arc that rotates in Update
        var spinnerGo = new GameObject("Spinner");
        spinnerGo.transform.SetParent(content, false);
        _spinnerRect = spinnerGo.AddComponent<RectTransform>();
        _spinnerRect.anchorMin        = new Vector2(0.5f, 0.5f);
        _spinnerRect.anchorMax        = new Vector2(0.5f, 0.5f);
        _spinnerRect.anchoredPosition = new Vector2(0f, 60f);
        _spinnerRect.sizeDelta        = new Vector2(72f, 72f);
        var arc = spinnerGo.AddComponent<Image>();
        arc.sprite     = MakeRingSprite();
        arc.color      = C_ACCENT;
        arc.type       = Image.Type.Filled;
        arc.fillMethod = Image.FillMethod.Radial360;
        arc.fillAmount = 0.75f;

        AddText(content, "LoadingText", "Cargando acuario…", 32, FontStyle.Normal,
            Color.white, new Vector2(0f, -80f), new Vector2(700f, 44f));

        _counterText = AddText(content, "CounterText", "", 24, FontStyle.Normal,
            C_MUTED, new Vector2(0f, -140f), new Vector2(700f, 36f));

        canvasGo.SetActive(false);
    }

    private RectTransform MakeStretchChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private Text AddText(Transform parent, string name, string content,
        int size, FontStyle style, Color color, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = sizeDelta;
        var t = go.AddComponent<Text>();
        t.text      = content;
        t.fontSize  = size;
        t.fontStyle = style;
        t.color     = color;
        t.alignment = TextAnchor.MiddleCenter;
        return t;
    }

    private Sprite MakeRingSprite()
    {
        // 64×64 ring texture used as the spinner arc base
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = new Vector2(size * 0.5f, size * 0.5f);
        const float r = 28f, thickness = 5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - r) / thickness);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void ShowLoadingOverlay()
    {
        if (_overlayGroup == null) return;
        _overlayGroup.gameObject.SetActive(true);
        _spinning = true;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOverlay(0f, 1f, 0.3f));
    }

    private void HideLoadingOverlay()
    {
        if (_overlayGroup == null) return;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeAndDeactivate());
    }

    private IEnumerator FadeAndDeactivate()
    {
        yield return FadeOverlay(1f, 0f, 0.5f);
        _spinning = false;
        _overlayGroup.gameObject.SetActive(false);
    }

    private IEnumerator FadeOverlay(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _overlayGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        _overlayGroup.alpha = to;
    }

    private void UpdateProgress(int done, int total)
    {
        if (_counterText == null) return;
        _counterText.text = total > 0 ? $"{done} / {total} cargados" : "";
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

    // ── Real-time asset update handlers ───────────────────────────────────────

    private IEnumerator AddFishAsync(string jsonValue)
    {
        var payload = SafeFromJson<TvAddFishPayload>(jsonValue);
        if (payload == null || string.IsNullOrEmpty(payload.speciesId)) yield break;

        var mgr = AquariumManager.Instance;
        if (mgr == null) yield break;

        // Reuse already-loaded data if available from INIT catalog
        FishData data = mgr.allFishCatalog.Find(d => d.itemId == payload.speciesId);
        if (data == null)
        {
            var h = Addressables.LoadAssetAsync<FishData>(payload.speciesId);
            yield return h;
            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                JsBridge.Log($"ERR add_fish: load failed {payload.speciesId}");
                yield break;
            }
            data = h.Result;
            _runtimeFishHandles[payload.speciesId] = h;
            mgr.allFishCatalog.Add(data);
        }

        var bounds = mgr.tankController.GetTankBounds();
        var save   = new OwnedFishSave
        {
            uid       = System.Guid.NewGuid().ToString(),
            speciesId = payload.speciesId,
            nickname  = payload.nickname ?? ""
        };
        var agent = mgr.fishSpawner.SpawnFish(data, bounds, save);
        if (agent != null) { agent.SetNickname(save.nickname); agent.SetUid(save.uid); }
        JsBridge.Log($"add_fish: {payload.speciesId} spawned");
    }

    private void RemoveFish(string speciesId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        int count = mgr.fishSpawner.DespawnBySpecies(speciesId);
        JsBridge.Log($"remove_fish: {speciesId} (removed={count})");

        // Release bundle only if it was runtime-loaded and no instances remain
        if (_runtimeFishHandles.TryGetValue(speciesId, out var h))
        {
            bool anyLeft = false;
            foreach (var f in mgr.fishSpawner.ActiveFish)
                if (f != null && f.Data?.itemId == speciesId) { anyLeft = true; break; }
            if (!anyLeft)
            {
                Addressables.Release(h);
                _runtimeFishHandles.Remove(speciesId);
                mgr.allFishCatalog.RemoveAll(d => d.itemId == speciesId);
            }
        }
    }

    private IEnumerator AddDecoAsync(string jsonValue)
    {
        var payload = SafeFromJson<TvAddDecoPayload>(jsonValue);
        if (payload == null || string.IsNullOrEmpty(payload.itemId)) yield break;

        var mgr = AquariumManager.Instance;
        if (mgr == null) yield break;
        var placer = mgr.tankController.GetComponent<DecorationPlacer>();
        if (placer == null) yield break;

        DecorationData data = mgr.allDecoCatalog.Find(d => d.itemId == payload.itemId);
        if (data == null)
        {
            var h = Addressables.LoadAssetAsync<DecorationData>(payload.itemId);
            yield return h;
            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                JsBridge.Log($"ERR add_deco: load failed {payload.itemId}");
                yield break;
            }
            data = h.Result;
            if (!_runtimeDecoHandles.ContainsKey(payload.itemId))
            {
                _runtimeDecoHandles[payload.itemId] = h;
                mgr.allDecoCatalog.Add(data);
            }
            else
            {
                Addressables.Release(h); // already tracked from a previous add
            }
        }

        placer.PlaceAt(data, payload.position,
            flipped:     payload.flipped,
            rotationY:   payload.rotationY,
            scaleFactor: payload.scaleFactor > 0f ? payload.scaleFactor : 1f,
            fromSave:    true,
            instanceId:  string.IsNullOrEmpty(payload.instanceId) ? null : payload.instanceId);

        JsBridge.Log($"add_deco: {payload.itemId} at {payload.position:F1}");
    }

    private void RemoveDeco(string instanceId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var placer = mgr.tankController.GetComponent<DecorationPlacer>();
        if (placer == null) return;

        // Derive itemId by stripping the trailing _N index from instanceId
        int    lastUs = instanceId.LastIndexOf('_');
        string itemId = lastUs > 0 ? instanceId.Substring(0, lastUs) : instanceId;

        bool ok = placer.Remove(instanceId);
        JsBridge.Log($"remove_deco: {instanceId} (ok={ok})");

        // Release bundle when the last instance of this itemId is gone
        if (ok && !placer.IsPlaced(itemId) && _runtimeDecoHandles.TryGetValue(itemId, out var h))
        {
            Addressables.Release(h);
            _runtimeDecoHandles.Remove(itemId);
            mgr.allDecoCatalog.RemoveAll(d => d.itemId == itemId);
        }
    }

    private void ChangeBg(string bgId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var bg = mgr.tankController.GetComponent<TankBackground>();
        if (bg != null) bg.SetPreset(bgId);
        if (mgr.SaveData != null) mgr.SaveData.selectedBgId = bgId;
        JsBridge.Log($"change_bg: {bgId}");
    }

    private void ChangeSub(string subId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var placer = mgr.tankController.GetComponent<DecorationPlacer>();
        if (placer != null) placer.SetSubstrate(subId);
        if (mgr.SaveData != null) mgr.SaveData.selectedSubId = subId;
        JsBridge.Log($"change_sub: {subId}");
    }

    private void ChangeLight(string lightId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var lighting = mgr.tankController.GetComponent<TankLightingController>();
        if (lighting != null) lighting.SetPreset(lightId);
        if (mgr.SaveData != null) mgr.SaveData.lightPresetId = lightId;
        JsBridge.Log($"change_light: {lightId}");
    }

    private static T SafeFromJson<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try   { return JsonUtility.FromJson<T>(json); }
        catch { return null; }
    }
}
