using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controla el modo ambiental del acuario: Día, Puesta de sol, Noche.
/// Transición suave con Coroutine (SmoothStep).
///
/// Setup:
///   1. Añadir a un GameObject vacío "AmbientController" en la escena.
///   2. Arrastrar el Directional Light de la escena al campo sunLight.
///   3. Llamar SetDay() / SetNight() / SetSunset() desde botones de UI.
///
/// Teclas de debug: D = Día | N = Noche | T = Sunset (Tarde)
/// </summary>
public class AmbientModeController : MonoBehaviour
{
    public enum AmbientMode { Day, Sunset, Night }

    [Header("Referencias")]
    public Light sunLight;  // Directional Light de la escena

    [Header("Modo Día")]
    public Color dayAmbient      = new Color(0.70f, 0.85f, 1.00f);
    public Color daySunColor     = new Color(1.00f, 0.95f, 0.80f);
    public float daySunIntensity = 1.2f;

    [Header("Modo Puesta de sol")]
    public Color sunsetAmbient      = new Color(0.55f, 0.30f, 0.15f);
    public Color sunsetSunColor     = new Color(1.00f, 0.45f, 0.10f);
    public float sunsetSunIntensity = 0.7f;

    [Header("Modo Noche")]
    public Color nightAmbient      = new Color(0.04f, 0.07f, 0.16f);
    public Color nightSunColor     = new Color(0.10f, 0.14f, 0.32f);
    public float nightSunIntensity = 0.12f;

    [Header("Transición")]
    [Tooltip("Duración de la transición entre modos (segundos)")]
    public float transitionDuration = 2f;

    [Header("Ciclo automático")]
    [Tooltip("Sincroniza el modo con la hora real del dispositivo")]
    public bool autoFollowRealTime = true;

    [Header("TV / Cast receiver")]
    [Tooltip("True en TvScene: fija Day mode, ignora reloj. Set by TvSceneBootstrap.")]
    public bool alwaysAmbient = false;

    [Header("Efecto del ciclo sobre decos y peces")]
    [Tooltip("Publica el color de la fase para que DecoLit y FishUnlit se apaguen de noche. " +
             "Apagarlo devuelve el comportamiento anterior: todo a pleno día las 24 h.")]
    public bool afectarDecos = true;
    [Range(0f, 1f)]
    [Tooltip("Brillo que conservan las decos en el punto más oscuro del ciclo. 0 = negras. " +
             "El cálculo puro daría ~0,03 en noche cerrada y las decos desaparecerían.")]
    public float sueloDecoNoche = 0.18f;
    [Range(0f, 1f)]
    [Tooltip("Igual para los peces, pero MÁS ALTO a propósito: son el protagonista de la " +
             "escena y con el suelo de las decos la noche se los comía. Subirlo los destaca " +
             "más sobre el fondo apagado; bajarlo hasta sueloDecoNoche los integra del todo.")]
    public float sueloPecesNoche = 0.35f;

    // Globals de shader que leen `Appquarium/DecoLit` y `Appquarium/FishUnlit`. Son DARKEN
    // (0 = sin cambio) a propósito: un global que nadie publica vale 0, así que el fallo cae
    // del lado del aspecto de siempre y no del de la escena en negro. Ver el comentario
    // largo de `DecoLit.shader`.
    private static readonly int AqDecoDarken = Shader.PropertyToID("_AqDecoDarken");
    private static readonly int AqFishDarken = Shader.PropertyToID("_AqFishDarken");

    // ── Estado ───────────────────────────────────────────────────────────────

    public AmbientMode CurrentMode { get; private set; } = AmbientMode.Day;

    /// <summary>Se dispara cuando el modo cambia. Suscribirse para reaccionar (peces, fondo, etc.).</summary>
    public static event System.Action<AmbientMode> OnModeChanged;

