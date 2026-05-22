/// <summary>
/// Feature flags centralizados. Cambiar aquí activa/desactiva características
/// que están implementadas pero no activas en la versión actual.
/// </summary>
public static class AppFlags
{
    /// <summary>
    /// Efectos visuales de descuido: suciedad del tanque (overlay verde) y
    /// desaturación/ralentización de peces hambrientos.
    /// Desactivado en v1 — activar en v1.x cuando estén los assets finales de peces.
    /// </summary>
    public const bool EnableNeglectVisuals = false;

    /// <summary>
    /// Cast a TV (Chromecast/AirPlay). Desactivado hasta v1.1.
    /// </summary>
    public const bool EnableCast = true;

    /// <summary>
    /// Sistema de reproducción de peces, ciclo de vida y Perlas (v1.2).
    /// Desactivado hasta v1.2.
    /// </summary>
    public const bool EnableBreeding = true;
}
