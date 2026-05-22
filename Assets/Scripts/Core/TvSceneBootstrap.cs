using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Entry point for the TvScene (WebGL Cast receiver build).
///
/// Responsibilities:
///   1. Disable UI — the TV display shows only the aquarium.
///   2. Wait for CastReceiver to deliver an INIT message.
///   3. Synthesize a SaveData from TvAquariumState and call InitializeFromCastState().
///   4. Apply delta UPDATE messages (ambient mode, fish speed, etc.).
///
/// The TvScene duplicates AquariumScene but with:
///   - UIManager removed (or disabled)
///   - AmbientModeController.alwaysAmbient = true (no real-time clock)
///   - This component added to AquariumManager's GameObject
/// </summary>
public class TvSceneBootstrap : MonoBehaviour
{
    public static TvSceneBootstrap Instance { get; private set; }

    [Header("TV Scene")]
    [Tooltip("If true, apply Day mode immediately and ignore real-time clock.")]
    public bool alwaysAmbient = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Force day-mode on ambient controller — no time-of-day cycling on TV
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb != null)
        {
            amb.alwaysAmbient    = alwaysAmbient;
            amb.autoFollowRealTime = false;
        }

        // Disable UI if UIManager is present (TV shows aquarium only)
        var ui = UIManager.Instance;
        if (ui != null)
            ui.gameObject.SetActive(false);

        Debug.Log("[TvSceneBootstrap] ✅ TV scene ready — waiting for Cast INIT.");
    }

    // ── Public API (called from CastReceiver) ─────────────────────────────────

    /// <summary>Full state received from the mobile sender on connect.</summary>
    public void InitializeFromState(TvAquariumState state)
    {
        if (state == null) { Debug.LogWarning("[TvScene] INIT received null state."); return; }
        Debug.Log($"[TvScene] INIT — fish:{state.activeFish?.Count ?? 0} bg:{state.bgId}");

        var mgr = AquariumManager.Instance;
        if (mgr == null) { Debug.LogError("[TvScene] AquariumManager not found."); return; }

        mgr.InitializeFromCastState(state);
    }

    /// <summary>Delta update received from the mobile sender during an active session.</summary>
    public void ApplyUpdate(TvUpdateMessage upd)
    {
        if (upd == null) return;
        Debug.Log($"[TvScene] UPDATE type={upd.type} value={upd.value}");

        var mgr = AquariumManager.Instance;
        if (mgr == null) return;

        switch (upd.type)
        {
            case "ambient":
                ApplyAmbientMode(upd.value);
                break;

            case "speed":
                if (float.TryParse(upd.value, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float spd))
                {
                    mgr.FishSpeedMultiplier = spd;
                }
                break;

            case "feed":
                // Spawn food at center-top of the tank
                var bounds = mgr.tankController.GetTankBounds();
                FoodManager.Instance?.SpawnFood(new Vector3(bounds.center.x, bounds.max.y - 0.5f, 0f));
                break;

            case "refresh":
                // Full re-sync — sender should follow up with a new INIT
                Debug.Log("[TvScene] Refresh requested — waiting for new INIT.");
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyAmbientMode(string mode)
    {
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb == null) return;
        switch (mode)
        {
            case "day":    amb.SetDay();    break;
            case "sunset": amb.SetSunset(); break;
            case "night":  amb.SetNight();  break;
        }
    }
}
