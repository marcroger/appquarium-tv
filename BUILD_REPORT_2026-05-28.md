# Build Report — Fase A.1 — 2026-05-28

**Builds completados esta sesión**
**Resultado clave: `.data` bajó de 411 MB → 26 MB (94% reducción) ✅**

---

## 1. Addressables Build (Build Player Content)

| Parámetro | Valor |
|---|---|
| Start (estimado) | 2026-05-27 noche |
| Duration | **17:50:58** (~18h — build frío desde caché anterior invalidada) |
| Output | `ServerData/WebGL/` → 93 bundles, **387.18 MB** |
| Errors | Ninguno |
| Unity | 6000.3.10f1 |
| Addressables | 3.0.0 |

### Configuración activa

| Setting | Valor |
|---|---|
| Compression (4 remote groups) | LZ4 ✅ |
| PackingMode | PackSeparately ✅ |
| NonRecursiveBuilding | **true** ✅ (NO cambiar a false — ver feedback_nonrecursivebuilding.md) |
| ContiguousBundles | true |
| BundleNaming | AppendHash |
| RemoteLoadPath | `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/` |

### Reparto por grupo

| Grupo | Bundles | Tamaño aprox |
|---|---:|---:|
| `Fish_Remote` | 25 | ~125 KB (SOs puros, ~4-5 KB c/u) |
| `Decos_Remote` | 54 | ~383 MB (prefabs + GLBs + texturas) |
| `Environments_Remote` | 11 | ~2.2 MB (~200 KB c/u) |
| `Audio_Remote` | 2 | ~5 MB |
| `Shared_Local` | 1 | ~56 KB |
| **Total** | **93** | **387 MB** |

### Duplicados detectados (Potential Issues)

24 assets duplicados en el build report → 25.22 MB de ahorro potencial si se de-duplican.
Top duplicados: texturas y meshes de `ThirdParty/HQ Rocks/`, `ThirdParty/Props/HallAnchor/`, `ThirdParty/Stylized Rock Pack/` — todos assets de decoraciones referenciados por múltiples `DecoData_*.asset` vía bundles `Decos_Remote`.

**Impacto:** solo afecta al tamaño de los bundles remotos (deco pack que el usuario descarga), NO al `.data` base. No es bloqueante para Cast. Fix pendiente: mover esos assets compartidos a `Shared_Local` tras validar en Xiaomi.

---

## 2. WebGL Player Build

| Archivo | Tamaño | Estado |
|---|---|---|
| `webgl-output.data` | **26 MB** | ✅ Era 411 MB — 94% reducción |
| `webgl-output.wasm` | 59.5 MB | ✅ Normal (runtime compilado) |
| `webgl-output.framework.js` | 0.46 MB | ✅ |
| `webgl-output.loader.js` | 0.03 MB | ✅ |

### Player Settings activos

| Setting | Valor |
|---|---|
| Compression Format | **Disabled** (cambiado de Brotli antes del build) ✅ |
| Exception Support | None ✅ |
| WebAssembly 2023 features | OFF ✅ |
| Decompression Fallback | OFF ✅ |
| Strip Engine Code | ON ✅ |
| Initial Memory Size | ~~256 MB~~ → **64 MB** (ver §9) ✅ |
| Maximum Memory Size | 512 MB ✅ |

> **Nota:** `webGLCompressionFormat` estaba en `2` (Brotli) al inicio de la sesión. Cambiado a `0` (Disabled) manualmente en Unity antes de lanzar el build. Asegurarse de que quede en Disabled para futuros builds.

### Por qué 26 MB (vs 411 MB anteriores)

El fix es el commit `4064e61` que ya vaciaba `allFishCatalog: []` y `allDecoCatalog: []` en `TvScene.unity`. Con las referencias directas a SOs eliminadas del scene, Unity ya no bakea el grafo de dependencias (GLBs/prefabs) en el `.data`. Los 26 MB son: scripts IL2CPP compilados + shaders URP + scaffolding de la scene + audio ambient + backgrounds/substrates de `Resources/`.

`allTankCatalog` sigue teniendo 4 entradas (`tank_l`, `tank_m`, `tank_nano`, `tank_ocean`) — correcto, son metadata pura (sin assets 3D) y `AquariumManager` los necesita para resolver `selectedTankId`.

---

## 3. Deploy a Cloudflare R2

**Completado 2026-05-28**

