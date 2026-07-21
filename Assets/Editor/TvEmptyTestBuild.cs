using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// RUNG "escena vacía" — build de DIAGNÓSTICO rollback-safe para la investigación del
/// corte Cast ~3min (ver CAST_DISCONNECT_INVESTIGATION.md).
///
/// Aísla si el corte lo dispara el ENGINE CORE de Unity (cualquier build WebGL) o
/// NUESTRO contenido (escena TvScene: acuario, peces, shaders, bundles).
///   - Escena vacía CORTA ~3min  → engine core → infixeable desde la app.
///   - Escena vacía AGUANTA >4min → nuestro contenido → HAY fix (iterar sobre la escena).
///
/// ⚠ ROLLBACK-SAFE POR DISEÑO:
///   - Construye a la carpeta APARTE  webgl-output-empty/  (NO toca webgl-output/ de prod).
///   - Los ficheros se llaman  webgl-output-empty.*  (NO colisionan con  webgl-output.*  de prod
///     ni en disco ni en R2/Build/).
///   - NO modifica EditorBuildSettings ni PlayerSettings.
///   - Restaura la escena que tenía abierta el usuario al terminar.
///   Rollback total tras el test = re-desplegar el receiver de producción a R2 /index.html.
///
/// ⚠ Guarda tu trabajo (la escena abierta) antes de ejecutar: se crea una escena temporal.
/// </summary>
public static class TvEmptyTestBuild
{
    const string OutDir    = "webgl-output-empty";
    const string SceneDir  = "Assets/_EmptyCastTest";
    const string ScenePath = SceneDir + "/EmptyCastTest.unity";

    [MenuItem("Appquarium TV/\U0001F9EA Build Empty Cast Test (rollback-safe)")]
    static void BuildEmpty()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            EditorUtility.DisplayDialog(
                "Empty Cast Test",
                "El target de build activo NO es WebGL.\n\nCambia a WebGL en File > Build Settings y vuelve a intentarlo.",
                "Vale");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Build Empty Cast Test (rollback-safe)",
                "Va a construir una ESCENA VACÍA (cámara + cubo + luz) a la carpeta aparte '" + OutDir + "'.\n\n" +
                "NO toca la build de producción (webgl-output/) ni ningún fichero de R2.\n" +
                "Restaura tu escena abierta al terminar, pero GUARDA tu trabajo por si acaso.\n\n" +
                "El build WebGL puede tardar (menos que el del acuario: no hay assets). ¿Continuar?",
                "Construir", "Cancelar"))
            return;

        // 1. Guardar el setup de escenas del editor para restaurarlo al final.
        var prevSetup = EditorSceneManager.GetSceneManagerSetup();

        // 2. Crear la escena vacía mínima y guardarla en disco.
        if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.28f, 0.45f);
        camGo.transform.position = new Vector3(0f, 0.6f, -4f);
        camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TestCube";
        cube.transform.position = Vector3.zero;

        EditorSceneManager.SaveScene(scene, ScenePath);

        // 3. Restaurar la escena que tenía abierta el usuario.
        try { EditorSceneManager.RestoreSceneManagerSetup(prevSetup); } catch { /* sin escena previa */ }

        // 4. Build SOLO de la escena vacía → carpeta aparte, con los PlayerSettings activos.
        if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
        var opts = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = OutDir,
            target           = BuildTarget.WebGL,
            options          = BuildOptions.None
        };

        Debug.Log("[EmptyTest] Iniciando build WebGL de escena vacía → " + OutDir + "/ (NO toca producción).");
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("[EmptyTest] ✅ BUILD OK (" + (s.totalSize / 1048576) + " MB). Ficheros en " +
                      OutDir + "/Build/webgl-output-empty.*  → siguiente: desplegar a R2 /Build/ (nombres distintos, no pisan prod).");
        }
        else
        {
            Debug.LogError("[EmptyTest] ❌ BUILD " + s.result + " — " + s.totalErrors + " errores. Revisa la consola.");
        }
    }
}
