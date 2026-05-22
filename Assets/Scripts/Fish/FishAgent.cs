using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Componente principal del pez. Integra Brain (FSM), Steering (movimiento) y Needs.
/// Es el único componente que necesitas añadir al prefab (los demás se añaden automáticamente).
/// </summary>
[RequireComponent(typeof(FishBrain))]
[RequireComponent(typeof(SteeringController))]
[RequireComponent(typeof(NeedsModule))]
public class FishAgent : MonoBehaviour
{
    // Registro global de todos los agentes activos — usado por Separate() sin colliders
    public static readonly List<FishAgent> All = new List<FishAgent>();

    [Header("Configuración de especie")]
    public FishData fishData;

    [Header("Info del individuo (generada en runtime)")]
    public string fishName;

    // Propiedades públicas de lectura
    public FishData    Data         => fishData;
    public FishState   CurrentState => _brain.CurrentState;
    public NeedsModule Needs        => _needs;
    public string      Uid          { get; private set; }
    public Vector3     Velocity     => _steering != null ? _steering.CurrentVelocity : Vector3.zero;

    // Pareja reproductiva (M/F) — para cohesión reforzada en steering
    public string PartnerUid { get; private set; }
    private FishAgent _partnerCache;

    /// <summary>Asigna el uid de instancia (desde el save).</summary>
    public void SetUid(string uid) => Uid = uid;

    /// <summary>Registra la pareja M/F de este pez (uid del otro). Nulo = sin pareja.</summary>
    public void SetPartner(string uid) { PartnerUid = uid; _partnerCache = null; }

    /// <summary>Devuelve el agente de la pareja (lazy-cached). Null si no tiene o no está en tanque.</summary>
    public FishAgent GetPartner()
    {
        if (string.IsNullOrEmpty(PartnerUid)) return null;
        if (_partnerCache == null || _partnerCache.Uid != PartnerUid)
            _partnerCache = All.Find(a => a != null && a.Uid == PartnerUid);
        return _partnerCache;
    }

    /// <summary>
    /// Conecta las parejas de breeding registradas en el save con los agentes activos en el tanque.
    /// Llamar tras hacer spawn de todos los peces, y también al añadir/quitar peces del tanque.
    /// </summary>
    public static void WirePairsFromSave(SaveData saveData)
    {
        // Limpiar parejas previas
        foreach (var a in All) if (a != null) a.SetPartner(null);

        if (saveData?.activePairs == null) return;
        foreach (var pair in saveData.activePairs)
        {
            var male   = All.Find(a => a != null && a.Uid == pair.maleUid);
            var female = All.Find(a => a != null && a.Uid == pair.femaleUid);
            // Solo vincular si AMBOS peces están en el tanque
            if (male != null && female != null)
            {
                male.SetPartner(pair.femaleUid);
                female.SetPartner(pair.maleUid);
            }
        }
    }

    /// <summary>Asigna el nombre propio del pez (desde el save).</summary>
    public void SetNickname(string nickname)
    {
        if (!string.IsNullOrEmpty(nickname))
        {
            fishName        = nickname;
            gameObject.name = $"{fishData?.speciesName} ({fishName})";
        }
    }

    // Referencias a componentes hermanos
    private FishBrain              _brain;
    private SteeringController     _steering;
    private NeedsModule            _needs;
    private FishProceduralAnimator _animator;

    // Hunger visuals
    private Renderer             _renderer;
    private MaterialPropertyBlock _mpb;
    private Color                _originalColor;
    private static readonly int  _propBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int  _propColor     = Shader.PropertyToID("_Color");

    // Pack 24 Animator — en el hijo visual (null si no hay prefab visual)
    private Animator  _visualAnimator;
    private FishState _lastFishState    = (FishState)(-1);  // sentinel para forzar 1ª transición
    private float     _targetAnimSpeed  = 1f;
    private float     _currentAnimSpeed = 1f;

    // Max speed suavizado para evitar bursts bruscos en transiciones de estado
    private float _smoothedMaxSpeed;

    // ── Inicialización ──────────────────────────────────────────────────────

