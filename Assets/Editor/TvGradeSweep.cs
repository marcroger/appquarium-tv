using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Barrido de grado de color en el EDITOR: entra en play, inyecta un acuario, y captura una
/// PNG por cada combinación de bloom / tonemapping / saturación.
///
/// Para qué: el user ve la tele más apagada y «más falsa» que la app (2026-08-21). Las palancas
/// están identificadas en CAST_PARIDAD_VISUAL.md, pero **elegir** valores a base de builds de
/// WebGL costaría 55 minutos por variante. Esto las compara en unos minutos y sin construir
/// nada. El build sólo se gasta al final, con las 2-3 finalistas.
///
/// ⚠ Esto NO es el device. Sirve para ELEGIR, no para dar por buena una variante: el Chromium
/// del Cast tiene su propio pipeline y su propio coste de GPU (el bloom se apagó por eso).
/// La validación sigue siendo la tele, con el protocolo de §4 del doc.
///
/// ⚠ Trampa que este arnés sí esquiva: entrar en play mode dispara un **domain reload** y
/// resetea cualquier `static`. Por eso el paso vive en `SessionState` (sobrevive al reload) y
/// el tick se re-registra desde `[InitializeOnLoadMethod]`.
/// </summary>
[InitializeOnLoad]
public static class TvGradeSweep
{
    private const string KeyPaso     = "TvGradeSweep.paso";
    private const string KeyProxima  = "TvGradeSweep.proximaHora";
    private const string KeyCuenta   = "TvGradeSweep.ultimaCuenta";
    private const string KeyEstable  = "TvGradeSweep.estableDesde";
    private const string KeyLimite   = "TvGradeSweep.limiteCarga";
    private const string KeyMedias   = "TvGradeSweep.medias";
    private const string CarpetaSalida = "_gradesweep";

    // ⚠ NO se espera un tiempo fijo a que carguen los bundles. Una espera a ciegas que se
    // quede corta captura el tanque VACÍO y devuelve 8 PNG idénticas que parecen un resultado:
    // exactamente el tipo de fallo silencioso que este proyecto ya ha pagado varias veces.
    // En su lugar se espera a que el número de peces y de sombras de deco deje de cambiar.
    private const double EstabilidadSeg  = 3.0;    // sin cambios durante esto = cargado
    private const double LimiteCargaSeg  = 180.0;  // más que esto = abortar, no capturar vacío
    // Espera entre aplicar una variante y capturarla (Destroy del Volume viejo es diferido).
    private const double EsperaVarianteSeg = 0.6;

    // ── Las variantes ────────────────────────────────────────────────────────
    // A es el estado ACTUAL de la TV y B el grado EXACTO del móvil: son los dos extremos que
    // hay que tener para que la comparación signifique algo. El resto son intermedios que
    // separan qué aporta cada palanca por separado.
    private struct Variante
    {
        public string nombre;
        public bool   bloom;
        public float  bloomIntensity;
        public bool   tonemapping;
        public float  saturation;
        public float  contrast;
        public float  postExposure;
    }

    private static readonly Variante[] Variantes =
    {
        new Variante { nombre = "A_tv_actual",        bloom = false, bloomIntensity = 0.00f, tonemapping = true,  saturation =  18f, contrast = 10f, postExposure = 0.05f },
        new Variante { nombre = "B_movil_exacto",     bloom = true,  bloomIntensity = 1.20f, tonemapping = false, saturation = -15f, contrast =  0f, postExposure = 0.10f },
        new Variante { nombre = "C_movil_con_tm",     bloom = true,  bloomIntensity = 1.20f, tonemapping = true,  saturation = -15f, contrast =  0f, postExposure = 0.10f },
        new Variante { nombre = "D_tv_sin_tm",        bloom = false, bloomIntensity = 0.00f, tonemapping = false, saturation =  18f, contrast = 10f, postExposure = 0.05f },
        new Variante { nombre = "E_bloom_medio",      bloom = true,  bloomIntensity = 0.60f, tonemapping = true,  saturation =   0f, contrast = 10f, postExposure = 0.05f },
        new Variante { nombre = "F_bloom_medio_notm", bloom = true,  bloomIntensity = 0.60f, tonemapping = false, saturation =   0f, contrast = 10f, postExposure = 0.05f },
        new Variante { nombre = "G_bloom_bajo",       bloom = true,  bloomIntensity = 0.35f, tonemapping = true,  saturation =  10f, contrast = 10f, postExposure = 0.05f },
        new Variante { nombre = "H_movil_mas_con",    bloom = true,  bloomIntensity = 1.20f, tonemapping = false, saturation = -15f, contrast = 10f, postExposure = 0.10f },
    };

