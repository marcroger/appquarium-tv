using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Aborta cualquier build de WebGL que salga SIN el token de los bundles.
///
/// Por qué hace falta: desde el 2026-08-21 el token ya no está en git (el repo es público,
/// ver TvBundleAuth). Lo aporta Assets/Scripts/Core/TvBundleAuthSecret.cs, que está en
/// .gitignore — o sea que un clon limpio, o un disco nuevo, compila perfectamente y produce
/// un player que NO puede descargar un solo bundle: el Worker le devuelve 401 y la tele se
/// queda con el acuario vacío. Sin errores de compilación, sin excepciones en runtime.
///
/// Es exactamente el patrón que este proyecto ya pagó con el audio (dos canales mudos
/// durante dos meses) y la respuesta es la misma que allí: que el build FALLE en vez de
/// salir "bien". Va en un IPreprocessBuildWithReport, no dentro de TvProdBuild, para que
/// también cubra la ruta por GUI (File → Build Settings → Build).
/// </summary>
public class TvBundleAuthPreflight : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;
        if (Ok()) return;

        throw new BuildFailedException(
            "[AuthPreflight] Falta el token de los bundles → este player no podría descargar " +
            "NINGÚN bundle (el Worker devuelve 401) y la tele saldría vacía.\n" +
            "Arreglo: copiar Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample a " +
            "Assets/Scripts/Core/TvBundleAuthSecret.cs y poner el token real (el mismo que " +
            "está en el secret BUNDLE_TOKENS del Worker). Ese fichero NO va a git a propósito.");
    }

    /// <summary>
    /// Pregunta al propio runtime, no al disco: así se verifica que el partial method
    /// ha entrado de verdad en la compilación, no sólo que el fichero exista.
    /// </summary>
    public static bool Ok()
    {
        var hay = TvBundleAuth.HasFallbackToken;
        Debug.Log(hay
            ? "[AuthPreflight] OK: el build llevará token constante para los bundles."
            : "[AuthPreflight] FALLIDO: no hay token (falta TvBundleAuthSecret.cs).");
        return hay;
    }
}
