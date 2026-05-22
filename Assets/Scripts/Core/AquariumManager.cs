using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Punto de entrada del sistema. Inicializa el acuario según la configuración del usuario.
/// En producción, la configuración vendrá de un servidor; ahora está hardcodeada para testing.
/// </summary>
public class AquariumManager : MonoBehaviour
{
    // Singleton: un solo AquariumManager por escena
    public static AquariumManager Instance { get; private set; }

    [Header("Subsistemas (arrastrar desde el Inspector)")]
    public TankController           tankController;
    public FishSpawner              fishSpawner;
    public AquariumCameraController cameraController;

    [Header("Configuración")]
    public List<TankData>      allTankCatalog;       // Los 5 tanques del juego
    public List<FishData>      allFishCatalog;       // Todos los FishData del juego
    public List<DecorationData> allDecoCatalog;      // Todas las DecorationData del juego

    /// <summary>TankData del tanque activo según SaveData.selectedTankId.</summary>
    public TankData ActiveTankData =>
        allTankCatalog?.Find(t => t.itemId == SaveData?.selectedTankId)
        ?? allTankCatalog?.Find(t => t.isStarterGift)
        ?? (allTankCatalog?.Count > 0 ? allTankCatalog[0] : null);

    /// <summary>Capacidad máxima de peces del tanque activo.</summary>
    public int MaxFishInTank => ActiveTankData?.maxFishCapacity ?? 3;

    [Header("─── Debug (NUNCA subir con esto activo) ───")]
    public bool debugMode        = false;    // ☑ Master: desbloquea IAPs + muestra UI de debug — #if UNITY_EDITOR, no-op en builds
    public bool resetSaveOnStart = false;    // ☑ Borra save al arrancar

    // Save activo
    public SaveData SaveData { get; private set; }

    /// <summary>
    /// Multiplicador global de velocidad de peces. 0.5 = lento, 1.0 = normal, 2.0 = rápido.
    /// Se persiste en SaveData y se aplica en FishAgent.GetCurrentMaxSpeed().
    /// </summary>
    public float FishSpeedMultiplier
    {
        get => SaveData?.fishSpeedMultiplier ?? 1f;
        set
        {
            if (SaveData == null) return;
            SaveData.fishSpeedMultiplier = Mathf.Clamp(value, 0.5f, 2f);
            SaveSystem.Save(SaveData);
        }
    }

    // Dirty tank
    private TankBackground _tankBackground;
    private float          _tankDirtyLevel;

    // Session time tracking
    private DateTime _sessionStart;

    // Daily reward pendiente de mostrar (se evalúa en Start, se muestra en StartupRoutine)
    private DailyRewardManager.RewardResult? _pendingDailyReward;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    void Awake()
    {
        // Patrón singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Mantener pantalla activa — el acuario es mobiliario digital, no debe apagarse
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // ── Batería: reducir frecuencia de física a 20Hz ───────────────────────
        // Unity default: 0.02s = 50Hz. No usamos Unity Physics para gameplay
        // (peces y decos usan transforms directos). 20Hz ahorra ~15% de CPU.
        Time.fixedDeltaTime = 0.05f; // 20Hz

        // ── Baja memoria: guardar estado inmediatamente ───────────────────────
        // En Android low-end el sistema puede matar la app sin aviso.
        Application.lowMemory += OnLowMemory;

        if (resetSaveOnStart) SaveSystem.DeleteSave();

        // Cargar catálogos desde JSON si no se asignaron manualmente en el Inspector
        CatalogLoader.LoadAll(out var freshFish, out var freshDecos);
        if (allFishCatalog == null || allFishCatalog.Count == 0) allFishCatalog = freshFish;
        if (allDecoCatalog  == null || allDecoCatalog.Count  == 0) allDecoCatalog  = freshDecos;

        // Sync runtime-only fields from JSON onto Inspector-assigned SOs
        foreach (var fresh in freshFish)
        {
            var so = allFishCatalog.Find(f => f != null && f.itemId == fresh.itemId);
            if (so == null) continue;
            if (fresh.pearlPrice > 0)       so.pearlPrice        = fresh.pearlPrice;
            if (fresh.schoolingStrength > 0) so.schoolingStrength = fresh.schoolingStrength;
            if (fresh.wanderJitter   > 0f)  so.wanderJitter      = fresh.wanderJitter;
            if (fresh.wanderRadius   > 0f)  so.wanderRadius      = fresh.wanderRadius;
            if (fresh.wanderDistance > 0f)  so.wanderDistance    = fresh.wanderDistance;
        }
        foreach (var fresh in freshDecos)
        {
            var so = allDecoCatalog.Find(d => d != null && d.itemId == fresh.itemId);
            if (so != null && fresh.pearlPrice > 0) so.pearlPrice = fresh.pearlPrice;
        }

        AdService.Initialize();
        SaveData = SaveSystem.Load();
#if UNITY_EDITOR
        if (debugMode) DebugUnlockEverything();
#endif
        _pendingDailyReward = DailyRewardManager.CheckAndGrant(SaveData);
        _sessionStart = DateTime.Now;
        ApplyOfflineProgression();
        BreedingManager.Instance?.TickLifecycles(SaveData);
        BreedingManager.Instance?.CheckBreedingPairs(SaveData);
        AnalyticsService.AppOpen(SaveData.isPremium, ReviewPromptManager.DaysSinceInstall(SaveData));

        // Inicializar el acuario de forma síncrona para que el fondo 3D sea visible
        // desde el primer frame — antes de que UIManager construya el canvas.
        // TankBackground.InitializeBackground() crea el mesh síncronamente y ajusta
        // cam.backgroundColor = colorBottom para que no haya flash de color.
        InitializeAquarium();
        NotificationService.Initialize(this);
        NotificationService.CancelAll();
        FoodManager.Instance?.StartAutoFeed(tankController.GetTankBounds());

        StartCoroutine(StartupRoutine());
    }

