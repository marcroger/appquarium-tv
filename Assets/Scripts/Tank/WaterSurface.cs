using UnityEngine;

/// <summary>
/// Superficie del agua animada en el tope del tanque.
/// Genera un quad semitransparente con caustics simulados por UV scroll.
/// No necesita assets — se autoconstruye desde código.
///
/// Setup: añadir al mismo GameObject que TankController.
/// TankController.InitializeWithBounds() llama a InitializeSurface().
/// </summary>
[RequireComponent(typeof(TankController))]
public class WaterSurface : MonoBehaviour
{
    [Header("Color del agua")]
    public Color surfaceColor = new Color(0.15f, 0.65f, 0.85f, 0.18f);

    [Header("Velocidad de ondulación UV")]
    public float scrollSpeedX = 0.04f;
    public float scrollSpeedY = 0.02f;

    [Header("Offset Y desde el tope del tanque")]
    public float yOffset = -0.05f;   // ligeramente por dentro

    // ── Internos ─────────────────────────────────────────────────────────────
    private Material _surfaceMat;
    private Material _glowMat;
    private float    _uvX, _uvY;

    // ── API pública ──────────────────────────────────────────────────────────

    public void InitializeSurface()
    {
        // Destruir instancias previas (al reinicializar por cambio de tanque)
        foreach (Transform child in transform)
            if (child.name == "WaterSurface" || child.name == "WaterSurface_Glow")
                Destroy(child.gameObject);

        Bounds bounds = GetComponent<TankController>().GetTankBounds();
        BuildSurface(bounds);
    }

    /// <summary>
    /// Cambia el tinte de la superficie de agua para adaptarse al fondo activo.
    /// Llamar desde TankBackground.SetPreset() al cambiar el fondo.
    /// </summary>
    public void SetTint(Color tint)
    {
        surfaceColor = tint;
        ApplyTintToMaterials();
    }

    void Update()
    {
        if (_surfaceMat == null) return;

        _uvX += scrollSpeedX * Time.deltaTime;
        _uvY += scrollSpeedY * Time.deltaTime;

        // Mantener en [0,1] para evitar drift infinito
        if (_uvX > 1f) _uvX -= 1f;
        if (_uvY > 1f) _uvY -= 1f;

        if (_surfaceMat.HasProperty("_BaseMap")) _surfaceMat.SetTextureOffset("_BaseMap", new Vector2(_uvX, _uvY));
        if (_surfaceMat.HasProperty("_MainTex")) _surfaceMat.SetTextureOffset("_MainTex", new Vector2(_uvX, _uvY));
    }

    // ── Construcción ─────────────────────────────────────────────────────────

