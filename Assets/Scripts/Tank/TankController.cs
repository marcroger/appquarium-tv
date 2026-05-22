using UnityEngine;

/// <summary>
/// Gestiona el tanque físico: dimensiones, colisiones y gizmos de debug.
/// Los colliders de paredes se generan por código — no se necesita configurar nada en el editor.
/// </summary>
public class TankController : MonoBehaviour
{
    [Header("Datos del tanque (solo para gizmos de editor)")]
    public TankData tankData;

    [Header("Opciones")]
    public bool generateBoundaryColliders = true;

    private Bounds _tankBounds;
    public Bounds GetTankBounds() => _tankBounds;

    // ── Inicialización ──────────────────────────────────────────────────────

    /// <summary>Inicializa el tanque con bounds calculados por la cámara (path normal).</summary>
    public void InitializeWithBounds(Bounds bounds)
    {
        _tankBounds        = bounds;
        _tankBounds.center = transform.position;

        if (generateBoundaryColliders)
            GenerateBoundaryColliders();

        // Subsistemas opcionales — solo se activan si el script está presente
        GetComponent<BubbleSystem>()?.InitializeBubbles();
        GetComponent<TankBackground>()?.InitializeBackground();
        GetComponent<WaterSurface>()?.InitializeSurface();
        GetComponent<DecorationPlacer>()?.InitializeDecoPlacer();
        GetComponent<TankLightingController>()?.Initialize(_tankBounds);
    }

    /// <summary>Fallback: inicializa desde TankData cuando no hay cámara.</summary>
    public void Initialize(TankData data)
    {
        tankData = data;
        InitializeWithBounds(new Bounds(transform.position, data.dimensions));
    }

    // ── Generación de paredes ───────────────────────────────────────────────

    private void GenerateBoundaryColliders()
    {
        // Limpiar paredes previas
        foreach (Transform child in transform)
            if (child.name.StartsWith("Wall_"))
                Destroy(child.gameObject);

        // Usar los bounds reales (no tankData.dimensions)
        float w = _tankBounds.size.x;
        float h = _tankBounds.size.y;
        float d = _tankBounds.size.z;
        float t = 0.2f;   // grosor de pared

        CreateWall("Wall_Left",   new Vector3(-w / 2f, 0,       0      ), new Vector3(t, h, d));
        CreateWall("Wall_Right",  new Vector3( w / 2f, 0,       0      ), new Vector3(t, h, d));
        CreateWall("Wall_Bottom", new Vector3(0,      -h / 2f,  0      ), new Vector3(w, t, d));
        CreateWall("Wall_Top",    new Vector3(0,       h / 2f,  0      ), new Vector3(w, t, d));
        CreateWall("Wall_Front",  new Vector3(0,       0,      -d / 2f ), new Vector3(w, h, t));
        CreateWall("Wall_Back",   new Vector3(0,       0,       d / 2f ), new Vector3(w, h, t));
    }

    private void CreateWall(string wallName, Vector3 localPos, Vector3 size)
    {
        var wall = new GameObject(wallName);
        wall.transform.SetParent(transform);
        wall.transform.localPosition = localPos;
        wall.AddComponent<BoxCollider>().size = size;
    }

    // ── Gizmos ──────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        // Mostrar bounds reales si están inicializados, si no usar tankData
        Vector3 size = _tankBounds.size.sqrMagnitude > 0.01f
            ? _tankBounds.size
            : (tankData != null ? tankData.dimensions : Vector3.one * 5f);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireCube(transform.position, size);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.05f);
        Gizmos.DrawCube(transform.position, size);
    }
}
