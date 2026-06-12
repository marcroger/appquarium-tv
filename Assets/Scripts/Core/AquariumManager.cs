using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TV receiver version — stripped of all mobile-only systems (IAP, Ads, Save, UI).
/// Initializes the aquarium via InitializeFromCastState() called by TvSceneBootstrap
/// when the Cast INIT message arrives. No save persistence.
/// </summary>
public class AquariumManager : MonoBehaviour
{
    public static AquariumManager Instance { get; private set; }

    [Header("Subsistemas")]
    public TankController           tankController;
    public FishSpawner              fishSpawner;
    public AquariumCameraController cameraController;

    [Header("Catálogos")]
    public List<TankData>       allTankCatalog;
    public List<FishData>       allFishCatalog;
    public List<DecorationData> allDecoCatalog;

    // Transient save — built from Cast state, never persisted
    public SaveData SaveData { get; private set; }

    public TankData ActiveTankData =>
        allTankCatalog?.Find(t => t.itemId == SaveData?.selectedTankId)
        ?? allTankCatalog?.Find(t => t.isStarterGift)
        ?? (allTankCatalog?.Count > 0 ? allTankCatalog[0] : null);

    public float FishSpeedMultiplier
    {
        get => SaveData?.fishSpeedMultiplier ?? 1f;
        set { if (SaveData != null) SaveData.fishSpeedMultiplier = Mathf.Clamp(value, 0.5f, 2f); }
    }

    private TankBackground _tankBackground;
    private float          _tankDirtyLevel;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Reduce physics rate — we don't use Unity physics for fish/decos
        Time.fixedDeltaTime = 0.05f;

        // v2 Addressables: catalogs are populated on demand when INIT arrives.
        // No pre-loading of all SOs at startup.
        allFishCatalog = new List<FishData>();
        allDecoCatalog = new List<DecorationData>();

