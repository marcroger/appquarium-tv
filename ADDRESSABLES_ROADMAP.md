# Cast Receiver — Unity Addressables (Fase A en curso)

**Actualizado:** 2026-05-25 — diagnóstico corregido + fix de duplicados aplicado.

## Objetivo

Reemplazar el receiver WebGL monolítico (~293MB `.data`) por una base ligera (~30-50MB) + bundles remotos cargados bajo demanda desde Cloudflare R2. Modelo Netflix: el usuario descarga **solo los assets activos en su tanque** (peces, decos, fondo, sustrato), no la librería completa. Escala a 2GB+ de contenido sin penalizar al usuario.

```
TV arranca → carga base WebGL (~50MB, sin .gz) → ready
  ▼ Cast SDK INIT
TvSceneBootstrap.LoadAndInitializeCoroutine(state)
  ├── Addressables.LoadAssetAsync<FishData>("fish_mandarinfish")  → fetch fish_mandarinfish.bundle (~5MB)
  ├── Addressables.LoadAssetAsync<FishData>("fish_boxfish_yellow") → fetch fish_boxfish_yellow.bundle
  ├── Addressables.LoadAssetAsync<DecorationData>("deco_anchor")   → fetch deco_anchor.bundle
  └── yield return (parallel downloads)
       ▼
AquariumManager.InitializeFromCastState(state, fishData, decoData)
  → acuario en pantalla
```

---

## Camino recorrido

### Intento 1 — 2026-05-23 (monolítico)

1. Build WebGL monolítico con texturas 512px → `data.gz` de 415MB → timeout Cast SDK en Xiaomi
2. Reducción a 256px → `data.gz` de 293MB → subido a R2
3. Test Xiaomi → `SyntaxError: Invalid or unexpected token`

**Causa**: Cloudflare R2 sirve `.gz` con `Content-Encoding: gzip`. Browser pre-descomprime, Unity loader también descomprime (URL termina en `.gz`) → doble descompresión → SyntaxError.

### Intento 2 — 2026-05-23 noche (Addressables, primera vuelta)

- Package `com.unity.addressables@3.0.0` instalado
- `★ Setup Addressables` ejecutado:
  - 25 FishData SOs → `Fish_Remote`
  - 54 DecorationData SOs → `Decos_Remote`
  - 11 backgrounds + 12 substrate textures → `Environments_Remote`
  - 2 audio clips → `Audio_Remote`
- Player Settings → `Compression Format = Disabled`
- Bundle mode: PackSeparately
- Build Player Content → Unity entra en bucle y no termina tras 6h. Cancelado.

**Diagnóstico erróneo registrado en su momento**: "OOM crash".

### Diagnóstico corregido — 2026-05-25

Revisión de `Logs/AssetImportWorker*-prev.log`:
- Workers 0 y 2: limpios.
- Workers 1 y 3: **289 + 289 = 578 fallos de aserción** `Assertion failed on expression: 'pred(*previous, *i)'`.
- Máquina tiene **31 GB RAM** — no era OOM, era un bucle de sort.

La aserción viene de `Runtime/Utilities/remove_duplicates.h:77` (utilidad de dedup interna). Se dispara cuando el sort recibe entradas con la misma key pero diferentes contenidos.

**Auditoría de `m_Address:` en los 4 grupos** confirmó: **12 colisiones**, todas substrate (`sub_*`):
- `Decos_Remote`: DecorationData SO con address `sub_sand` (GUID `bd0e9fe5...`)
- `Environments_Remote`: textura PNG con address `sub_sand` (GUID `32ce50b9...`)
- Repetido para los 12 substrates: `sub_coral_rubble`, `sub_gold`, `sub_gravel`, `sub_ice`, `sub_lava`, `sub_moss`, `sub_mud`, `sub_pebbles`, `sub_sand`, `sub_slate`, `sub_volcanic`, `sub_white`.

Origen del bug: el script `★ Setup Addressables` añadía las 12 texturas de `Resources/Substrates/` a `Environments_Remote` con `Path.GetFileNameWithoutExtension(path)` como address — coincidía exactamente con el `itemId` de los `DecorationData` SOs ya presentes en `Decos_Remote`.

**Fish_Remote y los `bg_*` de Environments_Remote NO tenían duplicados.**

---

## Fix aplicado — 2026-05-25

En `Assets/Editor/TvAddressablesSetup.cs`:

| Cambio | Por qué |
|---|---|
| `★ Setup Addressables` ya no añade `Resources/Substrates` | Los SOs de substrate ya están en `Decos_Remote` (mismo itemId). Las texturas están referenciadas por los SOs (en runtime se cargan vía `Resources.Load` mientras dure la Fase A). |
| `★ Clean Substrate Duplicates` (NEW) | Elimina los 12 entries colisionados que quedaron de la vuelta anterior. |
| `★ Set Bundle Mode (PackSeparately)` (NEW) | Revierte el `PackTogether` aplicado entre medias. Modelo Netflix: 1 bundle por asset. |
| `★ Fix Bundle Mode (PackTogether)` | Se mantiene como fallback emergencia si PackSeparately falla por otra causa. |

Adicional — limpieza de scripts (no Addressables, pero alineado):
- **Borrado** `Assets/Scripts/Core/CastManager.cs` (270 LOC) — era el sender móvil duplicado por error en el refactor del 22-may. Tenía `[RuntimeInitializeOnLoadMethod]` creando un GameObject inútil en el TV WebGL.
- **Creado** `Assets/Scripts/Core/CastDataTypes.cs` con las 5 clases de datos compartidas (`TvAquariumState`, `CastMessage`, `TvUpdateMessage`, `TvFishEntry`, `DecoPlacementList`) que sí usan `CastReceiver`, `TvSceneBootstrap` y `AquariumManager`.

