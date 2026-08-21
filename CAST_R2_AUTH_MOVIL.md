# Fase 2 de la auth de bundles — qué tiene que hacer el repo MÓVIL

> Escrito desde el repo **TV** el 2026-08-20, para que lo ejecute la sesión de
> `D:\dev\appquarium-unity\`. **Desde TV no se toca ese repo.**
>
> La Fase 1 ya está hecha y desplegada: los bundles salen de un bucket **privado** y los sirve
> un Worker de Cloudflare que exige `Authorization: Bearer`. Hoy el receiver manda un **token
> constante**. La Fase 2 sustituye ese token por un **JWT por usuario con claims de propiedad**,
> que es lo que convierte «nadie puede bajarse los assets» en «cada uno sólo baja lo suyo».

---

## 0. Lo que NO hay que hacer

- ⚠ **No hay que rebuildear el receiver de TV.** Ya está preparado: `TvAquariumState` tiene el
  campo `castJwt` y `TvBundleAuth` lo prefiere sobre el token constante si viene relleno.
  Todo el trabajo de esta fase es **móvil + Worker**.
- ⚠ **El secreto de firma no puede vivir en el APK.** Firma sólo el Worker. Si el APK pudiera
  firmar, cualquiera con el APK decompilado se emite un token premium.
- ⚠ **No cambiar el nombre del campo.** El JSON del INIT es un contrato entre los dos repos.

---

## 1. El contrato exacto

### 1.1 Campo nuevo en el JSON del INIT

TV ya deserializa esto (`Assets/Scripts/Core/CastDataTypes.cs`):

```csharp
[Serializable]
public class TvAquariumState {
    // ... los 9 campos que ya existen, sin tocar
    public string castJwt = "";   // ← el único añadido
}
```

En el móvil hay que añadir **el mismo campo con el mismo nombre** a la clase equivalente y
rellenarlo al construir el estado. Vacío o ausente = TV usa su token constante (o sea: **una app
vieja sigue funcionando**, ver §3).

### 1.2 Cómo viaja al Worker

TV pone en cada descarga de bundle:

```
Authorization: Bearer <castJwt>
GET https://appquarium-assets.appquarium.workers.dev/bundle/<nombre>.bundle
```

### 1.3 El JWT

- **Algoritmo:** HS256. Secreto sólo en el Worker (`JWT_SECRET`, Workers secret).
- **TTL:** 24 h.
- **Claims:**

```json
{
  "userId": "uuid-v4 anónimo, generado en el device y persistido en SaveData",
  "isPremium": false,
  "ownedSpecies": ["fish_mandarinfish", "..."],
  "ownedDecoIds": ["deco_anchor", "..."],
  "ownedPackIds": ["pack_decos_marine"],
  "iat": 0,
  "exp": 0
}
```

⚠ **Los ids tienen que ser los `itemId` del catálogo de Addressables**, porque es contra eso que
el Worker compara. El Worker los saca del nombre del bundle con este regex, que ya está escrito:

```
^(?:decos|fish|audio|environments)_remote_assets_(.+)_[0-9a-f]{32}\.bundle$
```

Es decir: `decos_remote_assets_deco_coral_acropora_205d35d0….bundle` → `deco_coral_acropora`.
Si el móvil guarda los ids con otro formato (sufijos de instancia, prefijos distintos), **hay que
normalizarlos antes de meterlos en los claims** o el usuario se quedará sin sus propias decos.

### 1.4 El endpoint de emisión

`POST https://appquarium-assets.appquarium.workers.dev/mint-token`

```json
{ "userId": "...", "isPremium": false,
  "ownedSpecies": [], "ownedDecoIds": [], "ownedPackIds": [] }
```
→ `200 {"token": "<jwt>"}`

**Este endpoint todavía NO existe**: lo escribo yo en el Worker desde el repo TV cuando la parte
móvil esté lista. Se implementa junto con la verificación HS256 y la comprobación de propiedad
(en `src/index.js` hay un hueco marcado en `authorize()`).

⚠ En el MVP el Worker **se fía de lo que le manda el APK** — no valida el `purchaseToken` contra
la Google Play Developer API. Es un trade-off conocido: quien manipule el APK se emite el token
que quiera. Cerrarlo es la mejora futura (pide un Service Account de Google Play).

---

## 2. Trabajo en el móvil

Según el spec de mayo (`CAST_R2_AUTH_SPEC.md` §6), los ficheros son estos — **verificar sobre el
terreno, que ese spec es de mayo y el repo ha cambiado**:

| Fichero | Qué |
|---|---|
| `SaveSystem.cs` | campos `userId` (uuid-v4, generado una vez) y `castJwt` |
| `IAPService.cs` | llamar a `/mint-token` tras cada compra con éxito y guardar el token |
| `AquariumManager.cs` | al arrancar, si el JWT caducó, re-emitirlo (transparente) |
| `CastManager.cs` | meter `castJwt` en el JSON del INIT |

---

## 3. ⚠ Esto es una MIGRACIÓN, no un interruptor

El día que salga la versión nueva, **todas las apps ya instaladas siguen mandando el campo
vacío**. Si el Worker dejara de aceptar el token constante ese día, a todo el que no haya
actualizado **se le queda la tele vacía**.

Orden correcto:

1. El Worker aprende a validar JWTs **y sigue aceptando el token constante** (`BUNDLE_TOKENS`
   admite varios valores separados por coma; ése es justamente el motivo).
2. Sale la versión móvil que emite y manda el JWT.
3. Se espera a que la adopción sea alta (mirar la consola de Play).
4. **Sólo entonces** se retira el token constante del secret del Worker.

En el paso 4 los receivers viejos dejan de cargar bundles — pero para entonces el receiver es el
mismo (no cambia), lo que cambia es el móvil que le manda el token.

---

## 4. Qué NO arregla esta fase

- `index.html` y el player (`.wasm`/`.data`) **siguen públicos y tienen que seguirlo**: el device
  de Cast pide la URL del receiver con el navegador y sin credenciales.
- Los 11 fondos horneados en el `.data` (~0,7 MB) siguen siendo extraíbles.
- `catalog.bin` sigue público: permite **enumerar** nombres de bundle, no descargarlos.

---

## 5. Para probar sin tocar producción

El Worker acepta varios tokens a la vez. Para probar la Fase 2 sin arriesgar:

```bash
# añadir un token de pruebas sin quitar el de produccion
cd Tools/r2-auth-worker
echo "aqtv_<produccion>,jwt-de-pruebas" | npx wrangler secret put BUNDLE_TOKENS
```
