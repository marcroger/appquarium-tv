using System;
using UnityEngine;

/// <summary>
/// Una partícula de comida que cae lentamente en el tanque.
/// Cuando un pez la alcanza, llama a Consume().
/// Visual procedural: 3 pellets esféricos naranjas en cluster.
/// </summary>
public class FoodItem : MonoBehaviour
{
    [Header("Comportamiento")]
    public float fallSpeed    = 0.3f;   // velocidad de caída
    public float eatRadius    = 0.4f;   // distancia a la que un pez puede comerla
    public float lifetime     = 30f;    // desaparece sola si nadie la come

    // Deriva horizontal única por pellet (asignada al spawnear)
    [HideInInspector] public float driftFreq   = 2.0f;   // frecuencia del swing
    [HideInInspector] public float driftAmp    = 0.018f; // amplitud horizontal (unidades/s)

    public event Action OnConsumed;

    public bool IsClaimed { get; private set; }  // evita que dos peces vayan a por la misma

    private float _timer;
    private bool  _consumed;
    [HideInInspector] public float driftPhase;  // fase inicial del swing (asignada al spawnear)

    void Start()
    {
        BuildPelletVisual();
    }

    private void BuildPelletVisual()
    {
        var rootRend = GetComponent<Renderer>();
        if (rootRend != null) rootRend.enabled = false;

        var mat  = BuildPelletMaterial();
        var mesh = BuildQuadMesh();

        // Cluster de 3 pellets: uno central grande + dos satélites más pequeños
        var pellets = new (float size, Vector2 offset)[]
        {
            (UnityEngine.Random.Range(0.45f, 0.52f), new Vector2( 0.00f,  0.05f)),  // centro
            (UnityEngine.Random.Range(0.30f, 0.36f), new Vector2(-0.22f, -0.12f)),  // izq-abajo
            (UnityEngine.Random.Range(0.28f, 0.34f), new Vector2( 0.20f, -0.16f)),  // der-abajo
        };

        for (int i = 0; i < pellets.Length; i++)
        {
            var (size, offset) = pellets[i];
            var pellet = new GameObject($"Pellet_{i}");
            pellet.transform.SetParent(transform, false);
            pellet.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            pellet.transform.localScale    = new Vector3(size, size, 1f);

            pellet.AddComponent<MeshFilter>().mesh = mesh;
            var mr = pellet.AddComponent<MeshRenderer>();
            mr.material            = mat;
            mr.sortingOrder        = 10;
            mr.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows      = false;
        }
    }

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "FoodQuad" };
        mesh.vertices  = new Vector3[] {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3( 0.5f,  0.5f, 0f)
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv        = new Vector2[] {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(0,1), new Vector2(1,1)
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Material BuildPelletMaterial()
    {
        // Sprites/Default soporta alpha de textura sin configuración extra en URP
        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("UI/Default")
                  ?? Shader.Find("Universal Render Pipeline/Unlit");

        var mat = new Material(shader) { name = "FoodPellet_Mat" };
        mat.color = Color.white;  // sin tinte — el color va en la textura

        var tex = BuildPelletTexture();
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

        return mat;
    }

    /// <summary>
    /// Genera una textura 64×64 de pellet esférico:
    /// círculo con alpha, gradiente naranja oscuro en bordes → naranja brillante en centro,
    /// más highlight amarillento en la zona superior-izquierda para simular volumen.
    /// </summary>
    private static Texture2D BuildPelletTexture()
    {
        const int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "PelletTex"
        };

        var  center    = new Vector2(res * 0.5f,  res * 0.5f);
        var  highlight = new Vector2(res * 0.33f, res * 0.67f);  // arriba-izquierda
        float radius   = res * 0.46f;

        var baseCol  = new Color(0.92f, 0.48f, 0.05f);  // naranja cálido
        var darkCol  = new Color(0.48f, 0.18f, 0.01f);  // marrón oscuro
        var lightCol = new Color(1.00f, 0.88f, 0.50f);  // amarillo highlight

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                var   pos  = new Vector2(x, y);
                float dist = Vector2.Distance(pos, center);

                if (dist >= radius) { tex.SetPixel(x, y, Color.clear); continue; }

                float t = dist / radius;                             // 0=centro, 1=borde

                // Gradiente base: brillante en centro, oscuro en borde
                Color c = Color.Lerp(baseCol, darkCol, t * t);

                // Highlight esférico: mancha luminosa en zona superior-izquierda
                float hDist   = Vector2.Distance(pos, highlight) / (radius * 0.55f);
                float hFactor = Mathf.Clamp01(1f - hDist) * 0.75f;
                c = Color.Lerp(c, lightCol, hFactor);

                // Soft alpha en el borde del círculo (antialiasing)
                c.a = Mathf.Clamp01((radius - dist) / (radius * 0.08f));

                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    void Update()
    {
        if (_consumed) return;

        // Caída con deriva horizontal única por pellet
        float drift = Mathf.Sin(Time.time * driftFreq + driftPhase) * driftAmp;
        transform.position += new Vector3(drift, -fallSpeed * Time.deltaTime, 0f);
        transform.rotation  = Quaternion.Euler(0f, 0f, drift * 180f);

        // Autodestrucción por timeout
        _timer += Time.deltaTime;
        if (_timer >= lifetime)
            Consume();
    }

    /// <summary>Marca la comida como reclamada por un pez (otro pez buscará otra).</summary>
    public void Claim()
    {
        IsClaimed = true;
    }

    /// <summary>Llamado por FishAgent cuando alcanza la comida.</summary>
    public void Consume()
    {
        if (_consumed) return;
        _consumed = true;

        OnConsumed?.Invoke();
        Destroy(gameObject);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, eatRadius);
    }
}
