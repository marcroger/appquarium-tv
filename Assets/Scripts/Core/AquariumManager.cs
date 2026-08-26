using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TV receiver version — stripped of all mobile-only systems (IAP, Ads, Save, UI).
/// Initializes the aquarium via InitializeFromCastStateAsync() called by TvSceneBootstrap
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
    // ⚠⚠ 2026-08-26 — Aqui vivian DOS metodos mas: `InitializeFromCastState(state, fish, decos)`
    // y `InitializeFromCastState(state)`, una copia SINCRONA de toda la logica de abajo.
    // No los llamaba nadie (sólo se llamaban entre ellos) y llevaban meses divergiendo en
    // silencio: el 26-ago, al adoptar el uid del movil, habia que tocar la logica en DOS
    // sitios y olvidarse de uno no daba ningun error, sólo un comportamiento distinto segun
    // la ruta. Borrados. Si algun dia hace falta una ruta sincrona, que llame a la async y
    // la agote, no que la duplique.


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
            // El móvil protege el <= 0 en su SaveSystem; la TV recibe el número crudo por
            // Cast y no lo comprobaba: un fishSpeed 0 (o un JSON sin el campo) dejaba TODOS
            // los peces clavados en el sitio, sin ningún error.
            fishSpeedMultiplier = state.fishSpeed <= 0f ? 1f : Mathf.Clamp(state.fishSpeed, 0.25f, 3f)
        };
        int uidsGenerados = 0;
        if (state.activeFish != null)
        {
            foreach (var entry in state.activeFish)
            {
                // ⚠ 2026-08-26 — Se ADOPTA el uid del movil. Antes se generaba uno aqui, y por eso
                // `activePairs` no podia funcionar: referencia los uid del movil, que en la TV no
                // existian. Un cliente viejo no manda uid -> se genera, y ese pez no se empareja.
                bool sinUid = string.IsNullOrEmpty(entry.uid);
                if (sinUid) uidsGenerados++;
                string uid = sinUid ? Guid.NewGuid().ToString() : entry.uid;
                castSave.ownedFish.Add(new OwnedFishSave { uid = uid, speciesId = entry.speciesId, nickname = entry.nickname, ageScale = entry.ageScale });
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
        // Las parejas viajan por uid del movil, asi que sin uid adoptado no sirven de nada.
        if (state.activePairs != null) castSave.activePairs = state.activePairs;

        // Se reporta por el canal Cast, que es lo unico que se ve desde fuera: si el movil es
        // viejo, `uid propios` sale distinto de 0 y ahi esta la explicacion de por que las
        // parejas no salen, sin tener que adivinar.
        JsBridge.Log($"peces: {castSave.activeFishUids.Count} (uid propios: {uidsGenerados})"
                   + $" | parejas recibidas: {castSave.activePairs?.Count ?? 0}");

        SaveData = castSave;

        // Sync part: tank init + fish spawn (fast, < 1 frame typically)
        JsBridge.Log("InitAsync: initializing tank + fish...");
        if (ActiveTankData == null) { JsBridge.Log("ERROR: ActiveTankData null"); yield break; }
        if (cameraController != null) cameraController.worldHalfHeight = ActiveTankData.worldHalfHeight;
        Bounds tankBounds = cameraController != null
            ? cameraController.SetupAndGetBounds()
            : new Bounds(Vector3.zero, ActiveTankData.dimensions);
        try { tankController.InitializeWithBounds(tankBounds); } catch (Exception e) { JsBridge.Log($"TankCtrl ERR: {e.Message}"); }

        // Clean up previous state before reinitializing (e.g. Cast reconnect sends a 2nd INIT).
        fishSpawner?.DespawnAll();
        tankController.GetComponent<DecorationPlacer>()?.RemoveAllDecos();

        SpawnCastFish(tankBounds);

        yield return null; // let browser process Cast heartbeats before heavy deco work

        // Async part: deco loading (yields every 2 decos)
        var placer = tankController.GetComponent<DecorationPlacer>();
        if (placer != null)
        {
            placer.allDecorationCatalog = allDecoCatalog ?? new List<DecorationData>();
            placer.MobileTankHalfWidth  = state.tankHalfWidth; // 0 if old client → no remap
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

        // Start auto-feed (4-min cycle, proporcional al número de peces)
        FoodManager.Instance.StartAutoFeed(tankBounds);

        Debug.Log($"[AquariumManager] Aquarium ready (async). Fish: {fishSpawner?.ActiveFish?.Count ?? 0}");
        JsBridge.Log($"AQUARIUM READY: {fishSpawner?.ActiveFish?.Count ?? 0} fish active"
                   + $" | shaders reapuntados al player: {DecorationPlacer.ShadersReapuntados}");
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
        JsBridge.Log($"AQUARIUM READY: {fishSpawner.ActiveFish.Count} fish active"
                   + $" | shaders reapuntados al player: {DecorationPlacer.ShadersReapuntados}");
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

    /// <summary>
    /// Spawna comida visual en el tanque. FishBrain detecta los FoodItems
    /// de forma natural via CheckForFood() → GetNearestFood() → TriggerFeed().
    /// </summary>
    public void FeedAll()
    {
        var fish = fishSpawner?.ActiveFish;
        if (fish == null || fish.Count == 0) return;
        var bounds = tankController.GetTankBounds();
        int count  = Mathf.Clamp(fish.Count, 2, 5);
        for (int i = 0; i < count; i++)
        {
            float x = UnityEngine.Random.Range(bounds.min.x + 0.5f, bounds.max.x - 0.5f);
            float z = UnityEngine.Random.Range(bounds.min.z + 0.3f, bounds.max.z - 0.3f);
            FoodManager.Instance.SpawnFood(new Vector3(x, bounds.max.y - 0.2f, z));
        }
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
