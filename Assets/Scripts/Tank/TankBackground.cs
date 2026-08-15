using System.Collections;
using UnityEngine;

/// <summary>
/// Genera el fondo degradado del acuario por código.
/// Soporta presets de background intercambiables con transición animada.
///
/// Setup: añadir al mismo GameObject que TankController.
/// TankController.InitializeWithBounds() llama a InitializeBackground().
/// </summary>
[RequireComponent(typeof(TankController))]
public class TankBackground : MonoBehaviour
{
    // ── Presets de fondo ──────────────────────────────────────────────────────

    public struct BackgroundPreset
    {
        public string id;
        public string displayName;
        public Color  bottom;
        public Color  mid;
        public Color  top;
        /// <summary>
        /// Tinte de la franja WaterSurface para este fondo.
        /// Cada preset ajusta el color/alpha para que la superficie de agua
        /// encaje visualmente con el ambiente del fondo.
        /// </summary>
        public Color  surfaceTint;
        public bool   isStarterGift;
        public float  price;
        public int    pearlPrice;
        public int    displayOrder;
    }

    public static readonly BackgroundPreset[] Presets =
    {
        // surfaceTint: adapta la franja de agua al color/ambiente de cada fondo
        // isStarterGift=true: bg_classic, bg_tropical, bg_night (free tier)
        new BackgroundPreset { id = "bg_classic",  displayName = "bg.classic",  bottom = new Color(0.02f, 0.07f, 0.16f), mid = new Color(0.04f, 0.14f, 0.30f), top = new Color(0.10f, 0.25f, 0.50f), surfaceTint = new Color(0.15f, 0.65f, 0.85f, 0.18f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  0 },
        new BackgroundPreset { id = "bg_tropical", displayName = "bg.tropical", bottom = new Color(0.00f, 0.10f, 0.15f), mid = new Color(0.02f, 0.18f, 0.28f), top = new Color(0.05f, 0.30f, 0.45f), surfaceTint = new Color(0.10f, 0.65f, 0.70f, 0.14f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  1 },
        new BackgroundPreset { id = "bg_kelp",     displayName = "bg.kelp",     bottom = new Color(0.01f, 0.10f, 0.04f), mid = new Color(0.02f, 0.18f, 0.06f), top = new Color(0.04f, 0.28f, 0.10f), surfaceTint = new Color(0.18f, 0.55f, 0.22f, 0.12f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  2 },
        new BackgroundPreset { id = "bg_deep",     displayName = "bg.deep",     bottom = new Color(0.01f, 0.02f, 0.08f), mid = new Color(0.02f, 0.05f, 0.15f), top = new Color(0.03f, 0.09f, 0.22f), surfaceTint = new Color(0.08f, 0.20f, 0.50f, 0.10f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  3 },
        new BackgroundPreset { id = "bg_night",    displayName = "bg.night",    bottom = new Color(0.00f, 0.00f, 0.05f), mid = new Color(0.01f, 0.02f, 0.10f), top = new Color(0.02f, 0.04f, 0.18f), surfaceTint = new Color(0.06f, 0.10f, 0.40f, 0.07f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  4 },
        new BackgroundPreset { id = "bg_abyss",    displayName = "bg.abyss",    bottom = new Color(0.00f, 0.00f, 0.02f), mid = new Color(0.00f, 0.01f, 0.05f), top = new Color(0.01f, 0.02f, 0.10f), surfaceTint = new Color(0.04f, 0.06f, 0.20f, 0.05f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  5 },
        new BackgroundPreset { id = "bg_cave",     displayName = "bg.cave",     bottom = new Color(0.00f, 0.04f, 0.04f), mid = new Color(0.01f, 0.08f, 0.08f), top = new Color(0.02f, 0.12f, 0.12f), surfaceTint = new Color(0.06f, 0.40f, 0.40f, 0.08f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  6 },
        new BackgroundPreset { id = "bg_arctic",   displayName = "bg.arctic",   bottom = new Color(0.04f, 0.10f, 0.20f), mid = new Color(0.10f, 0.20f, 0.35f), top = new Color(0.20f, 0.35f, 0.55f), surfaceTint = new Color(0.60f, 0.85f, 0.96f, 0.22f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  7 },
        new BackgroundPreset { id = "bg_volcanic", displayName = "bg.volcanic", bottom = new Color(0.18f, 0.04f, 0.01f), mid = new Color(0.10f, 0.03f, 0.01f), top = new Color(0.05f, 0.02f, 0.01f), surfaceTint = new Color(0.30f, 0.10f, 0.04f, 0.08f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  8 },
        new BackgroundPreset { id = "bg_jungle",   displayName = "bg.jungle",   bottom = new Color(0.01f, 0.08f, 0.03f), mid = new Color(0.02f, 0.15f, 0.05f), top = new Color(0.05f, 0.25f, 0.10f), surfaceTint = new Color(0.10f, 0.42f, 0.15f, 0.10f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  9 },
        new BackgroundPreset { id = "bg_wreck",    displayName = "bg.wreck",    bottom = new Color(0.01f, 0.05f, 0.06f), mid = new Color(0.02f, 0.08f, 0.10f), top = new Color(0.03f, 0.12f, 0.15f), surfaceTint = new Color(0.08f, 0.22f, 0.30f, 0.10f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 10 },
    };

    [Header("Colores por defecto (preset bg_classic)")]
    public Color colorBottom = new Color(0.04f, 0.12f, 0.28f);
    public Color colorMid    = new Color(0.06f, 0.20f, 0.42f);
    public Color colorTop    = new Color(0.10f, 0.32f, 0.58f);

    [Header("Offset Z (detrás del tanque)")]
    public float zOffset = 1.8f;  // legacy – no se usa en runtime (ver BGZOffset)

    // El background SIEMPRE se coloca aquí independientemente del campo serializado.
    // 5 u. garantizan que ningún prefab de deco (hasta 2.5× de escala y ~1 u. de profundidad)
    // llegue a taparse con el fondo, incluso en ZBack=+1.0.
    private const float BGZOffset = 5.0f;

    // Referencias internas
    private Material     _bgMaterial;
    private MeshRenderer _dirtyOverlay;
    private MeshRenderer _nightOverlay;
    private Coroutine    _bgTransition;
    private Coroutine    _nightTransition;

    private string _currentPresetId = "bg_classic";
    public  string CurrentPresetId  => _currentPresetId;

    // ── API pública ──────────────────────────────────────────────────────────

    public void InitializeBackground()
    {
        // Destruir instancias previas (al reinicializar por cambio de tanque)
        // ⚠ 2026-08-15 — "TankNightOverlay" FALTABA en esta limpieza.
        // Cada reconexión (el móvil manda INIT en cada OnCastConnected) creaba un quad de
        // noche nuevo dejando el anterior a alpha 0.75 para siempre, porque _nightOverlay
        // sólo apunta al último. El acuario se oscurecía en escalera hasta quedar negro.
        foreach (Transform child in transform)
            if (child.name == "TankBackground" || child.name == "TankDirtyOverlay"
                || child.name == "TankNightOverlay")
                Destroy(child.gameObject);

        Bounds bounds = GetComponent<TankController>().GetTankBounds();
        BuildBackground(bounds);
        BuildDirtyOverlay(bounds);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = colorBottom;
            TvLayerDebug.Set("CAM", $"SolidColor bg=({colorBottom.r:F2},{colorBottom.g:F2},{colorBottom.b:F2})");
        }

        BuildNightOverlay(bounds);
        // -= antes del += : OnModeChanged es estático y OnDestroy nunca corre (este
        // componente no se destruye entre INITs), así que cada reconexión acumulaba una
        // suscripción más del mismo delegate.
        AmbientModeController.OnModeChanged -= OnAmbientModeChanged;
        AmbientModeController.OnModeChanged += OnAmbientModeChanged;

        // Sincronizar con el modo actual al arrancar
        var ambient = UnityEngine.Object.FindFirstObjectByType<AmbientModeController>();
        if (ambient != null) OnAmbientModeChanged(ambient.CurrentMode);
    }

    void OnDestroy()
    {
        AmbientModeController.OnModeChanged -= OnAmbientModeChanged;
    }

    private void BuildNightOverlay(Bounds bounds)
    {
        var go = new GameObject("TankNightOverlay");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 0f, BGZOffset - 0.01f);
        go.transform.localRotation = Quaternion.identity;

        float hw = bounds.size.x * 1.05f * 0.5f;
        float hh = bounds.size.y * 1.05f * 0.5f;

        var mesh = new Mesh { name = "Night_Quad" };
        mesh.vertices  = new Vector3[] {
            new Vector3(-hw, -hh, 0), new Vector3( hw, -hh, 0),
            new Vector3(-hw,  hh, 0), new Vector3( hw,  hh, 0),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv        = new Vector2[] {
            new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1),
        };
        mesh.colors = new Color[] { Color.white, Color.white, Color.white, Color.white };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;
        _nightOverlay = go.AddComponent<MeshRenderer>();
        _nightOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _nightOverlay.receiveShadows    = false;
        _nightOverlay.sortingOrder      = -99;

        var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"))
        {
            name  = "NightOverlay_Mat",
            color = new Color(0.00f, 0.00f, 0.04f, 0f),
        };
        _nightOverlay.material = mat;
    }

    private void OnAmbientModeChanged(AmbientModeController.AmbientMode mode)
    {
        float target = mode switch {
            AmbientModeController.AmbientMode.Night  => 0.75f,
            AmbientModeController.AmbientMode.Sunset => 0.20f,
            _                                        => 0f,
        };
        if (_nightTransition != null) StopCoroutine(_nightTransition);
        _nightTransition = StartCoroutine(AnimateNightOverlay(target, 2f));
    }

    private System.Collections.IEnumerator AnimateNightOverlay(float targetAlpha, float duration)
    {
        if (_nightOverlay == null) yield break;
        float startAlpha = _nightOverlay.material.color.a;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, targetAlpha, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            var c = _nightOverlay.material.color;
            c.a = a;
            _nightOverlay.material.color = c;
            yield return null;
        }
        var final = _nightOverlay.material.color;
        final.a = targetAlpha;
        _nightOverlay.material.color = final;
    }

    /// <summary>Cambia al preset de fondo indicado con transición animada.</summary>
    public void SetPreset(string bgId, bool animate = true)
    {
        BackgroundPreset? found = null;
        foreach (var p in Presets)
            if (p.id == bgId) { found = p; break; }

        if (found == null)
        {
            Debug.LogWarning($"[TankBG] Preset '{bgId}' no encontrado.");
            TvLayerDebug.Set("BG", $"PRESET NOT FOUND: {bgId}");
            return;
        }

        _currentPresetId = bgId;

        if (_bgTransition != null) StopCoroutine(_bgTransition);

        // Adaptar superficie de agua al nuevo fondo
        var ws = GetComponent<WaterSurface>();
        if (ws != null) ws.SetTint(found.Value.surfaceTint);

        // Intentar cargar imagen desde Resources/Backgrounds/{bgId}
        var tex = Resources.Load<Texture2D>($"Backgrounds/{bgId}");
        if (tex != null)
        {
            ApplyImageTexture(tex);
            TvLayerDebug.Set("BG", $"{bgId} IMAGE shader={_bgMaterial?.shader?.name ?? "null"} tex={tex.width}x{tex.height}");
            return;
        }

        TvLayerDebug.Set("BG", $"{bgId} GRADIENT (no image) shader={_bgMaterial?.shader?.name ?? "null"}");
        // Fallback: gradiente procedural
        if (animate)
            _bgTransition = StartCoroutine(TransitionGradient(found.Value.bottom, found.Value.mid, found.Value.top, 1.2f));
        else
            ApplyGradient(found.Value.bottom, found.Value.mid, found.Value.top);
    }

    private void ApplyImageTexture(Texture2D tex)
    {
        if (_bgMaterial == null) return;
        if (_bgMaterial.HasProperty("_BaseMap")) _bgMaterial.SetTexture("_BaseMap", tex);
        else if (_bgMaterial.HasProperty("_MainTex")) _bgMaterial.SetTexture("_MainTex", tex);
        // Limpiar tinte de color para que la imagen se vea sin tint
        if (_bgMaterial.HasProperty("_BaseColor")) _bgMaterial.SetColor("_BaseColor", Color.white);
        else if (_bgMaterial.HasProperty("_Color")) _bgMaterial.SetColor("_Color", Color.white);
    }

    /// <summary>
    /// Actualiza la opacidad del overlay de algas según nivel de suciedad (0=limpio, 1=sucio).
    /// Solo tiene efecto si AppFlags.EnableNeglectVisuals está activo.
    /// </summary>
    public void SetDirtyLevel(float level)
    {
        if (!AppFlags.EnableNeglectVisuals || _dirtyOverlay == null) return;
        float alpha = Mathf.Lerp(0f, 0.38f, level * level);
        var c = _dirtyOverlay.material.color;
        c.a = alpha;
        _dirtyOverlay.material.color = c;
    }

    // ── Construcción ─────────────────────────────────────────────────────────

    private void BuildBackground(Bounds bounds)
    {
        var go = new GameObject("TankBackground");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 0f, BGZOffset);
        go.transform.localRotation = Quaternion.identity;

        float w = bounds.size.x * 1.05f;
        float h = bounds.size.y * 1.05f;

        var mesh = new Mesh { name = "BG_Quad" };
        float hw = w * 0.5f;
        float hh = h * 0.5f;

        mesh.vertices  = new Vector3[] {
            new Vector3(-hw, -hh, 0),
            new Vector3( hw, -hh, 0),
            new Vector3(-hw,  hh, 0),
            new Vector3( hw,  hh, 0),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv        = new Vector2[] {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 1), new Vector2(1, 1),
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sortingOrder      = -100;  // siempre detrás de cualquier deco (rango -8…+10)

        _bgMaterial = BuildGradientMaterial(colorBottom, colorMid, colorTop);
        mr.material = _bgMaterial;

        // Aplicar tinte de la superficie de agua para el preset inicial
        // (WaterSurface debe haberse inicializado antes, o hacerlo en LateStart vía SetPreset)
        foreach (var p in Presets)
        {
            if (p.id != _currentPresetId) continue;
            var ws = GetComponent<WaterSurface>();
            if (ws != null) ws.SetTint(p.surfaceTint);
            break;
        }

        // Si existe imagen para el preset inicial, aplicarla
        var initTex = Resources.Load<Texture2D>($"Backgrounds/{_currentPresetId}");
        if (initTex != null) { ApplyImageTexture(initTex); }
    }

    private void BuildDirtyOverlay(Bounds bounds)
    {
        var go = new GameObject("TankDirtyOverlay");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 0f, BGZOffset - 0.05f);
        go.transform.localRotation = Quaternion.identity;

        float hw = bounds.size.x * 1.05f * 0.5f;
        float hh = bounds.size.y * 1.05f * 0.5f;

        var mesh = new Mesh { name = "Dirty_Quad" };
        mesh.vertices  = new Vector3[] {
            new Vector3(-hw, -hh, 0), new Vector3( hw, -hh, 0),
            new Vector3(-hw,  hh, 0), new Vector3( hw,  hh, 0),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv        = new Vector2[] {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 1), new Vector2(1, 1),
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;

        _dirtyOverlay = go.AddComponent<MeshRenderer>();
        _dirtyOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _dirtyOverlay.receiveShadows    = false;
        _dirtyOverlay.sortingOrder      = -9;

        var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"))
        {
            name  = "DirtyOverlay_Mat",
            color = new Color(0.05f, 0.20f, 0.07f, 0f)
        };
        _dirtyOverlay.material = mat;
    }

    // ── Gradient helpers ──────────────────────────────────────────────────────

    private void ApplyGradient(Color bottom, Color mid, Color top)
    {
        if (_bgMaterial == null) return;

        const int texW = 64;
        const int texH = 64;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "BG_Gradient"
        };

        for (int y = 0; y < texH; y++)
        {
            float ty  = y / (float)(texH - 1);
            Color col = ty < 0.5f
                ? Color.Lerp(bottom, mid, ty * 2f)
                : Color.Lerp(mid, top, (ty - 0.5f) * 2f);
            for (int x = 0; x < texW; x++)
                tex.SetPixel(x, y, col);
        }
        tex.Apply();

        if (_bgMaterial.HasProperty("_BaseMap")) _bgMaterial.SetTexture("_BaseMap", tex);
        else if (_bgMaterial.HasProperty("_MainTex")) _bgMaterial.SetTexture("_MainTex", tex);

        colorBottom = bottom;
        colorMid    = mid;
        colorTop    = top;
    }

    private IEnumerator TransitionGradient(Color targetBot, Color targetMid, Color targetTop, float duration)
    {
        Color startBot = colorBottom;
        Color startMid = colorMid;
        Color startTop = colorTop;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / duration);
            ApplyGradient(
                Color.Lerp(startBot, targetBot, e),
                Color.Lerp(startMid, targetMid, e),
                Color.Lerp(startTop, targetTop, e)
            );
            yield return null;
        }

        ApplyGradient(targetBot, targetMid, targetTop);
    }

    private static Material BuildGradientMaterial(Color bottom, Color mid, Color top)
    {
        const int texW = 64;
        const int texH = 64;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "BG_Gradient"
        };

        for (int y = 0; y < texH; y++)
        {
            float ty  = y / (float)(texH - 1);
            Color col = ty < 0.5f
                ? Color.Lerp(bottom, mid, ty * 2f)
                : Color.Lerp(mid, top, (ty - 0.5f) * 2f);
            for (int x = 0; x < texW; x++)
                tex.SetPixel(x, y, col);
        }
        tex.Apply();

        // Sprites/Default está garantizado en Always Included Shaders y renderiza
        // texturas correctamente en WebGL. URP/Unlit tiene un bug de color space en WebGL.
        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("UI/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Texture");

        JsBridge.Log($"[TankBG] shader={shader?.name ?? "NULL(error)"}");

        var mat = new Material(shader) { name = "BG_Mat" };

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

        return mat;
    }
}
