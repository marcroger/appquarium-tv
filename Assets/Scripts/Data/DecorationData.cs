using UnityEngine;

/// <summary>
/// Define un ítem de decoración o gadget del acuario.
/// Crear vía: Assets > Create > Appquarium > Decoration Data
/// </summary>
[CreateAssetMenu(fileName = "DecorationData", menuName = "Appquarium/Decoration Data")]
public class DecorationData : ScriptableObject
{
    [Header("Identidad")]
    public string itemId          = "";         // ID único para persistencia (ej: "rock_small")
    public string itemName        = "Item";
    public string description     = "";
    public DecorationCategory category = DecorationCategory.Rock;
    public PlacementType      placement = PlacementType.Floor;

    [Header("Visual")]
    public GameObject prefab;                   // Prefab 3D (placeholder hasta tener assets)
    public Vector3    defaultScale    = Vector3.one;
    [Tooltip("Rotación Y adicional al colocar (útil cuando el mesh del prefab está girado). Usar 'Appquarium/Fix Deco Rotation +180°' para asignar 180 en lote.")]
    public float      defaultRotationY = 0f;
    [Tooltip("Rotación X de corrección al colocar (útil para modelos exportados en Z-up que quedan tumbados). Ej: -90 para columnas GLB.")]
    public float      defaultRotationX = 0f;
    [Tooltip("Rotación Z de corrección al colocar. Normalmente 0; usar solo cuando el modelo requiere inclinación lateral (ej: heliopora=135).")]
    public float      defaultRotationZ = 0f;
    [Tooltip("Ajuste fino de Y tras el snap automático al suelo. Negativo = bajar, positivo = subir. Útil cuando el pivot del mesh no coincide exactamente con la base visual (ej: pose de animación difiere del bind pose).")]
    public float      floorYOffset = 0f;
    public FishRarity rarity       = FishRarity.Common;
    [Tooltip("Material alternativo para esta variante. Si se asigna, reemplaza el material original del prefab (útil para variantes de textura que comparten el mismo prefab, ej: ancla nueva / oxidada / muy oxidada).")]
    public Material   overrideMaterial = null;
    [Tooltip("Color multiplicador sobre la textura base (blanco = sin cambio). Útil para dar color característico a scans fotogramétricos sin textura de color.")]
    public Color      tintColor        = Color.white;
    [Tooltip("Nombre del estado del Animator al que saltar al cargar desde save (evita la animación de intro)")]
    public string     loopStateName = "";        // ej: "Animated PBR Chest _Idle"
    [Tooltip("Destruye todos los ParticleSystem hijos al instanciar (elimina efectos FX permanentes no deseados)")]
    public bool       disableFX     = false;

    [Header("Tienda")]
    public bool  isStarterGift = true;  // true = se regala al instalar (no significa precio 0)
    public float price  = 0f;
    public int   pearlPrice = 0;
    [Tooltip("Orden de presentación en pantalla (menor = antes). Asignar con Appquarium/Assign DisplayOrder.")]
    public int   displayOrder = 0;

    [Header("Play Asset Delivery")]
    [Tooltip("Nombre del AssetBundle/PAD pack que contiene el prefab (auto-rellenado por BundleBuilder)")]
    public string assetBundleName      = "";   // e.g. "pack_decos_greek"
    [Tooltip("Nombre del asset dentro del bundle (auto-rellenado por BundleBuilder)")]
    public string assetBundleAssetName = "";  // e.g. "GreekColumn1"

    [Header("Iluminación")]
    [Tooltip("Esta decoración emite luz (glow)")]
    public bool  hasGlow       = false;
    public Color glowColor     = Color.white;
    public float glowIntensity = 0.8f;
    [Tooltip("Esta deco emite bioluminiscencia de noche (emission en material + bloom). Usar en corales que fluorescentan.")]
    public bool  hasBioLuminescence = false;
    [Tooltip("Multiplicador HDR de emisión nocturna aplicado sobre tintColor (>1 activa bloom)")]
    public float bioGlowIntensity   = 1.5f;

    [Header("Surface Climbing")]
    [Tooltip("Esta deco puede usarse como superficie: otras decos pueden montarse encima (rocas, columnas, estatuas)")]
    public bool isMountTarget  = false;
    [Tooltip("Esta deco puede montarse encima de otras decos marcadas como isMountTarget (corales, plantas, conchas, estrellas)")]
    public bool canMountOthers = false;

    [Header("Contacto con la superficie")]
    [Tooltip("Cuánto se entierra la base del mesh en la superficie de contacto (suelo o deco-target). Negativo = hundir, 0 = ras, positivo = flotar. Las decos sólidas suelen llevar -0.02..-0.05; los corales -0.02; rocas pueden hundirse más.")]
    public float embedDepth      = -0.03f;
    [Tooltip("Punto de apoyo en espacio LOCAL del prefab (antes de defaultScale). Si está en cero, se usa el punto más bajo del AABB. Útil para corales: el AABB.min puede ser la punta de una rama lateral; aquí indicas la base/raíz que realmente debe tocar la superficie.")]
    public Vector3 supportPointLocal = Vector3.zero;

    [Header("Efectos en el ecosistema")]
    [Tooltip("Reduce el estrés de los peces por segundo")]
    public float stressReduction   = 0f;        // filtro: 0.0003f
    [Tooltip("Modifica la tasa de hambre (negativo = hambre más lenta)")]
    public float hungerRateBonus   = 0f;        // calentador: -0.0001f
    [Tooltip("Este gadget genera columna de burbujas propia")]
    public bool  generatesBubbles  = false;     // piedra de aire, filtro
    [Tooltip("Evita que el tanque se ensucie")]
    public bool  preventsAlgae     = false;     // lámpara UV
    [Tooltip("Sirve como escondite: peces tímidos reducen su estrés cerca")]
    public bool  isHideout         = false;     // rocas grandes, castillo
}

// ── Enums ─────────────────────────────────────────────────────────────────────

public enum DecorationCategory
{
    // Naturales
    Rock,           // Rocas
    Coral,          // Corales
    Shell,          // Conchas, estrellas de mar, vida marina no-coral
    Plant,          // Plantas y algas
    // Objetos
    Toy,            // Cofre, castillo, pecio, estatua
    // Gadgets funcionales
    Gadget,         // Filtro, calentador, lámpara UV, piedra de aire
    // Entorno
    Substrate,      // Arena, grava (cubre el fondo)
    Background,     // Poster de fondo (reemplaza el degradado)
}

public enum PlacementType
{
    Floor,      // Se coloca en el fondo del tanque
    Floating,   // Flota a media altura (plantas largas, medusas deco)
    Surface,    // Se coloca en la superficie (alimentador automático)
    Wall,       // Se pega a la pared trasera
}
