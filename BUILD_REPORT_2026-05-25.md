# Build Report — Fase A — 2026-05-25/26

**Build:** Default Build Script (Addressables 3.0.0)
**Start:** 2026-05-25 10:41:24
**End:** 2026-05-26 03:35
**Duration:** **60.866 s = 16,91 h**
**BuildError:** (vacío — sin errores)
**Output:** `ServerData/WebGL/` → 92 bundles, 389 MB

Reporte completo: `Library/com.unity.addressables/BuildReports/buildlayout_2026.05.25.10.41.24.json`

---

## 1. Configuración usada

| Setting | Valor |
|---|---|
| Compression (los 4 remote) | **LZ4** (interno: LZ4HC) ✅ |
| PackingMode | **PackSeparately** en los 4 remote ✅ |
| NonRecursiveBuilding | True |
| ContiguousBundles | True |
| BundleNaming | AppendHash |
| InternalBundleIdMode | GroupGuidProjectIdHash |
| UseAssetBundleCache | True |
| UseAssetBundleCrc | True |
| RemoteLoadPath | `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/bundles/` |

**La memoria `cast-build-logging` estaba desactualizada** — el fix LZ4 ya estaba aplicado en los 4 grupos antes de este build. NO fue causa de las 17h.

---

## 2. Reparto por grupo

| Grupo | Bundles | Assets | Tamaño |
|---|---:|---:|---:|
| `Fish_Remote` | 25 | 25 | **0,1 MB** (solo SOs, ~2,5 KB c/u) |
| `Decos_Remote` | 54 | 54 | **381,2 MB** ⚠ el bulk |
| `Environments_Remote` | 11 | 11 | 2 MB |
| `Audio_Remote` | 2 | 2 | 5,3 MB |
| **Total** | **92** | **92** | **389 MB** |

### Bundles más pesados (Decos)
- `deco_column_greek_1`: **25,8 MB** (mesh nativo en metros)
- `deco_column_greek_3`: 17,9 MB
- `deco_column_greek_2`: 15,8 MB
- `deco_column_greek_5`: 15,3 MB
- `deco_column_greek_4`: 15,9 MB
- `deco_coral_meandrina`: 15,2 MB
- 8 corales: 13-15 MB cada uno (108 MB total)
- `deco_shell_helmet`: 11,5 MB

### Bundles fish (anomalía)
Cada uno **2-3 KB**: solo contiene el `FishData` SO. El `prefab` field referencia el visual del Pack 24 que está en `Assets/ThirdParty/Mikhail Nesterov/` **fuera de Addressables** ⇒ se baka en la base WebGL.

Para Fase A esto funciona (los 25 prefabs Pack 24 viven en la base). Para Fase B habrá que decidir si los prefabs entran a `Fish_Remote` o se mantienen como base.

---

## 3. Por qué 16,9h — análisis de causas

El TEP (`AddressablesBuildTEP.json`) registra **solo 1,5 s de eventos hijos** bajo "Building Default Build Script" (60.869 s totales). El resto del tiempo se fue en operaciones internas de Unity que no se trazan.

Causas estimadas, en orden de peso:

### a) Shader compilation en frío (60-80% del tiempo)
Cuatro `shadercompiler-AssetImportWorker*.log` activos durante el build. URP en WebGL compila **variantes por shader × por plataforma**:
- ~30 shaders URP (Lit, Unlit, Particles, Sprite, etc.)
- Cada uno con ~40 keyword variants (lighting, shadows, GPU instancing, etc.)
- Para WebGL target específicamente (no se reutiliza el cache de Android/Editor)

Caché en `Library/ShaderCache/`. **Próximo build en caliente: estos pasos serán prácticamente 0 s.**

### b) Asset reimport para WebGL (15-25%)
~200 texturas en ThirdParty + Resources se recomprimieron al formato WebGL (DXT/ETC2 según fallback). Aunque `ContiguousBundles=true` lo optimiza, sigue siendo costoso por cantidad.

Caché en `Library/Artifacts/` + `Library/com.unity.addressables/aa/`. **Próximo build en caliente: ~5 min.**

### c) PackSeparately overhead (5-10%)
92 bundles separados ⇒ 92 análisis de dependencias independientes. Es overhead linear, no exponencial — esto es lo único que NO mejora con cache caliente.

### d) NO fue compresión
LZ4 ya estaba aplicado. Si hubiera sido LZMA, hubiera sumado otras 4-6h.

### Estimación próximo build (cache caliente, sin cambios)
**1-3 h** (vs 16,9h primer build). Confirmaremos en el siguiente ciclo.

