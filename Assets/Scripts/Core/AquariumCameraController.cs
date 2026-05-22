using UnityEngine;

/// <summary>
/// Cámara fullscreen siempre.
/// La cámara define el espacio del mundo — el tanque se adapta a lo que ve la cámara.
///
/// Setup:
///   1. Añadir este script a la Main Camera.
///   2. En AquariumManager asignar este componente al campo cameraController.
///   3. AquariumManager llama a SetupAndGetBounds() ANTES de inicializar el tanque.
///
/// No hay que tocar nada más — siempre fullscreen en cualquier resolución.
/// </summary>
[RequireComponent(typeof(Camera))]
public class AquariumCameraController : MonoBehaviour
{
    [Header("Tamaño del mundo")]
    [Tooltip("Mitad del alto visible en unidades de mundo. Ancho se calcula automático por aspect ratio.")]
    public float worldHalfHeight = 5f;   // pantalla = 10 unidades de alto

    [Header("Profundidad del tanque")]
    [Tooltip("Profundidad en Z del tanque (no afecta al tamaño en pantalla)")]
    public float tankDepth = 4.8f;

    [Header("Distancia Z de la cámara al centro del tanque")]
    public float cameraZ = -10f;

    [Header("Follow mode")]
    [Tooltip("Velocidad de lerp de posición y zoom al seguir un pez")]
    public float followLerpSpeed = 4f;
    [Tooltip("El pez ocupa esta fracción del alto de pantalla al hacer zoom (0.45 = 45%)")]
    [Range(0.15f, 0.95f)]
    public float fishFillRatio = 0.45f;

    // ── Estado follow ───────────────────────────────────────────────────────────
    private FishAgent _followTarget;
    private bool      _following;
    private float     _targetOrthoSize;   // calculado por especie al hacer FollowFish
    public  bool      IsFollowing => _following;

    // ── Referencia ─────────────────────────────────────────────────────────────
    private Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        // Evitar fondo gris de Unity desde el primer frame, antes de que TankBackground.InitializeBackground() corra.
        _cam.clearFlags       = CameraClearFlags.SolidColor;
        _cam.backgroundColor  = new Color(0.01f, 0.03f, 0.10f, 1f);
    }

    void Update()
    {
        // Si el pez fue despawneado, volver al overview
        if (_following && _followTarget == null)
            _following = false;

        if (_following)
        {
            // ── Calcular posición destino, limitada a los bordes del tanque ──
            float aspect    = (float)Screen.width / Screen.height;
            float tankHalfH = worldHalfHeight;
            float tankHalfW = worldHalfHeight * aspect;
            // Semiancho/semialto visible de la cámara con el zoom destino
            float camHalfH  = _targetOrthoSize;
            float camHalfW  = _targetOrthoSize * aspect;

            float maxX = Mathf.Max(0f, tankHalfW - camHalfW);
            float maxY = Mathf.Max(0f, tankHalfH - camHalfH);

            float cx = Mathf.Clamp(_followTarget.transform.position.x, -maxX, maxX);
            float cy = Mathf.Clamp(_followTarget.transform.position.y, -maxY, maxY);

            transform.position    = Vector3.Lerp(transform.position,
                                        new Vector3(cx, cy, cameraZ),
                                        Time.deltaTime * followLerpSpeed);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, _targetOrthoSize,
                                        Time.deltaTime * followLerpSpeed);
        }
        else
        {
            // Volver suavemente al centro con el zoom original
            transform.position    = Vector3.Lerp(transform.position,
                                        new Vector3(0f, 0f, cameraZ),
                                        Time.deltaTime * followLerpSpeed);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, worldHalfHeight,
                                        Time.deltaTime * followLerpSpeed);
        }
    }

    /// <summary>
    /// Empieza a seguir al pez con un zoom suave calculado a partir de sus bounds reales.
    /// fishFillRatio marca el objetivo pero se limita a 0.30 para evitar zoom agresivo.
    /// </summary>
    public void FollowFish(FishAgent fish)
    {
        _followTarget = fish;
        _following    = true;

        // Bounds reales del renderer (más fiable que baseSize para modelos Pack 24)
        var rend = fish.GetComponentInChildren<Renderer>();
        float fishHeight = (rend != null && rend.bounds.size.y > 0.01f)
            ? rend.bounds.size.y
            : (fish.Data?.baseSize ?? 1f);

        // El pez ocupa hasta el 55% del alto visible al hacer zoom
        float clampedRatio = Mathf.Clamp(fishFillRatio, 0.10f, 0.55f) * 2f;
        _targetOrthoSize   = Mathf.Clamp(
            fishHeight / clampedRatio,
            worldHalfHeight * 0.20f,   // mínimo: zoom cercano
            worldHalfHeight * 0.55f);  // máximo: zoom moderado (antes 0.80)
    }

    public void StopFollow()
    {
        _followTarget = null;
        _following    = false;
    }

    // ── API pública ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Configura la cámara ortográfica fullscreen y devuelve los Bounds del tanque.
    /// Llamar ANTES de inicializar TankController.
    /// </summary>
    public Bounds SetupAndGetBounds()
    {
        _cam.orthographic     = true;
        _cam.orthographicSize = worldHalfHeight;

        transform.position = new Vector3(0f, 0f, cameraZ);
        transform.rotation = Quaternion.identity;

        float aspect = (float)Screen.width / Screen.height;
        float w      = worldHalfHeight * 2f * aspect;
        float h      = worldHalfHeight * 2f;

        var bounds = new Bounds(Vector3.zero, new Vector3(w, h, tankDepth));

        Debug.Log($"[Camera] ✅ Fullscreen ortográfica — mundo: {w:F1}×{h:F1} u, aspect: {aspect:F2}");
        return bounds;
    }

    /// <summary>
    /// Devuelve los Bounds actuales que ve la cámara (sin reconfigurar).
    /// </summary>
    public Bounds GetCurrentBounds()
    {
        float aspect = (float)Screen.width / Screen.height;
        float h      = _cam.orthographicSize * 2f;
        float w      = h * aspect;
        return new Bounds(Vector3.zero, new Vector3(w, h, tankDepth));
    }
}
