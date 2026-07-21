# ▶▶ EMPEZAR AQUÍ — sesión 2026-07-22 · Cast disconnect ~3min

> Handoff turnkey. Contexto completo en `CAST_DISCONNECT_INVESTIGATION.md`.
> Escrito al cierre de la sesión del 2026-07-21.

---

## 1. Qué pasó ayer (21-jul) en dos líneas

- **RUNG 22 (escena vacía) CORTÓ a 217.4 s** → el corte **no depende del contenido de la escena**.
- **PERO el veredicto "es el engine core, infixeable" era FALSO** y está corregido en el doc:
  el build "vacío" NO era un Unity mínimo (`.wasm` 44.249.290 B vs 44.250.183 B de prod — seguía
  llevando todo nuestro C#, Addressables y URP). Solo se vació la escena.
- **La investigación NO está cerrada.** Un research web posterior abrió frentes nuevos y mejores.

---

## 2. 🔑 Lo que cambió el panorama (research 2026-07-21)

### H1 · Google declara que WebGL NO está soportado en el Web Receiver
UX Guidelines oficiales, literal: *"The Web Receiver is a Chrome browser optimized for video
playback. As such, WebGL and Chrome Native Client (NaCL) are not currently supported"*.
https://developers.google.com/cast/docs/ux_guidelines — **confianza ALTA**.
→ Explica por qué **no existe ni un caso reportado** de este combo en foros/GitHub/SO: nadie lo hace.
→ Estamos fuera de contrato. "Comportamiento no definido", no un bug con fix garantizado.

### H2 · ⭐ HIPÓTESIS PRINCIPAL — el watchdog de Cast mide memoria del SISTEMA, no del heap
`chromecast/browser/cast_memory_pressure_monitor.cc`:
`kCriticalMemoryFraction = 0.25`, `kModerateMemoryFraction = 0.4`, `kPollingIntervalMS = 5000`.
Calcula la fracción de **`MemAvailable` de `/proc/meminfo`** (memoria del sistema), no del proceso.
https://chromium.googlesource.com/chromium/src/+/64.0.3282.104/chromecast/browser/cast_memory_pressure_monitor.cc

> **Nuestros 3 indicadores de "receiver sano" (heap WASM 64 MB, heap JS 98 MB, lag del hilo <250 ms)
> son CIEGOS a esto.** No incluyen memoria GPU/gralloc ni el proceso GPU. Unity puede estar agotando
> la memoria del sistema con texturas/FBO sin que el heap JS se mueva un byte.

→ **Encaja perfectamente con el síntoma "muere estando sano".** Es la mejor hipótesis viva.

### H3 · `maxInactivity: 3600` es contraproducente
Doc oficial: `maxInactivity` = *"Maximum time before closing an idle sender connection. Setting this
value **enables a heartbeat message** to keep the connection alive"* (default 10 s). `core_features`
recomienda **no fijarlo** en producción.
→ Ponerlo a 3600 **desactiva de facto la detección de senders muertos durante 1 h** y afloja el
heartbeat del SDK. No es la causa del corte, pero es **ruido de diagnóstico** y puede estar tapando
la razón real. ⚠ Contradice lo que dice hoy `CLAUDE.md` y `feedback_cast_maxinactivity.md` — revisar.

### H4 · El heartbeat de transporte vive FUERA de nuestro JS
`urn:x-cast:com.google.cast.tp.heartbeat`: PING/PONG cada ~5 s gestionado por el **proceso nativo**
(`mediashell`), no por el renderer.
→ Explica por qué bloquear el hilo 18 s (RUNG 14) no lo rompió, y por qué CAF nunca da razón:
**la conexión la tira la capa nativa/plataforma, no la app.**

### H5 · Punto ciego: eventos que NUNCA hemos logueado
`cast.framework.system.EventType` incluye **`STANDBY_CHANGED`** y **`VISIBILITY_CHANGED`** (además de
`ERROR`, `MAX_VIDEO_RESOLUTION_CHANGED`). Son justo los que dispararía un salvapantallas / ambient
mode / HDMI-CEC. **No los capturamos.**

### H6 · El debugging en Android TV NO es el puerto 9222
Es `adb connect <IP>:4321` (o `5555`) + `chrome://inspect`, más `adb logcat` en paralelo.
Proceso a vigilar: `com.google.android.apps.mediashell`.
https://developers.google.com/cast/docs/android_tv_receiver/debugging
⚠ Google avisa: dejar el remote debugger enganchado mucho rato agota recursos del receiver y puede
provocar fallos → perturba la medición. Usarlo lo justo.

### H7 · 🎯 CORRECCIÓN IMPORTANTE — "cambiar de stack" NO es reescribir en three.js
Existe **Cast Connect** (`https://developers.google.com/cast/docs/android_tv_receiver`): una **app
nativa Android TV** donde **Unity corre como app Android normal** (se reaprovecha el proyecto entero)
y Cast solo transporta el estado. **Sin WebGL, sin Web Receiver, sin este ciclo de vida.**
Es el camino que Google documenta para contenido 3D. Mucho menos brutal que lo que se dijo antes.

### H8 · Pistas débiles pero baratas de descartar
- Salvapantallas de Android TV matando receivers no-media: caso wahoo-results #229 (workaround =
  developer options → **Stay awake**) y caso Symfonium **en un Xiaomi Android TV** resuelto
  desactivando el salvapantallas. **Confianza BAJA-MEDIA** (no explica la varianza 150-220 s), pero
  cuesta 2 min probarlo.
