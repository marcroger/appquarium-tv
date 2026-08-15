# Cast Netflix Architecture — Spec ejecutable

> ✅ **COMPLETADO — mergeado a `main` en `325a931` (2026-08-15).**
> Este spec describe un refactor **ya hecho**; sus checkboxes sin marcar y sus «notas para el
> implementador» son del proceso, no trabajo pendiente. Cifras del enunciado que ya no valen:
> `.data` **16,9 MB** (el spec dice 411 MB), **5** refs directas a SOs en TvScene (decía 84, y
> ≤10 era su propio criterio de éxito), los bundles de pez pesan ~1,5 MB (decía 2,5 KB).
> ⚠ **NO ejecutar su «Fase A.0 OBLIGATORIO: sincronizar desde el móvil»** — hoy eso rompe la
> compilación y borra el lote visual. Ver el aviso en `CLAUDE.md` → «Sync desde mobile».
> `Initial Memory Size` real: **64 MB**, no los 256 MB del spec (256 fue la causa del OOM).

**Estado:** 2026-05-26 — spec listo para implementación por Sonnet.
**Pre-requisito:** lectura de [BUILD_REPORT_2026-05-25.md](BUILD_REPORT_2026-05-25.md) (diagnóstico) y [ADDRESSABLES_ROADMAP.md](ADDRESSABLES_ROADMAP.md) (estado bundles).

---

## 1. Visión y criterios de éxito

### Visión
Cast a TV con descarga progresiva de assets. El TV solo descarga lo que está actualmente en el tanque del usuario. Escala a cualquier cantidad futura de peces/decoraciones sin penalizar al usuario que tiene 3 peces.

