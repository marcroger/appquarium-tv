# Cast Receiver — Unity Addressables

> ⚠ **Contenido dado por pendiente que ya está hecho.** «PRÓXIMO PASO: los 23 peces
> restantes» y «stubs de 2,5 KB sin buildear» son de mayo: **los 25 peces están buildeados y
> en R2 desde el 2026-06-08**, y sus bundles pesan ~1,5 MB. «Las decos nunca se han validado
> visualmente en TV» tampoco vale: validadas el 2026-08-15 contando píxeles.
> La tabla «Estado actual — 2026-05-28» está desfasada (`.data` real: 16,9 MB, no 26 MB).

**Actualizado:** 2026-06-02

> **⭐ PRÓXIMO PASO:** Peces restantes (23) + validar decos en TV → aplicar FishUnlit a cada pez (cambiar .mat + ★ New Build + deploy). Ver §"Workflow por pez" abajo.
>
> **Estado actual (2026-06-08):**
> - ✅ Unity carga (~22s, sin OOM)
> - ✅ Background, WaterSurface, substrate: correctos (Sprites/Default)
> - ✅ **Banggai Cardinalfish: CONFIRMADO EN PANTALLA en Xiaomi vía Cast** (2026-06-08) — cuerpo opaco, rayas negras, aletas transparentes. Shader CG legacy `Appquarium/FishUnlit`.
> - ✅ Moorish Idol: bundle deployado con FishUnlit — pendiente validar visualmente en TV
> - ❌ 23 peces restantes: stubs 2.5 KB — no buildeados aún
> - ❓ Decos en TV: bundles deployados desde 2026-05-28 — nunca validadas visualmente (riesgo: URP Lit stripping, ver nota abajo)
> - 🐛 Bug doble-slash catálogo: workaround activo, fix pendiente en `TvAddressablesSetup.cs`
>
> **Ver:** [`BUILD_REPORT_2026-06-02.md`](BUILD_REPORT_2026-06-02.md) — diagnóstico completo de bugs de rendering WebGL.  
> **Reports anteriores:** [`BUILD_REPORT_2026-05-28.md`](BUILD_REPORT_2026-05-28.md) | [`BUILD_REPORT_2026-05-25.md`](BUILD_REPORT_2026-05-25.md)
> - ✅ Compresión correcta: `m_Compression: 1` = LZ4 en Addressables 3.0 (no LZMA — diagnóstico anterior incorrecto)
> - ✅ FishData.prefab asignado en 25 SOs (listos para buildear)
>
> **Ver:** [`BUILD_REPORT_2026-05-30.md`](BUILD_REPORT_2026-05-30.md) para diagnóstico completo.
>
> **Reports:** [`BUILD_REPORT_2026-05-28.md`](BUILD_REPORT_2026-05-28.md) | [`BUILD_REPORT_2026-05-25.md`](BUILD_REPORT_2026-05-25.md)

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

## Lecciones de build (dureza aprendida)

### Build de 9.5h — 2026-05-25/26

El build de Fase A tardó **9.5 horas** para 92 bundles con PackSeparately. Causa raíz confirmada por investigación:

#### ~~Culpable 1 — LZMA~~ ← DIAGNÓSTICO INCORRECTO (verificado 2026-05-30)

`m_Compression: 1` en Addressables 3.0 enum = **LZ4** (0=Uncompressed, 1=LZ4, 2=LZMA). Los grupos ya tenían LZ4 desde el principio. El build de 9.5h fue causado íntegramente por la recompresión de texturas en caché fría (Culpable 2). **No hay que cambiar nada de compresión.**

#### Culpable 2 — Recompresión de texturas en cada build

Addressables recomprime las texturas aunque ya estén importadas con el formato correcto. Con ~200 texturas a 1024px en WebGL (formato Automatic → ETC2/DXT), son ~200 operaciones de compresión adicionales sobre el overhead per-bundle.

**No hay fix fácil para esto** — es comportamiento interno de Unity. La única mitigación es usar texturas más pequeñas durante builds de prueba (ya lo hacemos con `★ Reduce TV Textures`).

#### Si el próximo build vuelve a tardar horas

Posible caché corrupta. Borrar y reconstruir:
```powershell
Remove-Item -Recurse -Force "Library\com.unity.addressables"
# Luego Build → New Build → Default Build Script
```

#### Tiempo esperado — builds con caché fría

El cuello de botella REAL es la compresión de texturas para WebGL (DXT/ETC2 en CPU, ~45s/textura). No hay doble compresión porque la compresión ya era LZ4 desde el principio.

- **1 pez (banggai) en frío**: ~10-15 min (texturas pequeñas del banggai)
- **25 peces en frío a 1024px**: ~8-16h (95 texturas × 45s + FBX + animaciones)
- **25 peces en frío a 512px** (con `★ Reduce TV Textures`): ~2-4h ← **recomendado**
- **Builds siguientes** (SBP cache caliente): solo los assets cambiados → minutos

---

## Sesión 2026-05-26 — Intentos, errores y estado actual

### Contexto de entrada