    // ── Estado del acuario a inyectar ────────────────────────────────────────
    // Si existe _gradesweep/state.json se usa ÉSE: es la vía para reproducir exactamente el
    // acuario que el user tiene en el móvil (mismo bgId, subId y ambientMode), que es lo único
    // que hace la comparación válida. Si no, se usa este por defecto.
    private const string EstadoPorDefecto = @"{
        ""activeFish"": [
            {""speciesId"": ""fish_banggai_cardinalfish"", ""nickname"": ""a""},
            {""speciesId"": ""fish_angelfish_emperor"", ""nickname"": ""b""}
        ],
        ""bgId"": ""bg_tropical"", ""subId"": ""sub_sand"", ""lightId"": ""light_white"",
        ""ambientMode"": ""day"", ""fishSpeed"": 1.0, ""selectedTankId"": ""tank_l"",
        ""decoJson"": ""{\""items\"":[
            {\""itemId\"":\""deco_anchor\"",\""instanceId\"":\""deco_anchor_0\"",\""position\"":{\""x\"":-3.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1},
            {\""itemId\"":\""deco_rock_hq_1\"",\""instanceId\"":\""deco_rock_hq_1_0\"",\""position\"":{\""x\"":0.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1},
            {\""itemId\"":\""deco_coral_acropora\"",\""instanceId\"":\""deco_coral_acropora_0\"",\""position\"":{\""x\"":3.0,\""y\"":-2.8,\""z\"":2.0},\""scaleFactor\"":1}
        ]}""
    }";

    static TvGradeSweep()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private const string RutaEscena = "Assets/Scenes/TvScene.unity";

    /// <summary>
    /// Entrada por línea de comandos. Se lanza el Editor CON interfaz (no `-batchmode`) y se le
    /// pasa este método, que corre en cuanto termina de compilar. Así no hace falta que la
    /// ventana tenga el foco ni que el bridge MCP esté levantado.
    ///
    ///   Unity.exe -projectPath . -executeMethod TvGradeSweep.LanzarBatch -logFile sweep.log
    ///
    /// ⚠⚠ NO usar `-batchmode` aquí, aunque sea lo natural. Sin pantalla, Unity no instancia el
    /// render pipeline de URP (`RenderPipelineManager.currentPipeline` = null), y entonces
    /// `SubmitRenderRequest` no está soportado — y `cam.Render()`, que fue el primer intento,
    /// cae al camino legacy y renderiza la escena SIN post-proceso sin decir nada. El barrido
    /// salió con 8 PNG y exit 0, y las 8 tenían exactamente la misma luminancia. De ahí salen
    /// las dos guardas que hay ahora: una aquí y otra en el comparador.
    ///
    /// ⚠ Y sin `-quit`: el barrido necesita seguir vivo entre frames.
    /// </summary>
    public static void LanzarBatch()
    {
        Debug.Log("[GRADE] Batchmode: abriendo la escena y armando el barrido.");
        Lanzar();
    }

    [MenuItem("Appquarium TV/🎨 Barrido de grado (Editor, sin build)", priority = 210)]
    public static void Lanzar()
    {
        var dir = RutaSalida();
        Directory.CreateDirectory(dir);

        // La escena tiene que ser TvScene: el acuario lo construye CastReceiver al recibir el
        // INIT, así que sin ella no hay nada que capturar. En batchmode el Editor arranca con
        // una escena vacía, y con el Editor abierto el user puede tener otra cosa delante.
        var activa = EditorSceneManager.GetActiveScene();
        if (activa.path != RutaEscena)
        {
            if (activa.isDirty && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Barrido de grado",
                    $"Hay cambios sin guardar en '{activa.name}'. Se abrirá {RutaEscena} y se perderán.",
                    "Abrir TvScene", "Cancelar"))
            {
                Debug.Log("[GRADE] Cancelado por el user: la escena activa tenía cambios sin guardar.");
                return;
            }
            Debug.Log($"[GRADE] Abriendo {RutaEscena} (la activa era '{activa.name}').");
            EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        }
        Debug.Log($"[GRADE] Barrido de {Variantes.Length} variantes → {dir}");
        Debug.Log("[GRADE] " + (File.Exists(Path.Combine(dir, "state.json"))
            ? "Usando el estado de _gradesweep/state.json."
            : "Sin _gradesweep/state.json → estado por defecto (bg_tropical / sub_sand / day)."));

        SessionState.SetInt(KeyPaso, 0);
        SessionState.SetFloat(KeyProxima, 0f);
        SessionState.SetString(KeyMedias, "");
        // Vigilante: si el play mode no llega a arrancar, hay que abortar en vez de dejar un
        // Unity de batchmode vivo para siempre pareciendo que trabaja.
        SessionState.SetFloat(KeyLimite, (float)(EditorApplication.timeSinceStartup + 240.0));

        if (!Application.isPlaying) EditorApplication.EnterPlaymode();
    }

