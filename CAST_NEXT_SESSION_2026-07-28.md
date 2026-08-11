# ▶▶ EMPEZAR AQUÍ — sesión 2026-07-28 · Cast disconnect

> Escrito al cierre del 2026-07-27. Contexto completo en `CAST_DISCONNECT_INVESTIGATION.md`.
> **El problema está RESUELTO en el rig de pruebas. Falta validar el acuario real en el device.**

---

## 1. Qué pasó ayer, en cuatro líneas

- Se activó **adb** en la caja (llevaba una semana bloqueando la investigación) y por fin se pudo
  medir la memoria del SISTEMA, no solo el heap del navegador.
- **Causa raíz:** nuestro `.wasm` de 44 MB hace que el renderer pique a ~795 MB en una caja de
  1,92 GB → `MemAvailable` cae al 6-10 % → por debajo del 25 % (`kCriticalMemoryFraction` de Cast)
  la plataforma se lleva la app a los 150-275 s. **No era el motor, ni Cast, ni WebGL.**
- **Un Unity mínimo (wasm 10,6 MB) NO se corta** (>660 s). ⇒ Unity WebGL sí cabe en el device.
- **Fix validado en el rig vacío:** quitando 7 paquetes de runtime sin usar + poniendo el Code
  Optimization de WebGL en `DiskSizeLTO`, el `.wasm` bajó a 25,4 MB (−42 %), el pico a 654 MB, la
  fuga de 18,8 → 0,1 MB/min, y **la sesión aguantó los 660 s completos sin cortarse**.

## 2. ⏭ LO ÚNICO QUE FALTA (30 minutos)

**Medir el acuario real con el build recortado.** Ya está todo desplegado; solo hay que castear.

```bash
bash Tools/cast-run.sh 2 acuario-slim
```

Ciclo completo sin intervención: reinicia la caja, espera asentamiento, castea sin navegador y
analiza. ⚠ **Requiere la caja DESPIERTA** (ver §5).

**Cómo leerlo:**
- Duración > 660 s y 0 firmas de crash → **PROBLEMA RESUELTO**, pasar a §3.
- Corta antes → el contenido del acuario añade memoria sobre el rig. Siguientes palancas en §4.

Referencias para comparar (todas medidas ayer, mismo device, mismo método):

| Build | `.wasm` | Pico renderer | Fuga | Duración |
|---|---|---|---|---|
| Producción original (acuario) | 44,2 MB | 794 MB | +20-26 MB/min | 148-274 s ❌ |
| Producción original (escena vacía) | 44,2 MB | 794 MB | +18,8 MB/min | 239 s ❌ |
| Unity mínimo (referencia) | 10,6 MB | 381 MB | +0,1 MB/min | >660 s ✅ |
| **Rig vacío recortado** | **25,4 MB** | **654 MB** | **+0,1 MB/min** | **>660 s ✅** |
| **Acuario recortado** | **25,4 MB** | ? | ? | **← medir esto** |

## 3. Si el acuario aguanta

1. **Verificar visualmente** que salen los peces (el build regeneró el catálogo de Addressables;
   los bundles de R2 NO se reconstruyeron, deberían casar pero hay que confirmarlo con los ojos).
2. Desplegar un **receiver de producción limpio** — el que sirve R2 ahora es el de DIAGNÓSTICO
   (`rcv-prod-config.html`, panel de debug + 23 escalones). Tarea acotada y aparte.
3. Commit de los cambios (§6) y decidir si se mergea a `main`.

## 4. Si el acuario NO aguanta — palancas restantes, por coste de calidad

| Palanca | Ganancia estimada | Coste en calidad |
|---|---|---|
| Mover assets del `.data` (16,9 MB) a bundles remotos | hasta −10 MB | **cero** — misma calidad, carga diferida |
| Revisar `gltfast` / `ugui` (se usan en 1 fichero cada uno) | 1-3 MB | cero si se sustituyen |
| `Initial Memory` 64 → 32 MB | reduce la reserva del heap | cero |
| Reducir texturas (`★ Reduce TV Textures`) | variable | **sí, visible** |
| Salir de URP a Built-in RP | grande | **alto**: refactor de 3 shaders + post-proceso |

⚠ El `.wasm` NO contiene calidad visual: es código. Recortarlo no cuesta un píxel.
⚠ El `.wasm` no se puede trocear ni cargar progresivamente (V8 lo compila entero antes de arrancar).
El contenido sí carga progresivamente, y eso ya lo hace Addressables.

## 5. Estado del entorno al cerrar

