# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del 2026-08-20. La anterior está en `CAST_NEXT_SESSION_2026-08-20.md`.
>
> **Lo que era el único punto bloqueante para producción está cerrado:** los bundles ya no se
> pueden descargar desde una URL pública. Todo validado en la tele y nada a medias.

---

## 1. Lo que se cerró: los bundles detrás de un Worker

### 1.1 Qué había y qué hay

| | antes | ahora |
|---|---|---|
| Dónde viven los 80 bundles | `appquarium-tv/bundles/` (**público**) | `appquarium-tv-assets/` (**privado**, raíz) |
| Cómo se piden | `curl` a la URL y ya | `Authorization: Bearer` contra el Worker |
| `curl` a pelo a un bundle | **200 + 87 MB de assets** | **404** (y el Worker sin token, **401**) |

```
https://appquarium-assets.appquarium.workers.dev/bundle/<fichero>.bundle
```

El problema no era sólo que te copiaran: el EULA del Pack 24 y las licencias no-CC0 de Sketchfab
**prohíben redistribuir los assets en forma extraíble**, y un `.bundle` servido en abierto es
exactamente eso — AssetStudio o AssetRipper lo abren en segundos y sacan FBX y PNG.

### 1.2 Las piezas

| | |
|---|---|
| `Tools/r2-auth-worker/` | El Worker (sin dependencias) + `wrangler.toml` + **README con el rollback** |
| `Tools/r2-auth-worker/test-local.mjs` | 21 comprobaciones de la lógica con mocks, sin red |
| `Tools/r2-auth-worker/smoke-test.sh` | 12 comprobaciones contra el Worker desplegado, incluido md5 de los bytes |
| `Assets/Scripts/Core/TvBundleAuth.cs` | El hook que firma cada descarga |
| `CAST_R2_AUTH_MOVIL.md` | **El contrato de la Fase 2 para el repo móvil** |

### 1.3 Fase 1 = token constante, y qué significa eso

El token vive dentro del `.wasm`. **No es DRM** y no pretende serlo: convierte «cualquiera con la
URL se baja los 87 MB» en «hay que atacar el producto», que es el estándar de diligencia que las
licencias esperan. La Fase 2 (JWT por usuario, con claims de propiedad) es la que hace que cada
uno sólo pueda bajarse **lo suyo**.

🎁 **La Fase 2 no necesitará rebuild de player.** `TvAquariumState` ya lleva el campo `castJwt` y
el hook lo prefiere sobre el token constante si viene relleno. Todo el trabajo restante es del
móvil y del Worker.

⚠ **Rotar el token constante SÍ cuesta un rebuild** (55 min). Por eso el Worker acepta varios a
la vez (`BUNDLE_TOKENS` separados por coma): permite solapar el viejo y el nuevo.

### 1.4 ⚠⚠ El token no está en git — y el repo es público (2026-08-21)

Al ir a publicar la rama saltó esto: `github.com/marcroger/appquarium-tv` tiene
`"private": false`, y `TvBundleAuth.cs` llevaba el token como `const` en claro, con la URL del
Worker documentada en los `.md` de al lado. El push habría deshecho la Fase 1 entera — y sin
vuelta atrás, porque un secreto publicado queda en forks, cachés y escáneres aunque se borre
después. GitHub push protection tampoco lo habría frenado: `aqtv_` es formato propio.

🧭 Que el token viaje dentro del `.wasm` público **no es lo mismo** y sigue estando asumido: ahí
hay que ir a buscarlo con `strings`. En GitHub es grepeable y sale en una búsqueda de código.

Cómo quedó:

| | |
|---|---|
| `Assets/Scripts/Core/TvBundleAuthSecret.cs` | El token real. **En `.gitignore`.** Un `partial method`. |
| `Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample` | La plantilla (ésta sí en git). |
| `Assets/Editor/TvAuthPreflight.cs` | Aborta **cualquier** build de WebGL que salga sin token. |

- **No costó rebuild**: el `.wasm` desplegado ya lleva el token y sigue siendo válido. El cambio
  sólo afecta al próximo build.
- **El historial de la rama se reescribió** (`git filter-branch`) para que el literal no quede en
  ninguno de los 8 commits. La rama era local, así que reescribirla no rompía nada de nadie.
- ⚠ **En un clon limpio hay que copiar la plantilla y poner el token**, o el player sale mudo de
  credencial: compila, arranca, y se queda sin un solo bundle. Es un `partial method` justamente
  para que la ausencia no rompa la compilación — el que tiene que fallar es el **build**.
