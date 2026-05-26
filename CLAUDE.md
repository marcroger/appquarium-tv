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
| [`CAST_NETFLIX_SPEC.md`](CAST_NETFLIX_SPEC.md) | ⭐ Spec ejecutable para Fase A.1 — contrato actual del refactor Netflix. 10 secciones. |
| [`BUILD_REPORT_2026-05-25.md`](BUILD_REPORT_2026-05-25.md) | Diagnóstico del build de 411MB + análisis duplicación. Justifica por qué hacemos lo que dice el spec. |
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

**Lo que NO existe en TV (mobile-only):** UI, IAP, Ad, SaveSystem, BreedingManager, FoodManager, InputHandler, FieldGuide, Localization (TV asume inglés/idioma fijo).

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

---

## Build pipeline (resumen)

### Fase A.1 — Slim base WebGL (BLOQUEANTE actual)

Ver `CAST_NETFLIX_SPEC.md` §5 para los 12 pasos detallados. Objetivo: bajar `.data` de 411MB a ≤50MB sacando los GLBs duplicados que se cuelan vía referencias del scene a los SOs.

### Comandos clave

```
# Verificar CORS R2 (pre-req crítico, §4.8 del spec)
curl -I -H "Origin: https://anything" https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/<some-bundle>.bundle

# Limpiar cache Addressables ANTES de Build Player Content (bug Unity conocido)
Remove-Item -Recurse -Force "Library\com.unity.addressables\aa"

# Build Player Content desde Unity: Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script

# Build WebGL base: File → Build (output en webgl-output/)

# Deploy a R2
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"

aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete
```

---

## Scripts — Ubicaciones

| Carpeta | Contenido |
|---|---|
| `Assets/Scripts/Core/` | AquariumManager (slim), AmbientModeController, AquariumCameraController, AudioManager, CastReceiver, CastDataTypes, FishSpawner, FoodItem, PostProcessingSetup, **TvSceneBootstrap** ⭐ |
| `Assets/Scripts/Fish/` | FishAgent, FishBrain, SteeringController, NeedsModule, FishProceduralAnimator (sync mobile) |
| `Assets/Scripts/Tank/` | TankController, DecorationPlacer, BubbleSystem, TankBackground, TankLightingController, WaterSurface (sync mobile) |
| `Assets/Scripts/Data/` | FishData, DecorationData, TankData (sync mobile) |
| `Assets/Scripts/Utils/` | AppFlags, AppVersion, CatalogLoader (sync mobile) |
| `Assets/Scripts/Stubs/` | TvStubs (stubs para clases mobile-only referenciadas indirectamente) |
| `Assets/Editor/` | TvAddressablesSetup, TvBuildTools, SyncFromMobileMenu |
| `Tools/` | SyncFromMobile.ps1 |

---

## Cast SDK — notas técnicas

- **Receiver Published** App ID `8F6C873F` — funciona en cualquier device sin registrar Cast Console
- **Cast SDK timeout = 30s** desde "Connecting…" hasta receiver READY. Sin esto la sesión aborta.
- **Xiaomi TV Box S** como `MiTV-AFMU0` en LAN. Cast SDK 3.72.446070.
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

Total bundles en `ServerData/WebGL/`: **92** (post build 26-may 03:35).

**LZ4 compression** confirmado. **NonRecursiveBuilding=true** (cambio pendiente a `false` en Fase A.1 — ver spec §4.2).

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