### Estimación con fix de duplicación (Shared_Local — ver §5)
**45-90 min** — al sacar los shared deps de los 92 bundles, cada análisis individual procesa menos.

---

## 4. Duplicación de assets — 584 inclusiones de 29 assets distintos

PackSeparately garantiza bundles self-contained (cada uno puede cargarse independientemente). El precio es duplicar dependencias compartidas en cada bundle que las use.

**Estimación de bloat por duplicación: ~60-80 MB** sobre los 389 MB totales (~15-20%).

### Top 4 worst (21 bundles cada uno) — clasificados por contexto

| GUID | Asset | Aparece en (21 decos) |
|---|---|---|
| `933532...d08ed7` | `WoodChest.mat` (Pack PBR Chest) | Set A: anchors (×3), cannon, cliffs (×4), rocks_hq (×3), rocks_plate (×3), rocks_stylized (×6), toy_chest |
| `e6e9a1...ef7dbd` | `UniversalRenderPipelineGlobalSettings.asset` | Set A (mismo) |
| `36e335...3188ae` | (built-in URP, GUID no resuelto en Assets) | Set A (mismo) |
| `99fa99...66d261d` | (built-in URP, GUID no resuelto en Assets) | Set B: columnas griegas (×5), corales (×8), shells (×3), starfish, statues (×4) |

Los GUIDs 933532, e6e9a1, 36e335 caen siempre juntos en exactamente los mismos 21 decos ⇒ son una **cadena de dependencias URP que esos 21 decos comparten**. El cuarto (99fa99) cae en los OTROS 21 ⇒ otra cadena URP para el segundo set de decos.

### Segundo nivel (6 bundles cada uno)
- `rock_lod0.fbx`, `rock_lod1.fbx`, `rock_lod2.fbx`, `rock_lod3.fbx`, `rock_lod4.fbx` (Stylized Rock Pack) — los 6 stylized rocks comparten los 5 LOD meshes

### Tercer nivel (3-4 bundles cada uno)
20+ assets más pequeños — texturas comunes, shaders específicos, materiales auxiliares.

---

## 5. Fix propuesto (Fase A.1 — opcional, no bloquea Cast)

### Estrategia: grupo `Shared_Local` con PackTogether

Crear un nuevo grupo Addressables:
- **Nombre:** `Shared_Local`
- **PackingMode:** PackTogether (1 solo bundle)
- **Path:** Local (`StreamingAssets`) — descargado UNA VEZ con la base WebGL
- **Compression:** LZ4

Contenido (a definir tras `Analyze → Find Duplicate Dependencies` en Unity):
- `WoodChest.mat` + sus texturas
- Materiales URP comunes detectados
- 5 LOD meshes de `Stylized Rock Pack`
- Cualquier shader/material que aparezca en >3 bundles según el report

**Resultado esperado:**
- Decos individuales bajan ~80 MB en total (~15-20% menos descarga por usuario)
- Base WebGL sube ~5-10 MB
- Build time baja a 45-90 min (menos análisis por bundle)
- Riesgo: cero — los assets siguen cargando igual, solo cambia DÓNDE viven en los bundles

### Cómo identificar exactamente qué meter en Shared_Local

```
Window → Asset Management → Addressables → Analyze
  → Check Duplicate Bundle Dependencies → Run
  → Fix Selected Rules (mueve los duplicados a un nuevo grupo)
```

Unity propone automáticamente qué assets duplicar-fix. Suele acertar en 90% del caso, dejando los corner cases para revisión manual.

### Cuándo hacerlo
**Después de validar Cast Fase A end-to-end en Xiaomi** (no antes — no queremos cambiar variables a la vez).

---

## 6. Próximos pasos del pipeline

| Paso | Estado | Resultado |
|---|---|---|
| Build Player Content (los 92 bundles) | ✅ done 26-may 03:35 | 389 MB en `ServerData/WebGL/` |
| Build WebGL base (`File → Build`) | ✅ done 26-may 11:13 | **411 MB .data** (ver §8) |
| Deploy a Cloudflare R2 | ⛔ BLOQUEADO | Base demasiado grande para Cast — ver §8 |
| Test Xiaomi TV Box S end-to-end | ⛔ BLOQUEADO | Cast SDK timeout 30s, .data tarda 33-66s en cargar |
| (Opcional) Fix Shared_Local | ❌ pendiente | ~30 min + rebuild 45-90 min |
| **Fase A.1 — strip prefab refs en build** | ❌ NUEVO bloqueante | ver §9 |
| Fase B — sacar Backgrounds/Substrates de Resources/ | ❌ pendiente | siguiente iteración |

---

## 7. Lecciones aprendidas — añadir a memoria

