using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Crea y configura el Volume de post-processing por código.
/// Efectos: Bloom sutil + Color Adjustments (tono submarino) + Vignette.
///
/// Setup:
///   1. Asegúrate de que el URP Renderer tiene "Post Processing" activado.
///   2. Asegúrate de que la Main Camera tiene "Post Processing" activado.
///   3. Añadir este script a cualquier GameObject de la escena (ej: Aquarium).
///   4. Al darle Play se autoconfigura — no hay que tocar nada más.
///
/// Teclas debug: P = toggle post-processing on/off
/// </summary>
public class PostProcessingSetup : MonoBehaviour
{
    [Header("Bloom")]
    [Tooltip("Bloom es un blur multi-pass: el efecto de post-proceso más caro en GPU. " +
             "OFF en el Cast device (Mali-G31) — se mantienen Color + Vignette, que son baratos. " +
             "Poner ON solo para builds de escritorio/preview.")]
    public bool      enableBloom    = false;
    [Range(0f, 3f)]  public float bloomIntensity  = 0.35f;
    [Range(0f, 1f)]  public float bloomThreshold  = 0.92f;
    [Range(0f, 1f)]  public float bloomScatter    = 0.6f;

    [Header("Tonemapping")]
    public bool enableTonemapping = true;

    [Header("Color (tono submarino)")]
    public Color  colorFilter      = new Color(0.95f, 0.98f, 1.00f);  // casi neutro, toque frío mínimo
    [Range(-100f, 100f)] public float contrast    = 10f;
    [Range(-50f, 50f)] public float saturation    = 18f;
    [Range(-1f, 1f)] public float postExposure    = 0.0f;

    [Header("Vignette")]
    [Range(0f, 1f)]  public float vignetteIntensity  = 0.18f;          // muy sutil
    [Range(0f, 1f)]  public float vignetteSmoothness = 0.6f;
    public Color                  vignetteColor       = new Color(0f, 0.05f, 0.12f);

    // ── Interno ──────────────────────────────────────────────────────────────
    private Volume _volume;

    // Referencias a los efectos, para poder cambiar valores SIN reconstruir el Volume.
    // Reconstruirlo por cada cambio (destruir + crear) resultó ser una carrera: capturas
    // seguidas salían unas con el grado nuevo y otras con el viejo (medido el 21-ago).
    private Bloom            _bloom;
    private Tonemapping      _tonemapping;
    private ColorAdjustments _color;
    private Vignette         _vignette;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Start()
    {
        BuildVolume();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log($"[PostFX] P pulsado — _volume es {(_volume == null ? "NULL" : "OK")}");
            TogglePostProcessing();
        }
        if (Input.GetKeyDown(KeyCode.O))
            Debug.Log($"[PostFX] Estado actual — volume={(_volume == null ? "NULL" : _volume.enabled.ToString())} profile={(_volume?.profile == null ? "NULL" : _volume.profile.name)}");
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reconstruye el Volume con los valores actuales de los campos públicos.
    /// Lo usa el barrido de grado del Editor (`Appquarium TV → 🎨 Barrido de grado`) para
    /// comparar variantes sin gastar un build, y es lo que necesitará el control en caliente
    /// por el mensaje DIAG cuando se afine en la tele.
    /// </summary>
    public void Rebuild()
    {
        BuildVolume();
    }

    /// <summary>
    /// Empuja los valores actuales de los campos a los efectos YA creados, sin tocar el Volume.
    /// Es la vía buena para cambiar el grado en caliente: la usa el barrido del Editor y es la
    /// que necesitará el control por `DIAG` para afinar en la tele sin gastar un build por
    /// variante. Si el Volume aún no existe, construye.
    /// </summary>
    public void AplicarValores()
    {
        if (_volume == null || _color == null) { BuildVolume(); return; }

        _bloom.active = enableBloom;
        _bloom.intensity.Override(bloomIntensity);
        _bloom.threshold.Override(bloomThreshold);
        _bloom.scatter.Override(bloomScatter);

        _tonemapping.active = enableTonemapping;

        _color.colorFilter.Override(colorFilter);
        _color.contrast.Override(contrast);
        _color.saturation.Override(saturation);
        _color.postExposure.Override(postExposure);

        _vignette.intensity.Override(vignetteIntensity);
        _vignette.smoothness.Override(vignetteSmoothness);
        _vignette.color.Override(vignetteColor);

        TvLayerDebug.Set("PostFX", $"bloom={(enableBloom ? bloomIntensity.ToString("F2") : "OFF")} " +
                                   $"tm={(enableTonemapping ? "Neutral" : "OFF")} sat={saturation:F0} con={contrast:F0}");
    }

