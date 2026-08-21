using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crea el render pipeline que a este proyecto le falta, y permite encenderlo y apagarlo para
/// poder COMPARAR.
///
/// Contexto (2026-08-21, ver CAST_PARIDAD_VISUAL.md §0): la TV renderiza con el pipeline
/// built-in porque `GraphicsSettings` apunta a un URP asset que no existe. Consecuencia: el
/// `Volume` de `PostProcessingSetup` no afecta a nada — ni bloom, ni tonemapping, ni saturación —
/// y `renderScale` tampoco se aplica. Verificado en el player desplegado, no sólo en el Editor.
///
/// ⚠ Encender URP NO es gratis ni obviamente bueno: la escena, sus luces y sus 4 shaders propios
/// se escribieron con built-in en marcha. Por eso esto no «arregla» nada por su cuenta: crea el
/// asset y da un interruptor, para medir las dos versiones con el mismo acuario y decidir con
/// capturas delante.
///
/// Valores copiados del móvil (`Mobile_RPAsset.asset`), que es una configuración ya validada en
/// producción, con una excepción: `renderScale` va a 0,7 (lo que `TvSceneBootstrap` intenta
/// poner en runtime) en vez del 0,8 del móvil.
/// </summary>
public static class TvUrpSetup
{
    private const string Carpeta      = "Assets/Settings";
    private const string RutaRenderer = Carpeta + "/TvUniversalRenderer.asset";
    private const string RutaPipeline = Carpeta + "/TvRenderPipeline.asset";

    [MenuItem("Appquarium TV/🎛 Pipeline — crear el URP asset", priority = 220)]
    public static void Crear()
    {
        if (!Directory.Exists(Carpeta)) Directory.CreateDirectory(Carpeta);

        if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RutaPipeline) != null)
        {
            Debug.Log("[URP] Ya existe " + RutaPipeline + " — no se recrea.");
            return;
        }

        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(renderer, RutaRenderer);

        // ⚠⚠ Sin esto, `postProcessData` se queda a NULL y **URP se salta TODO el post-proceso
        // en silencio**: el Volume existe, la cámara lo tiene activado, y no pasa nada. Costó
        // tres tandas de barrido el 21-ago hasta que una variante deliberadamente extrema
        // (saturación −100) salió idéntica a las demás.
        // `Create()` sí recarga los recursos del PIPELINE, pero el renderer se crea aparte y se
        // queda sin los suyos. URP lo resuelve con `ResourceReloader`, que es interno al paquete;
        // desde fuera se carga el asset por ruta, que es explícito y no depende de API privada.
        renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
            UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
        EditorUtility.SetDirty(renderer);

        // Create() rellena los recursos internos del pipeline (ResourceReloader). Hacerlo a mano
        // con CreateInstance dejaría referencias nulas y el post-proceso no funcionaría.
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        AssetDatabase.CreateAsset(pipeline, RutaPipeline);

        var so = new SerializedObject(pipeline);
        Poner(so, "m_RenderScale",                  0.7f);   // el móvil usa 0,8; TV pide 0,7
        Poner(so, "m_MSAA",                         1);      // 1 = desactivado, igual que el móvil
        Poner(so, "m_SupportsHDR",                  true);
        Poner(so, "m_UseSRPBatcher",                true);
        Poner(so, "m_MainLightShadowsSupported",    true);
        Poner(so, "m_MainLightShadowmapResolution", 1024);
        Poner(so, "m_AdditionalLightsRenderingMode", 1);     // per-pixel, como el móvil
        Poner(so, "m_AdditionalLightsPerObjectLimit", 4);
        Poner(so, "m_AdditionalLightShadowsSupported", false);
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Comprobar, no suponer: un renderer sin postProcessData produce exactamente el mismo
        // síntoma que no tener pipeline — todo se ve, pero el grado no se aplica.
        if (renderer.postProcessData == null)
        {
            Debug.LogError("[URP] El renderer se ha quedado SIN postProcessData → el post-proceso " +
                           "no funcionaría y no habría ningún aviso. Asset creado pero INSERVIBLE.");
            return;
        }
        Debug.Log($"[URP] Creados {RutaPipeline} y {RutaRenderer} " +
                  $"(postProcessData = {renderer.postProcessData.name}). NO se ha activado todavía.");
    }

    private static void Poner(SerializedObject so, string campo, object valor)
    {
        var p = so.FindProperty(campo);
        if (p == null) { Debug.LogWarning($"[URP] El asset no tiene el campo '{campo}' en esta versión de URP."); return; }
        switch (valor)
        {
            case float f: p.floatValue = f; break;
            case int i:   p.intValue   = i; break;
            case bool b:  p.boolValue  = b; break;
        }
    }

    [MenuItem("Appquarium TV/🎛 Pipeline — ACTIVAR URP", priority = 221)]
    public static void Activar()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RutaPipeline);
        if (pipeline == null) { Debug.LogError("[URP] No existe " + RutaPipeline + ". Ejecuta 'crear' primero."); return; }

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline         = pipeline;   // el nivel activo, por si lo pisara
        AssetDatabase.SaveAssets();
        Debug.Log("[URP] ACTIVADO. currentRenderPipeline = " +
                  (GraphicsSettings.currentRenderPipeline == null ? "NULL (¡mal!)" : GraphicsSettings.currentRenderPipeline.name));
    }

    [MenuItem("Appquarium TV/🎛 Pipeline — DESACTIVAR (volver a built-in)", priority = 222)]
    public static void Desactivar()
    {
        GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline         = null;
        AssetDatabase.SaveAssets();
        Debug.Log("[URP] Desactivado: se vuelve al pipeline built-in (el estado en el que está producción hoy).");
    }

    /// <summary>Diagnóstico rápido, para no volver a dar por hecho lo que hay puesto.</summary>
    [MenuItem("Appquarium TV/🎛 Pipeline — ¿qué hay activo?", priority = 223)]
    public static void Estado()
    {
        var actual = GraphicsSettings.currentRenderPipeline;
        Debug.Log("[URP] currentRenderPipeline = " + (actual == null ? "NULL → BUILT-IN" : actual.name) +
                  " · defaultRenderPipeline = " + (GraphicsSettings.defaultRenderPipeline == null ? "NULL" : GraphicsSettings.defaultRenderPipeline.name) +
                  " · asset en disco = " + (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RutaPipeline) == null ? "no" : "sí"));
    }

    // ── Entradas de línea de comandos, para encadenar medición sin tocar la interfaz ──
    public static void CrearBatch()      { Crear();      Estado(); }
    public static void ActivarBatch()    { Crear();      Activar(); Estado(); }
    public static void DesactivarBatch() { Desactivar(); Estado(); }
}