1. **Verificar compresión real en buildlayout antes de asumir** — la memoria decía "fix LZ4 pendiente" pero el reporte demuestra que ya estaba aplicado.

2. **El TEP no traza el grueso del trabajo de SBP** — para diagnosticar tiempos largos hay que mirar `Logs/shadercompiler-*.log`, `Logs/AssetImportWorker*.log` y timestamps de mtime, no el TEP.

3. **PackSeparately + URP pipeline = duplicación inevitable de ~15-20%** sin un Shared_Local explícito. No es bug, es trade-off. Para escalar a 2GB+ de contenido (Fase B), Shared_Local es obligatorio.

4. **Builds Addressables en frío (primer build de un proyecto WebGL): 8-17h es normal**. Builds en caliente: 30-90 min. Borrar `Library/ShaderCache/` o cambiar de target invalida el cache → vuelta al primer build.

5. **Fish bundles 2,5 KB es síntoma de que los prefabs visuales no están en Addressables** — para Fase A está OK (van en la base WebGL) pero registrar como deuda técnica.

6. **(NUEVO) Cualquier referencia DIRECTA en la scene a un asset también en Addressables ⇒ DUPLICACIÓN automática** — Unity baka el grafo de dependencias del scene en el `.data` aunque ese asset esté en un grupo Addressables. La única forma de evitarlo es: (a) `AssetReference` en lugar de `GameObject prefab`, (b) nullear la ref durante `IPreprocessBuildWithReport`, o (c) eliminar la ref del scene y poblarla en runtime via Addressables. **Sin esto NO hay modelo Netflix posible.**

---

## 8. Build WebGL base — Resultado 2026-05-26 11:13

### Output

| Fichero | Tamaño |
|---|---|
| `webgl-output.data` | **411 MB** ⚠ |
| `webgl-output.wasm` | 62 MB |
| `webgl-output.framework.js` | 485 KB |
| `webgl-output.loader.js` | 27 KB |
| `index.html` (CastReceiver template) | 9.7 KB |
| `StreamingAssets/aa/` (catalog + built-in) | 32 KB |
| **Total** | **452 MB** |

Editor report:
- *Total compressed size 374.0 MB. Total uncompressed size 0.60 GB.*
- Build setting: `Compression Format = Disabled` (intencional — Cloudflare R2 sirve .gz con Content-Encoding, doble descompresión bug del 23-may).

### Composición del .data (top assets) — confirma duplicación

```
44.7 MB  ThirdParty/GreekStatues/greek_underwater_broken_statue_4.glb
44.7 MB  ThirdParty/GreekStatues/greek_underwater_broken_statue_3.glb
44.6 MB  ThirdParty/GreekStatues/greek_underwater_broken_statue_2.glb
36.8 MB  ThirdParty/GreekColumns/greek_underwater_column_1.glb
33.6 MB  ThirdParty/GreekColumns/greek_underwater_column_3.glb
30.0 MB  ThirdParty/GreekStatues/greek_underwater_broken_statue_1.glb
22.7 MB  ThirdParty/GreekColumns/greek_underwater_column_2.glb
22.5 MB  ThirdParty/GreekColumns/greek_underwater_column_5.glb
22.2 MB  ThirdParty/GreekColumns/greek_underwater_column_4.glb
20.4 MB  ThirdParty/Corals/meandrina_meandrites.glb
19.6 MB  ThirdParty/Corals/heliopora_coerulea.glb
19.0 MB  ThirdParty/Corals/corallium_sp..glb
18.8 MB  ThirdParty/Corals/stylaster_sanguineus.glb
18.7 MB  ThirdParty/Corals/distichopora_violacea.glb
18.6 MB  ThirdParty/Corals/pocillopora_damicornis.glb
18.3 MB  ThirdParty/Corals/diploria_labyrinthiformis.glb
18.3 MB  ThirdParty/Corals/acropora_valenciennesi.glb
18.2 MB  ThirdParty/Shells/tridacna_squamosa.glb
17.9 MB  ThirdParty/Shells/linckia_laevigata.glb
17.9 MB  ThirdParty/Shells/cypraecassis_rufa.glb
15.2 MB  ThirdParty/Shells/lambis_shell.glb
…
4.8 MB   Resources/Audio/ambient_music.mp3
14 MB    Resources/Substrates/* + Backgrounds/* (esperado, Fase B)
```

**Suma ThirdParty GLBs: ~547 MB raw / ~411 MB comprimidos en .data.**

**Estos mismos GLBs están TAMBIÉN en `ServerData/WebGL/`** (los 92 bundles del paso anterior). El contenido se duplica entre `.data` (base WebGL) y los `.bundle` (R2 lazy).

