using UnityEngine;

/// <summary>
/// Finite State Machine del pez.
/// Decide en qué estado está y cuándo transicionar.
/// Estados: Idle, Explore, Flee, Feed, Sleep.
/// </summary>
public class FishBrain : MonoBehaviour
{
    // Estado actual (lectura pública)
    public FishState CurrentState { get; private set; } = FishState.Idle;

    // Target de huida o de comida (reutilizado por SteeringController)
    public Vector3 ActionTarget { get; private set; }

    [Header("Debug")]
    public bool logStateChanges = false;

    // Referencias (inyectadas por FishAgent)
    private FishData              _data;
    private NeedsModule           _needs;
    private AmbientModeController _ambient;

    // Timer del estado actual
    private float _stateTimer;

    // Etapa de vida — afecta a la duración de Idle/Explore
    private FishAgeGroup _ageGroup = FishAgeGroup.Adult;
    public void SetAgeGroup(FishAgeGroup age) => _ageGroup = age;

    // ── Inicialización ──────────────────────────────────────────────────────

    public void Initialize(FishData data, NeedsModule needs)
    {
        _data    = data;
        _needs   = needs;
        _ambient = FindFirstObjectByType<AmbientModeController>();
        FoodManager.OnFoodSpawned += OnFoodAppearedInTank;
        EnterState(FishState.Idle);
    }

    void OnDestroy()
    {
        FoodManager.OnFoodSpawned -= OnFoodAppearedInTank;
    }

    /// <summary>
    /// Reacciona cuando aparece comida en el tanque (tirada manual o AutoFeeder).
    /// Umbral de hambre muy bajo: incluso un pez no muy hambriento irá a por comida visible.
    /// </summary>
    private void OnFoodAppearedInTank()
    {
        if (CurrentState == FishState.Feed) return;
        if (_needs == null || _needs.hunger < 0.1f) return; // saciado al 100%, ignora
        CheckForFood();
    }

    // Comida objetivo actualmente asignada a este pez
    private FoodItem _targetFood;

    // ── Modificadores circadianos ────────────────────────────────────────────

    /// <summary>True si es de noche y este pez es diurno — debe descansar.</summary>
    private bool IsNightForDiurnal =>
        !_data.isNocturnal &&
        _ambient != null &&
        _ambient.CurrentMode == AmbientModeController.AmbientMode.Night;

    /// <summary>Curiosidad efectiva: los peces nocturnos se activan de noche y se aletargan de día.</summary>
    private float EffectiveCuriosity()
    {
        if (!_data.isNocturnal) return _data.curiosity;
        bool isNight = _ambient != null && _ambient.CurrentMode == AmbientModeController.AmbientMode.Night;
        return isNight
            ? Mathf.Min(1f, _data.curiosity + 0.35f)
            : Mathf.Max(0f, _data.curiosity - 0.3f);
    }

