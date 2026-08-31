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
| [`CAST_NEXT_SESSION_2026-09-01.md`](CAST_NEXT_SESSION_2026-09-01.md) | ⭐⭐ **EMPEZAR AQUÍ**. Cierre del 31-ago: **el día que se cerraron las tres cosas que llevaban semanas abiertas** — la ruta a R2 (volvió sola), el campo **`sex`** (desplegado y validado, **sin build de player**) y **las 7 luces** (medidas, y con **paridad a dos pantallas**: ninguna fundida en ninguna). **Las dos falsas alarmas del día fueron mías**: un acreditador que dio VERDE a capturas tomadas con la sesión ya muerta, y un detector que **inventó 160 px** de desajuste geométrico con dispersión `0.0000`. Trae la columna **`ILUM`** (qué luz de pago es luz y cuál es sólo un tinte), los **4 documentos caducados** del día, y **4 decisiones del user**. |
| [`CAST_NEXT_SESSION_2026-08-31.md`](CAST_NEXT_SESSION_2026-08-31.md) | Cierre del 30-ago: **el día que no se midió nada y aun así salió caro de bueno.** El objetivo eran las **7 luces** y no se midió ninguna: de los tres obstáculos, **dos eran instrumentos nuestros que informaban en la dirección tranquilizadora** (mi guarda daba **VERDE** con la línea de fracaso) y el tercero fue un **corte de rutado de Telefónica** contra los prefijos de `workers.dev` **y** del endpoint S3 de R2 — **✅ resuelto solo el 31-ago**, no lo arregló nadie de aquí. Trae: el **procedimiento de medida de las luces** (una luz no se mide como un fondo), el **estimador en lineal** con su suelo de ruido, el **plan del dominio propio** (cambiar de host **no cuesta build**) y **3 decisiones del user pendientes**. |
| [`CAST_NEXT_SESSION_2026-08-29.md`](CAST_NEXT_SESSION_2026-08-29.md) | Cierre del 28-ago (§0.ter). ⚠⚠ El ajuste visual de la mañana **reventó el suelo** (53,68 % clavado al blanco): se aprobó mirando el agua. Arreglado y desplegado la misma noche (`bloom 0.30 + tonemapping`, sello `rcv 2026-08-28 tmA`). Falta el **casteo con la app real**; producción **parada**. Cierre del 28-ago: Cierre del 28-ago, **el día que las dos sesiones de Claude se hablaron** (`ListAgents` + `SendMessage` con el repo móvil). El user **aprobó mirando la tele** un ajuste visual que iguala la claridad del teléfono (agua alta 75.9 contra 76.0) — horneado y **pendiente de build**. El **bloom no cuesta fps** (el «7 fps» era el framerate absoluto de junio). La **nitidez estaba del revés**: la tele no es más borrosa, es más dura. Y un **relay de logs que moría en silencio**, con HUD ya desplegado y **una lectura a medias que cuesta una captura** (§1). |
| [`CAST_NEXT_SESSION_2026-08-28.md`](CAST_NEXT_SESSION_2026-08-28.md) | Cierre del 27-ago (segundo día sin tele): `remove_fish` **por uid**, un chequeo de compilación **sin Unity y sin build**, y la **paridad visual medida** — no era el grado: copiar el del móvil *perdería* un 35 % de croma, la TV **no apaga el color**, y el «fondo en B/N» es el arte (7 de 11 fondos por debajo de croma 12 en el fichero). Mañana: **dos tandas** y comparar con **el mismo preset** en las dos pantallas. |
| [`CAST_NEXT_SESSION_2026-08-27.md`](CAST_NEXT_SESSION_2026-08-27.md) | Cierre del 26-ago (día sin tele): tres handlers que **confirmaban lo que no había pasado**, seis ids de preset fantasma, y el **rig local roto** desde el último build. Player nuevo `rcv 2026-08-26 ids` construido y verificado en local (9/9), **pendiente de una tanda** que valide también el `renderScale 0,75` de ayer. |
| [`CAST_NEXT_SESSION_2026-08-26.md`](CAST_NEXT_SESSION_2026-08-26.md) | Cierre del 25-ago: la escena deja de verse como assets separados (niebla de agua, tono de peces, `renderScale` 1:1). Todo desplegado y validado **menos `renderScale 0,75`**, que se quedó sin tanda. Trae las 3 trampas del día y 2 afirmaciones mías que resultaron falsas. |
| [`CAST_NEXT_SESSION_2026-08-25.md`](CAST_NEXT_SESSION_2026-08-25.md) | Cierre del 24-ago: Cierre del 24-ago: el ciclo día/noche por fin llega a decos y peces. **Construido y verificado, NO desplegado.** Trae la trampa del shader horneado en el bundle y por qué el deploy va sólo con `Build/`. |
| [`CAST_NEXT_SESSION_2026-08-22.md`](CAST_NEXT_SESSION_2026-08-22.md) | Cierre del 21-ago: la TV recuperó el color (llevaba desde siempre sin aplicar ningún grado, por 5 causas encadenadas). Protocolo auditado 11/11 y coste de URP medido. |
| [`CAST_PARIDAD_VISUAL.md`](CAST_PARIDAD_VISUAL.md) | 🎨 **El detalle y las pruebas** de lo anterior: qué se descartó con medida, qué reglas salieron, y lo que queda del fondo. |
| [`CAST_NEXT_SESSION_2026-08-21.md`](CAST_NEXT_SESSION_2026-08-21.md) | Cierre del 20-ago: Cierre del 20-ago: los bundles ya están detrás del Worker. Qué cambió en el deploy y qué queda. |
| [`CAST_R2_AUTH_MOVIL.md`](CAST_R2_AUTH_MOVIL.md) | 📄 **Para la sesión del repo MÓVIL.** Contrato de la Fase 2 (JWT por usuario): campo del JSON, claims, orden de migración. |
| [`CAST_NEXT_SESSION_2026-08-20.md`](CAST_NEXT_SESSION_2026-08-20.md) | Estado al cierre del 19-ago, Estado al cierre del 19-ago, pendientes reales y las trampas caras (bundle local que cambia de hash, y los 3 fallos de la conversión de materiales). |
| [`ESTADO_PRODUCCION_2026-08-19.md`](ESTADO_PRODUCCION_2026-08-19.md) | 📊 **Foto de estado y valoración para producción.** Qué está validado, y los 2 puntos que faltan antes de difundir (bucket abierto + rigs de diagnóstico servidos). |
| [`DECOS_PESO_PARA_MOVIL.md`](DECOS_PESO_PARA_MOVIL.md) | 📄 **Para leer en el repo MÓVIL.** Las 3 palancas de peso de decos, qué se reutiliza, qué cambiar (DXT1→ASTC/ETC2) y las 7 trampas ya pagadas. |
| [`CAST_HANDOFF_MOVIL_2026-08-26.md`](CAST_HANDOFF_MOVIL_2026-08-26.md) | 📲 **PARA LA SESIÓN DEL REPO MÓVIL.** Todo lo que cambió de su lado del contrato el 26-ago y no llegó a recibir: los **dos cambios de la Fase 2** (`/mint-token` con credencial, propiedad en modo `log`), el emparejamiento ya aceptado, la carrera del `pairs`, y lo que les toca. **Es el fichero que hay que pasarles.** |
| [`CAST_CONTRACT_TV.md`](CAST_CONTRACT_TV.md) | 🤝 **El contrato del canal Cast, lado receiver.** Qué campos del INIT leo de verdad y qué hago si faltan, los 12 UPDATE con su conducta ante basura, y **lo que NO cumplo**. Su pareja es `CAST_CONTRACT.md` en el repo MÓVIL. Regla fijada entre los dos: **todo cambio es aditivo**. |
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