    [MenuItem("Appquarium TV/🎨 Barrido de grado — CANCELAR", priority = 211)]
    public static void Cancelar()
    {
        SessionState.SetInt(KeyPaso, -1);
        Debug.Log("[GRADE] Barrido cancelado.");
    }

    // ── Máquina de estados ───────────────────────────────────────────────────
    // paso 0        = esperando a que arranque el play mode; inyecta el INIT
    // paso 1        = esperando a que la escena deje de cargar (peces + sombras estables)
    // paso 2..2+N-1 = capturando la variante (paso-2)
    // paso -1       = inactivo
    private static void Tick()
    {
        int paso = SessionState.GetInt(KeyPaso, -1);
        if (paso < 0) return;

        if (!Application.isPlaying)                      // aún entrando en play
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(KeyLimite, 0f))
                Abortar("el play mode no llegó a arrancar");
            return;
        }

        double ahora = EditorApplication.timeSinceStartup;
        if (ahora < SessionState.GetFloat(KeyProxima, 0f)) return;

        if (paso == 0)
        {
            if (!Inyectar()) { Abortar("no se pudo inyectar el estado"); return; }
            SessionState.SetInt(KeyCuenta, -1);
            SessionState.SetFloat(KeyEstable, (float)ahora);
            SessionState.SetFloat(KeyLimite, (float)(ahora + LimiteCargaSeg));
            Programar(1, 1.0);
            Debug.Log("[GRADE] INIT inyectado. Esperando a que la escena termine de cargar…");
            return;
        }

        if (paso == 1)
        {
            int cuenta   = CuentaCargada();
            int anterior = SessionState.GetInt(KeyCuenta, -1);

            if (cuenta != anterior)
            {
                SessionState.SetInt(KeyCuenta, cuenta);
                SessionState.SetFloat(KeyEstable, (float)ahora);
            }

            // ⚠ Además de la escena, hay que esperar a que EXISTA el render pipeline. URP no se
            // instancia hasta que se dibuja el primer frame, y `-executeMethod` corre bastante
            // antes de eso. Sin instancia, `SubmitRenderRequest` no está soportado y cualquier
            // captura saldría por el camino legacy: sin post-proceso y sin avisar.
            bool hayPipeline = RenderPipelineManager.currentPipeline != null;
            bool estable = cuenta > 0 && hayPipeline &&
                           (ahora - SessionState.GetFloat(KeyEstable, (float)ahora)) >= EstabilidadSeg;
            if (estable)
            {
                Debug.Log($"[GRADE] Escena cargada y estable ({cuenta} objetos entre peces y sombras), " +
                          $"pipeline={RenderPipelineManager.currentPipeline.GetType().Name}. Empieza el barrido.");
                Programar(2, 0.2);
                return;
            }

            if (ahora > SessionState.GetFloat(KeyLimite, 0f))
            {
                Abortar($"no se llegó a un estado capturable en {LimiteCargaSeg:F0} s " +
                        $"(objetos={cuenta}, pipeline={(hayPipeline ? "OK" : "NULL")}). " +
                        (hayPipeline
                            ? "Capturar ahora daría un tanque vacío y PNG que parecerían un resultado"
                            : "Sin pipeline de URP no hay post-proceso: ¿se está renderizando algo? " +
                              "Con -batchmode nunca lo habrá"));
                return;
            }

            Programar(1, 1.0);
            return;
        }

        int indice = paso - 2;
        if (indice >= Variantes.Length)
        {
            SessionState.SetInt(KeyPaso, -1);
            Debug.Log($"[GRADE] ✅ Barrido completo: {Variantes.Length} PNG en {RutaSalida()}");
            ComprobarQueMidioAlgo();
            EditorApplication.ExitPlaymode();
            if (Application.isBatchMode) EditorApplication.Exit(0);
            return;
        }

        // Aplicar la variante y capturar en el mismo tick: Rebuild() destruye el Volume viejo
        // (desactivándolo primero) y crea el nuevo, así que ya está activo al renderizar.
        var v = Variantes[indice];
        if (!Aplicar(v)) { Abortar("no encuentro PostProcessingSetup en la escena"); return; }
        if (!Capturar(v.nombre, indice)) { Abortar("fallo al capturar"); return; }

        Programar(paso + 1, EsperaVarianteSeg);
    }

    /// <summary>
    /// Señal observable de «la escena ya ha cargado»: peces vivos + contenedores de sombra de
    /// deco (`Shadow_<itemId>`, que DecorationPlacer crea al colocar cada una). Se mira que el
    /// número deje de cambiar en vez de fijar un tiempo, para que valga con cualquier estado.
    /// </summary>
    private static int CuentaCargada()
    {
        int n = Object.FindObjectsByType<FishAgent>(FindObjectsSortMode.None).Length;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Shadow_")) n++;
        return n;
    }

    private static double MediaLuminancia(Texture2D tex)
    {
        var px = tex.GetPixels32();
        double suma = 0;
        // Muestreo 1 de cada 16: sobra para detectar «todas iguales» y no cuesta nada.
        for (int i = 0; i < px.Length; i += 16)
            suma += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
        return suma / (px.Length / 16.0);
    }

    /// <summary>
    /// Al acabar: si todas las variantes dieron prácticamente la misma luminancia, el barrido
    /// NO ha medido nada y hay que decirlo a gritos en vez de entregar 8 PNG que parecen un
    /// resultado. Umbral generoso a propósito: variantes distintas mueven la media mucho más.
    /// </summary>
    private static void ComprobarQueMidioAlgo()
    {
        var trozos = SessionState.GetString(KeyMedias, "").Split(';');
        double min = double.MaxValue, max = double.MinValue;
        int n = 0;
        foreach (var t in trozos)
        {
            if (string.IsNullOrEmpty(t)) continue;
            if (!double.TryParse(t, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            min = System.Math.Min(min, v); max = System.Math.Max(max, v); n++;
        }
        if (n < 2) return;

        double rango = max - min;
        if (rango < 1.0)
            Debug.LogError($"[GRADE] ⚠⚠ Las {n} variantes tienen la MISMA luminancia (rango {rango:F3}). " +
                           "El post-proceso NO se está aplicando a la captura: las PNG no valen.");
        else
            Debug.Log($"[GRADE] Comprobación: la luminancia varía {rango:F1} entre variantes → el barrido sí mide.");
    }

    private static void Programar(int paso, double espera)
    {
        SessionState.SetInt(KeyPaso, paso);
        SessionState.SetFloat(KeyProxima, (float)(EditorApplication.timeSinceStartup + espera));
    }

    private static void Abortar(string motivo)
    {
        SessionState.SetInt(KeyPaso, -1);
        Debug.LogError($"[GRADE] Barrido abortado: {motivo}.");
        // En batchmode hay que salir con código != 0: si no, Unity se queda vivo para siempre
        // y el fallo pasa por "sigue trabajando".
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }

    // ── Piezas ───────────────────────────────────────────────────────────────

    private static string RutaSalida()
        => Path.Combine(Directory.GetCurrentDirectory(), CarpetaSalida);

    private static bool Inyectar()
    {
        var receiver = Object.FindFirstObjectByType<CastReceiver>();
        if (receiver == null) { Debug.LogError("[GRADE] No hay CastReceiver en la escena."); return false; }

        var ruta = Path.Combine(RutaSalida(), "state.json");
        string estado = File.Exists(ruta) ? File.ReadAllText(ruta) : EstadoPorDefecto;
        estado = estado.Replace("\r", "").Replace("\n", "").Replace("            ", "").Replace("        ", "");

        receiver.OnMessageReceived(JsonUtility.ToJson(new Wrapper { type = "INIT", payload = estado }));
        return true;
    }

    [System.Serializable]
    private class Wrapper { public string type; public string payload; }

    private static bool Aplicar(Variante v)
    {
        var pp = Object.FindFirstObjectByType<PostProcessingSetup>();
        if (pp == null) return false;

        pp.enableBloom       = v.bloom;
        pp.bloomIntensity    = v.bloomIntensity;
        pp.enableTonemapping = v.tonemapping;
        pp.saturation        = v.saturation;
        pp.contrast          = v.contrast;
        pp.postExposure      = v.postExposure;
        pp.Rebuild();
        return true;
    }

    /// <summary>
    /// Renderiza la cámara a una RenderTexture de 1920×1080 y la vuelca a PNG.
    /// Se hace así y no con ScreenCapture para no depender del tamaño que tenga el Game view
    /// (si cambia entre variantes, las capturas dejan de ser comparables).
    ///
    /// ⚠⚠ NO usar `cam.Render()`. La primera versión de esto lo hacía y el barrido salió
    /// «bien»: 8 PNG, exit 0, y las 8 con **exactamente** la misma luminancia y saturación.
    /// En URP, `Camera.Render()` no ejecuta el pipeline completo y **se salta el
    /// post-proceso**, sin avisar de nada. La vía soportada desde 2023.1 es
    /// `SubmitRenderRequest` con `UniversalRenderPipeline.SingleCameraRequest`
    /// (`RenderSingleCamera` está marcado obsoleto justo por esto).
    /// </summary>
    private static bool Capturar(string nombre, int indice)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[GRADE] No hay Camera.main."); return false; }

        // Si el post-proceso no está activo en la cámara, TODAS las capturas saldrían iguales
        // y el barrido no diría nada. Mejor fallar aquí que entregar 8 PNG idénticas.
        var datos = cam.GetComponent<UniversalAdditionalCameraData>();
        if (datos != null && !datos.renderPostProcessing)
        {
            Debug.LogError("[GRADE] La cámara tiene renderPostProcessing=false → el barrido no mediría nada.");
            return false;
        }

        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1
        };

        var activaPrev = RenderTexture.active;
        try
        {
            var peticion = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, peticion))
            {
                var actual = RenderPipelineManager.currentPipeline;
                Debug.LogError("[GRADE] SingleCameraRequest no soportado. currentPipeline=" +
                    (actual == null ? "NULL" : actual.GetType().Name) +
                    (actual == null && Application.isBatchMode
                        ? " → es esto: en -batchmode no hay pantalla, no se instancia URP, y sin " +
                          "instancia no hay post-proceso. Lanza el Editor con -executeMethod pero " +
                          "SIN -batchmode (ver la cabecera de este fichero)."
                        : ""));
                return false;
            }
            RenderPipeline.SubmitRenderRequest(cam, peticion);

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            var archivo = Path.Combine(RutaSalida(), $"{indice:00}_{nombre}.png");
            File.WriteAllBytes(archivo, tex.EncodeToPNG());

            // Guarda de «esto no está midiendo nada»: si el post-proceso no se aplicara, todas
            // las variantes saldrían con la misma media y el barrido parecería correcto. Ya
            // pasó una vez (cam.Render()), así que ahora el arnés se vigila a sí mismo.
            double media = MediaLuminancia(tex);
            SessionState.SetString(KeyMedias, SessionState.GetString(KeyMedias, "") + media.ToString("F3") + ";");
            Object.DestroyImmediate(tex);
            Debug.Log($"[GRADE] {indice + 1}/{Variantes.Length} → {Path.GetFileName(archivo)} (lum media {media:F2})");
        }
        finally
        {
            RenderTexture.active = activaPrev;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
        return true;
    }
}
