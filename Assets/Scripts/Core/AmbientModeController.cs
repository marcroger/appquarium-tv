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

    // ── Estado ───────────────────────────────────────────────────────────────

    public AmbientMode CurrentMode { get; private set; } = AmbientMode.Day;

    /// <summary>Se dispara cuando el modo cambia. Suscribirse para reaccionar (peces, fondo, etc.).</summary>
    public static event System.Action<AmbientMode> OnModeChanged;

    private Coroutine _transition;
    private int       _lastCheckedHour = -1;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Start()
    {
        if (sunLight == null)
            sunLight = FindFirstObjectByType<Light>();

        if (sunLight == null)
            Debug.LogWarning("[Ambient] ❌ No se encontró ningún Light. Sólo se modificará el ambient.");
        else
            Debug.Log($"[ShadowDiag] AmbientMode.Start() — sunLight='{sunLight.gameObject.name}' type={sunLight.type} shadows={sunLight.shadows} enabled={sunLight.enabled}");

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
        if (!alwaysAmbient && autoFollowRealTime)
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

    public void SetMode(AmbientMode mode)
    {
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
        SetMode(ModeForCurrentHour());
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
        if (sunLight == null) return;
        sunLight.color     = sun;
        sunLight.intensity = intensity;
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

            RenderSettings.ambientLight = Color.Lerp(startAmbient, target.ambient, smooth);

            if (sunLight != null)
            {
                sunLight.color     = Color.Lerp(startSun, target.sun, smooth);
                sunLight.intensity = Mathf.Lerp(startIntensity, target.intensity, smooth);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Valor final exacto
        RenderSettings.ambientLight = target.ambient;
        if (sunLight != null)
        {
            sunLight.color     = target.sun;
            sunLight.intensity = target.intensity;
        }
    }
}
