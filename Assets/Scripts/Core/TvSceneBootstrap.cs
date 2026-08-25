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

    [Header("Diagnóstico")]
    [Tooltip("Overlay amarillo con PostFX/CAM/Lighting/BG sobre el acuario. OFF en producción.")]
    [SerializeField] private bool showDebugOverlay = false;

    static readonly Color C_BG     = new Color(0.024f, 0.051f, 0.102f, 1f); // #060D1A
    static readonly Color C_ACCENT = new Color(0.0f,   0.85f,  1.0f,   1f); // cyan
    static readonly Color C_MUTED  = new Color(0.6f,   0.6f,   0.6f,   1f);

    private Coroutine _loadRoutine;

    // ⚠ 2026-08-15 — esto vivía en Start() y era una carrera.
    // AmbientModeController está serializado en la escena con autoFollowRealTime:1, y es
    // este script quien lo corrige. Los dos usaban Start(), sin Script Execution Order
    // definido: si ganaba el controller, aplicaba el modo según la hora real SIN actualizar
    // CurrentMode (se queda en Day), y el INIT posterior con ambientMode="day" salía por el
    // early-return de SetMode → casteabas a las 22:00 y la tele se quedaba en modo noche
    // con el móvil diciendo día. Awake() SIEMPRE precede a cualquier Start().
    void AplicarAjustesAmbiente()
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb != null)
        {
            amb.alwaysAmbient      = alwaysAmbient;
            amb.autoFollowRealTime = false;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Los bundles ya no salen de un bucket público: los sirve el Worker desde uno
        // privado y sin cabecera devuelve 401. Va aquí, en Awake, porque tiene que estar
        // puesto antes del primer LoadAssetAsync (que ocurre al llegar el INIT de Cast).
        TvBundleAuth.Install();

        // ⚠ 2026-08-11 — el overlay amarillo (PostFX/CAM/Lighting/BG) tapaba la esquina
        // superior izquierda del acuario EN PRODUCCIÓN: se añadía siempre, sin condición.
        // Las llamadas a TvLayerDebug.Set() repartidas por el código son inofensivas si el
        // componente no existe (Set() hace early return con Instance == null), así que
        // basta con no añadirlo. Para recuperarlo en una sesión de diagnóstico: poner
        // showDebugOverlay a true en el inspector de TvSceneBootstrap en la escena.
        if (showDebugOverlay) gameObject.AddComponent<TvLayerDebug>();

        // Sombras de contacto de los peces. Va aquí y no en FishSpawner porque ese fichero
        // se sincroniza desde el móvil y el siguiente sync se llevaría el cambio por delante.
        gameObject.AddComponent<TvFishShadows>();
    }

    void Start()
    {

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
            // Sello de configuración del pipeline por el canal Cast. Existe porque el device
            // CACHEA el player (`max-age=3600` en Build/) y sin esto no hay forma de saber qué
            // build está corriendo de verdad: el 21-ago comparé dos tandas de memoria creyendo
            // que eran builds distintos y era el mismo, servido de caché las dos veces.
            JsBridge.Log($"RP: {urpAsset.name} scale={urpAsset.renderScale:F2} " +
                         $"hdr={(urpAsset.supportsHDR ? "ON" : "OFF")} " +
                         $"msaa={urpAsset.msaaSampleCount} " +
                         $"sombras={(urpAsset.supportsMainLightShadows ? "ON" : "OFF")}");
        }
        else
        {
            Debug.Log($"[TvScene] renderScale SKIP — rp={(rpAsset == null ? "null" : rpAsset.GetType().Name)}");
        }

        // SMAA Low: 1 extra pass, sharper edges than no-AA. MSAA is broken on WebGL (Unity 6 bug).
        var cam = Camera.main;
        if (cam != null)
        {
            var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (camData != null)
            {
                // ⚠⚠ 2026-08-21: SIN esta línea no hay post-proceso, aunque exista el pipeline y
                // aunque PostProcessingSetup construya su Volume. En URP `renderPostProcessing`
                // viene en FALSE por defecto, y aquí nadie lo encendía: este mismo bloque llevaba
                // meses tocando la componente para poner SMAA y se dejaba lo importante.
                // El síntoma es el peor de todos: no falla nada, simplemente la tele se ve plana.
                camData.renderPostProcessing = true;

                camData.antialiasing        = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                camData.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.Low;
                Debug.Log("[TvScene] postFX ON + SMAA Low enabled");
                JsBridge.Log("POSTFX: activado en la cámara (renderPostProcessing=true)");
            }
            else
            {
                JsBridge.Log("ERR: la cámara no tiene UniversalAdditionalCameraData → sin post-proceso");
            }
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

        // Fase 2: si el sender manda su JWT, manda ese. Si no viene -- que es el caso hoy, y
        // el de cualquier app ya instalada -- sigue valiendo el token constante del receiver.
        TvBundleAuth.SetSessionToken(state.castJwt);

        // ⚠ 2026-08-15 — parar la carga anterior antes de arrancar otra.
        // Si el sender caía y reconectaba MIENTRAS se bajaban bundles (carga fría ~40 s,
        // muy probable), corrían dos corrutinas a la vez: la nueva liberaba handles que la
        // vieja aún iba a usar, se hacía doble DespawnAll + doble spawn, y el
        // HideLoadingOverlay de la primera destapaba la pantalla a media carga de la segunda.
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadAndInitializeCoroutine(state));
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

            // ⚠ Estos cuatro no reportaban NADA por el canal Cast (2026-08-21). Un fallo aquí
            // era invisible: el `Debug.Log` no viaja por Cast y el resto son efectos de
            // movimiento, que no se ven en una captura. Ahora cada uno confirma lo que hizo
            // Y sobre cuántos peces, que es lo que permite distinguir «no llegó el mensaje» de
            // «llegó pero no había a quién aplicárselo».
            case "speed":
                if (float.TryParse(upd.value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float spd))
                {
                    mgr.FishSpeedMultiplier = spd;
                    JsBridge.Log($"speed: x{spd:F2} aplicado a {mgr.fishSpawner?.ActiveFish?.Count ?? -1} peces");
                }
                else JsBridge.Log($"ERR speed: valor ilegible '{upd.value}'");
                break;

            case "feed":
                mgr.FeedAll();
                JsBridge.Log($"feed: comida soltada ({mgr.fishSpawner?.ActiveFish?.Count ?? -1} peces en el tanque)");
                break;

            case "startle":
                var sBounds = mgr.tankController.GetTankBounds();
                mgr.StartleAll(sBounds.center);
                JsBridge.Log($"startle: {mgr.fishSpawner?.ActiveFish?.Count ?? -1} peces espantados desde {sBounds.center:F1}");
                break;

            case "refresh":
                Debug.Log("[TvScene] Refresh requested — waiting for new INIT.");
                JsBridge.Log("refresh: recibido — esperando un INIT nuevo");
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

        // ── 2. Load serially — one bundle at a time to avoid simultaneous LZ4
        //    decompression peak that can crash the WASM heap on memory-limited devices.
        //    Per-bundle logs let us pinpoint which bundle was active when a crash occurs.
        var fishHandles = new List<AsyncOperationHandle<FishData>>();
        foreach (var key in fishKeys)
        {
            JsBridge.Log($"BDL {done+1}/{total} fish: {key}");
            var h = Addressables.LoadAssetAsync<FishData>(key);
            yield return h;
            fishHandles.Add(h);
            UpdateProgress(++done, total);
            JsBridge.Log($"BDL {done}/{total} {(h.Status == AsyncOperationStatus.Succeeded ? "OK" : "FAIL")}: {key}");
        }

        var decoHandles = new List<AsyncOperationHandle<DecorationData>>();
        foreach (var key in decoKeys)
        {
            JsBridge.Log($"BDL {done+1}/{total} deco: {key}");
            var h = Addressables.LoadAssetAsync<DecorationData>(key);
            yield return h;
            decoHandles.Add(h);
            UpdateProgress(++done, total);
            JsBridge.Log($"BDL {done}/{total} {(h.Status == AsyncOperationStatus.Succeeded ? "OK" : "FAIL")}: {key}");
        }

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

        // Trasvasar del catálogo JSON los campos que los SOs de los bundles no traen
        // (`hasBioLuminescence`). Sin esto la bioluminiscencia es código muerto: ninguno de los
        // 54 SOs tiene el flag. Ver `TvDecoCatalogPatch` y la memoria `pending_biolum`.
        TvDecoCatalogPatch.Aplicar(decoData);

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

        // Aspecto del agua: se publica AQUI, con el acuario ya montado, porque el color de la
        // niebla sale del preset de fondo y antes de esto TankBackground puede no existir.
        PublicarAspectoDelAgua();

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

    /// <summary>
    /// ⚠ 2026-08-24 — `ambient` era el ÚNICO de los 11 UPDATE que no reportaba nada por el
    /// canal Cast. La auditoría del 21-ago le puso confirmación a `speed`, `feed`, `startle`
    /// y `refresh` y se dejó éste fuera, y tenía TRES salidas mudas: sin controlador, con un
    /// modo que no encaja, y "ya estaba en ese modo". Resultado: del log de una sesión en la
    /// tele no se podía saber si el ciclo día/noche había funcionado — que es justo lo que
    /// hizo falta averiguar hoy. Ahora dice qué hizo, desde qué modo, y si no hizo nada.
    /// </summary>
    private void ApplyAmbientMode(string mode)
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb == null) { JsBridge.Log($"ERR ambient: no hay AmbientModeController en la escena (pedido '{mode}')"); return; }

        var previo = amb.CurrentMode;
        switch (mode)
        {
            case "day":    amb.SetDay();    break;
            case "sunset": amb.SetSunset(); break;
            case "night":  amb.SetNight();  break;
            default:       JsBridge.Log($"ERR ambient: modo desconocido '{mode}' (day|sunset|night)"); return;
        }

        JsBridge.Log(previo == amb.CurrentMode
            ? $"ambient: {mode} — ya estaba en ese modo, sin cambio"
            : $"ambient: {previo} → {amb.CurrentMode}");

        // Sonda: leer el estado real del render cuando el fundido haya terminado.
        StartCoroutine(SondaDeRender(mode));
    }



    // ── ASPECTO DEL AGUA (2026-08-25) ────────────────────────────────────────
    // Publica el tono de los peces y la niebla de agua. Estos son los valores POR DEFECTO
    // del producto; el mensaje `FOG` sigue pudiendo cambiarlos en caliente sin rebuild.
    //
    // POR QUE ESTOS NUMEROS: medido en la tele, los peces iban a croma C* 42,6 contra 23,1
    // del agua que los rodea (1,8x) y L* 59 contra 47, mientras las decos ya estaban
    // integradas (25,5). Por eso el tono sólo toca a los peces. Los valores salieron de un
    // barrido de 4 variantes sobre el device, elegidos por el user: los peces conservan su
    // color e identidad —que es de lo que vive el producto— pero dejan de parecer pegatinas.
    //
    // ⚠ La niebla usa el `surfaceTint` del preset de fondo activo, asi que se vuelve a
    // publicar en cada `change_bg`: cada fondo tiene su agua y con un color fijo la niebla
    // desentonaria en 10 de los 11 fondos.
    private const float TonoDesat  = 0.32f;   // elegido sobre el device
    private const float TonoDim    = 0.16f;
    private const float NieblaDens = 0.30f;   // suelo y peces
    // ⚠ 0 = las decos NO reciben niebla. Decision del user viendo la tele: con niebla
    // "pierden demasiado" (un ancla negra salia turquesa). No se puede resolver acotando el
    // rango de Z porque las decos se colocan a cualquier profundidad hasta ZDecoBack=+3,0.
    // Elegido por el user sobre la tele (barrido 0 / 0,25 / 0,50 / 1,00). El caso que manda
    // es el ANCLA, que es acromatica: con niebla completa su croma C* pasa de 1,9 a 17,3 y se
    // lee como turquesa, no como negra. Con 0,25 se queda en 8,4 — recibe agua, que es lo
    // fisicamente correcto, pero sigue siendo negra.
    // 🧭 El efecto depende MUCHO del color de la deco: la estrella azul cobalto apenas se
    // inmuta ni con niebla completa (14,6 → 12,3) porque su color ya esta cerca del agua.
    private const float DecoNiebla = 0.25f;

    public void PublicarAspectoDelAgua()
    {
        Shader.SetGlobalFloat(Shader.PropertyToID("_AqFishDesat"), TonoDesat);
        Shader.SetGlobalFloat(Shader.PropertyToID("_AqFishDim"),   TonoDim);
        Shader.SetGlobalFloat(Shader.PropertyToID("_AqDecoFogMul"), DecoNiebla);

        // Rango de la niebla: del frente del tanque al fondo del suelo. El TELON de fondo
        // vive en Z=+5,0 y queda FUERA a proposito — ya representa la lejania, y teñirlo del
        // color del agua le borraria la imagen.
        Shader.SetGlobalVector(Shader.PropertyToID("_AqWaterFogRange"),
                               new Vector4(DecorationPlacer.ZFront, DecorationPlacer.ZBack, 0f, 0f));

        var color = new Color(0.10f, 0.45f, 0.50f);   // por si no hay fondo todavia
        string origen = "default";
        var bg = FindFirstObjectByType<TankBackground>();
        if (bg != null)
        {
            foreach (var p in TankBackground.Presets)
            {
                if (p.id != bg.CurrentPresetId) continue;
                color = new Color(p.surfaceTint.r, p.surfaceTint.g, p.surfaceTint.b);
                origen = p.id;
                break;
            }
        }
        Shader.SetGlobalColor(Shader.PropertyToID("_AqWaterFog"),
                              new Color(color.r, color.g, color.b, NieblaDens));

        JsBridge.Log($"agua: niebla={color.r:F2}/{color.g:F2}/{color.b:F2} den={NieblaDens:F2} " +
                     $"z=[{DecorationPlacer.ZFront:F1},{DecorationPlacer.ZBack:F1}] " +
                     $"desat={TonoDesat:F2} dim={TonoDim:F2} deco={DecoNiebla:F2} ({origen})");
    }

    // ── SONDA DE RENDER (TV-only, 2026-08-25) ────────────────────────────────
    // ⚠⚠ POR QUÉ EXISTE: el ciclo día/noche funciona en Chrome y NO en el Chromecast.
    // Medido el 25-ago con el MISMO build y la MISMA escena (bg_classic + sub_sand):
    //
    //                    Chrome                    tele
    //   ancla sunset     0,831/0,593/0,437         0,966/1,010/1,074   ← no pasa nada
    //   arena sunset     0,862/0,636/0,478         1,001/1,001/1,001   ← no pasa nada
    //   ancla night      0,392/0,406/0,460         0,487/0,718/1,057   ← el AZUL SUBE
    //
    // El patrón de la tele en noche (R baja, B sube) es el de un VELO AZUL superpuesto,
    // no el de una multiplicación: o sea que lo que oscurece la tele de noche es algo que
    // ya existía, y el `_AqDecoDarken` no está llegando.
    //
    // El log `luz:` no puede distinguir el fallo porque reporta lo que el controlador
    // CALCULA, no lo que el material TIENE. Esta sonda lee el estado real del render:
    // el global tal y como lo ve el shader, y el material de cada renderer de la escena.
    //
    // Es la misma lección que ya está escrita en `AmbientModeController`: reportar el
    // MECANISMO, no sólo el efecto. Aquí se lleva un paso más allá — leer, no publicar.
    private System.Collections.IEnumerator SondaDeRender(string etiqueta)
    {
        // Esperar a que termine el fundido (~2-3 s) antes de leer nada.
        yield return new WaitForSeconds(4f);

        var gDeco = Shader.GetGlobalColor(Shader.PropertyToID("_AqDecoDarken"));
        var gPez  = Shader.GetGlobalColor(Shader.PropertyToID("_AqFishDarken"));
        JsBridge.Log($"sonda[{etiqueta}] GLOBAL deco={gDeco.r:F2}/{gDeco.g:F2}/{gDeco.b:F2} " +
                     $"pez={gPez.r:F2}/{gPez.g:F2}/{gPez.b:F2}");

        // ¿Los shaders que creemos usar existen y son soportados EN ESTE DEVICE?
        foreach (var n in new[] { "Appquarium/DecoLit", "Appquarium/FishUnlit", "Sprites/Default" })
        {
            var sh = Shader.Find(n);
            JsBridge.Log($"sonda[{etiqueta}] SHADER {n}: " +
                         (sh == null ? "NO ENCONTRADO" : $"ok supported={sh.isSupported}"));
        }

        // Estado REAL de los renderers que importan. Se listan por nombre para que se pueda
        // ver si el objeto que se tiñe es el mismo que se está viendo.
        int n_deco = 0;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var go = r.gameObject.name;
            bool esSuelo = go.StartsWith("TankFloor");
            var m = r.sharedMaterial;
            if (m == null) continue;
            bool esDeco = m.shader != null && m.shader.name.Contains("DecoLit");
            if (!esSuelo && !(esDeco && n_deco < 2)) continue;
            if (esDeco) n_deco++;

            var col  = m.HasProperty("_Color") ? m.GetColor("_Color") : new Color(-1, -1, -1);
            // ⚠ Si el MATERIAL declara `_AqDecoDarken`, su valor GANA al global y el ciclo
            // no se vería aunque el global esté bien puesto. Hay que saberlo.
            bool propEnMat = m.HasProperty("_AqDecoDarken");
            var  colMat    = propEnMat ? m.GetColor("_AqDecoDarken") : new Color(-1, -1, -1);

            JsBridge.Log($"sonda[{etiqueta}] {go} activo={r.enabled && r.gameObject.activeInHierarchy} " +
                         $"shader='{(m.shader == null ? "null" : m.shader.name)}' sup={(m.shader != null && m.shader.isSupported)} " +
                         $"_Color={col.r:F2}/{col.g:F2}/{col.b:F2}/{col.a:F2} " +
                         $"darkenEnMat={propEnMat}" + (propEnMat ? $"={colMat.r:F2}/{colMat.g:F2}/{colMat.b:F2}" : ""));
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
            nickname  = payload.nickname ?? "",
            ageScale  = payload.ageScale
        };
        var agent = mgr.fishSpawner.SpawnFish(data, bounds, save);
        if (agent != null) { agent.SetNickname(save.nickname); agent.SetUid(save.uid); }
        JsBridge.Log($"add_fish: {payload.speciesId} spawned");
    }

    private void RemoveFish(string speciesId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        int count = mgr.fishSpawner.DespawnOneBySpecies(speciesId);
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
            // Los SOs de los bundles no traen `hasBioLuminescence` (ver TvDecoCatalogPatch).
            TvDecoCatalogPatch.AplicarA(data);
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
        PublicarAspectoDelAgua();   // el color del agua sale del preset: hay que reeditarlo
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

    /// <summary>
    /// Parseo defensivo de los payloads que llegan por Cast.
    ///
    /// ⚠⚠ El `try/catch` de aquí abajo **NO es una red de seguridad en este build**: el player
    /// se compila con `Exception Support: None` (obligatorio, ver CLAUDE.md), y con eso una
    /// excepción del runtime no se captura — se escapa como error de JS. Medido el 2026-08-21:
    /// mandando `add_fish=<id suelto>` en vez del JSON que manda el móvil, el receiver soltó
    /// `JS ERR: Uncaught undefined` **con el catch puesto**, y el update se perdió sin más.
    ///
    /// Por eso lo que protege de verdad es la comprobación de FORMA de antes: si no parece un
    /// objeto JSON, ni se intenta. Y se avisa por JsBridge, porque un fallo que sólo se ve por
    /// Debug.Log es invisible en la tele.
    /// </summary>
    private static T SafeFromJson<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            JsBridge.Log("ERR payload: vacío");
            return null;
        }

        var limpio = json.TrimStart();
        if (limpio.Length == 0 || limpio[0] != '{')
        {
            var muestra = json.Length > 40 ? json.Substring(0, 40) + "…" : json;
            JsBridge.Log($"ERR payload: se esperaba un objeto JSON y llegó '{muestra}'");
            return null;
        }

        try   { return JsonUtility.FromJson<T>(limpio); }
        catch { JsBridge.Log("ERR payload: JSON con forma correcta pero ilegible"); return null; }
    }
}