| Cosa | Estado |
|---|---|
| **R2 `/Build/webgl-output.*`** | ⚠ **REEMPLAZADO** por el build recortado (wasm 25.430.429). El de junio ya NO está en R2. |
| **Backup del player de junio** | `scratchpad/prod-backup-2026-07-27/` — 4 ficheros, wasm 44.250.183 verificado. **Restaurar = volver a subirlos.** |
| R2 `/StreamingAssets/aa/` | catálogo + settings.json del build nuevo |
| R2 `/index.html` | receiver de DIAGNÓSTICO `rcv 2026-07-27 noKa-ab2` |
| Bundles en R2 `/bundles/` | intactos (mayo/junio), NO se reconstruyeron |
| `webgl-output/` local | el build recortado |
| Caja Xiaomi | **192.168.1.33** (¡no .47!), en standby con los puertos cerrados |
| Panel de estado | `node Tools/status-server.js` → http://localhost:3005 |

### 🔴 La caja se duerme y cierra adb

Entra en standby profundo sola: responde a ping pero cierra `adbd` (5555) y Cast (8008/8009). Desde
ahí **no se puede despertar por red** — hace falta pulsar una tecla del mando.

**Arreglo recomendado antes de la próxima tanda:** en la caja, `Opciones de desarrollador →
Permanecer activo` (Stay awake) ON. Así se pueden encadenar medidas sin intervención humana.
De paso cierra el TEST C que quedó pendiente (descartar el salvapantallas), aunque ya sabemos que
el corte era memoria.

## 6. Cambios en el repo (sin commit, sin push)

| Fichero | Qué |
|---|---|
| `Packages/manifest.json` | **−7 paquetes de runtime sin usar**: purchasing (IAP), visualscripting, inputsystem, mobile.notifications, timeline, ai.navigation, postprocessing v2. Backup: `manifest.json.bak-2026-07-27`. Verificado que ningún script los referencia y que `activeInputHandler: 0` (legacy). |
| `Assets/Editor/TvWasmOptimize.cs` | **nuevo** — lee/ajusta el Code Optimization del WASM |
| `Assets/Editor/TvProdBuild.cs` | **nuevo** — build de producción en batchmode |
| `Assets/Editor/TvEmptyTestBuild.cs` | + `BuildEmptyBatch()` sin diálogos |
| `Tools/cast-headless.js` | **nuevo** — sender Cast SIN navegador (protocolo Cast v2) |
| `Tools/cast-run.sh` | **nuevo** — ciclo completo autónomo |
| `Tools/status-server.js` | **nuevo** — panel de estado |
| `Tools/deploy-and-measure-empty.sh` | **nuevo** |
| `Tools/cast-adb-capture.sh` | reescrito: IP correcta, umbrales reales, `dumpsys`, reintentos |
| `Tools/rcv-prod-config.html`, `sender-video.html` | escalón 23 (sin vídeo keepalive) |
| `CAST_DISCONNECT_INVESTIGATION.md` | +400 líneas con todo lo medido |

### ⚠⚠ Ojo: el Code Optimization NO está en git

Vive en `Library/EditorUserBuildSettings.asset`. **Si se borra la `Library`, vuelve a `BuildTimes`**
(el valor por defecto, el que engorda el `.wasm`) y el problema regresa sin que nadie entienda por
qué. Antes de cada build de release: menú **`Appquarium TV → 📏 Ver Code Optimization del WASM`**,
o llamar a `TvWasmOptimize.SetDiskSizeLTO()`.

## 7. Descartado ayer con evidencia (no volver a perseguirlo)

- **El vídeo keepalive NO es la fuga** — A/B con `noKa`: sin vídeo fugaba igual (+25 MB/min).
- **`ProduceSkia: incompatible mailbox` es del vídeo, no de Unity** — 0 apariciones sin vídeo.
- **La fuga no es el gatillo del corte** — RUNG 7: con Unity destruido la memoria BAJA y aun así
  cortó a 174 s. El gatillo es la presión sostenida por debajo del 25 %.
- **No es "cap duro del device" ni "engine core infixeable"** (veredictos del 17 y 20 de julio).

## 8. Discrepancia pendiente de resolver

La memoria del proyecto dice que `FishUnlit` y `PlanarShadow` se reescribieron en **CG legacy**
porque el HLSL de URP no ejecutaba en el device. Hoy, en disco, **los tres shaders propios usan
HLSL de URP**. O se revirtieron en algún sync, o la nota era parcial. Afecta a la viabilidad de
salir de URP (§4).
