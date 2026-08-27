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

        // ⚠⚠ 2026-08-27 — CULTURA INVARIANTE PARA TODO EL RECEIVER.
        //
        // Todo lo que sale por el canal Cast se formateaba con la cultura de la maquina, asi que
        // el MISMO build imprimia cosas distintas segun donde corriera: en el device (locale
        // ingles) `speed: x1.80`, y en el Chrome de un Windows en español `speed: x1,80`. Eso es
        // lo peor posible para lineas que alguien parsea — y el movil esta a punto de parsear
        // este canal (su R2), ademas de que el volcado `dump` existe justo para eso.
        //
        // Se vio porque `speed` NO TENIA NINGUNA PRUEBA: el handler llevaba meses imprimiendo la
        // coma y nadie lo habia mirado. Al contar tipo a tipo cuales estaban cubiertos salieron
        // cuatro sin prueba en ningun sitio (`speed`, `feed`, `startle`, `remove_deco`).
        //
        // 🧭 Se pone aqui, una vez, en vez de parchear los 14 `:F2` sueltos: asi tambien queda
        // arreglado el codigo que se escriba manana. La TV no tiene UI localizada (asume idioma
        // fijo, ver CLAUDE.md), asi que no hay nada que se vea afectado por el cambio.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture   = inv;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = inv;
        System.Threading.Thread.CurrentThread.CurrentCulture           = inv;

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

        // renderScale < 1 → URP renderiza a menos resolución (ahorro de fill-rate en el
        // Mali-G31). Se hace en runtime porque el asset URP no está como fichero editable.
        //
        // ⚠⚠ 2026-08-25 — **LA TELE REPORTA 2560x1440, NO 1920x1080.** Este comentario decía
        // "0.7 = 49% de píxeles" y era falso: daba por hecho un panel de 1080p sin
        // comprobarlo. Con 2560x1440 de `Screen`, 0,70 renderiza **1792x1008**, que es el
        // 93 % LINEAL de 1080p — o sea que la renderScale apenas estaba costando nitidez, y
        // la diferencia con el móvil que reportó el user hay que buscarla en el GRADO
        // (la TV lleva tonemapping + sat +18; el móvil bloom 1,2 / sat -15).
        //
        // **0,75 es el único valor no arbitrario: 2560x0,75 = 1920 y 1440x0,75 = 1080**, o
        // sea 1:1 con lo que el device entrega de verdad. Por debajo se renderiza de menos y
        // se estira; por encima se renderiza de más y se tira (a 1,0 serían 2560x1440 para
        // sacar 1080p).
        //
        // Coste medido en el Xiaomi, una sesión por escala y leyendo el HUD siempre al mismo
        // SESSION (12 peces + 3 decos):
        //     0,70  1792x1008  avg 35     0,85  2176x1224  avg 34
        //     0,75  1920x1080  avg 35     1,00  2560x1440  avg 33
        // O sea: 0,75 sale GRATIS respecto al 0,70 anterior.
        //
        // ⚠ Para remedirlo NO sirve barrer las escalas dentro de una sesión: el `avg` del HUD
        // es acumulativo desde el arranque y sube monótonamente pase lo que pase. Hacen falta
        // sesiones separadas. Ajustable en caliente con `GRADE {"renderScale": x}`.
        // Lookup robusto: el asset activo puede venir del quality level o del default global.
        // Debug.Log (no JsBridge) porque esto corre muy temprano, antes de que el bridge esté listo.
        var rpAsset = QualitySettings.renderPipeline
                   ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (rpAsset is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urpAsset)
        {
            urpAsset.renderScale = 0.75f;   // 1:1 con la salida real (2560x1440 → 1920x1080)
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

        SanearEstado(state);

        // ⚠ 2026-08-15 — parar la carga anterior antes de arrancar otra.
        // Si el sender caía y reconectaba MIENTRAS se bajaban bundles (carga fría ~40 s,
        // muy probable), corrían dos corrutinas a la vez: la nueva liberaba handles que la
        // vieja aún iba a usar, se hacía doble DespawnAll + doble spawn, y el
        // HideLoadingOverlay de la primera destapaba la pantalla a media carga de la segunda.
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadAndInitializeCoroutine(state));
    }

    /// <summary>
    /// Comprueba el INIT y arregla lo que no sea usable, DICIENDOLO por el canal Cast.
    ///
    /// ⚠⚠ 2026-08-26 — Hasta hoy el INIT no validaba nada: un `bgId` que no existiera caia al
    /// preset por defecto **en silencio** (`SetPreset` se planta en un `Debug.LogWarning`, que
    /// no viaja por Cast) y un `ambientMode` raro se volvia dia sin decir nada. La validacion
    /// del 26-ago sólo cubria la ruta UPDATE, asi que el contrato tenia una asimetria fea:
    /// «por UPDATE se oye, por INIT no». Esto la cierra.
    ///
    /// Sanear en vez de rechazar: un INIT es la escena entera, y tirarla por un id malo deja
    /// la tele vacia. Se corrige el campo y se sigue.
    /// </summary>
    private static void SanearEstado(TvAquariumState state)
    {
        state.bgId    = IdSaneado("INIT bgId",    state.bgId,    IdsDeFondo(),    TankBackground.Presets[0].id);
        state.subId   = IdSaneado("INIT subId",   state.subId,   IdsDeSustrato(), DecorationPlacer.SubstratePresets[0].id);
        state.lightId = IdSaneado("INIT lightId", state.lightId, IdsDeLuz(),      "light_white");
        state.ambientMode = IdSaneado("INIT ambientMode", state.ambientMode,
                                      new[] { "day", "sunset", "night" }, "day");

        // ⚠ El `try/catch` que envuelve el parseo de `decoJson` en AquariumManager **NO protege**:
        // el player va con `Exception Support: None`, con lo que la excepcion se escapa como
        // error de JS. Lo que protege es esta comprobacion de FORMA, la misma que ya hacia
        // `SafeFromJson` para los payloads de UPDATE. Sin ella, un decoJson corrupto no daba un
        // aviso: tumbaba el INIT entero.
        if (!string.IsNullOrEmpty(state.decoJson))
        {
            var limpio = state.decoJson.TrimStart();
            if (limpio.Length > 0 && limpio[0] != '{')
            {
                var muestra = state.decoJson.Length > 40 ? state.decoJson.Substring(0, 40) + "…" : state.decoJson;
                JsBridge.Log($"ERR INIT decoJson: se esperaba un objeto JSON y llegó '{muestra}' — se ignoran las decos");
                state.decoJson = "{}";
            }
        }
    }

    /// <summary>
    /// Devuelve `valor` si es valido; si no, el defecto — y **distingue los dos casos**:
    ///
    ///   · **vacio** = el sender no lo mando. No es un error: un cliente viejo, o un rig de
    ///     diagnostico que manda un estado minimo, entran por aqui. Se calla.
    ///   · **no vacio y desconocido** = el sender mando algo que no existe. Eso SI se dice.
    ///
    /// 🧭 La distincion importa: una guarda que grita por todo se acaba ignorando, y entonces
    /// no sirve para nada el dia que grita con razon.
    /// </summary>
    private static string IdSaneado(string campo, string valor, string[] validos, string porDefecto)
    {
        if (string.IsNullOrEmpty(valor)) return porDefecto;
        foreach (var v in validos) if (v == valor) return valor;

        JsBridge.Log($"ERR {campo}: id desconocido '{valor}' — válidos: {string.Join("|", validos)}"
                   + $" — se usa '{porDefecto}'");
        return porDefecto;
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
                // Que el automatico de la TV se aparte mientras el movil alimente: si no, la
                // tele come lo suyo MAS lo del sender. Ver `FoodManager._ultimoFeedDelSender` (en TvFoodManager.cs).
                FoodManager.Instance?.FeedDelSender();
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
            case "pairs":       AplicarParejas(upd.value);                 break;
            case "dump":        VolcarEstado();                            break;
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

        // Aqui ya existen `SaveData` y los `FishAgent`, asi que un `pairs` que hubiera llegado
        // durante la carga (la ventana ciega, ver `AplicarParejas`) ya se puede aplicar.
        ReaplicarParejasPendientes();

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
        if (payload == null) yield break;                     // SafeFromJson ya lo ha dicho
        if (string.IsNullOrEmpty(payload.speciesId))
        {
            JsBridge.Log("ERR add_fish: el payload no trae speciesId");
            yield break;
        }

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
            // ⚠ 2026-08-26 — Se ADOPTA el uid del movil. Antes se generaba SIEMPRE aqui, y por
            // eso un pez anadido a mitad de sesion no podia emparejarse nunca: `activePairs`
            // referencia los uid del movil. Vacio = cliente viejo -> uid propio.
            uid       = string.IsNullOrEmpty(payload.uid) ? System.Guid.NewGuid().ToString() : payload.uid,
            speciesId = payload.speciesId,
            nickname  = payload.nickname ?? "",
            ageScale  = payload.ageScale
        };
        // ⚠ 2026-08-26 — Esto decía «spawned» AUNQUE `SpawnFish` devolviera null: el
        // `if (agent != null)` protegía las dos llamadas de abajo y el log salía igual. Mismo
        // fallo que tenían los tres `change_*`: se reportaba la intención, no el efecto.
        var agent = mgr.fishSpawner.SpawnFish(data, bounds, save);
        if (agent == null)
        {
            JsBridge.Log($"ERR add_fish: {payload.speciesId} cargó pero SpawnFish devolvió null — no hay pez");
            yield break;
        }
        agent.SetNickname(save.nickname);
        agent.SetUid(save.uid);

        // Que el pez conste en el save transitorio, o `remove_fish` y el emparejamiento no lo ven.
        if (mgr.SaveData != null)
        {
            mgr.SaveData.ownedFish.Add(save);
            mgr.SaveData.activeFishUids.Add(save.uid);
        }

        // ⚠⚠ LA CARRERA (2026-08-26). El movil emite `pairs` justo despues del `add_fish` que
        // forma la pareja, pero aqui se acaba de ESPERAR UNA DESCARGA DE BUNDLE (0,3-1,5 s en
        // local, mas en el device y en frio). Un `FishAgent` no entra en `FishAgent.All` hasta
        // su OnEnable, o sea hasta que existe de verdad, asi que el `pairs` puede llegar ANTES
        // que el pez y `All.Find` devuelve null: la pareja se descarta EN SILENCIO. Y como
        // `pairs` es reemplazo y sólo se emite al cambiar, esa pareja NO se vuelve a mandar.
        //
        // Por eso se re-empareja aqui, con la ultima lista recibida. Es seguro repetirlo tantas
        // veces como haga falta porque `WirePairsFromSave` limpia TODOS los partners antes de
        // re-cablear: el consumidor es de reemplazo total, igual que el emisor.
        int parejas = ReemparejarYContar(mgr, "add_fish");

        JsBridge.Log($"add_fish: {payload.speciesId} spawned"
                   + $" ({mgr.fishSpawner.ActiveFish?.Count ?? -1} peces en el tanque"
                   + (parejas > 0 ? $", {parejas} parejas" : "") + ")");
    }

    /// <summary>
    /// Re-aplica la ultima lista de parejas y devuelve cuantas quedaron CABLEADAS de verdad
    /// (las dos mitades presentes), que no tiene por que ser cuantas se recibieron.
    /// </summary>
    private static int ReemparejarYContar(AquariumManager mgr, string origen)
    {
        if (mgr?.SaveData?.activePairs == null || mgr.SaveData.activePairs.Count == 0) return 0;

        FishAgent.WirePairsFromSave(mgr.SaveData);

        int cableadas = 0;
        foreach (var p in mgr.SaveData.activePairs)
        {
            bool macho  = false, hembra = false;
            foreach (var a in FishAgent.All)
            {
                if (a == null) continue;
                if (a.Uid == p.maleUid)   macho  = true;
                if (a.Uid == p.femaleUid) hembra = true;
            }
            if (macho && hembra) cableadas++;
        }
        return cableadas;
    }

    /// <summary>
    /// Quita UN pez. Acepta las dos formas del valor, y es ADITIVO a proposito:
    ///
    ///   · `"fish_banggai"` — cliente viejo. Quita el PRIMERO de esa especie, que casi nunca
    ///     es el que el usuario quito en el movil. Se sigue soportando, pero ahora el log
    ///     **dice por que camino fue**, en vez de dar a entender que quito ese pez.
    ///   · `{"uid":"…","speciesId":"…"}` — el camino bueno (2026-08-27). Los uid ya son los
    ///     mismos en los dos lados desde que se adoptan en INIT y `add_fish`.
    ///
    /// ⚠ Si viene uid y ese pez NO esta en el tanque, **no se cae al camino de la especie**:
    /// eso quitaria un pez cualquiera, que es exactamente el fallo que esto viene a arreglar.
    /// Se reporta ERR y no se toca nada.
    /// </summary>
    private void RemoveFish(string value)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        string uid = "", speciesId = value ?? "";

        if (speciesId.TrimStart().StartsWith("{"))
        {
            var payload = SafeFromJson<TvRemoveFishPayload>(value);
            if (payload == null) return;                      // SafeFromJson ya lo ha dicho
            uid       = payload.uid ?? "";
            speciesId = payload.speciesId ?? "";
            if (string.IsNullOrEmpty(uid))
            {
                JsBridge.Log("ERR remove_fish: el payload es JSON pero no trae uid");
                return;
            }
        }

        if (!string.IsNullOrEmpty(uid))
        {
            var quitado = mgr.fishSpawner.DespawnByUid(uid);
            if (quitado == null)
            {
                JsBridge.Log($"ERR remove_fish: uid '{uid}' no esta en el tanque — no se quita nada");
                return;
            }
            // `Destroy` es diferido al final del frame, asi que el agente aun se puede leer.
            if (string.IsNullOrEmpty(speciesId)) speciesId = quitado.Data?.itemId ?? "";
            OlvidarPezDelSave(mgr, uid);
            JsBridge.Log($"remove_fish: {speciesId} uid={uid}"
                       + $" (quedan {mgr.fishSpawner.ActiveFish?.Count ?? -1} peces)");
        }
        else
        {
            if (string.IsNullOrEmpty(speciesId))
            {
                JsBridge.Log("ERR remove_fish: valor vacio");
                return;
            }
            // Mismo criterio que `DespawnOneBySpecies` —el primero de la lista— pero mirado
            // ANTES de quitarlo, para saber que uid hay que olvidar del save.
            string uidVictima = null;
            foreach (var f in mgr.fishSpawner.ActiveFish)
                if (f != null && f.Data?.itemId == speciesId) { uidVictima = f.Uid; break; }

            if (mgr.fishSpawner.DespawnOneBySpecies(speciesId) == 0)
            {
                JsBridge.Log($"ERR remove_fish: no hay ningun '{speciesId}' en el tanque");
                return;
            }
            OlvidarPezDelSave(mgr, uidVictima);
            JsBridge.Log($"remove_fish: {speciesId} por especie (cliente sin uid: quitado el primero)"
                       + $" — quedan {mgr.fishSpawner.ActiveFish?.Count ?? -1} peces");
        }

        SoltarBundleSiNoQuedaNinguno(mgr, speciesId);
    }

    /// <summary>
    /// Saca el pez del save transitorio.
    ///
    /// ⚠ 2026-08-27 — Antes NADIE lo hacia: `add_fish` alimentaba `ownedFish`/`activeFishUids`
    /// (`:809`) y `remove_fish` destruia el agente y se olvidaba del save, asi que las dos
    /// listas solo crecian y divergian del tanque segun avanzaba la sesion. Hoy solo se leen
    /// en el arranque, pero el emparejamiento ya consume uid de ahi y el proximo consumidor
    /// los dara por buenos igual.
    /// </summary>
    private static void OlvidarPezDelSave(AquariumManager mgr, string uid)
    {
        var save = mgr?.SaveData;
        if (save == null || string.IsNullOrEmpty(uid)) return;

        save.ownedFish?.RemoveAll(f => f != null && f.uid == uid);
        save.activeFishUids?.Remove(uid);

        // Si estaba emparejado, la pareja deja de existir: mientras siga en la lista, `pairs`
        // la contaria eternamente como «recibida pero no cableada» — el mismo sintoma que la
        // carrera del `add_fish`, y ahi si es un fallo. Re-cablear ademas limpia el PartnerUid
        // colgando del que se queda vivo.
        int antes = save.activePairs?.Count ?? 0;
        save.activePairs?.RemoveAll(p => p != null && (p.maleUid == uid || p.femaleUid == uid));
        if ((save.activePairs?.Count ?? 0) != antes) FishAgent.WirePairsFromSave(save);
    }

    /// <summary>
    /// Suelta el bundle de la especie si ya no queda ningun pez suyo y lo habia cargado el
    /// runtime (no el INIT). Estaba embebido en `RemoveFish`; sale aqui porque ahora hay dos
    /// caminos que terminan igual.
    /// </summary>
    private void SoltarBundleSiNoQuedaNinguno(AquariumManager mgr, string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return;
        if (!_runtimeFishHandles.TryGetValue(speciesId, out var h)) return;

        foreach (var f in mgr.fishSpawner.ActiveFish)
            if (f != null && f.Data?.itemId == speciesId) return;   // aun queda alguno

        Addressables.Release(h);
        _runtimeFishHandles.Remove(speciesId);
        mgr.allFishCatalog.RemoveAll(d => d.itemId == speciesId);
    }

    private IEnumerator AddDecoAsync(string jsonValue)
    {
        var payload = SafeFromJson<TvAddDecoPayload>(jsonValue);
        if (payload == null) yield break;                     // SafeFromJson ya lo ha dicho
        if (string.IsNullOrEmpty(payload.itemId))
        {
            JsBridge.Log("ERR add_deco: el payload no trae itemId");
            yield break;
        }

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

        // ⚠ 2026-08-26 — `PlaceAt` DEVUELVE bool y se estaba tirando: una deco rechazada
        // (sin sitio, fuera del tanque) se confirmaba como colocada.
        // ⚠⚠ 2026-08-27 — Se hace lo MISMO que el camino del INIT, que lleva funcionando desde
        // siempre: `DecorationPlacer.LoadFromSaveAsync` (:1167-1175) reconstruye el cuaternion
        // desde `hasUserRot` + `quat*` y monta despues. Este camino se habia quedado atras y
        // perdia giro, inclinacion y montaje — los tres que MAS se notan al editar una deco.
        // Copiar el camino que ya funciona es mas seguro que inventar otro.
        Quaternion? rotUsuario = payload.hasUserRot
            ? (Quaternion?)new Quaternion(payload.quatX, payload.quatY, payload.quatZ, payload.quatW)
            : null;

        bool colocada = placer.PlaceAt(data, payload.position,
            flipped:      payload.flipped,
            rotationY:    payload.rotationY,
            tiltX:        payload.tiltX,
            scaleFactor:  payload.scaleFactor > 0f ? payload.scaleFactor : 1f,
            fromSave:     true,
            instanceId:   string.IsNullOrEmpty(payload.instanceId) ? null : payload.instanceId,
            savedUserRot: rotUsuario);

        if (!colocada)
        {
            JsBridge.Log($"ERR add_deco: {payload.itemId} cargó pero PlaceAt lo rechazó (¿sin sitio en el tanque?)");
            yield break;
        }

        // El montaje va DESPUES de colocar, como en el INIT: necesita que la deco exista.
        string montaje = "";
        if (!string.IsNullOrEmpty(payload.mountedOnInstanceId) && !string.IsNullOrEmpty(payload.instanceId))
        {
            placer.MountDecoOnTarget(payload.instanceId, payload.mountedOnInstanceId);
            montaje = $" montada sobre {payload.mountedOnInstanceId}";
        }

        // Se reporta lo que se APLICO, no lo que llego: si el sender manda una rotacion y aqui
        // no aparece, es que venia sin `hasUserRot` y hay que mirar el emisor.
        JsBridge.Log($"add_deco: {payload.itemId} at {payload.position:F1}"
                   + (rotUsuario.HasValue ? " +rot" : "")
                   + (Mathf.Abs(payload.tiltX) > 0.01f ? $" +tilt {payload.tiltX:F0}°" : "")
                   + montaje);
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

    /// <summary>
    /// UPDATE `pairs` — la lista COMPLETA de parejas activas, no un delta.
    ///
    /// El movil la emite desde un unico choke point (el final de `CheckBreedingPairs`) cada vez
    /// que cambia. Es reemplazo a proposito: un delta `pair`/`unpair` se desincroniza para
    /// siempre si se pierde un mensaje, y aqui no hay acuse de recibo de nada.
    ///
    /// Encaja sin adaptador porque `WirePairsFromSave` **limpia TODOS los partners** antes de
    /// re-cablear: los dos lados son de reemplazo total por construccion.
    ///
    /// ⚠ Se reporta cuantas quedan CABLEADAS, no cuantas llegaron. No es lo mismo: una pareja
    /// cuyo pez aun se esta descargando no se cablea, y ese hueco es justo la carrera que
    /// documenta `AddFishAsync`. Si el log dice «3 recibidas, 2 cableadas», ahi esta.
    /// </summary>
    // ── Volcado del estado resuelto (2026-08-27) ──────────────────────────────
    //
    // POR QUE EXISTE: el objetivo del proyecto es que lo que se castea se vea IGUAL que en el
    // movil — tamaño del pez, donde esta cada deco, a que escala, girada como, y de quien
    // cuelga. Comparar eso a ojo entre dos pantallas es justo el error que este proyecto lleva
    // un mes pagando. Esto lo convierte en un DIFF.
    //
    // 🧭 Se vuelca lo que la escena tiene MONTADO, no lo que llego por el canal: la posicion
    // sale del transform vivo (`GetCurrentPlacements`, DecorationPlacer.cs:1257) y la escala del
    // pez de su `localScale`. Si el emisor manda una cosa y aqui se ve otra, la diferencia
    // aparece — que es todo el proposito.
    //
    // ⚠⚠ Las tres transformaciones que ESTE LADO aplica y que pueden separar las dos pantallas
    // aunque el dato llegue bien (por eso van en la cabecera del volcado):
    //   1. `remapX` — la X de las decos se re-escala por `bounds.x / tankHalfWidth`, y SOLO la
    //      X. Si el movil no manda `tankHalfWidth` vale 0 y NO hay remapeo: las posiciones se
    //      usan crudas. La escala de la deco no se re-escala, asi que con remapX != 1 la
    //      separacion relativa a su propio tamaño cambia.
    //   2. El `Clamp` a los bordes (DecorationPlacer.cs:365-366) mueve en SILENCIO una deco
    //      pegada al borde. Por eso se vuelca la posicion final y los bounds: se ve.
    //   3. El tamaño del pez esta CUANTIZADO a 4 escalones (TvStubs.cs:60-64). El round-trip
    //      es exacto solo si el movil manda uno de los cuatro valores discretos.
    //
    // Formato pensado para diff: una entidad por linea, ordenadas por id, precision fija.
    // ⚠⚠ 2026-08-27 — `INV` NO es cosmetico. La primera version formateaba con la cultura del
    // sistema (español), asi que salia `pos=(4,12,-1,87,0,54)`: con coma decimal Y coma
    // separadora es IMPOSIBLE saber donde acaba un numero y empieza el siguiente. El volcado
    // existe para diffear contra el movil, o sea para que alguien lo PARSEE, asi que eso lo
    // invalidaba entero. Y el test pasaba en verde: comprobaba que las cuentas cuadraran, no
    // que los numeros se pudieran leer. Se vio mirando la salida de verdad.
    private static readonly System.Globalization.CultureInfo INV =
        System.Globalization.CultureInfo.InvariantCulture;

    private void VolcarEstado()
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) { JsBridge.Log("ERR dump: no hay acuario"); return; }

        var placer = mgr.tankController != null ? mgr.tankController.GetComponent<DecorationPlacer>() : null;
        var bg     = FindFirstObjectByType<TankBackground>();
        var luz    = FindFirstObjectByType<TankLightingController>();
        var amb    = FindFirstObjectByType<AmbientModeController>();

        var b = mgr.tankController != null ? mgr.tankController.GetTankBounds() : new Bounds();
        float mediaAnchoMovil = placer != null ? placer.MobileTankHalfWidth : 0f;
        float remapX = (mediaAnchoMovil > 0.1f && b.extents.x > 0.1f) ? b.extents.x / mediaAnchoMovil : 1f;

        JsBridge.Log("DUMP ini"
            + $" tanque={mgr.SaveData?.selectedTankId ?? "?"}"
            // ⚠ `extents` (medias medidas) va porque es la convencion que eligio el emisor del
            // movil para su propio volcado. Sin un campo con la MISMA semantica en los dos, el
            // diff mete ruido en cada comparacion. Los min/max se quedan porque dicen mas: con
            // solo extents, un tanque descentrado parece igual que uno centrado.
            + $" extents=({b.extents.x.ToString("F2", INV)},{b.extents.y.ToString("F2", INV)},{b.extents.z.ToString("F2", INV)})"
            + $" bounds=({b.min.x.ToString("F2", INV)},{b.max.x.ToString("F2", INV)} | {b.min.y.ToString("F2", INV)},{b.max.y.ToString("F2", INV)} | {b.min.z.ToString("F2", INV)},{b.max.z.ToString("F2", INV)})"
            + $" anchoMovil={mediaAnchoMovil.ToString("F2", INV)} remapX={remapX.ToString("F3", INV)}"
            + (mediaAnchoMovil <= 0.1f ? " (SIN REMAPEO: el sender no mando tankHalfWidth)" : "")
            + $" bg={bg?.CurrentPresetId ?? "?"} sub={placer?.CurrentSubstrateId ?? "?"}"
            + $" luz={luz?.CurrentPresetId ?? "?"} ambiente={amb?.CurrentMode.ToString() ?? "?"}");

        // ── Peces, ordenados por uid ──────────────────────────────────────────
        var peces = new List<FishAgent>();
        foreach (var f in FishAgent.All) if (f != null) peces.Add(f);
        peces.Sort((x, y) => string.CompareOrdinal(x.Uid ?? "", y.Uid ?? ""));
        foreach (var f in peces)
        {
            // localScale ya es baseSize * AgeScaleFactor(grupo): el tamaño REAL en pantalla.
            JsBridge.Log($"DUMP pez {f.Uid ?? "-"} {f.Data?.itemId ?? "?"}"
                + $" escala={f.transform.localScale.x.ToString("F3", INV)}"
                + $" pos=({f.transform.position.x.ToString("F2", INV)},{f.transform.position.y.ToString("F2", INV)},{f.transform.position.z.ToString("F2", INV)})"
                + $" pareja={(string.IsNullOrEmpty(f.PartnerUid) ? "-" : f.PartnerUid)}");
        }

        // ── Decos, ordenadas por instanceId ───────────────────────────────────
        var decos = placer != null ? placer.GetCurrentPlacements() : new List<DecoPlacement>();
        decos.Sort((x, y) => string.CompareOrdinal(x.instanceId ?? "", y.instanceId ?? ""));
        foreach (var p in decos)
        {
            bool alBorde = Mathf.Abs(p.position.x - (b.min.x + 0.3f)) < 0.01f
                        || Mathf.Abs(p.position.x - (b.max.x - 0.3f)) < 0.01f;
            JsBridge.Log($"DUMP deco {p.instanceId} {p.itemId}"
                + $" pos=({p.position.x.ToString("F2", INV)},{p.position.y.ToString("F2", INV)},{p.position.z.ToString("F2", INV)})"
                + $" escala={p.scaleFactor.ToString("F3", INV)} flip={(p.flipped ? 1 : 0)}"
                + $" quat=({p.quatX.ToString("F3", INV)},{p.quatY.ToString("F3", INV)},{p.quatZ.ToString("F3", INV)},{p.quatW.ToString("F3", INV)})"
                + $" sobre={(string.IsNullOrEmpty(p.mountedOnInstanceId) ? "-" : p.mountedOnInstanceId)}"
                // ⚠ Se avisa del recorte: si no, una deco movida por el Clamp parece bien puesta.
                + (alBorde ? " ⚠RECORTADA-AL-BORDE" : ""));
        }

        JsBridge.Log($"DUMP fin peces={peces.Count} decos={decos.Count}");
    }

    /// <summary>
    /// El ultimo `pairs` que llego antes de que existiera el acuario. Ver la ventana ciega en
    /// `AplicarParejas`. Es de REEMPLAZO, como el propio mensaje: solo interesa el ultimo.
    /// </summary>
    private string _parejasPendientes;

    /// <summary>
    /// Aplica el `pairs` que se quedo esperando, si lo hubo. Se llama al terminar la carga,
    /// cuando ya existen `SaveData` y los `FishAgent`.
    /// </summary>
    private void ReaplicarParejasPendientes()
    {
        if (string.IsNullOrEmpty(_parejasPendientes)) return;
        var pendiente = _parejasPendientes;
        _parejasPendientes = null;          // antes de aplicar, o un fallo lo dejaria en bucle
        JsBridge.Log("pairs: aplicando las que llegaron durante la carga");
        AplicarParejas(pendiente);
    }

    private void AplicarParejas(string jsonValue)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        // ⚠⚠ 2026-08-27 — LA VENTANA CIEGA, que encontro la sesion del repo movil.
        // `SaveData` no existe hasta que termina la descarga de bundles: segundos, mas en frio.
        // Y el `pairs` del movil es *edge-triggered* —solo se emite cuando CAMBIA, sin tick
        // periodico—, asi que un cambio de parejas que caiga en esa ventana se perdia PARA
        // SIEMPRE, y no volvia hasta la siguiente reconexion. Esto no lo cubria el arreglo de
        // la carrera del 26-ago: aquel re-empareja tras cada `add_fish`, pero si el acuario ni
        // siquiera existe no hay nada que re-emparejar.
        //
        // 🧭 Se guarda aqui en vez de pedirle al movil que lo reemita: asi deja de perderse sin
        // depender de que publiquen un APK. Lo aplica `ReaplicarParejasPendientes()` al final
        // de la carga.
        if (mgr.SaveData == null)
        {
            _parejasPendientes = jsonValue;
            JsBridge.Log("pairs: aun no hay acuario — guardadas para aplicarlas al terminar la carga");
            return;
        }

        var payload = SafeFromJson<TvPairList>(jsonValue);
        if (payload == null) return;                      // SafeFromJson ya lo ha dicho

        mgr.SaveData.activePairs = payload.items ?? new System.Collections.Generic.List<BreedingPair>();
        int recibidas = mgr.SaveData.activePairs.Count;
        int cableadas = ReemparejarYContar(mgr, "pairs");

        if (recibidas == 0) { JsBridge.Log("pairs: 0 — todas las parejas deshechas"); return; }

        JsBridge.Log(recibidas == cableadas
            ? $"pairs: {recibidas} recibidas, {cableadas} cableadas"
            : $"pairs: {recibidas} recibidas pero sólo {cableadas} cableadas"
              + " — al resto le falta algun pez en el tanque (¿aun descargando?)");
    }

    // ── Los tres «cambiar preset»: fondo, sustrato y luz ─────────────────────
    //
    // ⚠⚠ 2026-08-26 — Los tres CONFIRMABAN ids que no existen. `SetPreset` y `SetSubstrate`
    // se plantan en un `Debug.LogWarning` y vuelven sin tocar nada; el `Debug.Log` NO viaja
    // por el canal Cast (ver CLAUDE.md), así que desde fuera sólo se veía la línea de aquí
    // abajo — «change_sub: sub_black» — y parecía que había funcionado. Encima el id fantasma
    // se guardaba en `SaveData`.
    //
    // Lo que costó: el 25-ago se dio por buena una prueba entera con `sub_black`, y
    // `Tools/test-updates.js` llevaba meses en VERDE mandando `bg_ocean`, que tampoco existe:
    // el test comprobaba que el receiver hacía eco del id, no que el fondo cambiara.
    //
    // Ahora se hacen las dos cosas que faltaban:
    //   1. Se valida el id contra la lista ANTES, y si no está se dice cuáles valen — el
    //      patrón que `ambient` ya usaba con day|sunset|night.
    //   2. Se RELEE el estado después de aplicar en vez de reportar la intención (mismo
    //      criterio que la sonda de render del 25-ago). Si algún día el setter deja de
    //      aplicar por otro motivo, se verá aquí en vez de salir en verde.

    private static string[] IdsDeFondo()
    {
        var ids = new string[TankBackground.Presets.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = TankBackground.Presets[i].id;
        return ids;
    }

    private static string[] IdsDeSustrato()
    {
        var ids = new string[DecorationPlacer.SubstratePresets.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = DecorationPlacer.SubstratePresets[i].id;
        return ids;
    }

    private static string[] IdsDeLuz()
    {
        var ids = new string[TankLightingController.Presets.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = TankLightingController.Presets[i].id;
        return ids;
    }

    /// <summary>
    /// ¿Está `id` en la lista? Si no, lo reporta por el canal Cast CON la lista de válidos,
    /// que es lo que convierte un «no pasó nada» en un diagnóstico.
    /// </summary>
    private static bool ComprobarId(string tipo, string id, string[] validos)
    {
        if (!string.IsNullOrEmpty(id))
            foreach (var v in validos)
                if (v == id) return true;

        JsBridge.Log($"ERR {tipo}: id desconocido '{id}' — válidos: {string.Join("|", validos)}");
        return false;
    }

    private void ChangeBg(string bgId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var bg = mgr.tankController.GetComponent<TankBackground>();
        if (bg == null) { JsBridge.Log("ERR change_bg: no hay TankBackground en la escena"); return; }
        if (!ComprobarId("change_bg", bgId, IdsDeFondo())) return;

        string previo = bg.CurrentPresetId;
        bg.SetPreset(bgId);

        if (bg.CurrentPresetId != bgId)
        {
            JsBridge.Log($"ERR change_bg: '{bgId}' es válido pero el fondo sigue en '{bg.CurrentPresetId}'");
            return;
        }

        if (mgr.SaveData != null) mgr.SaveData.selectedBgId = bgId;
        PublicarAspectoDelAgua();   // el color del agua sale del preset: hay que reeditarlo
        JsBridge.Log(previo == bgId
            ? $"change_bg: {bgId} — ya estaba puesto, sin cambio"
            : $"change_bg: {previo} → {bgId}");
    }

    private void ChangeSub(string subId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var placer = mgr.tankController.GetComponent<DecorationPlacer>();
        if (placer == null) { JsBridge.Log("ERR change_sub: no hay DecorationPlacer en la escena"); return; }
        if (!ComprobarId("change_sub", subId, IdsDeSustrato())) return;

        string previo = placer.CurrentSubstrateId;
        placer.SetSubstrate(subId);

        if (placer.CurrentSubstrateId != subId)
        {
            JsBridge.Log($"ERR change_sub: '{subId}' es válido pero el suelo sigue en '{placer.CurrentSubstrateId}'");
            return;
        }

        if (mgr.SaveData != null) mgr.SaveData.selectedSubId = subId;
        JsBridge.Log(previo == subId
            ? $"change_sub: {subId} — ya estaba puesto, sin cambio"
            : $"change_sub: {previo} → {subId}");
    }

    private void ChangeLight(string lightId)
    {
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;
        var lighting = mgr.tankController.GetComponent<TankLightingController>();
        if (lighting == null) { JsBridge.Log("ERR change_light: no hay TankLightingController en la escena"); return; }
        if (!ComprobarId("change_light", lightId, IdsDeLuz())) return;

        string previo = lighting.CurrentPresetId;
        lighting.SetPreset(lightId);

        if (lighting.CurrentPresetId != lightId)
        {
            JsBridge.Log($"ERR change_light: '{lightId}' es válido pero la luz sigue en '{lighting.CurrentPresetId}'");
            return;
        }

        if (mgr.SaveData != null) mgr.SaveData.lightPresetId = lightId;
        JsBridge.Log(previo == lightId
            ? $"change_light: {lightId} — ya estaba puesta, sin cambio"
            : $"change_light: {previo} → {lightId}");
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