    /// <summary>
    /// Inicializa el pez con sus datos de especie y los límites del tanque.
    /// Llamado por FishSpawner después de Instantiate.
    /// </summary>
    public void Initialize(FishData data, Bounds tankBounds, OwnedFishSave save = null)
    {
        fishData = data;
        fishName = GenerateName(data.speciesName);

        gameObject.name = $"{data.speciesName} ({fishName})";

        // Obtener referencias
        _brain    = GetComponent<FishBrain>();
        _steering = GetComponent<SteeringController>();
        _needs    = GetComponent<NeedsModule>();
        _animator = GetComponent<FishProceduralAnimator>();

        // Caché para hunger visuals y animator del visual hijo (Pack 24)
        _renderer      = GetComponentInChildren<Renderer>();
        _visualAnimator = GetComponentInChildren<Animator>();
        _mpb      = new MaterialPropertyBlock();
        if (_renderer != null)
        {
            var mat = _renderer.sharedMaterial;
            if      (mat == null)                       _originalColor = Color.white;
            else if (mat.HasProperty("_BaseColor"))     _originalColor = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color"))         _originalColor = mat.GetColor("_Color");
            else                                        _originalColor = Color.white;
        }

        // Configurar necesidades según especie
        _needs.hungerRate = data.hungerRate;

        // Configurar bounds del tanque en el steering
        _steering.SetTankBounds(tankBounds);
        _steering.HugsFloor = data.preferredZone == TankZone.Bottom;

        // Si hay visual animator (Pack 24), el cuerpo ya anima por huesos.
        // Desactivar el swing procedural del root para evitar doble animación.
        // FishProceduralAnimator solo gestiona facing + banking sutil.
        if (_visualAnimator != null && _animator != null)
        {
            _animator.bodySwingAmplitude = data.bodySwingOverride >= 0f ? data.bodySwingOverride : 0f;
            _animator.bankingAmount      = data.bankingAmountOverride >= 0f ? data.bankingAmountOverride : 6f;

            // Arrancar INMEDIATAMENTE en el clip de nado correcto (sin CrossFade desde
            // el estado por defecto del Animator, que varía por prefab y causaba sacudida
            // visible durante ~0.6s hasta que el blend completaba).
            _currentAnimSpeed = 0.45f;
            _targetAnimSpeed  = 0.45f;
            _visualAnimator.speed = 0.45f;
            _currentClipGroup = AnimClipGroup.Swim;
            _swimIsExploring  = false;
            string[] initSwim = { "Swim1_norm", "Swim1", "Swim ", "swim", "swimNorm" };
            foreach (string clip in initSwim)
            {
                int h = Animator.StringToHash(clip);
                if (_visualAnimator.HasState(0, h))
                {
                    // Play con offset aleatorio para que los peces no ciclen sincronizados
                    _visualAnimator.Play(h, 0, Random.Range(0f, 1f));
                    break;
                }
            }
        }

        // Overrides opcionales de animación
        if (_animator != null && data.turnSmoothingOverride >= 0f)
            _animator.turnSmoothing = data.turnSmoothingOverride;

        // Arrancar la FSM
        _brain.Initialize(data, _needs);
        _smoothedMaxSpeed = data.maxSpeed * 0.15f;  // empieza en velocidad Idle

        // Escala según tamaño de especie × factor de edad individual
        float ageScale = save != null ? SaveSystem.AgeScaleFactor(save.GetAgeGroup()) : 1f;
        transform.localScale = Vector3.one * data.baseSize * ageScale;

        // Fix 1: dirección inicial horizontal aleatoria.
        // Evita que todos los peces aparezcan orientados en la misma dirección al spawn.
        // FishProceduralAnimator.Start() leerá esta rotación para _smoothedFacing.
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Fix 2: seed por instancia para wander independiente.
        // Evita el movimiento en espejo entre peces de la misma especie.
        int rngSeed = !string.IsNullOrEmpty(save?.uid)
            ? save.uid.GetHashCode()
            : (GetInstanceID() ^ (int)(Time.time * 1000f));
        _steering.SetSeed(rngSeed);

        // Wander per-species: carácter de movimiento (errático vs. suave, corto vs. amplio).
        // 0 en FishData = mantener el default de SteeringController.
        if (data.wanderJitter   > 0f) _steering.wanderJitter   = data.wanderJitter;
        if (data.wanderRadius   > 0f) _steering.wanderRadius   = data.wanderRadius;
        if (data.wanderDistance > 0f) _steering.wanderDistance = data.wanderDistance;

        // Separación proporcional al tamaño físico: peces grandes necesitan más espacio personal.
        _steering.separationRadius = Mathf.Max(0.7f, data.baseSize * 1.2f);
    }

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ── Update ──────────────────────────────────────────────────────────────