    private Coroutine _transition;
    private int       _lastCheckedHour = -1;
    /// <summary>Se pone a true en cuanto llega una orden explícita (UPDATE `ambient` del
    /// móvil). A partir de ahí el reloj local deja de mandar: ver el comentario de Update().</summary>
    private bool      _modoManual;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Start()
    {
        // ⚠ 2026-08-24 — ANTES: `FindFirstObjectByType<Light>()`, SIN filtrar por tipo.
        // En la escena sólo hay una luz (la direccional), pero `TankLightingController` crea
        // 3 spots y 1 point EN RUNTIME, y el orden de los `Start()` no está garantizado: si
        // los spots ya existían, el ciclo día/noche acababa atenuando un LED de la barra en
        // vez del sol. `TankLightingController:147` sí filtra por Directional; esto no.
        // Se filtra igual que él, y además se REPORTA por el canal Cast: el
        // `Debug.Log` de abajo no viaja a la tele, así que el fallo era invisible.
        if (sunLight == null)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sunLight = l; break; }
        }

        if (sunLight == null)
        {
            Debug.LogWarning("[Ambient] ❌ No se encontró ningún Light direccional. Sólo se modificará el ambient.");
            JsBridge.Log("⚠ Ambient: sin luz direccional — el ciclo sólo moverá el ambiente");
        }
        else
        {
            Debug.Log($"[ShadowDiag] AmbientMode.Start() — sunLight='{sunLight.gameObject.name}' type={sunLight.type} shadows={sunLight.shadows} enabled={sunLight.enabled}");
            JsBridge.Log($"Ambient: sol='{sunLight.gameObject.name}' tipo={sunLight.type}");
        }

        if (alwaysAmbient)
            ApplyImmediate(AmbientMode.Day);
        else if (autoFollowRealTime)
            ApplyImmediate(ModeForCurrentHour());
        else
            ApplyImmediate(AmbientMode.Day);

        Debug.Log("[Ambient] ✅ AmbientModeController arrancado");
    }

    void Update()
    {
        // ⚠ 2026-08-24 — `_modoManual` corta el reloj interno.
        // `CheckRealTimeMode` reimponía el modo en CADA cambio de hora: si el móvil pedía
        // "noche" a las 15:30, a las 16:00 la tele volvía a día ella sola y se cargaba lo
        // que el user había elegido. En TV el sender YA es el reloj (el móvil manda un
        // UPDATE `ambient` cada vez que su propio ciclo cambia de fase), así que el reloj
        // local sólo debe gobernar MIENTRAS no haya llegado ninguna orden explícita.
        if (!alwaysAmbient && autoFollowRealTime && !_modoManual)
            CheckRealTimeMode();

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.D)) SetDay();
        if (Input.GetKeyDown(KeyCode.N)) SetNight();
        if (Input.GetKeyDown(KeyCode.T)) SetSunset();