    /// <summary>Timidez efectiva: los peces nocturnos son más valientes de noche.</summary>
    private float EffectiveShyness()
    {
        if (!_data.isNocturnal) return _data.shyness;
        bool isNight = _ambient != null && _ambient.CurrentMode == AmbientModeController.AmbientMode.Night;
        return isNight
            ? Mathf.Max(0f, _data.shyness - 0.3f)
            : Mathf.Min(1f, _data.shyness + 0.2f);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    void Update()
    {
        if (_data == null) return;

        _stateTimer -= Time.deltaTime;

        // Detectar comida si tenemos hambre y no estamos ya yendo a por una
        if (CurrentState != FishState.Feed && _needs.IsHungry)
            CheckForFood();

        // Tick de necesidades según estado activo
        TickNeeds();

        // Evaluar si hay que cambiar de estado
        EvaluateTransitions();
    }

    private void CheckForFood()
    {
        if (FoodManager.Instance == null) return;

        FoodItem food = FoodManager.Instance.GetNearestFood(transform.position, _data.perceptionRadius);
        if (food != null && !food.IsClaimed)
        {
            _targetFood = food;
            FoodManager.Instance.ClaimFood(food);
            TriggerFeed(food.transform.position);
        }
    }

    // ── Transiciones ────────────────────────────────────────────────────────

    private void EvaluateTransitions()
    {
        switch (CurrentState)
        {
            case FishState.Idle:
                if (_needs.IsExhausted)
                {
                    EnterState(FishState.Sleep);
                    break;
                }
                if (_stateTimer <= 0)
                {
                    if (IsNightForDiurnal)
                        EnterState(FishState.Sleep); // de noche los diurnos descansan
                    else
                    {
                        float exploreChance = EffectiveCuriosity();
                        EnterState(Random.value < exploreChance ? FishState.Explore : FishState.Idle);
                    }
                }
                break;

            case FishState.Explore:
                if (_needs.IsExhausted || IsNightForDiurnal)
                {
                    EnterState(FishState.Idle); // → irá a Sleep en el siguiente tick
                    break;
                }
                if (_stateTimer <= 0)
                    EnterState(FishState.Idle);
                break;

            case FishState.Flee:
                if (_stateTimer <= 0)
                    EnterState(FishState.Idle);
                break;

            case FishState.Feed:
                // Actualizar destino si la comida se movió (cae)
                if (_targetFood != null)
                    ActionTarget = _targetFood.transform.position;
                else
                    EnterState(FishState.Idle); // la comida desapareció

                if (_stateTimer <= 0)
                {
                    _targetFood = null;
                    EnterState(FishState.Idle);
                }
                break;

            case FishState.Sleep:
                if (_stateTimer <= 0 || _needs.energy > 0.9f)
                    EnterState(FishState.Idle);
                break;

            case FishState.Courtship:
                if (_stateTimer <= 0 || IsNightForDiurnal)
                    EnterState(FishState.Idle);
                break;
        }
    }

    private void TickNeeds()
    {
        switch (CurrentState)
        {
            case FishState.Idle:      _needs.TickIdle();    break;
            case FishState.Explore:   _needs.TickExplore(); break;
            case FishState.Flee:      _needs.TickFlee();    break;
            case FishState.Sleep:     _needs.TickSleep();   break;
            case FishState.Courtship: _needs.TickExplore(); break;
        }
    }

    // ── Triggers externos ───────────────────────────────────────────────────

    /// <summary>Llamado cuando se detecta una amenaza cerca (cursor, depredador).</summary>
    public void TriggerFlee(Vector3 threat)
    {
        ActionTarget = threat;
        EnterState(FishState.Flee);
    }

    /// <summary>Llamado cuando aparece comida en el tanque.</summary>
    public void TriggerFeed(Vector3 foodPosition)
    {
        ActionTarget = foodPosition;
        EnterState(FishState.Feed);
    }

    /// <summary>
    /// Fuerza al pez a buscar y comer la comida más cercana al origen dado,
    /// sin comprobar si tiene hambre. Llamado desde FishAgent.ForceFeed().
    /// </summary>
    public void ForceSeekFood(Vector3 origin, float radius)
    {
        if (FoodManager.Instance == null) return;
        FoodItem food = FoodManager.Instance.GetNearestFood(origin, radius);
        if (food == null || food.IsClaimed) return;

        _targetFood = food;
        FoodManager.Instance.ClaimFood(food);
        TriggerFeed(food.transform.position);
    }

    // ── Cambio de estado ────────────────────────────────────────────────────

    private void EnterState(FishState newState)
    {
        if (logStateChanges)
            Debug.Log($"[{gameObject.name}] {CurrentState} → {newState}");

        CurrentState = newState;

        // Duración de cada estado (con varianza aleatoria según personalidad)
        switch (newState)
        {
            case FishState.Idle:
                if (IsNightForDiurnal)
                    _stateTimer = Random.Range(1f, 2.5f);
                else
                {
                    float idleBase = Random.Range(2f, 5f) * (1f + EffectiveShyness() - EffectiveCuriosity() * 0.5f);
                    // Seniors descansan más: periodos Idle hasta 1.8× más largos
                    _stateTimer = idleBase * (_ageGroup == FishAgeGroup.Senior ? 1.8f : 1f);
                }
                break;

            case FishState.Explore:
                {
                    float exploreBase = Random.Range(5f, 10f) * (0.5f + EffectiveCuriosity());
                    // Seniors se cansan antes: exploración más corta
                    _stateTimer = exploreBase * (_ageGroup == FishAgeGroup.Senior ? 0.65f : 1f);
                }
                break;

            case FishState.Flee:
                // Los tímidos huyen durante más tiempo
                _stateTimer = 1.5f + EffectiveShyness() * 3f;
                break;

            case FishState.Feed:
                _stateTimer = 8f; // Timeout de seguridad si no encuentra comida
                break;

            case FishState.Sleep:
                _stateTimer = Random.Range(10f, 20f);
                break;

            case FishState.Courtship:
                _stateTimer = Random.Range(35f, 60f);
                break;
        }
    }

    /// <summary>Inicia el cortejo. Llamado por BreedingManager cuando la pareja tiene ~2h restantes de cooldown.</summary>
    public void TriggerCourtship()
    {
        if (CurrentState == FishState.Flee ||
            CurrentState == FishState.Feed ||
            CurrentState == FishState.Sleep) return;
        EnterState(FishState.Courtship);
    }
}
