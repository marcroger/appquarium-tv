# Build Report — 2026-06-02

> ⚠⚠ **DOCUMENTO HISTÓRICO — NO SEGUIR SUS INSTRUCCIONES.**
> Contiene comandos de deploy con `--delete` contra la raíz del bucket R2. Ese comando
> **borra `keepalive_black.mp4`** (el vídeo keepalive del que dependen las sesiones largas),
> `silence.wav` y los rigs de diagnóstico. `--exclude "bundles/*"` **NO** protege de eso:
> sólo protege el prefijo `bundles/`, y en la raíz hay más cosas que no están en
> `webgl-output/`. El comando correcto está en `CLAUDE.md` → «Comandos clave».
> Se conserva por su valor de registro, no como guía.

**Rama:** `feat/netflix-architecture`  
**Sesión:** Diagnóstico y fix de rendering de peces en WebGL/Cast

---

## Resumen ejecutivo

Se resolvió la cadena completa de bugs de rendering que impedía ver los peces. El Banggai Cardinalfish se ve con cuerpo opaco, textura correcta y aletas semitransparentes. **✅ CONFIRMADO EN PANTALLA en Xiaomi TV Box S vía Cast (2026-06-08).** Verificado primero en Chrome local (zoom screenshot), ahora validado end-to-end en el device real. Primer pez 3D real renderizando correctamente en Cast — hito de la Fase A.1.

Cadena de bugs: magenta → semitransparente → cuerpo invisible → **✅ opaco con textura**

**Estado final deployado en R2 (02/06 ~19:00):**
- Player `.data` 18:38 con shader `Appquarium/FishUnlit` (CG legacy)
- Bundles banggai `724dba...` + moorish `11704c...`
- Catalog sincronizado en StreamingAssets y R2

---

## Bug chain completo — NO iterar sobre lo ya probado

### Bug 1 ✅ — Fondo morado (resuelto sesión 01/06)
**Síntoma:** `bg_kelp` aparecía morado/violeta en lugar de verde.  
**Causa:** `URP/Unlit` shader tiene bug de color space en Unity 6 WebGL. Renderiza los colores incorrectamente aunque el shader esté disponible.  
**Fix:** `TankBackground.cs` y `WaterSurface.cs` usan `Sprites/Default` como shader primario.  
**Estado:** ✅ Deployado. Fondo verde confirmado en screenshot local.

---

### Bug 2 ✅ — Pez magenta (resuelto sesión 02/06)
**Síntoma:** El pez aparecía magenta sólido (color de error de Unity = shader no encontrado).  
**Causa:** `Universal Render Pipeline/Lit` (GUID `933532a4fcc9baf4fa0491de14d08ed7`) **se stripea del build WebGL** con `Managed Stripping Level: High`. Los materiales en Addressable bundles no se analizan durante el shader stripping → shader desaparece del player.  
**Lo que NO funciona:** Añadir URP Lit a "Always Included Shaders" — insuficiente con High stripping.  
**Fix:** Cambiar los materiales del fish pack a `Sprites/Default` o `Appquarium/FishUnlit` (ver Bug 4).  
**Estado:** ✅ Resuelto.

---

### Bug 3 ✅ — Catálogo remoto roto (resuelto sesión 02/06)
**Síntoma:** Unity siempre descargaba el bundle VIEJO aunque hubiera bundles nuevos en R2.  
**Causa:** `settings.json` embebido en el player tiene la URL del catálogo remoto con **doble slash**: `bundles//catalog_1.2.1.hash` → HTTP 404. Unity no puede actualizar el catálogo en runtime y cae back al catálogo local (que apunta a los bundles viejos).  
**Diagnóstico:** `curl -I "bundles//catalog_1.2.1.hash"` → 404 | `curl -I "bundles/catalog_1.2.1.hash"` → 200.  
**Workaround activo:** Después de cada `★ New Build`, copiar `ServerData/WebGL/catalog_1.2.1.bin` y `.hash` a `webgl-output/StreamingAssets/aa/catalog.{bin,hash}`. Unity usa el catálogo local embebido en StreamingAssets (que sí apunta a los bundles nuevos). El player rebuild automáticamente copia el catálogo correcto.  
**Fix correcto pendiente:** Encontrar y corregir el doble slash en `TvAddressablesSetup.cs` o en la configuración de perfiles de Addressables. Requiere player rebuild.

