using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Barra LED del acuario: 3 SpotLights + 1 fill ambiental + post-process Volume propio.
///
/// Por qué se necesita el post-process Volume:
///   TankBackground usa un shader procedural unlit y las decos usan URP/Unlit.
///   Los SpotLights no afectan objetos unlit, así que el cambio de color no era visible.
///   La solución es un Volume de prioridad 11 (> PostProcessingSetup=10) que sobreescribe
///   ColorAdjustments.colorFilter y postExposure — esto multiplica el frame completo
///   por el color del LED, afectando tanto objetos lit como unlit.
///
/// Por qué los spots necesitan alta intensidad:
///   La luz direccional (AmbientModeController día = 1.2) + ambient (0.70,0.85,1.00) ya satura
///   el frame. Para que el color de los LEDs sea visible en objetos lit, los spots deben
///   superar esa base en HDR, y el bloom @ threshold=0.92 dispersa el color.
///
/// Presets (todos gratuitos).
/// </summary>
[RequireComponent(typeof(TankController))]
public class TankLightingController : MonoBehaviour
{
    [System.Serializable]
    public struct LightPreset
    {
        public string id;
        public string displayName;
        public Color  color;             // color LED (spots + ambient tint)
        public float  spotIntensity;     // intensidad por spot (HDR — puede superar 1)
        public float  dirDimFactor;      // 0=apaga directional, 1=sin cambio
        public float  ambientBlend;      // 0=ambient base, 1=ambient completamente del color LED
        public Color  filterColor;       // post-process colorFilter: multiplica el frame completo
        public float  exposureOffset;    // post-process exposure offset (negativo = más oscuro)
        public bool   animated;
        public bool   isStarterGift;
        public float  price;
        public int    pearlPrice;
        public int    displayOrder;
    }

    public static readonly LightPreset[] Presets =
    {
        new LightPreset
        {
            id = "light_white",  displayName = "light.white",
            color = new Color(1.00f, 0.97f, 0.93f),
            spotIntensity = 1.0f,  dirDimFactor = 1.00f, ambientBlend = 0.00f,
            filterColor   = new Color(1.00f, 1.00f, 1.00f), exposureOffset =  0.00f,
            isStarterGift = true, price = 0f, pearlPrice =  0, displayOrder = 0
        },
        new LightPreset
        {
            id = "light_warm",   displayName = "light.warm",
            color = new Color(1.00f, 0.80f, 0.48f),
            spotIntensity = 3.2f,  dirDimFactor = 0.50f, ambientBlend = 0.35f,
            filterColor   = new Color(1.00f, 0.90f, 0.76f), exposureOffset = -0.10f,
            isStarterGift = true, price = 0f, pearlPrice =  0, displayOrder = 1
        },
        new LightPreset
        {
            id = "light_blue",   displayName = "light.blue",
            color = new Color(0.18f, 0.42f, 1.00f),
            spotIntensity = 3.5f,  dirDimFactor = 0.30f, ambientBlend = 0.45f,
            filterColor   = new Color(0.72f, 0.86f, 1.00f), exposureOffset = -0.30f,
            isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 2
        },
        new LightPreset
        {
            id = "light_deep",   displayName = "light.deep",
            color = new Color(0.06f, 0.14f, 0.52f),
            spotIntensity = 2.5f,  dirDimFactor = 0.15f, ambientBlend = 0.55f,
            filterColor   = new Color(0.55f, 0.65f, 1.00f), exposureOffset = -0.60f,
            isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 3
        },
        new LightPreset
        {
            id = "light_purple", displayName = "light.purple",
            color = new Color(0.62f, 0.12f, 1.00f),
            spotIntensity = 3.5f,  dirDimFactor = 0.30f, ambientBlend = 0.40f,
            filterColor   = new Color(0.88f, 0.72f, 1.00f), exposureOffset = -0.25f,
            isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 4
        },
        new LightPreset
        {
            id = "light_sunset", displayName = "light.sunset",
            color = new Color(1.00f, 0.48f, 0.22f),
            spotIntensity = 3.2f,  dirDimFactor = 0.45f, ambientBlend = 0.35f,
            filterColor   = new Color(1.00f, 0.82f, 0.65f), exposureOffset = -0.10f,
            isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 5
        },
        new LightPreset
        {
            id = "light_cycle",  displayName = "light.cycle",
            color = Color.white,
            spotIntensity = 3.0f,  dirDimFactor = 0.35f, ambientBlend = 0.30f,
            filterColor   = Color.white,                   exposureOffset = -0.15f,
            animated = true, isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 6
        },
    };

    // ── Estado interno ────────────────────────────────────────────────────────
    private readonly Light[] _spots = new Light[3];
    private Light    _fill;
    private Light    _dirLight;
    private float    _dirBaseIntensity;
    private Color    _ambientBase;
    private string   _currentPresetId = "light_white";
    private Coroutine _transition;

    // Post-processing
    private ColorAdjustments _colorAdj;
    private Color  _currentFilter   = Color.white;
    private float  _currentExposure = 0f;

    public string CurrentPresetId => _currentPresetId;