- **No hay pinning de versión** del CAF SDK: solo `v3` vs `preview`. A/B barato, poco probable.
- **powerPreference DESCARTADO**: es solo un hint para sistemas multi-GPU; en un SoC de GPU única no
  aplica. Coherente con RUNG 17.
- **WebGL 1.0 NO es una palanca disponible**: Unity 6 eliminó WebGL1/GLES2 (desde 2023.1). Solo WebGL2.

### H9 · Lo que el research NO encontró (explícito, sin relleno)
- Cero casos públicos de "Unity WebGL como Cast receiver" con desconexión a los minutos.
- **Ningún watchdog documentado de ~180 s**, ni en docs de Google ni en el código `chromecast/`.
- Ningún límite de heap publicado para receivers en Cast Built-In. Solo "sé ligero".
- Ninguna nota de changelog del Cast SDK sobre desconexiones/heartbeat/memoria.

---

## 3. PLAN DE MAÑANA — lo barato primero, sin rebuilds

> Los rebuilds (Unity mínimo, knobs de Player Settings) **bajan al final**. Antes hay 3 tests gratis
> y el primero puede resolverlo.

### 🔴 BLOQUEO ACTUAL — activar depuración por red en la caja
Aquí nos quedamos. La caja responde a ping en **`192.168.1.47`** ✅ pero **adb está cerrado**
(`connect` falla en 4321 y 5555).

Ruta en el Xiaomi TV Box S (el user se perdió porque el botón del mando abre **"Ajustes rápidos"**,
un panel lateral):
1. En el panel de Ajustes rápidos, **bajar del todo** → **"Todos los ajustes"** (engranaje).
2. **Preferencias del dispositivo** (o **Sistema**, si el firmware es Google TV) → **Acerca de**
   → bajar a **"Compilación"** → pulsar **7 veces** → *"Ya eres desarrollador"*.
3. Volver → **Opciones para desarrolladores** → activar **Depuración por USB** + **Depuración por red**.
   (Si solo existe "Depuración por USB", activarla igual y reintentar `adb connect` — en muchos
   builds de Android TV abre el 5555 automáticamente.)
4. Al lanzar `adb connect` sale un **diálogo de autorización en la TV** → aceptar + "permitir siempre".

⚠ **Stay awake y salvapantallas: NO tocarlos todavía.** Van después de la primera captura, para no
mover dos variables a la vez.

### TEST A ⭐ (el importante) — captura forense con adb
**Script ya escrito y listo: `Tools/cast-adb-capture.sh`.**
```bash
bash Tools/cast-adb-capture.sh            # usa 192.168.1.47 por defecto
# luego: castear desde http://localhost:3003 en RUNG 2, no tocar nada
# al cortar, esperar ~10s y Ctrl+C → imprime el análisis solo
```
Captura en paralelo a `_cast_adb_capture/`:
- `logcat.log` — filtrado al parar por `lowmemorykiller|lmkd|kill|SIGKILL|mediashell|MemoryPressure|Renderer`
  → **responde quién mata la sesión**: renderer reciclado / `mediashell` matado por LMK / stop ordenado.
- `meminfo.log` — `MemTotal/MemAvailable/MemFree` **cada 2 s** (el watchdog muestrea a 5 s)
  → **si `MemAvailable` cruza el 25% del total justo antes del corte, H2 CONFIRMADA.**
- `procs.log` — PIDs de `mediashell`/chrome cada 5 s → si reaparece con otro PID, lo reciclaron.

**Cómo leerlo:** correlacionar la hora del corte (la da el panel del sender) con las tres pistas.

### TEST B (gratis, sin rebuild) — cerrar el punto ciego + quitar el ruido
Editar `webgl-output/index.html` y **subir con boto3** (⚠ NUNCA copiar el template — ver `CLAUDE.md`):
1. Loguear `STANDBY_CHANGED`, `VISIBILITY_CHANGED`, `ERROR` (H5). Si alguno precede al corte, cambia
   el diagnóstico entero.