        SaveData = new SaveData();
        Debug.Log("[AquariumManager] TV receiver ready — waiting for Cast INIT.");
    }

    void Update()
    {
        if (!AppFlags.EnableNeglectVisuals || _tankBackground == null) return;
        float avg = GetAverageHunger();
        _tankDirtyLevel = Mathf.Lerp(_tankDirtyLevel, avg, Time.deltaTime * 0.02f);
        _tankBackground.SetDirtyLevel(_tankDirtyLevel);
    }

    // ── Cast initialization ───────────────────────────────────────────────────

    /// <summary>
    /// v2 entry point: called by TvSceneBootstrap after Addressables loading completes.
    /// Receives only the assets needed for this user's tank.
    /// </summary>
    public void InitializeFromCastState(TvAquariumState state, List<FishData> fishData, List<DecorationData> decoData)
    {
        allFishCatalog = fishData ?? new List<FishData>();
        allDecoCatalog = decoData ?? new List<DecorationData>();
        Debug.Log($"[AquariumManager] Catalogs loaded — fish:{allFishCatalog.Count} decos:{allDecoCatalog.Count}");
        InitializeFromCastState(state);
    }

    /// <summary>
    /// Async variant: loads decos with yields to keep the browser event loop alive.
    /// Use from TvSceneBootstrap coroutine instead of the sync version for Cast TV.
    /// </summary>
    public System.Collections.IEnumerator InitializeFromCastStateAsync(TvAquariumState state, List<FishData> fishData, List<DecorationData> decoData)
    {
        allFishCatalog = fishData ?? new List<FishData>();
        allDecoCatalog = decoData ?? new List<DecorationData>();
        JsBridge.Log($"InitAsync: fish={allFishCatalog.Count} decos={allDecoCatalog.Count}");

        // Build transient SaveData (same as sync path)
        if (state == null) yield break;
        var castSave = new SaveData
        {
            selectedTankId      = state.selectedTankId,
            selectedBgId        = state.bgId,
            selectedSubId       = state.subId,
            lightPresetId       = state.lightId,
            fishSpeedMultiplier = state.fishSpeed
        };
        if (state.activeFish != null)
        {
            foreach (var entry in state.activeFish)
            {
                string uid = Guid.NewGuid().ToString();
                castSave.ownedFish.Add(new OwnedFishSave { uid = uid, speciesId = entry.speciesId, nickname = entry.nickname });
                castSave.activeFishUids.Add(uid);
            }
        }
        if (!string.IsNullOrEmpty(state.decoJson) && state.decoJson != "{}")
        {
            try
            {
                var wrapper = JsonUtility.FromJson<DecoPlacementList>(state.decoJson);
                if (wrapper?.items != null) castSave.decoPositions = wrapper.items;
            }
            catch (Exception e) { Debug.LogWarning($"[AquariumManager] Cast deco parse failed: {e.Message}"); }
        }
        SaveData = castSave;

        // Sync part: tank init + fish spawn (fast, < 1 frame typically)
        JsBridge.Log("InitAsync: initializing tank + fish...");
        if (ActiveTankData == null) { JsBridge.Log("ERROR: ActiveTankData null"); yield break; }
        if (cameraController != null) cameraController.worldHalfHeight = ActiveTankData.worldHalfHeight;
        Bounds tankBounds = cameraController != null
            ? cameraController.SetupAndGetBounds()
            : new Bounds(Vector3.zero, ActiveTankData.dimensions);
        try { tankController.InitializeWithBounds(tankBounds); } catch (Exception e) { JsBridge.Log($"TankCtrl ERR: {e.Message}"); }
        SpawnCastFish(tankBounds);

        yield return null; // let browser process Cast heartbeats before heavy deco work

        // Async part: deco loading (yields every 2 decos)
        var placer = tankController.GetComponent<DecorationPlacer>();
        if (placer != null)
        {
            placer.allDecorationCatalog = allDecoCatalog ?? new List<DecorationData>();
            if (castSave.decoPositions != null && castSave.decoPositions.Count > 0)
                yield return StartCoroutine(placer.LoadFromSaveAsync(castSave.decoPositions));
            else
                placer.SaveLoaded = true;
        }

        yield return null; // heartbeat gap after decos

        // Background + substrate + lighting (sync, fast)
        try
        {
            _tankBackground = tankController.GetComponent<TankBackground>();
            if (_tankBackground != null && !string.IsNullOrEmpty(castSave.selectedBgId))
                _tankBackground.SetPreset(castSave.selectedBgId, animate: false);
            if (placer != null && !string.IsNullOrEmpty(castSave.selectedSubId))
                placer.SetSubstrate(castSave.selectedSubId);
            var lighting = tankController.GetComponent<TankLightingController>();
            if (lighting != null)
                lighting.SetPreset(!string.IsNullOrEmpty(castSave.lightPresetId) ? castSave.lightPresetId : "light_white", animate: false);
        }
        catch (Exception e) { JsBridge.Log($"BG/light ERR: {e.Message}"); }

        // Ambient mode
        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb != null)
        {
            switch (state.ambientMode)
            {
                case "sunset": amb.SetSunset(); break;
                case "night":  amb.SetNight();  break;
                default:       amb.SetDay();    break;
            }
        }

        Debug.Log($"[AquariumManager] Aquarium ready (async). Fish: {fishSpawner?.ActiveFish?.Count ?? 0}");
        JsBridge.Log($"AQUARIUM READY: {fishSpawner?.ActiveFish?.Count ?? 0} fish active");
    }

    /// <summary>
    /// Called by TvSceneBootstrap when the Cast INIT message arrives.
    /// Synthesizes a transient SaveData from the mobile state and initializes the aquarium.
    /// </summary>
    public void InitializeFromCastState(TvAquariumState state)
    {
        if (state == null) return;
        Debug.Log($"[AquariumManager] InitializeFromCastState — fish:{state.activeFish?.Count ?? 0}");

        var castSave = new SaveData
        {
            selectedTankId    = state.selectedTankId,
            selectedBgId      = state.bgId,
            selectedSubId     = state.subId,
            lightPresetId     = state.lightId,
            fishSpeedMultiplier = state.fishSpeed
        };

        if (state.activeFish != null)
        {
            foreach (var entry in state.activeFish)
            {
                string uid = Guid.NewGuid().ToString();
                castSave.ownedFish.Add(new OwnedFishSave
                {
                    uid       = uid,
                    speciesId = entry.speciesId,
                    nickname  = entry.nickname
                });
                castSave.activeFishUids.Add(uid);
            }
        }

        if (!string.IsNullOrEmpty(state.decoJson) && state.decoJson != "{}")
        {
            try
            {
                var wrapper = JsonUtility.FromJson<DecoPlacementList>(state.decoJson);
                if (wrapper?.items != null) castSave.decoPositions = wrapper.items;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AquariumManager] Cast deco parse failed: {e.Message}");
            }
        }

        SaveData = castSave;
        InitializeAquarium();

        var amb = FindFirstObjectByType<AmbientModeController>();
        if (amb != null)
        {
            switch (state.ambientMode)
            {
                case "sunset": amb.SetSunset(); break;
                case "night":  amb.SetNight();  break;
                default:       amb.SetDay();    break;
            }
        }
    }

    // ── Aquarium init ─────────────────────────────────────────────────────────

    private void InitializeAquarium()
    {
        JsBridge.Log($"InitAquarium: tankId={SaveData?.selectedTankId} catalog={allTankCatalog?.Count}");
        if (ActiveTankData == null)
        {
            Debug.LogError("[AquariumManager] allTankCatalog vacío o selectedTankId no encontrado.");
            JsBridge.Log("ERROR: ActiveTankData is null! Catalog empty or tankId mismatch.");
            return;
        }
        JsBridge.Log($"Tank: {ActiveTankData.itemId} wHH={ActiveTankData.worldHalfHeight}");

        if (cameraController != null)
            cameraController.worldHalfHeight = ActiveTankData.worldHalfHeight;

        Bounds tankBounds = cameraController != null
            ? cameraController.SetupAndGetBounds()
            : new Bounds(Vector3.zero, ActiveTankData.dimensions);

        try { tankController.InitializeWithBounds(tankBounds); JsBridge.Log($"TankCtrl OK: bounds={tankBounds.size.x:F1}x{tankBounds.size.y:F1}"); }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Tank init error: {e.Message}"); JsBridge.Log($"TankCtrl ERR: {e.Message}"); }

        SpawnCastFish(tankBounds);

        try
        {
            var placer = tankController.GetComponent<DecorationPlacer>();
            if (placer != null)
            {
                placer.allDecorationCatalog = allDecoCatalog ?? new List<DecorationData>();
                if (SaveData.decoPositions != null && SaveData.decoPositions.Count > 0)
                    placer.LoadFromSave(SaveData.decoPositions);
                else
                    placer.SaveLoaded = true;
            }
        }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Deco load error: {e.Message}"); }

        try
        {
            _tankBackground = tankController.GetComponent<TankBackground>();
            if (_tankBackground != null && !string.IsNullOrEmpty(SaveData.selectedBgId))
                _tankBackground.SetPreset(SaveData.selectedBgId, animate: false);

            var subPlacer = tankController.GetComponent<DecorationPlacer>();
            if (subPlacer != null && !string.IsNullOrEmpty(SaveData.selectedSubId))
                subPlacer.SetSubstrate(SaveData.selectedSubId);

            var lighting = tankController.GetComponent<TankLightingController>();
            if (lighting != null)
            {
                if (SaveData.lightPresetId == "light_green")
                    SaveData.lightPresetId = "light_white";
                lighting.SetPreset(
                    !string.IsNullOrEmpty(SaveData.lightPresetId) ? SaveData.lightPresetId : "light_white",
                    animate: false);
            }
        }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Preset load error: {e.Message}"); }

        Debug.Log($"[AquariumManager] Aquarium ready. Fish: {fishSpawner.ActiveFish.Count}");
        JsBridge.Log($"AQUARIUM READY: {fishSpawner.ActiveFish.Count} fish active");
    }

    private void SpawnCastFish(Bounds tankBounds)
    {
        JsBridge.Log($"SpawnFish: catalog={allFishCatalog?.Count ?? 0} active={SaveData?.activeFishUids?.Count ?? 0}");
        if (allFishCatalog == null || allFishCatalog.Count == 0)
        {
            Debug.LogWarning("[AquariumManager] allFishCatalog empty.");
            JsBridge.Log("SpawnFish: catalog empty, no fish spawned.");
            return;
        }

        foreach (string uid in SaveData.activeFishUids)
        {
            var saved = SaveData.ownedFish.Find(f => f.uid == uid);
            if (saved == null) continue;

            FishData data = allFishCatalog.Find(d => d.itemId == saved.speciesId)
                            ?? allFishCatalog[0];

            var agent = fishSpawner.SpawnFish(data, tankBounds, saved);
            if (agent != null)
            {
                agent.SetNickname(saved.nickname);
                agent.SetUid(uid);
            }
        }
        FishAgent.WirePairsFromSave(SaveData);
    }

    // ── Public API (used by TvSceneBootstrap) ─────────────────────────────────

    public void FeedAll()
    {
        foreach (var fish in fishSpawner.ActiveFish) fish?.Feed();
    }

    public void StartleAll(Vector3 position)
    {
        foreach (var fish in fishSpawner.ActiveFish) fish?.Startle(position);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private float GetAverageHunger()
    {
        var fish = fishSpawner.ActiveFish;
        if (fish.Count == 0) return 0f;
        float sum = 0f; int count = 0;
        foreach (var f in fish) if (f != null) { sum += f.Needs.hunger; count++; }
        return count > 0 ? sum / count : 0f;
    }
}