| Prefijo R2 | Archivos | Estado |
|---|---|---|
| `s3://appquarium-tv/bundles/` | 92 bundles | ✅ (catálogo `catalog.bin` se incluye en build base, no va en bundles/) |
| `s3://appquarium-tv/Build/` | data + wasm + js (×2) | ✅ |
| `s3://appquarium-tv/` | index.html, index_test.html | ✅ |

> **⚠ Bug encontrado en deploy:** El comando `aws s3 sync webgl-output/ s3://appquarium-tv/ --delete` borra todos los ficheros de `bundles/` porque ese prefijo existe en S3 pero no en `webgl-output/`. Ver §5 para los comandos corregidos.

> **⚠ SignatureDoesNotMatch intermitente con R2:** Algunos bundles (especialmente los más pequeños, ~2.5 KB) fallan con este error al subir. Fix: añadir `$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"` antes de los comandos AWS. El CLI v2 añade checksums extra que R2 rechaza en ciertos casos.

---

## 4. Estado R2 verificado

```
s3://appquarium-tv/Build/webgl-output.data          26 MB  ✅
s3://appquarium-tv/Build/webgl-output.wasm          59.5 MB ✅
s3://appquarium-tv/Build/webgl-output.framework.js  0.46 MB ✅
s3://appquarium-tv/Build/webgl-output.loader.js     0.03 MB ✅
s3://appquarium-tv/index.html                       9.7 KB  ✅
s3://appquarium-tv/bundles/  →  92 bundles          387 MB  ✅
```

---

## 5. Comandos deploy corregidos (usar en próximos deploys)

```powershell
# OBLIGATORIO antes de cualquier aws s3 sync/cp hacia R2
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"

# Limpiar bundles viejos (solo si hemos rebuildeado Addressables)
aws s3 rm s3://appquarium-tv/bundles/ --recursive `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com

# Subir bundles nuevos
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"

# Subir base WebGL — IMPORTANTE: --exclude "bundles/*" para no borrar los bundles
aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete `
  --exclude "bundles/*"
```

