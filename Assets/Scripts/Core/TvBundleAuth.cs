using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

/// <summary>
/// Pone la credencial en cada descarga de bundle. Los bundles ya no salen de un bucket
/// público: los sirve un Worker de Cloudflare (Tools/r2-auth-worker/) desde un bucket
/// privado, y sin cabecera devuelve 401.
///
/// FASE 1 (esto): token constante. Vive dentro del .wasm, así que no es DRM — es lo que
/// convierte "cualquiera con la URL se baja los assets" en "hay que atacar el producto".
/// Que era el problema real: las licencias del Pack 24 y de los Sketchfab no-CC0 prohíben
/// redistribuir los assets crudos, y un bucket público ES redistribuirlos.
///
/// FASE 2 (móvil): el sender mandará un JWT por usuario en el INIT de Cast. Este hook ya lo
/// prefiere si viene, así que esa fase NO necesita rebuild de player — sólo que el Worker
/// aprenda a validar JWTs y que el móvil rellene el campo.
///
/// ⚠⚠ EL TOKEN NO ESTÁ EN GIT (2026-08-21). El repo es PÚBLICO
/// (github.com/marcroger/appquarium-tv): dejarlo aquí, al lado de la URL del Worker que
/// documentan los .md, equivale a volver a abrir el bucket — con la diferencia de que en
/// GitHub es grepeable y lo indexan los escáneres de secretos. Que ya viaje dentro del
/// .wasm es asumido y no es lo mismo: ahí hay que ir a buscarlo.
/// Lo aporta <c>Assets/Scripts/Core/TvBundleAuthSecret.cs</c>, que está en .gitignore.
/// Plantilla y receta: <c>Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample</c>.
///
/// Si ese fichero falta, el proyecto COMPILA IGUAL (el partial method se elimina) y el
/// token sale vacío — por eso <c>TvBundleAuthPreflight</c> aborta cualquier build de WebGL
/// que no lo lleve. Un player sin token no peta: se queda sin bundles, o sea acuario vacío,
/// que es justo el tipo de fallo silencioso que este proyecto ya ha pagado caro.
///
/// ⚠ Si algún día hay que rotar el token constante, cuesta un rebuild de player (55 min).
/// El Worker acepta varios tokens a la vez (BUNDLE_TOKENS separados por coma) justamente
/// para que el receiver viejo siga vivo mientras se despliega el nuevo.
/// </summary>
public static partial class TvBundleAuth
{
    /// <summary>
    /// La implementa el fichero gitignoreado. Si no está, esta llamada desaparece en
    /// compilación y <see cref="FallbackToken"/> queda vacío.
    /// </summary>
    static partial void SupplyFallbackToken(ref string token);

    // Se busca por RUTA, no por host: así el fichero no depende del subdominio que acabe
    // teniendo el Worker. Ojo al detalle: los bundles viejos colgaban de "/bundles/" (plural),
    // que NO contiene "/bundle/", así que no hay falso positivo con la URL antigua.
    private const string WorkerPathMarker = "/bundle/";

    private static string _fallback;     // null = aún no resuelto; "" = no hay token
    private static string _jwt;          // Fase 2: llega por Cast INIT
    private static int    _tagged;       // cuántas requests se han firmado
    private static bool   _loggedOnce;

    /// <summary>Token de la Fase 1, o cadena vacía si el build salió sin él.</summary>
    private static string FallbackToken
    {
        get
        {
            if (_fallback == null)
            {
                string t = null;
                SupplyFallbackToken(ref t);
                _fallback = t ?? "";
            }
            return _fallback;
        }
    }

    /// <summary>Para el preflight de build: ¿este build llevará token constante?</summary>
    public static bool HasFallbackToken => !string.IsNullOrEmpty(FallbackToken);

    /// <summary>Fase 2. Vacío o null = se sigue usando el token constante.</summary>
    public static void SetSessionToken(string jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return;
        if (_jwt == jwt) return;
        _jwt = jwt;
        JsBridge.Log("AUTH: token de sesión recibido del sender (JWT)");
    }

    /// <summary>
    /// Se llama en Awake, antes de cualquier LoadAssetAsync. Addressables invoca este
    /// override en AssetBundleProvider.cs:547 para cada descarga.
    /// </summary>
    public static void Install()
    {
        Addressables.WebRequestOverride = OnWebRequest;
    }

    private static void OnWebRequest(UnityWebRequest request)
    {
        if (request == null) return;
        var url = request.url;
        if (string.IsNullOrEmpty(url)) return;
        if (url.IndexOf(WorkerPathMarker, StringComparison.OrdinalIgnoreCase) < 0) return;

        var token = string.IsNullOrEmpty(_jwt) ? FallbackToken : _jwt;
        if (!string.IsNullOrEmpty(token))
            request.SetRequestHeader("Authorization", "Bearer " + token);
        _tagged++;

        // Una sola línea, y por JsBridge: un Debug.Log aquí sería invisible (no viaja por
        // el canal Cast) y este es justo el sitio donde un fallo silencioso costaría horas.
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            if (string.IsNullOrEmpty(token))
                JsBridge.Log("AUTH: ⚠ SIN TOKEN — este build salió sin TvBundleAuthSecret.cs. " +
                             "El Worker devolverá 401 y el acuario se quedará VACÍO.");
            else
                JsBridge.Log($"AUTH: bundles autenticados vía Worker (fuente={(string.IsNullOrEmpty(_jwt) ? "constante" : "jwt")})");
        }
    }

    /// <summary>Para el panel de diagnóstico: cuántas descargas se han firmado.</summary>
    public static int TaggedRequests => _tagged;
}
