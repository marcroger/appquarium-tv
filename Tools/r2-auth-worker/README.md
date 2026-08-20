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

## Generar un token

```bash
python -c "import secrets; print('aqtv_' + secrets.token_urlsafe(32))"
```

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
