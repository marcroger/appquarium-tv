using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lee y ajusta el <b>Code Optimization</b> de WebGL, que decide con qué nivel de
/// optimización enlaza emscripten el <c>.wasm</c>.
///
/// ⚠ Este ajuste NO vive en ProjectSettings (está en <c>Library/EditorUserBuildSettings.asset</c>,
/// que no está en git), por eso nunca se había auditado. El valor por defecto de Unity es
/// <c>BuildTimes</c> ("Shorter Build Time"): el más rápido de compilar y el que produce el
/// <c>.wasm</c> MÁS GRANDE. Descubierto 2026-07-27 por la ruta de artefactos
/// <c>Library/Bee/artifacts/WebGL/build/<b>debug</b>_WebGL_wasm/</c>.
///
/// Contexto: el tamaño del <c>.wasm</c> determina el pico de memoria del renderer en el
/// device Cast, y ese pico es lo que tumba la sesión. Ver CAST_DISCONNECT_INVESTIGATION.md.
///
/// Se usa reflexión a propósito: el tipo vive en UnityEditor.WebGL.Extensions y así el
/// script compila aunque el módulo WebGL no esté instalado.
/// </summary>
/// <summary>
/// Fuerza el Code Optimization de WebGL a DiskSizeLTO en CUALQUIER build, venga de donde venga.
///
/// ⚠ 2026-08-15 — antes esto sólo lo hacía `TvProdBuild.BuildProd` (la ruta batchmode).
/// El ajuste vive en `Library/EditorUserBuildSettings.asset`, que **no está en git**: al
/// borrar la Library vuelve a `BuildTimes` y el `.wasm` pasa de 25,4 a 44,2 MB — que es la
/// causa raíz confirmada de los cortes de sesión Cast. Quien construyera desde
/// `File → Build Settings → Build` se lo saltaba sin enterarse.
/// </summary>
public class TvWasmOptimizePreBuild : UnityEditor.Build.IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;
        TvWasmOptimize.SetDiskSizeLTO();
        Debug.Log("[WasmOpt] Pre-build: Code Optimization forzado a DiskSizeLTO.");
    }
}

public static class TvWasmOptimize
{
    const string TypeName = "UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions";

    static PropertyInfo GetProp()
    {
        var t = Type.GetType(TypeName);
        if (t == null) { Debug.LogError("[WasmOpt] No se encuentra " + TypeName); return null; }
        var p = t.GetProperty("codeOptimization", BindingFlags.Public | BindingFlags.Static);
        if (p == null) Debug.LogError("[WasmOpt] El tipo no expone 'codeOptimization'");
        return p;
    }

    [MenuItem("Appquarium TV/📏 Ver Code Optimization del WASM")]
    public static void Report()
    {
        var p = GetProp(); if (p == null) return;
        Debug.Log("[WasmOpt] ACTUAL = " + p.GetValue(null) +
                  "   · posibles: " + string.Join(", ", Enum.GetNames(p.PropertyType)));
    }

    /// <summary>Pone el nivel más agresivo de reducción de tamaño disponible.</summary>
    public static void SetDiskSizeLTO()
    {
        var p = GetProp(); if (p == null) { EditorApplication.Exit(1); return; }
        var antes = p.GetValue(null);

        // Preferencia: DiskSizeLTO > DiskSize > el que haya. Los nombres varían entre versiones.
        string elegido = null;
        foreach (var candidato in new[] { "DiskSizeLTO", "DiskSize", "Size" })
            if (Array.IndexOf(Enum.GetNames(p.PropertyType), candidato) >= 0) { elegido = candidato; break; }

        if (elegido == null)
        {
            Debug.LogError("[WasmOpt] Ningún modo de tamaño disponible. Opciones: " +
                           string.Join(", ", Enum.GetNames(p.PropertyType)));
            EditorApplication.Exit(1); return;
        }

        p.SetValue(null, Enum.Parse(p.PropertyType, elegido));
        Debug.Log("[WasmOpt] " + antes + " → " + p.GetValue(null));
    }

    /// <summary>Entrada de batchmode: ajusta la optimización y construye el rig vacío.</summary>
    public static void SetAndBuildEmpty()
    {
        SetDiskSizeLTO();
        TvEmptyTestBuild.BuildEmptyBatch();
    }
}