---

### Bug 4 ✅ — Pez semitransparente / body invisible (RESUELTO 02/06)

**Síntoma A — Semitransparente (Sprites/Default en body):**  
Cuerpo del pez semitransparente. Se ven las rayas de ambos lados del mesh simultáneamente.  
**Causa:** `Sprites/Default` tiene `Cull Off` (renderiza ambas caras) y `ZWrite Off` + alpha blending. En un mesh 3D = ambos lados visibles, efecto de doble cara.

**Síntoma B — Body completamente invisible (FishUnlit con `Cull Back`):**  
Con `Appquarium/FishUnlit` shader configurado como `Cull Back`, el cuerpo del pez era **100% invisible** incluso con `_BaseColor: (8,8,8,1)`.  
**Causa confirmada:** Las **normales del mesh del fish pack están invertidas**. Con `Cull Back`, Unity considera TODAS las caras como "back faces" y las elimina. Con `Sprites/Default` (Cull Off) al menos se renderizaban ambas caras y algo era visible.  
**Esto elimina estas opciones para siempre:**
- `Cull Back` en cualquier shader para este fish pack → body invisible
- Alpha cutout (`clip()`) en el body → body invisible (alpha < threshold en todo el mesh)

**Fix correcto (shader `Appquarium/FishUnlit` v2, 02/06 14:xx):**
```hlsl
Cull  Off    // ambas caras — obligatorio por normales invertidas del pack
ZWrite On
// Sin clip() — el alpha channel del texture no debe usarse para cutout
// return half4(c.rgb, 1.0) — fuerza opaco, ignora alpha completamente
_Brightness: 1.5  // compensa el unlit flat shading contra fondo oscuro
```
Shader en `Assets/Shaders/FishUnlit.shader`. GUID: `60c4ee7717958bf408b5b7f628166d09`.

**⚠ CRÍTICO — Por qué URP HLSL NO funciona en Cast:**  
El pass `"LightMode" = "UniversalForward"` no se ejecuta en el Chromium del Cast device. El shader compila sin errores, sin magenta, pero el body es 100% invisible. Diagnosticado con `_BaseColor: (20,20,20,1)` — si el pass ejecutara, el body sería blanco brillante. Como era invisible → el pass no ejecuta.  
`Sprites/Default` funciona porque NO tiene LightMode tag.

**Solución final: CG legacy (CGPROGRAM) sin LightMode tag:**
```
Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
Cull Off   ← normales invertidas del fish pack
ZWrite On
Pass { /* SIN LightMode tag → ejecuta en cualquier SRP renderer */
  CGPROGRAM / UnityCG.cginc / tex2D(_MainTex) * _Color * _Brightness
  return fixed4(c.rgb, 1.0)  ← fuerza opaco, ignora alpha del texture
}
```

**Lo que NO funciona (no volver a probar):**
- `"LightMode"="UniversalForward"` URP HLSL → invisible en Cast (pass no ejecuta)
- `Cull Back` → invisible (normales invertidas del Global Reef Fish Pack)
- `clip(alpha - N)` → invisible (alpha del body mesh < threshold en todo el mesh)

**Estado:** ✅ Verificado en Chrome (screenshot zoom: textura + rayas correctas) Y ✅ confirmado en Xiaomi TV Box S vía Cast (2026-06-08).

---

## Estado de materiales del fish pack (Global Reef Fish Pack)

| Material | Shader | Notas |
|---|---|---|
| `M_fish_20_BanggaiCardinalfish_body.mat` | `Appquarium/FishUnlit` | `_Color:(1,1,1,1)` — brillo via shader `_Brightness:2.0` |
| `M_fish_20_BanggaiCardinalfish_fins.mat` | `Sprites/Default` | Transparencia correcta para aletas |
| `M_fish11_MoorishIdol.mat` | `Appquarium/FishUnlit` | Single material (body+fins), cutout no necesario |

