# Worker portero de los bundles — `appquarium-assets`

Cierra los bundles de Addressables: pasan a un bucket R2 **privado** y este Worker es la
única puerta. Sin token no salen bytes.

**Fase 1 (esto):** el receiver manda un token constante horneado en el `.wasm`.
**Fase 2 (cuando se toque el móvil):** el mismo header traerá un JWT por usuario.
El receiver **no habrá que rebuildearlo**: el hook de TV ya prefiere `state.castJwt` si viene.

⚠ El Worker **sirve los bytes, no redirige**. Un `302` no vale: el receiver pide los bundles
desde otro origen y con header `Authorization` — o sea, request con preflight — y el spec de
Fetch prohíbe seguir un redirect cross-origin en ese caso. Comprobado en `test-local.mjs`.

---

## Qué NO cubre

- `index.html` y el player (`.wasm`/`.data`) **siguen siendo públicos, y tienen que serlo**:
  el device de Cast pide la URL del receiver con el navegador y sin credenciales. No hay
  mecanismo en Cast para autenticar ese primer fetch.
- `catalog.bin` sigue público → permite **enumerar** nombres de bundle, no descargarlos.
- Los 11 fondos horneados en el `.data` (~0,7 MB) siguen extraíbles con AssetRipper.

Los 87,3 MB de contenido licenciado (21 GLB de fotogrametría + 25 peces) están **todos**
en los bundles, que es lo que esto cierra.

---

## Desplegar

```bash
cd Tools/r2-auth-worker
npx wrangler login                      # login interactivo del user
npx wrangler secret put BUNDLE_TOKENS   # pega el token; varios separados por coma para rotar
npx wrangler deploy
node test-local.mjs                     # lógica, sin red
./smoke-test.sh https://appquarium-assets.<sub>.workers.dev <TOKEN> <fichero.bundle>
```

`BUNDLE_TOKENS` admite **varios tokens separados por coma** a propósito: permite desplegar un
receiver con token nuevo sin dejar fuera al que ya está en la tele. El token viejo se retira
del secret cuando el nuevo esté validado.

## Desplegar la FASE 2 (JWT por usuario) — escrita y probada, SIN desplegar

El codigo ya esta en `src/` y `test-local.mjs` lo cubre (42/42). Lo que falta son **dos secrets
que solo puede poner el user**:

```bash
cd Tools/r2-auth-worker
npx wrangler secret put JWT_SECRET      # aleatorio largo; solo lo conoce el Worker
npx wrangler secret put MINT_TOKENS     # el que hornea el APK; admite varios por coma
npx wrangler deploy
```

Es **aditivo**: sin esos secrets el camino nuevo devuelve `503` y el token constante de la Fase 1
sigue funcionando igual, asi que la tele no se entera. Aun asi toca infraestructura viva.

⚠ **`OWNERSHIP_MODE` arranca en `log`** (`wrangler.toml`): firma y caducidad se verifican de
verdad, pero un bundle que no consta como del usuario **se sirve igual**, marcado
`X-Aq-Ownership: would-deny`. Se pasa a `enforce` cuando ese contador sea 0. Si los ids de los
claims llegaran mal, `enforce` deja al usuario sin **su** acuario — tele vacia, que es el sintoma
mas caro de diagnosticar en este proyecto.

⚠ **`/mint-token` no es abierto**: exige `Bearer <MINT_TOKENS>`. Un endpoint de emision sin
credencial dejaria pedir `isPremium` a cualquiera, y entonces la Fase 2 protegeria **menos** que
la Fase 1.

El contrato completo para el lado movil esta en [`../../CAST_R2_AUTH_MOVIL.md`](../../CAST_R2_AUTH_MOVIL.md) §1.4 y §7.

---

## Generar un token

```bash
python -c "import secrets; print('aqtv_' + secrets.token_urlsafe(32))"
```

---

## ⚠⚠ El token del receiver NO está en git (desde 2026-08-21)