#endif
    }


    // ── API pública ──────────────────────────────────────────────────────────

    public void SetDay()    => SetMode(AmbientMode.Day);
    public void SetSunset() => SetMode(AmbientMode.Sunset);
    public void SetNight()  => SetMode(AmbientMode.Night);

    /// <summary>Cicla al siguiente modo: Día → Tarde → Noche → Día.</summary>
    public void CycleMode() => SetMode((AmbientMode)(((int)CurrentMode + 1) % 3));

    /// <summary>Orden explícita (la que llega por el UPDATE `ambient`): además de cambiar el
    /// modo, desactiva el reloj local para que no la pise al cambiar de hora.</summary>
    public void SetMode(AmbientMode mode) => SetMode(mode, manual: true);

    private void SetMode(AmbientMode mode, bool manual)
    {
        if (manual) _modoManual = true;
        if (CurrentMode == mode) return;

        CurrentMode = mode;
        Debug.Log($"[Ambient] Modo → {mode}");
        OnModeChanged?.Invoke(mode);

        if (_transition != null)
            StopCoroutine(_transition);

        _transition = StartCoroutine(TransitionTo(GetConfig(mode)));
    }

    // ── Ciclo automático ─────────────────────────────────────────────────────

    private void CheckRealTimeMode()
    {
        int hour = DateTime.Now.Hour;
        if (hour == _lastCheckedHour) return;
        _lastCheckedHour = hour;
        SetMode(ModeForCurrentHour(), manual: false);
    }

    private static AmbientMode ModeForCurrentHour()
    {
        int h = DateTime.Now.Hour;
        if (h >= 6 && h < 18)  return AmbientMode.Day;
        if (h >= 18 && h < 21) return AmbientMode.Sunset;
        return AmbientMode.Night;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (Color ambient, Color sun, float intensity) GetConfig(AmbientMode mode) => mode switch
    {
        AmbientMode.Day    => (dayAmbient,    daySunColor,    daySunIntensity),
        AmbientMode.Sunset => (sunsetAmbient, sunsetSunColor, sunsetSunIntensity),
        AmbientMode.Night  => (nightAmbient,  nightSunColor,  nightSunIntensity),
        _                  => (dayAmbient,    daySunColor,    daySunIntensity)
    };

    private void ApplyImmediate(AmbientMode mode)
    {
        var (ambient, sun, intensity) = GetConfig(mode);
        RenderSettings.ambientLight = ambient;
        PublicarLuzDecos(ambient, sun, intensity);
        if (sunLight == null) return;
        sunLight.color     = sun;
        sunLight.intensity = intensity;
    }

    /// <summary>
    /// Traduce la iluminación de la fase a un factor por canal RELATIVO AL DÍA y lo publica
    /// como global de shader para `Appquarium/DecoLit`.
    ///
    /// Se normaliza contra el día a propósito, en vez de mandar el ambiente en bruto: así el
    /// día sale EXACTAMENTE en (1,1,1) y la imagen diurna —que es la que está validada en la
    /// tele desde agosto— no se mueve ni un píxel. Lo que cambia es sólo lo que debe cambiar.
    ///
    /// El suelo del rango existe porque el cálculo puro da ~0,03 en noche cerrada: fiel a la
    /// física, pero deja las decos invisibles. Con 0,18 la noche se lee como noche y las
    /// siluetas siguen ahí para que la bioluminiscencia tenga sobre qué destacar.
    /// </summary>
    private void PublicarLuzDecos(Color ambient, Color sun, float intensity)
    {
        if (!afectarDecos)
        {
            Shader.SetGlobalColor(AqDecoDarken, Color.clear);
            Shader.SetGlobalColor(AqFishDarken, Color.clear);
            return;
        }

        Color refDia = dayAmbient + daySunColor * daySunIntensity;
        Color actual = ambient    + sun         * intensity;

        // Se manda el complemento: el shader hace 1 - esto. Ver arriba por qué.
        Shader.SetGlobalColor(AqDecoDarken, Complemento(actual, refDia, sueloDecoNoche));
        Shader.SetGlobalColor(AqFishDarken, Complemento(actual, refDia, sueloPecesNoche));
    }

    private static Color Complemento(Color actual, Color refDia, float suelo)
    {
        float F(float act, float dia) =>
            Mathf.Lerp(suelo, 1f, Mathf.Clamp01(act / Mathf.Max(dia, 1e-4f)));

        return new Color(1f - F(actual.r, refDia.r),
                         1f - F(actual.g, refDia.g),
                         1f - F(actual.b, refDia.b), 0f);
    }

    private IEnumerator TransitionTo((Color ambient, Color sun, float intensity) target)
    {
        Color startAmbient   = RenderSettings.ambientLight;
        Color startSun       = sunLight != null ? sunLight.color     : target.sun;
        float startIntensity = sunLight != null ? sunLight.intensity : target.intensity;

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            float t      = elapsed / transitionDuration;
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            Color ambNow = Color.Lerp(startAmbient, target.ambient, smooth);
            Color sunNow = Color.Lerp(startSun,     target.sun,     smooth);
            float intNow = Mathf.Lerp(startIntensity, target.intensity, smooth);

            RenderSettings.ambientLight = ambNow;
            // Cada frame, para que las decos hagan el mismo fundido que el resto y no
            // salten de golpe al final de la transición.
            PublicarLuzDecos(ambNow, sunNow, intNow);

            if (sunLight != null)
            {
                sunLight.color     = sunNow;
                sunLight.intensity = intNow;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Valor final exacto
        RenderSettings.ambientLight = target.ambient;
        PublicarLuzDecos(target.ambient, target.sun, target.intensity);
        if (sunLight != null)
        {
            sunLight.color     = target.sun;
            sunLight.intensity = target.intensity;
        }
    }
}