    void Update()
    {
        if (fishData == null) return;

        // 1. Detectar cambio de estado ANTES de calcular fuerzas.
        //    Así el wander target se resetea al heading actual antes de que
        //    CalculateSteering use el nuevo peso de fuerza — evita el espasmo lateral.
        var state = _brain.CurrentState;
        if (state != _lastFishState)
        {
            _steering.OnStateEntered(state);
            OnFSMStateEntered(state);
            _lastFishState = state;
        }

        // 2. Calcular fuerza de steering según estado actual
        Vector3 force = _steering.CalculateSteering(state, _brain.ActionTarget, fishData);

        // 3. Aplicar fuerza → solo posición (rotación la gestiona FishProceduralAnimator)
        // Suavizar el max speed para evitar bursts bruscos en transiciones de estado
        _smoothedMaxSpeed = Mathf.Lerp(_smoothedMaxSpeed, GetCurrentMaxSpeed(), Time.deltaTime * 4f);
        _steering.ApplyForce(force, _smoothedMaxSpeed);

        // 4. Comprobar si hemos llegado a la comida
        if (state == FishState.Feed)
            CheckEatFood();

        // 5. Efectos visuales de hambre
        UpdateHungerVisuals();

        // 6. Suavizar la velocidad del Animator Pack 24 (evita espasmo al cambiar estado)
        if (_visualAnimator != null)
        {
            _currentAnimSpeed = Mathf.Lerp(_currentAnimSpeed, _targetAnimSpeed, Time.deltaTime * 5f);
            _visualAnimator.speed = _currentAnimSpeed;
        }

        // 7. Variedad de nado periódica: cambia de clip cada 20-40s en estado Explore
        if (_currentClipGroup == AnimClipGroup.Swim && _swimIsExploring && _visualAnimator != null)
        {
            _swimVarietyTimer -= Time.deltaTime;
            if (_swimVarietyTimer <= 0f)
            {
                _swimVarietyTimer = Random.Range(20f, 40f);
                RefreshSwimVariant();
            }
        }

        // 8. Animación de giro cuando el pez cambia de dirección bruscamente
        UpdateTurnAnimation();
    }

    private void CheckEatFood()
    {
        if (FoodManager.Instance == null) return;

        // Detectar comida por distancia al ActionTarget (no necesita collider en FoodItem)
        FoodItem nearest = FoodManager.Instance.GetNearestFood(transform.position, 0.6f);
        if (nearest != null)
        {
            Feed(0.4f);
            nearest.Consume();
        }
    }

    // ── API pública ─────────────────────────────────────────────────────────

    /// <summary>Alimentar al pez (llamado desde UI o evento de comida).</summary>
    public void Feed(float amount = 0.35f)
    {
        _needs.Feed(amount);
    }

    /// <summary>
    /// Spawna comida junto al pez y lo fuerza a ir a por ella,
    /// independientemente del nivel de hambre actual.
    /// Llamado desde FishInspectorUI al pulsar "Dar de comer".
    /// </summary>
    public void ForceFeed()
    {
        if (FoodManager.Instance == null) { _needs.Feed(0.5f); return; }

        // Spawnar comida encima del pez para que caiga visiblemente
        Vector3 spawnPos = transform.position + Vector3.up * 1.5f;
        FoodManager.Instance.SpawnFood(spawnPos);
        _brain.ForceSeekFood(spawnPos, Mathf.Max(fishData?.perceptionRadius ?? 3f, 3f) * 2f);
    }

    /// <summary>Asustar al pez (toque en pantalla, depredador, etc.).</summary>
    public void Startle(Vector3 threatPosition)
    {
        _brain.TriggerFlee(threatPosition);
    }

    // ── Animator Pack 24 ────────────────────────────────────────────────────
    //
    // Idle y Explore usan el MISMO grupo (Swim) — solo cambia la velocidad.
    // Esto evita el CrossFade Idle↔Explore que causaba espasmo visual.
    // Feed tiene grupo propio (Eat) → usa Eat_Swim / Eat_Ground del pack.
    // Solo se hace CrossFade cuando cambia el grupo real: Swim↔Eat↔Flee↔Rest.
    //
    // Clips por especie:
    //   22-clip angelfish : Swim1_norm · Swim2_Fast · Swim3_Long_Wide · Swim4_Long_Near
    //                       Eat_Swim · Eat_Ground · Eat_Wall · Idle
    //                       Turn_L_* · Turn_R_* · Attack · Death* · Jump
    //   Moorish Idol      : "Swim " (trailing space) · Idle · Eat Swim · Turn Left/Right
    //   Mandarinfish      : swim · idle · eatSwim · eatGround · turn_L · turn_R
    //   Emperor Angelfish : Swim1 · Swim2 · Eat_Swim · Eat_Ground · Idle
    //   1-clip fish       : swimNorm (loop único)

