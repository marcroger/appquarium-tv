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
    [Tooltip("worldHalfHeight de la cámara ortográfica. Menor = más zoom = peces más grandes.\nOcean/Large=5.0 | Starter=4.0 | Micro=3.65")]
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