2. **Quitar `maxInactivity: 3600`** y dejar el default de Google, manteniendo `disableIdleTimeout: true`
   (H3). Ver si con el default aparece por fin una razón de desconexión útil.
⚠ Hacerlo **después** del TEST A, para que la captura A refleje la config actual sin mezclar cambios.

### TEST C (2 min) — descartar el salvapantallas (H8)
Opciones de desarrollador → **Permanecer activo / Stay awake** ON.
Ajustes → **Salvapantallas** → *Nunca*. **Reiniciar la caja** después. Recastear.

### DESPUÉS, si A/B/C no resuelven — los caros
- **F2 · Unity MÍNIMO de verdad** — proyecto Unity **nuevo y limpio** (hello-world, Built-in RP, sin
  Addressables, sin nuestro C#), objetivo `.wasm` ~8 MB. **No toca este repo.** Unity 6000.3.10f1 ya
  está instalado, no hay que descargar nada.
  - CORTA → sí es el engine core → escalar a Unity/Google o ir a Cast Connect (H7).
  - AGUANTA → **es nuestro build** (tamaño del wasm, nuestro C#, Addressables, URP) → HAY fix.
- **F3 · Knobs de Player Settings sobre el rig vacío** (1 rebuild ~1 h cada uno):
  **Disable Unity Audio** es el de mejor hipótesis — RUNG 16 vio que Unity crea un `AudioContext` real
  a los ~23 s; RUNG 4 solo lo *suspendía después* de creado y RUNG 17 lo creó *sin* engine.
  **Nunca se ha probado un Unity que jamás lo cree.** Luego: Initial Memory 32 MB, Maximum Memory
  Size <512 MB, renderScale 0.5 (todos atacan H2).
- **F4 · Otra versión de Unity** (2022 LTS ⇒ emscripten distinto). Caro, pero es un eje real.
- **F5 · A/B `v3` vs `preview` del CAF SDK.** Barato, poco probable.

---

## 4. Estado del entorno al cerrar (21-jul)

| Cosa | Estado |
|---|---|
| R2 `/index.html` | **Rollback hecho**, verificado byte-idéntico al backup previo. Sigue siendo el receiver de DIAGNÓSTICO (`rcv-prod-config.html`, panel debug + 22 rungs). |
| R2 `/Build/webgl-output.*` (PROD) | **INTACTO** — 44.250.183 B, 23-jun. ETag verificado antes y después del deploy. Nunca se tocó. |
| R2 `/Build/webgl-output-empty.*` | Subido (4 ficheros). **Inofensivo** — nadie los carga salvo `rcv-empty-test.html`. Borrar cuando se quiera. |
| Backup del receiver | `scratchpad/r2-index-backup-2026-07-21.html` (112 KB). ⚠ El que citaba el spec viejo (`...KA9probe.html`) **NO existía**; este se creó descargando el vivo de R2. |
| Local `webgl-output-empty/` | Existe, gitignorado. **Reutilizable como rig** para los knobs de F3. |
| `Assets/_EmptyCastTest/` | Escena vacía, regenerable con el menú. |
| Sender node en `localhost:3003` | **Arrancado en background.** Log en `scratchpad/sender3003.log`. Matar o relanzar según haga falta. |
| adb | Disponible en `C:\Users\Behere\AppData\Local\Android\Sdk\platform-tools\adb`. Caja **no conecta** (depuración cerrada). |
| Xiaomi TV Box S | `192.168.1.47`, responde a ping. |
| `Tools/cast-adb-capture.sh` | **Nuevo, listo para usar.** |

**Commits de la sesión** (branch `feat/netflix-architecture`, **sin push**):
- `088422a` — harness de bisección + docs + `TvEmptyTestBuild.cs` (punto de restauración).
- `9bd0eda` — resultado RUNG 22.
- (este doc + corrección del veredicto + `cast-adb-capture.sh`).

⚠ **Pendiente independiente de todo esto:** el `index.html` vivo en R2 es un receiver de
**diagnóstico**. Antes de cualquier uso real hay que desplegar uno limpio (auto-hide del panel, sin
los 22 rungs). No bloquea la investigación.

---

## 5. Postura a mantener

El user **rechaza la reconexión cada ~3 min** como solución, con razón. La investigación sigue
abierta y quedan frentes reales. Pero conviene tener presente H1 y H7: puede que el final honesto de
esto no sea "arreglar el Web Receiver", sino **mover Unity a una app nativa Android TV con Cast
Connect** — que reaprovecha el proyecto entero y es el camino soportado. Eso **no** es tirar el
trabajo a la basura.
