using UnityEngine;

/// <summary>
/// Define un tanque: tamaño físico, capacidad y datos de tienda.
/// Crear instancias vía: Assets > Create > Appquarium > Tank Data
/// o usar Appquarium/Tanks/Crear Catálogo de Tanques
/// </summary>
[CreateAssetMenu(fileName = "TankData", menuName = "Appquarium/Tank Data")]
public class TankData : ScriptableObject
{
    [Header("Identidad")]
    public string itemId   = "tank_nano";
    public string tankName = "Nano Reef";

    [Header("Dimensiones (metros en Unity — ancho × alto × profundo)")]
    public Vector3 dimensions = new Vector3(8f, 5f, 4f);

    [Header("Cámara")]
    [Tooltip("worldHalfHeight de la cámara ortográfica. Menor = más zoom = peces más grandes.\nnano=2.5 | m=3.5 | l=4.2 | ocean=5.0  (el defecto 5f es el del OCEAN: ponlo a proposito)")]
    // ⚠⚠ CORREGIDO 2026-08-31. El Tooltip de arriba decia «Ocean/Large=5.0 | Starter=4.0 |
    //    Micro=3.65» y NINGUNO de esos tres describia un tanque real salvo el ocean: «Starter»
    //    y «Micro» ya no son nombres de nada, y sobre todo metia `tank_l` en el mismo saco que
    //    el ocean con «Large=5.0» cuando vale 4.2. Ese numero decide el ENCUADRE VERTICAL, o sea
    //    el tamano aparente de los peces, asi que un doc mentiroso aqui sale caro: el 31-ago
    //    costo una investigacion sobre si las dos pantallas encuadraban distinto (no: los dos
    //    `tank_l` valen 4.2, verificado leyendo el .asset en los DOS repos).
    //    Valores REALES: tank_nano 2.5 · tank_m 3.5 · tank_l 4.2 · tank_ocean 5.0
    // ⚠ El defecto de abajo (5f) es el del OCEAN: un TankData nuevo nace con el encuadre del
    //   tanque mas grande sin que nadie lo elija. Se deja asi para no tocar ningun asset
    //   existente, pero al crear un tanque hay que ponerlo A PROPOSITO.
    public float worldHalfHeight = 5f;

    [Header("Capacidad")]
    public int maxFishCapacity = 3;   // slots de peces simultáneos

    [Header("Tienda")]
    public bool  isStarterGift = true;  // true = se regala al instalar (no significa precio 0)
    public float price     = 0f;
    public int   pearlPrice = 0;
    public FishRarity rarity = FishRarity.Common;  // para el color del badge en tienda
    [Tooltip("Orden de presentación en pantalla (menor = antes). Asignar con Appquarium/Assign DisplayOrder.")]
    public int   displayOrder = 0;
}
