using UnityEngine;

/// <summary>
/// WebGL-side bridge: the Cast Receiver SDK (JS) calls window.unityInstance.SendMessage
/// targeting this component when a message arrives on the Cast Custom Channel.
///
/// Attach to a GameObject named exactly "CastReceiver" in the TvScene.
///
/// JS side (cast-receiver.js):
///   castSession.addMessageListener(CHANNEL_NAMESPACE, (ns, msg) => {
///       window.unityInstance.SendMessage('CastReceiver', 'OnMessageReceived', msg);
///   });
/// </summary>
public class CastReceiver : MonoBehaviour
{
    public static CastReceiver Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.name = "CastReceiver";  // must match JS SendMessage target
        Debug.Log("[CastReceiver] ✅ Ready — waiting for Cast sender messages.");
    }

    /// <summary>
    /// Entry point called from JavaScript by the Cast Receiver SDK.
    /// Parses the CastMessage and routes INIT / UPDATE to TvSceneBootstrap.
    /// </summary>
    // ⚠ 2026-08-15 — TODO el parseo va dentro de un try.
    // El player se compila con `Exception Support: None` (obligatorio: con wasm-exceptions
    // el Cast device peta). Con esa opción una excepción managed NO se puede capturar aguas
    // arriba: aborta el runtime IL2CPP en seco. El acuario se queda congelado en el último
    // frame, sin mensaje y sin forma de recuperarse (con disableIdleTimeout ni siquiera se
    // cierra la sesión sola). Antes los dos FromJson del payload estaban FUERA del try, así
    // que bastaba un UPDATE sin `payload` o con JSON malformado —y el único emisor es la app
    // móvil, que no vive en este repo— para colgar la tele.
    public void OnMessageReceived(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        Debug.Log($"[CastReceiver] ← {json.Substring(0, Mathf.Min(100, json.Length))}…");

        CastMessage msg;
        try   { msg = JsonUtility.FromJson<CastMessage>(json); }
        catch { Debug.LogWarning($"[CastReceiver] Failed to parse message: {json}"); return; }

        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        switch (msg.type)
        {
            case "INIT":
                TvAquariumState state;
                try   { state = JsonUtility.FromJson<TvAquariumState>(msg.payload); }
                catch (System.Exception e) { JsBridge.Log("ERR: INIT con payload ilegible — " + e.Message); return; }
                if (state == null) { JsBridge.Log("ERR: INIT con payload vacío — ignorado"); return; }
                if (TvSceneBootstrap.Instance == null)
                    JsBridge.Log("ERR: TvSceneBootstrap.Instance is NULL at INIT time!");
                TvSceneBootstrap.Instance?.InitializeFromState(state);
                break;

            case "UPDATE":
                TvUpdateMessage upd;
                try   { upd = JsonUtility.FromJson<TvUpdateMessage>(msg.payload); }
                catch (System.Exception e) { JsBridge.Log("ERR: UPDATE con payload ilegible — " + e.Message); return; }
                if (upd == null || string.IsNullOrEmpty(upd.type)) { JsBridge.Log("ERR: UPDATE sin tipo — ignorado"); return; }
                TvSceneBootstrap.Instance?.ApplyUpdate(upd);
                break;

            // Afinado del grado de color EN CALIENTE, sin rebuild de player (55 min por variante).
            // Los campos que no vengan en el JSON se quedan como están, así que se puede mandar
            // sólo lo que se quiere tocar: { "type":"GRADE", "payload":"{\"saturation\":-15}" }
            case "GRADE":
                AplicarGrado(msg.payload);
                break;

            case "PING":
            case "KEEPALIVE":
                break;

            default:
                Debug.LogWarning($"[CastReceiver] Unknown message type: {msg.type}");
                break;
        }
    }

    // ── JS interop helper (called from cast-receiver.js on sender connect) ───

    /// <summary>Called from JS when the Cast sender connects.</summary>
    public void OnSenderConnected(string senderId)
        => Debug.Log($"[CastReceiver] Sender connected: {senderId}");

    /// <summary>Called from JS when the Cast sender disconnects.</summary>
    public void OnSenderDisconnected(string senderId)
        => Debug.Log($"[CastReceiver] Sender disconnected: {senderId}");

    // ── Device input (Android TV remote / keyboard devtest) ──────────────────

    /// <summary>
    /// Called from JavaScript when the user presses a button on the Android TV remote
    /// or keyboard (devtest mode).
    ///   "startle" → fish scatter from tank center (analogous to tapping the screen)
    ///   "feed"    → spawn food visual + fish swim to eat
    /// </summary>
    public void OnDeviceInput(string action)
    {
        JsBridge.Log($"[Input] {action}");
        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        switch (action)
        {
            case "startle":
                mgr.StartleAll(mgr.tankController.GetTankBounds().center);
                break;
            case "feed":
                mgr.FeedAll();
                break;
        }
    }

    /// <summary>
    /// Cambia el grado de color en caliente. Existe porque elegir estos valores a base de builds
    /// cuesta 55 min por variante, y porque el barrido del Editor demostró no ser fiable para
    /// esto (ver CAST_PARIDAD_VISUAL.md §0.1): hay que decidirlo sobre el player de verdad.
    /// </summary>
    private void AplicarGrado(string payload)
    {
        var pp = FindFirstObjectByType<PostProcessingSetup>();
        if (pp == null) { JsBridge.Log("GRADE: no hay PostProcessingSetup en la escena"); return; }

        // Se parte de los valores ACTUALES y se sobreescribe sólo lo que traiga el JSON, para
        // poder mandar un único campo sin arrastrar el resto.
        var g = new GradePayload
        {
            bloom          = pp.enableBloom,
            bloomIntensity = pp.bloomIntensity,
            tonemapping    = pp.enableTonemapping,
            saturation     = pp.saturation,
            contrast       = pp.contrast,
            exposure       = pp.postExposure,
            vignette       = pp.vignetteIntensity,
            bgFit          = -1f,     // centinela: si el JSON no lo trae, no se toca el encuadre
        };
        try   { JsonUtility.FromJsonOverwrite(payload, g); }
        catch (System.Exception e) { JsBridge.Log("GRADE: payload ilegible — " + e.Message); return; }

        pp.enableBloom       = g.bloom;
        pp.bloomIntensity    = g.bloomIntensity;
        pp.enableTonemapping = g.tonemapping;
        pp.saturation        = g.saturation;
        pp.contrast          = g.contrast;
        pp.postExposure      = g.exposure;
        pp.vignetteIntensity = g.vignette;
        pp.AplicarValores();

        // Conmutar el shader del fondo en la misma sesión, para comparar sin otro build.
        if (!string.IsNullOrEmpty(g.bgShader))
        {
            var bg = FindFirstObjectByType<TankBackground>();
            if (bg == null) JsBridge.Log("BGSHADER: no hay TankBackground en la escena");
            else            bg.SwapBackgroundShader(g.bgShader);
        }

        // Encuadre del fondo: qué fracción tapa el suelo. Se barre en caliente para elegirlo.
        if (g.bgFit >= 0f)
        {
            var bg2 = FindFirstObjectByType<TankBackground>();
            if (bg2 == null) JsBridge.Log("BGFIT: no hay TankBackground en la escena");
            else             bg2.SetBackgroundFit(g.bgFit);
        }

        JsBridge.Log($"GRADE: bloom={(g.bloom ? g.bloomIntensity.ToString("F2") : "OFF")} " +
                     $"tm={(g.tonemapping ? "Neutral" : "OFF")} sat={g.saturation:F0} " +
                     $"con={g.contrast:F0} exp={g.exposure:F2} vig={g.vignette:F2}");
    }

    [System.Serializable]
    private class GradePayload
    {
        public bool  bloom;
        public float bloomIntensity;
        public bool  tonemapping;
        public float saturation;
        public float contrast;
        public float exposure;
        public float vignette;
        public string bgShader;   // "urp" | "sprites"; vacío = no tocar
        public float  bgFit;      // fracción tapada por el suelo; negativo = no tocar
    }
}
