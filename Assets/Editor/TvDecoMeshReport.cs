using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cuenta triángulos y vértices de cada `DecorationData` para decidir si merece la pena tocar
/// las mallas (la "segunda palanca" de tamaño, después de las texturas).
///
/// Nace del dato del 2026-08-18: con texturas IDÉNTICAS (1024² RGBA32 → DXT1), `lambis_shell`
/// (12.498 triángulos) bajó un 76,9 % y `linckia_laevigata` (100.000) sólo un 36,8 %. O sea que
/// **el rendimiento del paso a DXT1 lo predice la proporción textura/malla**, y para saber qué
/// decos siguen siendo caras hay que contar la malla, no suponerla.
///
/// ⚠ Cuenta la malla del PREFAB, que es lo que acaba en el bundle. No abre el GLB: varias decos
/// no vienen de GLB sino de FBX, y otras comparten prefab entre sí (las 3 anclas, por ejemplo).
/// </summary>
public static class TvDecoMeshReport
{
    private const string SALIDA = "deco-mallas.csv";

    [MenuItem("Appquarium TV/📐 Informe de mallas por deco", priority = 4)]
    public static void Informe() => Generar();

    /// Entrada para batchmode (`-executeMethod TvDecoMeshReport.InformeBatch`).
    public static void InformeBatch() => Generar();

    private static void Generar()
    {
        var guids = AssetDatabase.FindAssets("t:DecorationData",
            new[] { "Assets/ScriptableObjects/Decorations" });

        var filas = new List<(string id, string prefab, int tris, int verts, int mallas, int rends)>();
        int sinPrefab = 0;

        foreach (var guid in guids)
        {
            var rutaSo = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rutaSo);
            if (so == null) continue;

            var id = Path.GetFileNameWithoutExtension(rutaSo);
            var prop = new SerializedObject(so).FindProperty("prefab");
            var prefab = prop?.objectReferenceValue as GameObject;

            if (prefab == null)
            {
                // Los 12 sustratos (sub_*) no tienen prefab: son texturas por Resources.Load.
                filas.Add((id, "(sin prefab — sustrato)", 0, 0, 0, 0));
                sinPrefab++;
                continue;
            }

            int tris = 0, verts = 0, mallas = 0, rends = 0;
            // Un HashSet de mallas evita contar dos veces la misma malla compartida por varios
            // renderers dentro del mismo prefab; entre decos distintas sí se cuenta en cada una,
            // que es lo correcto porque cada bundle se la lleva.
            var vistas = new HashSet<Mesh>();

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                Acumular(mf.sharedMesh, vistas, ref tris, ref verts, ref mallas);
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Acumular(smr.sharedMesh, vistas, ref tris, ref verts, ref mallas);

            rends = prefab.GetComponentsInChildren<Renderer>(true).Length;
            filas.Add((id, AssetDatabase.GetAssetPath(prefab), tris, verts, mallas, rends));
        }

        var conMalla = filas.Where(f => f.tris > 0).OrderByDescending(f => f.tris).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("deco;triangulos;vertices;mallas;renderers;prefab");
        foreach (var f in conMalla.Concat(filas.Where(f => f.tris == 0)))
            sb.AppendLine(string.Join(";", f.id, f.tris.ToString(CultureInfo.InvariantCulture),
                f.verts.ToString(CultureInfo.InvariantCulture), f.mallas.ToString(),
                f.rends.ToString(), f.prefab));
        File.WriteAllText(SALIDA, sb.ToString());

        long total = conMalla.Sum(f => (long)f.tris);
        Debug.Log($"[MeshReport] {filas.Count} decos · {conMalla.Count} con malla · {sinPrefab} sin prefab.\n" +
                  $"Total {total:N0} triángulos. Mediana {conMalla[conMalla.Count / 2].tris:N0}. " +
                  $"Máx {conMalla[0].id} = {conMalla[0].tris:N0}.\n" +
                  $"CSV en {Path.GetFullPath(SALIDA)}");
    }

    private static void Acumular(Mesh m, HashSet<Mesh> vistas, ref int tris, ref int verts, ref int mallas)
    {
        if (m == null || !vistas.Add(m)) return;
        // GetIndexCount por submalla en vez de `m.triangles`: no aloja el array entero, que en
        // mallas de fotogrametría de 100k son varios MB por deco.
        for (int i = 0; i < m.subMeshCount; i++)
        {
            if (m.GetTopology(i) != MeshTopology.Triangles) continue;
            tris += (int)(m.GetIndexCount(i) / 3);
        }
        verts += m.vertexCount;
        mallas++;
    }
}
