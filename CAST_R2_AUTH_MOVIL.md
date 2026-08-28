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

> 📲 **Si eres la sesión del repo MÓVIL, lee antes**
> [`CAST_HANDOFF_MOVIL_2026-08-26.md`](CAST_HANDOFF_MOVIL_2026-08-26.md): resume esto y todo lo
> demás que cambió de tu lado el 26-ago.

### 1.4 El endpoint de emisión ✅ YA EXISTE (escrito el 2026-08-26, **sin desplegar**)

`POST https://appquarium-assets.appquarium.workers.dev/mint-token`

```
Authorization: Bearer <MINT_TOKENS>          ← ⚠ NUEVO, ver abajo
Content-Type: application/json

{ "userId": "...", "isPremium": false,
  "ownedSpecies": [], "ownedDecoIds": [], "ownedPackIds": [] }
```
→ `200 {"token": "<jwt>", "exp": 1756300000}`

Errores: `401` sin credencial o con una ajena · `400` sin `userId` o con body que no es JSON ·
`405` si no es POST · `503` si al Worker le falta `JWT_SECRET` o `MINT_TOKENS`.

#### ⚠⚠ DOS DECISIONES QUE NO ESTABAN EN ESTE SPEC

**1. `/mint-token` NO es abierto: pide una credencial (`MINT_TOKENS`).**

El spec decía que «el Worker se fía de lo que le manda el APK», y eso **sigue siendo cierto para
el CONTENIDO de los claims** (no se valida la compra contra Google Play — el trade-off conocido
del MVP). Pero un endpoint de emisión **sin ninguna credencial** es otra cosa: cualquiera pide un
token con `isPremium: true` y se baja el catálogo entero. Con eso la Fase 2 protegería **menos**
que la Fase 1, que es justo lo contrario de lo que se busca.

Así que para emitir hay que presentar uno de `MINT_TOKENS`, un secret propio del Worker que
**hornea el APK** (igual que el receiver hornea el suyo). El listón sigue siendo «hay que atacar
el producto», que es lo que las licencias esperan.

🧭 **Es un secret distinto del `BUNDLE_TOKENS` del receiver**, a propósito: son dos clientes con
credenciales separadas, y rotar una no obliga a rebuildear al otro.

**2. La propiedad NO se aplica todavía: se registra.** `OWNERSHIP_MODE` = `log` (por defecto) o
`enforce`.

En `log` la firma y la caducidad se verifican **de verdad**, pero si el usuario pide un bundle que
no consta como suyo **se le sirve igual** y la respuesta lleva `X-Aq-Ownership: would-deny`.

Motivo: si los ids de los claims llegan con otro formato —sufijos de instancia, prefijos
distintos— el usuario **se queda sin SU acuario**, y eso se ve como una tele vacía, que es el
síntoma más caro de diagnosticar de este proyecto. Primero se mide con tráfico real; se pasa a
`enforce` cuando el contador de `would-deny` sea 0.

#### Cómo se comporta el Worker con cada credencial

| lo que llega en `Authorization` | qué pasa |
|---|---|
| un `BUNDLE_TOKENS` (Fase 1) | se sirve, como siempre. **Sigue vivo toda la migración** |
| un JWT válido y con el ítem en sus claims | se sirve |
| un JWT válido **sin** el ítem, `OWNERSHIP_MODE=log` | se sirve + `X-Aq-Ownership: would-deny` |
| un JWT válido **sin** el ítem, `OWNERSHIP_MODE=enforce` | **403** |
| un JWT caducado, mal firmado, `alg: none` o sin `exp` | **401 JWT invalido o caducado** |
| un bundle de `audio` o `environments` | se sirve: no es de nadie |

⚠ Un token **con tres partes separadas por puntos se trata SIEMPRE como JWT** y no cae al camino
del token constante. Si no, un JWT caducado se compararía contra la lista y daría `403 Invalid
token`, un diagnóstico engañoso para algo que sólo necesita re-emitirse.

#### Estado y despliegue

**Escrito y probado, NO desplegado.** `Tools/r2-auth-worker/test-local.mjs` cubre 42 casos, 0
fallos, incluidos firma manipulada, `alg: none`, `HS512` y caducado.

Para ponerlo en producción hacen falta **dos secrets nuevos** en el Worker:

```bash
cd Tools/r2-auth-worker
npx wrangler secret put JWT_SECRET      # aleatorio largo; sólo lo conoce el Worker
npx wrangler secret put MINT_TOKENS     # el que hornea el APK; admite varios por coma
npx wrangler deploy
```

El despliegue es **aditivo**: sin esos secrets el camino nuevo devuelve `503` y el token constante
sigue funcionando igual, así que la tele no se entera. Aun así toca infraestructura viva, y lo
decide el user.

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