    /// <summary>
    /// Secuencia de arranque (el acuario ya está inicializado síncronamente en Start):
    ///   1. Esperar UIManager listo
    ///   2. Verificar y descargar Asset Packs necesarios (PAD)
    ///   3. Descargar packs background Premium
    ///   4. Flujo GDPR + onboarding + anuncio
    /// </summary>
    private IEnumerator StartupRoutine()
    {
        // 1. Esperar UIManager
        yield return new WaitUntil(() => UIManager.Instance != null && UIManager.Instance.IsReady);

        // 2. Verificar Asset Packs — en Android descarga los que faltan
        UIManager.DownloadOverlayController dlOverlay = null;
        bool downloadNeeded = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Comprobación rápida antes de mostrar overlay (evitar parpadeo si todo está listo)
        var neededCheck = AssetPackManager.BuildRequiredPackListPublic(SaveData);
        downloadNeeded  = neededCheck.Any(p => !AssetPackManager.IsPackReady(p));
#endif

        if (downloadNeeded)
            dlOverlay = UIManager.Instance.ShowDownloadOverlay();

        yield return StartCoroutine(AssetPackManager.CheckOnLaunch(
            SaveData,
            onProgress:   (packName, t) => dlOverlay?.SetProgress(t, null),
            onStatusText: text          => dlOverlay?.SetProgress(0f, text),
            onComplete:   () => {
                AudioManager.Instance?.ReloadAudio();
                UIManager.Instance?.RefreshShopBadge(SaveData);
                UIManager.Instance?.RefreshOvularioBadge(SaveData);
            }
        ));

        if (dlOverlay != null)
            yield return StartCoroutine(dlOverlay.CloseAfterDelay(0.4f));

        // 2b. Respawnear peces y decos que spawnearon como placeholder si el pack ya está listo
        fishSpawner?.RespawnPlaceholders();
        tankController?.GetComponent<DecorationPlacer>()?.RespawnPlaceholders();

        // 3. Descargar en background los packs Premium que no están listos aún
        //    (no bloqueante — el acuario ya está funcionando)
        var bgPacks = AssetPackManager.BuildBackgroundPackList(SaveData);
        if (bgPacks.Count > 0)
        {
            Debug.Log($"[AquariumManager] Iniciando descarga background de {bgPacks.Count} packs Premium: {string.Join(", ", bgPacks)}");
            StartCoroutine(AssetPackManager.DownloadBackgroundPacks(bgPacks));
        }

        // 4. GDPR + onboarding + anuncio
        yield return StartCoroutine(ShowStartupFlowRoutine());
    }