**Por qué `_MainTex` y no `_BaseMap`:**  
El shader CG usa `_MainTex` (API legacy). Los materiales URP Lit originales tienen AMBOS (`_BaseMap` y `_MainTex`) apuntando a la misma textura (`e3fd265f...`). No hay que cambiar nada en el material para que CG lo lea.

**Regla para todos los fish futuros (23 restantes):**
1. `.mat` de body → `m_Shader: {fileID: 4800000, guid: 60c4ee7717958bf408b5b7f628166d09, type: 3}` (FishUnlit)
2. `.mat` de fins (si existe separado) → `m_Shader: {fileID: 10753, guid: 0000000000000000f000000000000000, type: 0}` (Sprites/Default)
3. Fish de single-material → FishUnlit
4. `★ New Build` → bundle rebuild (~30s incremental, ~2h cold)
5. **NO player rebuild** — FishUnlit ya está compilado en el player actual

---

## Estado de R2 a fin de sesión 02/06 (~19:00)

```
Build/webgl-output.data     20.35 MB  (02/06 18:38) — player CG shader
Build/webgl-output.wasm     41.93 MB  (02/06 13:27) — sin cambios C#
Build/webgl-output.framework.js        (02/06 13:27)
Build/webgl-output.loader.js           (02/06 13:27)
index.html                             (02/06) — con fixes devtest

StreamingAssets/aa/catalog.bin         hash: b8615dc701eeea2ec39cd58002422c99
StreamingAssets/aa/catalog.hash        idem
StreamingAssets/aa/WebGL/unitybuiltinassets_51d929...bundle (53.8 KB)
StreamingAssets/aa/WebGL/monoscripts_4c29738a...bundle (1.7 KB)

bundles/fish_banggai_cardinalfish_724dbae801d11473f5a0a20a8ccc4d9e.bundle  311 KB
bundles/fish_moorish_idol_11704c1c6785433a310ce3e130e62c06.bundle           631 KB
bundles/catalog_1.2.1.bin / .hash
```

> ⚠ **El player build NO actualiza .wasm si no hay cambios de C#.** Solo el `.data` cambia cuando cambian shaders/assets. Un build "rápido" puede ser correcto si solo actualizó el `.data`.

> ⚠ **Workflow de catalog.** Cada ★ New Build genera nuevos hashes de bundle. El player build copia el último catálogo al StreamingAssets automáticamente. Si NO haces player rebuild, hay que copiar manualmente: `Copy-Item ServerData/WebGL/catalog_1.2.1.bin webgl-output/StreamingAssets/aa/catalog.bin` y subir a R2 con `put-object`.

---

## Workflow de build/deploy (actualizado 02/06)

### Bundle-only (el caso normal — añadir pez nuevo o cambiar material)

```powershell
# 1. Cambiar materiales (.mat) si hace falta
# 2. En Unity Editor:
#    Appquarium TV → ★ New Build (Default Build Script)  (~30s con SBP cache, ~2h cold)

# 3. Deploy bundles
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"
$ep = "https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com"

aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/ --profile r2 --endpoint-url $ep --cache-control "public, max-age=604800"
# catalog.hash siempre falla con sync → subir con put-object:
aws s3api put-object --bucket appquarium-tv --key bundles/catalog_1.2.1.hash --body ServerData/WebGL/catalog_1.2.1.hash --content-type "text/plain" --cache-control "public, max-age=60" --profile r2 --endpoint-url $ep

# 4. Actualizar catalog en StreamingAssets (workaround doble-slash bug)
Copy-Item ServerData/WebGL/catalog_1.2.1.bin webgl-output/StreamingAssets/aa/catalog.bin -Force
$h = Get-Content ServerData/WebGL/catalog_1.2.1.hash
Set-Content webgl-output/StreamingAssets/aa/catalog.hash $h -NoNewline -Encoding utf8

aws s3api put-object --bucket appquarium-tv --key StreamingAssets/aa/catalog.bin --body webgl-output/StreamingAssets/aa/catalog.bin --cache-control "public, max-age=60" --profile r2 --endpoint-url $ep
aws s3api put-object --bucket appquarium-tv --key StreamingAssets/aa/catalog.hash --body webgl-output/StreamingAssets/aa/catalog.hash --content-type "text/plain" --cache-control "public, max-age=60" --profile r2 --endpoint-url $ep
```