- ⚠ El token real ya no está en ningún fichero versionado. Si se pierde la copia local, hay que
  rotarlo (token nuevo en `BUNDLE_TOKENS` sin quitar el viejo → rebuild → validar → retirar el
  viejo).

### 1.5 Validación

| Prueba | Resultado |
|---|---|
| Lógica del Worker (mocks) | **21/21** |
| Matriz contra el Worker desplegado | **12/12** |
| **Los 80 bundles bajados por el Worker** | **80/80**: HTTP 200, tamaño, **md5 contra el bucket**, `Cache-Control` y CORS |
| Receiver real en Chrome (`Tools/local-test.js`) | `AUTH: … (fuente=constante)` y **7/7 bundles** |
| Tele, 25 peces + 6 decos, 420 s | **421,0 s**, 31/31 bundles, 0 `FixMat`, **0 errores** |
| Tele, **después** de borrar los públicos | 150,8 s, **14/14 bundles**, 0 errores |

Rendimiento sin cambios respecto al 19-ago, como era de esperar (el contenido es idéntico y el
`Cache-Control` se conserva):

| | 19-ago | 20-ago |
|---|---|---|
| WASM heap | 159 MB | **159 MB, plano** (64→92→111→133→159 y ahí se queda) |
| FPS medio | 37 | **36** |

---

## 2. ⚠⚠ Lo que cambia en el día a día

**El deploy de bundles va a OTRO bucket.** `CLAUDE.md` ya está corregido, pero si alguien
ejecuta un comando viejo de memoria, **vuelve a publicar los assets**:

```powershell
# BIEN
aws s3 sync ServerData/WebGL/ s3://appquarium-tv-assets/ --profile r2assets ...
# MAL (los deja públicos otra vez)
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ --profile r2 ...
```

- Perfil nuevo: **`r2assets`** (el viejo `r2` sólo ve el bucket público).
- `Tools/r2_huerfanos.py` ya mira los dos frentes: bundles remotos en el privado, locales en el
  público. Informe actual: **80 vivos / 87,3 MB · 3 locales · 0 huérfanos**.

---

## 3. Marcha atrás

- Backup verificado por md5 en **`D:/dev/_backups/appquarium-tv/backup-antes-auth-2026-08-20/`**
  (fuera del scratchpad de sesión a propósito, que ése se borra): `catalog.bin` (`7f3d9ee5…`),
  `catalog.hash`, `settings.json`, `index.html` y los 4 ficheros del player del 17-ago.
  Se comprueba con `md5sum -c MD5SUMS.txt` — 8/8 OK el 20-ago.
  ⚠ Sin él **no hay vuelta atrás del player**: `webgl-output/` no está en git y rehacerlo son 55 min.
- ⚠ **El interruptor sigue siendo `StreamingAssets/aa/catalog.bin`**: subir el viejo devuelve el
  sistema a pedir los bundles a `r2.dev/bundles/`.
- ⚠ **Pero desde el borrado del 20-ago eso ya no basta**: los 80 bundles públicos están
  borrados. Revertir de verdad = copiarlos del privado al público (`aws s3 sync` entre buckets,
  ~8 s) **y luego** el catálogo y el player viejos.

---

## 4. Trampas nuevas — las caras

### 4.1 ⚠⚠ El `302` del spec de mayo no podía funcionar

El spec proponía que el Worker redirigiera a R2 con un `302`. **Imposible en un navegador:** el
receiver pide los bundles cross-origin y con header `Authorization`, o sea request **con
preflight**, y el spec de Fetch **prohíbe seguir un redirect cross-origin** en ese caso. El
Worker tiene que **servir los bytes**. Efecto secundario bueno: así el bucket puede ser privado
de verdad, que es lo que queríamos.

### 4.2 ⚠⚠ Con el catálogo público, un token en la URL no protege nada

`catalog.bin` se sirve desde la raíz pública (y tiene que seguir así). Si el token viajara dentro
del `RemoteLoadPath`, estaría **dentro del catálogo**, que cualquiera se baja. Por eso el token
va en un header puesto por código, aunque cueste un rebuild de player.

### 4.3 ⚠ Cloudflare bloquea por User-Agent ANTES de llegar al Worker

Una tanda de verificación con `python-urllib` dio **403 en los 80**, y el cuerpo era
`error code 1010` — protección anti-bot del edge, no mi código. Con un UA de navegador, 80/80.
El Chromium del Chromecast pasa (validado en la tele). Si algún día no pasara, **no hay regla de
WAF que tocar**: `workers.dev` no es una zona propia. La salida sería un dominio propio.