    private IEnumerator ShowStartupFlowRoutine()
    {

        // Paso 1: GDPR — en Android lo gestiona el UMP SDK (AdService.Initialize).
        // En otras plataformas usamos el diálogo custom.
#if !UNITY_ANDROID || UNITY_EDITOR
        if (!SaveData.gdprConsentShown)
        {
            yield return StartCoroutine(UIManager.Instance.ShowGDPRConsentCoroutine(accepted =>
            {
                SaveData.gdprConsentShown    = true;
                SaveData.gdprConsentAccepted = accepted;
                SaveSystem.Save(SaveData);
            }));
        }
#endif

        // Paso 2: onboarding — primer arranque
        if (!SaveData.onboardingDone && SaveData.ownedFish.Count > 0)
        {
            bool onboardingComplete = false;
            var firstFish = SaveData.ownedFish[0];
            UIManager.Instance.RunOnboarding(
                firstFish: firstFish,
                onRename:  name => RenameFish(firstFish.uid, name),
                onComplete: () =>
                {
                    SaveData.onboardingDone = true;
                    SaveSystem.Save(SaveData);
                    AnalyticsService.OnboardingComplete(firstFish.nickname);
                    onboardingComplete = true;
                }
            );
            yield return new WaitUntil(() => onboardingComplete);
        }

        // Paso 3: Senior goodbye — mostrar popup por cada pez bred listo para liberar
        if (AppFlags.EnableBreeding)
        {
            var goodbyeFish = SaveData.ownedFish
                .Where(f => {
                    var fd = allFishCatalog?.Find(d => d.itemId == f.speciesId);
                    return BreedingManager.IsReadyForGoodbye(f, fd);
                })
                .ToList();

            foreach (var fish in goodbyeFish)
            {
                var fishData = allFishCatalog?.Find(d => d.itemId == fish.speciesId);
                bool responded = false;
                UIManager.Instance.ShowSeniorGoodbyeModal(fish, fishData,
                    onLiberate: () =>
                    {
                        BreedingManager.Instance?.LiberateFish(fish.uid);
                        responded = true;
                    },
                    onLater: () => { responded = true; });
                yield return new WaitUntil(() => responded);
            }
        }

        // Paso N: Popup de recompensa diaria — tras onboarding y goodbye, antes de ambient
        if (_pendingDailyReward.HasValue && AppFlags.EnableBreeding)
        {
            UIManager.Instance?.ShowDailyRewardPopup(
                _pendingDailyReward.Value.pearls,
                _pendingDailyReward.Value.streakDay);
            UIManager.Instance?.RefreshPearlCounter();
            SaveSystem.Save(SaveData);
        }

        // Siempre arrancar en modo ambiente tras el flujo de inicio (no solo en kiosk).
        // Los overlays de ad y onboarding son UI fullscreen — no se ven afectados por el modo ambiente.
        UIManager.Instance?.SetAmbientMode(true);

        // Mostrar el hint "mantén pulsado" sólo ahora, una vez completado el flujo
        // de inicio. Si el usuario ya lo descartó antes, no se vuelve a ver.
        UIManager.Instance?.ShowAmbientHintIfNeeded();
    }

    void Update()
    {
        if (!AppFlags.EnableNeglectVisuals || _tankBackground == null) return;

        float avg = GetAverageHunger();
        // Lerp muy lento: la suciedad es acumulación, no reacción inmediata
        _tankDirtyLevel = Mathf.Lerp(_tankDirtyLevel, avg, Time.deltaTime * 0.02f);
        _tankBackground.SetDirtyLevel(_tankDirtyLevel);
    }

    void OnLowMemory()
    {
        // El sistema avisa de presión de memoria — guardar inmediatamente antes de que nos maten
        Debug.LogWarning("[AquariumManager] lowMemory received — saving immediately.");
        SyncNeedsToSave();
        SyncDecoToSave();
        SaveSystem.Save(SaveData);
    }

    void OnApplicationQuit()
    {
        AccumulateSessionTime();
        SyncNeedsToSave();
        SyncDecoToSave();
        SaveSystem.Save(SaveData);
        NotificationService.ScheduleAll(SaveData);
    }