### Criterios de éxito (must-have, no negociables)
1. **Cast funciona en Xiaomi TV Box S** (Cast Built-In, SDK timeout 30s) — receiver pasa de "Connecting…" a "Ready" en <10s
2. **TV renderiza idéntico al móvil** — mismos peces, mismas decos, mismo background, sustrato, lighting, ambient mode
3. **Solo descarga lo activo** — usuario con 3 peces no descarga los 25; con 5 decos no descarga las 54
4. **Escalable** — añadir un pez nuevo en futuro = 1 bundle, ~15-25MB, descargado solo si el usuario lo activa
5. **No tocar repo móvil** — todos los cambios en `D:\dev\appquarium-tv-unity\`
6. **Build razonable** — siguientes builds <2h (vs 16h actuales en cold cache)

### Criterio de aceptación final
Test manual en Xiaomi TV Box S `MiTV-AFMU0`:
- Móvil con 3 peces starter + 5 decos básicas → Cast → TV muestra el acuario en <15s totales (receiver ready + bundles)
- Tap "comprar pez" en móvil → TV añade el pez sin recargar todo
- Toggle día/noche en móvil → TV transiciona suave

---

## 2. Estado actual (post Build 2026-05-26)

| Componente | Estado | Tamaño |
|---|---|---|
| 92 bundles remotos en `ServerData/WebGL/` | ✅ OK | 389 MB |
| `webgl-output/Build/webgl-output.data` | ⛔ inflado | **411 MB** |
| Duplicación assets entre `.data` y bundles | ⛔ ~547 MB raw | — |
| `TvSceneBootstrap.LoadAndInitializeCoroutine` | ✅ lazy load implementado | — |
| Cast receiver published `8F6C873F` | ✅ functional | — |

**Causa raíz del bloqueo:** `TvScene.unity` tiene 84 referencias directas a SOs (`AquariumManager.allFishCatalog/allDecoCatalog`). Cada SO referencia su prefab → GLB. Unity baka todo el grafo en `.data` aunque los mismos prefabs también vivan en grupos Addressables → contenido duplicado.

---

## 3. Arquitectura objetivo

```
┌─────────────────────────────────────────────────────────────┐
│  WebGL base (.data ≤ 50 MB)                                  │
│  ────────────────────────────                                │
│  • Scene scaffolding (TvScene without populated catalogs)   │
│  • Core scripts (AquariumManager, FishAgent, etc.)           │
│  • URP shaders + global settings                             │
│  • Audio (ambient_music.mp3 — bakeado)                       │
│  • Backgrounds + Substrates de Resources/* (Fase B mueve)    │
│  • NO prefabs Pack 24 / NO GLBs decos / NO SOs               │
└────────────────────────────────────────┬────────────────────┘
                                         │ Cast SDK INIT
                                         ▼
┌─────────────────────────────────────────────────────────────┐
│  TvSceneBootstrap parses INIT state                          │
│  ─────────────────────────────────                           │
│  state.activeFish[N] = [fish_X, fish_Y, fish_Z]              │
│  state.decoJson      = [deco_X_0, deco_Y_0, deco_Z_0]        │
└────────────────────────────────────────┬────────────────────┘
                                         │ parallel LoadAsync
                                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Cloudflare R2 — bundles individuales (un asset por bundle)  │
│  ─────────────────────────────────────                       │
│  fish_X.bundle      ≈ 5-15 MB  (SO + prefab + textures)      │
│  fish_Y.bundle      ≈ 5-15 MB                                 │
│  deco_X.bundle      ≈ 2-25 MB  (depende: anchor 2MB,         │
│                                  coral 15MB, statue 45MB)    │
│  …                                                            │
└────────────────────────────────────────┬────────────────────┘
                                         │ all loaded
                                         ▼
┌─────────────────────────────────────────────────────────────┐
│  AquariumManager.InitializeFromCastState(state, fish, deco)  │
│  → spawn fish, place decos, set bg/sub/light                 │
│  → fade-in scene visible                                     │
└─────────────────────────────────────────────────────────────┘
```

**Total descarga para un usuario starter (3 peces Common + 5 decos básicas):**
- Base: ~50 MB
- 3 bundles fish: ~30 MB
- 5 bundles deco: ~20 MB
- **Total primera sesión: ~100 MB** (vs 411 MB actual)
- Segunda sesión: ~30 MB (cache Xiaomi guarda los bundles)

---

## 4. Cambios necesarios

### 4.1 — Vaciar catálogos del scene (CRÍTICO)

**Fichero:** `Assets/Scenes/TvScene.unity`

**Cambio:** localizar el GameObject `AquariumManager` (o equivalente) y vaciar las listas `allFishCatalog` y `allDecoCatalog`. Es serialización YAML:

```yaml
# Antes
allFishCatalog:
- {fileID: 11400000, guid: 3b5fed519a4e27d4ebcfbddbc34a2128, type: 2}
- {fileID: 11400000, guid: 872f6b071c81c594daadb45a38fd6a28, type: 2}
- … (25 entries)
allDecoCatalog:
- {fileID: 11400000, guid: a0536cf876801114b8c24d24d5a140c7, type: 2}
- … (54 entries)

# Después
allFishCatalog: []
allDecoCatalog: []
```

**Verificación:** `grep -c "fileID: 11400000" Assets/Scenes/TvScene.unity` debe pasar de 84 a ~5 (los SOs no-catalog que se mantienen como ambient music, etc.).

### 4.2 — Marcar prefabs como deps implícitas + Shared_Local desde día 1 (CRÍTICO, DECIDIDO)

Hoy los 92 bundles SO pesan 2.5KB cada uno → solo contienen el SO, no el prefab.

**Decisión arquitectónica:** combinar A + Shared_Local desde el primer build para evitar duplicación de URP shaders entre los ~90 bundles (problema documentado en [Unity Asset Dependencies](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/AssetDependencies.html): *"If more than one Addressable references the same implicit asset, then copies of the implicit asset are included in each bundle"*).

**Paso A — Desactivar `NonRecursiveBuilding` en los 4 remote groups.**
- Cuando NonRecursive=false, Unity incluye en el bundle TODAS las dependencias transitivas del asset addressable que no estén ya en otro grupo addressable.
- Como los prefabs y GLBs no están en ningún grupo addressable separado, se incluyen en el bundle del SO que los referencia.
- Resultado esperado por bundle:
  - fish bundles: 2.5KB → 15-25MB (incluye Pack 24 prefab + fbx + textures)
  - deco bundles: 1.5-25MB → 2-50MB (incluye prefab + GLB + textures)

**Paso B — Crear grupo `Shared_Local` PackTogether (DESDE EL PRIMER BUILD).**
- Razón: con NonRecursive=false + PackSeparately, cada bundle traerá su propia copia de URP shaders compartidos, materiales como `WoodChest.mat`, y los 5 LOD meshes del Stylized Rock Pack. Sin Shared_Local los 92 bundles inflarían ~80MB de duplicación.
- Configuración:
  ```
  Shared_Local
    PackingMode: PackTogether (1 solo bundle)
    Compression: LZ4
    BuildPath: [UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]
    LoadPath: {Addressables.RuntimePath}/[BuildTarget]
    (NO marcar como remote — vive en StreamingAssets, va con el .data)
  ```
- Contenido inicial (basar en el build report del 25-may §4):
  - 4 worst dups (21 bundles cada uno): WoodChest.mat + URP global settings + 2 GUIDs Pack PBR
  - Stylized Rock Pack LOD meshes (rock_lod0..4.fbx)
  - Cualquier shader / material / mesh con >3 inclusiones según `Window → Addressables → Analyze → Check Duplicate Bundle Dependencies`
- Estimación contenido: ~5-10 MB compartido, descargado UNA VEZ con la base WebGL

**Paso C — Verificar tras build con `Check Duplicate Bundle Dependencies`.**
- Si quedan dups significativos (>10 inclusiones en un asset), mover ese asset a Shared_Local también
- Iterar hasta que el reporte muestre <5 inclusiones máximo por asset

**Por qué NO refactorizar a AssetReference (opción B descartada):**
- Coste 4-8h vs 1h del approach A+Shared_Local
- Breaking change en SOs (requiere migrar 79 SOs uno por uno)
- Riesgo de errores en runtime sin ganancia clara de tamaño
- Si Fase B (Backgrounds/Substrates) requiere refactor más profundo, AssetReference se evalúa entonces como migración global

### 4.2.1 — ⚠ Bug Unity conocido: limpiar cache entre rebuilds

[Issue Tracker: NonRecursiveDependencyCalculation causes prefab not to load when toggling addressable](https://issuetracker.unity3d.com/issues/nonrecursivedependencycalculation-causes-a-prefab-to-not-load-correctly-when-unchecking-its-addressable-property-and-rebuilding-addressables-without-clearing-the-cache)

Reproducido en Unity 2020.3, 2021.3, 2022.x, 2023.x. Asumimos que sigue en 6000.x.

**Workaround obligatorio entre rebuilds con cambios de addressables:**
```
Window → Asset Management → Addressables → Settings →
  Build → Clean Build → "All"
```
Equivalente CLI:
```powershell
Remove-Item -Recurse -Force "Library\com.unity.addressables\aa"
Remove-Item -Recurse -Force "Library\com.unity.addressables\BuildReports"
```
Ejecutar SIEMPRE antes de `Build Player Content` si se ha tocado configuración de algún grupo (PackingMode, NonRecursive, etc.). Sin esto, los bundles pueden cargarse con prefabs `null` en runtime → NullReferenceException.

### 4.3 — Configuración Addressables groups

En `Window → Asset Management → Addressables → Groups`:

```
Fish_Remote        (25 addresses)
  PackingMode: PackSeparately
  Compression: LZ4
  NonRecursiveBuilding: false  ← cambio 4.2
  ContiguousBundles: true
  LoadPath: R2 URL
  BuildPath: ServerData/[BuildTarget]

Decos_Remote       (54 addresses)
  igual config

Environments_Remote (11 addresses)
  igual config — pero Fase B los moverá de Resources/

Audio_Remote       (2 addresses)
  igual config

Shared_Local       (NUEVO en Fase A.2 — opcional, ver §6)
  PackingMode: PackTogether
  Path: Local (StreamingAssets)
  Compression: LZ4
  Para: URP shaders comunes, WoodChest.mat, materiales compartidos
```

### 4.4 — Player Settings → reducir base + build time

**Edit → Project Settings → Player → Web:**

| Setting | Valor | Razón |
|---|---|---|
| Compression Format | Disabled | R2 sirve raw, evita doble-gzip bug |
| Decompression Fallback | OFF | Cast Chromium sí soporta gzip nativo |
| Exception Support | None | Cast Chromium peta con wasm-EH |
| Strip Engine Code | ON | Quita features de engine no usadas |
| Managed Stripping Level | High | Quita IL2CPP no referenciado |
| WebAssembly 2023 features | OFF | Compatibilidad Cast |
| Initial Memory Size | 256 MB | Conservador para TVs viejas |
| Maximum Memory Size | 512 MB | Margen para bundles |
| Memory Growth Mode | Geometric | Crecimiento suave |

**Edit → Project Settings → Graphics → Shader Variant Stripping:**
- Strip Unused Variants: **Strict**
- Variant collection: usar el `ShaderVariantCollection` ya en el scene (warmup) si existe; si no, crear uno con menú `Save to asset` tras ejecutar la escena
- Reduce variants de URP de ~hundreds a ~10-20

**Edit → Project Settings → Quality:**
- Mantener solo 1 nivel (TV no necesita Low/Medium/High)

### 4.5 — Optimización texturas (build time)

Script editor `★ Apply TV Texture Strategy`:
- ThirdParty/Mikhail Nesterov (fish): max 2048, format ETC2 (RGBA Compressed), mipmaps OFF
- Resources/Backgrounds + Substrates: max 2048, ETC2, mipmaps OFF
- Resto de ThirdParty (decos): max 1024, ETC2, mipmaps OFF

Aplicar UNA vez. Subsecuentes builds reusan estos imports → no recomprime.

### 4.6 — TvSceneBootstrap: añadir loading UI (DECIDIDO)

**Fichero:** `Assets/Scripts/Core/TvSceneBootstrap.cs`

Hoy entre INIT y InitializeFromCastState (5-15s en cold cache R2), la pantalla está negra. Añadir overlay fullscreen con:

```
┌─────────────────────────────────────────┐
│                                          │
│                                          │
│          [Logo Appquarium]               │  ← centrado, escalado al 25% pantalla
│                                          │
│             ◌ (spinner)                  │  ← cyan C_ACCENT, rotando
│                                          │
│         Cargando acuario…                │  ← texto C_WHITE, 32px
│                                          │
│             3 / 8 cargados               │  ← texto C_MUTED, 24px, debajo
│                                          │
└─────────────────────────────────────────┘
```

**Diseño concreto:**
- Fondo: `C_BG` (#060D1A) fullscreen — no transparente para tapar el black/skybox
- Logo: `Resources/Brand/logo_appquarium.png` (si no existe, fallback a texto "APPQUARIUM" 64px Bold C_ACCENT)
- Spinner: círculo cyan rotando (Image con sprite "spinner" + componente rotador, o GameObject que rota su transform)
- Texto principal: `T("cast.loading")` con fallback "Cargando acuario…"
- Contador: actualiza cada bundle completado, formato `{done} / {total} cargados` (i18n via `T("cast.loading.progress")` con placeholders `{0}` `{1}`)
- Animación entrada: fade-in 0.3s al INIT
- Animación salida: fade-out 0.5s tras `InitializeFromCastState`

**i18n claves nuevas** (añadir a `Resources/Localization/*.json`):
- `cast.loading` → "Cargando acuario…" / "Loading aquarium…" / etc.
- `cast.loading.progress` → "{0} / {1} cargados" / "{0} / {1} loaded"
- `cast.loading.error` → "Error de descarga. Reconectando…" (para futuro retry)

Pseudocódigo:
```csharp
ShowLoadingOverlay();
int total = fishHandles.Count + decoHandles.Count;
int done  = 0;
UpdateProgress(done, total);

foreach (var h in fishHandles) {
    yield return h;
    UpdateProgress(++done, total);
}
foreach (var h in decoHandles) {
    yield return h;
    UpdateProgress(++done, total);
}

HideLoadingOverlay();
mgr.InitializeFromCastState(state, fishData, decoData);
```

⚠ **Nota técnica WebGL**: las bundles se descargan COMPLETAS antes de procesar (WebGL no permite streaming — XMLHttpRequest sync). Por tanto el contador avanza en saltos discretos (no progreso intra-bundle). Para un bundle grande (45MB coral statue), el contador se queda en "3/8" durante varios segundos hasta saltar a "4/8". Aceptable — el usuario sabe que algo pasa por el spinner.

### 4.7 — Catálogos: necesitamos populación en runtime?

Análisis: el TV es un **renderer**, no un shop. No necesita conocer todos los peces/decos disponibles — solo los del `state.activeFish/decoJson` del INIT.

**Decisión:** los catálogos quedan vacíos permanentemente en TV. Cualquier lookup `allFishCatalog.Find(d => d.itemId == X)` debe ser sustituido por `Addressables.LoadAssetAsync<FishData>(X)` que devuelve el SO directamente.

**Auditar:** buscar usos de `allFishCatalog` y `allDecoCatalog` en código TV. Lista esperada:
- `AquariumManager.InitializeFromCastState` — recibe la lista cargada via Bootstrap, debe trabajar solo con esa
- `DecorationPlacer.allDecorationCatalog` — se popula desde `allDecoCatalog`, debería pasarse explícitamente
- Cualquier UI/inspector: en TV no hay UI, todo OK

---

## 5. Plan de ejecución

### Fase A.0 — Pre-sync desde mobile (OBLIGATORIO antes de Fase A.1)

Antes de cualquier cambio en el TV project, sincronizar los cambios pendientes de mobile. En este momento el TV está OUTDATED respecto a:
- Branch `feat/deco-placement-polish` mergeada a main en mobile (commit `af61d7a` o posterior si se mergea antes): raycast surface mount, embedDepth, supportPointLocal, ResetTransform, edit panel ↺
- Cualquier otro cambio en `Scripts/{Fish,Tank,Data}` o SOs desde 2026-05-22

**Acción:**
```powershell
cd D:\dev\appquarium-tv-unity
.\Tools\SyncFromMobile.ps1 -DryRun     # listar qué difiere
.\Tools\SyncFromMobile.ps1             # confirmar y copiar
```

Esperado que reporte ~10-15 archivos diferentes (FishAgent, FishBrain, SteeringController, FishProceduralAnimator, DecorationPlacer, FishData, DecorationData, SOs varios). Aceptar todos.

Resultado: TV tiene el behavior actualizado del mobile antes de empezar el refactor Netflix.

### Fase A.1 — Slim base WebGL (BLOQUEANTE para Cast)

| Paso | Acción | Verificación | ETA |
|---|---|---|---|
| 0 | **Verificar CORS R2** (§4.8) — pre-req para que bundles carguen en WebGL Chromium | `curl -I -H "Origin: https://anything"` debe traer `Access-Control-Allow-Origin: *` | 5 min |
| 1 | Backup branch: `git checkout -b feat/netflix-architecture` | — | 1 min |
| 2 | §4.1 — vaciar `allFishCatalog` y `allDecoCatalog` en `TvScene.unity` | grep refs = ~5 | 5 min |
| 3 | §4.7 — auditar usos de catálogos, refactorizar a `Addressables.LoadAssetAsync` por key | compile OK | 30-60 min |
| 4 | §4.6 — añadir loading overlay en `TvSceneBootstrap` | manual visual | 30 min |
| 5 | §4.2 — desactivar `NonRecursiveBuilding` en 4 remote groups + crear `Shared_Local` con shared deps | `★ Print Addressables Summary` muestra 5 grupos | 30 min |
| 5.5 | §4.2.1 — **Clean Addressables build cache** antes del rebuild | `Library/com.unity.addressables/aa` borrado | 1 min |
| 6 | `Build Player Content` | ~92 bundles + Shared_Local, esperado ~600-700 MB total en `ServerData/WebGL/` | 1-3h cache caliente |
| 7 | `Window → Addressables → Analyze → Check Duplicate Bundle Dependencies → Run` | <5 inclusiones máx por asset. Si más → mover a Shared_Local | 5 min + posibles iteraciones |
| 8 | `File → Build` WebGL | `.data` esperado ≤ 50 MB | 15-30 min |
| 9 | Inspeccionar Editor.log build report — verificar GLBs NO listados en .data | top assets = framework/wasm/scenes | 5 min |
| 10 | Deploy R2: `aws s3 sync ServerData/WebGL/` + `webgl-output/` | upload OK | 5-10 min |
| 11 | Test Xiaomi → Cast desde móvil → medir tiempo carga | base <10s + bundles arrive | 10 min |

**Total Fase A.1:** ~3-5h (mayoría es build time)

### 4.8 — Verificación CORS Cloudflare R2 (pre-requisito CRÍTICO)

WebGL Chromium en Cast Receiver requiere CORS configurado en R2. Sin esto, los bundles fallan con *"Cross-Origin Request Blocked"* y no carga ningún asset.

**Verificar config actual:**
```powershell
curl -I -H "Origin: https://8f6c873f.cast.web.app" `
  https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/fish_remote_assets_fish_black_durgon_bf0228a531e9950e6e57c2b8d7a0a75b.bundle
```
Debe responder con:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, HEAD, OPTIONS
```

**Si no aparece:** configurar en Cloudflare R2 Dashboard:
1. R2 → bucket `appquarium-tv` → Settings → CORS policy
2. Añadir regla:
   ```json
   [
     {
       "AllowedOrigins": ["*"],
       "AllowedMethods": ["GET", "HEAD"],
       "AllowedHeaders": ["*"],
       "ExposeHeaders": ["Content-Length", "ETag"],
       "MaxAgeSeconds": 86400
     }
   ]
   ```
3. Save. Cambios aplican inmediatamente (no propagación CDN).

**Cache headers** (recomendado para Xiaomi reuse): añadir `Cache-Control: public, max-age=604800` a los `.bundle` files. Se hace en R2 via metadata al subir:
```powershell
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
```
Con esto la segunda sesión Cast en el mismo Xiaomi reutiliza los bundles cacheados → arranque casi instantáneo.

### Fase A.2 — Optimizaciones build time (paralela, opcional)

| Paso | Acción | Beneficio | ETA |
|---|---|---|---|
| 1 | §4.4 — Shader Variant Stripping Strict + collection | -40-60% shader compile time | 15 min |
| 2 | §4.5 — `★ Apply TV Texture Strategy` script | Builds posteriores no recomprimen | 30 min coding + reimport 30 min |
| 3 | §4.4 — Strip Engine Code + Managed Stripping High | -10-20MB wasm + base | 5 min |
| 4 | Verificar `Library/ShaderCache/` se reusa | Próximo build 1-3h vs 16h | observe |

Aplicar antes del rebuild del paso 6 de Fase A.1 si tiempo lo permite.

### Fase B — Streaming completo (NO bloqueante)

Cuando Fase A.1 valide en Xiaomi:
- Mover `Assets/Resources/Backgrounds/` → `Assets/Bundles/Backgrounds/`
- Mover `Assets/Resources/Substrates/` → `Assets/Bundles/Substrates/`
- Crear `BackgroundData` SO con `Texture2D backgroundTexture` field
- Añadir `Texture2D substrateTexture` field a `DecorationData` (sub_*)
- Refactorizar `TankBackground.SetPreset` y `DecorationPlacer.SetSubstrate` a `Addressables.LoadAssetAsync`
- Base esperada baja de ~50MB a ~25MB

ETA Fase B: 2-3h coding + 1-2h rebuild + deploy.

---

## 6. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| **CORS R2 no configurado** → bundles fallan en WebGL Chromium | Media | Cast no carga NADA tras .data | §4.8 verificar antes del deploy. Si falla, config R2 en 2 min. |
| **Bug Unity NonRecursive sin clear cache** → NRE en runtime | Alta | Bundles cargan con prefab=null | §4.2.1 — limpiar cache `Library/com.unity.addressables/aa` ANTES de cada build. Documentado en issue tracker Unity. |
| Shared_Local no captura todas las dups → bundles inflados | Media | Bundles 1.5-2× más grandes | Paso 7 del plan: correr `Check Duplicate Bundle Dependencies`, iterar moviendo a Shared_Local hasta <5 inclusiones/asset |
| Catálogo vacío rompe inicialización (algún script asume `allFishCatalog.Count > 0`) | Media | Runtime crash | Audit en paso 3. Test en Editor antes del build. |
| Cast SDK timeout aún con base de 50MB | Baja | Cast falla en redes lentas | Reducir más con Fase B + audit assets pesados restantes (ambient_music.mp3 = 4.8MB, puede pasarse a bundle remoto) |
| Bundles individuales saturan conexiones paralelas | Baja | Cargas lentas | Addressables tiene `MaxConcurrentWebRequests=3` por defecto. **WebGL NO descomprime en streaming** — cada bundle descarga completo antes de procesar, secuencial. Bundle de 45MB tarda lo suyo. |
| Shader stripping rompe rendering de algún material | Media | Visual roto en TV | Test scene completa antes de build. Ejecutar todos los presets de luz, todas las ambient modes. |
| Build sigue siendo lento (>3h) tras §4.4-4.5 | Media | Iteración lenta | Bloquear estudio del Editor.log de tiempo. Posibles culpables restantes: scene reimport, IL2CPP linking. |
| WebGL bundle cache no se reutiliza tras refresh ([Unity issue](https://issuetracker.unity3d.com/issues/addressable-bundles-are-not-retrieved-from-the-cache-when-a-webgl-player-is-refreshed)) | Baja en Cast | Cada Cast desde 0 si hay refresh | Cast no hace refresh, recibe INIT cada vez sin reload de la página. No nos afecta directamente; relevante solo si en futuro hacemos versión navegador. |

---

## 7. Test plan post Fase A.1

### En Editor (antes de deploy):
- [ ] Compilación limpia, sin warnings de NullReferenceException
- [ ] Play mode → TvScene → simular INIT vía debug call (mockear TvAquariumState con 3 peces + 5 decos) → verificar peces spawneen, decos colocadas
- [ ] Verificar el `.data` del build < 80 MB (lectura conservadora)
- [ ] Build report (Editor.log post-build): ningún `ThirdParty/*.glb` en top 50 assets del .data

### En Xiaomi TV Box S:
- [ ] Cast desde móvil con 3 peces starter + 5 decos default
- [ ] Tiempo medido desde "Casting…" hasta primer frame del acuario: **<15s** en WiFi de casa
- [ ] Verificar visualmente: peces correctos, decos correctas, background, sustrato, lighting, ambient mode
- [ ] Tap "alimentar" en móvil → comida cae en TV
- [ ] Toggle "día → noche" en móvil → TV transiciona en 1-2s
- [ ] Tap "comprar pez" → confirmar IAP → móvil envía UPDATE → TV añade el nuevo pez (descarga 1 bundle ~15MB, aparece con fade-in)

### Red:
- [ ] Mide tiempo total descarga: `Chrome DevTools (remote) → Network tab` durante el test. Suma debe ser ≤ esperado (~100MB para starter).
- [ ] R2 caching headers: `curl -I https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/fish_X.bundle` debe devolver `Cache-Control: public, max-age=86400` o similar para que Xiaomi cachee.

---

## 8. Definición de "done"

Fase A.1 está done cuando:
- [ ] Branch `feat/netflix-architecture` mergeable a main (no breaking)
- [ ] `.data` ≤ 80 MB confirmado por filesystem
- [ ] Cast en Xiaomi: tiempo total <15s, acuario renderiza idéntico al móvil
- [ ] Documentado en `BUILD_REPORT_2026-05-26.md` (nuevo doc post-fix)
- [ ] Memory `cast_addressables_roadmap.md` actualizada con estado final

---

## 9. Notas para Sonnet implementador

### Reglas hard
- **NO TOCAR repo móvil** (`D:\dev\appquarium-unity\`). Cualquier cambio en SO o script de runtime queda confinado a `D:\dev\appquarium-tv-unity\`.
- **NO MERGEAR a main sin user confirmation** — branch `feat/netflix-architecture` queda en local hasta validación Xiaomi.
- **NO HACER PUSH** sin que el usuario lo pida explícitamente.
- **Si encuentras ambigüedad** entre el spec y el código existente, parar y preguntar — no inventar interpretaciones.

### Build hygiene (obligatorio)
- **Build de TV es lento** (1-3h). No hacer rebuild "para verificar". Pensar bien antes de `Build Player Content`.
- **CLEAR Addressables cache ANTES de cada `Build Player Content`** (§4.2.1). Sin esto, bug Unity puede dejar bundles con prefab=null en runtime.
- **CORS R2 verificar PRIMERO** (paso 0 del plan). Sin CORS, todo lo demás es trabajo perdido — bundles fallan a la primera carga.
- **Logs de build importantes** quedan en `Library/com.unity.addressables/BuildReports/buildlayout_*.json` y `C:/Users/Behere/AppData/Local/Unity/Editor/Editor.log`. Consultar antes de asumir nada.

### Decisiones arquitectónicas YA tomadas (no re-debatir)
- **Catálogos vacíos en scene + lazy load por key** vía Addressables. NO refactor a AssetReference.
- **NonRecursiveBuilding=false + Shared_Local desde día 1**. NO empezar sin Shared_Local "a ver si pasa".
- **Loading UX: logo + spinner + "X/Y cargados"** (§4.6). NO inventar otra UX.
- **Shader Variant Stripping = Strict + Crunch texture compression** (§4.4-4.5). NO dejar en defaults.

### Si algo se rompe
- **Si Shared_Local no captura todas las dups** → iterar tras `Check Duplicate Bundle Dependencies`. No considerar refactor a AssetReference sin antes haber probado mover 5-10 assets más a Shared_Local.
- **Si TV peta con NullReferenceException** al cargar un FishData → 99% es el bug Unity de cache stale (§4.2.1). Clean cache + rebuild.
- **Si CORS falla** → mirar el header `Origin` exacto que envía el receiver Chromium en DevTools, ajustar CORS R2 si necesario (debería ser `*`).
- **Si el `.data` sigue >100MB tras vaciar catálogos** → grep en TvScene.unity por OTRAS referencias a SOs / prefabs / GLBs. Puede haber un Manager, una UI, o un script con campo serializado que no he visto.

### Contexto del entorno
- **El receiver Cast está publicado** (App ID `8F6C873F`, Unlisted) y sirve desde R2 — solo hay que redeploy de bundles + .data. No tocar Cast Console.
- **El usuario quiere TV exactamente igual al móvil** — cualquier diferencia visual debe ser intencional y documentada en el spec.
- **Profile R2 AWS CLI** está configurado como `r2` apuntando a `https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com`. URL pública `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/`.
- **Xiaomi disponible** en LAN como `MiTV-AFMU0`. Cast SDK 3.72.446070.
- **WebGL ≠ otras plataformas**: bundles se descargan COMPLETOS antes de procesar (no streaming). Diseñar UX asumiendo cada bundle es atómico.

### Reporting al user
- Reportar progreso cada vez que termine un paso del plan (§5).
- Si encuentras algo inesperado en el código actual, parar y reportar antes de improvisar fix.
- Al terminar Fase A.1, escribir `BUILD_REPORT_2026-05-XX.md` con resultado final (tamaño .data, tiempo build, dups detectadas, etc.) — mismo formato que el report de 2026-05-25.

---

## 10. Glosario rápido

- **`.data`** — fichero binario del WebGL build que contiene scenes + scripts + assets. Sirve desde R2 raíz.
- **Bundle remoto** — AssetBundle servido desde R2 `/bundles/`. Loaded lazy via Addressables.
- **PackSeparately** — 1 bundle por asset addressable. Modelo Netflix.
- **NonRecursiveBuilding** — si true, dependencias del addressable NO se incluyen en el bundle (deben estar en otro grupo addressable o se quedan en .data).
- **R2** — Cloudflare object storage. URL pública: `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/`.
- **Cast SDK timeout** — 30s desde "Connecting" hasta receiver READY. Si excede, sesión Cast aborta.
- **Receiver publicado** — App ID `8F6C873F`, Unlisted, funciona en cualquier Chromecast/Android TV sin registrar el device.
