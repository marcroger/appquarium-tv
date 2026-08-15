using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sombras de contacto de los PECES sobre la arena — 2026-08-11.
///
/// Los peces nunca habían tenido sombra: FishSpawner solo hace
/// `mr.shadowCastingMode = Off` y ahí acaba. Las decos sí (DecorationPlacer.AddShadow
/// + Appquarium/PlanarShadow), y al arreglar aquellas se notó el hueco.
///
/// ⚠ SILUETA REAL, NO UN BLOB
/// El primer intento fue un óvalo de 2 triángulos, por miedo al rendimiento. Se
/// descartó al verlo: en el móvil la sombra tiene FORMA DE PEZ (allí es sombra real
/// por shadow mapping) y un redondel no se le parece.
/// La solución sin coste de CPU es clonar el SkinnedMeshRenderer del pez compartiendo
/// malla y HUESOS: el clon se deforma solo con la animación —el skinning lo hace la
/// GPU— y Appquarium/PlanarShadow aplana esos vértices ya skinneados contra el suelo.
/// Cuesta un draw call skinneado más por pez. ⚠ SIN MEDIR EN EL DEVICE todavía.
/// El blob queda de reserva para peces sin SkinnedMeshRenderer.
///
/// ⚠ POR QUÉ ES UN QUAD VERTICAL Y NO HORIZONTAL
/// Misma trampa que tumbó las sombras de las decos durante meses: la cámara es
/// ORTOGRÁFICA y mira en horizontal, así que un quad tumbado se ve de canto y no
/// ocupa un solo píxel. La escena es 2.5D — el suelo es un sprite vertical con la
/// arena pintada en perspectiva —, así que la sombra también va vertical.
///
/// ⚠ POR QUÉ ESTÁ AQUÍ Y NO EN FishSpawner
/// FishSpawner.cs se sincroniza desde el repo móvil (ver SYNC_NOTES.md): cualquier
/// cambio ahí lo borra el siguiente sync. Este componente es TV-only y no toca ni un
/// fichero compartido. Lo añade TvSceneBootstrap.
/// </summary>
public class TvFishShadows : MonoBehaviour
{
    [Tooltip("Opacidad de la sombra de un pez pegado al suelo.")]
    [SerializeField] private float alphaCerca = 0.50f;
    // 0,06 medido y descartado: con el pez cerca de la superficie la sombra quedaba en
    // 211 de luminancia sobre una arena de 195-219, o sea DENTRO del ruido de la textura.
    // Invisible no es "sutil", es que no está.
    [Tooltip("Opacidad de un pez nadando arriba del todo. Cuanto más alto, más difusa.")]
    [SerializeField] private float alphaLejos = 0.22f;
    [Tooltip("Altura sobre el suelo a la que la sombra ya casi ha desaparecido.")]
    [SerializeField] private float alturaFade = 3.2f;
    [Tooltip("Ancho de la sombra respecto al largo del pez.")]
    [SerializeField] private float anchoRelativo = 0.85f;
    [Tooltip("Relación alto/ancho de la elipse. Bajo = más tumbada.")]
    [SerializeField] private float aplastado = 0.34f;
    [Tooltip("Empuje en Z para que el pez nunca tape su propia sombra por ZTest.")]
    [SerializeField] private float zPush = 0.30f;

    private DecorationPlacer _placer;
    private Transform        _root;
    private Mesh             _quad;
    private Texture2D        _tex;
    private Shader           _shader;
    private Shader           _shaderSil;

    private readonly Dictionary<FishAgent, Entrada> _sombras = new Dictionary<FishAgent, Entrada>();
    private float _proximoScan;

    private class Entrada
    {
        public Transform          tr;
        public MeshRenderer       mr;          // solo en el blob de reserva
        public SkinnedMeshRenderer silueta;    // solo en la silueta real
        public Material           mat;
        public float              anchoBase;
        public bool               esSilueta;
    }