    // ── Init ─────────────────────────────────────────────────────────────────

    private bool _baselineCapturada;

    public void Initialize(Bounds tankBounds)
    {
        // ⚠ 2026-08-15 — destruir las luces de la pasada anterior.
        // Initialize() corre en CADA INIT (el móvil manda uno en cada OnCastConnected) y
        // creaba 3 spots + 1 fill + 1 Volume nuevos SIN destruir los viejos: 10 reconexiones
        // = 30 spots acumulados en un Mali-G31. Las demás ramas (BubbleSystem, WaterSurface,
        // DecorationPlacer, TankBackground) sí limpian; ésta era la única que no.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c.name.StartsWith("TankLight_") || c.name == "TankLightingVolume")
                Destroy(c.gameObject);
        }

        // ── DIAGNÓSTICO: listar TODAS las luces presentes en escena ──────────
        var allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        Debug.Log($"[ShadowDiag] TankLighting.Initialize() — {allLights.Length} luces en escena:");
        foreach (var l in allLights)
            Debug.Log($"[ShadowDiag]   luz: {l.gameObject.name} type={l.type} enabled={l.enabled} shadows={l.shadows} intensity={l.intensity:F2}");

        // Buscar la luz direccional de la escena
        foreach (var l in allLights)
        {
            if (l.type == LightType.Directional) { _dirLight = l; break; }
        }
        // La línea base se captura UNA vez. Antes se re-capturaba en cada INIT sobre valores
        // ya modificados por el preset anterior → el tanque se iba oscureciendo/aclarando en
        // escalera a cada reconexión.
        if (!_baselineCapturada)
        {
            _dirBaseIntensity = _dirLight != null ? _dirLight.intensity : 1.2f;
            _ambientBase      = RenderSettings.ambientLight;
            _baselineCapturada = true;
        }

        // Forzar Hard shadows
        if (_dirLight != null)
        {
            _dirLight.shadows = LightShadows.Hard;
            Debug.Log($"[TankLighting] DirLight encontrada: '{_dirLight.gameObject.name}' shadows={_dirLight.shadows} intensity={_dirLight.intensity:F2} enabled={_dirLight.enabled} shadowStrength={_dirLight.shadowStrength:F2}");
        }
        else
        {
            Debug.LogWarning("[TankLighting] ❌ No directional light found in scene! Las sombras NO funcionarán.");
        }

        // ── 3 SpotLights (barra LED) ─────────────────────────────────────────
        float localTop  = tankBounds.max.y - transform.position.y + 0.8f;
        float halfW     = tankBounds.extents.x;
        float spotRange = tankBounds.size.y * 2.2f;
        float[] xPos    = { -halfW * 0.60f, 0f, halfW * 0.60f };
        string[] names  = { "TankLight_LED_L", "TankLight_LED_C", "TankLight_LED_R" };

        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject(names[i]);
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(xPos[i], localTop, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var light = go.AddComponent<Light>();
            light.type           = LightType.Spot;
            light.spotAngle      = 118f;
            light.innerSpotAngle = 65f;
            light.range          = spotRange;
            light.shadows        = LightShadows.None;
            _spots[i] = light;
        }

        // ── AmbientFill (Point, azul tenue fijo) ─────────────────────────────
        var fillGO = new GameObject("TankLight_Fill");
        fillGO.transform.SetParent(transform);
        float fillY = (tankBounds.min.y + tankBounds.max.y) * 0.5f - transform.position.y;
        fillGO.transform.localPosition = new Vector3(0f, fillY, -tankBounds.extents.z * 0.8f);

        _fill           = fillGO.AddComponent<Light>();
        _fill.type      = LightType.Point;
        _fill.range     = tankBounds.size.x * 1.1f;
        _fill.color     = new Color(0.28f, 0.50f, 0.72f);
        _fill.intensity = 0.45f;
        _fill.shadows   = LightShadows.None;

        // ── Post-process Volume (prioridad 11 > PostProcessingSetup=10) ───────
        // Sobreescribe colorFilter y postExposure para teñir el frame completo,
        // afectando también objetos con shaders unlit (TankBackground, decos GLB).
        var ppGO = new GameObject("TankLight_PostFX");
        ppGO.transform.SetParent(transform);
        var vol = ppGO.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 11;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // ⚠⚠ `Add<T>(false)`, NO `true`. El `true` marca TODOS los parámetros del efecto como
        // sobreescritos, no sólo los que se tocan después — y como este Volume va a prioridad 11,
        // imponía también `saturation = 0` y `contrast = 0` por encima del grado que monta
        // `PostProcessingSetup` (prioridad 10). Resultado: el grado de color de la TV **no podía
        // verse jamás**, ni con URP activo ni con el post-proceso encendido.
        // Descubierto el 2026-08-21 mandando un grado deliberadamente extremo (saturación −100)
        // al player real y viendo que la imagen no cambiaba nada. Con `false`, sólo se imponen
        // los dos parámetros que este Volume declara explícitamente con `.Override(...)`, y el
        // resto cae al Volume de abajo, que es justo lo que se quería.
        _colorAdj = profile.Add<ColorAdjustments>(false);
        _colorAdj.active = true;
        _colorAdj.colorFilter.Override(Color.white);
        _colorAdj.postExposure.Override(0f);
        vol.profile = profile;

