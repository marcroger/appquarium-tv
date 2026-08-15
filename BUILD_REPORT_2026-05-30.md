# Build Report — Sesión 2026-05-30

## Estado al cerrar sesión

### ✅ Lo que funciona en Xiaomi (confirmado con logs reales)
- Unity carga en ~22s (wasm 42.2 MB, sin OOM)
- INIT chain JS→C# completa: Cast SDK → CastReceiver → TvSceneBootstrap → AquariumManager
- Background (bg_kelp), substrate, **decos con modelos 3D reales** renderizan correctamente
- Loading overlay (spinner + contador "X/Y cargados") funciona
- Debug panel cyan siempre visible con JsBridge (logs C# aparecen en TV)

### ❌ Lo que falta
- Fish sin modelo 3D — aparecen como esferas de colores (procedural fallback)
- Disconnect Cast ~2 min / al abrir cámara (secundario)

---

## Root cause fish sin modelo 3D

`FishData.prefab = {fileID: 0}` en todos los 25 SOs. Los SOs usaban `assetBundleAssetName` (sistema legacy AssetBundle del mobile que no existe en TV). Fallback → `BuildProceduralFishVisual`.

**Fix aplicado (29-may):** Menu item `★ Assign Fish Prefabs` en `TvAddressablesSetup.cs` asignó `FishData.prefab` en los 25 SOs apuntando a `Assets/ThirdParty/Mikhail Nesterov/Global Reef Fish Pack/Prefabs/`.

---

## Por qué el build de fish falló (18h de espera por nada)

### Problema 1 — "Update a Previous Build" no sirve aquí
Está diseñado para actualizar contenido ya existente en bundles. Cuando una dependencia masiva nueva se añade (prefab 3D que antes no existía), no detecta el cambio correctamente. Los bundles de fish en `ServerData/WebGL/` siguen siendo los del **26/05/2026 03:35** (2.5 KB cada uno, sin prefab).

**Fix:** Usar **"New Build → Default Build Script"** (no Update Previous Build).

### ~~Problema 2 — LZMA en Fish_Remote~~ ← DIAGNÓSTICO INCORRECTO ✅

**Verificado 2026-05-30:** `m_Compression: 1` en Addressables 3.0 = **LZ4**, NO LZMA.

El enum real (`BundledAssetGroupSchema.BundleCompressionMode`):
- 0 = Uncompressed
- 1 = LZ4  ← lo que tenemos — correcto
- 2 = LZMA

**No hay nada que cambiar.** Los 4 grupos remotos ya usan LZ4.

### Problema 3 — Error Token Exchange (posiblemente bloqueante)
Al cancelar apareció:
```
UnityConnectWebRequestException: Token Exchange failed due a failure with the web request.
duration: 0:00:00
```
Podría ser ruido de fondo (Unity Services intentando autenticar) o podría ser lo que impedía que el build arrancara. **Pendiente confirmar** si Unity tiene sesión activa cuando se reabra el editor.

---

## Plan — sesión 2026-05-30 (revisado y corregido)

> ⚠ Los pasos 0 y 1 del plan anterior eran incorrectos. El Token Exchange es ruido de Unity Services (no bloqueante) y la compresión ya era LZ4 (m_Compression: 1 = LZ4 en Addressables 3.0). Ver §§ arriba.

### Sobre `Update a Previous Build` — NO usar

`Update a Previous Build` es una feature para workflows CCD (Unity Cloud Content Delivery). Para hosting propio en R2, **usar siempre `New Build → Default Build Script`**. El SBP cache hace que sea efectivamente incremental: solo reconstruye bundles cuyos assets cambiaron. Los bundles sin cambios usan el cache artifact → milisegundos.

El build fantasma de 18h ocurrió porque `Update a Previous Build` no sabe manejar la adición de dependencias nuevas (prefab que era null → asignado). No construyó nada, solo analizó.

### Paso 1 — Reducir texturas (ANTES del primer build con fish 3D)

```
Unity → Appquarium TV → ★ Reduce TV Textures
```

Reduce texturas a 512px. El cuello de botella es WebGL texture compression (~45s/textura). 512px = 4× menos trabajo → build 25 peces: **~2-4h** en lugar de 8-16h.

### Paso 2 — Test con 1 solo pez

En Addressables Groups: quitar 24 peces de Fish_Remote, dejar solo `fish_banggai_cardinalfish`.

```
New Build → Default Build Script
```

**Tiempo estimado: ~10-15 min.** Verificar `ServerData/WebGL/fish_remote*banggai*.bundle` → debe ser 2-5 MB (no 2.5 KB).

### Paso 3 — Deploy bundles (solo los nuevos)

```powershell
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
```

**NO hace falta rebuild del player** — el C# ya maneja `visualPrefab != null` correctamente.

### Paso 4 — Cast y verificar en Xiaomi

El banggai cardinalfish debería aparecer con modelo 3D. Los otros 24 peces siguen cayendo al fallback de esfera (bundles 2.5 KB sin prefab) — aceptable para este test.

### Paso 5 — Build completo los 25 peces

```
★ Setup Addressables  (re-añade los 24 peces quitados)
New Build → Default Build Script  (~2-4h con 512px)
```

El SBP cache del banggai ya estará caliente → solo los 24 nuevos en frío.

### Builds incrementales futuros

Añadir pez 26:
```
★ Setup Addressables → New Build   (~10-15 min, solo el nuevo en frío)
```

Cambiar dato de un FishData SO:
```
New Build   (segundos, SBP cache hit en assets 3D)
```

---

## Cambios en código realizados esta sesión (29-may)

| Archivo | Cambio |
|---|---|
| `Assets/Plugins/WebGL/JsBridge.jslib` | NUEVO — bridge C#→JS para logs en panel cyan |
| `Assets/Scripts/Utils/JsBridge.cs` | NUEVO — wrapper C# del bridge |
| `Assets/Scripts/Core/TvSceneBootstrap.cs` | JsBridge.Log en puntos clave de la chain |
| `Assets/Scripts/Core/AquariumManager.cs` | JsBridge.Log en InitAquarium, SpawnFish |
| `Assets/Editor/TvAddressablesSetup.cs` | NUEVO menu item `★ Assign Fish Prefabs` |
| `Assets/WebGLTemplates/CastReceiver/index.html` | Panel cyan siempre visible, font 17px, 22 líneas |
| `webgl-output/index.html` | Ídem (overwritten por Unity build, re-aplicado) |

**Player rebuildeado y deployado** (30-may) con JsBridge incluido.
**FishData SOs actualizados** — 25 SOs tienen `FishData.prefab` asignado al prefab real.

---

## Arquitectura visual confirmada en Xiaomi

| Asset | Ruta | Estado |
|---|---|---|
| Background texture | `Resources/Backgrounds/` → base .data | ✅ renderiza |
| Substrate texture | `Resources/Substrates/` → base .data | ✅ renderiza |
| Deco 3D models | `DecorationData.prefab` bundleado con SO | ✅ renderiza |
| Fish 3D models | `FishData.prefab` asignado, bundles pendientes | ⏳ próxima sesión |
| Fish behavior (IA) | `fishPrefab` en escena → base .data | ✅ |

## Nota sobre sync con mobile
Después de cada `SyncFromMobile.ps1`, los FishData SOs se sobrescriben (vuelven a `prefab: null`). Hay que re-ejecutar `★ Assign Fish Prefabs` + rebuild de Fish_Remote tras cada sync.

## Disconnect Cast (secundario)
Cae ~2 min o al abrir cámara en el móvil. `disableIdleTimeout: true` ya está en receiver — el problema es sender-side (lifecycle Android). Investigar en Fase B.
