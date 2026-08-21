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
| [`CAST_NEXT_SESSION_2026-08-22.md`](CAST_NEXT_SESSION_2026-08-22.md) | ⭐⭐ **EMPEZAR AQUÍ.** Cierre del 21-ago: la TV recuperó el color (llevaba desde siempre sin aplicar ningún grado, por 5 causas encadenadas). Protocolo auditado 11/11 y coste de URP medido. |
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
| `Tools/` | ~30 ficheros. Los que importan: **grade-tune.js** ⭐ (afina el grado sobre el player REAL en Chrome, mandando mensajes `GRADE`), **grade_contact_sheet.py** (hoja de contactos + luminancia/saturación por bandas, con guarda de «esto no mide nada»), **r2-auth-worker/** ⭐ (el Worker portero de los bundles + sus dos baterías de pruebas), **SyncFromMobile.ps1**, **cast-headless.js** (sender sin navegador), **cast-run.sh** (ciclo de medición completo), **restore-production-receiver.sh**, **extract_glb_textures.py** (saca las texturas embebidas de un GLB + `mapeo.txt`, paso previo a `TvDecoOptimize`), **r2_huerfanos.py** (lista/borra bundles huérfanos de R2), y los `rcv-*.html` (receivers de diagnóstico). ⚠ Varios escriben en R2 de producción. |

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
