using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Convierte una deco GLB a formato "game-ready": usa las texturas ya extraídas a assets
/// sueltos (comprimidas) y monta un prefab que las referencia, en vez de servir el GLB.
///
/// ⚠ Por qué hace falta (MEDIDO el 2026-08-16, no supuesto):
/// GLTFast decodifica las texturas embebidas del GLB a Texture2D **RGBA32 sin comprimir** y
/// su importador NO expone ninguna opción de compresión (el .glb.meta sólo tiene mipmaps,
/// filtros y anisotropía). Tampoco declara `SupportsRemappedAssetType`, así que el remapeo
/// estándar de Unity (`externalObjects`) tampoco vale. No hay atajo.
///
/// El coste real: `greek_underwater_broken_statue_2.glb` pesa 2,45 MB en disco y produce un
/// bundle de 25,0 MB. Sus 3 JPG de 1024×1024 (0,63 MB cada uno) pasan a 5,33 MB cada uno
/// como RGBA32 con mipmaps = 16,0 MB. Sobre las 21 GLB del proyecto: 35 texturas, 186,6 MB
/// en RGBA32 frente a 46,7 MB en DXT5. Ahorro estimado 140 MB SIN tocar geometría.
///
/// La extracción de los .jpg y el fichero `mapeo.txt` los genera un script Python aparte
/// (parsear glTF en C# no aporta nada). Este script hace lo que sólo puede hacer el Editor:
/// ajustar el import, crear materiales y guardar el prefab.
///
/// El prefab referencia las MALLAS del GLB (que no se tocan) pero materiales nuevos que
/// apuntan a las texturas comprimidas. Addressables resuelve dependencias por objeto y no
/// por fichero, así que las texturas RGBA32 del GLB no deberían entrar en el bundle.
/// **Ese supuesto es justo lo que el prototipo tiene que confirmar con el número del bundle.**
/// </summary>
public static class TvDecoOptimize
{
    [MenuItem("Appquarium TV/🗜 Optimizar deco: statue_greek_2 (prototipo)", priority = 2)]
    public static void PrototipoEstatua()
    {
        Optimizar("Assets/ThirdParty/GreekStatues/greek_underwater_broken_statue_2.glb",
                  "Assets/Content/Decos/greek_underwater_broken_statue_2",
                  "Assets/ScriptableObjects/Decorations/deco_statue_greek_2.asset");
    }

    public static void Optimizar(string rutaGlb, string carpeta, string rutaSo)
    {
        // ── 1. importar las texturas extraídas COMPRIMIDAS ───────────────────
        var texturas = new List<Texture2D>();
        var rutasTex = new List<string>();
        for (int i = 0; ; i++)
        {
            string ruta = null;
            foreach (var ext in new[] { ".jpg", ".png" })
                if (File.Exists($"{carpeta}/tex_{i}{ext}")) { ruta = $"{carpeta}/tex_{i}{ext}"; break; }
            if (ruta == null) break;

            var ti = (TextureImporter)AssetImporter.GetAtPath(ruta);
            if (ti == null) { Debug.LogError("[DecoOpt] Sin importer: " + ruta); return; }
            ti.textureType        = TextureImporterType.Default;
            ti.mipmapEnabled      = true;
            ti.maxTextureSize     = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.isReadable         = false;
            // DXT1 = 0,5 byte/px frente a los 4 byte/px de RGBA32. Es exactamente la
            // diferencia que perseguimos. Estas texturas no llevan alfa.
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name               = "WebGL",
                overridden         = true,
                maxTextureSize     = 1024,
                format             = TextureImporterFormat.DXT1,
                textureCompression = TextureImporterCompression.Compressed,
            });
            ti.SaveAndReimport();

