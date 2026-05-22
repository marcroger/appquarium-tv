using UnityEngine;

/// <summary>
/// Motor de movimiento procedural basado en Steering Behaviors.
/// No usa animaciones: el movimiento es 100% matemático.
/// Behaviors activos: Wander, Wall Avoidance, Flee, Seek.
/// </summary>
public class SteeringController : MonoBehaviour
{
    [Header("Wander")]
    public float wanderRadius   = 1.5f;   // Radio de la esfera de wander
    public float wanderDistance = 2.5f;   // Distancia de proyección del target
    public float wanderJitter   = 0.8f;   // Aleatoriedad por frame (más = menos fluido)

    [Header("Wall Avoidance")]
    public float wallDetectionDistance = 1.5f;
    public float wallAvoidanceWeight   = 4f;

    [Header("Separation (Boids)")]
    public float separationRadius = 1.2f;   // distancia mínima entre peces
    public float separationWeight = 2.5f;   // fuerza de repulsión

    [Header("Debug")]
    public bool drawGizmos = true;

    // ── Perspectiva 2.5D ─────────────────────────────────────────────────────
    // Mismas constantes que DecorationPlacer para que peces y decos coincidan.
    //   ZPerspectiveY: suelo/techo se aproximan 0.45 u.m. por cada unidad de Z al fondo.
    //   FishZHalfRange: los peces solo nadan en Z [-1, +1], igual que las decoraciones.
    //   FishMargin: mínimo de espacio entre el centro del pez y el borde de la cámara.
    private const float ZPerspectiveY  = 0.45f;
    private const float FishZHalfRange = 2.4f;
    private const float FishMargin     = 0.4f;
    private const float FloorHugMargin = 0.08f; // margen reducido para peces de fondo

    /// <summary>
    /// Si es true, el pez permanece pegado al suelo (peces bentónicos: gobios, mandarines).
    /// Lo activa FishAgent.Initialize según preferredZone == Bottom.
    /// </summary>
    public bool HugsFloor { get; set; }

    // Fracción de la altura total del mesh que cae por debajo del pivot.
    // 0.5 = pivot al centro (default). Valores > 0.5 = pivot más cerca del techo del mesh.
    // Se mide en FishSpawner con abs(sharedMesh.bounds.min.y) / meshHeight y se almacena aquí.
    private float _pivotToBottomRatio = 0.5f;

    /// <summary>
    /// Llamado por FishSpawner tras normalizar el visual. Fija la fracción real del mesh
    /// que está por debajo del pivot para que FloorMargin no cause ni clipping ni exceso.
    /// </summary>
    public void SetPivotToBottomRatio(float ratio) =>
        _pivotToBottomRatio = Mathf.Clamp(ratio, 0.2f, 1.1f);

    // Margen de suelo: distancia mínima entre el pivot del pez y el suelo perspectivo.
    // = baseSize × (fracción del mesh bajo el pivot) + buffer de seguridad.
    // El ratio real se mide en FishSpawner; el default 0.5 (pivot centrado) es conservador.
    private float FloorMargin => transform.localScale.x * _pivotToBottomRatio + (HugsFloor ? 0.05f : 0.15f);

    // Estado interno
    private Vector3      _velocity      = Vector3.zero;
    private Vector3      _wanderTarget  = Vector3.forward;
    private Vector3      _schoolOffset  = Vector3.zero;  // posición preferida dentro del cardumen
    private Bounds       _tankBounds;
    private System.Random _rng          = new System.Random(); // seed por instancia (ver SetSeed)

    // Referencia lazy a DecorationPlacer para evitar obstáculos colocados
    private DecorationPlacer _decoPlacer;

    /// <summary>
    /// Configura los límites del tanque. Llamado por FishAgent al inicializar.
    /// </summary>
    public void SetTankBounds(Bounds bounds)
    {
        _tankBounds = bounds;
    }

