using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Menu items para sincronizar scripts/SOs/recursos desde el proyecto mobile.
/// Invoca Tools/SyncFromMobile.ps1 (PowerShell) en una ventana nueva para que
/// el output sea legible y la interacción (confirmaciones) funcione.
///
/// Ver SYNC_NOTES.md y Tools/SyncFromMobile.ps1 para detalles.
/// </summary>
public static class SyncFromMobileMenu
{
    private const string ScriptRelPath = "Tools/SyncFromMobile.ps1";

    [MenuItem("Appquarium TV/🔄 Sync from Mobile (interactive)", priority = 10)]
    public static void SyncInteractive() => RunScript(extraArgs: "");

    [MenuItem("Appquarium TV/🔄 Sync from Mobile (dry-run, list diffs)", priority = 11)]
    public static void SyncDryRun() => RunScript(extraArgs: "-DryRun");

    [MenuItem("Appquarium TV/🔄 Sync from Mobile (copy all, no prompt)", priority = 12)]
    public static void SyncYes()
    {
        bool confirm = EditorUtility.DisplayDialog(
            "Sync from Mobile — Copy All",
            "Esto copia TODOS los archivos que difieren desde mobile a TV sin pedir confirmación archivo por archivo.\n\n" +
            "Recomendado: cerrar Unity TV primero (algunos scripts pueden disparar recompilación parcial).\n\n" +
            "¿Continuar?",
            "Sí, copiar todo", "Cancelar");
        if (!confirm) return;
        RunScript(extraArgs: "-Yes");
    }

    private static void RunScript(string extraArgs)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string scriptPath  = Path.Combine(projectRoot, ScriptRelPath);

        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog("Sync script not found",
                $"No encuentro:\n{scriptPath}\n\nVerifica que existe y reintenta.",
                "OK");
            return;
        }

        // Abre el script en una nueva ventana PowerShell para que el usuario vea
        // el diff/confirmaciones. Usamos -NoExit para que la ventana persista al terminar
        // y el usuario pueda leer el resumen.
        var psi = new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-NoExit -ExecutionPolicy Bypass -File \"{scriptPath}\" {extraArgs}",
            WorkingDirectory = projectRoot,
            UseShellExecute  = true, // false would suppress the visible window
            CreateNoWindow   = false
        };

        try
        {
            Process.Start(psi);
            Debug.Log($"[Sync] PowerShell window launched. Args: '{extraArgs}'");
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Error lanzando script",
                $"No se pudo lanzar PowerShell:\n{ex.Message}",
                "OK");
        }
    }
}