Si algún bundle falla con `SignatureDoesNotMatch` al hacer sync, subir individualmente:
```powershell
aws s3 cp "ServerData/WebGL/<nombre>.bundle" "s3://appquarium-tv/bundles/<nombre>.bundle" `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800" --no-progress
```

---

## 6. Cambios adicionales en esta sesión

### MCP Unity — puerto TV cambiado a 8091

- `ProjectSettings/McpUnitySettings.json`: `Port: 8090 → 8091`
- `.mcp.json` (nuevo en raíz del TV project): apunta al paquete MCP del TV project con `cwd` explícito
- **Por qué:** el proyecto mobile usa puerto 8090 por defecto. Con ambos Unity abiertos se peleaban.
- **Para activar:** reiniciar Unity TV → `Tools → MCP Unity → Server Window` debe mostrar "Port: 8091"

---

## 7. Próximo paso — Test Xiaomi TV Box S

**R2 listo. Solo falta la validación end-to-end.**

### Checklist de test (spec §7)

- [ ] Móvil con 3 peces starter (banggai_cardinalfish, boxfish_yellow, goby_firefish) + 5 decos básicas → Cast → seleccionar Xiaomi `MiTV-AFMU0`
- [ ] Tiempo desde "Casting…" hasta primer frame del acuario: **<15s** en WiFi de casa
- [ ] Loading overlay visible durante la carga (logo + spinner cyan + "X/Y cargados")
- [ ] Peces visibles y nadando (los 3 starter son de pack_content_free — deben cargar ✅)
- [ ] Decos colocadas correctamente
- [ ] Background, sustrato y lighting correctos
- [ ] Tap "alimentar" en móvil → comida cae en TV
- [ ] Toggle "día → noche" → transición suave

### Si algo va mal

- **Pantalla negra sin overlay:** revisar que `TvSceneBootstrap.Start()` se ejecuta y `CastReceiver` recibe el INIT
- **Peces no aparecen:** revisar Console en DevTools del Cast receiver (chrome://inspect → Remote targets)
- **NullReferenceException en fish:** `FishData.prefab` puede ser null con NonRecursiveBuilding=true — los 3 starter deberían funcionar pero los Pack24 pueden fallar (ver BUILD_REPORT_2026-05-25 §"Nota sobre fish prefabs")
- **Cast timeout (>30s):** comprobar que el .data en R2 es realmente 26 MB (no versión antigua en caché del browser)

---

## 9. Fix post-deploy — WebAssembly OOM en Cast (2026-05-28 tarde)

### Síntoma

Primera prueba en TV (Xiaomi TV Box S → Philips TV via HDMI) → pantalla negra + error rojo:

```
Unity load error:
abort(RangeError: WebAssembly.instantiate(): Out of memory: wasm memory)
```

### Root cause

`webGLInitialMemorySize: 256` en ProjectSettings bake **4096 páginas wasm (256 MB)** como mínimo en la sección `memory` del binario `.wasm`. Al llamar `WebAssembly.instantiate()`, el browser del Cast sandbox intenta reservar 256 MB de memoria lineal contigua — no tiene disponibles → OOM inmediato antes de que Unity siquiera arranque.

### Análisis

Inspeccionando el binario wasm (offset 194625 = sección `memory`):

```
01          # count: 1 memoria
01          # limits flag: min + max
80 20       # min = 4096 páginas = 256 MB  ← PROBLEMA
80 40       # max = 8192 páginas = 512 MB
```

El global section a continuación tiene el stack pointer inicial en **~10.5 MB** → static data + stack del runtime Unity ocupa 10.5 MB. Con 64 MB de initial hay 53 MB de margen antes de necesitar `memory.grow()`.

### Fix aplicado (sin rebuild)

Patch de 1 byte en `webgl-output.wasm` (offset 194628):

| Offset | Antes | Después | Efecto |
|---|---|---|---|
| 194628 | `0x20` | `0x08` | min pages: 4096 → 1024 (256 MB → 64 MB) |

Max (8192 páginas = 512 MB) sin cambiar — Unity puede crecer hasta 512 MB si lo necesita via `memory.grow()`.

```powershell
# Reproducir el patch manualmente si se necesita (tras un rebuild sin la config corregida)
$bytes = [System.IO.File]::ReadAllBytes("webgl-output\Build\webgl-output.wasm")
$bytes[194628] = 0x08   # 4096→1024 pages
[System.IO.File]::WriteAllBytes("webgl-output\Build\webgl-output.wasm", $bytes)
```

**NOTA:** Tras cada rebuild, verificar que el ProjectSettings ya tiene `webGLInitialMemorySize: 64` — si es así, el wasm saldrá del build con las 1024 páginas sin necesitar el patch manual.

### Cambios persistentes

| Archivo | Campo | Antes | Después |
|---|---|---|---|
| `ProjectSettings/ProjectSettings.asset` | `webGLInitialMemorySize` | `256` | `64` |
| `webgl-output/Build/webgl-output.wasm` | offset 194628 | `0x20` | `0x08` |
| R2 `Build/webgl-output.wasm` | — | 256MB min | 64MB min (re-subido con `--cache-control no-cache`) |

---

## 11. Rebuild WebGL player — en curso (2026-05-28 noche)

**Estado:** ✅ Build completado y deployado. Tests en Xiaomi en curso.

### Qué cambia respecto al build anterior

| Setting | Build anterior | Este build |
|---|---|---|
| `webGLInitialMemorySize` | 256 (parchado a 64 post-deploy) | **64** (bakeado desde origen) |
| `managedStrippingLevel` | `{}` (default ≈ Low) | **4 (High)** |
| `il2cppCodeGeneration` | `{}` (OptimizeSpeed) | **1 (OptimizeSize)** |
| HTML template | Sin splash screen | **Splash APPQUARIUM + error screens** |

### Estimación de resultado

| Archivo | Build anterior | Estimado |
|---|---|---|
| `webgl-output.wasm` | 59.5 MB | **~30-35 MB** |
| `webgl-output.data` | 26 MB | ~26 MB (sin cambios de assets) |
| Compilation RAM (Xiaomi) | ~120 MB → OOM | **~60 MB → OK** |

### Deploy tras build

```powershell
# NO hacer Build Player Content (bundles R2 quedan intactos)
# Solo subir el output del WebGL player:
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"

aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete `
  --exclude "bundles/*" `
  --cache-control "public, max-age=3600"
