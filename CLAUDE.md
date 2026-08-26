# CLAUDE.md — Appquarium TV (Cast Receiver)

Instrucciones de contexto para Claude Code en este proyecto.

---

## Proyecto

**Appquarium TV** — receiver Cast WebGL para acuario digital "Appquarium" (Android app móvil).

El móvil envía el estado del tanque vía Google Cast SDK; este proyecto **renderiza ese estado** en cualquier Chromecast, Android TV, Google TV o Cast Built-In (Xiaomi TV Box S validado/objetivo).

- Motor: **Unity 6 (URP)** | IDE: Rider | Build target: **WebGL**
- Cast App ID Published: **`8F6C873F`** (Unlisted, funciona en cualquier Chromecast/Android TV sin registrar device)
- Receiver hosting: **Cloudflare R2** (`https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/`)
- Repo: `github.com/marcroger/appquarium-tv`
- Móvil (proyecto separado, NO tocar): `D:\dev\appquarium-unity\`

---

## ⚠ Reglas hard

1. **NO TOCAR repo móvil** (`D:\dev\appquarium-unity\`). Si necesitas cambios ahí, parar y pedir al user.
2. **NO HACER PUSH** sin que el user lo pida explícitamente.
3. **NO MERGEAR a main sin user confirmation** — branches feature quedan locales hasta validación.
4. **Build WebGL es LENTO** (1-3h en caliente, 16h en frío). NO hacer rebuild "para verificar" — pensar bien antes de `Build Player Content`.
5. **Si encuentras ambigüedad** entre docs y código, parar y preguntar — no inventar interpretaciones.

---

## 📚 Documentación esencial (LEER EN ESTE ORDEN)

| Doc | Cuándo |
|---|---|
| [`CAST_NEXT_SESSION_2026-08-26.md`](CAST_NEXT_SESSION_2026-08-26.md) | ⭐⭐ **EMPEZAR AQUÍ.** Cierre del 25-ago: la escena deja de verse como assets separados (niebla de agua, tono de peces, `renderScale` 1:1). Todo desplegado y validado **menos `renderScale 0,75`**, que se quedó sin tanda. Trae las 3 trampas del día y 2 afirmaciones mías que resultaron falsas. |
| [`CAST_NEXT_SESSION_2026-08-25.md`](CAST_NEXT_SESSION_2026-08-25.md) | Cierre del 24-ago: Cierre del 24-ago: el ciclo día/noche por fin llega a decos y peces. **Construido y verificado, NO desplegado.** Trae la trampa del shader horneado en el bundle y por qué el deploy va sólo con `Build/`. |
| [`CAST_NEXT_SESSION_2026-08-22.md`](CAST_NEXT_SESSION_2026-08-22.md) | Cierre del 21-ago: la TV recuperó el color (llevaba desde siempre sin aplicar ningún grado, por 5 causas encadenadas). Protocolo auditado 11/11 y coste de URP medido. |
| [`CAST_PARIDAD_VISUAL.md`](CAST_PARIDAD_VISUAL.md) | 🎨 **El detalle y las pruebas** de lo anterior: qué se descartó con medida, qué reglas salieron, y lo que queda del fondo. |
| [`CAST_NEXT_SESSION_2026-08-21.md`](CAST_NEXT_SESSION_2026-08-21.md) | Cierre del 20-ago: Cierre del 20-ago: los bundles ya están detrás del Worker. Qué cambió en el deploy y qué queda. |
| [`CAST_R2_AUTH_MOVIL.md`](CAST_R2_AUTH_MOVIL.md) | 📄 **Para la sesión del repo MÓVIL.** Contrato de la Fase 2 (JWT por usuario): campo del JSON, claims, orden de migración. |
| [`CAST_NEXT_SESSION_2026-08-20.md`](CAST_NEXT_SESSION_2026-08-20.md) | Estado al cierre del 19-ago, Estado al cierre del 19-ago, pendientes reales y las trampas caras (bundle local que cambia de hash, y los 3 fallos de la conversión de materiales). |
| [`ESTADO_PRODUCCION_2026-08-19.md`](ESTADO_PRODUCCION_2026-08-19.md) | 📊 **Foto de estado y valoración para producción.** Qué está validado, y los 2 puntos que faltan antes de difundir (bucket abierto + rigs de diagnóstico servidos). |
| [`DECOS_PESO_PARA_MOVIL.md`](DECOS_PESO_PARA_MOVIL.md) | 📄 **Para leer en el repo MÓVIL.** Las 3 palancas de peso de decos, qué se reutiliza, qué cambiar (DXT1→ASTC/ETC2) y las 7 trampas ya pagadas. |
| [`CAST_UPDATES.md`](CAST_UPDATES.md) | ⭐ Protocolo UPDATE en tiempo real — tipos, payloads, gestión memoria, calls mobile pendientes. |
| [`CAST_NETFLIX_SPEC.md`](CAST_NETFLIX_SPEC.md) | Spec ejecutable para Fase A.1 — contrato del refactor Netflix. 10 secciones. |
| [`BUILD_REPORT_2026-05-25.md`](BUILD_REPORT_2026-05-25.md) | Diagnóstico del build de 411MB + análisis duplicación. |
| [`ADDRESSABLES_ROADMAP.md`](ADDRESSABLES_ROADMAP.md) | Estado de bundles + cómo se organizan los Addressables. |
| [`SYNC_NOTES.md`](SYNC_NOTES.md) | Cómo sincronizar scripts/SOs desde mobile y qué NO sincronizar. |
| [`HANDOFF.md`](HANDOFF.md) | Setup inicial del proyecto (histórico — F1..F8). |

---

## Arquitectura runtime

```
Mobile (sender)
  └── CastManager (mobile-only)
      └── envía TvAquariumState JSON + UPDATE messages vía Cast SDK
           │
           ▼
TV (este proyecto)
  └── TvScene
      ├── CastReceiver        ← recibe mensajes Cast SDK, deserializa JSON
      ├── TvSceneBootstrap    ← parsea state, LoadAssetAsync por key, espera, inicializa
      ├── AquariumManager     ← versión SLIM (sin BreedingManager/IAP/Ad/Save)
      ├── DecorationPlacer    ← renderiza decos (compartido con mobile)
      ├── FishSpawner         ← spawna peces (compartido con mobile)
      └── AmbientModeController, AquariumCameraController, etc.