El build overnight correcto (16h frío, NonRecursiveBuilding=true) había producido:
- 92 bundles en `ServerData/WebGL/` (389MB) ✅
- `webgl-output.data` = 411MB ⛔ (causa: scene tenía 84 refs directas a SOs → glbs bakeados en .data)

Commit 4064e61 ya había vaciado `allFishCatalog[]` y `allDecoCatalog[]` en TvScene.unity. El .data gordo era el bloqueante para Cast en Xiaomi (timeout 30s).

### Intento 3 — NonRecursiveBuilding=false + Shared_Local (ERROR — ~5h perdidas)

**Qué se hizo:**
1. Creado grupo `Shared_Local` (PackTogether, local) con 7 assets duplicados: WoodChest.mat, URPGlobalSettings.asset, rock_lod0-4.fbx
2. Cambiado `NonRecursiveBuilding: 1 → 0` en `AddressableAssetSettings.asset`
3. Lanzado Build Player Content × 2 (con cancelaciones)

**Por qué fue un error:**
`NonRecursiveBuilding=false` hace que cada bundle embeba **todas sus dependencias transitivas**. Un bundle de deco que con NonRecursiveBuilding=true era 15-25MB (deco prefab + GLB propio) pasa a ser 100MB+ (incluye WoodChest.mat × 21, rock LODs × 6, shaders URP completos). Cada bundle tardó **~47-48 minutos** en `WriteSerializedFiles`. Con 54 decos = ~42 horas estimadas. Cancelado.

El BUILD_REPORT §3 dice explícitamente que el tiempo del build anterior fue por **shader compilation en frío + reimport de texturas** — NO por NonRecursiveBuilding. Con caché caliente ya sería 1-3h con NonRecursiveBuilding=true. Se cambió innecesariamente.

**Qué quedó del intento:**
- ServerData/WebGL: vacío (nada producido)
- SBP cache de la build overnight: intacto en `Library/com.unity.addressables/` (buildlayout.json 690KB, addressables_content_state.bin 61KB — de las 9:11 del build correcto)
- Shared_Local group: se **mantiene** con los 7 entries (correcto, reduce duplicación)
- NonRecursiveBuilding: **revertido a 1 (true)**

### Estado actual — 2026-05-28

| Item | Valor | Estado |
|---|---|---|
| NonRecursiveBuilding | **1 (true)** | ✅ correcto |
| Shared_Local | 7 assets (WoodChest, URPGlobalSettings, rock_lod0-4) | ✅ |
| TvScene.unity allFishCatalog/allDecoCatalog | **[] (vacíos)** | ✅ |
| TvScene.unity allTankCatalog | 4 TankData SOs | ✅ (metadata pura, no GLBs) |
| ServerData/WebGL | **93 bundles, 387 MB** | ✅ |
| webgl-output.data | **26 MB** | ✅ era 411 MB |
| R2 bundles/ | **92 bundles** | ✅ deployado |
| R2 Build/ | data + wasm + js | ✅ deployado |
| Test Xiaomi | **pendiente** | ⏳ |

### Test Xiaomi — pasos para la tarde

### Nota sobre fish prefabs en Addressables

Los 25 fish de Pack 24 (Mikhail Nesterov) tienen `FishData.prefab` apuntando a `Assets/ThirdParty/Mikhail Nesterov/` que NO está en ningún grupo Addressables. Con NonRecursiveBuilding=true y scene refs vacías:
- Fish bundles = 2.5KB (solo SO)
- En runtime: `FishData.prefab` puede ser null → fish de Pack 24 no renderizarán
- **Para el test inicial en Xiaomi esto es aceptable** — los 3 peces starter (banggai_cardinalfish, boxfish_yellow, goby_firefish) son del `pack_content_free` bundle y sí cargan
- Para Fase B: añadir prefabs Pack 24 al grupo `Fish_Remote` como entries adicionales

---

## Test Xiaomi — sesión de la tarde (R2 deployado ✅)

**No hay que buildear nada.** Todo está en R2. Solo hay que probar.

Móvil → FAB → Cast → seleccionar Xiaomi (`MiTV-AFMU0`).

**Secuencia esperada:**
1. TV carga base WebGL (26 MB) → ready en <10s
2. Loading overlay aparece (logo + spinner cyan + "0/N cargados")
3. TvSceneBootstrap recibe INIT → descarga bundles activos en paralelo
4. Contador actualiza: "1/N cargados", "2/N cargados"...
5. Acuario visible, overlay hace fade-out

**Criterios de aceptación:**
- [ ] Tiempo total "Casting…" → primer frame: **<15s** en WiFi de casa
- [ ] Loading overlay visible durante la carga
- [ ] 3 peces starter (banggai_cardinalfish, boxfish_yellow, goby_firefish) nadando
- [ ] Decos colocadas correctamente
- [ ] Background, sustrato y lighting correctos
- [ ] Tap "alimentar" → comida cae
- [ ] Toggle día/noche → transición suave