    public void TogglePostProcessing()
    {
        if (_volume != null)
        {
            _volume.enabled = !_volume.enabled;
            Debug.Log($"[PostFX] {(_volume.enabled ? "ON" : "OFF")}");
        }
    }

    // ── Construcción ─────────────────────────────────────────────────────────

    private void BuildVolume()
    {
        Debug.Log("[PostFX] BuildVolume() iniciado...");

        // ⚠ Limpiar el anterior antes de crear otro. Sin esto, cada Rebuild() apilaría un
        // Volume global más con la misma prioridad y el resultado dependería del orden en que
        // URP los evalúe — el mismo patrón de bug que los quads de noche apilados del 15-ago.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != "PostProcessVolume") continue;
            // Destroy() es DIFERIDO al final del frame: si sólo lo destruyéramos, el Volume
            // viejo seguiría influyendo durante un frame junto al nuevo. Desactivarlo primero
            // lo saca del cálculo de URP inmediatamente.
            var viejo = child.GetComponent<Volume>();
            if (viejo != null) viejo.enabled = false;
            if (Application.isPlaying) Destroy(child.gameObject);
            else                       DestroyImmediate(child.gameObject);
        }

        var go = new GameObject("PostProcessVolume");
        go.transform.SetParent(transform);

        _volume          = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;

        Debug.Log($"[PostFX] Volume creado: isGlobal={_volume.isGlobal}, priority={_volume.priority}");

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // ── Bloom ─────────────────────────────────────────────────────────────
        // Desactivado en el Cast device: el blur multi-pass del bloom es el efecto
        // más caro en el Mali-G31. Sin él, el pass de post-proceso queda en Color +
        // Vignette (single-pass, barato). Recortar bloom recupera la mayor parte del
        // coste GPU del post-proceso manteniendo el tono submarino.
        // Se añade SIEMPRE y se enciende con `active`: así se puede alternar en caliente sin
        // reconstruir nada. Un efecto con active=false no cuesta pases de GPU.
        _bloom = profile.Add<Bloom>(true);
        _bloom.active = enableBloom;
        _bloom.intensity.Override(bloomIntensity);
        _bloom.threshold.Override(bloomThreshold);
        _bloom.scatter.Override(bloomScatter);
        _bloom.highQualityFiltering.Override(false); // Cast device GPU — high quality too expensive

        // ── Tonemapping ───────────────────────────────────────────────────────
        // Neutral: preserva colores de autor sin el gris-shift de ACES.
        // Coste cero — se hornea en el mismo LUT pass que ColorAdjustments.
        _tonemapping = profile.Add<Tonemapping>(true);
        _tonemapping.active = enableTonemapping;
        _tonemapping.mode.Override(TonemappingMode.Neutral);

        // ── Color Adjustments ─────────────────────────────────────────────────
        _color = profile.Add<ColorAdjustments>(true);
        _color.active = true;
        _color.colorFilter.Override(colorFilter);
        _color.contrast.Override(contrast);
        _color.saturation.Override(saturation);
        _color.postExposure.Override(postExposure);

        // ── Vignette ──────────────────────────────────────────────────────────
        _vignette = profile.Add<Vignette>(true);
        _vignette.active     = true;
        _vignette.color.Override(vignetteColor);
        _vignette.intensity.Override(vignetteIntensity);
        _vignette.smoothness.Override(vignetteSmoothness);
        _vignette.rounded.Override(true);

        _volume.profile = profile;

        TvLayerDebug.Set("PostFX", $"bloom={(enableBloom ? bloomIntensity.ToString("F2") : "OFF")} tm={(enableTonemapping?"Neutral":"OFF")} sat={saturation:F0} con={contrast:F0}");
        Debug.Log($"[PostFX] ✅ Bloom + Color + Vignette activos ({profile.components.Count} efectos). [P]=toggle [O]=estado");
    }
}
