using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sonda de una sola pregunta: <b>¿se está renderizando de verdad, y con URP?</b>
///
/// Nace el 2026-08-21 porque el barrido de grado falló dos veces por el mismo sitio y las dos
/// veces por suposición mía: primero `cam.Render()` (que en URP se salta el post-proceso sin
/// avisar) y después esperar a `RenderPipelineManager.currentPipeline`, que se quedó NULL 180 s
/// con la escena ya cargada. En vez de probar una tercera corazonada, esto MIDE:
///
///   · qué asset de pipeline hay configurado,
///   · si existe instancia de pipeline (y cuándo),
///   · cuántas veces se dispara realmente el callback de render de URP,
///   · si `ScreenCapture` produce un fichero y si tiene contenido.
///
///   Unity.exe -projectPath . -executeMethod TvRenderProbe.Ejecutar -logFile probe.log
/// </summary>
[InitializeOnLoad]
public static class TvRenderProbe
{
    private const string KeyActiva = "TvRenderProbe.activa";
    private const string KeyFin    = "TvRenderProbe.fin";
    private const string KeyPrimer = "TvRenderProbe.primerRender";
    private const string Salida    = "_gradesweep/probe.png";

    private static int _frames;

    static TvRenderProbe()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        RenderPipelineManager.beginCameraRendering -= AlRenderizar;
        RenderPipelineManager.beginCameraRendering += AlRenderizar;
    }

    private static void AlRenderizar(ScriptableRenderContext ctx, Camera cam)
    {
        _frames++;
        if (SessionState.GetFloat(KeyPrimer, -1f) < 0f)
            SessionState.SetFloat(KeyPrimer, (float)EditorApplication.timeSinceStartup);
    }

    [MenuItem("Appquarium TV/🔬 Sonda de render (diagnóstico)", priority = 212)]
    public static void Ejecutar()
    {
        var activa = EditorSceneManager.GetActiveScene();
        if (activa.path != "Assets/Scenes/TvScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/TvScene.unity", OpenSceneMode.Single);

        _frames = 0;
        SessionState.SetFloat(KeyPrimer, -1f);
        SessionState.SetBool(KeyActiva, true);
        SessionState.SetFloat(KeyFin, (float)(EditorApplication.timeSinceStartup + 25.0));
        Debug.Log("[PROBE] Arrancando. Play mode + 25 s de observación.");
        if (!Application.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActiva, false)) return;
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(KeyFin, 0f)) return;
        SessionState.SetBool(KeyActiva, false);

        var assetPipeline = GraphicsSettings.currentRenderPipeline;
        var instancia     = RenderPipelineManager.currentPipeline;
        float primer      = SessionState.GetFloat(KeyPrimer, -1f);

        Debug.Log("[PROBE] ── resultado ──────────────────────────────");
        Debug.Log($"[PROBE] isPlaying={Application.isPlaying} isBatchMode={Application.isBatchMode} " +
                  $"pantalla={Screen.width}x{Screen.height}");
        Debug.Log($"[PROBE] asset de pipeline = {(assetPipeline == null ? "NULL" : assetPipeline.name)}");
        Debug.Log($"[PROBE] instancia (currentPipeline) = {(instancia == null ? "NULL" : instancia.GetType().Name)}");
        Debug.Log($"[PROBE] callbacks beginCameraRendering = {_frames}" +
                  (primer < 0 ? "  → URP NO ha renderizado NI UNA VEZ" : $"  (el primero a los {primer:F0} s)"));

        var cam = Camera.main;
        Debug.Log($"[PROBE] Camera.main = {(cam == null ? "NULL" : cam.name)}");

        if (cam != null)
        {
            var peticion = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest();
            Debug.Log($"[PROBE] SupportsRenderRequest = {RenderPipeline.SupportsRenderRequest(cam, peticion)}");
        }

        // ¿Y ScreenCapture? Es la vía que sí pasa por el frame final ya post-procesado.
        var ruta = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), Salida);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ruta));
        if (System.IO.File.Exists(ruta)) System.IO.File.Delete(ruta);
        ScreenCapture.CaptureScreenshot(ruta);
        Debug.Log($"[PROBE] ScreenCapture solicitado → {ruta} (se comprueba en 3 s)");

        SessionState.SetBool("TvRenderProbe.comprobar", true);
        SessionState.SetFloat("TvRenderProbe.finComprobar", (float)(EditorApplication.timeSinceStartup + 3.0));
        EditorApplication.update -= TickComprobar;
        EditorApplication.update += TickComprobar;
    }

    // ── Segunda prueba: ¿el Volume afecta al render, o es mi captura la que se lo salta? ──
    // Se toman dos capturas por ScreenCapture (que fotografía el frame final ya post-procesado,
    // camino totalmente distinto al SubmitRenderRequest del barrido): una con el grado actual y
    // otra con un grado EXTREMO. Si la segunda no cambia, el Volume no está afectando a nada.
    private const string KeyGrado = "TvRenderProbe.grado";
    private const string KeyFinG  = "TvRenderProbe.finGrado";

    [MenuItem("Appquarium TV/🔬 Sonda de grado (¿el Volume afecta?)", priority = 213)]
    public static void PruebaGrado()
    {
        var activa = EditorSceneManager.GetActiveScene();
        if (activa.path != "Assets/Scenes/TvScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/TvScene.unity", OpenSceneMode.Single);

        SessionState.SetInt(KeyGrado, 1);
        SessionState.SetFloat(KeyFinG, (float)(EditorApplication.timeSinceStartup + 20.0));
        Debug.Log("[GRADO] Arrancando: 20 s de carga y luego dos capturas por ScreenCapture.");
        EditorApplication.update -= TickGrado;
        EditorApplication.update += TickGrado;
        if (!Application.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void TickGrado()
    {
        int paso = SessionState.GetInt(KeyGrado, 0);
        if (paso == 0) return;
        if (!Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(KeyFinG, 0f)) return;

        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "_gradesweep", "grado");
        System.IO.Directory.CreateDirectory(dir);
        var pp = Object.FindFirstObjectByType<PostProcessingSetup>();

        if (paso == 1)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "A_normal.png"));
            Debug.Log("[GRADO] Captura A (grado actual) solicitada.");
            SessionState.SetInt(KeyGrado, 2);
            SessionState.SetFloat(KeyFinG, (float)(EditorApplication.timeSinceStartup + 3.0));
            return;
        }

        if (paso == 2)
        {
            if (pp == null) { Debug.LogError("[GRADO] No hay PostProcessingSetup."); SessionState.SetInt(KeyGrado, 0); return; }
            pp.enableBloom = false; pp.enableTonemapping = false;
            pp.saturation = -100f; pp.contrast = 0f; pp.postExposure = -1f;
            pp.Rebuild();
            Debug.Log("[GRADO] Grado EXTREMO aplicado (sat -100, exp -1). Capturando B en 3 s.");
            SessionState.SetInt(KeyGrado, 3);
            SessionState.SetFloat(KeyFinG, (float)(EditorApplication.timeSinceStartup + 3.0));
            return;
        }

        if (paso == 3)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "B_extremo.png"));
            Debug.Log("[GRADO] Captura B (extremo) solicitada.");
            SessionState.SetInt(KeyGrado, 4);
            SessionState.SetFloat(KeyFinG, (float)(EditorApplication.timeSinceStartup + 4.0));
            return;
        }

        SessionState.SetInt(KeyGrado, 0);
        Debug.Log("[GRADO] Hecho. Compara _gradesweep/grado/A_normal.png con B_extremo.png: " +
                  "si son iguales, el Volume NO afecta al render.");
        EditorApplication.ExitPlaymode();
    }

    private static void TickComprobar()
    {
        if (!SessionState.GetBool("TvRenderProbe.comprobar", false)) return;
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat("TvRenderProbe.finComprobar", 0f)) return;
        SessionState.SetBool("TvRenderProbe.comprobar", false);

        var ruta = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), Salida);
        Debug.Log(System.IO.File.Exists(ruta)
            ? $"[PROBE] ScreenCapture SÍ escribió: {new System.IO.FileInfo(ruta).Length} bytes"
            : "[PROBE] ScreenCapture NO escribió nada");
        Debug.Log("[PROBE] ── fin ────────────────────────────────────");
        EditorApplication.ExitPlaymode();
    }
}
