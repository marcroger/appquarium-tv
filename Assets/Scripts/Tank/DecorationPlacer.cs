using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// Gestiona la colocación de decoraciones en el tanque con posicionamiento libre.
/// Las decoraciones se colocan en coordenadas de mundo exactas (drag & drop).
///
/// Setup: añadir al mismo GameObject que TankController.
/// TankController.InitializeWithBounds() llama a InitializeDecoPlacer().
///
/// Efectos activos según gadgets colocados:
///   Filtro      → BubbleSystem.emissionRate x1.5
///   Calentador  → reduce hungerRate de todos los peces
///   Lámpara UV  → preventsAlgae flag
///   Piedra aire → columna de burbujas en posición fija
/// </summary>
[RequireComponent(typeof(TankController))]
public class DecorationPlacer : MonoBehaviour
{
    // ── Substrate Presets ─────────────────────────────────────────────────────

    public struct SubstratePreset
    {
        public string id;
        public string displayName;
        public Color  colorA;
        public Color  colorB;
        public bool   isStarterGift;
        public float  price;
        public int    pearlPrice;
        public int    displayOrder;
    }

    public static readonly SubstratePreset[] SubstratePresets =
    {
        // isStarterGift=true: sub_sand, sub_white, sub_gravel (free tier)
        new SubstratePreset { id = "sub_sand",         displayName = "sub.sand",         colorA = new Color(0.80f, 0.70f, 0.50f), colorB = new Color(0.90f, 0.82f, 0.65f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  0 },
        new SubstratePreset { id = "sub_white",        displayName = "sub.white",        colorA = new Color(0.90f, 0.92f, 0.95f), colorB = new Color(0.96f, 0.96f, 0.98f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  1 },
        new SubstratePreset { id = "sub_gold",         displayName = "sub.gold",         colorA = new Color(0.85f, 0.72f, 0.28f), colorB = new Color(0.95f, 0.84f, 0.42f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  2 },
        new SubstratePreset { id = "sub_gravel",       displayName = "sub.gravel",       colorA = new Color(0.45f, 0.42f, 0.38f), colorB = new Color(0.55f, 0.52f, 0.48f), isStarterGift = true,  price = 0f,    pearlPrice =  0, displayOrder =  3 },
        new SubstratePreset { id = "sub_pebbles",      displayName = "sub.pebbles",      colorA = new Color(0.55f, 0.52f, 0.48f), colorB = new Color(0.68f, 0.65f, 0.60f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  4 },
        new SubstratePreset { id = "sub_coral_rubble", displayName = "sub.coral.rubble", colorA = new Color(0.82f, 0.76f, 0.66f), colorB = new Color(0.92f, 0.88f, 0.80f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  5 },
        new SubstratePreset { id = "sub_slate",        displayName = "sub.slate",        colorA = new Color(0.28f, 0.30f, 0.33f), colorB = new Color(0.38f, 0.40f, 0.44f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  6 },
        new SubstratePreset { id = "sub_moss",         displayName = "sub.moss",         colorA = new Color(0.20f, 0.38f, 0.15f), colorB = new Color(0.30f, 0.50f, 0.22f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  7 },
        new SubstratePreset { id = "sub_mud",          displayName = "sub.mud",          colorA = new Color(0.22f, 0.16f, 0.10f), colorB = new Color(0.32f, 0.24f, 0.16f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  8 },
        new SubstratePreset { id = "sub_volcanic",     displayName = "sub.volcanic",     colorA = new Color(0.12f, 0.10f, 0.10f), colorB = new Color(0.20f, 0.18f, 0.16f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder =  9 },
        new SubstratePreset { id = "sub_lava",         displayName = "sub.lava",         colorA = new Color(0.35f, 0.08f, 0.04f), colorB = new Color(0.55f, 0.18f, 0.06f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 10 },
        new SubstratePreset { id = "sub_ice",          displayName = "sub.ice",          colorA = new Color(0.72f, 0.88f, 0.96f), colorB = new Color(0.88f, 0.95f, 1.00f), isStarterGift = false, price = 0.49f, pearlPrice = 10, displayOrder = 11 },
    };

    [Header("Catálogo de decoraciones disponibles")]
    public List<DecorationData> allDecorationCatalog = new();

    [Header("Posición Y: snapping automático")]
    [Tooltip("Epsilon anti z-fighting global. El contacto visual real lo controla DecorationData.embedDepth per-deco (default -0.03f = base pegada a la superficie).")]
    public float floorSnapYOffset = 0f;

    // Aspect-ratio remapping: set by TvSceneBootstrap after receiving INIT from mobile.
    // 0 = no remapping (old mobile client or same aspect ratio). See PlaceAt().
    public float MobileTankHalfWidth = 0f;

    // Estado
    private Bounds _tankBounds;
    private readonly Dictionary<string, PlacedDeco> _placed = new();
    private MeshRenderer _floorRenderer;
    private MeshRenderer _floorOccluderRenderer;
    private MeshRenderer _floorFadeOverlayRenderer;
    private string _currentSubId = "sub_sand";

    // Geometría del mesh del suelo (calculada en BuildFloorVisual, usada por FloorY)
    private float _floorMeshBaseY;  // world Y del mesh en ZFront
    private float _floorMeshRiseY;  // cuánto sube el mesh desde ZFront hasta ZBack

    /// <summary>
    /// True después de que LoadFromSave (o LoadFromSaveLegacy) se ejecutó.
    /// Evita que SyncDecoToSave sobreescriba el save con lista vacía cuando
    /// la carga falló silenciosamente (itemId no encontrado en catálogo, etc.).
    /// </summary>
    public bool SaveLoaded { get; set; }

    // Animación de elementos vivos
    private readonly List<StemAnim> _stemAnims = new();
    private readonly List<TipAnim>  _tipAnims  = new();

    // Bioluminiscencia nocturna
    private readonly Dictionary<string, List<Material>> _bioLumMats   = new();
    private readonly Dictionary<string, Light>          _bioLumLights = new();
    private Coroutine _bioLumFade;
    private float     _bioLumCurrentStrength = 0f; // [0,1] — fuente de verdad para el fade

    // Escala de emisión HDR. Valores >1 por canal activan bloom en URP (umbral ≈1.0).
    // Mobile (bloom ON):  0.75 → heliopora=1.5, distichopora=1.35 (bloom), pocillopora=1.125, corallium=0.75
    // TV (bloom OFF): 0.25 → valores máx ~0.5 — evita el color saturado plano sin glow que produce HDR sin bloom.
    // TV: si en sync desde mobile este valor vuelve a 0.75, restaurar a 0.25 (TV no tiene bloom).
    private const float BioLumEmissionScale = 0.25f;

    void OnEnable()  => AmbientModeController.OnModeChanged += OnAmbientChanged;
    void OnDisable() => AmbientModeController.OnModeChanged -= OnAmbientChanged;

    private void OnAmbientChanged(AmbientModeController.AmbientMode mode)
    {
        if (_bioLumFade != null) StopCoroutine(_bioLumFade);
        _bioLumFade = StartCoroutine(FadeBioLum(
            mode == AmbientModeController.AmbientMode.Night ? 1f : 0f, 2f));
    }

    private IEnumerator FadeBioLum(float target, float duration)
    {
        float start   = _bioLumCurrentStrength;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetBioLumStrength(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / duration)));
            yield return null;
        }
        SetBioLumStrength(target);
    }

    private void SetBioLumStrength(float strength)
    {
        _bioLumCurrentStrength = strength;
        foreach (var kv in _bioLumMats)
        {
            if (!_placed.TryGetValue(kv.Key, out var pd)) continue;
            // Emission = tintColor del coral * intensidad calibrada. BioLumEmissionScale mantiene
            // el brillo sutil (la mayoría quedan < 0.8 HDR por canal, bloom muy tenue o nulo).
            Color emit = pd.data.tintColor * (pd.data.bioGlowIntensity * BioLumEmissionScale * strength);
            foreach (var mat in kv.Value)
            {
                if (mat == null) continue;
                if (strength > 0.001f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emit);
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
            }
        }

        // Luz puntual: ilumina el suelo y peces cercanos para que el efecto se lea como brillo,
        // no solo como "coral de color". Sin luz visible los peces y el suelo no se tiñen.
        foreach (var kv in _bioLumLights)
        {
            if (kv.Value == null) continue;
            if (!_placed.TryGetValue(kv.Key, out var pd)) continue;
            kv.Value.intensity = pd.data.bioGlowIntensity * strength * 0.80f;
            kv.Value.enabled   = strength > 0.001f;
        }
    }

    // Elevación de la sombra planar sobre la superficie del suelo (evita z-fighting)
    private const float PlanarShadowLift = 0.02f;

    // Efectos acumulados
    public float ActiveStressReduction  { get; private set; }
    public float ActiveHungerRateBonus  { get; private set; }
    public bool  HasFilter              { get; private set; }
    public bool  HasUVLamp              { get; private set; }

    public string CurrentSubstrateId => _currentSubId;

    // ── Animación ────────────────────────────────────────────────────────────

    void Update()
    {
        float t = Time.time;

        for (int i = _stemAnims.Count - 1; i >= 0; i--)
        {
            var a = _stemAnims[i];
            if (a.stem == null) { _stemAnims.RemoveAt(i); continue; }
            float sway = Mathf.Sin(t * a.speed + a.phase) * a.amplitude;
            a.stem.localRotation = Quaternion.Euler(0f, 0f, a.baseTiltZ + sway);
        }

        for (int i = _tipAnims.Count - 1; i >= 0; i--)
        {
            var a = _tipAnims[i];
            if (a.tip == null) { _tipAnims.RemoveAt(i); continue; }
            float pulse = 1f + Mathf.Sin(t * a.speed + a.phase) * 0.06f;
            a.tip.localScale = a.baseScale * pulse;
        }
    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void InitializeDecoPlacer()
    {
        _tankBounds = GetComponent<TankController>().GetTankBounds();
        // Destruir suelo previo (al reinicializar por cambio de tanque)
        foreach (Transform child in transform)
            if (child.name == "TankFloor" || child.name == "TankFloorOccluder" || child.name == "TankFloorFadeOverlay")
                Destroy(child.gameObject);
        _floorRenderer             = null;
        _floorOccluderRenderer     = null;
        _floorFadeOverlayRenderer  = null;

        BuildFloorVisual();
        Debug.Log("[Deco] ✅ DecorationPlacer listo (free positioning)");
    }

    /// <summary>
    /// Destroy all placed decos and reset tracking state.
    /// Called before re-initializing from a new INIT message (Cast reconnect).
    /// </summary>
    public void RemoveAllDecos()
    {
        foreach (var key in new System.Collections.Generic.List<string>(_placed.Keys))
            RemoveGameObject(key);
        _placed.Clear();
        _bioLumMats.Clear();
        _bioLumLights.Clear();
        SaveLoaded = false;
    }

    // Rango seguro de Z: el background está en Z=+1.8, margen al frente y al fondo
    public const float ZFront    = -1.0f;   // más cercano visible (sin salirse del suelo)
    public const float ZBack     = +4.2f;   // límite del mesh del suelo
    public const float ZDecoBack = +3.0f;   // límite de colocación de decos

    public const int   OccluderSortingOrder    = 20;    // TankFloorOccluder — tapa geometría underground
    public const int   FadeOverlaySortingOrder = 22;    // TankFloorFadeOverlay — funde el borde trasero
    public const float FadeOverlayMaxAlpha     = 0.85f; // oscuridad máxima en la franja del back (0=transparente, 1=negro)
    public const float FadeOverlayHeightAbove  = 0.30f; // % de riseY que el overlay se extiende sobre el borde del suelo (cubre la línea visible)

    // Máximo de instancias de la misma deco en el tanque a la vez
    public const int MaxInstancesPerItem = 5;

    // Corrección de perspectiva 2.5D
    private const float ZPerspectiveY     = 0.45f;  // unidades mundo por unidad Z (subir al alejar)
    private const float ZPerspectiveScale = 0.10f;  // 10% de escala menos por unidad Z

    // Surface climbing — rangos de proximidad X (solo eje horizontal; Z no importa para detección)
    public const float SurfaceEnterRange = 0.8f;   // distancia X para ENTRAR en modo surface
    public const float SurfaceExitRange  = 1.2f;   // distancia X para SALIR (histéresis)

    // sortingOrder: mapeamos Z al rango (-8 … +5). Background está en -100 (TankBackground).
    // Frente (Z=-1) → +4  |  Neutro (Z=0) → 0  |  Fondo (Z=+1) → -4
    // Todos los valores están por encima de -100, por lo que las decos SIEMPRE ganan al fondo.
    private static int ZToSortingOrder(float z) => Mathf.Clamp(Mathf.RoundToInt(-z * 4f), -8, 5);

    /// <summary>Y de la superficie real del mesh del suelo en la profundidad Z dada.</summary>
    private float FloorSurfaceY(float z)
    {
        float t = Mathf.Clamp01((z - ZFront) / (ZBack - ZFront));
        return _floorMeshBaseY + t * _floorMeshRiseY;
    }

    /// <summary>
    /// Suelo Y en world-space para snapping de decos: superficie real + pequeño margen anti-clip.
    /// Usa la geometría real del mesh (no la aproximación lineal antigua).
    /// </summary>
    private float FloorY(float z) => FloorSurfaceY(z) + floorSnapYOffset;

    /// <summary>
    /// Superficie del suelo en world-space a una Z dada. Público para TvFishShadows,
    /// que necesita saber dónde cae la sombra de los peces sin duplicar la geometría
    /// del suelo (que se calcula en BuildFloorVisual a partir de _tankBounds).
    /// </summary>
    public float GetFloorSurfaceY(float z) => FloorSurfaceY(z);

    /// <summary>
    /// Borde SUPERIOR del suelo en world-space (el del fondo del tanque, que en la perspectiva
    /// 2.5D es el que aparece más arriba en pantalla). Lo usa TankBackground para encajar la
    /// imagen de fondo justo encima y que no asome la zona repetida del borde de la textura.
    /// Devuelve 0 si el suelo aún no se ha construido.
    /// </summary>
    public float FloorTopY => _floorRenderer == null ? 0f : _floorMeshBaseY + _floorMeshRiseY;

    /// <summary>
    /// Margen (en unidades de mundo) en el que la sombra se desvanece al acercarse al borde
    /// superior del suelo. 0 = comportamiento de siempre. Se puede cambiar en caliente para
    /// compararlo en la tele sin gastar un build por variante.
    /// </summary>
    public float SombraFade { get; private set; }

    private readonly List<Material> _shadowMats = new();

    public void SetSombraFade(float margen)
    {
        SombraFade = Mathf.Max(0f, margen);
        int n = 0;
        foreach (var m in _shadowMats)
        {
            if (m == null) continue;
            m.SetFloat("_ShadowTop",  FloorTopY);
            m.SetFloat("_ShadowFade", SombraFade);
            n++;
        }
        JsBridge.Log($"SOMBRA: fade={SombraFade:F2} aplicado a {n} materiales (suelo y={FloorTopY:F2})");
    }

    /// <summary>Posición Y correcta del pivot de una deco en su Z actual.</summary>
    private float GetDecoFloorY(PlacedDeco pd, float z)
        => FloorY(z) + pd.data.floorYOffset + pd.pivotBaseHeight;

    /// <summary>
    /// Mapea la posición Y del mundo al valor Z para profundidad 2.5D.
    /// Bottom of floor zone → front (ZFront). Top of floor zone → ZDecoBack (límite de decos).
    /// </summary>
    public float ComputeZFromY(float worldY)
    {
        float yMin = _tankBounds.min.y;
        float yMax = yMin + _tankBounds.size.y * 0.20f;
        float t    = Mathf.InverseLerp(yMin, yMax, worldY);
        return Mathf.Lerp(ZFront, ZDecoBack, t);
    }

    /// <summary>Coloca una decoración en la posición de mundo indicada.</summary>
    /// <param name="instanceId">
    /// ID de instancia. Si es null, se genera uno nuevo (itemId + "_" + n).
    /// Si ya existe en _placed, reemplaza esa instancia (reposicionamiento).
    /// </param>
    public bool PlaceAt(DecorationData data, Vector3 worldPos, bool flipped = false,
        float rotationY = 0f, float tiltX = 0f, float scaleFactor = 1f, bool fromSave = false,
        string instanceId = null, Quaternion? savedUserRot = null)
    {
        if (data == null) return false;
        SaveLoaded = true; // La escena es válida: se colocó al menos una deco

        // Resolver instanceId: generar si no se provee, reemplazar si ya existe
        if (instanceId == null)
        {
            if (GetPlacedCount(data.itemId) >= MaxInstancesPerItem) return false;
            int n = 0;
            while (_placed.ContainsKey(data.itemId + "_" + n)) n++;
            instanceId = data.itemId + "_" + n;
        }
        else if (_placed.ContainsKey(instanceId))
        {
            RemoveGameObject(instanceId); // reposicionamiento de instancia específica
        }

        // Decos nuevas empiezan en ZDecoBack (fondo usable antes de la zona transparente).
        // fromSave=true preserva el Z guardado para reproducir la escena exacta.
        if (!fromSave)
            worldPos.z = ZDecoBack;

        // Snap de Y: solo en placement nuevo. Al cargar desde save, la Y ya tiene
        // la corrección acumulada → no tocarla.
        // Floor: usar FloorY(z) — geometría real del mesh, no la aproximación lineal.
        // Otros tipos: ApplyYSnap con rawY clamped.
        float snappedY;
        if (fromSave)
            snappedY = worldPos.y;
        else if (data.placement == PlacementType.Floor)
            snappedY = FloorY(worldPos.z) + data.floorYOffset;
        else
            snappedY = ApplyYSnap(worldPos.y, data.placement);
        worldPos = new Vector3(worldPos.x, snappedY, worldPos.z);

        // Remap X from mobile coordinate space to TV coordinate space.
        // Mobile sends absolute world-X based on its own aspect ratio (portrait tank narrower than TV).
        // Without this, all decos cluster near the center of the TV's wider view.
        if (MobileTankHalfWidth > 0.1f && _tankBounds.extents.x > 0.1f)
            worldPos.x = worldPos.x * (_tankBounds.extents.x / MobileTankHalfWidth);

        // Clamp dentro de los bounds del tanque
        worldPos.x = Mathf.Clamp(worldPos.x, _tankBounds.min.x + 0.3f, _tankBounds.max.x - 0.3f);
        worldPos.y = Mathf.Clamp(worldPos.y, _tankBounds.min.y, _tankBounds.max.y - 0.3f);

        // Orden de resolución: referencia directa → AssetBundle (PAD) → procedural placeholder.
        GameObject prefabToSpawn = data.prefab;
        if (prefabToSpawn == null
            && !string.IsNullOrEmpty(data.assetBundleName)
            && !string.IsNullOrEmpty(data.assetBundleAssetName))
        {
            prefabToSpawn = AssetBundleLoader.LoadPrefab(data.assetBundleName, data.assetBundleAssetName);
            if (prefabToSpawn != null)
                Debug.Log($"[DecorationPlacer] {data.itemId} — loaded from bundle '{data.assetBundleName}'");
        }
        bool usingPlaceholder = prefabToSpawn == null;
        GameObject go = !usingPlaceholder
            ? Instantiate(prefabToSpawn, worldPos, Quaternion.identity, transform)
            : BuildProceduralMesh(data, worldPos);

        // Material override: variantes visuales del mismo prefab (ej: ancla oxidada).
        // Se aplica ANTES de FixNonURPMaterials a propósito: HallAnchor_rust_mat y
        // _oldrust usan URP/Lit, que renderiza MAGENTA en WebGL/Cast. Aplicándolo antes,
        // FixNonURPMaterials lo convierte a FishUnlit (always-included) igual que al
        // material base. Antes el override se aplicaba sin arreglar → salía magenta.
        if (data.overrideMaterial != null)
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
                mr.material = data.overrideMaterial;

        // Fix materials: shaders URP/Lit, Standard o glTF (de GLBs importados o de
        // overrideMaterial) renderizan magenta en el Cast device. Convertir a FishUnlit
        // en runtime. Se ejecuta SIEMPRE — con o sin override — para cubrir ambos caminos.
        FixNonURPMaterials(go);

        // Tint color: multiplica sobre la textura del prefab real, o establece el color base del placeholder.
        // IMPORTANTE: después de FixNonURPMaterials, los materiales usan FishUnlit (_Color, no _BaseColor).
        // Aplicar en ambas propiedades para cubrir: FishUnlit (_Color) y glTF/URP que puedan sobrevivir (_BaseColor).
        bool applyTint = usingPlaceholder
            ? (data.tintColor.a > 0f && data.tintColor != Color.white)
            : (data.tintColor != Color.white);
        if (applyTint)
            foreach (var mr in go.GetComponentsInChildren<Renderer>())
                foreach (var mat in mr.materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", data.tintColor);
                    if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     data.tintColor);
                }

        // Desactivar física si el prefab tiene Rigidbody (evita que la gravedad tire la deco al suelo).
        // No se destruye porque puede haber HingeJoint u otros joints que dependen de él.
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity  = false;
        }

        // Destruir FX hijos (partículas + luces) si el prefab los tiene permanentes y no los queremos
        if (data.disableFX)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                Destroy(ps.gameObject);
        }

        // Deshabilitar root motion para que el Animator no luche contra
        // nuestros transforms (posición/rotación/escala gestionados por código).
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim != null) anim.applyRootMotion = false;
        }

        // Glow light
        if (data.hasGlow)
            AddGlowLight(go, data.glowColor, data.glowIntensity);

        // Bioluminiscencia: recoger materiales instanciados y añadir luz puntual de reflejo
        if (data.hasBioLuminescence)
        {
            var mats = new List<Material>();
            foreach (var mr in go.GetComponentsInChildren<Renderer>())
                foreach (var mat in mr.materials)
                    if (mat != null && mat.HasProperty("_EmissionColor"))
                        mats.Add(mat);
            if (mats.Count > 0)
            {
                _bioLumMats[instanceId] = mats;

                // Luz puntual: color del coral, apagada de día, se enciende en SetBioLumStrength
                var bioLightGO = new GameObject("BioLum");
                bioLightGO.transform.SetParent(go.transform);
                bioLightGO.transform.localPosition = Vector3.up * 0.3f;
                var bioLight = bioLightGO.AddComponent<Light>();
                bioLight.type      = LightType.Point;
                bioLight.color     = data.tintColor == Color.white ? Color.cyan : data.tintColor;
                bioLight.intensity = 0f;
                bioLight.range     = 4.0f;
                bioLight.shadows   = LightShadows.None;
                bioLight.enabled   = false;
                _bioLumLights[instanceId] = bioLight;

                // Si ya es de noche al colocar, aplicar el strength actual (puede estar en fade)
                if (_bioLumCurrentStrength > 0.001f)
                    SetBioLumStrength(_bioLumCurrentStrength);
            }
        }

        // Rotación base: valores explícitos del SO (defaultRotationY / defaultRotationX).
        // NO se usa data.prefab.transform.eulerAngles porque los GLBs importados por GLTFast
        // tienen Y=180 bakeado para la conversión de coordenadas, lo que gira los modelos de
        // espaldas si se suma a defaultRotationY. defaultRotationY es la fuente de verdad.
        // Se recalcula en cada PlaceAt y NO se guarda (se guarda el total combinado).
        // Al cargar desde save: baseRotY=0, rotationY=valor guardado (ya incluye todo).
        float baseRotY;
        float userRotY;
        float baseTiltX;
        float baseRotZ;
        if (!fromSave)
        {
            // Placeholder procedimental: ya está construido en la orientación correcta.
            // Las correcciones base (X/Z) son para los GLBs importados tumbados, no aplican aquí.
            baseRotY  = usingPlaceholder ? 0f : data.defaultRotationY;
            baseTiltX = usingPlaceholder ? 0f : data.defaultRotationX;
            baseRotZ  = usingPlaceholder ? 0f : data.defaultRotationZ;
            userRotY  = rotationY;
        }
        else
        {
            // Al cargar: base* se recalcula desde el SO para reconstruir la rotación completa.
            //
            // baseRotY: el quaternion guardado (pd.userRot) es la rotación del USUARIO sin incluir
            // la rotación base del prefab (defaultRotationY). Necesitamos restituir baseRotY para
            // que ApplyTransforms() recalcule worldDesiredRot = userRot * qBase correctamente.
            // Ejemplo: estatua griega defaultRotationY=180 — sin este valor se carga de espaldas.
            //
            // Para saves legacy sin quaternion (savedUserRot==null): baseRotY=0 porque en ese
            // formato el rotationY guardado ya lleva el total (base+user).
            baseRotY  = (savedUserRot.HasValue && !usingPlaceholder) ? data.defaultRotationY : 0f;
            baseTiltX = usingPlaceholder ? 0f : data.defaultRotationX;
            baseRotZ  = usingPlaceholder ? 0f : data.defaultRotationZ;
            userRotY  = rotationY;
            tiltX     = tiltX - data.defaultRotationX;
        }

        var pd = new PlacedDeco
        {
            instanceId    = instanceId,
            go            = go,
            data          = data,
            flipped       = flipped,
            baseRotY      = baseRotY,
            rotationY     = userRotY,
            baseTiltX     = baseTiltX,
            baseRotZ      = baseRotZ,
            tiltX         = tiltX,
            scaleFactor   = Mathf.Max(0.1f, scaleFactor),
            isPlaceholder = usingPlaceholder,
        };
        _placed[instanceId] = pd;

        // ⚠ 2026-08-15 — reaplicar la bioluminiscencia AQUÍ, no antes.
        // El bloque de biolum de más arriba llama a SetBioLumStrength() para el caso "se
        // coloca de noche", pero ese método itera _placed... y esta deco no entraba en
        // _placed hasta esta línea, 80 más abajo: la guarda era un no-op.
        // Escenario que rompía: reconexión de noche. RemoveAllDecos limpia _bioLumMats pero
        // no _bioLumCurrentStrength, las decos se recolocan y SetNight() sale por su
        // early-return (el modo no ha cambiado) → los corales se quedaban apagados toda la
        // sesión, que es justo cuando se supone que tienen que brillar.
        if (_bioLumCurrentStrength > 0.001f) SetBioLumStrength(_bioLumCurrentStrength);

        // Inicializar userRot: cuaternión acumulado que representa la rotación del usuario en espacio mundo.
        // • savedUserRot != null → cargado desde save nuevo formato (quaternion directo).
        // • fromSave sin quaternion → reconstruir desde rotationY/tiltX legacy (saves antiguos).
        // • New placement → identity (sin rotación de usuario; la base está en qBase via baseTiltX+baseRotY).
        if (savedUserRot.HasValue)
            pd.userRot = savedUserRot.Value;
        else if (fromSave || userRotY != 0f || tiltX != 0f)
            pd.userRot = Quaternion.AngleAxis(tiltX, Vector3.forward)
                       * Quaternion.AngleAxis(userRotY, Vector3.up);
        // else: new placement, userRot stays Quaternion.identity

        // Ajustar Y para que el PUNTO DE APOYO del mesh quede a ras del suelo + embedDepth.
        //
        // Pipeline:
        //   1. Pre-escalar a defaultScale y aplicar rotación final ANTES de medir
        //      (bounds y supportPointLocal escalan correctamente con depthScale).
        //   2. Resolver punto de apoyo via TryGetSupportWorldY:
        //        a) supportPointLocal del SO (corales con anchor central)
        //        b) MeshRenderer.bounds.min.y (la mayoría de decos)
        //        c) SkinnedMeshRenderer fallback con validación (cofre/estatuas)
        //   3. Aplicar lift × depthScale para que el offset escale con perspectiva 2.5D.
        if (!fromSave && data.placement == PlacementType.Floor)
        {
            Vector3 preScale = data.defaultScale != Vector3.zero ? data.defaultScale : Vector3.one;
            go.transform.localScale    = preScale; // escala real sin flip — solo para medir
            go.transform.localRotation = Quaternion.Euler(pd.baseTiltX, pd.baseRotY, pd.baseRotZ); // rotación final para bounds correctos

            if (TryGetSupportWorldY(pd, out float supportY))
            {
                float targetY = FloorSurfaceY(go.transform.position.z) + data.embedDepth;
                float lift    = targetY - supportY;
                float absLift = Mathf.Abs(lift);
                // Sanity: skip near-zero noise y bounds corruptos (nodo Collada 100×).
                if (absLift > 0.01f && absLift < _tankBounds.size.y * 0.9f)
                {
                    // ApplyTransforms va a escalar el mesh por depthScale (perspectiva 2.5D).
                    // El offset del mesh respecto al pivot escala proporcionalmente.
                    float depthScale = Mathf.Clamp(1f - go.transform.position.z * ZPerspectiveScale, 0.6f, 1.4f);
                    var   pos        = go.transform.position;
                    pos.y = Mathf.Clamp(pos.y + lift * depthScale,
                        _tankBounds.min.y - _tankBounds.size.y,
                        _tankBounds.max.y - 0.3f);
                    go.transform.position = pos;
                }
            }
            pd.pivotBaseHeight = Mathf.Max(0f, go.transform.position.y - FloorY(go.transform.position.z));
        }
        else if (fromSave && data.placement == PlacementType.Floor)
        {
            // Al cargar desde save, la Y guardada ya incluye pivotBaseHeight.
            // Reconstruirlo para que drag/moveZ usen siempre la posición correcta.
            pd.pivotBaseHeight = Mathf.Max(0f, worldPos.y - FloorY(worldPos.z));
        }

        ApplyTransforms(pd); // aplica escala final (flip + depthScale + scaleFactor + rotación)

        AddShadow(pd);

        // Placements nuevos: los bounds del frame 0 son aproximados (Animator no ha corrido).
        // Refinar posición Y y sombra en el siguiente frame con bounds reales.
        if (!fromSave && data.placement == PlacementType.Floor)
            StartCoroutine(RefineFloorSnapNextFrame(pd));

        // Arrancar animación: ciclo open/close si el prefab lo tiene; si no, loop idle.
        var animator = go.GetComponentInChildren<Animator>();
        if (!TryStartAnimCycle(pd, animator))
            TryPlayLoopState(animator, data.loopStateName, fromSave ? Random.value : 0f);

        RecalculateEffects();
        ApplyGadgetSideEffects(data);

        // Si es superficie de montaje, añadir MeshColliders para que el raycast de snap funcione.
        if (data.isMountTarget)
            AddMeshCollidersIfNeeded(go);

        Debug.Log($"[Deco] Colocado: {data.itemName} en {worldPos}");
        return true;
    }

    /// <summary>Actualiza la rotación, inclinación y escala de una deco ya colocada.</summary>
    public bool UpdatePlacedTransform(string itemId, float rotY, float tiltX, float scaleFactor)
    {
        if (!_placed.TryGetValue(itemId, out var pd)) return false;
        bool tiltChanged  = !Mathf.Approximately(tiltX,       pd.tiltX);
        bool scaleChanged = !Mathf.Approximately(scaleFactor,  pd.scaleFactor);

        // Ajustar pivotBaseHeight proporcionalmente al cambio de escala ANTES de guardarlo.
        if (scaleChanged && pd.scaleFactor > 0.001f)
            pd.pivotBaseHeight = pd.pivotBaseHeight * scaleFactor / pd.scaleFactor;

        pd.rotationY   = rotY;
        pd.tiltX       = tiltX;
        pd.scaleFactor = Mathf.Max(0.1f, scaleFactor);
        ApplyTransforms(pd);

        // Ajustar Y solo cuando tilt o escala cambian — evita snap por error flotante en rotación pura.
        if (pd.mountedOnId == null && pd.data.placement == PlacementType.Floor && (tiltChanged || scaleChanged))
        {
            float tiltRad    = pd.tiltX * Mathf.Deg2Rad;
            float effectiveH = pd.pivotBaseHeight * Mathf.Cos(tiltRad);
            var pos = pd.go.transform.position;
            pos.y = FloorY(pos.z) + effectiveH;
            pd.go.transform.position = pos;
        }

        // Si está montada sobre otra deco, recalcular posición en la superficie del target.
        // Necesario cuando escala o tilt cambian: el clearance (pivotBaseHeight) ya se actualizó
        // arriba → DragDecoToSurface re-proyecta sobre el elipsoide con los nuevos parámetros.
        if (pd.mountedOnId != null && (tiltChanged || scaleChanged))
        {
            var cur = pd.go.transform.position;
            DragDecoToSurface(pd.instanceId, cur.x, cur.z, pd.mountedOnId);
            // DragDecoToSurface ya llama UpdateShadow y ApplyTransforms internamente.
            return true;
        }

        UpdateShadow(pd);
        return true;
    }

    // ── API delta de rotación/tilt (siempre en espacio mundo) ────────────────────

    /// <summary>
    /// Gira la deco indicada delta grados alrededor del eje Y mundo (spin izq/der).
    /// Al pre-multiplicar en world space, el eje de giro es SIEMPRE Y mundo independientemente
    /// del tilt acumulado — el usuario siempre ve la deco girar a izquierda o derecha.
    /// </summary>
    public void ApplyUserRotDelta(string id, float deltaRotY)
    {
        if (!_placed.TryGetValue(id, out var pd) || pd.go == null) return;
        pd.userRot = Quaternion.AngleAxis(deltaRotY, Vector3.up) * pd.userRot;
        ApplyTransforms(pd);
        // Snapear al suelo: aunque Y-rotation no cambia el height en teoría,
        // si hay tilt acumulado el punto más bajo del mesh puede cambiar al girar.
        if (pd.mountedOnId == null)
            SnapBoundsToFloor(pd);
        UpdateShadow(pd);
    }

    /// <summary>
    /// Inclina la deco indicada delta grados alrededor del eje Z mundo (lean izq/der en pantalla).
    /// Al pre-multiplicar en world space, el eje de tilt es SIEMPRE Z mundo independientemente
    /// del giro acumulado — el usuario siempre ve la deco inclinarse a izquierda o derecha.
    /// Tras el tilt re-snapea al suelo para mantener la base del mesh alineada.
    /// </summary>
    public void ApplyUserTiltDelta(string id, float deltaTilt)
    {
        if (!_placed.TryGetValue(id, out var pd) || pd.go == null) return;
        pd.userRot = Quaternion.AngleAxis(deltaTilt, Vector3.forward) * pd.userRot;
        ApplyTransforms(pd);
        // Re-snap al suelo (el tilt cambia la altura del punto más bajo del mesh).
        if (pd.mountedOnId == null)
            SnapBoundsToFloor(pd);
        else
        {
            var cur = pd.go.transform.position;
            DragDecoToSurface(pd.instanceId, cur.x, cur.z, pd.mountedOnId);
        }
        UpdateShadow(pd);
    }

    /// <summary>
    /// Resetea rotación, tilt y escala de la deco a los valores por defecto del SO.
    /// Mantiene la posición XZ y el target de montaje. Re-snapea Y al suelo o a la superficie.
    /// </summary>
    public bool ResetTransform(string itemId)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return false;

        // Si había rotación/escala custom, hay que reproporcionar pivotBaseHeight a la escala 1
        // antes de resetar (igual que UpdatePlacedScale).
        if (pd.scaleFactor > 0.001f && !Mathf.Approximately(pd.scaleFactor, 1f))
            pd.pivotBaseHeight = pd.pivotBaseHeight / pd.scaleFactor;

        pd.userRot     = Quaternion.identity;
        pd.rotationY   = 0f;
        pd.tiltX       = 0f;
        pd.scaleFactor = 1f;
        ApplyTransforms(pd);

        // Re-snap Y: al suelo si está en el suelo, a la superficie del target si está montada
        if (pd.mountedOnId != null)
        {
            var cur = pd.go.transform.position;
            DragDecoToSurface(pd.instanceId, cur.x, cur.z, pd.mountedOnId);
            // DragDecoToSurface llama ApplyTransforms y oculta la sombra internamente
        }
        else
        {
            SnapBoundsToFloor(pd);
            UpdateShadow(pd);
        }
        return true;
    }

    /// <summary>
    /// Devuelve el scaleFactor actual de la deco indicada.
    /// </summary>
    public float GetPlacedScale(string id) =>
        _placed.TryGetValue(id, out var pd) ? pd.scaleFactor : 1f;

    /// <summary>
    /// Actualiza solo la escala de la deco (sin tocar userRot).
    /// Re-snapea al suelo si está en el suelo; re-proyecta al target si está montada.
    /// </summary>
    public bool UpdatePlacedScale(string id, float scaleFactor)
    {
        if (!_placed.TryGetValue(id, out var pd) || pd.go == null) return false;
        bool scaleChanged = !Mathf.Approximately(scaleFactor, pd.scaleFactor);
        if (scaleChanged && pd.scaleFactor > 0.001f)
            pd.pivotBaseHeight = pd.pivotBaseHeight * scaleFactor / pd.scaleFactor;
        pd.scaleFactor = Mathf.Max(0.1f, scaleFactor);
        ApplyTransforms(pd);
        if (pd.mountedOnId == null && pd.data.placement == PlacementType.Floor)
            SnapBoundsToFloor(pd);
        if (pd.mountedOnId != null && scaleChanged)
        {
            var cur = pd.go.transform.position;
            DragDecoToSurface(pd.instanceId, cur.x, cur.z, pd.mountedOnId);
            return true;
        }
        UpdateShadow(pd);
        return true;
    }

    /// <summary>
    /// Mueve el GO hasta que el punto más bajo de sus renderers coincide con el suelo 2.5D en su Z.
    /// Llamar tras cambios de tilt o para corregir decos flotando desde el save.
    /// </summary>
    public void SnapBoundsToFloor(string itemId)
    {
        if (_placed.TryGetValue(itemId, out var pd)) SnapBoundsToFloor(pd);
    }

    /// <summary>
    /// Al cambiar de tanque: refresca _tankBounds y clampea todas las decos de suelo
    /// a los nuevos límites. Las decos montadas sobre otra se ignoran (siguen su padre).
    /// </summary>
    /// <summary>
    /// Reposiciona todas las decos al cambiar de tanque.
    /// Escala X proporcionalmente respecto a los oldBounds (opción B),
    /// luego clampea al nuevo espacio como red de seguridad (opción A).
    /// Y se re-snapa al nuevo suelo siempre.
    /// </summary>
    public void RescaleAndClampToNewBounds(Bounds oldBounds)
    {
        _tankBounds = GetComponent<TankController>().GetTankBounds();

        float scaleX = oldBounds.size.x > 0.01f
            ? _tankBounds.size.x / oldBounds.size.x
            : 1f;

        float xMin = _tankBounds.min.x + 0.3f;
        float xMax = _tankBounds.max.x - 0.3f;

        foreach (var pd in _placed.Values)
        {
            if (pd.go == null)          continue;
            if (pd.mountedOnId != null) continue; // sigue a su padre

            var pos = pd.go.transform.position;
            // Escala X proporcional respecto al centro (0)
            pos.x = pos.x * scaleX;
            // Clamp de seguridad
            pos.x = Mathf.Clamp(pos.x, xMin, xMax);
            pd.go.transform.position = pos;
            SnapBoundsToFloor(pd); // re-snap Y al nuevo suelo
            UpdateShadow(pd);      // sincronizar sombra a la nueva posición
        }
    }

    /// <summary>Clamp sin escalar — usado si no hay oldBounds disponibles.</summary>
    public void ClampAllToCurrentBounds()
    {
        _tankBounds = GetComponent<TankController>().GetTankBounds();
        float xMin = _tankBounds.min.x + 0.3f;
        float xMax = _tankBounds.max.x - 0.3f;

        foreach (var pd in _placed.Values)
        {
            if (pd.go == null)           continue;
            if (pd.mountedOnId != null)  continue;

            var pos = pd.go.transform.position;
            pos.x = Mathf.Clamp(pos.x, xMin, xMax);
            pd.go.transform.position = pos;
            SnapBoundsToFloor(pd);
            UpdateShadow(pd);
        }
    }

    private void SnapBoundsToFloor(PlacedDeco pd)
    {
        if (pd.go == null || pd.data.placement != PlacementType.Floor) return;
        if (pd.mountedOnId != null) return; // montada sobre otra deco — no snapear al suelo

        var   pos            = pd.go.transform.position;
        float effectiveFloor = FloorSurfaceY(pos.z) + pd.data.embedDepth;

        // Punto de apoyo actual del mesh en mundo (Y). Soporta tres fuentes:
        // 1. supportPointLocal del SO (override manual — útil en corales con anchor central)
        // 2. MeshRenderer estático → bounds.min.y (preciso para la mayoría de decos)
        // 3. SkinnedMeshRenderer si no hay MR estático (cofre/estatuas — solo si bounds son plausibles)
        if (!TryGetSupportWorldY(pd, out float supportY)) return;

        float lift = effectiveFloor - supportY;
        pos.y += lift;
        pd.go.transform.position = pos;
        // pivotBaseHeight = distancia de FloorY (surface + snapOffset) al pivot.
        // Mantiene la semántica original para drag/move (clearance sigue siendo correcto).
        pd.pivotBaseHeight = Mathf.Max(0f, pos.y - FloorY(pos.z));
    }

    /// <summary>
    /// Calcula la Y world-space del "punto de apoyo" actual del mesh — el punto que debe
    /// tocar la superficie de contacto (suelo o mount target).
    ///
    /// Orden de resolución:
    ///   1. <see cref="DecorationData.supportPointLocal"/> ≠ zero: usar ese punto local del
    ///      prefab, transformado al mundo (incluye rotación y escala actuales).
    ///   2. MeshRenderer estático: usar <c>bounds.min.y</c> del AABB combinado.
    ///   3. SkinnedMeshRenderer (fallback): solo si los bounds son plausibles
    ///      (size razonable y centro cerca de la posición actual). Evita el bug de
    ///      Animator activo que reporta bounds basura.
    /// </summary>
    private bool TryGetSupportWorldY(PlacedDeco pd, out float supportY)
    {
        supportY = 0f;
        if (pd.go == null) return false;

        // 1. Override manual del SO (útil para corales con anchor central)
        if (pd.data.supportPointLocal != Vector3.zero)
        {
            // TransformPoint usa la rotación y escala actuales del GO → válido para
            // calcular dónde caería el punto de apoyo tras user rotation/scale.
            supportY = pd.go.transform.TransformPoint(pd.data.supportPointLocal).y;
            return true;
        }

        // 2. MeshRenderer estático
        Bounds? mb = null;
        foreach (var r in pd.go.GetComponentsInChildren<MeshRenderer>())
        {
            if (mb == null) mb = r.bounds;
            else { var tmp = mb.Value; tmp.Encapsulate(r.bounds); mb = tmp; }
        }
        if (mb.HasValue && mb.Value.size.magnitude < 30f)
        {
            supportY = mb.Value.min.y;
            return true;
        }

        // 3. SkinnedMeshRenderer fallback (cofre, estatuas con SMR-only)
        // Validar bounds antes de fiarse — Animator puede reportar basura.
        Vector3 posXY = new Vector3(pd.go.transform.position.x, 0f, pd.go.transform.position.z);
        float   maxR  = _tankBounds.size.magnitude;
        Bounds? sb    = null;
        foreach (var smr in pd.go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var b = smr.bounds;
            if (b.size.magnitude < 0.01f || b.size.magnitude > 30f) continue;
            if (Vector3.Distance(new Vector3(b.center.x, 0f, b.center.z), posXY) > maxR) continue;
            if (sb == null) sb = b;
            else { var tmp = sb.Value; tmp.Encapsulate(b); sb = tmp; }
        }
        if (sb.HasValue)
        {
            supportY = sb.Value.min.y;
            return true;
        }

        // Sin bounds plausibles: no re-snapear (confiar en pivotBaseHeight original).
        return false;
    }

    /// <summary>Desplaza la deco en X, respetando los bordes del tanque.</summary>
    public bool MoveDecoX(string itemId, float deltaX)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return false;
        var pos = pd.go.transform.position;
        pos.x = Mathf.Clamp(pos.x + deltaX, _tankBounds.min.x + 0.3f, _tankBounds.max.x - 0.3f);
        pd.go.transform.position = pos;
        UpdateShadow(pd);
        return true;
    }

    /// <summary>
    /// Desplaza el Z de una deco colocada con corrección de perspectiva 2.5D:
    ///   - Positivo = más al fondo (Z aumenta, Y sube, escala baja)
    ///   - Negativo = más al frente (Z baja, Y baja, escala sube)
    /// </summary>
    public bool MoveDecoZ(string itemId, float deltaZ)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return false;
        var pos  = pd.go.transform.position;
        pos.z    = Mathf.Clamp(pos.z + deltaZ, ZFront, ZDecoBack);
        if (pd.mountedOnId == null)
            pos.y = GetDecoFloorY(pd, pos.z);
        pd.go.transform.position = pos;
        ApplyTransforms(pd);
        UpdateShadow(pd);
        return true;
    }

