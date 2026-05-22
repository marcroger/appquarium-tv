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
    [Range(0f, 3f)]  public float bloomIntensity  = 0.35f;
    [Range(0f, 1f)]  public float bloomThreshold  = 0.92f;
    [Range(0f, 1f)]  public float bloomScatter    = 0.6f;

    [Header("Color (tono submarino)")]
    public Color  colorFilter      = new Color(0.95f, 0.98f, 1.00f);  // casi neutro, toque frío mínimo
    [Range(-50f, 50f)] public float saturation    = 0f;                // sin desaturar
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

        var go = new GameObject("PostProcessVolume");
        go.transform.SetParent(transform);

        _volume          = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;

        Debug.Log($"[PostFX] Volume creado: isGlobal={_volume.isGlobal}, priority={_volume.priority}");

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // ── Bloom ─────────────────────────────────────────────────────────────
        var bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.intensity.Override(bloomIntensity);
        bloom.threshold.Override(bloomThreshold);
        bloom.scatter.Override(bloomScatter);
        bloom.highQualityFiltering.Override(true);

        // ── Color Adjustments ─────────────────────────────────────────────────
        var color = profile.Add<ColorAdjustments>(true);
        color.active = true;
        color.colorFilter.Override(colorFilter);
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

        Debug.Log($"[PostFX] ✅ Bloom + Color + Vignette activos ({profile.components.Count} efectos). [P]=toggle [O]=estado");
    }
}
