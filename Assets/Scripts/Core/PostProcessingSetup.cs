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
        if (enableBloom)
        {
            var bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(bloomScatter);
            bloom.highQualityFiltering.Override(false); // Cast device GPU — high quality too expensive
        }

        // ── Tonemapping ───────────────────────────────────────────────────────
        // Neutral: preserva colores de autor sin el gris-shift de ACES.
        // Coste cero — se hornea en el mismo LUT pass que ColorAdjustments.
        if (enableTonemapping)
        {
            var tm = profile.Add<Tonemapping>(true);
            tm.active = true;
            tm.mode.Override(TonemappingMode.Neutral);
        }

        // ── Color Adjustments ─────────────────────────────────────────────────
        var color = profile.Add<ColorAdjustments>(true);
        color.active = true;
        color.colorFilter.Override(colorFilter);
        color.contrast.Override(contrast);
        color.saturation.Override(saturation);
        color.postExposure.Override(postExposure);

        // ── Vignette ──────────────────────────────────────────────────────────
        var vignette = profile.Add<Vignette>(true);
        vignette.active     = true;
        vignette.color.Override(vignetteColor);
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(vignetteSmoothness);
        vignette.rounded.Override(true);

        _volume.profile = profile;

        TvLayerDebug.Set("PostFX", $"bloom={(enableBloom ? bloomIntensity.ToString("F2") : "OFF")} tm={(enableTonemapping?"Neutral":"OFF")} sat={saturation:F0} con={contrast:F0}");
        Debug.Log($"[PostFX] ✅ Bloom + Color + Vignette activos ({profile.components.Count} efectos). [P]=toggle [O]=estado");
    }
}
