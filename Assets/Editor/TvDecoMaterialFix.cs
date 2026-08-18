using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pasa a `Appquarium/DecoLit`, EN EL EDITOR, los materiales que usan las decos y que hoy están
/// en URP/Lit, Standard o FishUnlit.
///
/// ── Por qué adelgaza el bundle ──────────────────────────────────────────────────────────────
/// Unity sólo empaqueta las texturas correspondientes a propiedades que **declara el shader del
/// material**. `DecoLit` declara `_MainTex`, `_Color`, `_Brightness`, `_Ambient` y
/// `_EmissionColor`; un material en URP/Lit declara además normal, metallic/smoothness, AO,
/// detail... y esos mapas viajan a R2 y se cargan en memoria.
///
/// Y el runtime **los tira igualmente**: `DecorationPlacer.FixNonURPMaterials()` reconstruye el
/// material como DecoLit copiando sólo textura base + color. O sea que hoy se paga el transporte
/// de mapas que se descartan al llegar.
///
/// Medido el 2026-08-18 con las 3 anclas, que comparten prefab y malla (2.512 triángulos):
///   deco_anchor          material ya device-safe        0,150 MB
///   deco_anchor_rust     HallAnchor_rust_mat (URP/Lit)  0,708 MB
///   deco_anchor_oldrust  HallAnchor_oldrust_mat (URP)   0,721 MB
/// El .mat de la primera SIGUE listando _BumpMap/_MetallicGlossMap/_OcclusionMap con sus guids
/// (Unity conserva las propiedades huérfanas al cambiar de shader) y aun así esas texturas NO
/// están en su bundle. Ésa es la prueba de que lo que manda es el shader activo, no el YAML.
///
/// ── Por qué NO cambia el aspecto ────────────────────────────────────────────────────────────
/// La transferencia replica EXACTAMENTE la de `FixNonURPMaterials`: `_MainTex` sale de
/// `_BaseMap` ?? `baseColorTexture` ?? `_MainTex`, y `_Color` de `_BaseColor` ??
/// `baseColorFactor` ?? `_Color`. Se adelanta a build lo que el runtime ya hacía al colocar.
/// Efecto secundario bueno: desaparece su `FixMat` del log.
///
/// ⚠⚠ El destino es **DecoLit, NUNCA FishUnlit**. FishUnlit es plano y en una deco la deja sin
/// iluminación: el ancla salió como una silueta negra en la tele el 2026-08-11, y por eso
/// `FixNonURPMaterials` tiene la guarda `unlitEnDeco` que reconvierte FishUnlit → DecoLit.
///
/// ℹ Se puede tocar `Assets/ThirdParty` sin miedo a un sync: `Tools/SyncFromMobile.ps1` sólo
/// copia `Assets\Scripts\*` y `Assets\Resources\Data`.
/// </summary>
public static class TvDecoMaterialFix
{
    private const string CSV = "deco-materiales.csv";

    // Propiedades que declara URP/Lit y DecoLit no: son las que dejan de viajar al bundle.
    private static readonly string[] MapasMuertos =
    {
        "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_ParallaxMap",
        "_DetailNormalMap", "_DetailAlbedoMap", "_SpecGlossMap", "_EmissionMap",
    };

    [MenuItem("Appquarium TV/🎨 Informe de materiales de decos", priority = 5)]
    public static void Informe() => Ejecutar(convertir: false);

    [MenuItem("Appquarium TV/🎨 Convertir materiales de decos a DecoLit", priority = 6)]
    public static void ConvertirMenu()
    {
        if (!EditorUtility.DisplayDialog("Convertir materiales a DecoLit",
                "Se cambia el shader de los materiales que usan las decos a Appquarium/DecoLit, " +
                "copiando textura base y color igual que hace el runtime.\n\n" +
                "Después hace falta New Build y deploy.\n\n¿Seguir?", "Convertir", "Cancelar")) return;
        Ejecutar(convertir: true);
    }

    /// Entradas para batchmode (`-executeMethod TvDecoMaterialFix.InformeBatch` / `.ConvertirBatch`).
    public static void InformeBatch()   => Ejecutar(convertir: false);
    public static void ConvertirBatch() => Ejecutar(convertir: true);

    private static void Ejecutar(bool convertir)
    {
        var decoLit = Shader.Find("Appquarium/DecoLit");
        if (decoLit == null) { Debug.LogError("[MatFix] No existe Appquarium/DecoLit — aborto."); return; }

        // material -> decos que lo usan (varias decos comparten .mat: las rocas, las anclas...)
        var usos = new Dictionary<Material, List<string>>();
        var guids = AssetDatabase.FindAssets("t:DecorationData",
            new[] { "Assets/ScriptableObjects/Decorations" });

        foreach (var g in guids)
        {
            var rutaSo = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rutaSo);
            if (so == null) continue;
            var idDeco = Path.GetFileNameWithoutExtension(rutaSo);
            var sp = new SerializedObject(so);

            var prefab = sp.FindProperty("prefab")?.objectReferenceValue as GameObject;
            if (prefab != null)
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    // ⚠ Los ParticleSystemRenderer se saltan, igual que hace
                    // `FixNonURPMaterials`. `deco_toy_chest` lleva `Light_D`/`LightOrb_D` en
                    // `Legacy Shaders/Particles/Additive` (el brillo del cofre): pasarlos a
                    // DecoLit convertiría un aditivo en un quad opaco.
                    if (r is ParticleSystemRenderer) continue;
                    foreach (var m in r.sharedMaterials)
                        Anotar(usos, m, idDeco);
                }