### 🐟 El emparejamiento estaba montado y VACÍO (2026-08-26)

Toda la maquinaria de parejas existía en la TV —`FishAgent.WirePairsFromSave`,
`SaveData.activePairs`, `BreedingPair`, `SteeringController.PairBond()` con peso **1,8** en Idle y
**1,2** en Explore— **y no se usaba nunca**: `TvAquariumState` no transportaba las parejas, así que
`activePairs` estaba siempre vacío y la función se iba por su primera línea. Una pareja emparejada
nadaba junta en el móvil y **suelta** en la tele. Lo encontró la sesión del repo móvil.

- **El uid del móvil se ADOPTA** (INIT y `add_fish`). Antes se generaba aquí con `Guid.NewGuid()`
  en **tres** sitios; quedan dos, ambos fallback para cliente viejo.
  ⚠ `uid` en `TvAddFishPayload` **no es opcional**: un pez que entra a mitad de sesión con uid
  propio **no puede emparejarse jamás**.
- **UPDATE `pairs`** = lista **completa**, no delta: `{"items":[{maleUid,femaleUid},…]}`. Encaja
  sin adaptador porque `WirePairsFromSave` limpia **todos** los partners antes de re-cablear.
- ⚠⚠ **La carrera.** El móvil emite `pairs` justo detrás del `add_fish` que forma la pareja, pero
  `AddFishAsync` **espera una descarga de bundle** y un `FishAgent` no entra en `FishAgent.All`
  hasta su `OnEnable`. El `pairs` puede llegar **antes que el pez** → `All.Find` devuelve null →
  la pareja se descarta **en silencio**, y como `pairs` sólo se emite al cambiar, no se reintenta.
  **Fix:** re-emparejar tras cada `add_fish` que termine bien.
- 🧭 **Se reporta lo CABLEADO, no lo recibido**: `pairs: 3 recibidas pero sólo 2 cableadas`. Esa
  diferencia *es* el síntoma de la carrera.

### 🔐 Fase 2 del JWT — escrita y probada, SIN desplegar (2026-08-26)

El Worker sólo comparaba contra `BUNDLE_TOKENS`, así que **el bloqueo de la Fase 2 no era sólo del
móvil**: un JWT habría recibido 401. Ya está la verificación HS256 + `POST /mint-token`
(`Tools/r2-auth-worker/`, **42/42** en `test-local.mjs`, incluidos firma manipulada, `alg: none`,
HS512, caducado y sin `exp`).

⚠⚠ **Dos decisiones que NO estaban en el spec** y que el móvil tiene que conocer — están en
`CAST_R2_AUTH_MOVIL.md` §1.4 y en el handoff:

1. **`/mint-token` NO es abierto**: exige `Bearer <MINT_TOKENS>`, credencial propia del APK. Un
   endpoint de emisión sin credencial dejaría pedir `isPremium` a cualquiera → la Fase 2
   protegería **menos** que la Fase 1.
2. **`OWNERSHIP_MODE=log`**: firma y caducidad se verifican de verdad, pero un bundle que no
   consta como suyo **se sirve igual**, marcado `X-Aq-Ownership: would-deny`. Si los ids de los
   claims llegaran mal, el usuario se quedaría sin **su** acuario → tele vacía, el síntoma más
   caro de diagnosticar aquí. Se pasa a `enforce` cuando el contador sea 0.

**Para desplegarlo** hacen falta dos secrets que pone el user:
```bash
cd Tools/r2-auth-worker
npx wrangler secret put JWT_SECRET
npx wrangler secret put MINT_TOKENS
npx wrangler deploy
```
Es aditivo: sin ellos el camino nuevo da `503` y el token constante sigue igual.

### 🐟 `remove_fish` por uid (2026-08-27) — y una contabilidad rota desde siempre

`remove_fish` sólo transportaba la **especie**, así que `DespawnOneBySpecies` quitaba **el
primero** de esa especie. Con 3 Banggai en el tanque, quitabas uno concreto en el móvil y en la
tele desaparecía otro — sin error, con el log diciendo que todo bien. Ahora que el uid del móvil
se adopta (26-ago), el arreglo era barato.

**Es aditivo, y el camino viejo se identifica en el log:**

| llega | qué hace |
|---|---|
| `"fish_banggai"` | `remove_fish: fish_banggai por especie (cliente sin uid: quitado el primero)` |
| `{"uid":"…","speciesId":"…"}` | `remove_fish: fish_banggai uid=… (quedan N peces)` |
| uid que no está en el tanque | `ERR remove_fish: uid 'x' no esta en el tanque` — y **no quita nada** |

⚠ Ese último punto es deliberado: **caer al camino de la especie sería reintroducir el fallo por
la puerta de atrás**. `speciesId` en el JSON es opcional (se saca del propio pez si no viene).

⚠⚠ **De paso salió otra**: `remove_fish` destruía el pez pero **no lo sacaba de
`ownedFish`/`activeFishUids`**. `add_fish` los alimentaba y nadie los limpiaba, así que el save
transitorio sólo crecía y divergía del tanque según avanzaba la sesión. Hoy sólo se leen en el
arranque —por eso no se notaba— pero el emparejamiento ya consume uid de ahí. Ahora se limpian, y
si el pez estaba emparejado **se retira la pareja y se re-cablea** (si no, `pairs` la contaría
para siempre como «recibida pero no cableada», que es el síntoma de la carrera del `add_fish` y
ahí sí es un fallo).