    void OnApplicationPause(bool pause)
    {
        // Restaurar timeout del sistema al ir a background; retomar al volver
        Screen.sleepTimeout = pause ? SleepTimeout.SystemSetting : SleepTimeout.NeverSleep;

        if (pause)
        {
            // ── Batería: throttle al ir a background ──────────────────────────
            // Algunos dispositivos Android siguen corriendo Unity en background.
            // 5fps evita burn innecesario si eso ocurre.
            Application.targetFrameRate = 5;

            AccumulateSessionTime();
            SyncNeedsToSave();
            SyncDecoToSave();
            SaveSystem.Save(SaveData);
            NotificationService.ScheduleAll(SaveData);
        }
        else
        {
            // Restaurar fps al volver a primer plano (ambient=24, UI=30)
            Application.targetFrameRate = UIManager.Instance != null && UIManager.Instance.IsAmbientMode ? 24 : 30;

            _sessionStart = DateTime.Now;
            NotificationService.CancelAll();
        }
    }

    /// <summary>Persiste las posiciones actuales de las decoraciones al save.</summary>
    private void SyncDecoToSave()
    {
        if (tankController == null || SaveData == null) return;
        var placer = tankController.GetComponent<DecorationPlacer>();
        if (placer == null) return;

        // Guardia anti-corrupción: si la escena nunca llegó a tener decos (SaveLoaded=false),
        // no sobreescribimos un save válido con lista vacía. Esto protege contra el caso
        // en que LoadFromSave falla (itemId no encontrado en catálogo) y _placed queda vacío.
        var current = placer.GetCurrentPlacements();
        if (!placer.SaveLoaded && current.Count == 0 && SaveData.decoPositions?.Count > 0)
        {
            Debug.LogWarning("[AquariumManager] SyncDecoToSave omitido: placer no inicializado pero el save tiene decos. Datos conservados.");
            return;
        }

        SaveData.decoPositions = current;
    }

    private void AccumulateSessionTime()
    {
        if (SaveData == null) return;
        float sessionDays = (float)((DateTime.Now - _sessionStart).TotalSeconds / 86400.0);
        SaveData.profile.totalDaysPlayed += sessionDays;
        _sessionStart = DateTime.Now; // reset para no doble-contar si se llama varias veces
    }

    /// <summary>
    /// Copia el estado actual de NeedsModule de cada pez activo a su OwnedFishSave.
    /// Llamar antes de cualquier SaveSystem.Save() para que hunger/stress/energy persistan.
    /// </summary>
    private void SyncNeedsToSave()
    {
        if (fishSpawner == null || SaveData == null) return;
        foreach (var agent in fishSpawner.ActiveFish)
        {
            if (agent == null || agent.Needs == null) continue;
            var saved = SaveData.ownedFish.Find(f => f.uid == agent.Uid);
            if (saved == null) continue;
            saved.hunger = agent.Needs.hunger;
            saved.stress = agent.Needs.stress;
            saved.energy = agent.Needs.energy;
        }
    }

    // ── Progresión offline ───────────────────────────────────────────────────

    /// <summary>
    /// Calcula el tiempo transcurrido desde el último guardado y avanza las
    /// necesidades de los peces en consecuencia (hambre + edad).
    /// Llamar después de Load() y ANTES de SpawnSavedFish() para que los peces
    /// se instancien ya con los valores actualizados.
    /// </summary>
    private void ApplyOfflineProgression()
    {
        if (string.IsNullOrEmpty(SaveData.lastSaveDate)) return;
        if (!DateTime.TryParse(SaveData.lastSaveDate, out var lastSave)) return;

        double elapsedSeconds = (DateTime.Now - lastSave).TotalSeconds;

        // Ignorar si la diferencia es menor de 5s (reinicio inmediato, editor, etc.)
        if (elapsedSeconds < 5.0) return;

        // Cap: máximo 48h para no castigar a usuarios que no entran varios días
        elapsedSeconds = Math.Min(elapsedSeconds, 48.0 * 3600.0);

        float elapsedDays = (float)(elapsedSeconds / 86400.0);

        foreach (var fish in SaveData.ownedFish)
        {
            // La edad avanza siempre, estén en el tanque o en la colección
            fish.ageInDays += elapsedDays;

            // El hambre solo avanza si el pez estaba activo en el tanque
            if (!SaveData.activeFishUids.Contains(fish.uid)) continue;

            var fishData = allFishCatalog?.Find(d => d.itemId == fish.speciesId);
            float rate   = fishData != null ? fishData.hungerRate : 0.0008f;
            fish.hunger  = Mathf.Clamp01(fish.hunger + rate * (float)elapsedSeconds);
        }

        Debug.Log($"[AquariumManager] Offline: {elapsedSeconds:F0}s ({elapsedDays:F2}d) aplicados a {SaveData.ownedFish.Count} peces.");
    }

