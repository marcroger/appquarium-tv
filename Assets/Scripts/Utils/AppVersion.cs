/// <summary>
/// Fuente única de verdad para la versión de Appquarium.
/// Usar en BuildSetup.cs y en cualquier lugar que necesite la versión.
/// Actualizar aquí al hacer builds.
/// </summary>
public static class AppVersion
{
    /// <summary>Número de versión semántica. Ej: "0.7.0"</summary>
    public const string Number = "1.2.1";

    /// <summary>Versión corta para mostrar en UI. Ej: "v0.7"</summary>
    public const string Display = "v1.2";

    /// <summary>Cadena completa para el label de versión. Ej: "Appquarium v0.7"</summary>
    public const string Full = "Appquarium v1.2";

    /// <summary>versionCode de Android (incrementar en cada build — nunca repetir).</summary>
    public const int AndroidVersionCode = 31;

    /// <summary>buildNumber de iOS (incrementar en cada build).</summary>
    public const string IOSBuildNumber = "31";
}