            // El overrideMaterial NO cuelga del prefab: vive en el SO, y es justo el caso de las
            // anclas oxidadas, que eran las que más pesaban.
            Anotar(usos, sp.FindProperty("overrideMaterial")?.objectReferenceValue as Material, idDeco);
        }

        var filas = new List<string>();
        int yaOk = 0;
        var pendientes = new List<(Material m, string sh, List<string> muertos, List<string> decos)>();

        foreach (var kv in usos.OrderBy(k => k.Key.name))
        {
            var m = kv.Key;
            var sh = m.shader != null ? m.shader.name : "(sin shader)";
            var muertos = MapasMuertos.Where(p => m.HasProperty(p) && m.GetTexture(p) != null).ToList();

            // ⚠⚠ La regla es EXACTAMENTE la de `FixNonURPMaterials`: se convierte sólo lo que el
            // runtime ya convertiría. Así el resultado es idéntico por construcción, y —más
            // importante— NO se toca lo que el runtime deja pasar a propósito, como los
            // `Legacy Shaders/Particles/Additive` del cofre.
            bool esDecoLit  = sh == "Appquarium/DecoLit";
            bool unlitEnDeco = sh.Contains("Appquarium/FishUnlit") && !m.name.EndsWith("_DECOLIT");
            bool intocable  = !unlitEnDeco
                              && (sh.Contains("Sprites") || sh.Contains("UI/Default")
                                  || sh.Contains("Appquarium/") || m.name.EndsWith("_DECOLIT"));
            bool convertible = !intocable
                               && (unlitEnDeco
                                   || sh.Contains("Universal Render Pipeline/Lit")
                                   || sh.Contains("Hidden/InternalError")
                                   || sh.Contains("Standard")
                                   || sh.Contains("glTF")
                                   || sh.Contains("PbrMetallic"));

            string estado = esDecoLit ? "OK" : convertible ? "PENDIENTE" : "INTACTO (el runtime tampoco lo toca)";
            if (esDecoLit) yaOk++;
            if (convertible) pendientes.Add((m, sh, muertos, kv.Value));

            filas.Add(string.Join(";", m.name, sh, estado,
                muertos.Count.ToString(), string.Join("+", muertos.Select(x => x.TrimStart('_'))),
                kv.Value.Count.ToString(), string.Join("+", kv.Value),
                AssetDatabase.GetAssetPath(m)));
        }

        int convertidos = 0;
        if (convertir)
        {
            foreach (var (m, sh, muertos, decos) in pendientes)
            {
                Texture baseTex = null;
                if (m.HasProperty("_BaseMap"))                            baseTex = m.GetTexture("_BaseMap");
                if (baseTex == null && m.HasProperty("baseColorTexture")) baseTex = m.GetTexture("baseColorTexture");
                if (baseTex == null && m.HasProperty("_MainTex"))         baseTex = m.GetTexture("_MainTex");

                var color = Color.white;
                if (m.HasProperty("_BaseColor"))           color = m.GetColor("_BaseColor");
                else if (m.HasProperty("baseColorFactor")) color = m.GetColor("baseColorFactor");
                else if (m.HasProperty("_Color"))          color = m.GetColor("_Color");

                if (baseTex == null)
                    Debug.LogWarning($"[MatFix] {m.name}: sin textura base — quedará de color plano " +
                                     "(el runtime hacía lo mismo, pero conviene mirarlo).");

                m.shader = decoLit;
                if (baseTex != null) m.SetTexture("_MainTex", baseTex);
                m.SetColor("_Color", color);
                m.SetFloat("_Brightness", 1f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                convertidos++;

                Debug.Log($"[MatFix] {m.name}: {sh} -> DecoLit" +
                          (muertos.Count > 0
                              ? " (fuera " + muertos.Count + " mapa(s): " +
                                string.Join(", ", muertos.Select(x => x.TrimStart('_'))) + ")"
                              : "") +
                          " · lo usan " + decos.Count + " deco(s)");
            }
            AssetDatabase.SaveAssets();
        }

        File.WriteAllText(CSV, "material;shader;estado;mapas_muertos;cuales;decos;lista;ruta\n" +
                               string.Join("\n", filas) + "\n");

        int totalMuertos = pendientes.Sum(p => p.muertos.Count);
        int intactos = usos.Count - yaOk - pendientes.Count;
        Debug.Log($"[MatFix] {usos.Count} materiales usados por las decos · {yaOk} ya en DecoLit · " +
                  $"{pendientes.Count} convertibles, con {totalMuertos} mapas muertos · " +
                  $"{intactos} intactos a propósito (el runtime tampoco los toca).\n" +
                  (convertir
                      ? "CONVERTIDOS " + convertidos + ". Siguiente: New Build y comparar bundles.\n"
                      : "Modo informe: no se ha tocado nada.\n") +
                  $"CSV en {Path.GetFullPath(CSV)}");
    }

    private static void Anotar(Dictionary<Material, List<string>> usos, Material m, string idDeco)
    {
        if (m == null) return;
        if (!usos.TryGetValue(m, out var l)) usos[m] = l = new List<string>();
        if (!l.Contains(idDeco)) l.Add(idDeco);
    }
}