    private enum AnimClipGroup { None, Swim, Eat, Flee, Rest }
    private AnimClipGroup _currentClipGroup  = AnimClipGroup.None;
    private bool          _swimIsExploring;
    private float         _swimVarietyTimer;

    // Turn animation detection — purely visual layer, no effect on steering
    private Vector3 _prevSampledDir   = Vector3.forward;
    private float   _dirSampleTimer   = 0f;
    private float   _turnCooldown     = 0f;
    private float   _turnAnimEnd      = 0f;   // Time.time when current turn clip should finish

    private void OnFSMStateEntered(FishState state)
    {
        if (_visualAnimator == null) return;

        AnimClipGroup targetGroup;
        float         speed;

        switch (state)
        {
            case FishState.Idle:
                targetGroup      = AnimClipGroup.Swim;
                speed            = 0.45f;
                _swimIsExploring = false;
                break;
            case FishState.Explore:
                targetGroup      = AnimClipGroup.Swim;
                speed            = 1.0f;
                _swimIsExploring = true;
                break;
            case FishState.Feed:
                targetGroup = AnimClipGroup.Eat;  speed = 0.85f; break;
            case FishState.Flee:
                targetGroup = AnimClipGroup.Flee; speed = 1.6f;  break;
            case FishState.Sleep:
                targetGroup = AnimClipGroup.Rest; speed = 0.18f; break;
            default: return;
        }

        // Solo CrossFade al cambiar de grupo (Idle↔Explore = mismo Swim → no CrossFade)
        _targetAnimSpeed = speed;
        if (targetGroup == _currentClipGroup) return;
        _currentClipGroup = targetGroup;

        string[] candidates;
        float    blend;

        switch (targetGroup)
        {
            case AnimClipGroup.Swim:
                candidates = BuildSwimCandidates(_swimIsExploring);
                blend = 0.6f;
                _swimVarietyTimer = Random.Range(20f, 40f);
                break;
            case AnimClipGroup.Eat:
                // Usar clip de comer real del pack — fallback a nado si no existe
                candidates = new[] { "Eat_Swim", "Eat_Ground", "Eat Ground", "eatSwim", "eatGround",
                                     "Swim1_norm", "Swim1", "Swim ", "swim", "swimNorm" };
                blend = 0.3f;
                break;
            case AnimClipGroup.Flee:
                candidates = new[] { "Swim2_Fast", "Swim2", "Swim1_norm", "Swim ", "swim", "swimNorm" };
                blend = 0.08f;
                break;
            case AnimClipGroup.Rest:
                candidates = new[] { "Idle", "idle", "swimNorm" };
                blend = 0.9f;
                break;
            default: return;
        }

        CrossFadeFirst(candidates, blend);
    }

    // Construye la lista de candidatos de nado.
    // En Explore: orden aleatorio entre Swim1_norm/Swim3_Long_Wide/Swim4_Long_Near para variedad.
    // En Idle: siempre Swim1_norm (calmo, sin saltos visuales bruscos).
    private string[] BuildSwimCandidates(bool exploring)
    {
        if (!exploring)
            return new[] { "Swim1_norm", "Swim1", "Swim ", "swim", "swimNorm" };

        // Barajar los 3 clips de nado para variedad en cada entrada al estado Explore
        string[] variants = { "Swim1_norm", "Swim3_Long_Wide", "Swim4_Long_Near" };
        int r = Random.Range(0, variants.Length);
        (variants[0], variants[r]) = (variants[r], variants[0]);
        return new[] { variants[0], variants[1], variants[2], "Swim1", "Swim ", "swim", "swimNorm" };
    }

    // CrossFade al primer estado que exista en el Animator (por nombre → hash)
    private void CrossFadeFirst(string[] candidates, float blend)
    {
        foreach (string clip in candidates)
        {
            int hash = Animator.StringToHash(clip);
            if (_visualAnimator.HasState(0, hash))
            {
                _visualAnimator.CrossFade(hash, blend, 0);
                return;
            }
        }
    }

    // Cambia a un clip de nado diferente — llamado periódicamente en Explore para variedad
    private void RefreshSwimVariant()
    {
        if (_visualAnimator == null || _currentClipGroup != AnimClipGroup.Swim || !_swimIsExploring) return;
        CrossFadeFirst(BuildSwimCandidates(true), 0.8f);
    }