    // ── Cambio de tanque en caliente ─────────────────────────────────────────

    /// <summary>
    /// Aplica el tanque activo (ya guardado en SaveData.selectedTankId) sin reiniciar la app.
    /// Actualiza cámara, paredes, suelo y clampea peces y decos a los nuevos bounds.
    /// Llamado desde ShopPanel tras confirmar el cambio de tanque.
    /// </summary>
    public void ApplyTankSwitch()
    {
        if (ActiveTankData == null) return;

        // 0. Guardar bounds ANTES de cambiar (para escalar decos proporcionalmente)
        Bounds oldBounds = tankController != null
            ? tankController.GetTankBounds()
            : new Bounds(Vector3.zero, Vector3.one * 10f);

        // 1. Cámara
        if (cameraController != null)
            cameraController.worldHalfHeight = ActiveTankData.worldHalfHeight;

        Bounds newBounds = cameraController != null
            ? cameraController.SetupAndGetBounds()
            : new Bounds(Vector3.zero, ActiveTankData.dimensions);

        // 2. Tanque (paredes, suelo, fondo)
        tankController.InitializeWithBounds(newBounds);

        // 3. Reescalar decos proporcionalmente + clamp de seguridad
        var placer = tankController.GetComponent<DecorationPlacer>();
        if (placer != null)
            placer.RescaleAndClampToNewBounds(oldBounds);

        // 4. Reescalar posición de peces + actualizar bounds de steering
        float scaleX = oldBounds.size.x > 0.01f ? newBounds.size.x / oldBounds.size.x : 1f;
        float scaleY = oldBounds.size.y > 0.01f ? newBounds.size.y / oldBounds.size.y : 1f;
        float pad = 0.5f;
        foreach (var fish in fishSpawner.ActiveFish)
        {
            if (fish == null) continue;
            var p = fish.transform.position;
            p.x = Mathf.Clamp(p.x * scaleX, newBounds.min.x + pad, newBounds.max.x - pad);
            p.y = Mathf.Clamp(p.y * scaleY, newBounds.min.y + pad, newBounds.max.y - pad);
            fish.transform.position = p;
            // Actualizar bounds internos del steering — sin esto los peces ignoran el nuevo tanque
            fish.GetComponent<SteeringController>()?.SetTankBounds(newBounds);
        }

        Debug.Log($"[AquariumManager] Tank switch → {ActiveTankData.itemId} (worldHalfHeight={ActiveTankData.worldHalfHeight}, scaleX={scaleX:F2})");
    }

    // ── Inicialización ───────────────────────────────────────────────────────

