# Build Report — Sesión 2026-05-30 (tarde/noche)

## Resumen

Sesión de diagnóstico + intento de test con 1 pez (banggai). Build completo fallido por orden incorrecto de operaciones. Build de banggai aislado corriendo al cerrar sesión.

---

## Correcciones importantes a documentación previa

### ✅ m_Compression: 1 = LZ4 (no LZMA)

BUILD_REPORT_2026-05-30 (mañana) tenía mal el enum de Addressables 3.0:

```
BundleCompressionMode (Addressables 3.0):
  0 = Uncompressed
  1 = LZ4   ← lo que teníamos siempre
  2 = LZMA
```

Los 4 grupos remotos ya tenían LZ4 desde el primer build. No era el culpable de los builds lentos.

### ✅ `Update a Previous Build` — NO usar para R2

Es una feature de CCD (Unity Cloud). Para hosting propio en R2, **siempre `New Build → Default Build Script`**. El SBP cache lo hace incremental automáticamente.

### ✅ Por qué los builds son lentos

El cuello de botella es la **compresión de texturas para WebGL** (DXT/ETC2 en CPU, ~45s/textura). Cada fish tiene ~4 texturas únicas. En caché fría:
- 1 pez: ~15-30 min
- 25 peces: ~2-4h (con 512px) / ~8-16h (con 1024px)

---

## Qué salió mal hoy — La cagada

### Error: orden incorrecto de operaciones

**Lo que debería haberse hecho:**
1. Aislar banggai en Fish_Remote
2. Deshabilitar otros grupos (Decos_Remote, Environments_Remote, Audio_Remote)
3. `New Build` → solo banggai → 15-30 min
4. Validar que funciona
5. Re-habilitar grupos
6. **LUEGO** reducir texturas si se quiere optimizar build time
7. Full rebuild de todos los grupos (~2-4h)

**Lo que se hizo:**
1. Reducir texturas (512px) ← PRIMERO, **error grave**
2. Aislar banggai
3. `New Build` ← invalida TODO el SBP cache porque los hashes de texturas cambiaron

**Consecuencia:** el build reconstruyó los ~68 bundles activos (54 decos + 11 env + 2 audio + 1 fish) en lugar de solo 1. Se paró a las ~2h51min de ~4-6h estimadas.

### Regla que hay que recordar

> **Nunca reducir texturas ANTES de un test build.** El SBP cache almacena los artefactos indexados por hash de los assets. Cambiar texture size = cambiar hash = invalidar TODO el cache = rebuild completo.

---

## Estado al cerrar sesión

| Item | Estado |
|---|---|
| Fish_Remote | Solo `fish_banggai_cardinalfish` ✅ |
| Decos_Remote | `m_IncludeInBuild: 0` (desactivado) ✅ |
| Environments_Remote | `m_IncludeInBuild: 0` (desactivado) ✅ |
| Audio_Remote | `m_IncludeInBuild: 0` (desactivado) ✅ |
| Texturas | 512px (reducidas) |
| Build activo | Sí — banggai only, lanzado ~23:44 |
| ServerData/WebGL | Bundles viejos del 26-may (2.5 KB fish, decos OK) |

### Build corriendo al cerrar

Se lanzó `★ New Build (Default Build Script)` con solo Fish_Remote activo (banggai). Tiempo estimado: **15-30 min**.

Resultado esperado en `ServerData/WebGL/`:
```
fish_remote_assets_fish_banggai_cardinalfish_*.bundle → 2-5 MB  (era 2.5 KB)
```

---

## Plan mañana (LEER ESTO PRIMERO)

### 1. Verificar si el build terminó

```powershell
Get-ChildItem "ServerData\WebGL" | Where-Object { $_.Name -like "*banggai*" } |
  Select-Object Name, @{N="KB";E={[math]::Round($_.Length/1KB,1)}}
```

- Si ~2000-5000 KB → build OK ✅ → ir al paso 2
- Si 2.5 KB → build falló → ver Console Unity para el error → relanzar `★ New Build`

### 2. Deploy solo el bundle banggai

```powershell
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"
aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --cache-control "public, max-age=604800"
```

### 3. Test en Xiaomi

Cast desde mobile. El banggai debería aparecer con modelo 3D (los otros 24 peces seguirán como esferas — aceptable para este test).

### 4. Si banggai funciona → Full build

Re-habilitar grupos y construir todo:

```
Unity → Appquarium TV/★ New Build (Default Build Script)
```

Con los 3 grupos re-habilitados (Decos, Environments, Audio) y los 24 peces restantes añadidos vía `★ Setup Addressables`, el build completo tardará **~2-4h** (con 512px, SBP cache warm para algunos assets).

Para re-habilitar grupos: editar `m_IncludeInBuild: 0 → 1` en los 3 schemas:
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Decos_Remote_BundledAssetGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Environments_Remote_BundledAssetGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Audio_Remote_BundledAssetGroupSchema.asset`

O bien: Claude puede hacerlo vía edición directa de los .asset files.

---

## Procedimiento correcto para futuros tests de 1 asset

```
1. Aislar el asset:     Appquarium TV → ★ Test: Isolate Banggai (1 pez)
2. Deshabilitar grupos: editar m_IncludeInBuild: 0 en Decos/Env/Audio schemas
3. New Build           → 15-30 min (solo 1 bundle)
4. Verificar bundle    → debe ser 2-5 MB
5. Deploy + test
6. Re-habilitar grupos: editar m_IncludeInBuild: 1 en los 3 schemas
7. ★ Setup Addressables (re-añade los 24 peces)
8. New Build completo  → ~2-4h con 512px (one-time)
```

**Si se quiere reducir texturas:** hacerlo en el paso 8, no antes.

---

## Cambios en código esta sesión

| Archivo | Cambio |
|---|---|
| `Assets/Editor/TvAddressablesSetup.cs` | Nuevo `★ Test: Isolate Banggai (1 pez)` — quita 24 peces excepto banggai |
| `Assets/Editor/TvAddressablesSetup.cs` | Nuevo `★ New Build (Default Build Script)` — lanza build vía MCP |
| `Assets/Editor/TvAddressablesSetup.cs` | Fix comment `AssignFishPrefabs`: "Update a Previous Build" → "New Build" |
| `Assets/AddressableAssetsData/AssetGroups/Schemas/Decos_Remote_BundledAssetGroupSchema.asset` | `m_IncludeInBuild: 0` (temporal, re-habilitar tras test) |
| `Assets/AddressableAssetsData/AssetGroups/Schemas/Environments_Remote_BundledAssetGroupSchema.asset` | `m_IncludeInBuild: 0` (temporal) |
| `Assets/AddressableAssetsData/AssetGroups/Schemas/Audio_Remote_BundledAssetGroupSchema.asset` | `m_IncludeInBuild: 0` (temporal) |
| `BUILD_REPORT_2026-05-30.md` | Corrección enum LZ4 + plan actualizado |
| `ADDRESSABLES_ROADMAP.md` | Corrección enum + tabla de builds actualizada + "Update a Previous Build" explicado |
| `CLAUDE.md` | Nota "Update a Previous Build" eliminada, build workflow corregido |