🧭 Regla: ante un 403 masivo, **leer el cuerpo antes de tocar el código**. Los 401/403 del Worker
dicen `Missing auth` / `Invalid token`; los de Cloudflare dicen `error code NNNN`.

### 4.4 ⚠ Addressables genera la URL del remote hash con DOBLE barra

`…/bundle//catalog_1.2.1.hash`. Hoy esa URL **no se pide nunca** (`m_DisableCatalogUpdateOnStart
= true`), pero si alguien cambia ese flag el 404 sería difícil de diagnosticar. El Worker colapsa
barras repetidas desde el 20-ago.

### 4.5 ⚠ Los nombres de bundle del disco local NO son los desplegados

El primer `smoke-test.sh` dio 3 fallos porque cogí un nombre de `ServerData/WebGL/` — donde hay
**287** `.bundle`, casi todos huérfanos de builds viejos. Con el nombre real del bucket, 12/12.
Es la misma trampa que ya costó dos cifras falsas en agosto: **para cualquier cosa que dependa
de un nombre de bundle, sacarlo del catálogo o del bucket, nunca de `ls`.**

---

## 5. Lo que queda

- [ ] **Fase 2 — JWT por usuario.** Todo escrito en `CAST_R2_AUTH_MOVIL.md`. Es trabajo del repo
      móvil + un rato de Worker. ⚠ Es una **migración**: el token constante tiene que seguir
      aceptándose hasta que la versión nueva esté adoptada, o a quien no actualice se le queda
      la tele vacía.
- [ ] 🎨 **Paridad visual con el móvil (abierto el 21-ago por el user)** — en la tele los colores
      se ven menos vivos y **el fondo casi en blanco y negro**. Ya está localizado sobre el papel:
      el móvil lleva **bloom 1,2 y saturación −15**, la TV **bloom OFF y saturación +18**, más
      tonemapping Neutral que el móvil no tiene; y los fondos van a WebGL con un override a
      **512 px** frente a los 2048 del móvil, con `renderScale 0,7` encima. **Todo en
      `CAST_PARIDAD_VISUAL.md`, con el protocolo de comparación y el precio de cada palanca.**
      ⚠ Medir antes de tocar: son hipótesis, no mediciones de pantalla.
- [ ] 🎨 **¿La sombra de una deco debe caer sobre el fondo?** (mismo doc, §2.5). No es un bug:
      sale de la geometría 2.5D — cuanto más al fondo está la deco, más arriba cae su sombra, y
      allí ya sólo hay telón. **Decisión de arte del user**, y lo que se toque afecta también a
      las sombras de los peces.
- [ ] 🎯 **Las MALLAS** — 11 decos a 100.000 triángulos: 77 % de los triángulos y 52 % del peso.
      Decimar a 50k → −14 MB; a 25k → −21 MB. **Cuesta calidad → decisión del user.**
- [ ] **Halo de la bioluminiscencia** (quad aditivo, shader CG legacy en Always Included).
- [ ] **Editar una deco ya colocada** no manda UPDATE — pide tocar el móvil.
- [ ] `ageScale` de peces: parte TV lista, falta build móvil.
- [ ] **Sacar los fondos del `.data`** (~0,7 MB): pide convertir carga síncrona en asíncrona.
- [ ] Contradicción `maxInactivity` — no tocar sin una tanda A/B.
- [ ] 🎯 **Cast Connect** — salida arquitectónica (app nativa Android TV reaprovechando Unity).

---

## 6. Estado desplegado

| | |
|---|---|
| Bundles | **80 = 87,3 MB** en `appquarium-tv-assets` (privado) · 0 huérfanos |
| Bucket público | `index.html`, `Build/` (38 MB), `StreamingAssets/`, `keepalive_black.mp4`, `silence.wav` + los 2 ficheros de catálogo de `bundles/` |
| Catálogo | md5 `90177197…` · hash `212caf2e…` |
| Player | `.wasm` **21.664.370** · `.data` **15.942.355** (build del 20-ago) |
| Sello receiver | **`rcv 2026-08-20 auth`** |
| Worker | `appquarium-assets.appquarium.workers.dev` · secret `BUNDLE_TOKENS` |
| Rama | `feat/r2-auth-worker` — mergeada a `main` y pusheada el **2026-08-21**, ya sin el token dentro |
