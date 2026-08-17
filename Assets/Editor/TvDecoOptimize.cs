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
    /// <summary>
    /// Las decos que de verdad pesan. MEDIDO el 2026-08-17 sobre los bundles de
    /// `ServerData/WebGL/` filtrando por los hashes presentes en `catalog_1.2.1.bin`
    /// (⚠ NO con `ls -S`: coge el mayor por nombre, que suele ser un huérfano de un build
    /// viejo — así salió la cifra falsa de «375 MB de decos»).
    ///
    /// Real: 54 decos vivas = 149,8 MB. Las 17 de ≥5 MB son el 78 % (117,5 MB); aquí van esas
    /// 17 más `deco_statue_greek_1` (4,75 MB, justo por debajo del corte) = **18 entradas**.
    /// `deco_statue_greek_2` ya está hecha (9,89 → 2,05 MB) y por eso no está en la lista.
    /// Fuera quedan `lambis_shell` (4,30) y `linckia_laevigata` (4,45): mismo método si hace falta.
    ///
    /// La correspondencia SO → GLB se verificó resolviendo el guid del campo `prefab` de
    /// cada `DecorationData`, no por parecido de nombre.
    /// </summary>
    private static readonly (string so, string glb)[] DecosPesadas =
    {
        ("deco_column_greek_1",     "Assets/ThirdParty/GreekColumns/greek_underwater_column_1.glb"),
        ("deco_statue_greek_4",     "Assets/ThirdParty/GreekStatues/greek_underwater_broken_statue_4.glb"),
        ("deco_statue_greek_3",     "Assets/ThirdParty/GreekStatues/greek_underwater_broken_statue_3.glb"),
        ("deco_coral_meandrina",    "Assets/ThirdParty/Corals/meandrina_meandrites.glb"),
        ("deco_coral_corallium",    "Assets/ThirdParty/Corals/corallium_sp..glb"),
        ("deco_coral_heliopora",    "Assets/ThirdParty/Corals/heliopora_coerulea.glb"),
        ("deco_coral_stylaster",    "Assets/ThirdParty/Corals/stylaster_sanguineus.glb"),
        ("deco_coral_pocillopora",  "Assets/ThirdParty/Corals/pocillopora_damicornis.glb"),
        ("deco_coral_distichopora", "Assets/ThirdParty/Corals/distichopora_violacea.glb"),
        ("deco_coral_diploria",     "Assets/ThirdParty/Corals/diploria_labyrinthiformis.glb"),
        ("deco_coral_acropora",     "Assets/ThirdParty/Corals/acropora_valenciennesi.glb"),
        ("deco_column_greek_3",     "Assets/ThirdParty/GreekColumns/greek_underwater_column_3.glb"),
        ("deco_shell_helmet",       "Assets/ThirdParty/Shells/cypraecassis_rufa.glb"),
        ("deco_shell_tridacna",     "Assets/ThirdParty/Shells/tridacna_squamosa.glb"),
        ("deco_column_greek_2",     "Assets/ThirdParty/GreekColumns/greek_underwater_column_2.glb"),
        ("deco_column_greek_4",     "Assets/ThirdParty/GreekColumns/greek_underwater_column_4.glb"),
        ("deco_column_greek_5",     "Assets/ThirdParty/GreekColumns/greek_underwater_column_5.glb"),
        ("deco_statue_greek_1",     "Assets/ThirdParty/GreekStatues/greek_underwater_broken_statue_1.glb"),
    };

    // La carpeta de trabajo de cada deco es Assets/Content/Decos/<nombre del glb>/, la misma
    // convención que usa `Tools/extract_glb_textures.py` al volcar tex_N + mapeo.txt.
    private static string CarpetaDe(string rutaGlb) =>
        "Assets/Content/Decos/" + Path.GetFileNameWithoutExtension(rutaGlb).TrimEnd('.');

    private static string SoDe(string nombreSo) =>
        $"Assets/ScriptableObjects/Decorations/{nombreSo}.asset";

    [MenuItem("Appquarium TV/🗜 Optimizar deco seleccionada (GLB en el Project)", priority = 2)]
    public static void OptimizarSeleccionada()
    {
        var ruta = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(ruta) || !ruta.EndsWith(".glb"))
        { Debug.LogError("[DecoOpt] Selecciona un .glb en el Project."); return; }

        var entrada = System.Array.Find(DecosPesadas, d => d.glb == ruta);
        if (entrada.so == null)
        { Debug.LogError("[DecoOpt] Ese GLB no está en DecosPesadas — añádelo con su SO."); return; }

        Optimizar(ruta, CarpetaDe(ruta), SoDe(entrada.so));
    }

    [MenuItem("Appquarium TV/🗜 Optimizar TODAS las decos pesadas (18)", priority = 3)]
    public static void OptimizarTodasLasPesadas() => OptimizarLote(preguntar: true);

    /// Entrada para batchmode (`-executeMethod TvDecoOptimize.OptimizarLoteBatch`): igual pero
    /// sin diálogo ni barra de progreso, que en batchmode no existen.
    public static void OptimizarLoteBatch() => OptimizarLote(preguntar: false);

    private static void OptimizarLote(bool preguntar)
    {
        // Preflight: sin tex_N + mapeo.txt no hay nada que hacer, y `Optimizar` abortaría una a
        // una dejando el lote a medias. Mejor listar lo que falta y no empezar.
        var sinExtraer = new List<string>();
        foreach (var (so, glb) in DecosPesadas)
        {
            if (!File.Exists(glb)) { Debug.LogError("[DecoOpt] No existe el GLB: " + glb); return; }
            if (!File.Exists(SoDe(so))) { Debug.LogError("[DecoOpt] No existe el SO: " + SoDe(so)); return; }
            if (!File.Exists($"{CarpetaDe(glb)}/mapeo.txt")) sinExtraer.Add(Path.GetFileName(glb));
        }
        if (sinExtraer.Count > 0)
        {
            Debug.LogError($"[DecoOpt] Faltan las texturas extraídas de {sinExtraer.Count} deco(s): " +
                           string.Join(", ", sinExtraer) + "\nEjecuta primero:  " +
                           "python Tools/extract_glb_textures.py --todas");
            return;
        }

        if (preguntar && !EditorUtility.DisplayDialog("Optimizar decos pesadas",
                $"Se van a reapuntar {DecosPesadas.Length} DecorationData a prefabs nuevos con " +
                "texturas DXT1.\n\nDespués hace falta ★ New Build y comparar el tamaño de los " +
                "bundles.\n\n¿Seguir?", "Optimizar", "Cancelar")) return;

        int ok = 0;
        try
        {
            for (int i = 0; i < DecosPesadas.Length; i++)
            {
                var (so, glb) = DecosPesadas[i];
                if (preguntar)
                    EditorUtility.DisplayProgressBar("Optimizando decos",
                        Path.GetFileName(glb), (float)i / DecosPesadas.Length);
                if (Optimizar(glb, CarpetaDe(glb), SoDe(so))) ok++;
            }
        }
        finally { if (preguntar) EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DecoOpt] LOTE: {ok}/{DecosPesadas.Length} decos optimizadas. " +
                  "Siguiente: ★ New Build y comparar contra los 117,5 MB de hoy.");
    }

    public static bool Optimizar(string rutaGlb, string carpeta, string rutaSo)
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
            if (ti == null) { Debug.LogError("[DecoOpt] Sin importer: " + ruta); return false; }
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
        if (texturas.Count == 0) { Debug.LogError("[DecoOpt] No hay tex_N en " + carpeta); return false; }

        // ── 2. mapeo material → índice de imagen (lo escribe el script Python) ──
        var mapa = new Dictionary<string, int>();
        var rutaMapeo = $"{carpeta}/mapeo.txt";
        if (!File.Exists(rutaMapeo)) { Debug.LogError("[DecoOpt] Falta " + rutaMapeo); return false; }
        foreach (var linea in File.ReadAllLines(rutaMapeo))
        {
            var t = linea.Split('=');
            if (t.Length == 2 && int.TryParse(t[1].Trim(), out int idx)) mapa[t[0].Trim()] = idx;
        }

        // ── 3. prefab: mallas del GLB + materiales nuevos ────────────────────
        var raizGlb = AssetDatabase.LoadAssetAtPath<GameObject>(rutaGlb);
        if (raizGlb == null) { Debug.LogError("[DecoOpt] No carga el GLB: " + rutaGlb); return false; }

        var shader = Shader.Find("Appquarium/DecoLit");
        if (shader == null) { Debug.LogError("[DecoOpt] Falta el shader Appquarium/DecoLit"); return false; }

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
        if (so == null) { Debug.LogError("[DecoOpt] No carga el SO: " + rutaSo); return false; }
        var sp = new SerializedObject(so);
        var prop = sp.FindProperty("prefab");
        if (prop == null) { Debug.LogError("[DecoOpt] El SO no tiene campo 'prefab'"); return false; }
        var anterior = prop.objectReferenceValue;
        prop.objectReferenceValue = prefab;
        sp.ApplyModifiedProperties();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssetIfDirty(so);
        AssetDatabase.SaveAssets();

        Debug.Log($"[DecoOpt] {nombre}: {texturas.Count} texturas a DXT1, {cambiados} materiales nuevos" +
                  (sinMapeo > 0 ? $" ⚠ {sinMapeo} SIN mapeo" : "") +
                  $". SO reapuntado ({(anterior != null ? anterior.name : "?")} → {prefab.name}). " +
                  "Siguiente: ★ New Build y comparar el tamaño del bundle.");
        return true;
    }
}