    void Start()
    {
        _placer = FindFirstObjectByType<DecorationPlacer>();
        if (_placer == null)
        {
            Debug.LogWarning("[TvFishShadows] Sin DecorationPlacer: no sé dónde está el suelo. Desactivado.");
            enabled = false;
            return;
        }

        // ⚠ Appquarium/FishShadow, NO Sprites/Default: este último no pinta materiales
        // creados en runtime (probado a rojo opaco, orden 500, cola 4000 ⇒ 0 píxeles).
        // Ver la cabecera de Assets/Shaders/FishShadow.shader.
        _shader = Shader.Find("Appquarium/FishShadow");
        if (_shader == null)
        {
            Debug.LogWarning("[TvFishShadows] Falta Appquarium/FishShadow " +
                             "(¿sin registrar en Always Included Shaders?). Sin sombras de pez.");
            enabled = false;
            return;
        }

        // El mismo shader que usan las sombras de las decos: aplana los vértices ya
        // skinneados contra el plano del suelo.
        _shaderSil = Shader.Find("Appquarium/PlanarShadow");

        _root = new GameObject("FishShadows").transform;
        _root.SetParent(transform);

        _quad = ConstruirQuad();
        _tex  = ConstruirElipse(64);
    }

    void LateUpdate()
    {
        if (_placer == null) return;

        // Rescan periódico: los peces aparecen y desaparecen por mensajes UPDATE del móvil.
        if (Time.time >= _proximoScan)
        {
            _proximoScan = Time.time + 1f;
            Rescan();
        }

        foreach (var kv in _sombras)
        {
            var pez = kv.Key;
            var e   = kv.Value;
            if (pez == null || e.tr == null) continue;

            Vector3 p       = pez.transform.position;
            // ⚠ El MAX con la Z=0 no es decorativo: sin él la sombra se iba al BORDE INFERIOR
            // de la pantalla y quedaba medio fuera (medido: viewport y=0,02). Los peces nadan
            // en Z cercana a 0 o negativa, y ahí FloorSurfaceY() devuelve el borde delantero
            // del suelo, abajo del todo. DecorationPlacer.UpdateShadow hace exactamente este
            // mismo MAX por el mismo motivo; copiarlo tal cual mantiene las sombras de peces
            // y de decos en la misma banda de arena.
            float   suelo   = Mathf.Max(_placer.GetFloorSurfaceY(p.z), _placer.GetFloorSurfaceY(0f));
            float   altura  = Mathf.Max(0f, p.y - suelo);
            float   t       = Mathf.Clamp01(altura / Mathf.Max(0.01f, alturaFade));

            // Más alto ⇒ más tenue (penumbra).
            float alpha = Mathf.Lerp(alphaCerca, alphaLejos, t);
            e.mat.SetFloat("_ShadowAlpha", alpha);

            if (e.esSilueta)
            {
                // La silueta NO se coloca a mano: la mueven los huesos del pez, igual que al
                // pez mismo. Aquí solo se le dice a qué altura está el suelo para que el
                // shader aplane contra él.
                e.mat.SetFloat("_FloorY", suelo + 0.03f);
                continue;
            }

            float escala = e.anchoBase * Mathf.Lerp(1f, 1.35f, t);
            e.tr.position   = new Vector3(p.x, suelo + 0.03f, p.z + zPush);
            e.tr.localScale = new Vector3(escala, escala * aplastado, 1f);
        }
    }

    private void Rescan()
    {
        // Limpiar los que ya no existen (pez retirado por remove_fish).
        var muertos = new List<FishAgent>();
        foreach (var kv in _sombras)
            if (kv.Key == null) { if (kv.Value.tr != null) Destroy(kv.Value.tr.gameObject); muertos.Add(kv.Key); }
        foreach (var m in muertos) _sombras.Remove(m);

        foreach (var pez in FindObjectsByType<FishAgent>(FindObjectsSortMode.None))
        {
            if (_sombras.ContainsKey(pez)) continue;
            _sombras[pez] = Crear(pez);
        }
    }

