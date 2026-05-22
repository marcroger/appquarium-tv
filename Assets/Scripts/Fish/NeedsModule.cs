using UnityEngine;

/// <summary>
/// Gestiona las necesidades vitales del pez: hambre, estrés y energía.
/// Estos valores influyen en las transiciones de la FSM (FishBrain).
/// </summary>
public class NeedsModule : MonoBehaviour
{
    [Header("Valores actuales (0=ok, 1=crítico)")]
    [Range(0f, 1f)] public float hunger = 0f;
    [Range(0f, 1f)] public float stress = 0f;
    [Range(0f, 1f)] public float energy = 1f;   // Energy es inversa: 1=lleno, 0=agotado

    [Header("Tasas (configuradas por FishData)")]
    public float hungerRate = 0.0013f;           // Hambre sube sola con el tiempo (~13 min para saturarse)
    public float stressRecoveryRate = 0.003f;   // Estrés baja en reposo
    public float energyDrainIdle = 0.00005f;    // Energía que gasta en idle
    public float energyDrainActive = 0.0002f;   // Energía que gasta explorando
    public float energyDrainFlee = 0.0005f;     // Energía que gasta huyendo
    public float energyRecoveryRate = 0.001f;   // Energía que recupera al dormir

    // Propiedades de lectura para la FSM
    public bool IsHungry   => hunger > 0.75f;
    public bool IsStressed => stress > 0.70f;
    public bool IsExhausted => energy < 0.15f;

    void Update()
    {
        // El hambre sube sola con el tiempo
        hunger = Mathf.Clamp01(hunger + hungerRate * Time.deltaTime);
    }

    // Llamado por FishBrain según el estado actual
    public void TickIdle()
    {
        stress  = Mathf.Clamp01(stress - stressRecoveryRate * Time.deltaTime);
        energy  = Mathf.Clamp01(energy - energyDrainIdle * Time.deltaTime);
    }

    public void TickExplore()
    {
        energy = Mathf.Clamp01(energy - energyDrainActive * Time.deltaTime);
    }

    public void TickFlee()
    {
        stress = Mathf.Clamp01(stress + 0.002f * Time.deltaTime);
        energy = Mathf.Clamp01(energy - energyDrainFlee * Time.deltaTime);
    }

    public void TickSleep()
    {
        energy = Mathf.Clamp01(energy + energyRecoveryRate * Time.deltaTime);
        stress = Mathf.Clamp01(stress - stressRecoveryRate * 2f * Time.deltaTime);
    }

    // Acciones externas
    public void Feed(float amount)
    {
        hunger = Mathf.Clamp01(hunger - amount);
    }

    public void AddStress(float amount)
    {
        stress = Mathf.Clamp01(stress + amount);
    }
}
