using UnityEngine;

/// <summary>
/// Sistema de burbujas generado 100% por código.
/// Crea un ParticleSystem que emite burbujas desde el fondo del tanque
/// que suben con ligera deriva lateral, se agrandan y desvanecen al llegar arriba.
///
/// Setup:
///   1. Añadir este script al mismo GameObject que TankController.
///   2. TankController.Initialize() llama a BubbleSystem.InitializeBubbles()
///      automáticamente — no se necesita nada más en el Inspector.
///
/// Tecla de debug: B = toggle burbujas On/Off
/// </summary>
[RequireComponent(typeof(TankController))]
public class BubbleSystem : MonoBehaviour
{
    [Header("Emisión")]
    [Range(1f, 30f)]  public float emissionRate   = 8f;
    [Range(1f, 20f)]  public float maxParticles   = 150f;

    [Header("Tamaño")]
    [Range(0.02f, 0.2f)] public float minSize = 0.04f;
    [Range(0.02f, 0.2f)] public float maxSize = 0.12f;

    [Header("Velocidad ascendente")]
    [Range(0.1f, 2f)] public float minSpeed = 0.3f;
    [Range(0.1f, 2f)] public float maxSpeed = 0.9f;

    [Header("Duración de vida")]
    [Range(2f, 15f)] public float minLifetime = 4f;
    [Range(2f, 15f)] public float maxLifetime = 9f;

    [Header("Deriva lateral (Noise)")]
    [Range(0f, 1f)] public float noiseStrength = 0.15f;
    [Range(0.1f, 2f)] public float noiseFrequency = 0.4f;

    // ── Internos ─────────────────────────────────────────────────────────────

    private ParticleSystem _ps;
    private TankController _tank;
    private bool           _active = true;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Awake()
    {
        _tank = GetComponent<TankController>();
    }

    /// <summary>
    /// Llamado por TankController.Initialize() una vez los bounds están listos.
    /// </summary>
    public void InitializeBubbles()
    {
        // Destruir instancias previas (al reinicializar por cambio de tanque)
        foreach (Transform child in transform)
            if (child.name == "Bubbles_PS")
                Destroy(child.gameObject);
        _ps = null;

        BuildParticleSystem();
        Debug.Log("[Bubbles] ✅ Sistema de burbujas activo");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
            ToggleBubbles();
    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void ToggleBubbles()
    {
        _active = !_active;
        if (_active)
            _ps.Play();
        else
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        Debug.Log($"[Bubbles] {(_active ? "ON" : "OFF")}");
    }

    public void SetEmissionRate(float rate)
    {
        emissionRate = rate;
        if (_ps == null) return;
        var emission = _ps.emission;
        emission.rateOverTime = rate;
    }

    // ── Construcción del ParticleSystem ──────────────────────────────────────

    private void BuildParticleSystem()
    {
        // GameObject hijo para no interferir con TankController
        var psGo = new GameObject("Bubbles_PS");
        psGo.transform.SetParent(transform);
        psGo.transform.localPosition = Vector3.zero;

        _ps = psGo.AddComponent<ParticleSystem>();

        Bounds bounds = _tank.GetTankBounds();
        float  w      = bounds.size.x;
        float  h      = bounds.size.y;
        float  d      = bounds.size.z;

        // ── Main module ───────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop                 = true;
        main.startLifetime        = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed           = new ParticleSystem.MinMaxCurve(0f);  // velocidad controlada por velocityOverLifetime
        main.startSize            = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.gravityModifier      = -0.05f;   // empuje suave hacia arriba
        main.maxParticles         = (int)maxParticles;
        main.simulationSpace      = ParticleSystemSimulationSpace.World;

        // Color inicial: blanco semitransparente
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 0.75f),
            new Color(0.85f, 0.92f, 1f, 0.5f)
        );

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.rateOverTime = emissionRate;

        // ── Shape: línea en el fondo del tanque ───────────────────────────────
        var shape = _ps.shape;
        shape.enabled      = true;
        shape.shapeType    = ParticleSystemShapeType.Box;
        shape.scale        = new Vector3(w * 0.85f, 0.05f, d * 0.85f);
        // Posicionar en el fondo del tanque
        psGo.transform.localPosition = new Vector3(0f, -h * 0.5f + 0.1f, 0f);