    private void BuildSurface(Bounds bounds)
    {
        var go = new GameObject("WaterSurface");
        go.transform.SetParent(transform);

        float topY = bounds.max.y + yOffset;
        go.transform.localPosition = new Vector3(0f, topY, -0.5f);  // delante del fondo
        go.transform.localRotation = Quaternion.identity;

        float w = bounds.size.x;

        // Quad plano horizontal visible desde arriba/frente
        var mesh = new Mesh { name = "WaterSurface_Mesh" };
        float hw = w * 0.5f;
        float halfThick = 0.28f;   // grosor visual de la franja (más alto → más recorrido de fade)

        mesh.vertices = new Vector3[]
        {
            new Vector3(-hw, -halfThick, 0),
            new Vector3( hw, -halfThick, 0),
            new Vector3(-hw,  halfThick, 0),
            new Vector3( hw,  halfThick, 0),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv        = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(4, 0),
            new Vector2(0, 1), new Vector2(4, 1),
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sortingOrder      = 5;
        mr.material          = BuildWaterMaterial();

        // Segunda capa: resplandor más grueso y transparente (brillo superficial)
        BuildGlowLayer(go.transform.parent, bounds, topY, w);
    }

    private void BuildGlowLayer(Transform parent, Bounds bounds, float topY, float w)
    {
        var glow = new GameObject("WaterSurface_Glow");
        glow.transform.SetParent(parent);
        glow.transform.localPosition = new Vector3(0f, topY - 0.3f, -0.4f);

        var mesh = new Mesh { name = "Glow_Mesh" };
        float hw = w * 0.5f;

        mesh.vertices = new Vector3[]
        {
            new Vector3(-hw, -0.5f, 0),
            new Vector3( hw, -0.5f, 0),
            new Vector3(-hw,  0.5f, 0),
            new Vector3( hw,  0.5f, 0),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv = new Vector2[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(0,1), new Vector2(1,1),
        };
        mesh.RecalculateNormals();

        glow.AddComponent<MeshFilter>().mesh = mesh;

        var mr = glow.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sortingOrder      = 4;

        // Textura: solo alfa (blanco puro + degradado de transparencia)
        // El color real viene del _BaseColor/_Color del material → SetTint() lo cambia sin reconstruir.
        const int texH = 32;
        var tex = new Texture2D(1, texH, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < texH; y++)
        {
            float t  = y / (float)(texH - 1);
            float sg = Mathf.Sin(t * Mathf.PI);  // Sin^2: caída más rápida en ambos bordes
            float alpha = (sg * sg) * 0.18f;
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();

        _glowMat = BuildTransparentMaterial(tex);
        if (_glowMat != null)
        {
            SetMaterialColor(_glowMat, surfaceColor);
            mr.material = _glowMat;
        }
    }

    private Material BuildWaterMaterial()
    {
        // Textura de ondas: patrón sinusoidal en horizontal — solo alfa, blanco puro.
        // El color real viene del _BaseColor/_Color del material → SetTint() lo cambia sin reconstruir.
        const int w = 128;
        const int h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name       = "WaterTex"
        };

        for (int x = 0; x < w; x++)
        {
            float wave = (Mathf.Sin(x / (float)w * Mathf.PI * 6f) + 1f) * 0.5f;
            for (int y = 0; y < h; y++)
            {
                float fade  = y / (float)(h - 1);
                // Sin^2 fade: pico estrecho en el centro, caída rápida en ambos bordes
                float s = Mathf.Sin(fade * Mathf.PI);
                float alpha = wave * (s * s) * surfaceColor.a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        _surfaceMat = BuildTransparentMaterial(tex);
        if (_surfaceMat == null) return null;
        _surfaceMat.name = "WaterSurface_Mat";

        SetMaterialColor(_surfaceMat, surfaceColor);

        // Tiling para que las ondas se repitan
        if (_surfaceMat.HasProperty("_BaseMap")) _surfaceMat.SetTextureScale("_BaseMap", new Vector2(3f, 1f));
        if (_surfaceMat.HasProperty("_MainTex")) _surfaceMat.SetTextureScale("_MainTex", new Vector2(3f, 1f));

        return _surfaceMat;
    }

    private void ApplyTintToMaterials()
    {
        if (_surfaceMat != null) SetMaterialColor(_surfaceMat, surfaceColor);
        if (_glowMat    != null) SetMaterialColor(_glowMat,    surfaceColor);
    }

    private static void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }

    private Material BuildTransparentMaterial(Texture2D tex)
    {
        // Sprites/Default está garantizado en Always Included Shaders.
        // URP/Unlit tiene bug de color space en WebGL — usarlo como fallback, no primario.
        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("UI/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");

        if (shader == null)
        {
            Debug.LogWarning("[WaterSurface] No se encontró ningún shader transparente. WaterSurface omitida.");
            return null;
        }

        var mat = new Material(shader);

        // Modo transparente en URP
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);    // Transparent
            mat.SetFloat("_Blend",   0f);    // Alpha
            mat.renderQueue = 3000;
            mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",    0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", tex);
        else if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", tex);

        return mat;
    }
}
