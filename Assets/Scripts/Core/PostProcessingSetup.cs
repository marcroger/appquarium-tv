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
    // ⚠⚠ 2026-08-28 — BLOOM ENCENDIDO, y el umbral es la razon de que antes «no aportara
    // nada». Medido en el device (10 peces, 3 decos, medidor de 1 s, alternando con la
    // referencia): a umbral 0.92 el efecto es INVISIBLE — la escena queda en L* 43.2 contra
    // 43.1-43.9 de las tres referencias apagadas, o sea dentro de su propia dispersion. A
    // 0.60 la escena sube a **L* 51.9 (+8)** y el agua alta de 75.9 a **88.3 (+12)**.
    // Coste: +1.1 fps a favor del encendido con banda ±4.9 -> indistinguible de cero.
    // 🧭 El barrido de agosto concluyo «el bloom no aporta nada» porque `grade-tune.js` NUNCA
    //    mandaba el umbral: encendia un efecto que no cruzaba ningun pixel y pagaba el coste.
    // Elegido por el user viendo las cuatro variantes: «mas brillo y vida que azul profundo».
    // ⚠ `bloomHQ` se queda en false: sumaba solo +1.2 L* mas y su coste NO se llego a medir.
    public bool      enableBloom    = true;
    [Range(0f, 3f)]  public float bloomIntensity  = 1.2f;
    [Range(0f, 1f)]  public float bloomThreshold  = 0.60f;   // 2026-08-28 (era 0.92, invisible)
    [Range(0f, 1f)]  public float bloomScatter    = 0.75f;
    // ⚠ 2026-08-28 — expuestos para barrerlos por el mensaje GRADE sin gastar un build
    // por variante. El movil va a threshold 0.60 / scatter 0.75 / HQ true; aqui el HQ va a
    // false a proposito (Mali-G31). Las tres ultimas son las palancas de COSTE de la
    // piramide de mips: estaban en los defaults de URP y nadie las habia tocado nunca.
    public bool      bloomHQ             = false;  // highQualityFiltering
    [Range(0, 1)]    public int bloomDownscale     = 0;  // 0 = Half (default), 1 = Quarter
    [Range(2, 8)]    public int bloomMaxIterations = 6;  // default URP
    [Range(0, 4)]    public int bloomSkipIterations = 1; // default URP

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
    /// <summary>
    /// Las palancas de COSTE del bloom. Van aparte y por reflexion porque `downscale`,
    /// `maxIterations` y `skipIterations` no existen en todas las versiones de URP: si el
    /// campo no esta, se ignora en vez de romper el build.
    /// </summary>
    private void AplicarPiramideBloom()
    {
        if (_bloom == null) return;
        var t = _bloom.GetType();
        var dn = t.GetField("downscale");
        if (dn != null)
        {
            var par = dn.GetValue(_bloom);
            var mi = par == null ? null : par.GetType().GetMethod("Override");
            if (mi != null)
            {
                var tipo = mi.GetParameters()[0].ParameterType;
                if (tipo.IsEnum) mi.Invoke(par, new object[] { System.Enum.ToObject(tipo, bloomDownscale) });
            }
        }
        PonerIntBloom(t, "maxIterations",  bloomMaxIterations);
        PonerIntBloom(t, "skipIterations", bloomSkipIterations);
    }

    private void PonerIntBloom(System.Type t, string campo, int valor)
    {
        var f = t.GetField(campo);
        if (f == null) return;
        var par = f.GetValue(_bloom);
        var mi = par == null ? null : par.GetType().GetMethod("Override", new[] { typeof(int) });
        if (mi != null) mi.Invoke(par, new object[] { valor });
    }

    public void AplicarValores()
    {
        if (_volume == null || _color == null) { BuildVolume(); return; }

        _bloom.active = enableBloom;
        _bloom.intensity.Override(bloomIntensity);
        _bloom.threshold.Override(bloomThreshold);
        _bloom.scatter.Override(bloomScatter);
        _bloom.highQualityFiltering.Override(bloomHQ);
        AplicarPiramideBloom();

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
        // ⚠ Ya NO va clavado a false: se puede barrer por GRADE. El DEFAULT del campo
        // sigue siendo false por el Mali-G31 — la conducta solo cambia si alguien lo manda.
        _bloom.highQualityFiltering.Override(bloomHQ);
        AplicarPiramideBloom();

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
