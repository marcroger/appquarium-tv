using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build del player de PRODUCCIÓN (TvScene → webgl-output/) sin diálogos, para batchmode:
///
///   Unity.exe -batchmode -quit -nographics -projectPath . -buildTarget WebGL \
///             -executeMethod TvProdBuild.BuildProd -logFile build-prod.log
///
/// Añadido 2026-07-27, tras demostrar en el rig vacío que quitar 7 paquetes de runtime
/// sin usar + poner el Code Optimization de WebGL en DiskSizeLTO baja el .wasm un 42 %
/// (44,2 → 25,4 MB), el pico de memoria de 794 a 654 MB y elimina la fuga
/// (18,8 → 0,1 MB/min) — con eso la sesión Cast dejó de cortarse.
/// Ver CAST_DISCONNECT_INVESTIGATION.md.
///
/// ⚠ Escribe en webgl-output/ (local). NO despliega nada a R2.
/// </summary>
public static class TvProdBuild
{
    public static void BuildProd()
    {
        // Asegura el mismo nivel de optimización que se validó en el rig.
        TvWasmOptimize.SetDiskSizeLTO();

        var scenes = new[] { "Assets/Scenes/TvScene.unity" };
        var opts = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = "webgl-output",
            target           = BuildTarget.WebGL,
            options          = BuildOptions.None,
        };

        Debug.Log("[ProdBuild] Construyendo TvScene → webgl-output/ (no toca R2).");
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log("[ProdBuild] " + s.result + " · " + s.totalSize + " bytes · " + s.totalTime +
                  " · errores=" + s.totalErrors);
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