            rutasTex.Add(ruta);
            texturas.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(ruta));
        }
        if (texturas.Count == 0) { Debug.LogError("[DecoOpt] No hay tex_N en " + carpeta); return; }

        // ── 2. mapeo material → índice de imagen (lo escribe el script Python) ──
        var mapa = new Dictionary<string, int>();
        var rutaMapeo = $"{carpeta}/mapeo.txt";
        if (!File.Exists(rutaMapeo)) { Debug.LogError("[DecoOpt] Falta " + rutaMapeo); return; }
        foreach (var linea in File.ReadAllLines(rutaMapeo))
        {
            var t = linea.Split('=');
            if (t.Length == 2 && int.TryParse(t[1].Trim(), out int idx)) mapa[t[0].Trim()] = idx;
        }

        // ── 3. prefab: mallas del GLB + materiales nuevos ────────────────────
        var raizGlb = AssetDatabase.LoadAssetAtPath<GameObject>(rutaGlb);
        if (raizGlb == null) { Debug.LogError("[DecoOpt] No carga el GLB: " + rutaGlb); return; }

        var shader = Shader.Find("Appquarium/DecoLit");
        if (shader == null) { Debug.LogError("[DecoOpt] Falta el shader Appquarium/DecoLit"); return; }

        var instancia = (GameObject)PrefabUtility.InstantiatePrefab(raizGlb);
        PrefabUtility.UnpackPrefabInstance(instancia, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        int cambiados = 0, sinMapeo = 0;
        foreach (var mr in instancia.GetComponentsInChildren<Renderer>(true))
        {
            var nuevos = new Material[mr.sharedMaterials.Length];
            for (int i = 0; i < mr.sharedMaterials.Length; i++)
            {
                var orig = mr.sharedMaterials[i];
                if (orig == null) { nuevos[i] = null; continue; }

                var limpio = orig.name.Replace(" (Instance)", "").Trim();
                if (!mapa.TryGetValue(limpio, out int idxImg) || idxImg >= texturas.Count)
                {
                    Debug.LogWarning($"[DecoOpt] Material sin mapeo: '{limpio}' — lo dejo intacto " +
                                     "(⚠ arrastraría la textura RGBA32 del GLB al bundle).");
                    nuevos[i] = orig; sinMapeo++; continue;
                }

                var rutaMat = $"{carpeta}/{limpio}_opt.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(rutaMat);
                if (mat == null)
                {
                    mat = new Material(shader) { name = limpio + "_opt" };
                    AssetDatabase.CreateAsset(mat, rutaMat);
                }
                mat.shader = shader;
                mat.SetTexture("_MainTex", texturas[idxImg]);
                mat.SetColor("_Color", Color.white);
                EditorUtility.SetDirty(mat);

                nuevos[i] = mat;
                cambiados++;
            }
            mr.sharedMaterials = nuevos;
        }

        var nombre = Path.GetFileNameWithoutExtension(rutaGlb);
        var rutaPrefab = $"{carpeta}/{nombre}_opt.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(instancia, rutaPrefab);
        Object.DestroyImmediate(instancia);

        // ── 4. apuntar el DecorationData al prefab nuevo ─────────────────────
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rutaSo);
        if (so == null) { Debug.LogError("[DecoOpt] No carga el SO: " + rutaSo); return; }
        var sp = new SerializedObject(so);
        var prop = sp.FindProperty("prefab");
        if (prop == null) { Debug.LogError("[DecoOpt] El SO no tiene campo 'prefab'"); return; }
        var anterior = prop.objectReferenceValue;
        prop.objectReferenceValue = prefab;
        sp.ApplyModifiedProperties();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssetIfDirty(so);
        AssetDatabase.SaveAssets();

        Debug.Log($"[DecoOpt] {nombre}: {texturas.Count} texturas a DXT1, {cambiados} materiales nuevos" +
                  (sinMapeo > 0 ? $" ⚠ {sinMapeo} SIN mapeo" : "") +
                  $". SO reapuntado ({(anterior != null ? anterior.name : "?")} → {prefab.name}). " +
                  "Siguiente: ★ New Build y comparar el tamaño del bundle contra los 25,0 MB de hoy.");
    }
}
