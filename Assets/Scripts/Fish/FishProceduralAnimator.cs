using UnityEngine;

/// <summary>
/// Único dueño de la rotación del pez.
/// Combina en LateUpdate: dirección de movimiento + swing + banking.
/// SteeringController solo mueve la posición; aquí se calcula toda la rotación.
/// </summary>
[RequireComponent(typeof(FishAgent))]
public class FishProceduralAnimator : MonoBehaviour
{
    [Header("Oscilación del cuerpo")]
    public float bodySwingAmplitude = 15f;
    public float idleFrequency      = 1.2f;
    public float swimFrequency      = 2.5f;
    public float fleeFrequency      = 5.0f;

    [Header("Suavidad del giro (mayor = más rápido)")]
    public float turnSmoothing = 5f;

    [Header("Inclinación al girar")]
    public float bankingAmount    = 18f;
    public float bankingSmoothing = 4f;

    private FishAgent          _agent;
    private SteeringController _steering;

    private float      _currentFrequency;
    private float      _currentBankAngle;
    private float      _phaseOffset;
    private Vector3    _previousPosition;
    private Quaternion _smoothedFacing;   // dirección de movimiento suavizada
    private float      _hungerFactor = 1f; // 1=sano, 0=muy hambriento → reduce swing

    void Awake()
    {
        _agent    = GetComponent<FishAgent>();
        _steering = GetComponent<SteeringController>();
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    void Start()
    {
        _previousPosition = transform.position;
        _smoothedFacing   = transform.rotation;
    }

    // LateUpdate: corre después de FishAgent.Update() y SteeringController
    // En este punto la posición ya está actualizada; calculamos la rotación final
    void LateUpdate()
    {
        if (_agent == null || _agent.Data == null) return;

        // ── 1. Dirección de movimiento suavizada ───────────────────────────
        Vector3 velocity = _steering.CurrentVelocity;
        // Aplanar el componente Y: los peces se mueven principalmente en horizontal.
        // Sin esto, una velocidad lenta con leve Y negativo apunta el morro hacia abajo.
        Vector3 flatVelocity = new Vector3(velocity.x, velocity.y * 0.25f, velocity.z);
        if (flatVelocity.sqrMagnitude > 0.04f)
        {
            Quaternion targetFacing = Quaternion.LookRotation(flatVelocity.normalized);
            float maxTurnDeg = _agent.CurrentState switch
            {
                FishState.Flee  => 280f,
                FishState.Feed  => 150f,
                FishState.Sleep => 20f,
                _               => 110f
            };
            _smoothedFacing = Quaternion.RotateTowards(_smoothedFacing, targetFacing, maxTurnDeg * Time.deltaTime);

            // Clamp de pitch: evita que el pez hunda la nariz en el sustrato.
            // Los peces reales no nadan en ángulos verticales pronunciados durante exploración.
            float maxPitch = _agent.CurrentState == FishState.Flee ? 35f : 20f;
            Vector3 fe    = _smoothedFacing.eulerAngles;
            float   pitch = fe.x > 180f ? fe.x - 360f : fe.x;
            if (Mathf.Abs(pitch) > maxPitch)
                _smoothedFacing = Quaternion.Euler(Mathf.Clamp(pitch, -maxPitch, maxPitch), fe.y, fe.z);
        }

        // ── 2. Frecuencia de swing según estado ────────────────────────────
        float targetFreq = _agent.CurrentState switch
        {
            FishState.Flee    => fleeFrequency,
            FishState.Explore => swimFrequency,
            FishState.Feed    => swimFrequency * 1.2f,
            FishState.Sleep   => idleFrequency  * 0.2f,
            _                 => idleFrequency
        };
        _currentFrequency = Mathf.Lerp(_currentFrequency, targetFreq, Time.deltaTime * 3f);

        // ── 3. Banking (inclinación al girar) ─────────────────────────────
        Vector3 moveDelta = transform.position - _previousPosition;
        // Usar el right del facing suavizado (sin swing ni banking) para evitar
        // el bucle de retroalimentación que causaba el espasmo visual
        Vector3 facingRight = _smoothedFacing * Vector3.right;
        float lateralDelta  = Vector3.Dot(moveDelta, facingRight);
        float targetBank    = Mathf.Clamp(-lateralDelta * bankingAmount * 8f, -bankingAmount, bankingAmount);
        _currentBankAngle   = Mathf.Lerp(_currentBankAngle, targetBank, Time.deltaTime * bankingSmoothing);
        _previousPosition   = transform.position;

        // ── 4. Swing lateral (onda seno) ───────────────────────────────────
        float swingAngle = Mathf.Sin(Time.time * _currentFrequency + _phaseOffset)
                           * bodySwingAmplitude * _hungerFactor;

        // ── 5. Rotación final = facing * swing * banking ───────────────────
        // Una sola asignación, sin acumulación entre frames
        Quaternion swingRot   = Quaternion.AngleAxis(swingAngle,       Vector3.up);
        Quaternion bankingRot = Quaternion.AngleAxis(_currentBankAngle, Vector3.forward);

        transform.rotation = _smoothedFacing * swingRot * bankingRot;
    }

    /// <summary>
    /// Ajusta la intensidad de la animación según el hambre del pez.
    /// 1 = sano (animación completa). 0.3 = muy hambriento (movimiento lánguido).
    /// Llamado por FishAgent cada frame.
    /// </summary>
    public void SetHungerFactor(float factor)
    {
        _hungerFactor = Mathf.Lerp(_hungerFactor, factor, Time.deltaTime * 2f);
    }
}
