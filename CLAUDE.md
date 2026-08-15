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

### Estado actual — 2026-08-15 ⭐

En R2: `.data` = **16,9 MB** | `.wasm` = **25,4 MB** | receiver limpio (sello `rcv 2026-08-15 visual`).
**Validado en el Xiaomi TV Box S el 2026-08-15**, con acuario real y sin reiniciar la caja:

| | 12 peces + 6 decos | 25 peces + 6 decos |
|---|---|---|
| FPS (medio / peor) | **45 / 36** | **37 / 17** |
| WASM heap | 133 MB, plano | 191 MB, plano |
| Memoria libre del sistema | — | 19 % (banda estable validada: 22-24 %; peligro ~10 %) |
| Sesión | 900 s, 0 cortes | 420 s, 0 cortes |

Sale a **~4,5 MB y ~0,6 fps por pez**. El techo no son los peces: **una deco cuesta 8-13 MB**.
Sombras de decos y de peces **visibles y medidas** (ancla −106 de contraste, roca −130, pez −22).

⚠ El `.wasm` de 25,4 MB depende de `Code Optimization = DiskSizeLTO`, que **no está en git**
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
| Managed Stripping Level | **Minimal** (`WebGL: 4`) ⚠ el doc decía High — era falso |
| IL2CPP Code Generation | **OptimizeSize** |

> **⚠ 2026-08-15 — CORREGIDO: el nivel real es `Minimal`, no High.** `ProjectSettings.asset:919`
> → `managedStrippingLevel: { WebGL: 4 }`, y en el enum de Unity 4 = Minimal (High es 3).
> Comprobado además en el output del linker del build del 12-ago: `Unity.Addressables.dll`
> pesa lo mismo antes y después de strippear (0 % de reducción), que es exactamente lo que
> hace Minimal — copiar los ensamblados sin tocarlos.
>
> **Oportunidad pendiente:** subirlo a High (`3`) reduciría el `.wasm`, y el tamaño del `.wasm`
> es la causa raíz confirmada de los cortes de sesión. `Assets/link.xml` ya preserva los tipos
> de URP que High podría romper. Requiere rebuild (~55 min) + revalidación en la tele, y el
> riesgo clásico es `TypeLoadException` en runtime. No hacerlo a la ligera, pero está sobre la mesa.

### Comandos clave

```powershell
# Verificar CORS R2 (pre-req crítico)
curl -I -H "Origin: https://anything" https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/<bundle>.bundle

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
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
# El catalog.hash suele fallar con sync → subir por separado:
aws s3 cp ServerData/WebGL/catalog_1.2.1.hash s3://appquarium-tv/bundles/catalog_1.2.1.hash `
  --profile r2 `
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
aws s3 rm s3://appquarium-tv/bundles/ --recursive `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com
# 2. Subir bundles nuevos
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
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
| `Assets/Scripts/Core/` | AquariumManager (slim), AmbientModeController, AquariumCameraController, AudioManager, CastReceiver, CastDataTypes, FishSpawner, FoodItem, PostProcessingSetup, **TvSceneBootstrap** ⭐, **TvFoodManager** |
| `Assets/Scripts/Fish/` | FishAgent, FishBrain, SteeringController, NeedsModule, FishProceduralAnimator (sync mobile) |
| `Assets/Scripts/Tank/` | TankController, DecorationPlacer, BubbleSystem, TankBackground, TankLightingController, WaterSurface (sync mobile) |
| `Assets/Scripts/Data/` | FishData, DecorationData, TankData (sync mobile) |
| `Assets/Scripts/Utils/` | AppFlags, AppVersion, CatalogLoader (sync mobile) |
| `Assets/Scripts/Stubs/` | TvStubs (stubs para clases mobile-only referenciadas indirectamente) |
| `Assets/Editor/` | TvAddressablesSetup, TvBuildTools, SyncFromMobileMenu, **TvBuildPostprocess** (parchea settings.json tras cada build), **TvProdBuild** ⭐ (build de producción en batchmode + preflight de audio), **TvWasmOptimize** ⭐ (fuerza `DiskSizeLTO` en cualquier build), TvEmptyTestBuild, TvShadowDiag |
| `Tools/` | ~30 ficheros. Los que importan: **SyncFromMobile.ps1**, **cast-headless.js** (sender sin navegador), **cast-run.sh** (ciclo de medición completo), **restore-production-receiver.sh**, y los `rcv-*.html` (receivers de diagnóstico). ⚠ Varios escriben en R2 de producción. |

---

## Cast SDK — notas técnicas

- **Receiver Published** App ID `8F6C873F` — funciona en cualquier device sin registrar Cast Console
- **Cast SDK timeout = 30s** desde "Connecting…" hasta receiver READY. Sin esto la sesión aborta.
- **Xiaomi TV Box S** como `MiTV-AFMU0` en LAN. Cast SDK 3.72.446070.
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
| `Environments_Remote` | PackSeparately | 11 backgrounds | 11 |
| `Audio_Remote` | PackSeparately | 1 clip (`ambient_music`) | 1 |
| `Default Local Group` | PackTogether | scaffolding | 1 |

Total: **186 bundles en local, 188 claves en R2** (fish×25 + decos×54 + envs×11 + audio×1 +
duplicados de builds anteriores que nadie ha limpiado). El catalog apunta siempre al correcto.
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