### Player rebuild (solo si cambia C# o shaders)

```powershell
# 1. File → Build Settings → Build → webgl-output/

# 2. Deploy player (--delete sin tocar bundles/)
aws s3 sync webgl-output/ s3://appquarium-tv/ --profile r2 --endpoint-url $ep --delete --exclude "bundles/*" --cache-control "public, max-age=3600"
# Los archivos pequeños (loader.js, index.html, catalog.*, unitybuiltinassets.bundle)
# fallan con sync → subir con aws s3api put-object individual

# 3. Si también cambió el bundle (porque se cambió un .mat o shader):
#    Ejecutar también el workflow bundle-only de arriba
```

### Verificar antes de deploy a TV

```powershell
# Servidor local:
cd D:\dev\appquarium-tv-unity\webgl-output
python -m http.server 3001   # ⚠ 8080 ocupado por Docker

# Test automatizado (otra terminal):
cd D:\dev\appquarium-tv-unity
node Tools/local-test.js
# → local-test-screenshot.png (Claude puede leer la imagen directamente)
```

---

## Shaders — mapa definitivo para WebGL Cast (Unity 6 URP)

| Shader | Estado | Uso |
|---|---|---|
| `Sprites/Default` | ✅ Funciona | Backgrounds, WaterSurface, Fins |
| `Appquarium/FishUnlit` (CG legacy) | ✅ Funciona | Body de peces — ver Assets/Shaders/FishUnlit.shader |
| `Universal Render Pipeline/Lit` | ❌ Stripeado con High stripping | NO usar en bundles |
| `Universal Render Pipeline/Unlit` | ❌ Bug color space WebGL | NO usar — colores incorrectos |
| Cualquier shader URP HLSL con `"LightMode"="UniversalForward"` | ❌ No ejecuta en Cast | Pass ignorado por Chromium Cast |
| `UI/Default` | ✅ Existe | Para UI |

**Always Included Shaders actuales en GraphicsSettings.asset:**
```yaml
- Sprites/Default          (fileID: 10753, guid: 0000000000000000f000000000000000)
- Appquarium/FishUnlit     (fileID: 4800000, guid: 60c4ee7717958bf408b5b7f628166d09)
- URP Lit                  (GUID: 933532a4fcc9baf4fa0491de14d08ed7) ← irrelevante, stripeado
```

**Regla general:** Si un shader URP no renderiza en Cast, probar con CG legacy sin LightMode tag.

---

## Local testing workflow

```
URL devtest:  http://localhost:3001?devtest=1
INIT hardcoded: fish_banggai_cardinalfish + bg_kelp + tank_l
Cast SDK:     Se carga desde gstatic (internet), ctx.start() OK en Chrome
Puppeteer:    espera "AQUARIUM READY" en #dbg-panel → screenshot
```

**Fixes aplicados al pipeline de devtest (02/06):**
- `isDevTest` global en index.html — evita mostrar error de Cast SDK en modo local
- `window.unityInstance = instance` — el interval de devtest ahora puede disparar el INIT
- `hideSplash()` automático en devtest sin Cast SDK
- Puerto 3001 (8080 tomado por Docker backend)
- Editar siempre `Assets/WebGLTemplates/CastReceiver/index.html`, nunca `webgl-output/index.html`

---

## SBP Cache — tiempos de referencia

| Operación | Tiempo |
|---|---|
| Bundle cold (1 pez nuevo, primera vez) | ~2h |
| Bundle incremental (solo cambio material) | ~30s |
| Bundle todo cacheado (solo catalog) | ~9s |
| Player rebuild (caliente) | ~1-3h |
| Player rebuild (frío, desde cero) | ~16h |

**⚠ NO usar `Update a Previous Build`** — solo para CCD/Unity Cloud. Para R2 self-hosted siempre `New Build`. Con SBP cache, `New Build` ya es efectivamente incremental.
