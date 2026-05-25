using UnityEditor;
using UnityEngine;
using System.IO;

public static class TvBuildTools
{
    [MenuItem("Appquarium TV/1. Reduce Textures for WebGL (512px)")]
    static void ReduceTextures512() => ApplyTextureOverride(512);

    [MenuItem("Appquarium TV/2. Reduce Textures for WebGL (256px)")]
    static void ReduceTextures256() => ApplyTextureOverride(256);

    [MenuItem("Appquarium TV/3. Restore Textures (1024px)")]
    static void RestoreTextures() => ApplyTextureOverride(1024);

    static void ApplyTextureOverride(int maxSize)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/ThirdParty", "Assets/Resources" });
        int count = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var settings = importer.GetPlatformTextureSettings("WebGL");
                bool changed = false;

                if (!settings.overridden || settings.maxTextureSize != maxSize)
                {
                    settings.overridden    = true;
                    settings.maxTextureSize = maxSize;
                    settings.format        = TextureImporterFormat.Automatic;
                    importer.SetPlatformTextureSettings(settings);
                    importer.SaveAndReimport();
                    changed = true;
                }

                if (changed) count++;

                if (count % 20 == 0)
                    EditorUtility.DisplayProgressBar(
                        $"Textures → {maxSize}px",
                        path,
                        (float)count / guids.Length);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[TvBuildTools] {count} textures set to max {maxSize}px for WebGL.");
    }
}
