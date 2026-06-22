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

**SIEMPRE antes de cualquier build/deploy:** sincronizar cambios del mobile.

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
- `loadType: 0` → cambiar a `loadType: 2` (Compressed in Memory) — si no, rebuild + deploy
- Referencia correcta: `ambient_water.wav.meta` (loadType:2, quality:0.7, forceToMono:0)
- `ambient_bubbles.wav.meta` corregido 2026-06-20 (loadType:2, forceToMono:1, quality:0.7, 3D:0)

---

## Build pipeline (resumen)

### Estado actual — 2026-06-22

`.data` = 32.0 MB | `.wasm` = 42.2 MB en R2. 🎉 **FLUIDO en Xiaomi TV Box S** — bloom OFF + renderScale 0.7 + targetFrameRate 30.
**Calidad visual mejorada** — Tonemapping Neutral + saturation +18 + contrast +10 + SMAA Low. Confirmado visualmente en TV (2026-06-22).

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

**⚠ PENDIENTE — Disconnect sender (mobile):**
- Se desconecta a los ~2-3 min. Root cause probable: Android Doze mata el proceso del sender.
- Fix en mobile (repo separado): PARTIAL_WAKE_LOCK en `CastPlugin.java` + keepalive PING sender→receiver cada 60s.
- Para diagnosticar: ver razón en panel debug TV al desconectar (`reason:REQUESTED_BY_SENDER` vs `reason:unknown`).
- `reason:unknown` = Android mató el proceso → WakeLock en mobile es el fix.

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
| Managed Stripping Level | **High** |
| IL2CPP Code Generation | **OptimizeSize** |

> **⚠ NO cambiar Managed Stripping a menos que haya TypeLoadException en runtime.** High stripping es necesario para que el wasm quepa en memoria del Cast device.

### Comandos clave

```powershell
# Verificar CORS R2 (pre-req crítico)
curl -I -H "Origin: https://anything" https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/<bundle>.bundle

# Build bundles: Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script
# ❌ NO usar "Update a Previous Build" — es para workflows CCD (Unity Cloud), no para R2 self-hosted.
#    Causa builds fantasma que no producen output cuando hay nuevas dependencias.
#    "New Build" con SBP cache ya es efectivamente incremental: solo reconstruye lo que cambió.
# Build WebGL player: File → Build Settings → Build (output en webgl-output/)
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
aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete `
  --exclude "bundles/*" `
  --cache-control "public, max-age=3600"
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
# 3. Subir player (SIEMPRE --exclude "bundles/*" con --delete)
aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete `
  --exclude "bundles/*" `
  --cache-control "public, max-age=3600"
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
| `Assets/Editor/` | TvAddressablesSetup, TvBuildTools, SyncFromMobileMenu, **TvBuildPostprocess** (parchea settings.json tras cada build) |
| `Tools/` | SyncFromMobile.ps1 |

---

## Cast SDK — notas técnicas

- **Receiver Published** App ID `8F6C873F` — funciona en cualquier device sin registrar Cast Console
- **Cast SDK timeout = 30s** desde "Connecting…" hasta receiver READY. Sin esto la sesión aborta.
- **Xiaomi TV Box S** como `MiTV-AFMU0` en LAN. Cast SDK 3.72.446070.
- **`ctx.start()` — parámetros correctos:** `{ disableIdleTimeout: true, maxInactivity: 3600 }`
  - `disableIdleTimeout: true` — no shutdown cuando 0 senders conectados
  - `maxInactivity: 3600` — no desconectar sender "inactivo". ⚠ El SDK **rechaza valores ≤ 5** con error en runtime. Usar 3600 (1h) como "nunca".
  - El keepalive cada 60s en JS (`sendCustomMessage`) complementa esto para Cast Built-In implementations que ignoran `maxInactivity`.
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
| `Audio_Remote` | PackSeparately | 2 audio clips | 2 |
| `Default Local Group` | PackTogether | scaffolding | 1 |

Total bundles en R2: **92+** (fish×25 + decos×54 + envs×11 + audio×2). Algunos tienen versiones duplicadas de builds anteriores — el catalog siempre apunta al correcto.

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