---

## Plan de ejecución actual

### Paso 1 — Cleanup + setup (en Unity, ~10 seg cada uno)

```
Appquarium TV → ★ Clean Substrate Duplicates       → "Removed 12 substrate texture duplicates"
Appquarium TV → ★ Set Bundle Mode (PackSeparately) → "All remote groups set to PackSeparately"
Appquarium TV → ★ Print Addressables Summary       → "Total: 92" (25 + 54 + 11 + 2)
```

### Paso 2 — Build Player Content

```
Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script
```

**Esperado con PackSeparately + fix de duplicados**: build limpio en 1-3h. 92 bundles en `ServerData/WebGL/`. Puede tardar más por overhead per-bundle pero ya no debe loopear en assertion.

**Si peta otra vez**: el log limpio nos dará la causa real (no estará enmascarado por el bucle de assertion).

### Paso 3 — Build WebGL base

```
File → Build Settings → Build → webgl-output/
```

Compression = Disabled (ya configurado). Esperado ~30-50MB sin `.gz`.

### Paso 4 — Deploy a R2

```powershell
cd D:\dev\appquarium-tv-unity

# Bundles (catálogo + 92 .bundle files)
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com

# Base WebGL (--delete limpia restos del build viejo de 307MB)
aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete
```

### Paso 5 — Test Xiaomi TV Box S

Móvil → FAB → Cast → seleccionar Xiaomi. TV debe:
1. Cargar base ligera en <10s (vs timeout 30s anterior)
2. Recibir INIT y empezar a fetch los bundles de los assets activos del tanque del usuario
3. Renderizar el acuario conforme van llegando los bundles (lazy, ordenado por importancia: fish primero)

---

## Fase B — Streaming real (pendiente, siguiente sesión)

**Problema actual**: `Resources/Backgrounds/` (25MB) y `Resources/Substrates/` (32MB) **siguen siendo auto-incluidos en la base WebGL** porque cualquier cosa dentro de `Resources/` se bakea en el `.data`. Eso bloata la base con ~57MB de contenido que el usuario quizás no use, y no escala cuando lleguen 4K backgrounds o nuevos packs.

**Acciones Fase B**:
1. Mover `Assets/Resources/Backgrounds/` → `Assets/Bundles/Backgrounds/`
2. Mover `Assets/Resources/Substrates/` → `Assets/Bundles/Substrates/`
3. Crear `BackgroundData` ScriptableObject con campo `Texture2D backgroundTexture` (1 SO por bg, 11 totales)
4. Añadir campo `Texture2D substrateTexture` a `DecorationData` (asignar a los 12 sub_* SOs)
5. Actualizar `TankBackground.SetPreset` y `DecorationPlacer.SetSubstrate` para usar `Addressables.LoadAssetAsync<Texture2D>` en vez de `Resources.Load`
6. Re-Setup Addressables (los nuevos SOs entran en `Environments_Remote`)
7. Rebuild + redeploy

**Resultado esperado**: base WebGL ~30MB. Backgrounds y substrates se descargan SOLO el seleccionado por el usuario (~3MB cada uno).

---

## Iteración 2 — Texturas por categoría (post-validación Fase A)

Estado actual: `TvBuildTools.cs` aplica `maxTextureSize = 1024` a TODO (`ThirdParty` + `Resources`). En móvil es heterogéneo: `Resources/Backgrounds` y `Resources/Substrates` están en 2048, `Pack 24` y decos en 1024, portraits en 512.

Estrategia TV propuesta:
| Categoría | maxSize | Justificación |
|---|---|---|
| `ThirdParty/Mikhail Nesterov` (fish) | 2048 | Hero objects con cámara cercana, justifica subir vs móvil |
| `Resources/Backgrounds` | 2048 | Fullscreen |
| `Resources/Substrates` | 2048 | Suelo tileado visible |
| Resto de `ThirdParty` (decos) | 1024 | Props secundarios |
| Mipmaps en todo | OFF | Cámara a distancia fija, sin LOD útil |

Implementación: nuevo menu item `★ Apply TV Texture Strategy` que aplique tamaños por carpeta + formato `Automatic` para WebGL (Unity escogerá DXT/ETC2/etc según el browser).

---

## Cómo está organizado el código de Addressables

| Archivo | Función |
|---|---|
| `Assets/Editor/TvAddressablesSetup.cs` | Menu items de setup, clean, switch bundle mode, print summary |
| `Assets/Editor/TvBuildTools.cs` | Menu items de reducir/restaurar texturas (uniforme — Fase A) |
| `Assets/AddressableAssetsData/` | Configuración persistente (settings + 4 grupos) |
| `Assets/Scripts/Core/TvSceneBootstrap.cs` | Runtime: parsea INIT, `Addressables.LoadAssetAsync` en paralelo, inicializa |
| `Assets/Scripts/Core/AquariumManager.cs` | Slim TV version: catálogos vacíos al start, se rellenan con lo que llega vía Bootstrap |

---

## Cuándo re-buildear bundles vs base WebGL

| Cambio | Qué rebuild |
|---|---|
| Solo lógica C# (CastReceiver, TvSceneBootstrap, AquariumManager) | Solo WebGL base. Bundles intactos. |
| Stats/precio de un FishData (sin cambiar prefab) | Solo Fish_Remote (rebuild incremental con Update Build → Update a Previous Build) |
| Nuevo pez | Re-run `★ Setup Addressables`, luego rebuild Fish_Remote |
| Nueva deco | Re-run setup, rebuild Decos_Remote |
| Nuevo background/sustrato | Re-run setup, rebuild Environments_Remote |
| Strategy de texturas (iteración 2) | Reimport completo + rebuild de TODOS los bundles (~1-2h) |
