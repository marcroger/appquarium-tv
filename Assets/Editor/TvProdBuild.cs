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
        if (!PreflightAudio()) { EditorApplication.Exit(1); return; }

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

    /// <summary>
    /// Aborta el build si falta algún clip de ambiente o si su import setting es el que
    /// mata al device.
    ///
    /// ⚠ Por qué existe esta guarda (2026-08-15): AudioManager pide 3 clips a Resources y
    /// sigue adelante si no están — sólo hace Debug.Log, que NO viaja por el canal Cast.
    /// Los .wav estaban en .gitignore, desaparecieron del disco sin que nada lo notara y
    /// el build del 12-ago salió con 1 de 3 canales. Nadie se enteró en ~2 meses: no hay
    /// error, sólo silencio. Un build que no suena tiene que fallar, no salir "bien".
    ///
    /// loadType 0 (Decompress on Load) es el otro fallo recurrente: descomprime el WAV
    /// entero en el heap WASM → OOM → Chrome muere → pantalla azul sin peces. El síntoma
    /// es idéntico a una caída de R2 y ya costó una sesión entera de diagnóstico.
    /// Se cuela cada vez que se sincronizan los .meta del móvil (Android tiene 6 GB y le da igual).
    /// </summary>
    private static bool PreflightAudio()
    {
        string[] clips = { "ambient_water", "ambient_bubbles", "ambient_music" };
        bool ok = true;

        foreach (var nombre in clips)
        {
            string ruta = null;
            foreach (var ext in new[] { ".wav", ".mp3", ".ogg" })
            {
                var p = "Assets/Resources/Audio/" + nombre + ext;
                if (System.IO.File.Exists(p)) { ruta = p; break; }
            }

            if (ruta == null)
            {
                Debug.LogError("[ProdBuild] FALTA el clip Assets/Resources/Audio/" + nombre +
                               ".(wav|mp3|ogg) → ese canal saldría MUDO. Original en el repo móvil.");
                ok = false;
                continue;
            }

            var imp = AssetImporter.GetAtPath(ruta) as AudioImporter;
            if (imp != null && imp.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                Debug.LogError("[ProdBuild] " + ruta + " tiene loadType=DecompressOnLoad → OOM en el Cast. " +
                               "Ponlo en CompressedInMemory (loadType: 2 en el .meta).");
                ok = false;
            }
        }

        Debug.Log(ok ? "[ProdBuild] Preflight de audio OK: 3/3 clips presentes y comprimidos en memoria."
                     : "[ProdBuild] Preflight de audio FALLIDO — build abortado.");
        return ok;
    }
}
