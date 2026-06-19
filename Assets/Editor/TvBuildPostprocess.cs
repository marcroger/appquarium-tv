using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Post-build step: patches StreamingAssets/aa/settings.json after every WebGL player build.
///
/// Unity Addressables generates settings.json with two bugs:
///   1. Double-slash in catalog paths (bundles//catalog_x.hash) — caused by profile variable
///      kRemoteLoadPath being resolved with a trailing separator before the catalog suffix.
///   2. m_DisableCatalogUpdateOnStart: false — Addressables 3.0 downloads the remote catalog
///      even when the hash matches local, which crashes in WebGL + Exception Support: None.
///
/// This postprocess runs automatically after Build Player and fixes both bugs in-place,
/// so the file is correct before deploy — no manual patching needed.
/// </summary>
public class TvBuildPostprocess : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;

        var settingsPath = Path.Combine(
            report.summary.outputPath,
            "StreamingAssets", "aa", "settings.json");

        if (!File.Exists(settingsPath))
        {
            Debug.LogWarning("[TvBuildPostprocess] settings.json not found at: " + settingsPath);
            return;
        }

        var json = File.ReadAllText(settingsPath, System.Text.Encoding.UTF8);
        var patched = json
            .Replace("bundles//",       "bundles/")
            .Replace("addressables//",  "addressables/")
            .Replace("\"m_DisableCatalogUpdateOnStart\":false",
                     "\"m_DisableCatalogUpdateOnStart\":true");

        if (json == patched)
        {
            Debug.Log("[TvBuildPostprocess] settings.json OK — no patches needed.");
            return;
        }

        File.WriteAllText(settingsPath, patched, new System.Text.UTF8Encoding(false));
        Debug.Log("[TvBuildPostprocess] ✅ settings.json patched: double-slash + DisableCatalogUpdateOnStart=true");
    }
}