        // ── Velocity over lifetime: subir recto ───────────────────────────────
        var vel = _ps.velocityOverLifetime;
        vel.enabled  = true;
        vel.space    = ParticleSystemSimulationSpace.World;
        // X, Y, Z deben estar en el mismo modo (TwoConstants) para evitar el error
        vel.x        = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);   // deriva lateral mínima
        vel.y        = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        vel.z        = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);   // deriva frontal mínima

        // ── Size over lifetime: se agrandan mientras suben ────────────────────
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f,    0.4f);   // pequeñas al nacer
        sizeCurve.AddKey(0.6f,  1.0f);   // tamaño máximo
        sizeCurve.AddKey(1.0f,  1.1f);   // ligero pop al final

        var sizeOL = _ps.sizeOverLifetime;
        sizeOL.enabled = true;
        sizeOL.size    = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // ── Color over lifetime: desvanece en el tope ─────────────────────────
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.85f, 0.93f, 1f), 0.0f),
                new GradientColorKey(new Color(1f,    1f,    1f), 0.5f),
                new GradientColorKey(new Color(1f,    1f,    1f), 1.0f)
            },
            new[] {
                new GradientAlphaKey(0.0f,  0.0f),   // aparece gradualmente
                new GradientAlphaKey(0.7f,  0.15f),  // visible
                new GradientAlphaKey(0.6f,  0.80f),  // se mantiene
                new GradientAlphaKey(0.0f,  1.0f)    // desaparece al llegar arriba
            }
        );

        var colorOL = _ps.colorOverLifetime;
        colorOL.enabled = true;
        colorOL.color   = new ParticleSystem.MinMaxGradient(gradient);

        // ── Noise: deriva lateral orgánica ────────────────────────────────────
        var noise = _ps.noise;
        noise.enabled           = true;
        noise.strength          = noiseStrength;
        noise.frequency         = noiseFrequency;
        noise.scrollSpeed       = 0.2f;
        noise.damping           = true;
        noise.octaveCount       = 2;

        // ── Material con textura circular ────────────────────────────────────
        var renderer = _ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        // Sprites/Default: siempre respeta alpha de la textura, funciona en todas las versiones URP
        Shader bubbleShader = Shader.Find("Sprites/Default")
                           ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                           ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");

        if (bubbleShader != null)
        {
            var tex = BuildBubbleTex();
            var mat = new Material(bubbleShader);
            mat.SetTexture("_MainTex", tex);
            mat.renderQueue = 3000;
            renderer.material = mat;
        }

        renderer.sortingOrder = 1;

        // Arrancar
        _ps.Play();
    }

    /// <summary>
    /// Genera una textura circular para la burbuja: anillo semitransparente,
    /// hueco en el centro (para que parezca una burbuja de cristal, no un círculo sólido).
    /// </summary>
    private Texture2D BuildBubbleTex()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "BubbleTex"
        };

        float center = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx   = (x - center) / center;
                float dy   = (y - center) / center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);  // 0 = centro, 1 = borde

                float alpha;
                if (dist > 1.0f)
                {
                    alpha = 0f;  // fuera del círculo
                }
                else if (dist > 0.82f)
                {
                    // Anillo exterior: fade in → peak → fade out
                    float t = (dist - 0.82f) / 0.18f;
                    alpha = Mathf.Sin(t * Mathf.PI) * 0.9f;
                }
                else if (dist > 0.55f)
                {
                    // Interior del anillo: casi transparente
                    alpha = (dist - 0.55f) / 0.27f * 0.15f;
                }
                else
                {
                    // Centro: completamente transparente (burbuja hueca)
                    alpha = 0f;
                }

                // Pequeño destello en la parte superior izquierda (reflejo de luz)
                float glowDx   = dx + 0.35f;
                float glowDy   = dy - 0.35f;
                float glowDist = Mathf.Sqrt(glowDx * glowDx + glowDy * glowDy);
                float glow     = Mathf.Max(0f, 1f - glowDist / 0.22f) * 0.6f;

                float finalAlpha = Mathf.Clamp01(alpha + glow * (1f - dist));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, finalAlpha));
            }
        }

        tex.Apply();
        return tex;
    }
}