### Diagnóstico de la duplicación

Cadena de dependencias auditada:

```
TvScene.unity
  └── AquariumManager (GameObject)
      ├── allFishCatalog[25] ──► FishData_*.asset ──► .prefab ──► Pack 24 fish.prefab ──► fish.fbx + textures
      └── allDecoCatalog[54] ──► DecorationData_*.asset ──► .prefab ──► deco_*.prefab ──► GLB + textures
```

Verificado: `grep -c "fileID: 11400000" TvScene.unity` = **84 SO references**. El scene baka todo el grafo de dependencias en el `.data` del build, aunque los mismos prefabs también vivan en grupos Addressables.

**Origen del bug:** mismo `AquariumManager` que en mobile, donde la duplicación no es problema porque no usa Addressables. En TV, el catálogo serializado en el scene fuerza la duplicación.

### Impacto para Cast en Xiaomi

Cast SDK timeout = 30s desde que el receiver es seleccionado hasta que envía READY. Tiempo de descarga del .data:

| Conexión WiFi | ETA 411MB | Cast SDK |
|---|---|---|
| 50 Mbps | 66 s | ❌ timeout |
| 100 Mbps | 33 s | ⚠ borderline |
| 200 Mbps (fibra+wifi6) | 16 s | ✅ probable OK |

Mismo síntoma que el monolítico 293MB del 23-may. Bundles remotos cargarían DESPUÉS del READY (no afectados por el timeout) → la arquitectura Netflix funciona si el base es ≤50MB.

---

## 9. Fase A.1 — Strip prefab refs durante build (NUEVO bloqueante)

### Objetivo
Reducir el .data de **411 MB → ~50 MB** sacando los GLBs duplicados del build. Los prefabs solo viven en los 92 bundles remotos y se cargan via Addressables en runtime.

### Implementación (3 opciones, en orden de complejidad creciente)

**A. `IPreprocessBuildWithReport` que nullea `data.prefab`**
- En `Assets/Editor/`, nuevo script que implementa `IPreprocessBuildWithReport`
- Antes del build: iterar SOs del catálogo, guardar refs originales en un JSON temporal, set `prefab = null`, `AssetDatabase.SaveAssets()`
- `IPostprocessBuildWithReport`: restaurar refs desde el JSON
- Runtime: `TvSceneBootstrap.LoadAndInitializeCoroutine` ya hace `Addressables.LoadAssetAsync<FishData>` — devuelve el SO con su `prefab` field repoblado por el bundle
- **Pros:** mínima cirugía, reversible, no toca runtime
- **Contras:** corre el riesgo de dejar prefabs nulleados si el build aborta a medias

**B. `AssetReference` en lugar de `GameObject prefab`**
- Refactorizar `FishData.prefab` y `DecorationData.prefab` de `GameObject` a `AssetReferenceGameObject`
- Runtime: `await data.prefabRef.LoadAssetAsync<GameObject>()` en vez de `data.prefab`
- **Pros:** modelo Addressables idiomático, no need de pre/post-process hooks
- **Contras:** breaking change que rompe el proyecto mobile (compartimos los SOs). Solo viable si los SOs del TV son COPIAS, no symlinks/refs al mobile

**C. Construir catálogos en runtime sin scene reference**
- Borrar `allFishCatalog` y `allDecoCatalog` del scene
- `AquariumManager.Start()` hace `Addressables.LoadAssetsAsync<FishData>("fish")` (label group) para poblar el catálogo
- **Pros:** ningún SO referenciado estáticamente
- **Contras:** cualquier UI que asuma el catálogo ya poblado en frame 0 se rompe; requiere refactor de inicialización

### Recomendación
**Opción A** para Fase A.1 — mínimo cambio, atómico, sin tocar runtime. Si funciona se queda; si Fase B requiere refactor más profundo, se hace junto a la migración a `AssetReference`.

### Coste estimado
- Implementación A: ~30 min
- Rebuild Player Content (cache caliente): ~1-3h
- Rebuild WebGL base: ~15-30 min
- Deploy R2: ~5 min
- Total: 2-4h hasta poder testear Cast en Xiaomi

---

## 10. Estado final 2026-05-26 11:30

```
✅ Build Player Content done       (92 bundles, 389 MB, en ServerData/WebGL/)
✅ Build WebGL done                (411 MB .data, en webgl-output/)
⛔ Deploy a R2                     (no merece la pena hasta resolver §9)
⛔ Test Cast Xiaomi                (mismo timeout que monolítico 23-may esperado)
❌ Fase A.1 — strip prefab refs   (bloqueante — sin esto no hay modelo Netflix)
```