    /// <summary>
    /// Seed por instancia para el wander. Evita el efecto espejo entre peces de la misma especie.
    /// También randomiza el _wanderTarget inicial para que cada pez empiece en una dirección distinta.
    /// </summary>
    public void SetSeed(int seed)
    {
        _rng = new System.Random(seed);
        // Fase inicial del wander target aleatoria por instancia
        float angle = (float)(_rng.NextDouble() * Mathf.PI * 2f);
        _wanderTarget = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * wanderRadius;
        // Offset personal dentro del cardumen: cada pez quiere estar en una posición
        // ligeramente distinta del centro de masa, rompiendo el efecto espejo.
        float ox = (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.8f;
        float oy = (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.35f;
        _schoolOffset = new Vector3(ox, oy, 0f);
    }

    /// <summary>
    /// Calcula la fuerza de steering según el estado de la FSM.
    /// Llamado por FishAgent en Update().
    /// </summary>
    public Vector3 CalculateSteering(FishState state, Vector3 fleeTarget, FishData data)
    {
        Vector3 force = Vector3.zero;

        switch (state)
        {
            case FishState.Idle:
                force += Wander()                                             * 0.15f;
                force += AvoidWalls()                                         * wallAvoidanceWeight;
                force += Separate(data)                                       * separationWeight;
                force += Cohesion(data)                                       * data.schoolingStrength * 0.4f;
                force += Alignment(data)                                      * data.schoolingStrength * 0.15f;
                force += PairBond()                                           * 1.8f;
                force += ZoneBias(data)                                       * data.zoneAttraction;
                force += AvoidObstacles()                                     * 5f;
                break;

            case FishState.Explore:
                force += Wander()                                             * 1f;
                force += AvoidWalls()                                         * wallAvoidanceWeight;
                force += Separate(data)                                       * separationWeight;
                force += Cohesion(data)                                       * data.schoolingStrength * 0.8f;
                force += Alignment(data)                                      * data.schoolingStrength * 0.28f;
                force += PairBond()                                           * 1.2f;
                force += ZoneBias(data)                                       * (data.zoneAttraction * 0.4f);
                force += AvoidObstacles()                                     * 5f;
                break;

            case FishState.Flee:
                force += Flee(fleeTarget)  * 3f;
                force += AvoidWalls()      * wallAvoidanceWeight;
                force += AvoidObstacles()  * 4f;
                break;

            case FishState.Feed:
                force += Seek(fleeTarget) * 2f;
                force += AvoidWalls()     * wallAvoidanceWeight;
                force += Separate(data)   * (separationWeight * 0.5f);
                force += AvoidObstacles() * 2f;
                break;

            case FishState.Sleep:
                _velocity = Vector3.Lerp(_velocity, Vector3.zero, Time.deltaTime * 2f);
                return Vector3.zero;
        }

        return force;
    }

    /// <summary>Velocidad actual del pez. Usada por FishProceduralAnimator para calcular la rotación.</summary>
    public Vector3 CurrentVelocity => _velocity;

    /// <summary>
    /// Llamado por FishAgent al detectar cambio de estado FSM.
    /// Reinicia el wander target al heading actual para que el nuevo estado
    /// no arranque con una dirección aleatoria acumulada del estado anterior.
    /// </summary>
    public void OnStateEntered(FishState newState)
    {
        _wanderTarget = (transform.forward.sqrMagnitude > 0.01f
            ? transform.forward.normalized
            : Vector3.right) * wanderRadius;
    }

    /// <summary>
    /// Aplica la fuerza al objeto: solo mueve la posición.
    /// La rotación la gestiona FishProceduralAnimator en LateUpdate para evitar conflictos.
    /// </summary>
    public void ApplyForce(Vector3 force, float maxSpeed)
    {
        _velocity += force * Time.deltaTime;
        _velocity  = Vector3.ClampMagnitude(_velocity, maxSpeed);

        if (_velocity.sqrMagnitude > 0.001f)
            transform.position += _velocity * Time.deltaTime;

        // Clamp final: el pez no puede salir de los bounds.
        //   • Z restringido a [-FishZHalfRange, +FishZHalfRange] (misma franja que decos).
        //   • Suelo y techo con perspectiva 2.5D: al fondo (Z+) el espacio vertical se estrecha.
        //   • FishMargin evita que el centro del pez quede pegado al borde de pantalla.
        float z = transform.position.z;
        float fishMinZ = Mathf.Max(_tankBounds.min.z, -FishZHalfRange);
        float fishMaxZ = Mathf.Min(_tankBounds.max.z, +FishZHalfRange);
        float floorY    = PerspectiveFloorY(z);
        float ceilY     = PerspectiveCeilY(z);
        float yMargin   = FloorMargin;
        transform.position = new Vector3(
            Mathf.Clamp(transform.position.x, _tankBounds.min.x + FishMargin, _tankBounds.max.x - FishMargin),
            Mathf.Clamp(transform.position.y, floorY + yMargin,               ceilY  - FishMargin),
            Mathf.Clamp(transform.position.z, fishMinZ + 0.1f,                fishMaxZ - 0.1f)
        );

        // Hard push-out: saca el pez si ya está solapando una decoración.
        // Se hace DESPUÉS del clamp de paredes para no acumular correcciones contradictorias.
        EnsureDecoPlacer();
        PushOutOfObstacles();
    }

    // ── Behaviors individuales ──────────────────────────────────────────────

    private float Rng11() => (float)(_rng.NextDouble() * 2.0 - 1.0);

    private Vector3 Wander()
    {
        // Jitter usando _rng per-instancia — evita el movimiento en espejo entre clones
        _wanderTarget += new Vector3(
            Rng11() * wanderJitter * Time.deltaTime,
            Rng11() * wanderJitter * Time.deltaTime,
            Rng11() * wanderJitter * Time.deltaTime
        );
        _wanderTarget = _wanderTarget.normalized * wanderRadius;

        // Proyectar el target en el espacio mundial delante del pez
        Vector3 targetWorld = transform.position
                              + transform.forward * wanderDistance
                              + _wanderTarget;

        return (targetWorld - transform.position).normalized;
    }

    private Vector3 AvoidWalls()
    {
        Vector3 avoidance = Vector3.zero;
        Vector3 pos       = transform.position;
        float   d         = wallDetectionDistance;

        // Por cada pared, si estamos demasiado cerca empujamos en la dirección opuesta
        // La fuerza aumenta cuanto más cerca estamos (inversa proporcional)
        float fishMinZ = Mathf.Max(_tankBounds.min.z, -FishZHalfRange);
        float fishMaxZ = Mathf.Min(_tankBounds.max.z, +FishZHalfRange);

        float leftDist   = pos.x - _tankBounds.min.x;
        float rightDist  = _tankBounds.max.x - pos.x;
        // suelo: medir desde el límite real del clamp (floorY + FloorMargin) para que
        // la fuerza de repulsión empiece antes cuando el pez es grande.
        float floorLimit = PerspectiveFloorY(pos.z) + FloorMargin;
        float bottomDist = pos.y - floorLimit;
        float topDist    = PerspectiveCeilY(pos.z) - pos.y;
        float frontDist  = pos.z - fishMinZ;
        float backDist   = fishMaxZ - pos.z;

        if (leftDist   < d) avoidance += Vector3.right   * (1f - leftDist   / d);
        if (rightDist  < d) avoidance += Vector3.left    * (1f - rightDist  / d);
        if (bottomDist < d) avoidance += Vector3.up      * (1f - bottomDist / d);
        if (topDist    < d) avoidance += Vector3.down    * (1f - topDist    / d);
        if (frontDist  < d) avoidance += Vector3.forward * (1f - frontDist  / d);
        if (backDist   < d) avoidance += Vector3.back    * (1f - backDist   / d);

        return avoidance;
    }

    private Vector3 Flee(Vector3 threat)
    {
        return (transform.position - threat).normalized;
    }

    private Vector3 Seek(Vector3 target)
    {
        return (target - transform.position).normalized;
    }

    /// <summary>
    /// Fuerza vertical que atrae al pez hacia su zona favorita del tanque.
    /// Surface = 20% superior | MidWater = 60% central | Bottom = 20% inferior.
    /// </summary>
    private Vector3 ZoneBias(FishData data)
    {
        if (data == null || data.preferredZone == TankZone.Anywhere) return Vector3.zero;
        if (_tankBounds.size.sqrMagnitude < 0.01f) return Vector3.zero;

        float tankH   = _tankBounds.size.y;
        float minY    = _tankBounds.min.y;
        float currentY = transform.position.y;

        // Calcular el centro Y de la zona preferida.
        // Bottom usa el suelo perspectivo real a la Z actual + margen pequeño,
        // de modo que la fuerza siempre empuje hacia el suelo visible, no un % fijo.
        float targetY = data.preferredZone switch
        {
            TankZone.Surface => minY + tankH * 0.88f,
            TankZone.Bottom  => PerspectiveFloorY(transform.position.z) + FloorMargin,
            _                => minY + tankH * 0.50f,
        };

        float diff = targetY - currentY;

        // Peces bentónicos: aplicar siempre la fuerza (deadzone muy pequeña)
        // Resto: solo si está lejos de la zona (>15% del alto)
        float deadZone = HugsFloor ? FloorHugMargin * 2f : tankH * 0.15f;
        if (Mathf.Abs(diff) < deadZone) return Vector3.zero;

        return Vector3.up * Mathf.Sign(diff);
    }

    /// <summary>
    /// Separación entre peces. Tiene en cuenta compatibilidad de especie:
    /// rivales se repelen más, especies amigas pueden acercarse más.
    /// </summary>
    private Vector3 Separate(FishData data)
    {
        Vector3 steer = Vector3.zero;
        int     count = 0;

        foreach (var other in FishAgent.All)
        {
            if (other == null || other.gameObject == gameObject) continue;

            Vector3 diff = transform.position - other.transform.position;
            float   dist = diff.magnitude;

            float radius = separationRadius;
            if (data != null && other.Data != null)
            {
                string otherId = other.Data.itemId;
                if (System.Array.IndexOf(data.rivalSpecies,   otherId) >= 0) radius *= 2.2f;
                else if (System.Array.IndexOf(data.friendlySpecies, otherId) >= 0) radius *= 0.5f;
            }

            if (dist > 0f && dist < radius)
            {
                steer += diff.normalized / dist;
                count++;
            }
        }

        if (count > 0) steer /= count;
        return steer;
    }

    /// <summary>
    /// Cohesión de cardumen: mueve al pez hacia el centro de masa de su especie.
    /// Solo actúa si schoolingStrength > 0.
    /// </summary>
    private Vector3 Cohesion(FishData data)
    {
        if (data == null || data.schoolingStrength < 0.01f) return Vector3.zero;

        Vector3 center = Vector3.zero;
        int     count  = 0;
        float   radius = data.perceptionRadius;

        foreach (var other in FishAgent.All)
        {
            if (other == null || other.gameObject == gameObject) continue;
            if (other.Data == null || other.Data.itemId != data.itemId) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < radius)
            {
                center += other.transform.position;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        center /= count;
        return (center + _schoolOffset - transform.position).normalized;
    }

    /// <summary>
    /// Alineación de cardumen: iguala la dirección de movimiento con la de su especie.
    /// Solo actúa si schoolingStrength > 0.
    /// </summary>
    private Vector3 Alignment(FishData data)
    {
        if (data == null || data.schoolingStrength < 0.01f) return Vector3.zero;

        Vector3 avgVel = Vector3.zero;
        int     count  = 0;
        float   radius = data.perceptionRadius;

        foreach (var other in FishAgent.All)
        {
            if (other == null || other.gameObject == gameObject) continue;
            if (other.Data == null || other.Data.itemId != data.itemId) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < radius)
            {
                avgVel += other.Velocity;
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        avgVel /= count;
        return avgVel.sqrMagnitude > 0.001f ? avgVel.normalized : Vector3.zero;
    }

    private Vector3 PairBond()
    {
        var agent = GetComponent<FishAgent>();
        if (agent == null) return Vector3.zero;
        var partner = agent.GetPartner();
        if (partner == null) return Vector3.zero;
        float dist = Vector3.Distance(transform.position, partner.transform.position);
        const float desiredDist = 1.5f;
        if (dist > desiredDist)
            return (partner.transform.position - transform.position).normalized;
        return Vector3.zero;
    }

    // ── Perspectiva 2.5D ────────────────────────────────────────────────────

    /// <summary>Suelo perspectivo: al fondo (Z+) el suelo sube en pantalla.
    /// La malla del suelo empieza en ZFront (no en z=0), por lo que el offset correcto es (z - ZFront).</summary>
    private float PerspectiveFloorY(float z)
        => _tankBounds.min.y + Mathf.Max(0f, z - DecorationPlacer.ZFront) * ZPerspectiveY;

    /// <summary>Techo perspectivo: al fondo (Z+) el techo baja en pantalla.
    /// El espacio vertical se estrecha simétricamente, simulando el trapecio de perspectiva.</summary>
    private float PerspectiveCeilY(float z)
        => _tankBounds.max.y - Mathf.Max(0f, z) * ZPerspectiveY;

    // ── Evitación de decoraciones ────────────────────────────────────────────

    private void EnsureDecoPlacer()
    {
        if (_decoPlacer == null)
            _decoPlacer = FindFirstObjectByType<DecorationPlacer>();
    }

    // Margen en Z: si el pez está más de este valor fuera del rango Z de la deco,
    // puede pasar libremente por delante o por detrás sin ser bloqueado.
    private const float ZPassThroughMargin = 0.45f;

    /// <summary>
    /// Fuerza de repulsión SUAVE con look-ahead 0.3s.
    /// Solo actúa si el pez está en una profundidad Z similar a la decoración.
    /// </summary>
    private Vector3 AvoidObstacles()
    {
        EnsureDecoPlacer();
        if (_decoPlacer == null) return Vector3.zero;

        const float lookMargin = 1.2f;
        Vector3 futurePos = transform.position + _velocity * 0.4f;
        Vector3 steer     = Vector3.zero;

        foreach (var (_, aabb) in _decoPlacer.GetPlacedObstacleData())
        {
            // Pez en capa Z diferente → puede pasar por delante/detrás sin obstáculo
            if (Mathf.Abs(futurePos.z - aabb.center.z) > aabb.extents.z + ZPassThroughMargin)
                continue;

            float dist = AabbDistXY(aabb, futurePos);
            if (dist < lookMargin)
            {
                // Dirección de huida: desde el punto más cercano de la superficie del AABB.
                // Esto empuja al pez en la dirección óptima (lateral si está al lado,
                // hacia arriba si está encima) en lugar de siempre desde el centro.
                float nearX = Mathf.Clamp(futurePos.x, aabb.min.x, aabb.max.x);
                float nearY = Mathf.Clamp(futurePos.y, aabb.min.y, aabb.max.y);
                Vector3 away = new Vector3(futurePos.x - nearX, futurePos.y - nearY, 0f);
                // Si el pez ya está dentro del AABB (dist=0), huir desde el centro
                if (away.sqrMagnitude < 0.001f)
                    away = new Vector3(futurePos.x - aabb.center.x, futurePos.y - aabb.center.y, 0f);
                if (away.sqrMagnitude > 0.001f)
                    steer += away.normalized * (1f - dist / lookMargin);
            }
        }
        return steer;
    }

    /// <summary>
    /// Empuje DURO en XY con MTV. Solo actúa si el pez está en la misma capa Z.
    /// El push se limita por frame para evitar teleportaciones.
    /// </summary>
    private void PushOutOfObstacles()
    {
        if (_decoPlacer == null) return;

        const float clearance = 0.15f;

        foreach (var (_, aabb) in _decoPlacer.GetPlacedObstacleData())
        {
            if (Mathf.Abs(transform.position.z - aabb.center.z) > aabb.extents.z + ZPassThroughMargin)
                continue;

            if (AabbPushOut2D(aabb, transform.position, clearance, out Vector3 push))
            {
                // Limitar push por frame para evitar teleportaciones;
                // el pez saldrá suavemente en varios frames si la penetración es grande.
                push = Vector3.ClampMagnitude(push, 0.07f);
                transform.position += push;

                // Cancelar completamente la velocidad hacia la deco
                // para que no reentren en el siguiente frame
                if (push.sqrMagnitude > 0.0001f)
                {
                    Vector3 pushDir = push.normalized;
                    float   towards = Vector3.Dot(_velocity, -pushDir);
                    if (towards > 0f) _velocity += pushDir * towards;
                }
            }
        }
    }

    // ── Helpers AABB 2D ──────────────────────────────────────────────────────

    /// <summary>
    /// Distancia en XY desde el punto hasta el exterior del AABB (0 si está dentro).
    /// </summary>
    private static float AabbDistXY(Bounds b, Vector3 point)
    {
        float dx = Mathf.Max(0f, Mathf.Abs(point.x - b.center.x) - b.extents.x);
        float dy = Mathf.Max(0f, Mathf.Abs(point.y - b.center.y) - b.extents.y);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// MTV (minimum translation vector) en XY para expulsar el punto del AABB expandido.
    /// Devuelve true y rellena push si hay solapamiento; false si el punto está fuera.
    /// </summary>
    private static bool AabbPushOut2D(Bounds b, Vector3 point, float margin, out Vector3 push)
    {
        push = Vector3.zero;
        float halfW = b.extents.x + margin;
        float halfH = b.extents.y + margin;
        float dx    = point.x - b.center.x;
        float dy    = point.y - b.center.y;

        if (Mathf.Abs(dx) >= halfW || Mathf.Abs(dy) >= halfH)
            return false; // fuera del AABB expandido

        float overlapX = halfW - Mathf.Abs(dx);
        float overlapY = halfH - Mathf.Abs(dy);

        // Empujar por el eje de menor penetración
        if (overlapX < overlapY)
            push = Vector3.right * Mathf.Sign(dx) * overlapX;
        else
            push = Vector3.up   * Mathf.Sign(dy) * overlapY;

        return true;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Visualizar velocidad
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + _velocity);

        // Visualizar wander target
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Vector3 targetWorld = transform.position + transform.forward * wanderDistance + _wanderTarget;
            Gizmos.DrawSphere(targetWorld, 0.1f);
        }
    }
}
