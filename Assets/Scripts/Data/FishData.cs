using UnityEngine;

/// <summary>
/// Define una especie de pez: stats, personalidad y datos de monetización.
/// Crear instancias vía: Assets > Create > Appquarium > Fish Data
/// </summary>
[CreateAssetMenu(fileName = "FishData", menuName = "Appquarium/Fish Data")]
public class FishData : ScriptableObject
{
    [Header("Identidad")]
    public string speciesName = "Unknown Fish";
    public string latinName   = "";             // Nombre científico (e.g. "Pterapogon kauderni")
    public string speciesDescription = "";

    [Header("Personalidad (0 = nada, 1 = máximo)")]
    [Range(0f, 1f)] public float shyness = 0.3f;       // Cuánto huye
    [Range(0f, 1f)] public float aggression = 0.1f;    // Invade espacio ajeno
    [Range(0f, 1f)] public float curiosity = 0.5f;     // Tiempo explorando vs idle
    [Range(0f, 1f)] public float sociability = 0.5f;   // Busca al grupo

    [Header("Movimiento")]
    public float maxSpeed = 2f;
    public float turnSpeed = 3f;
    public float perceptionRadius = 3f;
    public float baseSize = 1f;

    [Tooltip("0 = usar defecto (0.8). Alto = nervioso/errático. Bajo = arcos suaves y graduales.")]
    public float wanderJitter   = 0f;
    [Tooltip("0 = usar defecto (1.5). Pequeño = dardos cortos. Grande = giros amplios.")]
    public float wanderRadius   = 0f;
    [Tooltip("0 = usar defecto (2.5). Corto = movimiento local. Largo = barridos amplios.")]
    public float wanderDistance = 0f;

    [Tooltip("-1 = comportamiento por defecto (0 para Pack 24, 15 para peces sin Animator). " +
             "Valor positivo = override del swing procedural del cuerpo. " +
             "Usar 5-10 para peces Pack 24 que se vean demasiado rígidos.")]
    public float bodySwingOverride = -1f;

    [Tooltip("-1 = usar el valor global (6°). Peces planos/discoidales: 8-11. Ovalados: 5. Cilíndricos: 3-4. Caja: 2.")]
    public float bankingAmountOverride = -1f;

    [Tooltip("-1 = usar el valor global (5). Peces grandes/lentos: 7-8 para giros más graduales.")]
    public float turnSmoothingOverride = -1f;

    [Header("Zona favorita del tanque")]
    public TankZone preferredZone     = TankZone.MidWater;
    [Range(0f, 1f)]
    public float    zoneAttraction    = 0.6f;   // 0 = libre, 1 = muy apegado a su zona

    [Header("Necesidades")]
    public float hungerRate = 0.0008f;   // Por segundo

    [Header("Efectos de gadgets (modificadores)")]
    public float stressBaseRate  = 0.0002f;  // estrés por segundo en condiciones normales

    [Header("Comportamiento social")]
    [Range(0f, 1f)] public float schoolingStrength = 0f;   // 0=solitario, 1=cardumen fuerte
    public string[] friendlySpecies = new string[0];        // especies compatibles (menor separación)
    public string[] rivalSpecies    = new string[0];        // especies rivales (mayor separación, estrés)

    [Header("Comportamiento circadiano")]
    public bool isNocturnal = false;       // Si true: más activo y menos tímido de noche (AmbientMode.Night)

    [Header("Tienda")]
    public string itemId  = "";            // ID único para persistencia (ej: "clownfish_01")
    public bool   isStarterGift = true;  // true = se regala al instalar (no significa precio 0)
    public float  price   = 0f;
    public FishRarity rarity = FishRarity.Common;
    [Tooltip("Orden de presentación en pantalla (menor = antes). Asignar con Appquarium/Assign DisplayOrder.")]
    public int displayOrder = 0;
    public GameObject prefab;              // Prefab 3D (asignar cuando lleguen los assets)

    [Header("Play Asset Delivery")]
    [Tooltip("Nombre del AssetBundle/PAD pack que contiene el prefab (auto-rellenado por BundleBuilder)")]
    public string assetBundleName     = "";   // e.g. "pack_fish_rare"
    [Tooltip("Nombre del asset dentro del bundle (auto-rellenado por BundleBuilder)")]
    public string assetBundleAssetName = "";  // e.g. "BanggaiCardinalFish"

    [Header("Ciclo de vida y Perlas (v1.2)")]
    [Tooltip("Días en pasar de Cría a Adulto (distribuido 25% Cría / 75% Juvenil). Rellenar con Appquarium/Breeding/Populate Lifecycle Data.")]
    public int maturationDays = 8;   // días Cría → Adulto
    [Tooltip("Días como Adulto hasta pasar a Senior.")]
    public int seniorDays = 12;      // días Adulto → Senior
    [Tooltip("Días como Senior antes de mostrar la card de despedida (solo Liberar).")]
    public int seniorGoodbyeDays = 7; // días Senior → goodbye card
    [Tooltip("Coste en Perlas para comprar la especie (alternativa a €). 0 = no disponible por Perlas.")]
    public int pearlPrice = 0;       // Perlas para desbloquear especie
    [Tooltip("Perlas obtenidas al vender UN espécimen criado en la pecera (isBred=true).")]
    public int pearlSellValue = 2;   // Perlas al vender cría
}

/// <summary>Zona vertical preferida por la especie dentro del tanque.</summary>
public enum TankZone
{
    Surface,    // 20% superior  — ej: peces de superficie, bettas
    MidWater,   // 60% central   — ej: la mayoría de peces tropicales
    Bottom,     // 20% inferior  — ej: corydoras, plecos
    Anywhere    // sin preferencia
}

public enum FishRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum FishSex
{
    Male,
    Female,
    Unknown   // hermafroditas o sin dato
}

public enum FishAgeGroup
{
    Cria,      // 0.40x escala — recién nacida del huevo (breeding v1.2)
    Juvenile,  // 0.65x escala — 40% prob (peces comprados)
    Adult,     // 1.00x escala — 45% prob
    Senior     // 1.18x escala — 15% prob (tint sutil)
}

public enum FishState
{
    Idle,
    Explore,
    Flee,
    Feed,
    Sleep,
    Courtship
}