    private Entrada Crear(FishAgent pez)
    {
        // ── Camino preferido: SILUETA REAL del pez ──────────────────────────────
        // Se clona su SkinnedMeshRenderer compartiendo malla Y HUESOS. El clon se
        // deforma solo con la animación (el skinning lo hace la GPU una vez por
        // renderer, sin coste de CPU ni de memoria extra: la malla es la misma).
        // Luego Appquarium/PlanarShadow aplana esos vértices ya skinneados contra el
        // plano del suelo, igual que con las decos.
        // El blob elíptico se queda solo de reserva para peces sin SkinnedMeshRenderer.
        var orig = pez.GetComponentInChildren<SkinnedMeshRenderer>();
        if (orig != null && orig.sharedMesh != null && _shaderSil != null)
        {
            var goSil = new GameObject("FishShadowSil");
            goSil.transform.SetParent(orig.transform.parent, false);
            goSil.transform.SetLocalPositionAndRotation(orig.transform.localPosition, orig.transform.localRotation);
            goSil.transform.localScale = orig.transform.localScale;

            var smrSil = goSil.AddComponent<SkinnedMeshRenderer>();
            smrSil.sharedMesh   = orig.sharedMesh;
            smrSil.bones        = orig.bones;      // MISMOS huesos ⇒ misma deformación
            smrSil.rootBone     = orig.rootBone;
            smrSil.quality      = SkinQuality.Bone1;   // 1 hueso por vértice: es una mancha, no hace falta más
            smrSil.updateWhenOffscreen = false;
            smrSil.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            smrSil.receiveShadows      = false;
            smrSil.sortingLayerName    = "Default";
            smrSil.sortingOrder        = 21;

            var matSil = new Material(_shaderSil) { name = "FishShadowSil_Mat" };
            matSil.SetFloat("_ShadowAlpha", alphaCerca);
            matSil.SetFloat("_Flatten", 0.16f);
            matSil.SetFloat("_ZPush",   zPush);
            smrSil.material = matSil;

            return new Entrada { tr = goSil.transform, silueta = smrSil, mat = matSil, esSilueta = true };
        }

        // ── Reserva: blob elíptico ──────────────────────────────────────────────
        var go = new GameObject("FishShadow");
        go.transform.SetParent(_root, false);

        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sortingLayerName  = "Default";
        mr.sortingOrder      = 21;

        var mat = new Material(_shader) { name = "FishShadow_Mat" };
        mat.mainTexture = _tex;
        mat.SetFloat("_ShadowAlpha", alphaCerca);
        mr.material     = mat;

        float ancho = 0.5f;
        var m2 = pez.GetComponentInChildren<MeshRenderer>();
        if (m2 != null) ancho = m2.bounds.size.x;

        return new Entrada
        {
            tr = go.transform, mr = mr, mat = mat,
            anchoBase = Mathf.Max(0.15f, ancho * anchoRelativo),
        };
    }

    private static Mesh ConstruirQuad()
    {
        var m = new Mesh { name = "FishShadowQuad" };
        m.vertices  = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0),
                              new Vector3(-0.5f, 0.5f,0), new Vector3(0.5f, 0.5f,0) };
        m.uv        = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
        // ⚠ Sprites/Default multiplica por el COLOR DE VÉRTICE. Sin array de colores el
        // valor queda indefinido y la sombra sale más floja de lo que pides, con el alpha
        // del material mintiendo. Blanco = neutro, manda solo mat.color.
        m.colors    = new[] { Color.white, Color.white, Color.white, Color.white };
        m.triangles = new[] { 0,2,1, 2,3,1 };
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Elipse con caída suave hacia el borde — evita el canto duro de un blob plano.</summary>
    private static Texture2D ConstruirElipse(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            // Núcleo sólido hasta 0,45 y desvanecido hasta el borde.
            float a  = 1f - Mathf.SmoothStep(0.45f, 1f, d);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        t.SetPixels(px);
        t.Apply(false, true);
        return t;
    }
}