        SetPreset("light_white", animate: false);
        TvLayerDebug.Set("Lighting", "initialized light_white");
        Debug.Log("[Lighting] ✅ TankLightingController (3 spots + fill + postFX)");
    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void SetPreset(string id, bool animate = true)
    {
        var preset = FindPreset(id);
        if (preset == null)
        {
            Debug.LogWarning($"[Lighting] Preset '{id}' no encontrado.");
            return;
        }

        _currentPresetId = id;
        if (_transition != null) StopCoroutine(_transition);

        TvLayerDebug.Set("Lighting", $"preset={id} filter=({preset.Value.filterColor.r:F2},{preset.Value.filterColor.g:F2},{preset.Value.filterColor.b:F2})");
        if (_spots[0] == null) return;

        if (animate && !preset.Value.animated)
            _transition = StartCoroutine(TransitionTo(preset.Value, 0.7f));
        else if (!preset.Value.animated)
            ApplyPreset(preset.Value);

        if (preset.Value.animated)
            ApplySceneOverrides(preset.Value, Color.white);
    }

    // Llamado por AmbientModeController tras una transición día/noche.
    public void RefreshBaseValues()
    {
        _dirBaseIntensity = _dirLight != null ? _dirLight.intensity : _dirBaseIntensity;
        _ambientBase      = RenderSettings.ambientLight;
        var preset = FindPreset(_currentPresetId);
        if (preset != null && !preset.Value.animated) ApplyPreset(preset.Value);
    }

    // ── Update (solo RGB cycle) ───────────────────────────────────────────────

    void Update()
    {
        if (_spots[0] == null) return;
        var preset = FindPreset(_currentPresetId);
        if (preset == null || !preset.Value.animated) return;

        float hue = Mathf.Repeat(Time.time * 0.07f, 1f);
        Color c   = Color.HSVToRGB(hue, 0.90f, 1f);
        for (int i = 0; i < 3; i++)
            if (_spots[i] != null) _spots[i].color = c;

        // Animar también el colorFilter con el ciclo RGB (tinte sutil)
        if (_colorAdj != null)
        {
            Color tint = new Color(0.82f + c.r * 0.18f, 0.82f + c.g * 0.18f, 0.82f + c.b * 0.18f);
            _colorAdj.colorFilter.Override(tint);
        }
    }

    // ── Internos ─────────────────────────────────────────────────────────────

    private void ApplyPreset(LightPreset preset)
    {
        for (int i = 0; i < 3; i++)
        {
            if (_spots[i] == null) continue;
            _spots[i].color     = preset.color;
            _spots[i].intensity = preset.spotIntensity;
        }
        ApplySceneOverrides(preset, preset.color);
    }

    private void ApplySceneOverrides(LightPreset preset, Color spotColor)
    {
        if (_dirLight != null)
            _dirLight.intensity = _dirBaseIntensity * preset.dirDimFactor;

        RenderSettings.ambientLight = Color.Lerp(_ambientBase, spotColor * 0.55f, preset.ambientBlend);

        if (_colorAdj != null)
        {
            _colorAdj.colorFilter.Override(preset.filterColor);
            _colorAdj.postExposure.Override(preset.exposureOffset);
            _currentFilter   = preset.filterColor;
            _currentExposure = preset.exposureOffset;
        }
    }

    private IEnumerator TransitionTo(LightPreset target, float duration)
    {
        Color startSpotColor     = _spots[0] != null ? _spots[0].color     : Color.white;
        float startSpotIntensity = _spots[0] != null ? _spots[0].intensity : 1f;
        float startDirIntensity  = _dirLight != null ? _dirLight.intensity  : _dirBaseIntensity;
        Color startAmbient       = RenderSettings.ambientLight;
        Color startFilter        = _currentFilter;
        float startExposure      = _currentExposure;

        float targetDirIntensity = _dirBaseIntensity * target.dirDimFactor;
        Color targetAmbient      = Color.Lerp(_ambientBase, target.color * 0.55f, target.ambientBlend);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));

            Color c = Color.Lerp(startSpotColor, target.color, e);
            float intensity = Mathf.Lerp(startSpotIntensity, target.spotIntensity, e);
            for (int i = 0; i < 3; i++)
                if (_spots[i] != null) { _spots[i].color = c; _spots[i].intensity = intensity; }

            if (_dirLight != null)
                _dirLight.intensity = Mathf.Lerp(startDirIntensity, targetDirIntensity, e);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, targetAmbient, e);

            if (_colorAdj != null)
            {
                _colorAdj.colorFilter.Override(Color.Lerp(startFilter, target.filterColor, e));
                _colorAdj.postExposure.Override(Mathf.Lerp(startExposure, target.exposureOffset, e));
            }

            yield return null;
        }

        ApplyPreset(target);
    }

    private LightPreset? FindPreset(string id)
    {
        foreach (var p in Presets)
            if (p.id == id) return p;
        return null;
    }
}