    // Detecta giros bruscos y reproduce Turn_L_Fast / Turn_R_Fast.
    // Capa puramente visual: no toca steering ni FSM.
    // Salvaguardas anti-vibración:
    //   · Solo actúa en grupo Swim (no en Flee/Eat/Rest)
    //   · Muestrea la dirección cada 0.25s (filtra el noise de steering frame a frame)
    //   · Umbral alto (65°) para detectar solo giros reales, no correcciones leves
    //   · Cooldown de 2.5s para evitar spamming
    //   · Velocidad mínima: ignora el pez casi parado (Idle)
    private void UpdateTurnAnimation()
    {
        if (_visualAnimator == null || _currentClipGroup != AnimClipGroup.Swim) return;

        Vector3 vel = _steering != null ? _steering.CurrentVelocity : Vector3.zero;
        float speedSq = vel.x * vel.x + vel.z * vel.z;
        if (speedSq < 0.04f) return;   // demasiado lento — Idle o casi parado

        _turnCooldown -= Time.deltaTime;

        // Cuando el clip de giro acaba, volver al nado normal
        if (_turnAnimEnd > 0f && Time.time >= _turnAnimEnd)
        {
            _turnAnimEnd = 0f;
            CrossFadeFirst(BuildSwimCandidates(_swimIsExploring), 0.4f);
        }

        // Muestreo periódico: comparar dirección actual vs hace 0.25s
        _dirSampleTimer -= Time.deltaTime;
        if (_dirSampleTimer > 0f) return;
        _dirSampleTimer = 0.25f;

        Vector3 curDir = new Vector3(vel.x, 0f, vel.z).normalized;

        if (_turnAnimEnd <= 0f && _turnCooldown <= 0f)
        {
            float angle = Vector3.SignedAngle(_prevSampledDir, curDir, Vector3.up);
            if (Mathf.Abs(angle) > 75f)
            {
                bool left = angle < 0f;
                string[] clips = left
                    ? new[] { "Turn_L_Fast", "turn_L", "Turn Left" }
                    : new[] { "Turn_R_Fast", "turn_R", "Turn Right" };
                CrossFadeFirst(clips, 0.1f);
                _turnAnimEnd  = Time.time + 0.55f;  // duración estimada del clip
                _turnCooldown = 4.5f;               // cooldown más largo: evita giros consecutivos
            }
        }

        _prevSampledDir = curDir;
    }

    // ── Hunger visuals ───────────────────────────────────────────────────────

    private void UpdateHungerVisuals()
    {
        if (!AppFlags.EnableNeglectVisuals || _needs == null) return;

        // Factor: 1.0 cuando hunger < 0.5; cae hasta 0.3 cuando hunger = 1.0
        float factor = _needs.hunger < 0.5f
            ? 1f
            : Mathf.Lerp(1f, 0.3f, (_needs.hunger - 0.5f) / 0.5f);

        _animator?.SetHungerFactor(factor);

        // Desaturar color del renderer (si existe)
        if (_renderer == null) return;
        float t = Mathf.Clamp01((_needs.hunger - 0.5f) / 0.5f);
        Color target = Color.Lerp(_originalColor, new Color(0.55f, 0.58f, 0.62f), t * 0.55f);
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_propBaseColor, target);
        _mpb.SetColor(_propColor,     target);
        _renderer.SetPropertyBlock(_mpb);
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    private float GetCurrentMaxSpeed()
    {
        float base_ = _brain.CurrentState switch
        {
            FishState.Flee    => fishData.maxSpeed * 2.5f,
            FishState.Idle    => fishData.maxSpeed * 0.15f,
            FishState.Sleep   => 0f,
            FishState.Explore => fishData.maxSpeed,
            FishState.Feed    => fishData.maxSpeed * 1.5f,
            _                 => fishData.maxSpeed
        };

        // Pez hambriento se mueve más despacio (excepto cuando huye o come)
        if (AppFlags.EnableNeglectVisuals &&
            _needs != null && _needs.hunger > 0.5f &&
            _brain.CurrentState != FishState.Flee &&
            _brain.CurrentState != FishState.Feed)
        {
            float hungerPenalty = Mathf.Lerp(1f, 0.45f, (_needs.hunger - 0.5f) / 0.5f);
            base_ *= hungerPenalty;
        }

        return base_ * (AquariumManager.Instance != null ? AquariumManager.Instance.FishSpeedMultiplier : 1f);
    }

    private static readonly string[] Names =
    {
        "Nemo", "Dory", "Bubbles", "Splash", "Finn", "Coral",
        "Wave", "Tide", "Pebble", "Ripple", "Azul", "Coral",
        "Marina", "Rocco", "Zara", "Mako"
    };

    private string GenerateName(string species)
    {
        return Names[Random.Range(0, Names.Length)];
    }
}