**Falta el lado móvil:** mandar el uid en el payload. Contrato en `CAST_CONTRACT_TV.md` §5.3.

### ⚠⚠ `waitForLog` miraba TODO el log acumulado (2026-08-27)

En `Tools/test-updates.js`, `waitForLog(patrón)` buscaba en el log **desde el arranque**, así que
**una línea de un test anterior daba por bueno un test posterior sin que pasara nada**. No es
teórico: los dos tests nuevos de `remove_fish` habrían pasado con líneas de los tests 2 y 12.

Ahora hay `desde()`, que marca el punto del log, y `waitForLog(patrón, ms, marca)` sólo mira de
ahí en adelante. Los tests viejos siguen llamando sin marca.

🧭 Misma familia que el `bg_ocean` que tuvo este fichero meses en verde: **el test pasaba, y no
comprobaba nada**.

### 🧪 Guardas que se pagan solas (2026-08-26)

`node Tools/check_preset_ids.js` — sin Unity, sin navegador y sin tele. Comprueba tres cosas:

1. Que **ningún id de preset fantasma** ande suelto por el receiver o las herramientas (encontró
   **cinco** el día que se escribió).
2. Que las **cifras del contrato** (`11 fondos / 12 sustratos / 7 luces`) cuadren con los arrays
   de C#. Un doc con listas a mano del que **depende otro repo** es el mismo bug que persigue.
3. Que **todo tipo de UPDATE del switch esté documentado** en `CAST_CONTRACT_TV.md`. Cazó `pairs`
   el mismo día en que se cableó. ⚠ El escáner **corta en el fin de `ApplyUpdate`**, o se cuela el
   switch de `ApplyAmbientMode`: si cuenta 15 tipos en vez de 12, se pasó de largo.

🧭 No es descuido de nadie: **la ventana entre escribir código y escribir el doc siempre existe**,
y en esa ventana el contrato miente.
### ⚠⚠ El MCP de Unity puede estar hablando con OTRO proyecto (2026-08-27)

El puerto **8091** lo sirve **el Editor que esté abierto**, sea cual sea el proyecto. Ese día
había un Unity abierto con `D:\dev\Distill` y ninguno con éste: `recompile_scripts` se quedó
**7 minutos «working»**, `Library/ScriptAssemblies/Assembly-CSharp.dll` seguía siendo del día
anterior, y el `Editor.log` que se leía era el del otro proyecto.

⚠ Lo peligroso no es el cuelgue, es que **una Console vacía porque no ha compilado se parece a
una Console limpia**. Comprobar antes de fiarse:

```powershell
Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Select-Object ProcessId,CommandLine
```

### 🧰 Comprobar que el C# compila SIN Unity y sin build (2026-08-27)

```bash
bash Tools/compile-check.sh          # Assembly-CSharp (runtime)
bash Tools/compile-check.sh Editor   # Assembly-CSharp-Editor
```

**~15 s.** Usa el Roslyn y el host de .NET que trae Unity, y saca las 308 referencias y los
~2.500 caracteres de `define` del `.csproj` que Unity genera para el IDE. Hasta ahora la única
forma de saber si un cambio compilaba era la Console del Editor o gastarse un build.

- ⚠ **Comprueba SÓLO que compila.** Ni runtime, ni stripping, ni que el shader exista, ni que el
  bundle cargue. Es el escalón más bajo: encima siguen `static-server.js` + `test-updates.js`, y
  encima de todo, la tele.
- ⚠ Depende de que el `.csproj` esté al día (lo regenera Unity al reimportar). Un `.cs` **recién
  creado** que no esté en el csproj **no se compila y sale verde**. Mirar la cuenta de fuentes
  que imprime (hoy: 35 runtime / 15 editor).
- 🧭 Se validó **en los dos sentidos**: verde con el código bueno y rojo con un `CS0029` metido a
  propósito. Una herramienta de verificación que sólo se ha visto en verde no está verificada.

Tres trampas que costó montarlo, todas de fallo silencioso:
1. La versión inicial terminaba con **éxito si el generador reventaba** (el bucle no iteraba y
   `CODE` seguía a 0). Ahora aborta si no hay ficheros de respuesta.
2. `python` en Windows escribe el salto de línea como **CR+LF**, y `$(cat orden.txt)` **no parte
   por CR**: el nombre salía `Assembly-CSharp<CR>` y `csc` daba `CS2011` sobre un fichero que
   existía y que se abría a mano. Va con `newline=''`.