```

**Lo que NO existe en TV (mobile-only):** UI, IAP, Ad, SaveSystem, BreedingManager, InputHandler, FieldGuide, Localization (TV asume inglés/idioma fijo).
**TV tiene su propio:** `TvFoodManager` (stub del FoodManager mobile — feed visual + auto-feed).

---

## Sync desde mobile

**⚠ NO sincronices "por si acaso" antes de un build.** Este doc decía "SIEMPRE antes de
cualquier build/deploy", y era la instrucción más peligrosa que tenía: `SyncFromMobile.ps1`
copiaba carpetas enteras, y las versiones móviles de `AmbientModeController.cs` y
`DecorationPlacer.cs` llaman a `CastManager.Instance`, que **en TV no existe ni tiene stub**
→ el proyecto deja de compilar. De paso se llevaba por delante el lote visual validado.
El script se acotó el 2026-08-15 (ver sus comentarios). Sincroniza sólo cuando el móvil haya
cambiado algo que TV necesita, mira el `-DryRun` primero, y revisa los avisos uno a uno.

```powershell
# Listar diffs (no copia)
.\Tools\SyncFromMobile.ps1 -DryRun

# Copiar con confirmación
.\Tools\SyncFromMobile.ps1

# Copiar todo sin prompt
.\Tools\SyncFromMobile.ps1 -Yes
```

O desde Unity Editor: `Appquarium TV → 🔄 Sync from Mobile (...)`.

Detalle de qué se sincroniza, qué NO, y por qué: ver `SYNC_NOTES.md`.

**Tras sync:** Unity TV reimporta automáticamente. Verificar Console sin errores antes de cualquier build.

**⚠ CRÍTICO tras sync — verificar audio .meta files:**
Los `.meta` de mobile tienen `loadType:0` (Decompress on Load, OK en Android 6 GB).
En WebGL/Cast esto causa **OOM → Chrome muere → pantalla azul sin peces** (bug recurrente).
Después de cada sync, verificar `Assets/Resources/Audio/*.meta`:
- `loadType: 0` → cambiar a `loadType: 2` — si no, rebuild + deploy
  ⚠ **`2` es `Streaming` en el enum de Unity, NO "Compressed in Memory"** (que es el `1`).
  Este doc lo etiquetaba mal y el riesgo es que alguien lo "corrija" a 1. El valor validado
  contra el device es el **2**; lo que importa es que NO sea 0 (Decompress on Load = OOM).
- Referencia correcta: `ambient_water.wav.meta` (loadType:2, quality:0.7, forceToMono:0)
- `ambient_bubbles.wav.meta` (loadType:2, forceToMono:1, quality:0.7, 3D:0)

**Desde 2026-08-15 esto ya no depende de que alguien se acuerde:** `TvProdBuild.BuildProd`
ejecuta `PreflightAudio()` y **aborta el build** si falta un clip o si alguno tiene
`loadType:0`. Un build mudo falla en vez de salir "bien".

**Los 3 clips de ambiente SÍ van a git** (agua 3,9 MB · música 3,8 MB · burbujas 5,5 MB).
Antes estaban en `.gitignore` y eso costó 2 de los 3 canales durante ~2 meses: los `.wav`
desaparecieron del disco, git no los veía, `AudioManager` falla en silencio (sólo `Debug.Log`,
que NO viaja por el canal Cast) y el build del 12-ago salió con música y nada más.
El original íntegro de burbujas (110 MB, 10 min) está en el repo móvil, en `Assets/Audio/`;
en TV va recortado a un bucle de 60 s en mono con crossfade (~0,8 MB en el build en vez de ~8 MB).

---

## Build pipeline (resumen)

### ⚠⚠ Los bundles ya NO son públicos (2026-08-20)

Los 80 bundles remotos viven en el bucket **privado** `appquarium-tv-assets` (raíz, sin prefijo,
sin dominio público) y los sirve un Worker de Cloudflare que exige `Authorization: Bearer`:

```
https://appquarium-assets.appquarium.workers.dev/bundle/<fichero>.bundle
```

- Código y despliegue del Worker: **`Tools/r2-auth-worker/`** (README con el rollback).
- El receiver pone la cabecera en `TvBundleAuth.cs` (hook `Addressables.WebRequestOverride`,
  instalado en `Awake` de `TvSceneBootstrap`). Busca por **ruta** (`/bundle/`), no por host.
- **Fase 1 = token constante** dentro del `.wasm`. No es DRM: es lo que convierte «cualquiera
  con la URL se lo baja» en «hay que atacar el producto», que es lo que las licencias esperan.
- ⚠⚠ **El token NO está en git y este repo es PÚBLICO** (2026-08-21). Vive en
  `Assets/Scripts/Core/TvBundleAuthSecret.cs`, que está en `.gitignore`; la plantilla es
  `Tools/r2-auth-worker/TvBundleAuthSecret.cs.sample`. **En un clon limpio hay que copiarla y
  poner el token real**, o el player sale sin credencial. No peta: se queda sin bundles y la
  tele muestra el acuario vacío — por eso `TvAuthPreflight` **aborta cualquier build de WebGL**
  que no lleve token (misma lógica que el preflight de audio).
- **Fase 2** (JWT por usuario) **no necesitará rebuild de player**: `TvAquariumState.castJwt` ya
  existe y el hook lo prefiere. Contrato en `CAST_R2_AUTH_MOVIL.md`.
- ⚠ **Rotar el token constante cuesta un rebuild de player.** El Worker acepta varios a la vez
  (`BUNDLE_TOKENS` separados por coma) para poder solapar el viejo y el nuevo.
- ⚠ **Lo que sigue público y tiene que seguirlo:** `index.html` y el player. El device de Cast
  pide la URL del receiver con el navegador y **sin credenciales**; no hay forma de autenticar
  ese primer fetch. `catalog.bin` también: permite enumerar nombres, no descargar bytes.
- ⚠ Cloudflare tiene **protección anti-bot delante del Worker**: un `curl` con User-Agent de
  `python-urllib` recibe `error code 1010` **antes** de llegar al código. El Chromium del
  Chromecast pasa (validado). Si algún día no pasara, no hay regla de WAF que tocar —
  `workers.dev` no es una zona propia; la salida sería ponerle un dominio propio.

### ⚠⚠ La TV usa URP desde el 2026-08-21 (antes NO usaba ninguno)

Durante toda la vida del proyecto, `GraphicsSettings` apuntaba a un URP asset **que no existía**,
así que la TV renderizaba con el pipeline **built-in**. Consecuencia: el `Volume` de
`PostProcessingSetup` no afectaba a nada — **ni bloom, ni tonemapping, ni saturación, ni
contraste** — y `renderScale` tampoco se aplicaba. Verificado en el player desplegado, no sólo en
el Editor. Detalle completo y pruebas: **`CAST_PARIDAD_VISUAL.md` §0**.

- El pipeline vive en **`Assets/Settings/TvRenderPipeline.asset`** (+ `TvUniversalRenderer.asset`).
  Se crea y se enciende/apaga con `Appquarium TV → 🎛 Pipeline — …` (`TvUrpSetup`).
- ⚠ **Al crear un `UniversalRendererData` por código hay que rellenarle `postProcessData`**: si se
  queda a null, **URP se salta todo el post-proceso en silencio**. `TvUrpSetup` lo rellena y lo
  **verifica**, con error si falta.
- ⚠ **`renderPostProcessing` de la cámara viene en `false`** por defecto en URP. Lo enciende
  `TvSceneBootstrap`; sin esa línea no hay grado aunque exista el pipeline.
- ⚠⚠ **Un `Volume` con `Add<T>(true)` marca TODOS los parámetros como override.** El de la barra
  LED (`TankLightingController`, prioridad 11) lo hacía y machacaba saturación y contraste del
  grado. Va con `Add<T>(false)` y sólo manda en los dos que declara. **No volver a poner `true`.**
- El receiver imprime al arrancar `RP: <asset> scale= hdr= msaa= sombras=` — sirve para saber
  **qué build está corriendo de verdad** en la tele, que cachea.
- Coste medido (25 peces, 420 s): **FPS 37 contra 37** — URP no cuesta FPS. La memoria sube un
  escalón del heap geométrico (159 → 191 MB).

### ⚠⚠ Un shader tocado NO llega a las decos con sólo rebuildear el player (2026-08-24)

Un material que sale de un **AssetBundle trae su propia copia del shader**, compilada cuando se
construyó el bundle. Sigue llamándose `Appquarium/DecoLit`, así que la guarda «ya es device-safe»
de `FixNonURPMaterials` lo deja pasar — pero es el bytecode del día en que se construyó el bundle,
y no conoce las propiedades nuevas.

- **Costó un build entero de 55 min**: el ciclo día/noche salió «hecho» y las decos siguieron
  planas (47,97 de luminancia en las 8 fases), mientras una sonda en el Editor demostraba que el
  global sí llegaba al shader.
- **`FishSpawner.cs:341-360` ya lo resolvía para los peces** desde hace meses, con el razonamiento
  escrito en su comentario. A las decos nunca se les aplicó.
- Ahora `DecorationPlacer.FixNonURPMaterials` **reapunta** (`mat.shader = Shader.Find(...)`, que
  devuelve la copia del player) y lo **cuenta**: `AQUARIUM READY … | shaders reapuntados: N`.
- 🧭 La alternativa era reconstruir los 80 bundles: **68 min + 87 MB de subida**. Reapuntar es
  gratis y sobrevive a cualquier cambio futuro de shader.

### ⚠⚠ Añadir un shader nuevo: GUID hexadecimal + Always Included (2026-08-25)

Dos trampas encadenadas, y las dos fallan **en silencio** — nada peta, nada sale magenta, el
shader simplemente no existe en el device y `Shader.Find` devuelve `null`.

1. **El GUID del `.meta` DEBE ser hexadecimal** (`0-9a-f`). Se crearon `SubstrateFog` y
   `FishFin` con GUIDs "legibles" (`5ub57ra7e…`, `f15hf1n0…`) y **`u`, `r`, `h`, `n` no son
   hex**. Unity **reescribió las entradas de `m_AlwaysIncludedShaders` como `{fileID: 0}`** y
   stripeó los shaders. **Costó un build.** La convención buena ya existía: `DecoLit` usa
   `dec011710000000000000000000000ab` — leetspeak dentro del alfabeto hex.
2. **Hay que registrarlo en `ProjectSettings/GraphicsSettings.asset`**
   (`m_AlwaysIncludedShaders`), o el build lo stripea.

⚠ Al arreglar el GUID, buscar-y-reemplazar el viejo **no encuentra nada** (Unity ya lo borró):
hay que reponer las líneas `- {fileID: 0}`. Comprobar que el GUID **nuevo esté**, no que el
viejo no esté.

🧭 **Se caza en 60 s con la sonda**, que reporta el shader REAL de cada renderer por el canal
Cast: `node Tools/cast-headless.js --ip <IP> --fish 2 --decos deco_anchor --update ambient=day@45`
→ `sonda[day] TankFloor … shader='Appquarium/SubstrateFog'` (o `'Sprites/Default'` si no llegó).

### 🌊 Niebla de agua y tono de peces (2026-08-25) — el «assets separados»

Medido en la tele: los peces iban a croma perceptual C* **42,6** contra **23,1** del agua, y las
decos ya estaban integradas (**25,5**). O sea, **el problema eran los peces, no el decorado**.
Y ningún shader leía la profundidad, así que un pez del fondo tenía el mismo contraste que uno
pegado al cristal.

Constantes en `TvSceneBootstrap` (`PublicarAspectoDelAgua`), elegidas sobre el device:
`TonoDesat 0,32` · `TonoDim 0,16` · `NieblaDens 0,30` · **`DecoNiebla 0,25`** · rango
`[ZFront, ZBack] = [-1,0 · +4,2]`.

- ⚠ **La cámara es ORTOGRÁFICA** → la distancia a cámara no sirve. Se usa la **Z del mundo**.
- El **telón de fondo (Z=5,0) queda fuera** del rango a propósito: ya representa la lejanía.
- Como el suelo llega a Z=+4,2 y ahí la niebla satura, **la juntura suelo/fondo se funde de
  regalo** — era el otro problema medido (salto de ×12 a ×30 en 40 píxeles).
- **Las decos llevan multiplicador propio** (`_AqDecoFogMul`). ⚠⚠ El primer intento fue acortar
  el rango de Z para dejarlas fuera; **no vale**: se colocan a cualquier profundidad hasta
  `ZDecoBack=+3,0`. **Un corte por profundidad no puede proteger algo que se mueve en
  profundidad.** El caso que manda es el **ancla, acromática**: croma 1,9 → 8,4 (con 0,25) →
  17,3 (con 1,0, ya turquesa). La estrella azul apenas se inmuta: su color ya está cerca del agua.
- **Las aletas NO eran `FishUnlit`, eran `Sprites/Default`** → `Appquarium/FishFin`. El suelo
  igual → `Appquarium/SubstrateFog`. Ambos clonan `Sprites/Default` al pie de la letra (blend
  premultiplicado, `ZWrite Off`, `Cull Off`, cola `Transparent`) y sólo añaden la niebla.
- El color del agua sale del **`surfaceTint` del preset de fondo** y se republica en cada
  `change_bg`.

**Afinar sin gastar builds — mensaje `FOG`** (mismo espíritu que `GRADE`):
```
--raw 'FOG={"auto":true,"density":0.30,"decoFog":0.25,"fishDesat":0.32,"fishDim":0.16}@70'
```
Campos: `r/g/b` · `density` · `z0/z1` · `fishDim` · `fishDesat` · `decoFog` · `auto`. Los que no
vengan se quedan como están. **Rollback sin build:** `{"density":0,"fishDesat":0,"fishDim":0}`.
⚠ Todos los globales valen **0 = sin cambio**: un global que nadie publica vale 0, nunca 1.
Coste medido: FPS 29-43 (avg 32-35), igual que sin niebla.

⚠ **Medir esto sobre los peces desde capturas sueltas NO es fiable**: entran y salen del encuadre
y esa varianza domina. Y una máscara por umbral (`croma > 35`) **tiene sesgo de selección** —
escoge «lo más saturado que haya» y su media no se mueve aunque desatures un 35 %. Usar objetos
**FIJOS** (las decos).

### ⚠⚠ La tele reporta 2560x1440, NO 1920x1080 (2026-08-25)

`Screen.width x Screen.height` en el Xiaomi es **2560x1440**. Durante toda la vida del proyecto
el comentario de `TvSceneBootstrap` decía que `renderScale 0,70` era «49 % de píxeles» dando por
hecho un panel de 1080p — **falso, y nunca se comprobó**. Con 2560x1440, el 0,70 renderizaba
**1792x1008**, que es el **93 % LINEAL** de 1080p.

Consecuencia: **la `renderScale` apenas estaba costando nitidez**. Si la diferencia con el móvil
que reporta el user sigue ahí, hay que buscarla en el **grado** (la TV lleva tonemapping +
`sat +18`; el móvil `bloom 1,2` / `sat -15`), no en la resolución.

🎯 **`renderScale = 0,75` es el único valor no arbitrario**: `2560 x 0,75 = 1920` y
`1440 x 0,75 = 1080`, o sea **1:1 con lo que el device entrega**. Por debajo se renderiza de
menos y se estira; por encima se tira trabajo (a 1,0 son 2560x1440 para sacar 1080p).

Coste medido en el device (12 peces + 3 decos, una sesión por escala, HUD leído siempre al mismo
`SESSION`):

| escala | resolución | FPS avg |
|---|---|---|
| 0,70 | 1792x1008 | 35 |
| **0,75** | **1920x1080** | **35** ← gratis |
| 0,85 | 2176x1224 | 34 |
| 1,00 | 2560x1440 | 33 |

**Ajustable en caliente**, sin gastar builds: `--raw 'GRADE={"renderScale":0.85}@50'`. Reporta la
resolución efectiva: `RENDERSCALE: 0.70 → 0.85 (2176x1224 sobre 2560x1440)`.

⚠⚠ **Para comparar escalas NO sirve barrerlas dentro de una sesión.** El `FPS avg` del HUD es
**acumulativo desde el arranque**, así que sube monótonamente pase lo que pase. El primer intento
dio «0,70 → 28 fps» al principio y «0,70 → 41 fps» al final de la misma tanda: pura deriva.
Hacen falta **sesiones separadas leídas al mismo `SESSION`**.

### ⚠⚠ El catálogo local YA NO cuadra con R2 (2026-08-24)

Un build de player regenera `webgl-output/StreamingAssets/aa/` con **hashes de bundle distintos**
a los que hay desplegados (fish `b5a9bb42…` local contra `724dbae8…` en R2). **Subir
`StreamingAssets/` deja la tele vacía**: los 80 bundles dejan de encontrarse. Comprobado en vivo el
24-ago (7/7 `RemoteProviderException`).

**Al desplegar sólo un cambio de código: subir `Build/` + `index.html` y NADA MÁS.** Si algún día
hay que volver a cuadrarlos, la vía es un New Build de Addressables + redespliegue de los 80
bundles, nunca subir el catálogo suelto.

### ⚠⚠ Un id de preset que no existe NO da error: el receiver lo confirma (2026-08-26)

`change_bg` / `change_sub` / `change_light` **confirmaban cualquier id**. `SetPreset` y
`SetSubstrate` se plantan en un `Debug.LogWarning` —que **no viaja por el canal Cast**— y
vuelven sin tocar nada, pero el handler logueaba `change_sub: sub_black` igual y encima
guardaba el id fantasma en `SaveData`.

Había **seis ids fantasma** repartidos por el proyecto: `bg_ocean`, `bg_reef`, `bg_sunset`,
`sub_black`, `sub_coral` (y `light_green`, que sí es legítimo: preset retirado que
`AquariumManager` migra a `light_white`). Consecuencias medidas:

- La tecla **B** del `?devtest=1` no hacía nada en **3 de cada 6** pulsaciones, y la **S** en
  **2 de cada 4**. Con eso se dio por buena una prueba entera el 25-ago.
- **`Tools/test-updates.js` llevaba MESES en verde** mandando `bg_ocean`: comprobaba que el
  receiver hiciera **eco** del id, no que el fondo cambiara. La prueba de que no cambiaba
  estaba en la línea de al lado —`agua: … (bg_kelp)`— y nadie la miró.

Lo que hay ahora:

1. Los tres handlers **validan** contra el array de C# (`ERR change_bg: id desconocido 'x' —
   válidos: …`) y **releen el estado** después de aplicar en vez de reportar la intención.
2. `DEV_BGS`/`DEV_SUBS` del `index.html` con ids reales.
3. `test-updates.js` comprueba el **efecto** (para el fondo, contra `agua: … (<id>)`) y añade
   tres tests **negativos** que exigen el `ERR`. ⚠ Esos tres fallan contra un player anterior
   al 2026-08-26 — a propósito.
4. **`node Tools/check_preset_ids.js`** — guarda sin Unity, sin navegador y sin tele: lee los
   ids de los arrays de C# y revisa receiver y herramientas. Sale 1 si hay fantasmas.

🧭 **Regla:** no comprobar nunca que el receiver **repita** lo que le mandaste. Comprobar
contra algo que lea el estado real.

**El mismo patrón estaba en dos sitios más** (auditado el mismo día, ya arreglado):
- **`add_fish` decía «spawned» aunque `SpawnFish` devolviera null.** El `if (agent != null)`
  protegía las dos llamadas de debajo, pero el log salía igual. Ahora responde `ERR add_fish:
  … SpawnFish devolvió null` y, si va bien, dice **cuántos peces hay en el tanque**.
- **`add_deco` tiraba el `bool` que devuelve `PlaceAt`**: una deco rechazada (sin sitio) se
  confirmaba como colocada.
- Y los `yield break` mudos de payload sin `speciesId`/`itemId` ahora dicen por qué se van.

### ⚠⚠ El rig local servía un catálogo que R2 no tiene (2026-08-26)

`Tools/static-server.js` + `?devtest=1` (puerto 3001) llevaba roto **desde el último build de
player**: los 7 bundles daban **404** y el acuario salía vacío. No era el token, ni CORS, ni el
anti-bot de Cloudflare — el Worker respondía **404 con CORS puesto y preflight 204**, o sea
«ese bundle no está en el bucket».

Es la trampa de la sección anterior mordiendo al rig local: el servidor servía
`webgl-output/StreamingAssets/aa/catalog.bin` **del disco**, que pide hashes que un build de
player regeneró y que **nunca se despliegan**.

⚠⚠ **Los dos catálogos pesan EXACTAMENTE lo mismo (44.826 bytes)** y sólo cambian los hashes
de dentro. Comparar por tamaño —o el «suele ser idéntico y no hace falta tocarlo» que decía
este mismo doc— **no lo detecta**.

**Arreglado:** el servidor sirve `/StreamingAssets/aa/*` **desde R2** (lo que ve la tele) y el
resto del disco, que es justo lo que se quiere probar. `--local-catalog` vuelve a lo de antes.
Tras el cambio: **7/7 bundles OK** y `test-updates.js` **6/6**.

```bash
node Tools/static-server.js       # deja corriendo el receiver en localhost:3001
node Tools/test-updates.js        # los 9 tests de los handlers UPDATE
node Tools/check_preset_ids.js    # ids fantasma
```

### Estado actual — 2026-08-19 ⭐

⚠ **Cifras del 19-ago. El 20-ago se rebuildeó el player** (hook de auth): `.data` = **15.942.355** ·
`.wasm` = **21.664.370** · sello **`rcv 2026-08-20 auth`**. Y los bundles ya **no** están en R2
público: ver la sección de arriba.
Bundles: **80 vivos = 87,3 MB**, 0 huérfanos · **3 bundles locales** (0,5 MB) en
`StreamingAssets/aa/WebGL/`. Todo validado en el Xiaomi TV Box S.

**Decos: 149,8 → 61,03 MB (−59,3 %)** en dos palancas:
1. **Texturas embebidas en GLB** → DXT1 sueltas (20 decos). Problema de GLTFast, que decodifica a
   RGBA32 y se salta el override de plataforma.
2. **Shader del material** (21 decos, 2026-08-19): Unity sólo empaqueta las texturas de propiedades
   que **declara el shader activo**. Pasar los materiales de URP/Lit a `Appquarium/DecoLit` deja
   fuera normal/metallic/AO/emission — que el runtime **ya descartaba** en
   `DecorationPlacer.FixNonURPMaterials()`. Efecto extra: esas decos dejan de generar `FixMat`.

⚠ **Las cifras de tamaño de este doc son MB DECIMALES (10^6)**, que es lo que informa
`Tools/r2_huerfanos.py`. Medir en MiB (2^20) da un ~4,9 % menos y parece que R2 y el disco local no
cuadran; el 18-ago se persiguió esa falsa discrepancia hasta comprobar, fichero a fichero, que eran
idénticos.

🧭 **El % que rinde el paso a DXT1 lo predice la proporción textura/malla**, así que conviene contar
triángulos antes de estimar (`Appquarium TV → 📐 Informe de mallas por deco`). Medido con texturas
idénticas: `lambis_shell` 12.498 triángulos → −76,9 %; `linckia_laevigata` 100.000 → −36,8 %.

🎯 **La palanca que queda son las MALLAS.** 11 decos están clavadas en ~100.000 triángulos (tope de
decimación del proveedor): son el 77 % de los triángulos y el 52 % del peso, y en ellas la malla es
~79 % de su bundle. Decimarlas a 50k/25k daría −14/−21 MB. Cuesta calidad → decisión del user.

⚠⚠ **Al desplegar sólo bundles, comprobar los bundles LOCALES.** El `shared_local_assets_all_<hash>`
cambia de hash en casi cada build; vive en `StreamingAssets/aa/WebGL/` (se sirve por HTTP, **no hace
falta rebuild de player**) y si el catálogo pide uno que no está, las decos que dependan de él
fallan con **Dependency Exception**. `Tools/r2_huerfanos.py` ya revisa esa ruta y avisa.

Cómo optimizar una deco nueva: `python Tools/extract_glb_textures.py <glb>` y luego
`Appquarium TV → 🗜 Optimizar deco seleccionada` (o el lote). ⚠ La señal de que el prefab
optimizado se está usando es que **NO aparece ningún `FixMat`** sobre esa deco en el log.

**Rendimiento — remedido el 2026-08-19** con el mismo protocolo del 15-ago, ya con las decos
optimizadas (25 peces + 6 decos, 420 s):

| | 15-ago | 19-ago |
|---|---|---|
| WASM heap | 191 MB | **159 MB (−16,8 %)** |
| FPS medio | 37 | **37** |
| Sesión | 420 s, 0 cortes | **421 s, 0 errores** |

Los 32 MB de heap que se ganan son exactamente el peso que perdieron las decos.

**Histórico — validado el 2026-08-15**, con acuario real y sin reiniciar la caja:

| | 12 peces + 6 decos | 25 peces + 6 decos |
|---|---|---|
| FPS (medio / peor) | **45 / 36** | **37 / 17** |
| WASM heap | 133 MB, plano | 191 MB, plano |
| Memoria libre del sistema | — | 19 % (banda estable validada: 22-24 %; peligro ~10 %) |
| Sesión | 900 s, 0 cortes | 420 s, 0 cortes |

Sale a **~4,5 MB y ~0,6 fps por pez**. ⚠ Ese cuadro decía «una deco cuesta 8-13 MB»: **ya no**.
Tras la optimización del 17-ago una deco va de **1,3 a 4,5 MB** (media 1,4 MB en las 18 tocadas),
así que **los peces y las decos ya pesan parecido** y el «techo son las decos» dejó de ser cierto.
Sombras de decos y de peces **visibles y medidas** (ancla −106 de contraste, roca −130, pez −22).

⚠ El `.wasm` depende de `Code Optimization = DiskSizeLTO`, que **no está en git**
(vive en `Library/EditorUserBuildSettings.asset`). `TvProdBuild.BuildProd` lo fuerza por código;
si construyes por GUI, comprobarlo antes con `Appquarium TV → 📏 Ver Code Optimization del WASM`.

**Histórico — Build 2026-06-22 (calidad visual + SMAA), sigue vigente:**

**Build 2026-06-22 — DEPLOYADO (calidad visual + SMAA):**
- `PostProcessingSetup.cs` — Tonemapping Neutral (evita highlights lavados) + saturation +18 + contrast +10
- `TvSceneBootstrap.cs` — SMAA Low en cámara principal (bordes menos dentados, 1 pass extra)
- Valores serializados en escena: `enableTonemapping=true`, `saturation=18`, `contrast=10`, `postExposure=0.05`
- Panel debug confirma: `PostFX: bloom=OFF tm=Neutral sat=18 con=10`

**Build 2026-06-22 (también en R2) — disconnect diag + Fase B:**
- `index.html` — logs `e.reason` + duración de sesión en `SENDER_DISCONNECTED`
- `index.html` — overlay visual "Sender desconectado" con contador en TV
- `index.html` — keepalive receiver→sender 60s (logea cada 5 ticks o al fallar)
- `CastReceiver.cs` — PING/KEEPALIVE mensajes silenciados (no spam en log)
- `ctx.start({ disableIdleTimeout:true, maxInactivity:3600 })` — ⚠ SDK rechaza valores ≤5; usar 3600
- Fase B mobile: `SendUpdate()` conectado en todos los puntos de acción (ver `CAST_UPDATES.md`)

**Build 2026-06-20 — base (audio OOM resuelto, tint corales, feed visual):**
- `TvFoodManager.cs` — feed visual (pellets + peces nadan a comer) + auto-feed cada 4 min
- `ambient_bubbles.wav` loadType:2, forceToMono:1 ✅
- Handlers UPDATE: `add_fish`, `remove_fish`, `add_deco`, `remove_deco`, `change_bg`, `change_sub`, `change_light`
- `DecorationPlacer.PlaceAt()` aplica tint a `_BaseColor` Y `_Color`

**✅ RESUELTO — Disconnect sender (2026-08-10).** ⚠ La causa que este doc daba era **falsa**:
no era Android Doze. Era **presión de memoria del sistema por el tamaño del `.wasm`**; se
arregló bajándolo de 44,2 a 25,4 MB (−7 paquetes de runtime + `DiskSizeLTO`). Validado el
2026-08-15 con sesiones de 900 s y 420 s, 0 cortes.
(El `PARTIAL_WAKE_LOCK` y el PING cada 60 s que proponía como fix ya estaban en el móvil
desde antes, en `CastPlugin.java` — no eran lo que faltaba.)

**settings.json auto-parcheado** por `TvBuildPostprocess.cs` tras cada build — no intervención manual.
**SBP cache incremental:** 1 pez cold = 2:01h | 2 peces incremental = 1:39h | todo cacheado = 9s.
**Workflow actual:** New Build + deploy bundles+catalog. Player solo se rebuilda si cambia C#.

**Tras sync mobile:** re-ejecutar `Appquarium TV → ★ Assign Fish Prefabs` (los SOs se resetean).

Ver `BUILD_REPORT_2026-05-28.md` para histórico de la Fase A.1.

### Player Settings WebGL activos (tras sesión 2026-05-28)

| Setting | Valor |
|---|---|
| Compression Format | Disabled |
| Exception Support | None |
| WebAssembly 2023 | OFF |
| Initial Memory Size | **64 MB** |
| Maximum Memory Size | 512 MB |
| Memory Growth Mode | Geometric (0.2 step, 96 MB cap) |
| Strip Engine Code | ON |
| Managed Stripping Level | **High** (`WebGL: 3`) — verificado en `ProjectSettings.asset:919` |
| IL2CPP Code Generation | **OptimizeSize** |

> **✅ 2026-08-16 — HECHO Y VALIDADO: subido de `Minimal` a `High`.** El `.wasm` bajó de 25,4 a
> **21,7 MB (−14,8 %)**, sin `TypeLoadException` y sin nada roto visualmente en la tele.
> `Assets/link.xml` preserva los tipos de URP que High podría romper.
>
> ⚠ **Historia, para que no se repita:** este doc afirmó durante meses que era High cuando el
> valor real era `Minimal` (`WebGL: 4`, y en el enum de Unity 4 = Minimal, High es 3). Se descubrió
> el 15-ago mirando el output del linker: `Unity.Addressables.dll` pesaba lo mismo antes y después
> de strippear. **Ahora está en `ProjectSettings` (versionado) Y forzado por código en
> `TvProdBuild` antes de construir**, así que no depende de que nadie se acuerde.

### Comandos clave

```powershell
# Verificar el Worker (pre-req crítico) — los bundles YA NO están en el bucket público
Tools/r2-auth-worker/smoke-test.sh https://appquarium-assets.appquarium.workers.dev <TOKEN> <bundle>.bundle

# Build bundles: Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script
# ❌ NO usar "Update a Previous Build" — es para workflows CCD (Unity Cloud), no para R2 self-hosted.
#    Causa builds fantasma que no producen output cuando hay nuevas dependencias.
#    "New Build" con SBP cache ya es efectivamente incremental: solo reconstruye lo que cambió.
# Build WebGL player — RUTA RECOMENDADA (batchmode, con el Editor CERRADO):
#   "/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe" #     -batchmode -quit -nographics -projectPath . -buildTarget WebGL #     -executeMethod TvProdBuild.BuildProd -logFile build-prod.log
# Fuerza DiskSizeLTO por código y corre PreflightAudio() (aborta si falta un clip de ambiente).
# La ruta por GUI (File → Build Settings → Build) también vale desde el 2026-08-15: el LTO
# ahora se aplica en un IPreprocessBuildWithReport, así que ya no depende de por dónde entres.
# NOTA: ambos son pasos INDEPENDIENTES. Solo rebuild player si cambió código C#.

# Si el build tarda demasiado: reducir texturas primero
# Unity → Appquarium TV → ★ Reduce TV Textures  (512px → 4× menos tiempo de compresión WebGL)
# Build de 25 peces: ~2-4h con 512px (vs 8-16h con 1024px)

# Solo si hay corrupción confirmada (builds que abortan a mitad, assertion failures):
Remove-Item -Recurse -Force "Library\com.unity.addressables\aa"

# Deploy a R2 — SIEMPRE añadir los env vars
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"

# ⚠ Archivos pequeños con aws s3 cp en CLI 2.23+:
#   - aws s3 cp <5KB falla con SignatureDoesNotMatch (R2 no soporta CRC64NVME default de CLI 2.23+)
#   - aws s3 sync funciona para archivos medianos, pero NO para archivos muy pequeños sueltos
#   - FIX: usar boto3 directamente para archivos problemáticos:
#     python -c "import boto3,configparser,os; c=configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')]); client=boto3.client('s3',endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',aws_access_key_id=c.get('r2','aws_access_key_id'),aws_secret_access_key=c.get('r2','aws_secret_access_key'),region_name='auto'); client.put_object(Bucket='appquarium-tv',Key='<KEY>',Body=open('<FILE>','rb').read(),CacheControl='<CC>'); print('OK')"

# — Caso normal: solo New Build (bundles + catalog, sin tocar player) —
# Los bundles usan sync SIN --delete para no borrar los que no se rebuildearon
# ⚠⚠ AL BUCKET PRIVADO `appquarium-tv-assets`, EN LA RAÍZ (sin prefijo `bundles/`) y con el
# perfil `r2assets`. Subirlos a `appquarium-tv/bundles/` los volvería a dejar PÚBLICOS, que es
# exactamente el agujero que se cerró el 2026-08-20.
aws s3 sync ServerData/WebGL/ s3://appquarium-tv-assets/ `
  --profile r2assets `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
# El catalog.hash suele fallar con sync → subir por separado:
aws s3 cp ServerData/WebGL/catalog_1.2.1.hash s3://appquarium-tv-assets/catalog_1.2.1.hash `
  --profile r2assets `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=60" --content-type "text/plain"

# — Solo player rebuild (bundles intactos en R2) —
#
# ⚠⚠ NO USAR `--delete` EN LA RAÍZ DEL BUCKET. `--exclude "bundles/*"` NO basta.
# R2 tiene en la raíz ficheros que NO están en webgl-output/ y que --delete BORRA:
#   · keepalive_black.mp4  ← el receiver lo usa (vídeo keepalive). Borrarlo revienta
#                             las sesiones largas, que es justo lo que costó meses arreglar.
#   · silence.wav
#   · Build/webgl-min.*  y  Build/webgl-output-empty.*  ← los rigs de diagnóstico
# El 2026-08-15 se estuvo a un comando de hacerlo. Subir sólo lo que cambia:
aws s3 sync webgl-output/Build/ s3://appquarium-tv/Build/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --exclude "*" `
  --include "webgl-output.data" --include "webgl-output.wasm" `
  --include "webgl-output.framework.js" --include "webgl-output.loader.js" `
  --cache-control "public, max-age=3600"
# index.html va aparte con boto3 (ver abajo). StreamingAssets/ sólo si cambió el catálogo:
# comprobar antes con `diff` contra R2 — suele ser idéntico y no hace falta tocarlo.
# Archivos pequeños que fallan con sync (catalog.hash, settings.json) → subir con boto3:
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
client = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
for local, key, ct, cc in [
    ('webgl-output/StreamingAssets/aa/catalog.hash','StreamingAssets/aa/catalog.hash','text/plain','public, max-age=60'),
    ('webgl-output/StreamingAssets/aa/settings.json','StreamingAssets/aa/settings.json','application/json','public, max-age=60'),
]:
    client.put_object(Bucket='appquarium-tv', Key=key, Body=open(local,'rb').read(), ContentType=ct, CacheControl=cc)
    print('OK:', key)
"

# — Rebuild completo (player + todos los bundles, caso raro) —
# 1. Limpiar bundles viejos
aws s3 rm s3://appquarium-tv-assets/ --recursive `
  --profile r2assets `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com
# 2. Subir bundles nuevos
aws s3 sync ServerData/WebGL/ s3://appquarium-tv-assets/ `
  --profile r2assets `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
# 3. Subir player — mismo comando acotado a Build/ del bloque anterior. SIN --delete.
#    (Ver el aviso de arriba: --delete en la raíz borra keepalive_black.mp4 y los rigs.)

# — index.html (el receiver) — SIEMPRE con boto3, nunca con `aws s3 cp` —
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
client = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
client.put_object(Bucket='appquarium-tv', Key='index.html', Body=open('webgl-output/index.html','rb').read(),
                  ContentType='text/html; charset=utf-8', CacheControl='public, max-age=60')
print('OK index.html')
"
# max-age=60 a propósito: el device cachea el receiver (disableIdleTimeout lo permite) y con
# 3600 te pasas una hora viendo el index viejo sin saberlo. El sello de la esquina lo delata.
```

---

## Scripts — Ubicaciones

| Carpeta | Contenido |
|---|---|
| `Assets/Scripts/Core/` | **TvBundleAuth** ⭐ (firma cada descarga de bundle contra el Worker; el token lo aporta `TvBundleAuthSecret.cs`, que **no está en git**), AquariumManager (slim), AmbientModeController, AquariumCameraController, AudioManager, CastReceiver, CastDataTypes, FishSpawner, FoodItem, PostProcessingSetup, **TvSceneBootstrap** ⭐, **TvFoodManager**, **TvDecoCatalogPatch** (trasvasa `hasBioLuminescence` del JSON a los SOs — sin esto la biolum es código muerto) |
| `Assets/Scripts/Fish/` | FishAgent, FishBrain, SteeringController, NeedsModule, FishProceduralAnimator (sync mobile) |
| `Assets/Scripts/Tank/` | TankController, DecorationPlacer, BubbleSystem, TankBackground, TankLightingController, WaterSurface (sync mobile) |
| `Assets/Scripts/Data/` | FishData, DecorationData, TankData (sync mobile) |
| `Assets/Scripts/Utils/` | AppFlags, AppVersion, CatalogLoader (sync mobile) |
| `Assets/Scripts/Stubs/` | TvStubs (stubs para clases mobile-only referenciadas indirectamente) |
| `Assets/Settings/` | **TvRenderPipeline.asset** ⭐ + **TvUniversalRenderer.asset** — el render pipeline que faltaba (2026-08-21). Sin esto no hay post-proceso |
| `Assets/Editor/` | **TvUrpSetup** ⭐ (crea/enciende/apaga el pipeline y verifica `postProcessData`), **TvRenderProbe** (sonda: ¿se está renderizando, y con qué?), **TvGradeSweep** (barrido de grado en el Editor — ⚠ NO fiable para elegir valores, ver `CAST_PARIDAD_VISUAL.md` §0.1), TvAddressablesSetup, TvBuildTools, SyncFromMobileMenu, **TvBuildPostprocess** (parchea settings.json tras cada build), **TvProdBuild** ⭐ (build de producción en batchmode + preflight de audio), **TvWasmOptimize** ⭐ (fuerza `DiskSizeLTO` en cualquier build), TvEmptyTestBuild, **TvAuthPreflight** ⭐ (aborta el build si falta el token de los bundles), TvShadowDiag, **TvDecoOptimize** ⭐ (pasa una deco a texturas DXT1 sueltas: −49,8 % de peso medido) |
| `Tools/` | ~30 ficheros. Los que importan: **grade-tune.js** ⭐ (afina el grado sobre el player REAL en Chrome, mandando mensajes `GRADE`), **grade_contact_sheet.py** (hoja de contactos + luminancia/saturación por bandas, con guarda de «esto no mide nada»), **r2-auth-worker/** ⭐ (el Worker portero de los bundles + sus dos baterías de pruebas), **SyncFromMobile.ps1**, **check_preset_ids.js** ⭐ (guarda: ningún id de preset fantasma, sin Unity ni tele), **static-server.js** (rig local en :3001 — sirve el catálogo **desde R2**, no del disco), **test-updates.js** (los 9 tests de los handlers UPDATE), **cast-headless.js** (sender sin navegador), **cast-run.sh** (ciclo de medición completo), **restore-production-receiver.sh**, **extract_glb_textures.py** (saca las texturas embebidas de un GLB + `mapeo.txt`, paso previo a `TvDecoOptimize`), **r2_huerfanos.py** (lista/borra bundles huérfanos de R2), y los `rcv-*.html` (receivers de diagnóstico). ⚠ Varios escriben en R2 de producción. |

---

## Cast SDK — notas técnicas

- **Receiver Published** App ID `8F6C873F` — funciona en cualquier device sin registrar Cast Console
- **Cast SDK timeout = 30s** desde "Connecting…" hasta receiver READY. Sin esto la sesión aborta.
- **Xiaomi TV Box S** como `MiTV-AFMU0` en LAN. Cast SDK 3.72.446070.
  - ⚠⚠ **Para encontrarla: ni el ping ni el puerto 8008 bastan.** El DHCP le mueve la IP y **el user
    la apaga cuando no la usa** (aclarado por él el 2026-08-19: NO es un fallo del device, y
    `stay_on_while_plugged_in 7` no tiene nada que ver). El ping falla
    porque otro cacharro coge la IP libre; y el 8008 tampoco vale: hay **otro Cast en la casa**
    («Comedor») con el puerto abierto. Hay que leer el nombre:
    `curl http://IP:8008/setup/eureka_info | grep -i xiaomi`. `cast-run.sh` ya lo hace bien.
  - ⚠ **El receiver sobrevive al sender:** si lanzas una tanda con el receiver aún vivo, hereda su
    cuenta atrás y muere a los pocos segundos (se ve porque el reloj `RCV` arranca alto).
    Hacer `node Tools/cast-headless.js --stop --ip <IP>` antes de cada tanda.
- **`ctx.start()` — parámetros correctos:** `{ disableIdleTimeout: true, maxInactivity: 3600 }`
  - `disableIdleTimeout: true` — no shutdown cuando 0 senders conectados
  - `maxInactivity: 3600` — no desconectar sender "inactivo". ⚠ El SDK **rechaza valores ≤ 5** con error en runtime. Usar 3600 (1h) como "nunca".
  - ⚠ **El keepalive JS de 60 s que decía este doc YA NO EXISTE** (`index.html`: *"KEEPALIVE 30s
    (receiver→sender) ELIMINADO (bisección)"*), y el PONG también está suprimido. Lo que mantiene
    viva la sesión hoy es el **vídeo keepalive** (`keepalive_black.mp4` en bucle), que este doc
    no mencionaba en ningún sitio — y que el comando de deploy con `--delete` borraba.
  - ⚠ Contradicción abierta: el research de julio sostiene que fijar `maxInactivity` es
    contraproducente. En disco gana lo de aquí (3600) y es la configuración con la que se han
    validado las sesiones de 900 s. No tocar sin una tanda A/B.
- WebGL Chromium en Cast = sandbox MUY estricto:
  - `Exception Support: None` (sino peta con wasm-exceptions)
  - `WebAssembly 2023 features: OFF`
  - `Compression Format: Disabled` (R2 sirve raw — evita bug double-gzip del 23-may)

---

## Addressables — config actual

| Group | Mode | Contenido | Bundles |
|---|---|---|---|
| `Fish_Remote` | PackSeparately | 25 FishData SOs | 25 |
| `Decos_Remote` | PackSeparately | 54 DecorationData SOs | 54 |
| `Environments_Remote` | PackSeparately | **vacío desde el 2026-08-18** | 0 |
| `Audio_Remote` | PackSeparately | 1 clip (`ambient_music`) | 1 |
| `Default Local Group` | PackTogether | scaffolding | 1 |

Total vivo: **80 bundles = 25 fish + 54 decos + 1 audio**, y en R2 ocupan **87,3 MB**.
Ese 80 cuadra clavado con la tabla, así que es la comprobación rápida de que no falta nada.

⚠ **Los 11 fondos salieron de Addressables el 2026-08-18.** Se cargan SIEMPRE por
`Resources.Load` (`TankBackground.cs:207` y `:296`) — en todo el proyecto no hay ni un
`LoadAssetAsync<Texture2D>` — así que sus 11 bundles no los descargaba nadie. **No bastaba con
borrar las entradas del grupo: `★ Setup Addressables` las recreaba en cada ejecución**; hubo que
quitar también ese bucle. Para revertir: restaurar el bucle y ejecutarlo. El grupo se deja vacío
a propósito (un grupo sin entradas no produce bundle) en vez de borrarlo.
ℹ La copia de los fondos horneada en el `.data` (~0,7 MiB) **sí se usa y se queda**: sacarla
exigiría convertir carga síncrona en asíncrona y rebuild de player.
⚠ En local hay **208** `.bundle`: los sobrantes son huérfanos de builds anteriores (en R2 hay 0). **Para medir
tamaños hay que filtrar por los hashes que aparecen dentro de `catalog.bin`** — `ls -S` coge el
mayor por nombre, que suele ser un huérfano, y así salieron ya dos cifras falsas (los «375 MB de
decos» y un «−83,8 %»). En R2 se limpian con `python Tools/r2_huerfanos.py --borrar` (informe por
defecto; compara contra el catálogo bajado **de R2**, no el local).
⚠ Falta en esta tabla el grupo **`Shared_Local`** (7 entradas, PackTogether, rutas locales),
que es el grupo local de verdad; `Default Local Group` tiene 0 entradas y no produce bundle.

**LZ4 compression** confirmado. **NonRecursiveBuilding=true** — ⚠ NO cambiar a `false` (causa 47 min/bundle, builds de 30h+). Ver `feedback_nonrecursivebuilding.md`.

---

## ⚠ index.html — template vs procesado

`Assets/WebGLTemplates/CastReceiver/index.html` es el **template fuente**. Unity lo procesa durante el build y sustituye los placeholders `{{{ LOADER_FILENAME }}}`, `{{{ DATA_FILENAME }}}`, `{{{ FRAMEWORK_FILENAME }}}`, `{{{ CODE_FILENAME }}}`, `{{{ PRODUCT_NAME }}}`, etc. El resultado procesado va a `webgl-output/index.html`.

**NUNCA copiar el template sobre `webgl-output/index.html`** — el browser intentará cargar `Build/{{{ LOADER_FILENAME }}}` literalmente → 404 → "Error de red" en el receiver.

Para cambiar solo el `index.html` sin rebuild del player:
1. Editar `webgl-output/index.html` directamente
2. Subir a R2 con boto3: `client.put_object(Bucket='appquarium-tv', Key='index.html', ...)`

Los valores correctos de los placeholders (para emergencias):
- `{{{ LOADER_FILENAME }}}` → `webgl-output.loader.js`
- `{{{ DATA_FILENAME }}}` → `webgl-output.data`
- `{{{ FRAMEWORK_FILENAME }}}` → `webgl-output.framework.js`
- `{{{ CODE_FILENAME }}}` → `webgl-output.wasm`
- `{{{ WIDTH }}}` / `{{{ HEIGHT }}}` → `960` / `600`
- `{{{ PRODUCT_NAME }}}` / `{{{ COMPANY_NAME }}}` → `Appquarium`
- `{{{ PRODUCT_VERSION }}}` → `1.2.1`

---

## Memoria de Claude

Ficheros en `C:\Users\Behere\.claude\projects\D--dev-appquarium-tv-unity\memory\`:

- `MEMORY.md` — índice principal
- Curated subset de las memorias del mobile (solo lo relevante para TV)

---

## Glosario

- **`.data`** — fichero binario del WebGL build. Contiene scenes + scripts + assets baked. Sirve desde R2 raíz.
- **Bundle remoto** — AssetBundle servido desde R2 `/bundles/`. Loaded lazy via Addressables.
- **PackSeparately** — 1 bundle por asset addressable. Modelo Netflix.
- **R2** — Cloudflare object storage. Public URL: `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/`.
- **Cast SDK** — Google Cast Receiver framework. Receiver app types: v1 HTML, v2 (no usamos), CAF v3 (este).