`github.com/marcroger/appquarium-tv` es un repo **público**. El token vivía como `const` en
`Assets/Scripts/Core/TvBundleAuth.cs`, y con la URL del Worker documentada en los `.md` de al
lado eso es un `curl` de dos líneas: publicar la rama habría deshecho la Fase 1 entera. Que el
token viaje dentro del `.wasm` público es otra cosa y está asumido — ahí hay que ir a buscarlo,
no lo indexa un escáner de secretos ni sale en una búsqueda de GitHub.

Cómo queda:

| | |
|---|---|
| `Assets/Scripts/Core/TvBundleAuthSecret.cs` | El token real. **En `.gitignore`.** |
| `Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample` | La plantilla, ésta sí en git. |
| `Assets/Editor/TvAuthPreflight.cs` | Aborta **cualquier** build de WebGL que salga sin token. |

En un clon limpio, por tanto:

```bash
cp Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample Assets/Scripts/Core/TvBundleAuthSecret.cs
# y poner dentro el token real (el mismo que está en el secret BUNDLE_TOKENS)
```

⚠ Sin ese fichero el proyecto **compila igual** (es un `partial method`: si no está, la llamada
desaparece) y el player resultante arranca sin un solo error… y sin un solo bundle, porque el
Worker le devuelve 401 a todo. Por eso el preflight aborta el build en vez de dejarlo pasar: es
el mismo fallo silencioso que costó dos meses de audio mudo, y la respuesta es la misma.

⚠ El token real no está en ningún sitio del repo. Si se pierde el fichero local, se saca del
secret del Worker (`npx wrangler secret list` sólo da nombres — hay que tenerlo apuntado) o se
rota: token nuevo en `BUNDLE_TOKENS` **sin quitar el viejo**, rebuild de player (55 min),
validar en la tele, y entonces retirar el viejo.

---

## Marcha atrás

Cada paso es reversible por separado y **no se borra nada del bucket público hasta el final**.

| Si falla en… | Cómo se vuelve |
|---|---|
| El Worker (paso 1-3) | No se ha tocado producción. Nada que revertir. |
| El catálogo nuevo (paso 5) | Subir de nuevo `StreamingAssets/aa/catalog.bin` + `.hash` del backup. Los bundles del bucket público **siguen ahí**, así que el catálogo viejo vuelve a funcionar tal cual. |
| El player nuevo (paso 5) | Subir de nuevo `Build/webgl-output.*` del backup. |
| Falta el backup | Está en `D:/dev/_backups/appquarium-tv/backup-antes-auth-2026-08-20/` (catálogo, settings, index y los 4 del player del 17-ago; `md5sum -c MD5SUMS.txt`). |
| Se detecta días después | Mientras no se haya ejecutado el paso 7, el bucket público conserva los 82 objetos: revertir catálogo + player devuelve el sistema al estado del 19-ago. |
| Ya se ejecutó el paso 7 | Volver a copiar los bundles del bucket privado al público (`aws s3 sync` entre buckets) y luego revertir catálogo + player. |

⚠ El interruptor real es **`StreamingAssets/aa/catalog.bin`**, no el de `bundles/`. Producción
no cambia hasta que se sube ése — útil: permite dejar todo lo demás desplegado y accionar el
cambio (o deshacerlo) al final con un solo fichero.

⚠⚠ Nada de `--delete` en la raíz del bucket público: ahí está `keepalive_black.mp4`, que es lo
que mantiene viva la sesión. Borrar por **lista explícita**.

---

## Ficheros

| | |
|---|---|
| `src/index.js` | El Worker. Sin dependencias. |
| `wrangler.toml` | Binding R2 + `ALLOWED_ORIGINS`. El token va como *secret*, no aquí. |
| `test-local.mjs` | 20 comprobaciones de la lógica con mocks. `node test-local.mjs`. |
| `smoke-test.sh` | Matriz contra el Worker ya desplegado, incluido md5 de los bytes contra el bundle local. |
| `TvBundleAuthSecret.cs.sample` | Plantilla del fichero con el token que **no** va a git. |