3. Con rutas de MSYS (`/tmp/…`) python resolvía contra `D:\tmp` y `csc` buscaba en
   `C:\Users\…\Temp`. La ruta de trabajo es **relativa** (`Temp/compile-check`) a propósito.

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
| `Tools/` | ~30 ficheros. Los que importan: **grade-tune.js** ⭐ (afina el grado sobre el player REAL en Chrome, mandando mensajes `GRADE`), **grade_contact_sheet.py** (hoja de contactos + luminancia/saturación por bandas, con guarda de «esto no mide nada»), **r2-auth-worker/** ⭐ (el Worker portero de los bundles + sus dos baterías de pruebas), **SyncFromMobile.ps1**, **check_preset_ids.js** ⭐ (guarda: ningún id de preset fantasma, sin Unity ni tele), **barre-luces.sh** ⭐ + **mide_luces.py** ⭐ (las 7 luces: barrido en el device con preflight de ruta y guarda de fotograma congelado, y analizador que **se niega a producir tabla** si el acta no acredita las capturas por sha256), **compile-check.sh** ⭐ (¿compila el C#? en ~15 s, sin Unity y sin build), **static-server.js** (rig local en :3001 — sirve el catálogo **desde R2**, no del disco), **test-updates.js** (los tests de los handlers UPDATE — **16** desde el 27-ago), **test-frases.js** ⭐ (las frases de la pantalla de carga: reparto por tipo, sin repeticiones, idioma y caída a castellano — 29 comprobaciones, sin device ni navegador), **cast-headless.js** (sender sin navegador), **cast-run.sh** (ciclo de medición completo), **restore-production-receiver.sh**, **extract_glb_textures.py** (saca las texturas embebidas de un GLB + `mapeo.txt`, paso previo a `TvDecoOptimize`), **r2_huerfanos.py** (lista/borra bundles huérfanos de R2), y los `rcv-*.html` (receivers de diagnóstico). ⚠ Varios escriben en R2 de producción. |

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

### 📡 El relay de logs del receiver puede morir en silencio (2026-08-28)

Cada línea de `dbg()` del `index.html` viaja al sender por el canal Cast (`_logSink` →
`ctx.sendCustomMessage`). **Ese relay se muere y no queda rastro en ninguna parte**, porque había
dos `try/catch` que se comían el fallo y el único informe de contadores salía… **por el mismo canal
roto**:

```
index.html:136   if (_logSink) { try { _logSink(line); } catch(e) {} }   ← tragadero
index.html:492   _logSink = …  catch (e2) { _logFail++; … }             ← sólo cuenta
index.html:522   dbg('… 📡 stream sent='+_logSent+' fail='+_logFail)     ← ¡por el canal roto!
```

**Medido el 28-ago**, cruzando el logcat del móvil con nuestro log:

| sender | líneas del receiver que llegan | última |
|---|---|---|
| `cast-headless` **solo** | **135 · 139 · 202+** | toda la sesión |
| APK del móvil delante | **3-4** | **a los ~45 ms** de su `RemoteMediaClient.load()` |

Cuatro sesiones de cuatro, Δ entre +3 y +57 ms tras el `load()` del `silence.wav` del emisor.
⚠⚠ **Y el acuario seguía renderizando**: se comprobó con dos `adb exec-out screencap` consecutivos
(103.088 y 134.270 píxeles cambiando de 2.073.600). La escena montaba; lo que no llegaba eran los
logs. Eso tuvo a la sesión del repo móvil media mañana concluyendo «el receptor no arranca» y llegó
a escribirse en su `CAST_CONTRACT.md` §11.2 como diagnóstico establecido — corregido desde este
lado en `CAST_CONTRACT_TV.md` §5.5.

### ✅ LA CAUSA, y el arreglo es UNA LÍNEA: `gms_cast_mrp`

```
12:13:52.860  Sender CONNECTED #1: …:com.appquarium.qa-43
12:13:52.861  Sender CONNECTED #2: …:gms_cast_mrp-42      ← 1 ms después
```

Cuando el emisor abre la sesión de media (`RemoteMediaClient.load()`), **GMS registra su Media Route
Provider como un segundo sender**. `_lastSenderId` pasaba a apuntarle y **todas** las líneas se
enviaban a un sender **vivo y válido que NO escucha nuestro namespace**. Enviar ahí **no lanza**.

```js
ctx.sendCustomMessage(NAMESPACE, undefined, payload);   // era _lastSenderId || undefined
```

**Medido: 134 líneas en 120 s contra 1-4 antes**, y 603 con 0 fallos en una sesión de 15 min.

🧭 **Por qué llevaba tanto oculto: el bug se escondía a sí mismo.** El `dbg('Sender CONNECTED #2…')`
se emite **después** de reasignar `_lastSenderId`, así que **el aviso de que había un segundo sender
viajaba al segundo sender**. La primera vez que se vio ese `#2` fue al curarlo.

⚠ **Cualquier receptor Cast que guarde `_lastSenderId` y tenga un emisor que use `RemoteMediaClient`
tiene el mismo bug esperando.** No es específico de este proyecto.

**Lo que hay ahora (desplegado y verificado en el device):** un HUD `#relay-meter` que pinta
`RLY env:N fallos:M snd:K off:<motivo>@<s>` **EN PANTALLA**.

- 🧭 **En pantalla a propósito:** un instrumento no puede reportar su propia muerte por el conducto
  que ha muerto. Y aquí la pantalla se lee con `adb exec-out screencap`, así que es un canal de
  verdad, no un consuelo.
- **Los tres números juntos son el diagnóstico**, y decide de un vistazo entre cuatro causas:

| lo que se lee | causa |
|---|---|
| `env` sube · `fallos:0` · `snd:1` | el mensaje se pierde **dentro del SDK**, sin lanzar |
| `fallos` sube | **excepción** en `sendCustomMessage` |
| `env` congelado · `fallos:0` · **`snd:0`** | `_logSink` sale por `if (senderCount <= 0) return;` **antes del `try`** → el receptor cree que no hay nadie escuchando |
| `env` congelado · `fallos:0` · `snd:1` | no es el relay: **`dbg()` ha dejado de llamarse** |

  ⚠ El tercer caso lo aportó la sesión del repo móvil, y es el más probable: encaja con que el
  relay muera **con la página viva**, siempre en el mismo punto y 45 ms después de una operación
  del emisor que toca la capa de sesión. `off:` dice **quién bajó `snd` y cuándo`.
- **El HUD se repinta solo cada 2 s**, no cuando alguien llama a `dbg()`: si dependiera de `dbg()`
  se congelaría justo en el caso que existe para diagnosticar.
- ⚠ Y si el propio repintado revienta, **lo escribe en su hueco** (`RLY HUD ROTO: …`). La primera
  versión del parche llevaba un `catch {}` mudo ahí dentro — un tragadero **dentro del parche que
  quita tragaderos**.
- **Oculto en producción salvo que haya un fallo** (es un indicador de error), y visible siempre
  con `DIAG`.

⚠ El parche va **al template Y al procesado**, aplicado por separado — **nunca copiando uno sobre
otro** (ver la sección de abajo).

### 🧹 Producción va LIMPIA: todo el debug sólo con `DIAG` (2026-08-28)

Petición del user: *«sin ese debug es pro normal; no debería verse nada del número de versión ni fps
ni nada que hemos puesto de debug; el acuario productivo»*.

| elemento | cuándo se ve |
|---|---|
| `#fps-meter` · `#stats-panel` | sólo con `DIAG` (ya era así) |
| **`#rcv-tag`** (el sello de la esquina) | **sólo con `DIAG`** — antes salía SIEMPRE |
| **`#relay-meter`** | **sólo con `DIAG`** — antes salía también los primeros 60 s y ante cualquier fallo |

⚠⚠ **Esto cambia el protocolo de diagnóstico**: «mándame una captura» ya no vale a secas, hay que
castear con `--diag`. 🧭 Lo bueno: `DIAG` viaja por el **canal de IDA**, el único que no se rompe,
así que sigue siendo alcanzable justo cuando el retorno falla.

### 🖼 La splash espera al ACUARIO, y rota frases (2026-08-28)

`hideSplash()` colgaba de que arrancara **Unity** (~24 s), no de que montara el acuario (~41 s):
eran **~17 s de tanque vacío a la vista**, reportados por el user. Ahora espera a `AQUARIUM READY` y
la barra avanza con las líneas **`BDL i/N`** que el receptor ya emitía por cada bundle.
⚠ Con red de seguridad a 90 s: **una carga que no se va nunca es peor que un tanque vacío.**

Y `#splash-tip` rota frases mientras tanto: **53 fijas en `es` y `en`** (ambiente · info · espera)
más 16 plantillas personalizadas con `activeFish[].nickname`, que el móvil ya mandaba y **no se
pintaba en ningún sitio**. Cuotas por tipo, cola sin repetición, 7,5 s, cursiva.

- **Idioma:** campo **`lang`** de `TvAquariumState` (dos letras o locale completo, se recorta).
  Validado en device: `lang=es -> es`. Sin él, castellano.
- ⚠⚠ **Las frases viven en el `index.html` A PROPÓSITO**, no en el emisor: cambiarlas es un deploy de
  minutos en vez de un build de Android de días.
- ⚠ Un idioma sin banco **cae a castellano**. Los dos bancos deben tener el **mismo número de frases
  por tipo**, o uno tendrá menos variedad sin que nadie lo note — lo comprueba `Tools/test-frases.js`.

### 🌸 El mensaje `GRADE` ya expone el BLOOM entero (2026-08-28)

```
bloom · bloomIntensity · bloomThreshold · bloomScatter · bloomHQ
bloomDownscale (0=Half,1=Quarter) · bloomMaxIterations · bloomSkipIterations
```

⚠⚠ **Sin el umbral, un barrido del bloom mide otra cosa.** `grade-tune.js` nunca lo mandaba, así que
las ocho variantes de agosto corrieron a **0.92** —invisible en escena submarina— y de ahí salió el
«el bloom no aporta nada», que costó meses. A **0.60** la escena sube **+8 L\*** sin coste medible,
y es lo que hay desplegado.

Y la línea `BLOOM: thr=… scatter=… hq=… downscale=… maxIt=… skipIt=…` sale **siempre, aunque el
bloom esté OFF**: 🧭 *el estado que determina un resultado tiene que viajar CON el resultado.*

🧭 **La regla que sale de aquí:** *ausencia de líneas en un log no es ausencia de eventos.* Separar
«no pasó» de «no me llegó», y el desempate barato en este proyecto es **mirar la pantalla**.

### ⚠⚠ Quitar el TONEMAPPING en un pipeline LDR revienta las altas luces (2026-08-28, noche)

El ajuste visual aprobado por la mañana (tonemapping OFF + viñeta 0 + niebla a la mitad) dejó **el
suelo del acuario clavado al blanco**: **53,68 %** de la banda del suelo con L\* > 95, contra
**0,00 %** el día anterior. Se aprobó **mirando el agua**; nadie miró el suelo.

Aislado en caliente con `GRADE`, misma escena y misma sesión:

```
bloom ON  · tm OFF   53,68 % clavado     <- lo que se desplego por la mañana
bloom OFF · tm OFF    3,81 %
bloom ON  · tm ON     0,00 %
bloom OFF · tm ON     0,00 %
```

- **No son dos culpables: son un aportador y un ausente.** El bloom mete la energía y el
  tonemapping era **lo único que la absorbía**.
- ⚠⚠ **El pipeline de la TV va con HDR APAGADO** (`RP: … hdr=OFF`). En LDR todo lo que pasa de 1.0
  **se clava al escribir**. El móvil corre bloom **sin** tonemapping y no se quema **porque sí tiene
  HDR**. ⇒ **En LDR el tonemapping no es estética, es el paracaídas.**
- ⚠ **Sin tonemapping no se salva ni bajando el bloom a 0.30**: sigue clavando el 19,65 %.
- ⚠ **`exposure` no compensa: es inerte en la TV** (el `Volume` de la barra LED, prioridad 11, pisa
  al grado, que va a 10).

**Desplegado y elegido por el user entre cuatro imágenes: `bloom 0.30 + tonemapping`** (sello
`rcv 2026-08-28 tmA`). La niebla de la mañana **se queda**, y por eso la escena sigue **más clara
que el 27-ago** en las dos bandas (agua 55.5 → 58.9, suelo 67.2 → 72.4) con el suelo entero.
Verificado sobre el player ya desplegado: clip **0,00 %**, textura **86 %** de la referencia.

```
bloom (con tm)  agua L*  suelo L*  suelo con rango  textura
OFF               56.7     69.5        100 %          91 %
0.30              58.9     72.4         88 %          86 %   <- desplegado
0.90              61.6     76.6         62 %          79 %
1.20              61.7     78.6         52 %          78 %
```

🧭 **No hay rodilla: es un intercambio suave**, así que es una elección del user y no un óptimo
calculable.

### ⭐ La línea `HORNEADO:` — prueba de artefacto que además dice lo que hace (2026-08-28)

`PostProcessingSetup` emite al arrancar, **por `JsBridge` (canal Cast)**:

```
HORNEADO: bloom=0.30 thr=0.60 tm=Neutral sat=18 con=10 exp=0.05 vig=0.00
```

- ⚠ **Va por `JsBridge` a propósito**: los `Debug.Log` **no viajan al sender**, así que el grado
  horneado era **invisible desde fuera** y la única prueba de versión era el sello del `index.html`
  — que **se despliega aparte del player** y puede estar nuevo con un `.wasm` viejo.
- 🧭 **Su texto lo generan los valores del build**, así que **una sola lectura** demuestra a la vez
  *que corre el binario nuevo* **y** *que se horneó lo que se midió en caliente*. Un sello dice «soy
  la versión X» y hay que fiarse; esto **dice lo que hace**.
- De paso descarta la caché del device: los `Build/*` van con `max-age=3600`.

### ⚠⚠ Un build de player REVIERTE un deploy de `index.html` (2026-08-28)

El aviso que ya había abajo es *no copiar el template sobre el procesado*. **Éste es el fallo
contrario y es silencioso:** el build **regenera `webgl-output/index.html` desde el template**, así
que **borra cualquier cambio desplegado sólo sobre el procesado**.

El 28-ago se desplegó el `index.html` **cinco veces** (relay, HUD, splash, frases, limpieza) sin
pasar por el template. Un build habría borrado el arreglo del relay **en silencio**.

**Antes de cada build**, comprobar los marcadores en el template:

```bash
for m in "sendCustomMessage(NAMESPACE, undefined" relay-meter _progresoAcuario \
         "PLANTILLAS\[_fraseIdioma\]" splash-tip RCV_HTML_VER; do
  echo "$m $(grep -c "$m" Assets/WebGLTemplates/CastReceiver/index.html)"
done
```

Un `0` en cualquiera = **parar y portar al template antes de construir**.

### 🧪 Medir sin engañarse: cuatro trampas nuevas (2026-08-28)

1. ⚠⚠ **Una métrica global sobre el encuadre entero mide el objeto más brillante, no lo que
   preguntas.** Los 11 fondos dieron cifras **idénticas** (`P99` 99.7, croma 0.0), `bg_abyss` —que es
   negro— incluido: era **el suelo** dominando la cola alta. **Medir por bandas**
   (`Tools/mide_bandas.py`: agua 0.12-0.50, suelo 0.80-0.93).
   🧭 *Un dato demasiado limpio es tan sospechoso como uno imposible.*
2. ⚠⚠ **Una métrica agregada sobre una región que CONTIENE el defecto mide el defecto.** La medida
   de textura dio «**textura OK 86 %**» a la imagen reventada: los **bordes del clip** son gradientes
   enormes que sostenían la desviación. Cura: medir sólo donde queda rango (`L* < 85`) con la máscara
   **erosionada** (`Tools/mide_textura_suelo.py`).
3. ⚠ **Sincronizar contra una línea que no distingue lo que buscas.** Se esperó `BLOOM:`, que es
   **idéntica con el bloom encendido y apagado**, teniendo `GRADE: bloom=OFF` al lado; con `sleep`
   encadenados, **las 6 etiquetas de la primera tanda salieron falsas** (una captura llegó 7 s
   *después* de acabar la sesión). Capturar **por evento**, y **anotar el segundo de cada captura en
   un acta** para que un desfase futuro salga como desfase y no como dato.
4. ⚠⚠ **`adb exec-out screencap` puede devolver un FOTOGRAMA CONGELADO** sin dar error, con la app
   viva y el log sano (`stream sent=379 fail=0`). Cuatro capturas byte a byte iguales estuvieron a
   punto de colar **«dos sustratos fundidos a ΔE 0.1»**. El md5 lo caza en un segundo, y hay que
   compararlo **también alrededor de un cambio**: dos capturas idénticas a los lados de un
   `change_bg` significan que **el cambio no llegó**.
   🧭 *Un cero perfecto casi nunca es un resultado; casi siempre es un fallo de tubería.*

⚠ **Y una de proceso: NO editar un script mientras se está ejecutando.** Bash lo lee **por trozos
sobre la marcha**; añadirle una guarda en vuelo lo reventó a media función y la tanda salió **sin una
sola captura**. **`bash -n` daba verde**: el fichero era válido, lo inválido era el estado del
intérprete. 🧭 *Un script en ejecución es un artefacto en uso, no un fichero.*

### 🎨 ¿Se distinguen los fondos y los suelos en la tele? (2026-08-28)

Criterio del user: *«los fondos, suelos, luces y todo eso deben ser diferenciales como en la app
móvil»*. Varios son de pago. Medido con `Tools/mide_diferencial.py bg` / `sub` (ΔE al vecino más
parecido, **en la banda que toca**):

- **Fondos: ninguno fundido.** Y la tele los separa **MÁS** que el arte (`bg_tropical` 33.2 → 37.3;
  `bg_classic` 23.0 → 30.5). Sólo comprime en la zona oscura (4.4 → 3.3).
- **Sustratos: sólo `sub_sand` / `sub_white`** fundidos (ΔE 1.3), y **ya venían a 2.2 en el arte**.
- ⚠ **Las 7 luces NO están medidas.**

⇒ **Tres pares apretados, los tres de origen en el arte** — ninguno lo arregla un ajuste de render:

```
sub_sand / sub_white     arte 2.2 -> pantalla 1.3   FUNDIDOS   (los dos gratis)
bg_abyss / bg_cave       arte 4.4 -> pantalla 3.3   casi       (los dos de pago)
bg_deep  / bg_night      arte 6.4 -> pantalla 5.5   casi       <- CRUZA LINEA DE PRECIO
```

⚠ Aviso de la sesión del móvil: **`colorA`/`colorB` es código muerto** para los sustratos que tienen
PNG — `DecorationPlacer.BuildFloorMaterial` carga `Resources/Substrates/{id}.png` y gana; el ruido
Perlin con `colorA`/`colorB` es sólo el fallback. **El arreglo son los PNG, no el código.**

### 🚻 Las frases de la splash son NEUTRAS en género — el sexo no viaja (2026-08-28)

`TvFishEntry` trae `speciesId`, `nickname`, `uid` y `ageScale`, **y nada más**. El móvil tiene el
sexo en su save (`OwnedFishSave.sex`) pero **no lo manda**, así que un macho salía como *«Nemo está
deseando que LA veas»*. **7 de 26** plantillas estaban marcadas; ya son neutras en `es` y `en`, con
**guarda en `Tools/test-frases.js` validada en rojo**.

⚠ **No se puede deducir:** `pairs` trae `maleUid`/`femaleUid`, pero sólo cubre peces **emparejados**
y llega **después del INIT**, o sea cuando la splash ya lleva rato rotando frases.

Campo `sex` ✅ **lo manda el móvil desde la 1.2.6 / code 41** (30-ago), documentado en
`CAST_CONTRACT_TV.md`. Valores exactos: `"Male"` · `"Female"` · `"Unknown"` · `""` (mayúscula
inicial, `.ToString()` de un enum de C#). Cualquier otra cosa → tratar como desconocido, **sin
normalizar a ciegas**.

⚠⚠ **CORREGIDO EL 30-ago — ESTE DOC PEDÍA LO CONTRARIO.** Decía: *«El save del móvil tiene `"Male"`
por defecto, así que `"Male"` no significa «es macho»: el emisor debe mandar `""` cuando no esté
seguro»*. **La conclusión se da la vuelta**, y lo persiguió la sesión del repo móvil: `sex` sólo se
escribe con un valor **deliberado** (`SaveSystem.AddFish:383`; `MigrateSave` no lo toca), así que ese
`"Male"` residual sale **sólo en peces adquiridos antes del 2026-03-09** — y para ésos **la propia app YA los
trata como machos** (`FishInspectorUI:340` les pinta ♂, `FishStatusOverlay:34` color de macho,
`BreedingManager:236` los empareja como machos). Mandar `""` haría que **la tele dijera una cosa y el
móvil otra con las dos pantallas delante del usuario**. ⇒ **manda el valor guardado tal cual**; `""`
sólo lo mandan clientes que no traen el campo.

🧭 **La regla «manda `""` si no estás seguro» era buena en abstracto y mala aquí: NO EXISTE el estado
“no seguro”. La TV está exactamente igual de segura que la pantalla del móvil, y con las dos delante
la COHERENCIA ENTRE PANTALLAS gana a la corrección abstracta.**

✅ **HECHO EL 31-ago: la TV ya lo consume**, y **sin build de player** — lo lee el `index.html`
(`_leerAcuarioParaFrases`), no `CastDataTypes`.

- **`"Male"` y `"Female"` marcan género; `"Unknown"`, `""`, ausente y cualquier otra cosa → banco
  NEUTRO**, sin normalizar a ciegas. Las frases de **pareja** se quedan neutras siempre (dos peces
  pueden tener sexos distintos).
- 💰 **`"Male"` SÍ marca, y es deliberado.** Desde el runtime **no se puede distinguir «macho de
  verdad» de «macho por defecto»** ⇒ mandarlo al neutro **no arregla el pez roto: degrada a todos los
  sanos**, que son mayoría desde marzo. El riesgo se acota por diseño: **concordancia gramatical**
  («está deseando que **lo** veas»), nunca «el macho Nemo» ⇒ **el peor caso es un pronombre**.
- ⚠⚠ **El camino NEUTRO es EL camino** mientras la mayoría no actualice, y si se rompe **no se nota**
  ⇒ tiene tests propios.
- ⚠⚠ El log separa los cuatro: `sexo M1/F1/Unknown1/ausente1` (+`/RAROSn` si llega algo no
  reconocido). **`ausente` va aparte de `Unknown` a propósito**: el día que el emisor deje de mandar
  el campo por un bug, juntarlos lo leería como «peces sin sexo conocido» en vez de como **regresión
  del emisor**.
- 🧪 `Tools/cast-headless.js` ya manda **`--sex`** y **`--lang`**. `--sex ciclo` reparte **los cuatro**
  caminos. Nuevo **`--dry-init`**: imprime el INIT que se enviaría y sale, **sin red ni tele**.
  ⚠⚠ **El defecto de `--sex` es `ninguno`** a propósito — cambiar el INIT por defecto haría que una
  tanda de hoy **no fuera comparable** con las de agosto, y ese desfase **no da ningún error**.
  ⚠⚠ Y el arnés **emite lo que se le teclee**: verlo funcionar prueba que **el `index.html` parsea**,
  NO que **el móvil manda**. Eso sólo lo cierra un volcado de la APK real.

⚠⚠ **CORREGIDO EL 31-ago: la frontera NO es «v1.2», es el 2026-03-09.** El campo `sex` entra en el
commit `07b9091` («feat: age/sex identity»), de **marzo**, no en la v1.2 de breeding — lo persiguió
por git la sesión del repo móvil. Decir «pre-v1.2» señalaba una ventana **MÁS GRANDE que la real**.
Desde esa fecha la mitad de los peces nacen `Male` por un `RandomSex()` **deliberado**: ésos son dato
bueno.

🔴 **Bug abierto del MÓVIL, salido de aquí:** los peces adquiridos **antes del 2026-03-09** son
**todos `"Male"`** y nadie les asigna sexo nunca ⇒ quien venga de esa época **no puede emparejar sus
peces viejos entre sí**. Es **contenido de pago (cría) que un usuario antiguo no puede usar**, y no da
ningún error.

🏆 **Por qué aguantó seis meses sin que nadie lo viera:** ese mismo commit traía un `MenuItem` de
Editor («Migrar Identidad Peces») que sexaba los peces existentes. Era **Editor-only** ⇒ parcheó el
save del **desarrollador** y no corrió jamás en un dispositivo. Quien mirara esa máquina vería peces
bien sexados. **El propio arreglo borró la evidencia de que hacía falta**, y hoy el `MenuItem` ni
existe: no quedó ni el arreglo ni la señal de que faltaba. 🧭 Misma familia que el `_lastSenderId`
(el aviso del 2º sender viajaba al 2º sender): **la comprobación se hizo donde el fallo no vivía.**


### 💡 Las 7 luces: cómo se miden (2026-08-30) — tercera pata del criterio del user

Fondos y sustratos ya medidos (arriba); las **7 luces** no, y **5 de ellas son de pago**.
`Tools/barre-luces.sh` barre en el device y `Tools/mide_luces.py` analiza.

⚠⚠ **Una luz NO se mide como un fondo.** Actúa por **dos caminos** y hay que separarlos:
**(1) iluminación real** —3 spots, `dirDimFactor`, `ambientBlend`— que sólo alcanza a lo que se
ilumina; **(2) post** —`colorFilter` + `postExposure` del `ColorAdjustments` a priority 11— que
alcanza al frame entero, **incluidos los shaders unlit** que la iluminación no toca.

El telón es **unlit** ⇒ **la banda de agua ve casi sólo el post y el suelo ve los dos sumados. La
diferencia entre esas dos bandas ES la descomposición.**

| banda | filas | qué ve |
|---|---|---|
| agua alta | 0.12–0.50 | post casi puro |
| agua honda | 0.50–0.75 | post + niebla |
| suelo cercano | **0.90–1.00** | iluminación + post |

⚠ El suelo va con el **último 10 %**, no con el `0.80-0.93` de los sustratos: la banda ancha promedia
el suelo lejano niebleado y **mide la niebla** (§0.6.2 de `CAST_PARIDAD_VISUAL.md`).

🧭 **`light_white` no es la referencia que parece:** es neutro **sólo en post** (filtro `(1,1,1)`,
exposure `0.00`); su spot es `(1.00,0.97,0.93)` y su `spotIntensity` **1.0** contra **2.5–3.5** de los
otros seis ⇒ **en el suelo el post negativo pelea contra ×3 de luz: esa banda no aísla para nadie.**

⚠⚠ **`light_cycle` va aparte, AL FINAL y como RANGO:** reescribe spots y `colorFilter` **cada frame**
a 0.07 Hz (periodo **14,3 s**). 9 capturas cubriendo un periodo, fuera de la tabla de ΔE. Si fuera en
medio, la transición de 0,7 s del preset siguiente correría contra su `Update()`.

⚠ **Confound que no estaba en ninguna lista: el ciclo día/noche.** `AmbientModeController` toca la
direccional y el ambiente —que caen en las mismas bandas— y **manda el reloj local** mientras no
llegue un UPDATE ⇒ un barrido que cruce la hora **cambia de fase a mitad**. Se fija con `ambient=day`.

**🏆 Restar deltas de Lab NO separa un producto.** `ILUM = Δ(suelo) − Δ(agua)` daba **4,4-16,5** sobre
fixtures donde la respuesta correcta era **0**: Lab es no lineal y esa curvatura se colaba entera
etiquetada como «iluminación». El post es un **producto por canal**, y un producto sólo es separable
en un espacio **LINEAL**:

```
ganancia = lin_k(agua) / lin_white(agua)      <- el post, MEDIDO
previsto = lin_white(suelo) * ganancia        <- el suelo si SOLO cambiara el post
ILUM     = medido(suelo) - previsto           <- los spots
```
Calibrado en los **dos** sentidos: sin diferencia de iluminación baja a **0,2-2,2**; con iluminación
inyectada sube a **36,1** con las otras cuatro quietas ⇒ **suelo de ruido 2,2, umbral 2,5**, impreso
**en la fila** de la tabla. 💰 `ILUM` ≈ 0 significa que ese preset es **sólo un filtro de color
encima**: al user le llega un tinte, no una luz.

### ⚠⚠ Un patrón que casa con el ÉXITO Y con su FRACASO no es una guarda (2026-08-30)

`barre-luces.sh` esperaba `AQUARIUM READY`. Cuando los bundles no llegan, la splash emite su red de
seguridad a los 90 s: **`⚠ splash: AQUARIUM READY no llegó en 90s — se descubre la escena igual`**,
que **contiene la cadena**. El `grep` daba **VERDE** con la línea que dice lo contrario y el barrido
siguió **4 minutos** fotografiando una pantalla negra. La buena es `AQUARIUM READY: <n> fish active`
⇒ **exigir los dos puntos**.

⚠ Es peor que un instrumento que se queda mudo: **el mudo es sospechoso, el verde es tranquilizador.**

Del mismo día: se le atribuyó al móvil un `SIN REMAPEO: el sender no mando tankHalfWidth` que salía de
**`Tools/cast-headless.js`, que manda `tankHalfWidth: 0.0` a propósito**. 🧭 **Medir con un emisor que
no es el de producción y tratar el resultado como si lo fuera.**

### ✅ RESUELTO — Si `workers.dev` se queda sin ruta: el host NO está en el player (30-ago → 31-ago)

> ✅ **31-ago: la ruta VOLVIÓ sola.** Worker **`401`** sin token · R2 público **200** · endpoint S3
> **lista y escribe** (PUT+HEAD+DELETE) · **80 bundles = 87,3 MB MEDIDOS**, ya no inferidos.
> ⇒ **el dominio propio deja de ser urgente.** Sigue siendo buen seguro contra el próximo corte, y
> el plan de abajo queda **listo para ejecutar**, pero no bloquea nada.
> ⚠⚠ **La nota que describe un bloqueo SOBREVIVE al bloqueo y lee igual de convincente que cuando
> era cierta.** Esta sección estuvo en presente medio día después de volver la ruta.
> 🧭 **Al desbloquear algo, buscar quién lo daba por bloqueado.**

El 30-ago la tele se quedó en **`BDL 1/7`** con **cero errores** y pantalla negra. No era el token, ni
el anti-bot, ni el Worker: **Telefónica sin ruta a `188.114.96.0/24`** (y **tampoco al endpoint S3 de
R2**, `172.64.66.1`/`.190.1`), llegando bien al resto de Cloudflare. Medido por **dos** caminos
independientes y por **dos** líneas (fija y móvil). ⚠ El hotspot **no sirve**: falla igual.

⭐ **La salida, y es barata:** poner el Worker en un **dominio propio** lo saca del prefijo roto.
- **La URL NO está en el player**: `webgl-output.data` tiene **0** apariciones del host. Vive en el
  **catálogo** ⇒ **redesplegar catálogo, NO rebuild de player.**
- **El hook `TvBundleAuth` casa por RUTA (`/bundle/`), no por host** — decisión de agosto que paga aquí.
- ⚠⚠ **El nombre es ARITMÉTICA**: el catálogo guarda el host **una vez**, con **prefijo de longitud**
  (`47` = 40 de host + 7 de `/bundle`). Sólo un host de **exactamente 40 bytes** permite el parche
  **en sitio**; con otro haría falta un New Build de Addressables y entran los 80 hashes.
- ⚠⚠ **Nada de `npx wrangler deploy`**: sube **código** además de rutas, y sería el despliegue a
  ciegas que prohíbe la nota del 28-ago. El Custom Domain se pone **por panel**; el código no se toca.
- 🧭 **La prueba: `401` sin token y `200` con token.** El **401 es lo que discrimina** — un 200 lo da
  cualquier servidor; **sólo nuestro Worker rechaza**.
- ✔ `catalog.hash` **no se toca** (`m_DisableCatalogUpdateOnStart: True`), y los tres ficheros del
  catálogo van con `max-age=60` ⇒ **la vuelta atrás surte efecto en ~1 minuto**.
- ⚠ **El R2 público NO sirve los bundles** (`/bundles/` → 404) y ponerlos ahí sería deshacer el cierre
  del 20-ago: **el argumento decisivo es legal** — Pack 24 y Sketchfab no-CC0 **prohíben redistribuir
  los assets crudos**, y un bucket público **es** redistribución.
- ⚠ Mientras dure un corte así, el `aws s3`/boto3 de este doc **no funciona**: subir **por el panel de
  R2**. (El 31-ago volvió a funcionar: verificado con PUT + HEAD + DELETE contra el bucket privado.)

### ✅ `RP: … sombras=OFF` NO es una regresión (2026-08-30)

El asset lleva `m_MainLightShadowsSupported: 0` y la línea lo reporta con fidelidad, pero **las
sombras de este proyecto no son las de URP**: las pintan `Appquarium/PlanarShadow` y
`Appquarium/FishShadow` (`TvFishShadows.cs:94`, `DecorationPlacer`) por proyección plana contra el
suelo, que no dependen de esa opción.
⚠ **La etiqueta engaña y volverá a costar una investigación**: dirá `OFF` para siempre estando todo
bien. Renombrarla a `urpShadows=` cuesta un build ⇒ **aprovechar el próximo**, no hacerlo solo.

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