```

> **⚠ Importante `--cache-control "public, max-age=3600"`** para este primer deploy post-rebuild: 1h de caché permite que el Xiaomi cachee el nuevo `.wasm` compilado. En el segundo cast ya no recompila → aún más rápido.

### Resultados tests post-build

| Test | Resultado |
|---|---|
| OOM wasm instantiation | ✅ Resuelto |
| OOM wasm compilation | ✅ Resuelto (wasm 42.2 MB, compilación ~85 MB) |
| Splash APPQUARIUM | ✅ Aparece instantáneamente |
| Canvas pantalla completa | ✅ (fix CSS width:100vw/height:100vh) |
| Unity carga en Xiaomi | ✅ Blue camera background visible |
| Acuario visible | ❌ Pantalla azul Unity, sin contenido |
| Disconnect 2 min | ❌ Cast SDK timeout por inactividad |

**Diagnóstico en curso:** Unity carga y renderiza (blue camera background) pero acuario no aparece.

**Debug panel cyan** deployado en `index.html` — mostrará sobre la pantalla azul qué pasó con el chain INIT→Unity. Pendiente foto en próxima sesión (2026-05-29).

**Bug corregido en debug panel:** `hideSplash()` ocultaba el panel justo después de mostrarlo. Ahora `hideSplash()` no toca el panel — queda visible hasta el disconnect.

**Sospechas priorizadas:**
1. INIT message no llega a TvSceneBootstrap (JS SendMessage falla silenciosamente — el GameObject "CastReceiver" existe y tiene nombre correcto, pero algo en el bridge falla)
2. INIT llega pero Addressables.InitializeAsync no puede cargar catalog.bin (URL incorrecta en StreamingAssets, o catalog regenerado con hashes distintos)
3. INIT llega, Addressables cargan, pero AquariumManager.InitializeFromCastState falla silenciosamente

**Nota escena:** `FishInspectorUI` y `AquariumInputHandler` son "Missing Script" (huérfanos), inofensivos en runtime.

**Qué leer en el panel cyan cuando aparezca:**
- `Unity READY — pending: 0` → INIT llegó después de Unity (se mandó por SendMessage directo)
- `Unity READY — pending: 1` → INIT llegó antes de Unity (se mandó por buffer)
- `SM ok: INIT` → SendMessage ejecutado sin excepción JS
- Si no aparece `SM ok: INIT` → el mensaje nunca llegó al buffer ni al listener directo

---

## 10. Fix post-test — OOM wasm compilation (2026-05-28 noche)

### Síntoma

Tras el fix OOM de §9 (wasm con 64MB initial), segunda prueba en Xiaomi: debug panel mostró `Unity loading 52%` y luego el proceso fue matado por el OS (Android LMK) sin mensaje de error visible. Sin `Sender DISCONNECTED` en el log → muerte abrupta del proceso, no disconnect graceful del sender.

### Root cause

59.5 MB de wasm × ~2x para compilación JIT nativa = **~120 MB de código compilado** + 64 MB memoria lineal + overhead del browser Cast + Android OS ≈ >700 MB total en un device con poco margen. El OS mata el proceso antes de que Unity termine de arrancar.

### Fix

Dos cambios en `ProjectSettings.asset` que se aplican en el **próximo rebuild WebGL player** (no hace falta rebuild de Addressables):

| Setting | Antes | Después | Efecto |
|---|---|---|---|
| `managedStrippingLevel.WebGL` | `{}` (default/Low) | `4` (High) | Stripping agresivo de tipos C# no usados |
| `il2cppCodeGeneration.WebGL` | `{}` (OptimizeSpeed) | `1` (OptimizeSize) | IL2CPP genera código C++ más compacto |

**Estimación:** wasm 59.5 MB → ~30-35 MB (aprox 40-50% reducción). Compilation memory: ~60-70 MB. Total memory footprint del receiver: ~350 MB → cabe bien en 2 GB RAM del Xiaomi TV Box S.

### Qué hace falta

1. Abrir Unity TV
2. `File → Build Settings → Build` (output: `webgl-output/`) — **NO** "Build Player Content" (los bundles quedan intactos en R2)
3. Tiempo esperado: **15-45 min** con caché caliente
4. Tras build: desplegar solo `webgl-output/` (sin `--exclude bundles/*` para el index.html)

### Cambio adicional: splash screen inmediata

`Assets/WebGLTemplates/CastReceiver/index.html` actualizado para:
- Mostrar pantalla de marca APPQUARIUM inmediatamente al conectar Cast
- Unity carga en background mientras la splash mantiene la sesión Cast viva
- Barra de progreso en la splash (`Cargando… 52%`)
- Transición fadeout splash → Unity cuando está listo

---

## 8. Deuda técnica registrada (Fase B)

| Item | Impacto | Cuándo |
|---|---|---|
| Duplicados Shared_Local (24 assets, 25 MB) | Bundles decos ~5% más grandes | Post-validación Xiaomi |
| Fish prefabs Pack24 no en Addressables | 22 peces no renderizan en TV | Fase B |
| `Resources/Backgrounds/` (25 MB) en .data | Base +25 MB sin necesidad | Fase B |
| `Resources/Substrates/` (32 MB) en .data | Base +32 MB sin necesidad | Fase B |
| webGLCompressionFormat puede volver a Brotli | Build roto | Verificar cada build |