    /// <summary>Posición mundo actual de una deco colocada (para iniciar drag).</summary>
    public Vector3 GetPlacedWorldPos(string itemId)
        => _placed.TryGetValue(itemId, out var pd) && pd.go != null
            ? pd.go.transform.position
            : Vector3.zero;

    /// <summary>
    /// Mueve una deco directamente a la posición (x, z) indicada — para drag suave en tiempo real.
    /// Y se calcula automáticamente desde pivotBaseHeight: la deco siempre toca el suelo.
    /// </summary>
    public void DragDecoTo(string itemId, float newX, float newZ)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return;
        var pos    = pd.go.transform.position;
        pos.x      = Mathf.Clamp(newX, _tankBounds.min.x + 0.3f, _tankBounds.max.x - 0.3f);
        pos.z      = Mathf.Clamp(newZ, ZFront, ZDecoBack);
        pos.y      = GetDecoFloorY(pd, pos.z);
        pd.go.transform.position = pos;
        ApplyTransforms(pd);
        // Re-snap después de ApplyTransforms: la escala cambia con Z (perspectiva 2.5D),
        // lo que desplaza la base del mesh. Sin esto, la deco queda hundida durante el drag
        // y salta al soltar cuando EndDrag llama SnapDecoToFloor.
        SnapBoundsToFloor(pd);
        // Restaurar sombra si venía oculta por DragDecoToSurface (vuelta al suelo desde una roca).
        if (pd.shadowGO != null && !pd.shadowGO.activeSelf) pd.shadowGO.SetActive(true);
        UpdateShadow(pd);
    }

    /// <summary>Obtiene la transformación actual de una deco colocada.</summary>
    public bool TryGetTransform(string itemId, out float rotY, out float tiltX, out float scaleFactor)
    {
        if (_placed.TryGetValue(itemId, out var pd))
        {
            rotY        = pd.rotationY;
            tiltX       = pd.tiltX;
            scaleFactor = pd.scaleFactor;
            return true;
        }
        rotY = tiltX = 0f;
        scaleFactor = 1f;
        return false;
    }

    /// <summary>Invierte el flip (espejo X) de la deco indicada.</summary>
    public bool FlipDeco(string instanceId)
    {
        if (!_placed.TryGetValue(instanceId, out var pd)) return false;
        pd.flipped = !pd.flipped;
        ApplyTransforms(pd);
        UpdateShadow(pd);
        return true;
    }

    /// <summary>Quita la decoración con el itemId indicado del tanque.</summary>
    public bool Remove(string itemId)
    {
        if (!_placed.ContainsKey(itemId)) return false;
        RemoveGameObject(itemId);
        RecalculateEffects();
        return true;
    }

    /// <summary>Devuelve true si al menos una instancia de itemId está colocada en el tanque.</summary>
    public bool IsPlaced(string itemId)
    {
        foreach (var pd in _placed.Values)
            if (pd.data != null && pd.data.itemId == itemId) return true;
        return false;
    }

    /// <summary>Número de instancias de itemId colocadas actualmente.</summary>
    public int GetPlacedCount(string itemId)
    {
        int n = 0;
        foreach (var pd in _placed.Values)
            if (pd.data != null && pd.data.itemId == itemId) n++;
        return n;
    }

    /// <summary>True si se pueden colocar más instancias de itemId.</summary>
    public bool CanPlaceMore(string itemId) => GetPlacedCount(itemId) < MaxInstancesPerItem;

    /// <summary>Devuelve el itemId al que pertenece una instanceId dada.</summary>
    public string GetItemIdForInstance(string instanceId)
        => _placed.TryGetValue(instanceId, out var pd) ? pd.data.itemId : instanceId;

    /// <summary>Devuelve la instanceId de la primera instancia de itemId encontrada (o null).</summary>
    public string GetFirstInstanceId(string itemId)
    {
        foreach (var kv in _placed)
            if (kv.Value.data != null && kv.Value.data.itemId == itemId) return kv.Key;
        return null;
    }

    /// <summary>
    /// Diagnóstico: coloca una instancia de cada deco en la lista, espera unos frames a que
    /// los GLBs/prefabs carguen, log-ea sus bounds y borra las instancias temporales.
    /// Llamar desde un MenuItem en play mode.
    /// </summary>
    public IEnumerator MeasureAllDecosCoroutine(System.Collections.Generic.List<DecorationData> allDecos)
    {
        const int   BatchSize  = 6;
        const float Spacing    = 1.8f;
        string desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

        // Filtrar decos reales (temporal: solo los 3 corales que se están ajustando)
        var _testIds = new System.Collections.Generic.HashSet<string>
            { "deco_coral_corallium", "deco_coral_stylaster", "deco_coral_distichopora" };
        var filtered = new System.Collections.Generic.List<DecorationData>();
        foreach (var d in allDecos)
            if (d != null && _testIds.Contains(d.itemId)) filtered.Add(d);

        float z      = ZDecoBack;
        float floorY = FloorY(z);

        int batchNum = 0;
        for (int batchStart = 0; batchStart < filtered.Count; batchStart += BatchSize)
        {
            batchNum++;
            var batch = new System.Collections.Generic.List<string>();

            // Distribuir este lote centrado en el tank
            int count = Mathf.Min(BatchSize, filtered.Count - batchStart);
            float totalW = (count - 1) * Spacing;
            float xStart = -totalW * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var data  = filtered[batchStart + i];
                string id = "MEAS_" + data.itemId;
                float x   = xStart + i * Spacing;
                PlaceAt(data, new Vector3(x, floorY, z), flipped: false,
                        rotationY: 0f, tiltX: 0f, scaleFactor: 1f,
                        fromSave: false, instanceId: id);
                batch.Add(id);
            }

            // Esperar — GLTFast necesita 60 frames para cargar todos los GLBs del lote
            for (int i = 0; i < 60; i++) yield return null;

            // Screenshot
            string path = System.IO.Path.Combine(desktopPath,
                $"deco_review_{batchNum:D2}.png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null; // 1 frame para escribir el fichero

            // Log de qué hay en este screenshot
            var names = new System.Text.StringBuilder($"[DecoReview] Screenshot {batchNum}: ");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) names.Append(" | ");
                names.Append(filtered[batchStart + i].itemId);
            }
            Debug.Log(names.ToString());

            // Limpiar lote
            foreach (var id in batch) Remove(id);

            // 1 frame de pausa entre lotes
            yield return null;
        }

        Debug.Log($"[DecoReview] ✅ {batchNum} screenshots en Desktop (deco_review_01.png … deco_review_{batchNum:D2}.png)");
    }

    /// <summary>
    /// Diagnóstico: imprime en Console el itemId y los bounds visuales (tamaño world-space)
    /// de todas las decos actualmente colocadas. Útil para calibrar defaultScale.
    /// Llamar en play mode (desde MenuItem) después de que el juego haya cargado.
    /// </summary>
    public void LogAllPlacedBounds()
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("[DecoSizes] itemId | sizeX | sizeY | sizeZ | maxDim | scale");
        foreach (var kv in _placed)
        {
            var pd = kv.Value;
            if (pd.go == null || pd.data == null) continue;
            Bounds? b = null;
            foreach (var r in pd.go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds; else { var t = b.Value; t.Encapsulate(r.bounds); b = t; }
            }
            if (!b.HasValue) continue;
            var sz = b.Value.size;
            float maxDim = Mathf.Max(sz.x, sz.y, sz.z);
            lines.AppendLine($"  {pd.data.itemId,-38} {sz.x:F2} {sz.y:F2} {sz.z:F2}  max={maxDim:F2}  sc={pd.data.defaultScale.x}");
        }
        Debug.Log(lines.ToString());
    }

    /// <summary>Devuelve el Z actual de una deco colocada (0f si no existe).</summary>
    public float GetDecoZ(string itemId) =>
        _placed.TryGetValue(itemId, out var pd) && pd.go != null ? pd.go.transform.position.z : 0f;

    /// <summary>
    /// Devuelve true si el movimiento Z es posible: el Z no ha llegado al límite
    /// Y además la corrección de perspectiva en Y tiene recorrido (la deco no está
    /// pegada al suelo/techo del tanque en la dirección que se quiere mover).
    /// </summary>
    public bool CanMoveDecoZ(string itemId, float deltaZ)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return false;
        var   pos  = pd.go.transform.position;
        float newZ = Mathf.Clamp(pos.z + deltaZ, ZFront, ZDecoBack);
        return Mathf.Abs(newZ - pos.z) > 0.001f;   // solo límite de Z
    }

    /// <summary>Carga decoraciones de forma asíncrona (yield cada 2 decos) para no bloquear el browser loop.</summary>
    public System.Collections.IEnumerator LoadFromSaveAsync(List<DecoPlacement> placements)
    {
        SaveLoaded = true;
        if (placements == null) yield break;
        int n = 0;
        foreach (var p in placements)
        {
            if (string.IsNullOrEmpty(p.instanceId))
                p.instanceId = p.itemId + "_0";

            var data = allDecorationCatalog.Find(d => d.itemId == p.itemId);
            if (data == null)
            {
                Debug.LogWarning($"[Deco] LoadFromSaveAsync: itemId '{p.itemId}' not found ({allDecorationCatalog.Count} entries). Skipped.");
                JsBridge.Log($"ERR deco not found: {p.itemId}");
                continue;
            }
            JsBridge.Log($"Placing deco {++n}: {p.itemId} prefab={(data.prefab != null ? "OK" : "NULL")}");
            var pos = p.position;
            if (data.placement == PlacementType.Floor && Mathf.Approximately(pos.z, 0f))
                pos.z = ZDecoBack;
            Quaternion? savedQ = p.hasUserRot
                ? (Quaternion?)new Quaternion(p.quatX, p.quatY, p.quatZ, p.quatW)
                : null;
            PlaceAt(data, pos, p.flipped, p.rotationY, p.tiltX, p.scaleFactor, fromSave: true, instanceId: p.instanceId, savedUserRot: savedQ);
            if (n % 2 == 0) yield return null; // yield every 2 decos — keeps browser loop alive
        }
        foreach (var p in placements)
            if (!string.IsNullOrEmpty(p.mountedOnInstanceId))
                MountDecoOnTarget(p.instanceId, p.mountedOnInstanceId);
        JsBridge.Log($"Decos placed: {_placed.Count}/{placements.Count}");
    }

    /// <summary>Carga decoraciones desde la lista de posiciones guardada.</summary>
    public void LoadFromSave(List<DecoPlacement> placements)
    {
        SaveLoaded = true;
        if (placements == null) return;
        foreach (var p in placements)
        {
            // Migración: saves anteriores no tienen instanceId → asignar sufijo _0
            if (string.IsNullOrEmpty(p.instanceId))
                p.instanceId = p.itemId + "_0";

            var data = allDecorationCatalog.Find(d => d.itemId == p.itemId);
            if (data == null)
            {
                Debug.LogWarning($"[Deco] LoadFromSave: itemId '{p.itemId}' no encontrado en catálogo ({allDecorationCatalog.Count} entradas). Deco omitida.");
                continue;
            }
            try
            {
                // Migración: saves antiguos guardaban Z=0 → mover al fondo.
                var pos = p.position;
                if (data.placement == PlacementType.Floor && Mathf.Approximately(pos.z, 0f))
                {
                    pos.z = ZDecoBack;
                    Debug.Log($"[Deco] Migración Z=0→ZDecoBack: {p.itemId}");
                }
                Quaternion? savedQ = p.hasUserRot
                    ? (Quaternion?)new Quaternion(p.quatX, p.quatY, p.quatZ, p.quatW)
                    : null;
                PlaceAt(data, pos, p.flipped, p.rotationY, p.tiltX, p.scaleFactor, fromSave: true, instanceId: p.instanceId, savedUserRot: savedQ);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Deco] LoadFromSave: error al colocar '{p.itemId}': {e.Message}");
            }
        }
        // Segundo pase: restaurar relaciones de montaje (targets ya están colocados).
        foreach (var p in placements)
        {
            if (!string.IsNullOrEmpty(p.mountedOnInstanceId))
                MountDecoOnTarget(p.instanceId, p.mountedOnInstanceId);
        }

        Debug.Log($"[Deco] LoadFromSave: {_placed.Count}/{placements.Count} decos restauradas.");
    }

    /// <summary>Carga decoraciones desde lista legacy de itemIds (sin posición).</summary>
    public void LoadFromSaveLegacy(List<string> activeDecoIds)
    {
        SaveLoaded = true;
        if (activeDecoIds == null) return;
        // Distribuir automáticamente en el fondo, igual que el sistema anterior de slots
        float usableWidth = _tankBounds.size.x * 0.8f;
        float step        = activeDecoIds.Count > 1 ? usableWidth / (activeDecoIds.Count + 1) : usableWidth * 0.5f;
        float startX      = _tankBounds.min.x + (_tankBounds.size.x - usableWidth) * 0.5f;
        float floorY      = _tankBounds.min.y + floorSnapYOffset;

        for (int i = 0; i < activeDecoIds.Count; i++)
        {
            var data = allDecorationCatalog.Find(d => d.itemId == activeDecoIds[i]);
            if (data == null) continue;
            float x = startX + step * (i + 1);
            PlaceAt(data, new Vector3(x, floorY, 0f), flipped: false);
        }
    }

    /// <summary>Devuelve la lista de posiciones actuales para persistir.</summary>
    public List<DecoPlacement> GetCurrentPlacements()
    {
        var result = new List<DecoPlacement>();
        foreach (var kv in _placed)
        {
            var pdv = kv.Value;
            var q   = pdv.userRot;
            result.Add(new DecoPlacement
            {
                instanceId          = kv.Key,
                itemId              = pdv.data.itemId,
                position            = pdv.go != null ? pdv.go.transform.position : Vector3.zero,
                flipped             = pdv.flipped,
                rotationY           = pdv.baseRotY + pdv.rotationY,  // legacy (por si se lee con versión antigua)
                tiltX               = pdv.baseTiltX + pdv.tiltX,      // legacy
                scaleFactor         = pdv.scaleFactor,
                mountedOnInstanceId = pdv.mountedOnId,
                hasUserRot          = true,
                quatX = q.x, quatY = q.y, quatZ = q.z, quatW = q.w,
            });
        }
        return result;
    }

    // ── Mount / Unmount API ──────────────────────────────────────────────────

    /// <summary>
    /// Añade MeshCollider a cada MeshFilter hijo del GO (si no tiene ya uno).
    /// Necesario para que RaycastSurface pueda encontrar la geometría exacta.
    /// Requiere que los meshes sean legibles (isReadable=true en el ModelImporter).
    /// </summary>
    private static void AddMeshCollidersIfNeeded(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.sharedMesh.isReadable) continue; // mesh no legible (bundle sin Read/Write) → skip
            if (mf.GetComponent<MeshCollider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex     = false; // cóncavo para geometría exacta
        }
    }

    /// <summary>
    /// Lanza un rayo hacia abajo en (x, targetZ) contra los MeshColliders del target.
    /// Devuelve la Y del punto de impacto más alto, o null si no hay impacto.
    /// </summary>
    private float? RaycastSurface(string targetId, float x, float z)
    {
        if (!_placed.TryGetValue(targetId, out var tpd) || tpd.go == null) return null;

        var bounds  = GetPlacedBounds(targetId);
        float startY = (bounds.HasValue ? bounds.Value.max.y : tpd.go.transform.position.y) + 2f;

        var   ray   = new Ray(new Vector3(x, startY, z), Vector3.down);
        float bestY = float.MinValue;
        bool  hit   = false;

        foreach (var col in tpd.go.GetComponentsInChildren<MeshCollider>())
        {
            if (!col.enabled) continue;
            if (col.Raycast(ray, out RaycastHit info, 20f))
            {
                if (info.point.y > bestY) { bestY = info.point.y; hit = true; }
            }
        }
        return hit ? bestY : (float?)null;
    }

    /// <summary>
    /// Monta la deco <paramref name="decoId"/> encima de <paramref name="targetId"/>:
    /// reparenta su GO como hijo del target y registra la relación.
    /// </summary>
    public void MountDecoOnTarget(string decoId, string targetId)
    {
        if (!_placed.TryGetValue(decoId,   out var pd)  || pd.go  == null) return;
        if (!_placed.TryGetValue(targetId, out var tpd) || tpd.go == null) return;

        pd.go.transform.SetParent(tpd.go.transform, worldPositionStays: true);
        pd.mountedOnId = targetId;

        // Actualizar sortingOrder ahora que mountedOnId está asignado (+1 sobre el target).
        // Necesario tanto en EndDecoDrag (drag terminó) como en LoadFromSave (segundo pase).
        ApplyTransforms(pd);

        // Ocultar sombra mientras está montado (queda raro flotando sobre la roca)
        if (pd.shadowGO != null) pd.shadowGO.SetActive(false);

        // Refrescar AABB caché tras montar (la posición y rotación han cambiado)
        UpdateShadow(pd);

        Debug.Log($"[Deco] Montado: {decoId} sobre {targetId}");
    }

    /// <summary>
    /// Desmonta la deco de su target: la reparenta de vuelta al DecorationPlacer
    /// manteniendo la posición world. No hace floor-snap (el caller decide qué hacer después).
    /// </summary>
    public void UnparentDeco(string decoId)
    {
        if (!_placed.TryGetValue(decoId, out var pd) || pd.go == null) return;
        if (pd.mountedOnId == null) return;

        pd.go.transform.SetParent(transform, worldPositionStays: true);
        pd.mountedOnId = null;

        if (pd.shadowGO != null) pd.shadowGO.SetActive(true);

        Debug.Log($"[Deco] Desmontado: {decoId}");
    }

    // ── Surface climbing API ──────────────────────────────────────────────────

    /// <summary>
    /// AABB world-space de una deco colocada.
    /// Prioridad: Colliders > MeshRenderers + SkinnedMeshRenderers > esfera por defaultScale.
    /// </summary>
    public Bounds? GetPlacedBounds(string itemId)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return null;

        Bounds? b = null;
        foreach (var col in pd.go.GetComponentsInChildren<Collider>())
        {
            if (!col.enabled) continue;
            if (b == null) b = col.bounds;
            else { var tmp = b.Value; tmp.Encapsulate(col.bounds); b = tmp; }
        }
        if (!b.HasValue)
            foreach (var r in pd.go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds;
                else { var tmp = b.Value; tmp.Encapsulate(r.bounds); b = tmp; }
            }
        if (!b.HasValue)
        {
            Vector3 s = pd.data.defaultScale != Vector3.zero ? pd.data.defaultScale : Vector3.one;
            float r = Mathf.Max(s.x, s.y, s.z) * 0.5f * pd.scaleFactor;
            b = new Bounds(pd.go.transform.position, Vector3.one * r * 2f);
        }
        return b;
    }

    /// <summary>
    /// Devuelve el instanceId de la deco colocada más cercana a <paramref name="worldPos"/>
    /// (ignorando la propia deco con id <paramref name="draggingId"/>), dentro del rango dado.
    /// La distancia se mide en 3D al AABB del target (XYZ): 0 si worldPos está dentro del AABB.
    /// Devuelve null si no hay ninguna candidata dentro del rango.
    /// </summary>
    public string TryGetNearestSurface(string draggingId, Vector3 worldPos, float range)
    {
        string nearest = null;
        float  minDist = float.MaxValue;

        foreach (var kv in _placed)
        {
            if (kv.Key == draggingId || kv.Value.go == null) continue;
            if (kv.Value.data?.isMountTarget != true) continue;
            var b = GetPlacedBounds(kv.Key);
            if (!b.HasValue) continue;

            // Distancia 3D al AABB surface (0 si worldPos está dentro del volumen).
            // Usar XZ para detectar proximidad horizontal/profundidad + Y como guardia de altura:
            //   · dx, dz: distancia al borde del AABB en cada eje horizontal/profundidad.
            //   · dy: distancia fuera del rango [min.y .. max.y+1u] — la +1u deja margen
            //         para entrar en surface mode cuando la deco está justo sobre la cima.
            float dx = Mathf.Max(0f, b.Value.min.x - worldPos.x, worldPos.x - b.Value.max.x);
            float dz = Mathf.Max(0f, b.Value.min.z - worldPos.z, worldPos.z - b.Value.max.z);
            float dy = Mathf.Max(0f, b.Value.min.y - worldPos.y, worldPos.y - (b.Value.max.y + 1f));

            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist < range && dist < minDist) { minDist = dist; nearest = kv.Key; }
        }
        return nearest;
    }

    /// <summary>
    /// Mueve una deco en modo surface climbing: la deco se pega a la superficie del target.
    ///
    /// Pipeline:
    ///   1. Raycast vertical desde (newX, target.max.y+1, requestedZ) hacia abajo.
    ///      Filtrado por subtree del target (el dragging deco puede tener sus propios colliders).
    ///   2. Si impacta: alinear el punto de apoyo del mesh con hit.point + hit.normal × embedDepth.
    ///      Esto sigue la SUPERFICIE REAL del mesh triangulado, no una aproximación elipsoidal.
    ///   3. Si no impacta (off-mesh): fallback al modelo elipsoidal antiguo.
    ///
    /// Gesto horizontal → X. Gesto vertical → Z (profundidad). Y automática.
    /// </summary>
    public void DragDecoToSurface(string itemId, float newX, float requestedZ, string targetId)
    {
        if (!_placed.TryGetValue(itemId,   out var pd)  || pd.go  == null) return;
        if (!_placed.TryGetValue(targetId, out var tpd) || tpd.go == null) return;

        var tb = GetPlacedBounds(targetId);
        if (!tb.HasValue) return;

        // Clamp X/Z al footprint del target (con margen anti-overhang)
        float clampedX = Mathf.Clamp(newX,      tb.Value.min.x, tb.Value.max.x);
        float clampedZ = Mathf.Clamp(requestedZ, tb.Value.min.z, tb.Value.max.z);

        // Intentar raycast vertical contra el mesh real del target
        bool hitOk = TryRaycastSurface(tpd, clampedX, clampedZ,
                                       out Vector3 hitPoint, out Vector3 hitNormal);

        Vector3 newPos;
        if (hitOk)
        {
            // Punto de contacto deseado: superficie real + offset en la dirección normal
            Vector3 contactWorld = hitPoint + hitNormal * pd.data.embedDepth;

            // Calcular dónde está el punto de apoyo del mesh actualmente y trasladar
            if (!TryGetSupportWorldY(pd, out float supportY))
                supportY = pd.go.transform.position.y - pd.pivotBaseHeight; // fallback al modelo de pivot

            float deltaY = contactWorld.y - supportY;
            newPos = new Vector3(clampedX, pd.go.transform.position.y + deltaY, clampedZ);
        }
        else
        {
            // Fallback elipsoidal (raycast no impactó — target sin MeshCollider o XZ off-mesh)
            float cx = tb.Value.center.x, cy = tb.Value.center.y, cz = tb.Value.center.z;
            float a  = Mathf.Max(tb.Value.extents.x, 0.01f);
            float b  = Mathf.Max(tb.Value.extents.y, 0.01f);
            float c  = Mathf.Max(Mathf.Min(tb.Value.extents.z, a * 2f), 0.01f);
            float dx = Mathf.Clamp(clampedX - cx, -a, a);
            float dz = Mathf.Clamp(clampedZ - cz, -c, c);
            float txz2 = (dx / a) * (dx / a) + (dz / c) * (dz / c);
            if (txz2 > 1f) { float inv = 1f / Mathf.Sqrt(txz2); dx *= inv; dz *= inv; }
            float inner = Mathf.Max(0f, 1f - (dx / a) * (dx / a) - (dz / c) * (dz / c));
            float dy = b * Mathf.Sqrt(inner);
            float nrx = dx / (a * a), nry = dy / (b * b), nrz = dz / (c * c);
            float nrLen = Mathf.Sqrt(nrx * nrx + nry * nry + nrz * nrz);
            if (nrLen > 0.001f) { nrx /= nrLen; nry /= nrLen; nrz /= nrLen; }
            else                 { nrx  = 0f;    nry  = 1f;    nrz  = 0f; }
            float clearance = pd.pivotBaseHeight + pd.data.embedDepth + floorSnapYOffset;
            newPos = new Vector3(cx + dx + nrx * clearance,
                                 cy + dy + nry * clearance,
                                 cz + dz + nrz * clearance);
        }

        var pos = newPos;
        pos.x = Mathf.Clamp(pos.x, _tankBounds.min.x + 0.3f, _tankBounds.max.x - 0.3f);
        pos.y = Mathf.Clamp(pos.y, _tankBounds.min.y,         _tankBounds.max.y - 0.1f);
        pos.z = Mathf.Clamp(pos.z, ZFront, ZDecoBack);
        pd.go.transform.position = pos;
        ApplyTransforms(pd);
        // Mientras la deco asciende por la superficie de otra, la sombra de contacto
        // se oculta: su posición Y está encima de la roca, no en el suelo, y se vería
        // flotando en el agua. Se restaura en DragDecoTo (vuelta al suelo) o en
        // UnparentDeco → SnapDecoToFloor (soltar en suelo).
        if (pd.shadowGO != null) pd.shadowGO.SetActive(false);
    }

    // Buffer compartido para Physics.RaycastNonAlloc — evita alocar arrays en cada drag
    private static readonly RaycastHit[] _surfaceHitBuffer = new RaycastHit[16];

    /// <summary>
    /// Raycast vertical contra los colliders del target. Filtra solo hits que pertenecen
    /// al subtree del target GO (ignora colliders del dragging deco u otras decos).
    /// Devuelve el hit más alto (el primero encontrado bajando).
    /// </summary>
    private bool TryRaycastSurface(PlacedDeco target, float x, float z,
                                   out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = default; hitNormal = default;
        var tb = GetPlacedBounds(target.instanceId);
        if (!tb.HasValue) return false;

        // Origen 1u por encima del top del AABB del target, distancia = altura del AABB + margen
        Vector3 origin   = new Vector3(x, tb.Value.max.y + 1f, z);
        float   distance = tb.Value.size.y + 2f;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _surfaceHitBuffer, distance,
            ~0, QueryTriggerInteraction.Ignore);

        // Filtrar hits que estén bajo el subtree del target GO
        float bestY = float.NegativeInfinity;
        bool  found = false;
        for (int i = 0; i < count; i++)
        {
            var h = _surfaceHitBuffer[i];
            if (h.collider == null) continue;
            if (!IsDescendantOf(h.collider.transform, target.go.transform)) continue;
            if (h.point.y > bestY)
            {
                bestY     = h.point.y;
                hitPoint  = h.point;
                hitNormal = h.normal.sqrMagnitude > 0.001f ? h.normal.normalized : Vector3.up;
                found     = true;
            }
        }
        return found;
    }

    private static bool IsDescendantOf(Transform t, Transform root)
    {
        while (t != null)
        {
            if (t == root) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>
    /// Lleva una deco de vuelta al suelo en su Z actual (perspectiva 2.5D).
    /// Si estaba montada sobre otra deco, la desmonta primero.
    /// </summary>
    public void SnapDecoToFloor(string itemId)
    {
        if (!_placed.TryGetValue(itemId, out var pd) || pd.go == null) return;

        if (pd.mountedOnId != null) UnparentDeco(itemId);

        // Recalcular pivotBaseHeight desde los bounds reales del mesh.
        // Si venía montada sobre otra deco, su pivotBaseHeight era relativo a esa altura —
        // no al suelo. SnapBoundsToFloor mide los renderers actuales y recalcula ambos.
        SnapBoundsToFloor(pd);
        ApplyTransforms(pd);
        UpdateShadow(pd);
    }

    /// <summary>Itera todas las decos colocadas como (instanceId, GameObject).</summary>
    public IEnumerable<(string instanceId, GameObject go)> GetAllPlaced()
    {
        foreach (var kv in _placed)
            if (kv.Value.go != null)
                yield return (kv.Key, kv.Value.go);
    }

    /// <summary>
    /// Devuelve el AABB world-space cacheado de cada deco para SteeringController.
    /// El caché se actualiza en RefreshAabb(), llamado desde UpdateShadow() en cada
    /// evento de movimiento o escala — coste cero en el hot-loop de peces.
    /// </summary>
    public IEnumerable<(Vector3 pos, Bounds aabb)> GetPlacedObstacleData()
    {
        foreach (var kv in _placed)
        {
            var pd = kv.Value;
            if (pd.go == null) continue;
            if (!pd.cachedAabb.HasValue) RefreshAabb(pd); // lazy init primer frame
            yield return (pd.go.transform.position, pd.cachedAabb.Value);
        }
    }

    /// <summary>
    /// Devuelve la posición del escondite (isHideout) colocado más cercano dentro de maxDist.
    /// Null si no hay ninguno en rango. Usado por SteeringController para peces tímidos en Flee.
    /// </summary>
    public Vector3? GetNearestHideoutPosition(Vector3 from, float maxDist)
    {
        Vector3? best    = null;
        float   bestSqr  = maxDist * maxDist;
        foreach (var kv in _placed)
        {
            if (kv.Value.data?.isHideout != true || kv.Value.go == null) continue;
            Vector3 pos     = kv.Value.go.transform.position;
            float   sqrDist = (pos - from).sqrMagnitude;
            if (sqrDist < bestSqr) { bestSqr = sqrDist; best = pos; }
        }
        return best;
    }

    /// <summary>
    /// Recalcula el AABB world-space de una deco y lo guarda en cachedAabb.
    /// MeshRenderers primero (tamaño visual real); Colliders no-trigger como fallback
    /// (los joints kinematic del ancla pueden tener bounds inesperados).
    /// </summary>
    private void RefreshAabb(PlacedDeco pd)
    {
        if (pd.go == null) { pd.cachedAabb = null; return; }

        Bounds? b = null;

        // 1. MeshRenderers — tamaño visual exacto, funciona para decos estáticas y GLBs
        foreach (var mr in pd.go.GetComponentsInChildren<MeshRenderer>())
        {
            if (b == null) b = mr.bounds;
            else { var tmp = b.Value; tmp.Encapsulate(mr.bounds); b = tmp; }
        }

        // 2. Colliders no-trigger — para decos sin MeshRenderer (algunos props de physics)
        if (!b.HasValue)
        {
            foreach (var col in pd.go.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                if (b == null) b = col.bounds;
                else { var tmp = b.Value; tmp.Encapsulate(col.bounds); b = tmp; }
            }
        }

        // 3. Fallback esférico — placeholders procedimentales sin renderer ni collider
        if (!b.HasValue)
        {
            Vector3 s = pd.data.defaultScale != Vector3.zero ? pd.data.defaultScale : Vector3.one;
            float r   = Mathf.Max(Mathf.Max(s.x, s.y), 0.35f) * pd.scaleFactor;
            b = new Bounds(pd.go.transform.position, new Vector3(r * 2f, r * 2f, r * 2f));
        }

        pd.cachedAabb = b;
    }

    /// <summary>Devuelve los itemIds de las decoraciones colocadas (compatibilidad legacy).</summary>
    public List<string> GetActiveSaveIds()
    {
        var ids = new List<string>(_placed.Keys);
        return ids;
    }

    /// <summary>Cambia el sustrato (textura del suelo) al preset indicado.</summary>
    public void SetSubstrate(string subId)
    {
        SubstratePreset? found = null;
        foreach (var p in SubstratePresets)
            if (p.id == subId) { found = p; break; }

        if (found == null) return;

        _currentSubId = subId;
        var mat = BuildFloorMaterial(subId, found.Value.colorA, found.Value.colorB);
        if (_floorRenderer         != null) _floorRenderer.material         = mat;
        if (_floorOccluderRenderer != null) _floorOccluderRenderer.material = mat;
    }

    // ── Internos ─────────────────────────────────────────────────────────────

    private float ApplyYSnap(float rawY, PlacementType placement)
    {
        switch (placement)
        {
            case PlacementType.Floor:
                return _tankBounds.min.y + floorSnapYOffset;
            case PlacementType.Surface:
                return _tankBounds.max.y - 0.2f;
            default: // Floating, Wall → keep raw Y clamped later
                return rawY;
        }
    }

    private void RemoveGameObject(string itemId)
    {
        if (_placed.TryGetValue(itemId, out var inst))
        {
            if (inst.animCycle != null) StopCoroutine(inst.animCycle);
            if (inst.go        != null) Destroy(inst.go);
            if (inst.shadowGO  != null) Destroy(inst.shadowGO);
            _placed.Remove(itemId);
            _bioLumMats.Remove(itemId);
            _bioLumLights.Remove(itemId);
        }
    }

    private static void AddGlowLight(GameObject parent, Color color, float intensity)
    {
        var glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(parent.transform);
        glowGO.transform.localPosition = Vector3.zero;
        var light = glowGO.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = color;
        light.intensity = intensity;
        light.range     = 1.5f;
        light.shadows   = LightShadows.None;
    }

    /// <summary>
    /// Convierte en runtime cualquier material con shader no-URP (e.g. Standard embebido en GLB)
    /// a URP/Lit o URP/Unlit, copiando la textura principal y el color base.
    /// Se omiten renderers con overrideMaterial, Sprites/Default y similares.
    /// </summary>
    /// <summary>
    /// Convierte en runtime cualquier material con shader no-URP (e.g. Standard o GLTFast
    /// embebido en GLB) a URP/Lit, copiando la textura principal y el color base.
    /// También repara materiales URP/Unlit sin textura que hayan quedado blancos por una
    /// conversión incorrecta en el Editor (cuando se leyó _MainTex pero GLTFast usa _BaseMap).
    /// </summary>
    public static void FixNonURPMaterials(GameObject go)
    {
        // Device-safe targets (CG legacy, sin LightMode → ejecutan en el Cast renderer):
        //   DecoLit  = con iluminación (relieve) — preferido para decos.
        //   FishUnlit/Sprites = plano — fallback si DecoLit no estuviera disponible.
        // URP/Lit, Standard y glTF NO ejecutan en el Cast (magenta), aunque estén always-included.
        var decoLit   = Shader.Find("Appquarium/DecoLit");
        var fishUnlit = Shader.Find("Appquarium/FishUnlit") ?? Shader.Find("Sprites/Default");
        var litTarget = decoLit != null ? decoLit : fishUnlit;
        if (litTarget == null) return;

        foreach (var mr in go.GetComponentsInChildren<Renderer>())
        {
            if (mr is ParticleSystemRenderer) continue;
            var mats = mr.sharedMaterials;
            bool anyFixed = false;
            var newMats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) { newMats[i] = mat; continue; }
                string sname = mat.shader != null ? mat.shader.name : "";

                // ⚠ 2026-08-11 — FishUnlit en una DECO la deja SIN ILUMINACIÓN.
                // El ancla venía de fábrica con Appquarium/FishUnlit y esta guarda la
                // dejaba pasar por "ya es device-safe": salía como una silueta negra en la
                // tele mientras la roca y el coral (que sí caían en DecoLit) tenían volumen.
                // FishUnlit es plano a propósito —vale para peces— pero una deco necesita
                // el lambert de DecoLit para que se le note el relieve del mesh.
                bool unlitEnDeco = decoLit != null
                                   && sname.Contains("Appquarium/FishUnlit")
                                   && !mat.name.EndsWith("_DECOLIT");

                // Ya device-safe → dejar intacto: Sprites/UI, DecoLit, o ya procesado.
                if (!unlitEnDeco
                    && (sname.Contains("Sprites") || sname.Contains("UI/Default")
                        || sname.Contains("Appquarium/")
                        || mat.name.EndsWith("_DECOLIT")))
                { newMats[i] = mat; continue; }

                // Todo lo demás que NO ejecuta en el Cast (URP/Lit, Standard, glTF/PbrMetallic,
                // Hidden/InternalError) → convertir a DecoLit (o FishUnlit fallback). Soporta los
                // tres convenios de propiedades: URP (_BaseMap), Standard (_MainTex), glTFast
                // (baseColorTexture). Antes el glTF se escapaba ("glTF/PbrMetallicRoughness" no
                // contiene "glTFPbr") → corales/estatuas salían magenta en el device.
                bool needsFix = unlitEnDeco
                             || sname.Contains("Universal Render Pipeline/Lit")
                             || sname.Contains("Hidden/InternalError")
                             || sname.Contains("Standard")
                             || sname.Contains("glTF")
                             || sname.Contains("PbrMetallic");
                if (!needsFix) { newMats[i] = mat; continue; }

                Texture baseTex = null;
                if (mat.HasProperty("_BaseMap"))                            baseTex = mat.GetTexture("_BaseMap");
                if (baseTex == null && mat.HasProperty("baseColorTexture")) baseTex = mat.GetTexture("baseColorTexture");
                if (baseTex == null && mat.HasProperty("_MainTex"))         baseTex = mat.GetTexture("_MainTex");

                Color baseColor = Color.white;
                if (mat.HasProperty("_BaseColor"))           baseColor = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("baseColorFactor"))  baseColor = mat.GetColor("baseColorFactor");
                else if (mat.HasProperty("_Color"))           baseColor = mat.GetColor("_Color");

                // ⚠ 2026-08-15 — antes se logueaba CADA material inspeccionado (por material,
                // por renderer, por prefab): cientos de mensajes en ráfaga durante la carga,
                // y cada línea de log viaja por el canal Cast hasta la app del móvil.
                // Ahora sólo se anota la CONVERSIÓN, que es la señal útil y es rara.
                // (Aquel log, además, engañaba: imprimía el shader de ENTRADA, y hacía pensar
                //  que el ancla seguía en FishUnlit cuando ya se estaba convirtiendo.)
                JsBridge.Log($"FixMat {go.name}: {mat.name} [{sname}] → {litTarget.name}");
                var fixedMat = new Material(litTarget) { name = mat.name + "_DECOLIT" };
                if (baseTex != null) fixedMat.SetTexture("_MainTex", baseTex);
                if (fixedMat.HasProperty("_Color"))      fixedMat.SetColor("_Color", baseColor);
                if (fixedMat.HasProperty("_Brightness")) fixedMat.SetFloat("_Brightness", 1f);
                newMats[i] = fixedMat;
                anyFixed = true;
            }
            if (anyFixed) mr.materials = newMats;
        }
    }

    private void BuildFloorVisual()
    {
        var go = new GameObject("TankFloor");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // Perspectiva 2.5D: borde trasero más alto en Y → aparece más arriba en pantalla ortográfica.
        // riseY es proporcional al alto del tanque para que el suelo ocupe el mismo % visual
        // independientemente del tamaño del acuario (tanque pequeño o grande).
        float hw     = _tankBounds.size.x * 0.52f;
        float floorY = _tankBounds.min.y - transform.position.y;
        const float FloorHeightFraction = 0.22f;  // suelo = 22% del alto del tanque (consistente en todos los tamaños)
        float riseY  = _tankBounds.size.y * FloorHeightFraction;

        // Guardar geometría para FloorSurfaceY() — calcula dónde está el suelo a cada Z.
        _floorMeshBaseY = _tankBounds.min.y;
        _floorMeshRiseY = riseY;

        // tileW en unidades mundo: tanque más grande → más repeticiones → textura más densa.
        const float tileW = 5f;
        float uvX        = _tankBounds.size.x / tileW;
        float uvDecoBack = (ZDecoBack - ZFront) / tileW;
        float uvTotal    = (ZBack     - ZFront) / tileW;

        // Fila intermedia en ZDecoBack: hasta ahí el sustrato es totalmente opaco
        // (zona donde se colocan las decoraciones). De ZDecoBack a ZBack alpha cae a 0
        // (la textura desaparece). Esto va alineado con TankFloorFadeOverlay (mismo rango Z).
        // Decos limitadas a ZDecoBack → nunca pisan la zona transparente → sin clipping.
        float tDecoBack = (ZDecoBack - ZFront) / (ZBack - ZFront);
        float yDecoBack = floorY + tDecoBack * riseY;

        var mesh = new Mesh { name = "Floor_Mesh" };

        // 9 vértices: 3 filas × 3 columnas (izq, centro, der).
        // Vertex color RGB: oscuro en bordes → blanco en centro (sombra lateral).
        const float s = 0.55f;

        mesh.vertices = new Vector3[]
        {
            new(-hw, floorY,         ZFront   ),  // 0 front-izq
            new(  0, floorY,         ZFront   ),  // 1 front-centro
            new( hw, floorY,         ZFront   ),  // 2 front-der
            new(-hw, yDecoBack,      ZDecoBack),  // 3 mid-izq
            new(  0, yDecoBack,      ZDecoBack),  // 4 mid-centro
            new( hw, yDecoBack,      ZDecoBack),  // 5 mid-der
            new(-hw, floorY + riseY, ZBack    ),  // 6 back-izq
            new(  0, floorY + riseY, ZBack    ),  // 7 back-centro
            new( hw, floorY + riseY, ZBack    ),  // 8 back-der
        };
        mesh.triangles = new int[]
        {
            0,3,1, 1,3,4,  1,4,2, 2,4,5,   // front → decoBack (alpha 1 → 1)
            3,6,4, 4,6,7,  4,7,5, 5,7,8,   // decoBack → back (alpha 1 → 0)
        };
        mesh.uv = new Vector2[]
        {
            new(0, 0),           new(uvX*0.5f, 0),           new(uvX, 0),
            new(0, uvDecoBack),  new(uvX*0.5f, uvDecoBack),  new(uvX, uvDecoBack),
            new(0, uvTotal),     new(uvX*0.5f, uvTotal),     new(uvX, uvTotal),
        };
        mesh.colors = new Color[]
        {
            new(s,s,s, 1), new(1,1,1, 1), new(s,s,s, 1),  // front: opaco
            new(s,s,s, 1), new(1,1,1, 1), new(s,s,s, 1),  // decoBack: opaco
            new(s,s,s, 0), new(1,1,1, 0), new(s,s,s, 0),  // back: alpha 0 (textura desaparece)
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;

        _floorRenderer = go.AddComponent<MeshRenderer>();
        _floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _floorRenderer.receiveShadows    = true;  // recibe sombras de peces y decos
        _floorRenderer.sortingOrder      = -5;

        var defaultMat = BuildFloorMaterial("sub_sand",
            new Color(0.72f, 0.62f, 0.42f), new Color(0.58f, 0.48f, 0.30f));
        _floorRenderer.material = defaultMat;

        BuildFloorOccluder(hw, floorY, riseY);
        if (_floorOccluderRenderer != null)
            _floorOccluderRenderer.material = defaultMat;

        BuildFloorFadeOverlay(hw, floorY, riseY);
    }

    /// <summary>
    /// Mesh occluder que cubre la zona underground (por debajo del suelo).
    /// sortingOrder=+20 garantiza que renderiza encima de todas las decos,
    /// tapando la geometría que se mete dentro del suelo.
    /// Se renderiza con el mismo material que el sustrato, vertex alpha=1 (opaco total).
    /// </summary>
    private void BuildFloorOccluder(float hw, float floorY, float riseY)
    {
        var go = new GameObject("TankFloorOccluder");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // Extensión hacia abajo: suficiente para tapar cualquier geometría underground.
        float depth = _tankBounds.size.y * 0.6f;

        // El occluder tiene su borde superior siguiendo la pendiente del suelo
        // (ZFront a ZDecoBack) y se extiende hacia abajo desde ahí.
        // Esto garantiza que tapa exactamente la zona underground en espacio de pantalla.
        float t          = (ZDecoBack - ZFront) / (ZBack - ZFront);
        float riseAtBack = riseY * t;

        const float s = 0.55f;  // sombra de bordes — igual que el suelo
        float uvX    = _tankBounds.size.x / 5f;

        var mesh = new Mesh { name = "FloorOccluder_Mesh" };

        // 9 vértices: 3 columnas × 3 filas (top-front, top-back, bottom)
        mesh.vertices = new Vector3[]
        {
            new(-hw, floorY,            ZFront   ),  // 0 top-front-izq
            new(  0, floorY,            ZFront   ),  // 1 top-front-centro
            new( hw, floorY,            ZFront   ),  // 2 top-front-der
            new(-hw, floorY+riseAtBack, ZDecoBack),  // 3 top-back-izq
            new(  0, floorY+riseAtBack, ZDecoBack),  // 4 top-back-centro
            new( hw, floorY+riseAtBack, ZDecoBack),  // 5 top-back-der
            new(-hw, floorY - depth,    ZFront   ),  // 6 bot-izq
            new(  0, floorY - depth,    ZFront   ),  // 7 bot-centro
            new( hw, floorY - depth,    ZFront   ),  // 8 bot-der
        };
        mesh.triangles = new int[]
        {
            // Rampa (top-front → top-back): cubre underground de decos en Z medio
            0,3,1, 1,3,4,  1,4,2, 2,4,5,
            // Frente (top-front → bottom): cubre underground de decos en Z=ZFront
            6,0,7, 7,0,1,  7,1,8, 8,1,2,
        };
        mesh.uv = new Vector2[]
        {
            new(0,       0), new(uvX*0.5f, 0), new(uvX, 0),  // top-front
            new(0,       1), new(uvX*0.5f, 1), new(uvX, 1),  // top-back
            new(0,       0), new(uvX*0.5f, 0), new(uvX, 0),  // bottom
        };
        // Todos alpha=1 (completamente opaco) — el punto clave del occluder
        mesh.colors = new Color[]
        {
            new(s,s,s, 1), new(1,1,1, 1), new(s,s,s, 1),
            new(s,s,s, 1), new(1,1,1, 1), new(s,s,s, 1),
            new(s,s,s, 1), new(1,1,1, 1), new(s,s,s, 1),
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;
        _floorOccluderRenderer = go.AddComponent<MeshRenderer>();
        _floorOccluderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _floorOccluderRenderer.receiveShadows    = false;
        _floorOccluderRenderer.sortingOrder      = OccluderSortingOrder;
        // El material se asigna en SetSubstrate (llamado tras BuildFloorVisual)
    }

    /// <summary>
    /// Franja oscura semitransparente que funde el borde trasero del sustrato con el fondo.
    /// Mesh de 3 filas × 3 cols:
    ///   Fila 0 (front, en superficie del suelo en ZDecoBack): alpha=0 → no afecta zona de decos
    ///   Fila 1 (mid,   en superficie del suelo en ZBack):     alpha=MaxAlpha → oscurece el back del sustrato
    ///   Fila 2 (top,   por encima del borde del suelo):       alpha=0 → fade hacia el fondo
    /// Resultado: una banda oscura horizontal que CRUZA físicamente la línea visible
    /// donde sustrato y fondo se encuentran, suavizando la transición en espacio de pantalla.
    /// No modifica la opacidad del sustrato → sin clipping de decoraciones.
    /// </summary>
    private void BuildFloorFadeOverlay(float hw, float floorY, float riseY)
    {
        var go = new GameObject("TankFloorFadeOverlay");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // Y sigue la pendiente del suelo en ZDecoBack y ZBack, y se extiende por encima en ZBack
        float tDecoBack  = (ZDecoBack - ZFront) / (ZBack - ZFront);
        float yDecoBack  = floorY + tDecoBack * riseY;     // superficie del suelo en ZDecoBack
        float yBackFloor = floorY + riseY;                  // superficie del suelo en ZBack
        float yBackTop   = yBackFloor + riseY * FadeOverlayHeightAbove;  // por encima del borde del suelo

        var mesh = new Mesh { name = "FloorFadeOverlay_Mesh" };

        // 9 vértices: 3 filas × 3 cols (izq, centro, der)
        mesh.vertices = new Vector3[]
        {
            new(-hw, yDecoBack,  ZDecoBack),  // 0 front-izq    (sobre el suelo, ZDecoBack)
            new(  0, yDecoBack,  ZDecoBack),  // 1 front-centro
            new( hw, yDecoBack,  ZDecoBack),  // 2 front-der
            new(-hw, yBackFloor, ZBack    ),  // 3 mid-izq      (sobre el suelo, ZBack)
            new(  0, yBackFloor, ZBack    ),  // 4 mid-centro
            new( hw, yBackFloor, ZBack    ),  // 5 mid-der
            new(-hw, yBackTop,   ZBack    ),  // 6 top-izq      (por encima del borde del suelo)
            new(  0, yBackTop,   ZBack    ),  // 7 top-centro
            new( hw, yBackTop,   ZBack    ),  // 8 top-der
        };
        mesh.triangles = new int[]
        {
            0,3,1, 1,3,4,  1,4,2, 2,4,5,   // front → mid (alpha 0 → MaxAlpha)
            3,6,4, 4,6,7,  4,7,5, 5,7,8,   // mid   → top (alpha MaxAlpha → 0)
        };
        mesh.uv = new Vector2[]
        {
            new(0, 0), new(0.5f, 0),    new(1, 0),
            new(0, 0.5f), new(0.5f, 0.5f), new(1, 0.5f),
            new(0, 1), new(0.5f, 1),    new(1, 1),
        };
        mesh.colors = new Color[]
        {
            new(0,0,0, 0),                   new(0,0,0, 0),                   new(0,0,0, 0),                    // front: transparente
            new(0,0,0, FadeOverlayMaxAlpha), new(0,0,0, FadeOverlayMaxAlpha), new(0,0,0, FadeOverlayMaxAlpha),  // mid: oscuridad máxima
            new(0,0,0, 0),                   new(0,0,0, 0),                   new(0,0,0, 0),                    // top: vuelve a transparente
        };
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;

        _floorFadeOverlayRenderer = go.AddComponent<MeshRenderer>();
        _floorFadeOverlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _floorFadeOverlayRenderer.receiveShadows    = false;
        _floorFadeOverlayRenderer.sortingOrder      = FadeOverlaySortingOrder;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        var mat = new Material(shader) { name = "FloorFadeOverlay_Mat" };
        mat.color = Color.white;
        _floorFadeOverlayRenderer.material = mat;
    }

    /// <summary>
    /// Construye el material del suelo.
    /// Primero intenta cargar una textura PNG desde Resources/Substrates/{id}.
    /// Si no existe, genera una textura procedural con ruido Perlin usando colorA/colorB.
    /// Las UVs de tiling ya están en el mesh — NO usar SetTextureScale en el material.
    /// </summary>
    private static Material BuildFloorMaterial(string id, Color colorA, Color colorB)
    {
        // SubstrateShadow: como Sprites/Default (vertex color RGB×tex + alpha fade)
        // pero también recibe sombras URP del main light (fish/decos proyectan sombra en el suelo).
        // Fallback a Sprites/Default si el shader custom no está disponible.
        Shader shader = Shader.Find("Appquarium/SubstrateShadow")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("UI/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogWarning("[DecorationPlacer] No se encontró shader para el suelo.");
            return new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
        }

        Debug.Log($"[ShadowDiag] Floor shader: '{shader.name}'");

        var mat = new Material(shader) { name = $"Floor_{id}_Mat" };
        mat.color = Color.white;  // CRÍTICO: sin esto Sprites/Default multiplica por (0,0,0,0)

        // Log de keywords del material para diagnosticar variantes stripeadas
        var kw = mat.shaderKeywords;
        Debug.Log($"[ShadowDiag] Floor mat keywords ({kw.Length}): {(kw.Length > 0 ? string.Join(", ", kw) : "ninguna")}");
        Debug.Log($"[ShadowDiag] Floor mat renderQueue={mat.renderQueue} enableInstancing={mat.enableInstancing}");

        // Intentar cargar PNG del usuario desde Resources/Substrates/
        var userTex = Resources.Load<Texture2D>($"Substrates/{id}");
        if (userTex != null)
        {
            userTex.wrapMode   = TextureWrapMode.Repeat;
            userTex.filterMode = FilterMode.Bilinear;
            if (mat.HasProperty("_BaseMap"))      mat.SetTexture("_BaseMap", userTex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", userTex);
            return mat;
        }

        // Fallback: textura procedural con ruido Perlin
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name       = $"FloorTex_{id}"
        };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                tex.SetPixel(x, y, Color.Lerp(colorA, colorB, n));
            }
        tex.Apply();

        if (mat.HasProperty("_BaseMap"))      mat.SetTexture("_BaseMap", tex);
        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

        return mat;
    }

    // ── Respawn de placeholders tras descarga PAD ─────────────────────────────

    /// <summary>
    /// Intenta sustituir las decos que spawnearon como placeholder (sin prefab ni bundle disponible)
    /// por sus modelos reales ahora que el asset pack puede estar descargado.
    /// Preserva posición, rotación, escala y flipped exactos. Llamar desde AquariumManager
    /// después de que CheckOnLaunch complete.
    /// </summary>
    public void RespawnPlaceholders()
    {
        var toRespawn = new List<PlacedDeco>();
        foreach (var pd in _placed.Values)
            if (pd.isPlaceholder) toRespawn.Add(pd);

        if (toRespawn.Count == 0) return;

        int replaced = 0;
        foreach (var pd in toRespawn)
        {
            // Intentar obtener el prefab real ahora (directo o vía bundle)
            GameObject realPrefab = pd.data.prefab;
            if (realPrefab == null
                && !string.IsNullOrEmpty(pd.data.assetBundleName)
                && !string.IsNullOrEmpty(pd.data.assetBundleAssetName))
            {
                realPrefab = AssetBundleLoader.LoadPrefab(pd.data.assetBundleName, pd.data.assetBundleAssetName);
            }

            if (realPrefab == null) continue; // pack aún no disponible, dejar el placeholder

            // La posición actual ya tiene todos los ajustes de floor-lift aplicados.
            // Llamar PlaceAt con instanceId existente y fromSave=true para reutilizar
            // el slot exacto (PlaceAt destruye el GO viejo antes de crear el nuevo).
            Vector3 currentPos  = pd.go.transform.position;
            float combinedRotY  = pd.baseRotY + pd.rotationY;
            string oldMountedOn = pd.mountedOnId; // preservar antes de que PlaceAt destruya el pd
            PlaceAt(pd.data, currentPos, pd.flipped,
                    rotationY: combinedRotY, tiltX: pd.tiltX,
                    scaleFactor: pd.scaleFactor,
                    fromSave: true, instanceId: pd.instanceId,
                    savedUserRot: pd.userRot);

            // Restaurar relación de montaje si la tenía (PlaceAt crea un nuevo PlacedDeco con mountedOnId=null)
            if (!string.IsNullOrEmpty(oldMountedOn) && _placed.ContainsKey(oldMountedOn))
                MountDecoOnTarget(pd.instanceId, oldMountedOn);

            replaced++;
        }

        if (replaced > 0)
            Debug.Log($"[DecorationPlacer] ✅ {replaced} decos reemplazadas de placeholder a modelo real.");
    }

    // ── Generación procedural de meshes placeholder ───────────────────────────

    private GameObject BuildProceduralMesh(DecorationData data, Vector3 position)
    {
        GameObject root = new($"[Deco] {data.itemName}");
        root.transform.SetParent(transform);
        root.transform.position = position;

        switch (data.category)
        {
            case DecorationCategory.Rock:    BuildRock(root, data);   break;
            case DecorationCategory.Coral:   BuildCoral(root, data);  break;
            case DecorationCategory.Plant:   BuildPlant(root, data);  break;
            case DecorationCategory.Toy:     BuildToy(root, data);    break;
            case DecorationCategory.Gadget:  BuildGadget(root, data); break;
            default:                         BuildGeneric(root);      break;
        }

        return root;
    }

    private void BuildRock(GameObject root, DecorationData data)
    {
        Color rockColor = new Color(0.38f, 0.34f, 0.30f);
        int   count     = data.isHideout ? 3 : 2;
        float baseScale = data.defaultScale.y > 0.5f ? 0.55f : 0.32f;

        for (int i = 0; i < count; i++)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(root.transform);
            float s = baseScale * Random.Range(0.7f, 1.1f);
            sphere.transform.localPosition = new Vector3(
                i * baseScale * 0.5f - baseScale * 0.25f, s * 0.3f,
                Random.Range(-0.05f, 0.05f));
            sphere.transform.localScale = new Vector3(s * 1.2f, s * 0.75f, s);
            SetColor(sphere, rockColor + new Color(Random.Range(-0.05f, 0.05f), 0, 0));
        }
    }

    private void BuildCoral(GameObject root, DecorationData data)
    {
        // Usar tintColor del SO si está definido; si no, fallback por tipo
        bool hasTint = data.tintColor != Color.white && data.tintColor.a > 0f;
        Color coralColor = hasTint ? data.tintColor
            : data.itemName.ToLower().Contains("brain")
                ? new Color(0.85f, 0.45f, 0.55f)
                : new Color(0.90f, 0.40f, 0.20f);

        int   branches = data.itemName.ToLower().Contains("branch") ? 4 : 1;
        float height   = 0.45f;

        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(trunk.GetComponent<Collider>());
        trunk.transform.SetParent(root.transform);
        trunk.transform.localPosition = new Vector3(0, height * 0.5f, 0);
        trunk.transform.localScale    = new Vector3(0.06f, height * 0.5f, 0.06f);
        SetColor(trunk, coralColor * 0.8f);

        for (int i = 0; i < branches; i++)
        {
            float angle = i * (360f / branches);
            var branch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(branch.GetComponent<Collider>());
            branch.transform.SetParent(root.transform);
            branch.transform.localPosition = new Vector3(
                Mathf.Sin(angle * Mathf.Deg2Rad) * 0.08f,
                height * 0.7f,
                Mathf.Cos(angle * Mathf.Deg2Rad) * 0.08f);
            branch.transform.localScale    = new Vector3(0.04f, 0.18f, 0.04f);
            branch.transform.localRotation = Quaternion.Euler(30f, angle, 0f);
            SetColor(branch, coralColor);

            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(branch.transform);
            tip.transform.localPosition = new Vector3(0, 1f, 0);
            var tipScale = Vector3.one * 0.6f;
            tip.transform.localScale = tipScale;
            SetColor(tip, coralColor * 1.1f);

            _tipAnims.Add(new TipAnim
            {
                tip       = tip.transform,
                baseScale = tipScale,
                phase     = Random.value * Mathf.PI * 2f,
                speed     = Random.Range(0.5f, 0.9f)
            });
        }
    }

    private void BuildPlant(GameObject root, DecorationData data)
    {
        bool  tall      = data.itemName.ToLower().Contains("tall") || data.itemName.ToLower().Contains("alta");
        float height    = tall ? 0.9f : 0.45f;
        Color stemColor = new Color(0.15f, 0.45f, 0.18f);
        Color leafColor = new Color(0.20f, 0.65f, 0.22f);

        float hueShift = Random.Range(-0.03f, 0.03f);
        stemColor += new Color(hueShift, 0f, hueShift * 0.5f);
        leafColor += new Color(hueShift, 0f, hueShift * 0.5f);

        int stems = Random.Range(2, 4);
        for (int i = 0; i < stems; i++)
        {
            float h     = height * Random.Range(0.7f, 1.0f);
            float tiltZ = Random.Range(-8f, 8f);

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(stem.GetComponent<Collider>());
            stem.transform.SetParent(root.transform);
            stem.transform.localPosition = new Vector3(
                Random.Range(-0.08f, 0.08f), h * 0.5f, Random.Range(-0.05f, 0.05f));
            stem.transform.localScale    = new Vector3(0.03f, h * 0.5f, 0.03f);
            stem.transform.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
            SetColor(stem, stemColor);

            _stemAnims.Add(new StemAnim
            {
                stem      = stem.transform,
                baseTiltZ = tiltZ,
                phase     = Random.value * Mathf.PI * 2f,
                speed     = Random.Range(0.6f, 1.3f),
                amplitude = Random.Range(4f, 9f)
            });

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(stem.transform);
            leaf.transform.localPosition = new Vector3(0, 1.1f, 0);
            leaf.transform.localScale    = new Vector3(1.5f, 0.5f, 1.5f);
            SetColor(leaf, leafColor);
        }
    }

    private void BuildToy(GameObject root, DecorationData data)
    {
        bool isChest = data.itemName.ToLower().Contains("chest") || data.itemName.ToLower().Contains("cofre");

        if (isChest)
        {
            Color chestColor = new Color(0.45f, 0.28f, 0.12f);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(root.transform);
            box.transform.localPosition = new Vector3(0, 0.12f, 0);
            box.transform.localScale    = new Vector3(0.35f, 0.22f, 0.22f);
            SetColor(box, chestColor);

            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(lid.GetComponent<Collider>());
            lid.transform.SetParent(root.transform);
            lid.transform.localPosition = new Vector3(0, 0.28f, 0);
            lid.transform.localScale    = new Vector3(0.35f, 0.10f, 0.22f);
            lid.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            SetColor(lid, chestColor * 1.1f);

            var lck = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(lck.GetComponent<Collider>());
            lck.transform.SetParent(root.transform);
            lck.transform.localPosition = new Vector3(0, 0.22f, 0.12f);
            lck.transform.localScale    = Vector3.one * 0.06f;
            SetColor(lck, new Color(0.9f, 0.75f, 0.1f));
        }
        else
        {
            Color stoneColor = new Color(0.60f, 0.58f, 0.55f);
            var base1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(base1.GetComponent<Collider>());
            base1.transform.SetParent(root.transform);
            base1.transform.localPosition = Vector3.up * 0.15f;
            base1.transform.localScale    = new Vector3(0.4f, 0.3f, 0.3f);
            SetColor(base1, stoneColor);

            var tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(tower.GetComponent<Collider>());
            tower.transform.SetParent(root.transform);
            tower.transform.localPosition = new Vector3(0.1f, 0.45f, 0);
            tower.transform.localScale    = new Vector3(0.12f, 0.22f, 0.12f);
            SetColor(tower, stoneColor * 0.9f);
        }
    }

    private void BuildGadget(GameObject root, DecorationData data)
    {
        bool isFilter = data.generatesBubbles;
        bool isHeater = data.itemName.ToLower().Contains("calent") || data.itemName.ToLower().Contains("heat");
        bool isUV     = data.preventsAlgae;

        Color gadgetColor = isFilter ? new Color(0.3f, 0.3f, 0.35f) :
                            isHeater ? new Color(0.7f, 0.2f, 0.15f) :
                            isUV     ? new Color(0.4f, 0.2f, 0.7f)  :
                                       new Color(0.4f, 0.4f, 0.4f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform);
        body.transform.localPosition = new Vector3(0, 0.2f, 0);
        body.transform.localScale    = new Vector3(0.12f, 0.38f, 0.08f);
        SetColor(body, gadgetColor);

        var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(tube.GetComponent<Collider>());
        tube.transform.SetParent(root.transform);
        tube.transform.localPosition = new Vector3(0, 0.42f, 0);
        tube.transform.localScale    = new Vector3(0.04f, 0.06f, 0.04f);
        SetColor(tube, gadgetColor * 0.7f);

        var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(led.GetComponent<Collider>());
        led.transform.SetParent(root.transform);
        led.transform.localPosition = new Vector3(0.07f, 0.3f, 0.05f);
        led.transform.localScale    = Vector3.one * 0.04f;
        SetColor(led, isUV ? new Color(0.6f, 0.4f, 1f) : new Color(0.2f, 0.9f, 0.3f));
    }

    private void BuildGeneric(GameObject root)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(root.transform);
        cube.transform.localPosition = Vector3.up * 0.15f;
        cube.transform.localScale    = Vector3.one * 0.25f;
        SetColor(cube, new Color(0.5f, 0.5f, 0.5f));
    }

    // ── Efectos de ecosistema ─────────────────────────────────────────────────

    private void RecalculateEffects()
    {
        ActiveStressReduction = 0f;
        ActiveHungerRateBonus = 0f;
        HasFilter             = false;
        HasUVLamp             = false;

        foreach (var kv in _placed)
        {
            var d = kv.Value.data;
            ActiveStressReduction += d.stressReduction;
            ActiveHungerRateBonus += d.hungerRateBonus;
            if (d.generatesBubbles) HasFilter = true;
            if (d.preventsAlgae)   HasUVLamp = true;
        }
    }

    private void ApplyGadgetSideEffects(DecorationData data)
    {
        if (data.generatesBubbles)
        {
            var bubbles = GetComponent<BubbleSystem>();
            if (bubbles != null) bubbles.SetEmissionRate(bubbles.emissionRate * 1.5f);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyTransforms(PlacedDeco pd)
    {
        if (pd.go == null) return;
        Vector3 base3 = pd.data.defaultScale != Vector3.zero ? pd.data.defaultScale : Vector3.one;

        // Escala de profundidad: más atrás (Z+) → más pequeño (perspectiva 2.5D simulada)
        float z          = pd.go.transform.position.z;
        float depthScale = Mathf.Clamp(1f - z * ZPerspectiveScale, 0.6f, 1.4f);
        float combined   = pd.scaleFactor * depthScale;

        float sx  = Mathf.Abs(base3.x) * combined * (pd.flipped ? -1f : 1f);
        float sy  = base3.y * combined;
        float sz  = base3.z * combined;

        // Cuando la deco está montada sobre otra es hija de ese GO, cuyo lossyScale
        // ya incluye el depthScale del target. Si asignamos localScale directamente,
        // Unity multiplica parent.lossyScale × child.localScale → doble escala 2.5D.
        // Compensamos dividiendo por el lossyScale del padre para que el resultado
        // en world-space sea siempre el tamaño correcto para la Z actual de la deco.
        if (pd.mountedOnId != null && pd.go.transform.parent != null)
        {
            Vector3 ps = pd.go.transform.parent.lossyScale;
            // Usamos Abs del componente del padre para preservar el signo de flip del hijo.
            if (Mathf.Abs(ps.x) > 1e-5f) sx /= Mathf.Abs(ps.x);
            if (Mathf.Abs(ps.y) > 1e-5f) sy /= Mathf.Abs(ps.y);
            if (Mathf.Abs(ps.z) > 1e-5f) sz /= Mathf.Abs(ps.z);
        }

        pd.go.transform.localScale = new Vector3(sx, sy, sz);

        // Composición de rotaciones en espacio MUNDO.
        //
        // pd.userRot: cuaternión acumulado del usuario. Cada botón de giro/tilt aplica
        //   un delta PRE-multiplicado en espacio mundo (ver ApplyUserRotDelta / ApplyUserTiltDelta):
        //     Girar:  pd.userRot = AngleAxis(delta, Vector3.up)     * pd.userRot
        //     Tilt:   pd.userRot = AngleAxis(delta, Vector3.forward) * pd.userRot
        //   La pre-multiplicación garantiza que el eje de rotación sea SIEMPRE world-space
        //   independientemente del estado acumulado — giro es siempre Y, tilt es siempre Z.
        //
        // qBase: corrección fija del prefab (GLB baseTiltX + baseRotY).
        //   worldDesiredRot = pd.userRot * qBase
        Quaternion qBase         = Quaternion.Euler(pd.baseTiltX, pd.baseRotY, pd.baseRotZ);
        // Rotación deseada en espacio MUNDO (no local).
        // Para decos montadas sobre un target con rotación no-identidad (columnas baseTiltX=-90,
        // estatuas baseTiltX=-90 baseRotY=180), compensamos la rotación del padre.
        Quaternion worldDesiredRot = pd.userRot * qBase;
        if (pd.mountedOnId != null && pd.go.transform.parent != null)
            pd.go.transform.localRotation = Quaternion.Inverse(pd.go.transform.parent.rotation) * worldDesiredRot;
        else
            pd.go.transform.localRotation = worldDesiredRot;

        // sortingOrder explícito: garantiza que el fondo nunca tape la deco.
        // Background = -10 → decos en fondo = -4, decos en frente = +4.
        // Se aplica a TODOS los Renderer (MeshRenderer + SkinnedMeshRenderer + etc.)
        // y se fuerza el sortingLayer a "Default" para que prefabs importados no
        // queden en una capa de menor prioridad que el fondo.
        // Decos montadas: +1 sobre su Z-order natural para garantizar que siempre
        // se pintan encima del target (comparten aproximadamente el mismo Z).
        int order = ZToSortingOrder(z) + (pd.mountedOnId != null ? 1 : 0);
        foreach (var r in pd.go.GetComponentsInChildren<Renderer>())
        {
            r.sortingLayerName = "Default";
            r.sortingOrder     = order;
        }
    }

    // ── Sombra de contacto ────────────────────────────────────────────────────

    private void AddShadow(PlacedDeco pd)
    {
        var container = new GameObject("Shadow_" + pd.data.itemId);
        container.transform.SetParent(transform); // sibling del deco, hijo de TankController
        pd.shadowGO = container;

        // ── Intentar sombra planar (silueta real) ──────────────────────────────
        var planarShader = Shader.Find("Appquarium/PlanarShadow");
        if (planarShader == null)
            Debug.LogWarning("[DecorationPlacer] PlanarShadow shader NOT FOUND — sin sombras de deco");
        else
            Debug.Log($"[DecorationPlacer] PlanarShadow shader: '{planarShader.name}'");
        if (planarShader != null)
        {
            pd.planarShadowPairs = new List<(MeshRenderer, GameObject, Material)>();
            float floorY = Mathf.Max(FloorSurfaceY(pd.go.transform.position.z),
                                     FloorSurfaceY(0f)) + PlanarShadowLift;

            foreach (var mr in pd.go.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr is ParticleSystemRenderer) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                // Material por shadow child (para poder actualizar _FloorY individualmente)
                var shadowMat = new Material(planarShader) { name = "PlanarShadow_Mat" };
                shadowMat.SetFloat("_FloorY", floorY);
                // Desvanecer la sombra que sube por encima del borde del suelo: ahí ya no hay
                // arena, sólo el telón del fondo, y se lee como una mancha pegada al fondo
                // (reportado por el user el 2026-08-21). Con SombraFade = 0 no cambia nada.
                shadowMat.SetFloat("_ShadowTop",  FloorTopY);
                shadowMat.SetFloat("_ShadowFade", SombraFade);
                _shadowMats.Add(shadowMat);

                var child = new GameObject("PS");
                child.transform.SetParent(container.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

                var smr = child.AddComponent<MeshRenderer>();
                // Un material por cada submesh → todos apuntan a la misma instancia
                var mats = new Material[mr.sharedMaterials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = shadowMat;
                smr.sharedMaterials  = mats;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows    = false;
                smr.sortingLayerName  = "Default";
                // sortingOrder 21: por encima del occluder (+20) para que el ZTest lo oculte
                // correctamente detrás de la geometría sólida de la deco
                smr.sortingOrder = 21;

                // Sincronizar world transform con el MeshRenderer original
                child.transform.position   = mr.transform.position;
                child.transform.rotation   = mr.transform.rotation;
                child.transform.localScale = mr.transform.lossyScale;

                pd.planarShadowPairs.Add((mr, child, shadowMat));
            }
        }

        UpdateShadow(pd);
        StartCoroutine(RefineShadowNextFrame(pd));
    }

    // SkinnedMeshRenderer reporta bounds incorrectos antes de que el Animator
    // evalúe su primer frame. Esperamos un frame y recalculamos la sombra.
    private IEnumerator RefineShadowNextFrame(PlacedDeco pd)
    {
        yield return null;
        if (pd.go != null && pd.shadowGO != null)
            UpdateShadow(pd);
    }

    // En frame 1 los bounds del MeshRenderer son fiables (GLTFast ha terminado su init).
    // Llamamos SnapBoundsToFloor para alinear la base real del mesh a la superficie del suelo,
    // igual que hace el drag-drop. Para decos SMR-only (cofre con Animator), SnapBoundsToFloor
    // hace early return (no encuentra MeshRenderer) → la posición no cambia, pivotBaseHeight intacto.
    private IEnumerator RefineFloorSnapNextFrame(PlacedDeco pd)
    {
        yield return null;
        if (pd.go == null) yield break;
        SnapBoundsToFloor(pd); // alinea base mesh a FloorSurface (SMR-only: no-op)
        UpdateShadow(pd);
    }

    private void UpdateShadow(PlacedDeco pd)
    {
        if (pd.shadowGO == null || pd.go == null) return;

        Vector3 decoPos     = pd.go.transform.position;
        float   floorSurface = Mathf.Max(FloorSurfaceY(decoPos.z), FloorSurfaceY(0f));
        float   floorY       = floorSurface + PlanarShadowLift;

        // ⚠ 2026-08-11 — NO derivar esta Y de los bounds de la deco. Probado y descartado
        // dos veces:
        //   · pegarla a bounds.min.y  ⇒ la propia deco tapa la sombra entera, invisible.
        //   · bounds.min.y - margen   ⇒ la roca tiene la base ENTERRADA (bounds hasta
        //     -3,80, por debajo del suelo en -3,13; el occluder tapa lo de abajo), así que
        //     su sombra se iba al sótano, muy separada del objeto.
        // La superficie del suelo es la referencia correcta para las dos. El grosor visible
        // lo da _Flatten en el shader, no la posición.

        if (pd.planarShadowPairs != null)
        {
            // ── Sombra planar: sincronizar world transform + actualizar _FloorY ─
            foreach (var (origMR, shadowChild, shadowMat) in pd.planarShadowPairs)
            {
                if (origMR == null || shadowChild == null) continue;

                // El MeshRenderer original se mueve con pd.go (child indirecto).
                // El shadow child es sibling (child de shadowGO → child de TankController).
                // Hay que mantenerlos sincronizados manualmente.
                shadowChild.transform.position   = origMR.transform.position;
                shadowChild.transform.rotation   = origMR.transform.rotation;
                shadowChild.transform.localScale = origMR.transform.lossyScale;

                if (shadowMat != null)
                    shadowMat.SetFloat("_FloorY", floorY);
            }
        }

        // Invalidar caché AABB para que SteeringController use la posición actualizada.
        RefreshAabb(pd);
    }

    // ── Ciclo open/close para decos animadas (cofre, etc.) ───────────────────

    // Segundos que la deco permanece en estado cerrado/abierto antes de transicionar.
    private const float AnimCycleClosedWait = 12f;
    private const float AnimCycleOpenWait   = 5f;

    /// <summary>
    /// Si el Animator tiene clips de apertura Y cierre, arranca el ciclo automático
    /// y devuelve true. Si no, devuelve false (el caller usará TryPlayLoopState).
    /// </summary>
    private bool TryStartAnimCycle(PlacedDeco pd, Animator anim)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;

        AnimationClip openClip   = null;
        AnimationClip closeClip  = null;
        AnimationClip closedIdle = null;
        AnimationClip openIdle   = null;

        foreach (var clip in anim.runtimeAnimatorController.animationClips)
        {
            string n      = clip.name.ToLower();
            bool hasOpen  = n.Contains("open");
            bool hasClose = n.Contains("clos") || n.Contains("shut");
            bool hasIdle  = n.Contains("idle") || n.Contains("loop");

            if      (hasOpen  && !hasIdle && !hasClose) openClip   = openClip   ?? clip;
            else if (hasClose && !hasIdle && !hasOpen)  closeClip  = closeClip  ?? clip;
            else if (hasIdle  && hasClose)              closedIdle = closedIdle ?? clip;
            else if (hasIdle  && hasOpen)               openIdle   = openIdle   ?? clip;
            else if (hasIdle  && !hasOpen && !hasClose) closedIdle = closedIdle ?? clip;
        }

        // Solo ciclamos si existe al menos la animación de apertura
        if (openClip == null) return false;

        anim.applyRootMotion = false;
        pd.animCycle = StartCoroutine(AnimCycle(anim, openClip, closeClip, closedIdle));
        return true;
    }

    private static IEnumerator AnimCycle(Animator anim,
        AnimationClip openClip, AnimationClip closeClip, AnimationClip closedIdle)
    {
        anim.applyRootMotion = false;

        // Helper: congelar el Animator en su pose actual (speed=0 evita auto-transiciones).
        // Se deja enabled=true para que la pose se siga renderizando.
        // El margen (0.05s antes del final del clip) evita que la transición automática
        // del controller se dispare antes de que el código llegue a congelar.
        const float FreezeMargin = 0.05f;

        // ── Fase inicial: estado CERRADO ─────────────────────────────────────
        // Jugar closedIdle y congelar inmediatamente (speed=0).
        // Así el cofre aparece cerrado desde el primer frame sin ningún flicker.
        if (closedIdle != null)
        {
            anim.Play(closedIdle.name, 0, 0f);
        }
        else
        {
            // Sin closedIdle: congelar al inicio del open clip (= posición cerrada)
            anim.Play(openClip.name, 0, 0f);
        }
        yield return null;          // dejar que el Animator aplique el Play()
        anim.speed = 0f;            // congelar: sin auto-transiciones

        while (true)
        {
            // ── CERRADO (espera) ──────────────────────────────────────────────
            yield return new WaitForSeconds(AnimCycleClosedWait);

            // ── ABRIR ─────────────────────────────────────────────────────────
            anim.speed = 1f;
            anim.Play(openClip.name, 0, 0f);
            // Esperar hasta un poco antes del final para congelar ANTES de que
            // el controller haga auto-transición a otro estado.
            yield return new WaitForSeconds(Mathf.Max(0f, openClip.length - FreezeMargin));
            anim.speed = 0f;        // congelar en pose "abierto"

            // ── ABIERTO (espera) ──────────────────────────────────────────────
            yield return new WaitForSeconds(AnimCycleOpenWait);

            // ── CERRAR ────────────────────────────────────────────────────────
            if (closeClip != null)
            {
                anim.speed = 1f;
                anim.Play(closeClip.name, 0, 0f);
                yield return new WaitForSeconds(Mathf.Max(0f, closeClip.length - FreezeMargin));
                anim.speed = 0f;    // congelar en pose "cerrado"
            }
            else
            {
                // Sin close clip: scrubbing manual del openClip en reversa.
                // Llamar anim.Play() cada frame con normalizedTime decreciente es
                // compatible con cualquier Animator, no requiere SampleAnimation
                // ni speed negativo (ninguno de los dos funciona en Unity sin Recorder).
                float duration = openClip.length;
                float elapsed  = 0f;
                while (elapsed < duration)
                {
                    float normalizedTime = 1f - elapsed / duration;
                    anim.Play(openClip.name, 0, normalizedTime);
                    yield return null;
                    elapsed += Time.deltaTime;
                }
                anim.Play(openClip.name, 0, 0f); // asegurar frame cerrado
                yield return null;
                anim.speed = 0f;
            }

            // Opcional: volver al estado closedIdle congelado para pose más limpia
            if (closedIdle != null)
            {
                anim.speed = 1f;
                anim.Play(closedIdle.name, 0, 0f);
                yield return null;
                anim.speed = 0f;
            }
        }
    }

    // ── Animación automática ──────────────────────────────────────────────────

    // Prioridad: estados cerrados/idle → si no existen, fallback a loop genérico
    private static readonly string[] LoopStateGuesses =
    {
        "Idle_Closed", "Closed_Idle", "Closed", "closed",
        "Idle",        "idle",        "Idle_Loop", "idle_loop",
        "Loop",        "loop",        "Armature|Idle", "Armature|idle"
    };

    /// <summary>
    /// Intenta reproducir el estado de loop del Animator sin configuración manual.
    /// Estrategia (en orden):
    ///   1. loopStateName del SO (si está definido y existe el estado)
    ///   2. Nombres comunes "closed/idle" (LoopStateGuesses)
    ///   3. Clips del RuntimeAnimatorController marcados como isLooping, priorizando
    ///      los que contengan "clos", "idle" o "loop" en el nombre
    ///   4. Cualquier clip disponible (último recurso)
    /// </summary>
    private static void TryPlayLoopState(Animator anim, string preferredState, float normalizedTime = 0f)
    {
        if (anim == null) return;

        // 1. Estado explícito del SO
        if (!string.IsNullOrEmpty(preferredState))
        {
            int h = Animator.StringToHash(preferredState);
            if (anim.HasState(0, h)) { anim.Play(h, 0, normalizedTime); return; }
        }

        // 2. Nombres comunes
        foreach (var name in LoopStateGuesses)
        {
            int h = Animator.StringToHash(name);
            if (anim.HasState(0, h)) { anim.Play(h, 0, normalizedTime); return; }
        }

        // 3 & 4. Leer clips del AnimatorController en runtime
        var rac = anim.runtimeAnimatorController;
        if (rac == null) return;

        AnimationClip bestLooping  = null;
        AnimationClip firstLooping = null;
        AnimationClip firstAny     = null;

        foreach (var clip in rac.animationClips)
        {
            if (firstAny == null) firstAny = clip;

            if (!clip.isLooping) continue;
            if (firstLooping == null) firstLooping = clip;

            // Priorizar clips cuyo nombre sugiere estado cerrado/idle
            string n = clip.name.ToLower();
            if (n.Contains("clos") || n.Contains("idle") || n.Contains("loop"))
            {
                bestLooping = clip;
                break;
            }
        }

        var target = bestLooping ?? firstLooping ?? firstAny;
        if (target == null) return;

        int sh = Animator.StringToHash(target.name);
        if (anim.HasState(0, sh))
            anim.Play(sh, 0, normalizedTime);
        else
            // El estado puede tener nombre distinto al clip; reproducir por hash del clip
            anim.Play(target.name, 0, normalizedTime);
    }


    private void SetColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;

        Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(s);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
        mr.material = mat;
    }

    // ── Clases internas ───────────────────────────────────────────────────────

    private class PlacedDeco
    {
        public string         instanceId;
        public GameObject     go;
        public GameObject     shadowGO;   // contenedor de sombra (sibling de go, child del TankController)
        public List<(MeshRenderer src, GameObject child, Material mat)> planarShadowPairs; // null si no hay MeshRenderers estáticos
        public DecorationData data;
        public bool           flipped;
        public float          baseRotY        = 0f;  // orientación base del prefab (no se guarda: se recalcula al cargar)
        public float          rotationY       = 0f;  // legacy: solo para migración de saves (no se actualiza en runtime)
        public float          baseTiltX       = 0f;  // corrección X del SO (no se guarda: se recalcula al cargar)
        public float          baseRotZ        = 0f;  // corrección Z del SO (no se guarda: se recalcula al cargar)
        public float          tiltX           = 0f;  // legacy: solo para migración de saves (no se actualiza en runtime)
        public Quaternion     userRot         = Quaternion.identity; // rotación acumulada usuario (espacio mundo; sustituye rotationY+tiltX)
        public float          scaleFactor     = 1f;
        public float          pivotBaseHeight = 0f;  // altura del pivot sobre el suelo Z-aware (≥0; calculado una vez al colocar)
        public Coroutine      animCycle;              // ciclo open/close (null si no aplica)
        public string         mountedOnId     = null; // instanceId del target si está montada encima; null si está en el suelo
        public bool           isPlaceholder   = false; // true si se renderizó con mesh procedural por falta de prefab/bundle
        public Bounds?        cachedAabb;             // AABB world-space para SteeringController; se invalida al mover/escalar
    }

    private class StemAnim
    {
        public Transform stem;
        public float     baseTiltZ;
        public float     phase;
        public float     speed;
        public float     amplitude;
    }

    private class TipAnim
    {
        public Transform tip;
        public Vector3   baseScale;
        public float     phase;
        public float     speed;
    }
}