**Si algo falla:**
- **~~WebAssembly OOM (pantalla roja "Out of memory: wasm memory")~~ RESUELTO** — Fix aplicado 2026-05-28: wasm parchado de 256MB a 64MB initial, R2 re-subido. Si vuelve a aparecer tras un rebuild: verificar `webGLInitialMemorySize: 64` en ProjectSettings o aplicar patch manual (ver BUILD_REPORT_2026-05-28 §9).
- **Timeout Cast (>30s):** `curl -I https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/Build/webgl-output.data` — verificar Content-Length ~27 MB
- **Pantalla negra sin error:** abrir DevTools remotas → `chrome://inspect` en Chrome del PC → buscar el receiver Cast
- **Peces no aparecen (null prefab):** los Pack24 son deuda técnica conocida (ver §"Nota sobre fish prefabs"). Los 3 starter SÍ deben cargar
- **NullReferenceException en bundles:** bundles fresh, no debería pasar; si ocurre revisar Console Unity TV

**Si Cast funciona en Xiaomi:** ✅ Fase A.1 done. Proceder a Fase B (Backgrounds/Substrates fuera de Resources).
**Si Cast falla por timeout:** .data puede haber quedado en caché de R2 anterior. Limpiar caché Cloudflare si aplica.

---

---

## Workflow por pez — aplicar FishUnlit (23 restantes)

Cada pez del Global Reef Fish Pack necesita el fix de materiales descubierto con Banggai. El proceso incremental con SBP cache hace que el coste sea ~30-60s por pez (no 2h — solo el primer build de cada pez es frío).

**Regla confirmada (ver `BUILD_REPORT_2026-06-02.md §Bug4`):**
- Body `.mat` → shader `Appquarium/FishUnlit` (GUID `60c4ee7717958bf408b5b7f628166d09`) — `Cull Off`, `ZWrite On`, sin clip
- Fins `.mat` (si existe separado) → shader `Sprites/Default` (fileID 10753)
- Peces de single-material → FishUnlit únicamente
- **NO** player rebuild — FishUnlit ya está compilado y bakeado en el player

**Por qué Cull Off es obligatorio:** las normales del Global Reef Fish Pack están invertidas. Con `Cull Back` el body es invisible (Unity elimina todas las caras). Este problema es de TODO el pack, no solo del Banggai.

**Coste por pez:**
| Situación | Tiempo |
|---|---|
| Primer pez nuevo (SBP cache frío de ese pez) | ~30-60 min |
| Cambio de .mat en pez ya buildeado | ~30s |
| Todos los peces en caché caliente | ~9s (solo catalog) |

**No hace falta buildear todos a la vez.** Se puede ir pez a pez: cambiar .mat → ★ New Build → deploy ese bundle → el resto siguen como están.

---

## Decos en TV — estado y riesgo

Los 54 bundles de decos están deployados en R2 desde 2026-05-28 (92 bundles totales). **Nunca se han validado visualmente en TV.**

**Riesgo:** los prefabs de deco usan materiales con `Universal Render Pipeline/Lit` que se stripea con `Managed Stripping Level: High` (mismo bug que los peces). Si las decos aparecen **magenta** en TV = mismo problema que el Banggai antes del fix.

**Diferencia con peces:** las decos NO tienen normales invertidas (GLBs normales), así que NO necesitan `Cull Off`. Si el material es el problema, el fix es más simple: cambiar a `Sprites/Default` o crear variante de `FishUnlit` sin `Cull Off` para decos.

**Próximo paso:** castear cualquier tanque con una deco y ver si aparece. Si magenta → fix de shader. Si bien → decos OK sin cambios.

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

**Regla de oro: siempre usar `New Build → Default Build Script`. El SBP cache hace que sea efectivamente incremental — solo reconstruye lo que cambió.**

> ❌ **`Update a Previous Build` — NO usar** para nuestra arquitectura R2.  
> Es una feature para workflows CCD (Unity Cloud). Para hosting propio (R2) no funciona correctamente cuando hay nuevas dependencias (prefabs, assets 3D). Causa builds fantasma que no producen output.

| Cambio | Herramienta | Tiempo estimado |
|---|---|---|
| Solo lógica C# | Solo rebuild WebGL base (File → Build Settings → Build). Bundles intactos. | 15-30 min |
| Stat/precio de un FishData SO | `New Build` | Segundos (SBP cache, solo el SO cambió) |
| Textura de un pez cambiada | `New Build` | ~1-2 min (solo esa textura se recomprime) |
| Nuevo pez (pez 11 con 10 ya construidos) | `★ Setup Addressables` → `New Build` | ~10-15 min (solo el nuevo en frío, los 10 usan SBP cache) |
| Nueva deco | `★ Setup Addressables` → `New Build` | ~10-15 min (solo la nueva) |
| Nuevo background/sustrato | `★ Setup Addressables` → `New Build` | ~2-5 min |
| Primer build completo (25 peces, 512px) | `★ Reduce TV Textures` → `New Build` | ~2-4h (todos en caché fría — one-time) |
| Rebuild total tras `Remove Library/` | `New Build` | = primer build completo |