    private void InitializeAquarium()
    {
        // ── DIAGNÓSTICO SOMBRAS — log completo de entorno gráfico ─────────────
        Debug.Log($"[ShadowDiag] === INICIO DIAGNÓSTICO SOMBRAS ===");
        Debug.Log($"[ShadowDiag] Graphics API: {UnityEngine.SystemInfo.graphicsDeviceType} | Device: {UnityEngine.SystemInfo.graphicsDeviceName}");
        Debug.Log($"[ShadowDiag] GPU Vendor: {UnityEngine.SystemInfo.graphicsDeviceVendor}");
        Debug.Log($"[ShadowDiag] Supports shadows: {UnityEngine.SystemInfo.supportsShadows}");
        Debug.Log($"[ShadowDiag] Max shadow distance (QualitySettings): {UnityEngine.QualitySettings.shadowDistance}");
        Debug.Log($"[ShadowDiag] Shadow cascades: {UnityEngine.QualitySettings.shadowCascades}");
        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        Debug.Log($"[ShadowDiag] URP Asset: {(urpAsset != null ? urpAsset.name : "NULL — no URP!")}");
        // ─────────────────────────────────────────────────────────────────────

        if (ActiveTankData == null)
        {
            Debug.LogError("[AquariumManager] allTankCatalog vacío o selectedTankId no encontrado.");
            return;
        }

        // 1. Cámara → define el espacio del mundo
        // worldHalfHeight del tanque activo controla el zoom: menor = peces más grandes.
        if (cameraController != null)
            cameraController.worldHalfHeight = ActiveTankData.worldHalfHeight;

        Bounds tankBounds = cameraController != null
            ? cameraController.SetupAndGetBounds()
            : new Bounds(Vector3.zero, ActiveTankData.dimensions);

        // 2. Montar el tanque
        try { tankController.InitializeWithBounds(tankBounds); }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Error inicializando tanque: {e.Message}"); }

        // 3. Instanciar los peces activos del save
        SpawnSavedFish(tankBounds);

        // 4. Colocar decoraciones guardadas
        try
        {
            var placer = tankController.GetComponent<DecorationPlacer>();
            if (placer != null)
            {
                placer.allDecorationCatalog = allDecoCatalog ?? new List<DecorationData>();
                if (SaveData.decoPositions != null && SaveData.decoPositions.Count > 0)
                    placer.LoadFromSave(SaveData.decoPositions);
                else if (SaveData.activeDecoIds != null && SaveData.activeDecoIds.Count > 0)
                    placer.LoadFromSaveLegacy(SaveData.activeDecoIds);
                else
                    placer.SaveLoaded = true;
            }
        }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Error cargando decos: {e.Message}"); }

        // 5. Aplicar presets de entorno guardados
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
                // Migrar preset eliminado (light_green → light_white)
                if (SaveData.lightPresetId == "light_green")
                    SaveData.lightPresetId = "light_white";
                lighting.SetPreset(
                    !string.IsNullOrEmpty(SaveData.lightPresetId) ? SaveData.lightPresetId : "light_white",
                    animate: false);
            }
        }
        catch (Exception e) { Debug.LogError($"[AquariumManager] Error aplicando presets: {e.Message}"); }

        Debug.Log($"[AquariumManager] Acuario listo. Peces: {fishSpawner.ActiveFish.Count} | Premium: {SaveData.isPremium}");
        ReviewPromptManager.CheckAndShow(SaveData);
    }

    private void SpawnSavedFish(Bounds tankBounds)
    {
        if (allFishCatalog == null || allFishCatalog.Count == 0)
        {
            Debug.LogWarning("[AquariumManager] allFishCatalog vacío — asigna los FishData en el Inspector.");
            return;
        }

        // Primer arranque: no hay peces en colección → dar los peces iniciales (sin activar en tanque)
        if (SaveData.ownedFish.Count == 0)
        {
            SaveSystem.AddStartingFish(SaveData, allFishCatalog);
            Debug.Log("[AquariumManager] Primer arranque — peces iniciales añadidos a la colección.");
        }

        foreach (string uid in SaveData.activeFishUids)
        {
            var saved = SaveData.ownedFish.Find(f => f.uid == uid);
            if (saved == null) continue;

            FishData data = allFishCatalog.Find(d => d.itemId == saved.speciesId);
            if (data == null)
            {
                data = allFishCatalog[0];
                Debug.LogWarning($"[AquariumManager] Especie '{saved.speciesId}' no encontrada, usando fallback.");
            }

            var agent = fishSpawner.SpawnFish(data, tankBounds, saved);
            if (agent != null)
            {
                agent.SetNickname(saved.nickname);
                agent.SetUid(uid);
                // Restaurar estado de necesidades guardado
                agent.Needs.hunger = saved.hunger;
                agent.Needs.stress = saved.stress;
                agent.Needs.energy = saved.energy;
            }
        }
        FishAgent.WirePairsFromSave(SaveData);
    }

    // ── API pública ──────────────────────────────────────────────────────────

    // ── Cast / TV receiver ───────────────────────────────────────────────────

    /// <summary>
    /// Initializes the aquarium from a Cast state (TV receiver / WebGL build).
    /// Synthesizes a SaveData from TvAquariumState and sets up the scene without
    /// touching persistent storage.
    /// </summary>
    public void InitializeFromCastState(TvAquariumState state)
    {
        if (state == null) return;
        Debug.Log($"[AquariumManager] InitializeFromCastState — fish:{state.activeFish?.Count ?? 0}");

        // Build a transient SaveData from cast state (never persisted on TV)
        var castSave = new SaveData();
        castSave.selectedTankId  = state.selectedTankId;
        castSave.selectedBgId    = state.bgId;
        castSave.selectedSubId   = state.subId;
        castSave.lightPresetId   = state.lightId;
        castSave.fishSpeedMultiplier = state.fishSpeed;

        // Create fake owned fish entries so SpawnSavedFish can work
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

        // Deserialize deco placements
        if (!string.IsNullOrEmpty(state.decoJson) && state.decoJson != "{}")
        {
            try
            {
                var wrapper = JsonUtility.FromJson<DecoPlacementList>(state.decoJson);
                if (wrapper?.items != null) castSave.decoPositions = wrapper.items;
            }
            catch (Exception e) { Debug.LogWarning($"[AquariumManager] Cast deco parse failed: {e.Message}"); }
        }

        // Swap save — use cast save for initialization only
        var originalSave = SaveData;
        SaveData = castSave;

        try { InitializeAquarium(); }
        finally { /* Keep castSave as SaveData for the session — TV never writes to disk */ }

        // Apply ambient mode from sender
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

    // ── Gestión de tanque en runtime ─────────────────────────────────────────

    /// <summary>Añade un pez de la colección al tanque activo.</summary>
    public bool AddFishToTank(string uid)
    {
        if (SaveData.activeFishUids.Contains(uid)) return false;
        if (SaveData.activeFishUids.Count >= MaxFishInTank) return false;

        var saved = SaveData.ownedFish.Find(f => f.uid == uid);
        if (saved == null) return false;

        FishData data = allFishCatalog.Find(d => d.itemId == saved.speciesId) ?? allFishCatalog[0];
        Bounds bounds = tankController.GetTankBounds();

        var agent = fishSpawner.SpawnFish(data, bounds, saved);
        if (agent == null) return false;

        agent.SetNickname(saved.nickname);
        agent.SetUid(uid);
        // Restaurar estado de necesidades guardado
        agent.Needs.hunger = saved.hunger;
        agent.Needs.stress = saved.stress;
        agent.Needs.energy = saved.energy;
        SaveData.activeFishUids.Add(uid);
        SaveSystem.Save(SaveData);
        BreedingManager.Instance?.CheckBreedingPairs(SaveData);
        FishAgent.WirePairsFromSave(SaveData);
        return true;
    }

    /// <summary>Saca un pez del tanque (queda en la colección).</summary>
    public bool RemoveFishFromTank(string uid)
    {
        FishAgent target = null;
        foreach (var a in fishSpawner.ActiveFish)
            if (a != null && a.Uid == uid) { target = a; break; }

        if (target == null) return false;

        // Persistir necesidades antes de destruir el GameObject
        var savedFish = SaveData.ownedFish.Find(f => f.uid == uid);
        if (savedFish != null && target.Needs != null)
        {
            savedFish.hunger = target.Needs.hunger;
            savedFish.stress = target.Needs.stress;
            savedFish.energy = target.Needs.energy;
        }

        fishSpawner.DespawnFish(target);
        SaveData.activeFishUids.Remove(uid);
        SaveSystem.Save(SaveData);
        BreedingManager.Instance?.CheckBreedingPairs(SaveData);
        FishAgent.WirePairsFromSave(SaveData);
        return true;
    }

    /// <summary>Alimenta a todos los peces activos.</summary>
    public void FeedAll()
    {
        foreach (var fish in fishSpawner.ActiveFish)
            fish?.Feed();
    }

    /// <summary>Asusta a todos los peces (ej: toque brusco en pantalla).</summary>
    public void StartleAll(Vector3 position)
    {
        foreach (var fish in fishSpawner.ActiveFish)
            fish?.Startle(position);
    }

    /// <summary>
    /// Devuelve el hambre actual de un pez: en vivo si está en el tanque, guardado si no.
    /// </summary>
    public float GetLiveHunger(string uid)
    {
        foreach (var agent in fishSpawner.ActiveFish)
            if (agent != null && agent.Uid == uid) return agent.Needs.hunger;
        var saved = SaveData.ownedFish.Find(f => f.uid == uid);
        return saved?.hunger ?? 0f;
    }

    /// <summary>Estado FSM actual de un pez en el tanque. Null si no está activo.</summary>
    public FishState? GetLiveFishState(string uid)
    {
        foreach (var agent in fishSpawner.ActiveFish)
            if (agent != null && agent.Uid == uid) return agent.CurrentState;
        return null;
    }

    private float GetAverageHunger()
    {
        var fish = fishSpawner.ActiveFish;
        if (fish.Count == 0) return 0f;
        float sum = 0f;
        int count = 0;
        foreach (var f in fish)
            if (f != null) { sum += f.Needs.hunger; count++; }
        return count > 0 ? sum / count : 0f;
    }

    /// <summary>Renombra un pez en la colección y actualiza su FishAgent si está en el tanque.</summary>
    public bool RenameFish(string uid, string newName)
    {
        var saved = SaveData.ownedFish.Find(f => f.uid == uid);
        if (saved == null) return false;

        saved.nickname = newName;
        foreach (var agent in fishSpawner.ActiveFish)
            if (agent != null && agent.Uid == uid) { agent.SetNickname(newName); break; }

        SaveSystem.Save(SaveData);
        return true;
    }

    // ── Descarga de packs mid-session ────────────────────────────────────────────

    /// <summary>
    /// Descarga un pack si no está listo y, al terminar, respawnea cualquier placeholder
    /// del tanque como modelo real. Llamar tras comprar/reclamar cualquier ítem.
    /// En Editor no hace nada (los prefabs están disponibles directamente).
    /// </summary>
    public void TriggerPackDownload(string packName)
    {
        if (string.IsNullOrEmpty(packName)) return;
        if (AssetPackManager.IsPackReady(packName)) return; // ya descargado, nada que hacer
        StartCoroutine(TriggerPackDownloadCoroutine(packName));
    }

    private IEnumerator TriggerPackDownloadCoroutine(string packName)
    {
        var banner = UIManager.Instance?.ShowDownloadBanner();
        yield return AssetPackManager.DownloadPackOnPurchase(
            packName,
            onProgress: t => banner?.SetProgress(t),
            onComplete: () =>
            {
                fishSpawner?.RespawnPlaceholders();
                tankController?.GetComponent<DecorationPlacer>()?.RespawnPlaceholders();
            }
        );
        if (banner != null)
            yield return StartCoroutine(banner.CloseAfterDelay());
    }

    /// <summary>
    /// Descarga todos los packs on-demand en background tras una compra Premium mid-session.
    /// El UIManager ya escucha OnPackDownloadProgress y muestra el banner automáticamente.
    /// </summary>
    public void TriggerPremiumPackDownloads()
    {
        var packs = AssetPackManager.BuildBackgroundPackList(SaveData);
        if (packs.Count == 0) return;
        StartCoroutine(PremiumDownloadCoroutine(packs));
    }

    private IEnumerator PremiumDownloadCoroutine(List<string> packs)
    {
        yield return AssetPackManager.DownloadBackgroundPacks(packs);
        fishSpawner?.RespawnPlaceholders();
        tankController?.GetComponent<DecorationPlacer>()?.RespawnPlaceholders();
    }

    // ── Debug ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Solo para testeo en Editor. Desbloquea todo en memoria sin tocar el save en disco.
    /// Activa: premium, todas las decos, fondos, sustratos y luces del catálogo.
    /// </summary>
    private void DebugUnlockEverything()
    {
        var sd = SaveData;
        sd.isPremium = true;

        // Todas las decoraciones del catálogo
        if (allDecoCatalog != null)
            foreach (var d in allDecoCatalog)
                if (d != null && !string.IsNullOrEmpty(d.itemId) && !sd.ownedDecoIds.Contains(d.itemId))
                    sd.ownedDecoIds.Add(d.itemId);

        // Todos los fondos
        foreach (var p in TankBackground.Presets)
            if (!sd.ownedBgIds.Contains(p.id))
                sd.ownedBgIds.Add(p.id);

        // Todos los sustratos
        foreach (var p in DecorationPlacer.SubstratePresets)
            if (!sd.ownedSubIds.Contains(p.id))
                sd.ownedSubIds.Add(p.id);

        // Todas las luces
        foreach (var p in TankLightingController.Presets)
            if (!sd.ownedLightIds.Contains(p.id))
                sd.ownedLightIds.Add(p.id);

        // Todos los peces del catálogo (añade los que no estén ya en la colección)
        if (allFishCatalog != null)
        {
            foreach (var fishData in allFishCatalog)
            {
                bool already = sd.ownedFish.Exists(f => f.speciesId == fishData.itemId);
                if (!already)
                    SaveSystem.AddFish(sd, fishData.itemId);
            }
        }

        Debug.Log("[Debug] DebugUnlockAll: premium + " +
                  $"{sd.ownedDecoIds.Count} decos + {sd.ownedBgIds.Count} bg + " +
                  $"{sd.ownedSubIds.Count} sub + {sd.ownedLightIds.Count} luces + " +
                  $"{sd.ownedFish.Count} peces — solo en memoria, save no modificado.");
    }
}
