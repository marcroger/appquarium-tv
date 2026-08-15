# Cast Disconnect Investigation — Appquarium TV

> Iniciada: 2026-06-27 | Última actualización: **2026-07-27**  
> Branch activo: `feat/netflix-architecture`

---

# 🏆 2026-07-27 — **FIX VALIDADO EN EL RIG: 660 s SIN CORTARSE** (antes 239 s)

> ▶▶ Para retomar: **`CAST_NEXT_SESSION_2026-07-28.md`**. Falta solo medir el acuario real.

Dos cambios, ninguno toca gameplay ni calidad visual:

1. **7 paquetes de runtime eliminados** del `manifest.json`, ninguno referenciado por el código de
   TV: `purchasing` (IAP), `visualscripting`, `inputsystem`, `mobile.notifications`, `timeline`,
   `ai.navigation`, `postprocessing` (v2 legacy). Verificado `activeInputHandler: 0` antes de tocar.
2. **Code Optimization de WebGL: `BuildTimes` → `DiskSizeLTO`.** Estaba en el valor por defecto de
   Unity, el que produce el `.wasm` más grande. **No está en git** (vive en
   `Library/EditorUserBuildSettings.asset`), por eso nunca se auditó pese a tener documentados
   stripping, IL2CPP y memoria. Confirmado en el log: `[WasmOpt] BuildTimes → DiskSizeLTO`.

| Rig vacío | Antes | **Después** |
|---|---|---|
| `.wasm` | 44.249.290 B | **25.428.426 B** (−42,5 %) |
| `.data` | 20.814.692 B | **16.902.606 B** (−18,8 %) |
| Pico del renderer | 794 MB | **653,6 MB** |
| Renderer en régimen | 400-500 MB | **239 MB** |
| Fuga `Native Heap` | +18,8 MB/min | **+0,1 MB/min** |
| `MemAvailable` en régimen | 6-10 % | **23-24 %** |
| **Duración** | **239 s ❌** | **660,7 s ✅ (límite del sender, 0 crashes)** |

La sesión terminó porque el sender manda `STOP` al llegar a su límite de 11 minutos, no por caída.
**Primera vez en toda la investigación que hubo que parar una sesión porque no se moría sola.**

### La fuga también desapareció, y no era URP

De 18,8 a 0,1 MB/min. La hipótesis era URP — **falsa**, URP sigue instalado. La fuga venía de alguno
de los 7 paquetes eliminados. Sospecha no confirmada: `inputsystem`, que sondea dispositivos cada
frame aunque no haya nada que leer. **No aislado, no darlo por hecho.**

### Estado: el acuario real está construido y desplegado, SIN MEDIR

Build de producción con las dos palancas: `.wasm` 25.430.429 · `.data` 16.874.702 (56 min, 0 errores).
Desplegado a R2 junto con el catálogo. ⚠ **El player de junio ya solo existe en
`scratchpad/prod-backup-2026-07-27/`.** Falta castearlo: la caja entró en standby y cierra `adbd`.

---

# ✅✅✅ 2026-07-27 — **UN UNITY MÍNIMO NO CORTA.** El problema es NUESTRO BUILD, y tiene arreglo.

Proyecto Unity **nuevo y limpio** (hello-world, Built-in RP, sin Addressables, sin una línea de
nuestro C#), construido en batchmode con los mismos Player Settings, desplegado a R2 y casteado con
la misma captura forense:

| | Nuestro build (acuario) | Nuestro build (escena vacía) | **Unity MÍNIMO** |
|---|---|---|---|
| `.wasm` | 44.250.183 B | 44.249.290 B | **10.632.074 B** |
| `.data` | 20.814.692 B | 20.814.692 B | **2.926.283 B** |
| Pico del renderer | 778-797 MB | 794 MB | **380,9 MB** |
| Fuga `Native Heap` | +20 a +26 MB/min | +18,8 MB/min | **+0,1 MB/min** |
| mín. `MemAvailable` | 6-10 % | 6 % | **17 %** |
| Duración | 148-274 s ❌ | 239 s ❌ | **>660 s ✅ SIN CAÍDA** |

**Unity WebGL SÍ cabe en el Xiaomi TV Box S.** No es la plataforma, ni Cast, ni WebGL, ni el motor.
Queda REFUTADO el veredicto que arrastraba la investigación desde el 20-jul ("el disparador es
intrínseco al motor WASM de Unity, no hay fix app-side"): sí lo hay, y es cuantitativo.

**Desaparecen los DOS síntomas a la vez** — el pico se reduce a menos de la mitad y la fuga
prácticamente a cero. Ambos los mete nuestro build, no el engine.

## Presupuesto de memoria (objetivos medidos, no estimados)

| Métrica | Ahora | Objetivo | Recorte |
|---|---|---|---|
| `.wasm` | 44,2 MB | ≤ ~15 MB | **−33,6 MB** de C# + URP + Addressables |
| Pico del renderer | ~795 MB | < 500 MB | −300 MB |
| `MemAvailable` mínimo | 6-10 % | **> 25 %** | no cruzar el umbral crítico de Cast |
| Fuga | +20 MB/min | ~0 | eliminar |

## ⭐ Sospechoso nº1 de la fuga: URP

Nuestra **escena vacía fugaba 18,8 MB/min sin ejecutar un solo script nuestro**. Con cámara, cubo y
luz no corre ningún `MonoBehaviour` de gameplay: lo único que trabaja cada frame es el render
pipeline. Y ahí está la diferencia entre los dos proyectos — el mínimo usa **Built-in RP**, el
nuestro **URP 17.3.0** configurado a nivel de proyecto.

A/B en curso: proyecto mínimo + URP 17.3.0, misma escena (fondo naranja para distinguirlo del verde).

## Herramientas nuevas que hicieron esto posible (2026-07-27)

- **`Tools/cast-headless.js`** — sender Cast **sin navegador**: habla el protocolo Cast v2 (TCP+TLS
  8009) con `castv2`, lanza el App ID, manda `RUNG_CONFIG` y recibe el log del receiver por el canal.
  Elimina el único paso que exigía un humano (el selector nativo de dispositivos de Chrome).
- **`Tools/cast-run.sh <rung> [etiqueta]`** — ciclo completo sin intervención: reinicio por adb →
  espera de asentamiento (≥40 % y 0 matanzas del lmkd) → 4 sondas → cast → análisis automático.
  `FREE_MEM=1` hace force-stop de apps de fondo antes de castear.
- **`Tools/status-server.js`** — panel en `http://localhost:3005`, se refresca solo.
- **`D:\dev\_unity-min-test\`** — proyecto Unity mínimo + scripts de build en batchmode. Fuera del repo.

## Otros resultados del 2026-07-27

- **RUNG 7 (Unity destruido a los 15 s):** con Unity muerto el `Native Heap` **decrece** (111 → 82 MB)
  ⇒ la fuga es del bucle de render de Unity. Pero la sesión murió igual a los 174 s ⇒ **la fuga no es
  el gatillo**.
- **Gatillo real = presión crítica sostenida.** En el crash del RUNG 7 `MemAvailable` llevaba 2,5 min
  estable en 20-21 %, no cayendo. Encaja con `kCriticalMemoryFraction = 0.25` del monitor de presión
  de Cast, y explica las 7 duraciones (148-274 s) sin necesidad de la fuga.
- **Dos formas de morir:** crash del renderer (`crashpad`/`tombstoned`) en 6 runs; en el run con
  memoria liberada la plataforma **retiró la app limpiamente, sin crash**.
- **`FREE_MEM=1`** (force-stop de apps de fondo) dio la sesión más larga con nuestro build: **274,6 s**.

---

# 🎯🎯🎯 CAUSA RAÍZ ENCONTRADA — 2026-07-27 (TEST A, captura forense adb)

**El renderer de Chromium que ejecuta nuestro receiver SE ESTRELLA por agotamiento de memoria del
sistema. El crash es SILENCIOSO porque `crashpad` y `tombstoned` están rotos en este device.**

Todo lo que llevábamos meses observando ("el receiver muere estando 100% sano", "CAF no da razón",
"la conexión se tira desde la plataforma") era el síntoma de un proceso que desaparece de golpe.
Nuestros indicadores (heap WASM 64 MB / heap JS 98 MB / lag del hilo) eran **ciegos** a esto:
medían el interior del renderer, no su coste real para el sistema. **H2 del research CONFIRMADA.**

## Cómo se obtuvo

Se activó por fin la depuración por red en la caja (bloqueo que arrastrábamos desde el 21-jul) y se
lanzó `Tools/cast-adb-capture.sh`: `logcat` + `MemAvailable` del sistema cada 2 s + RSS de los
procesos de `mediashell` cada 5 s, mientras se casteaba RUNG 2 (Unity ON) desde el harness del PC.

⚠ **La caja había cambiado de IP: `192.168.1.33`, no `192.168.1.47`** (DHCP). Toda la doc anterior
apunta a la IP vieja. Verificar siempre con `curl -s http://<IP>:8008/setup/eureka_info | grep name`.

## Device (medido, no supuesto)

| Dato | Valor |
|---|---|
| Modelo | `MiTV-AFMU0` (`twilight`) · Android **14** (SDK 34) · build `UKG3.250826.001` |
| `MemTotal` | **1.963.668 kB (1,92 GB)** |
| `MemAvailable` en reposo, recién reiniciada | 629.360 kB = **32 %** |
| Umbral *moderado* del watchdog Cast (40 %) | 785.467 kB |
| Umbral **crítico** (25 %) | **490.917 kB** |

La caja **ya arranca por debajo del umbral moderado**. Le sobran ~139 MB para cruzar el crítico.

## Timeline del corte (run del 2026-07-27, 236 s)

```
10:04:58.98  App starting: app_id=8F6C873F (Appquarium), session_id=40bc6edb
10:05:01     renderer  92 MB · GPU 112 MB          (Unity empezando a cargar)
10:05:15     ▼ PRIMERA OLEADA lowmemorykiller
10:05:34     renderer 778,7 MB ← PICO  · GPU 118 MB   (instanciación del WASM)
10:05:15-54  lmkd mata ~18 procesos: tvhome, dreamx, launcherx, youtube.tv,
             netflix.ninja, vending, gapps, gms.unstable, keychain, acore,
             permissioncontroller, providers.tv, gservices, tv.settings…
             razones: "low watermark is breached and swap is low"
                      "device is not responding"
10:05:26     MemAvailable cruza el CRÍTICO: 451.536 kB (22 %)
10:06:08     renderer 274 MB · GPU 283 MB   (asentado tras el pico)
10:07:15     renderer 280 MB · GPU 276 MB
10:08:21     renderer 334 MB · GPU 276 MB   ← FUGA sostenida ~+50 MB/min
10:08:54     renderer 392-401 MB · GPU 273 MB   (último tick con vida)
10:08:55.438 F/crashpad(8435): dlopen failed: library "libicu.so" not found
10:08:55.440 E/libc(6497): failed to connect to tombstoned: Operation not permitted
             ↑ pid 6497 = mediashell:sandboxed_process0 = NUESTRO RENDERER
10:08:59     procesos del receiver desaparecidos. MemAvailable se recupera al 45 %.
```

`MemAvailable` estuvo **82 ticks en CRÍTICO** y 63 en moderado; mínimo observado **206.564 kB (10 %)**.

## El mecanismo, en orden

1. **Pico de carga: el renderer llega a 778,7 MB** instanciando el WASM de 44 MB. En una caja de
   1,92 GB eso es demoledor.
2. **El `lowmemorykiller` destripa el sistema** para hacerle sitio: ~18 procesos muertos, swap
   agotada (`swap is low (1052kB < 64800kB)` en el peor momento).
3. El receiver se asienta en ~275 MB (renderer) + ~276 MB (GPU) = **~550 MB permanentes**, con el
   sistema ya sin colchón y por debajo del umbral crítico del watchdog.
4. **Fuga sostenida de ~50 MB/min en el renderer** (274 → 392 MB en 2 min). El proceso GPU NO crece.
5. Sobre los 400 MB, con el sistema exhausto, **una asignación falla y el renderer se estrella**.
6. `crashpad` no puede ni cargar sus librerías (`libicu.so`) y `tombstoned` rechaza la conexión →
   **no hay tombstone, no hay stack, no hay reporte**. El crash es invisible desde dentro y desde CAF.
7. La sesión Cast cae porque el proceso que la sostenía ya no existe.

## Segundo hallazgo: error de compositing GPU cada frame

```
E/cast_shell(6519): [ERROR:shared_image_manager.cc(221)] SharedImageManager::ProduceSkia:
                    Trying to produce a Skia representation from an incompatible mailbox.
```

Se repite **cada ~65 ms (por frame)** durante toda la sesión, desde el proceso GPU. El buffer que
produce WebGL no es compatible con lo que Skia espera al componer. Sospechoso directo de la fuga.

## Qué explica esto de todo lo anterior

| Observación histórica | Explicación |
|---|---|
| "Receiver 100% SANO al morir" | Sano por dentro; el proceso entero desaparece de golpe |
| CAF nunca da razón de desconexión | El renderer se estrella; no queda nadie que loguee |
| Cortes decrecientes en tests seguidos (198→153 s) | Menos memoria libre acumulada ⇒ se llega antes al fallo |
| Escena vacía cortó igual (RUNG 22, 217 s) | El `.wasm` es el mismo 44 MB ⇒ mismo pico ⇒ mismo final |
| Ningún proxy JS reproduce el corte (RUNGs 10-21) | Ninguno llegó a 778 MB de RSS ni fugaba |
| RUNG 7 (Quit a 43 s) cortó igual | El destrozo del sistema por el lmkd ya estaba hecho |
| RUNG 9 (Unity a los 4 min) mató la sesión en 15 s | El pico de carga es el evento letal, llegue cuando llegue |
| Varianza 153-217 s | Es una carrera fuga-vs-memoria-libre, no un temporizador |

**"Cap duro del device" y "engine core infixeable" quedan REFUTADOS.** No hay watchdog de ~180 s:
hay un crash por memoria cuyo instante depende de cuánta queda.

## Por qué ahora SÍ hay fixes

El problema es **cuantitativo** (huella de memoria), no un límite arbitrario de plataforma. Dos
frentes independientes, ambos accionables:

**A · Bajar el pico de carga (778 MB).** Es lo que destripa el sistema.
- `.wasm` de 44 MB → más stripping, quitar URP si sobra, quitar Addressables del arranque.
- `Initial Memory Size` 64 MB → 32 MB · `Maximum Memory Size` 512 MB → menos.
- Revisar si la descompresión del `.data` duplica memoria en el pico.

**B · Encontrar la fuga de ~50 MB/min.** Sin ella la sesión duraría indefinidamente aunque el pico
siga siendo alto (a 275 MB estables llevaba minutos sin morir).
- Sospechoso nº1: **el bucle del vídeo keepalive** (recarga cada ~10 s vía `REPEAT_SINGLE`) —
  y es **código nuestro, eliminable**. Encaja con el spam de `ProduceSkia` por frame.
- Sospechoso nº2: nuestro panel de debug / streaming de logs al sender.
- Sospechoso nº3: acumulación de shared images en el compositing WebGL↔Skia.

## Siguiente medición (gratis, ya con adb)

`dumpsys meminfo <pid del renderer>` cada 10 s → desglosa la fuga por categoría (Native heap /
Graphics / GL mtrack / EGL mtrack). Eso dice **qué** crece, no solo que crece. Y A/B inmediato:
misma captura con el **vídeo keepalive desactivado** para confirmar o descartar al sospechoso nº1.

## RUN 2 (2026-07-27, mismo día) — LA FUGA LOCALIZADA + confirmación del modelo

Segunda captura, misma config (RUNG 2, receiver `rcv 2026-07-17 KA9-probe` verificado byte-idéntico
entre R2 y local), añadiendo `dumpsys meminfo` cada 10 s. Caja reiniciada por adb antes de empezar.

| | Run 1 | Run 2 |
|---|---|---|
| `MemAvailable` al arrancar | 32 % | **41,7 %** |
| Pico del renderer al cargar | 778,7 MB | **797 MB** |
| RSS del renderer al morir | 392-401 MB | **493,7 MB** |
| Duración | 236 s | **243 s** |
| Firma del crash | `crashpad` + `tombstoned` | **idéntica** (pid 5586) |

**Con más memoria libre, el renderer creció más y la sesión duró más.** El instante de la muerte lo
fija la memoria disponible, no un reloj. **Reproducible al megabyte** (778,7 vs 797 MB de pico).

### La fuga está en el `Native Heap` del RENDERER, y es lineal

```
10:25:37   46,7 MB      10:27:17   79,7 MB
10:26:11   55,3 MB      10:27:50   92,7 MB
10:26:44   68,3 MB      10:28:12   98,9 MB
→ +52 MB en 155 s = +20 MB/min, sin una sola meseta
```

- **`Graphics` del renderer = 0** — no es memoria gráfica en el renderer.
- **`Unknown` oscila 105-310 MB sin tendencia** — es la `WebAssembly.Memory` + transitorios, NO fuga.
- **El proceso GPU está PLANO en ~285 MB** todo el run (`procs.log`) — **no fuga**.

⇒ La fuga son asignaciones C++ del proceso renderer de Chromium (PartitionAlloc / media / Blink),
a ritmo constante. Un ritmo constante apunta a algo **periódico**, y lo único periódico que corre
todo el rato es **el bucle del vídeo keepalive** (`kEnded` → `seek(0)` cada ~10 s).

### Siguiente test decisivo (A/B de una variable)

Nuevo escalón: **Unity ON + vídeo keepalive DESACTIVADO**, misma captura.
- `Native Heap` plano → **el vídeo keepalive es la fuga** → se elimina (es código nuestro, y nació de
  una teoría —"mantener media activa evita el idle timeout"— que RUNG 5 ya refutó).
- Sigue fugando → es churn de Unity/WebGL en el renderer → atacar por huella (F3).

⚠ Bug del arnés detectado y corregido: dentro del bucle de `dumpsys`, `adb shell` se comía el stdin
y solo dumpeaba el primer proceso. Arreglado con `< /dev/null` (por eso el proceso GPU del run 2 se
midió por `procs.log` y no por `dumpsys`).

## RUNG 23 (2026-07-27) — ❌ EL VÍDEO KEEPALIVE **NO** ES LA FUGA. Hipótesis REFUTADA.

A/B limpio: producción entera con `noKa:true` (el receiver no carga el clip). Verificado por eventos
de media en logcat, no por fe:

| Evento de media | Sesión con vídeo | RUNG 23 (sin vídeo) |
|---|---|---|
| `kLoad` | 18 | **3** |
| `kWebMediaPlayerCreated` | 18 | **3** |
| `kBufferingStateChanged` | 95 | **5** |
| `kPipelineStateChange` | 66 | **9** |

El bucle estaba efectivamente muerto (`kSuspended` en el pipeline). **Y la fuga siguió igual:**

```
Native Heap del renderer, SIN vídeo:
11:01:05   87,8 MB      11:01:39  101,1 MB
11:01:16   93,3 MB      11:01:50  105,3 MB
11:01:28   98,3 MB      11:01:55  109,0 MB
→ +21 MB en 50 s = +25 MB/min   (con vídeo era +20 a +26 MB/min)
```

**Duración 148 s** — la más corta de las cuatro. Quitar el vídeo no ayudó en absoluto.

⇒ **La fuga es intrínseca a Unity ejecutándose en el renderer**, no al keepalive. El sospechoso nº1
queda descartado con evidencia directa.

### ⚠ Dos bugs propios que costaron un run entero (lección)

1. `window.__noKa` se definió **fuera del closure de `ctx`** → `ctx is not defined` → el `pm.stop()`
   nunca corrió y el clip siguió en bucle toda la sesión con el log diciendo `noKa=true`. Arreglado
   usando `cast.framework.CastReceiverContext.getInstance()`.
2. `kaLoad()` salía en `SENDER_CONNECTED` (~0,2 s) **antes** de que llegara `RUNG_CONFIG` (~0,3 s):
   con `REPEAT_SINGLE` el clip ya quedaba en bucle para siempre. Arreglado retrasando el load 1,5 s.

**Lección de método:** no dar por bueno un flag porque el log diga que está activo — verificar el
EFECTO en el sistema (aquí, los eventos de media del logcat). Ver [[feedback-verify-diagnoses-with-logs]].

## Tabla de los 4 runs con adb (2026-07-27)

| Run | Config | Mem libre inicial | Duración | RSS al morir | Fuga Native Heap | mín. MemAvailable |
|---|---|---|---|---|---|---|
| 1 | producción | 32 % | 236 s | 401 MB | (no medida) | 10 % |
| 2 | producción | 41,7 % | 243 s | 494 MB | +20 MB/min | — |
| 3 | producción | 48 % | 186 s | 360 MB | +26 MB/min | 7 % |
| 4 | **sin vídeo KA** | 43 % | **148 s** | 387 MB | **+25 MB/min** | 7 % |

**La duración NO escala limpiamente con la memoria libre inicial** (el run 3 arrancó con la mejor
cifra y fue de los más cortos): lo que cuenta es la demanda total del sistema en ese momento, no el
número de partida. El mecanismo sí es idéntico en los 4: mismo crash del renderer, misma firma
`crashpad`+`tombstoned`, `MemAvailable` desplomado al 7-10 %, matanzas del lmkd.

## ▶ SIGUIENTE — medir el build de ESCENA VACÍA con adb (0 rebuilds, ya está desplegado)

`Build/webgl-output-empty.*` sigue en R2 y `Tools/rcv-empty-test.html` lo carga. Castearlo con la
captura adb responde la pregunta que ahora importa:

- **Pico y fuga IGUALES** → es el engine/runtime de Unity ⇒ atacar por Player Settings (Initial
  Memory 32 MB, Max Memory, Disable Unity Audio, stripping, wasm más pequeño) sobre el rig vacío.
- **Sin fuga (o mucho menor)** → la fuga la mete **nuestro contenido/C#** ⇒ hay que bisecar la escena,
  y esta vez con un instrumento que sí ve el problema (RSS del sistema, no el heap del panel).

Es el test de mayor valor por coste cero que queda.

## ESCENA VACÍA + adb (2026-07-27) — 🎯 ES EL ENGINE, NO NUESTRO CONTENIDO

Mismo test que RUNG 22 (build de escena vacía: cubo azul, confirmado en la TV) pero **midiendo el
sistema con adb** en vez de solo cronometrar. Receiver `rcv 2026-07-27 EMPTY-mem`, vídeo keepalive
ON para ser comparable con producción.

| | Producción (runs 1-3) | **Escena VACÍA** |
|---|---|---|
| Pico del renderer | 778 · 797 MB | **794 MB** |
| Fuga `Native Heap` | +20 a +26 MB/min | **+18,8 MB/min** (55,1 → 99,4 MB en 141 s) |
| Duración | 186-243 s | **239 s** |
| RSS al morir | 360-494 MB | 479 MB |
| mín. `MemAvailable` | 7-10 % | **6 %** |
| Firma del crash | `crashpad`+`tombstoned` | **idéntica** |

**Un cubo y una luz consumen exactamente lo mismo que el acuario entero con 25 peces.**

⇒ El pico y la fuga son del **engine/runtime de Unity**, no de nuestra escena, assets, shaders,
bundles ni C# de gameplay. Bisecar `TvScene` no habría servido de nada — y ahora se sabe con una
medición, no por descarte.

⇒ **El rig vacío es un banco de pruebas válido y barato** para los knobs de Player Settings: se
comporta igual que producción y se rebuildea en ~1 h en vez de las 2-4 h del acuario.

### Dato de severidad para producto

El `lowmemorykiller` mata el **launcher de Android TV** (`launcherx` + `:coreservices`), GMS, Play
Services y providers del sistema. Al usuario le parece que **la caja se ha reiniciado sola** (pantalla
en negro y home recargándose), aunque `system_server` (pid 692) sobrevive: no es un reboot real.
**Nuestro receiver degrada el device entero, no solo su propia sesión.**

### ▶ SIGUIENTE test (0 rebuilds, alto valor): ¿la fuga es POR FRAME?

Correr **RUNG 6 (`throttleRaf` → 4 fps) sobre el rig vacío, con captura adb**.
- Si la fuga cae de ~19 MB/min a ~2-3 MB/min ⇒ **fuga por frame** ⇒ encaja con el error
  `SharedImageManager::ProduceSkia: incompatible mailbox` que aparece **una vez por frame**, y el fix
  pasa por el present path / `renderScale` / framerate.
- Si la fuga no cambia ⇒ es por tiempo, no por frame ⇒ atacar footprint (Initial Memory, wasm).

Después, y solo después, los knobs con rebuild sobre el rig vacío: `Initial Memory` 64→32 MB,
`Maximum Memory` 512 MB→menos, **Disable Unity Audio**, y sobre todo **reducir el `.wasm` de 44 MB**
(el pico de `Native Heap` de 419 MB al instanciar sugiere que compilar ese wasm es el grueso del
pico). Sigue pendiente **F2** (Unity mínimo de verdad) para saber cuánto de esos 44 MB es suelo del
engine y cuánto es nuestro build.

## Artefactos

- Escena vacía en `_cast_adb_capture/`.
- Run 1 en `_cast_adb_capture_run1/`, run 2 en `_cast_adb_capture_run2/`, run 4 (RUNG 23) en
  `_cast_adb_capture/`. ⚠ Los ficheros del run 3 se perdieron (el `mv` falló con la captura anterior
  aún escribiendo); sus números están en la tabla de arriba.
  (logcat.log, meminfo.log, procs.log, memdetail.log, thresholds.txt).
- `Tools/cast-adb-capture.sh` reescrito: IP correcta, umbrales calculados sobre el `MemTotal` real,
  marcado `[MODERADO]`/`[CRITICO]` por tick y RSS de `mediashell` en cada muestra.
  Copia previa en `Tools/cast-adb-capture.sh.bak`.

---

## 🏁🏁🏁 RUNG 21 EJECUTADO (2026-07-20) — EL KITCHEN SINK AGUANTÓ >5:06. SUPERFICIE JS-PROXY AGOTADA. Fin de la bisección receiver-side (21 escalones).

`glCombo`: TODO junto — canvas present 1920x1080 + FBO 1440p + 12 texturas + depth/blend + blit + fenceSync/clientWaitSync por frame. `frames=13507`, **>5:06 sin cortar**, lag plano. → **Ni siquiera la combinación completa a intensidad reproduce el corte.**

### CONCLUSIÓN FINAL DE LA BISECCIÓN (21 escalones)
Excluido, con test A/B de una variable, **TODO lo observable Y reproducible desde JavaScript**: red · contexto WebGL (+atributos+powerPreference+audio+exts) · GPU (192MB texturas) · CPU (1 y N cores) · bloqueo de hilo (hasta 18s) · compilación WASM · memoria WASM · instanciación WASM · fences GPU (clientWaitSync/fenceSync) · FBO render-to-texture 1440p · present surface 1080p · **y la COMBINACIÓN de todo junto (RUNG 21)**. Confirmado sin pthreads (SharedArrayBuffer=false), sin readPixels/finish. Present path = default implícito (renderViaOffscreenBackBuffer:0, explicitSwapControl:0) = idéntico a los proxies.

**→ El disparador es INTRÍNSECO al motor WASM de Unity ejecutándose (runtime emscripten / C# / present real), y NO es reproducible desde JavaScript.** Ningún proxy JS —ni combinado— lo replica. Por RUNG 7, es un daño de una vez al cargar, irreversible. Receiver 100% sano al morir (mem plana, hilo suelto, media PLAYING, CAF sin log). El corte ocurre en la capa de transporte/plataforma del Xiaomi, disparado por algo que SOLO hace el engine compilado real.

### ▶▶ 2026-07-21 EMPEZAR AQUÍ: el user rechaza la reconexión → vamos con RUNG 22 (escena vacía). Spec turnkey en `CAST_EMPTY_SCENE_TEST.md`.
Preparado 2026-07-20: `Assets/Editor/TvEmptyTestBuild.cs` (menú `🧪 Build Empty Cast Test`, rollback-safe) + `Tools/rcv-empty-test.html`. ⚠ Compilación del .cs SIN verificar (MCP timeout) → paso 0 = Console limpia. Rollback = 1 comando (prod /Build/ nunca se toca). Escena vacía CORTA→engine core (infixeable); AGUANTA→nuestro contenido (fixable, bisecar TvScene).

### VEREDICTO: no hay fix receiver-side alcanzable desde JS. 4 caminos, ninguno es un proxy JS:
1. **✅ SHIPPEABLE YA — aceptar + reconexión.** El CastPlugin móvil ya reconecta ~5s; el receiver salta re-INIT. Pulir overlay "Reconectando…". Cierra el problema de cara al usuario. **RECOMENDADO como cierre.**
2. **Test en OTRO device Cast (barato, SIN rebuild):** castear Unity ON (RUNG 2) a un Chromecast/otro Cast target. Si aguanta >4min → es firmware ESPECÍFICO del Xiaomi TV Box S (device-specific); si corta → Cast+Unity fundamental. Solo requiere otro device Cast en la LAN.
3. **Remote Debugger (diagnóstico definitivo):** registrar nº serie del Xiaomi en Cast Console + abrir 9222 → leer el código de cierre REAL del WebSocket. Nombra el mecanismo (GPU process crash / surface lost / heartbeat / etc.). Requiere registro.
4. **Rebuild escena VACÍA (único fork con posible fix de contenido, cuesta 1-3h):** si un Unity de escena vacía TAMBIÉN corta → engine core, infixeable; si NO corta → es nuestro contenido/escena → habría fix.

### ⚠ LIMPIEZA PENDIENTE
- R2 `index.html` = receiver de DIAGNÓSTICO (`rcv-prod-config.html`, panel debug siempre visible + streaming + 21 escalones). **Restaurar receiver de PRODUCCIÓN limpio antes de uso real.** Backup prod Unity ON: `scratchpad/r2-index-backup-KA9probe.html`.
- Harness server node (3003) vivo; matar al cerrar.

---

## ✅ RUNG 20 EJECUTADO (2026-07-20) — AGUANTÓ >6:07. El FBO 1440p tampoco. TODA la huella GL replicada, nada corta solo.

`glFbo`: WebGL2 high-perf + FBO 2560x1440 (color+depth24_stencil8) + draw + blitFramebuffer/frame. `frames=20469`, **>6:07 sin cortar**. → **render offscreen 1440p DESCARTADO.**

**Techo de la replicación individual:** cada op distintiva de la huella GL de RUNG 18 replicada por separado (contexto+audio+exts=17, fences=19, FBO 1440p=20, draws+192MB tex=12) → TODAS aguantan. **Ninguna op aislada de Unity es el disparador.** El disparador es EMERGENTE (la combinación completa a intensidad) o está en el present de emscripten (cómo el frame llega al compositor Cast), no replicado.

**▶ SIGUIENTE — RUNG 21 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20; `glCombo`, kitchen sink):** TODO junto en un render loop — canvas de present 1920x1080 (grande, como Unity) + FBO 1440p + 12 texturas + depth/blend + blit + `fenceSync`/`clientWaitSync` por frame, SIN engine. Receiver R2 `index.html` (112021 bytes), radio RUNG 21 en sender (proxy). Log: `canvas 1920x1080 + FBO 1440p + 12 tex + depth/blend listos` → `frames=..` cada 10s. Corta ~180-216s → es la combinación/intensidad (reproducido → bisecar). Aguanta → es el engine WASM/present de emscripten, NO alcanzable por proxy JS → Remote Debugger / rebuild escena vacía / aceptar reconexión.

---

## ✅ RUNG 19 EJECUTADO (2026-07-20) — AGUANTÓ >5:03. Las fences GPU NO son. Queda el FBO 1440p.

`glFence`: WebGL2 high-perf + 8 texturas + draws + `fenceSync`/`clientWaitSync`×2 por frame. Corrió `frames=16335 fenceSync=16335 clientWaitSync=32670` y **aguantó >5:03**, lag plano. → **32.670 clientWaitSync (bloqueos CPU↔GPU) y NO corta → candidato A (fences GPU) DESCARTADO.**

**▶ SIGUIENTE — RUNG 20 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20; `glFbo`, candidato B):** WebGL2 high-perf + FBO render-to-texture **2560x1440** (color RGBA8 + depth24_stencil8) + draw dentro + `blitFramebuffer` a la pantalla por frame, SIN engine. Receiver R2 `index.html` (106355 bytes), radio RUNG 20 en sender (proxy). Log: `FBO 2560x1440 … COMPLETO` → `frames=..` cada 10s. Reproduce el render offscreen 1440p de Unity (ningún proxy lo hizo; todos dibujaban directo al canvas). Corta ~180-216s → **el FBO offscreen 1440p es el disparador** (fix: renderScale / resolución del RT / offscreen buffer en Unity). Aguanta → el disparador no está en las ops GL observables replicadas → combinación acumulada / present de emscripten / Remote Debugger.

---

## 🔬 RUNG 18 EJECUTADO (2026-07-20) — la huella GL de Unity: `fenceSync`/`clientWaitSync` (GPU sync) + FBO a 1440p. readPixels/finish DESCARTADOS.

Unity ON + contadores GL. Cortó 216s. Fingerprint estable: `fbo=3 rtt=4 rbuf=2 progs=5 tex=15/61MB texStor=20 readPix=0 finish=0 cws=7227 fence=3616 draws=14460`.
- **`readPixels=0 finish=0`** → esas syncs GPU NO se usan → DESCARTADAS.
- **⭐ `fenceSync`+`clientWaitSync` continuos** (~17 fence/s + ~34 cws/s, lineales) → primitivas de sync GPU↔CPU. `clientWaitSync` BLOQUEA CPU esperando GPU → puede serializar con el compositor/present de la plataforma. Ningún proxy (12/17) lo hizo.
- **`1ª renderbufferStorage 2560x1440` + `1ª framebufferTexture2D`** → Unity renderiza a FBO offscreen de **1440p** (>pantalla) y blitea. Proxies dibujaban directo al canvas.
- STALL de 14.6s al arrancar el render (~54s; shaders + 1ª fences). Mem plana 64/98MB.

**→ 2 candidatos NUEVOS: (A) fences GPU (clientWaitSync/fenceSync), (B) FBO render-to-texture 1440p.**

**▶ SIGUIENTE — RUNG 19 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20; `glFence`, single variable = A):** WebGL2 high-perf + 8 texturas + draws reales + `fenceSync`+`clientWaitSync`×2 por frame (como Unity), SIN engine. Receiver R2 `index.html` (100818 bytes), radio RUNG 19 en sender (proxy, unity=false). Log: `contexto + shader + 8 texturas listos` → `frames=.. fenceSync=.. clientWaitSync=..` cada 10s. Corta ~180-216s → **las fences GPU son el disparador**. Aguanta → RUNG 20 = FBO 1440p (B).

---

## ✅ RUNG 17 EJECUTADO (2026-07-20) — AGUANTÓ >9:24. powerPreference + contexto + audio EXCLUIDOS. Es la EJECUCIÓN del engine.

Replicó fielmente: `new AudioContext() state=running sr=48000` + `WebGL2 powerPreference=high-performance + stencil/depth/alpha` + `9/18 extensiones` (las que el device soporta) + render loop. **Aguantó >9:24.** → **Ninguna de las 4 acciones del contexto de RUNG 16 es el disparador**, incluido `powerPreference:"high-performance"` (el candidato estrella del research — FALSADO). El montaje del contexto/audio NO es la causa.

→ El disparador está en lo que Unity **ejecuta** renderizando (shaders, FBOs/render-to-texture, draws con depth/stencil/blending, posible `readPixels`/`finish` = sync GPU) y/o el runtime emscripten. RUNG 17 usó render trivial (`clearColor`, como RUNG 11) → no replicó eso.

**▶ SIGUIENTE — RUNG 18 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20; mismo método que RUNG 16, un nivel más abajo):** espiar las OPERACIONES GL de Unity (Unity ON, `glSpy:true`). Receiver R2 `index.html` (96381 bytes), radio RUNG 18 en sender (unity=true). Log: `🔬 1ª …` (primera ocurrencia de los distintivos) + `🔬 GL#N fbo=.. rtt=.. progs=.. tex=../..MB readPix=.. finish=.. cws=.. draws=..` cada 10s hasta el corte ~198s. Hooks contadores en el contexto: FBOs (`createFramebuffer`/`framebufferTexture2D`), renderbuffers (+MSAA), `linkProgram`, `texImage2D`/`texStorage2D` (+MB), **`readPixels`/`finish`/`clientWaitSync` (sync GPU = sospechosos)**, draws. Resumen cada 10s + 1ª ocurrencia de los distintivos. Revela la huella de render de Unity → replicar el distintivo en RUNG 19. Si sale `readPixels`/`finish`/sync → sospechoso fuerte (fuerzan sync que puede stallar compositing/heartbeat).

---

## 🕵 RUNG 16 EJECUTADO (2026-07-20) — EL ESPÍA DIO CANDIDATOS CONCRETOS. Prime suspect: `powerPreference:"high-performance"`.

Unity ON + hooks. Cortó 180s (esperado). Log `🕵` capturó lo que Unity hace y los proxies NO:
1. **⭐ `canvas.getContext('webgl2', {... powerPreference:"high-performance" ...})`** — los proxies (RUNG 11/12) usaban getContext SIN atributos. Unity pide **high-performance** explícito → path GPU/energía distinto en Android/Cast. **Corrobora la pista del research web (probar powerPreference LowPower).** Atributos completos: `alpha:true depth:true stencil:true antialias:false premultipliedAlpha:false preserveDrawingBuffer:false powerPreference:"high-performance" failIfMajorPerformanceCaveat:false`.
2. **18 `getExtension`** — `WEBGL_debug_renderer_info`, `EXT_disjoint_timer_query(_webgl2)`, `EXT_color_buffer_float`, `EXT_float_blend`, `OES_texture_float_linear`, anisotropic, multi_draw, compressed_texture (astc/etc/etc1/s3tc_srgb), draw_buffers, instanced. Los proxies no pedían ninguna.
3. **`new AudioContext()`** (~23s) — Unity agarra foco de audio real. Proxies nunca. (RUNG 4 lo suspendía TRAS crearlo → no evita el agarre.)
4. **`SharedArrayBuffer=false crossOriginIsolated=false`** → Unity = **WASM de 1 solo hilo, SIN pthreads** → ELIMINA la hipótesis de hilos de fondo (RUNG 13 con workers planos era representativo). ✅
5. `instantiateStreaming` (compile+instantiate) — ambas partes ya sobrevivieron (14, 15). Stalls solo al cargar (3.6s, 2.2s), mem plana 64/98MB.

**→ La bisección aditiva a ciegas está superada: tenemos 4 acciones concretas. Prime suspect nº1 = `powerPreference:"high-performance"` (fixable vía `PlayerSettings.WebGL.powerPreference`).**

**▶ SIGUIENTE — RUNG 17 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20, aditivo DIRIGIDO):** RUNG 11 (aguantó) + los atributos EXACTOS del contexto de Unity (`powerPreference:high-performance` + stencil/depth/alpha) + las 18 extensiones + `new AudioContext()`, SIN engine. `unityCtx:true` (proxy, unity=false). Receiver R2 `index.html` (92538 bytes), radio RUNG 17 en sender. Log: `new AudioContext() state=…` → `WebGL2 con powerPreference=high-performance…` → `N/18 extensiones` → `render loop ACTIVO`. **Corta ~180s → el disparador está en esas 4 acciones** → bisecar (RUNG 18 = solo powerPreference; fix = `PlayerSettings.WebGL.powerPreference`). **Aguanta → el disparador está más adentro** (ejecución real del engine) → Remote Debugger / escena vacía.

---

## 🏁🏁 CONCLUSIÓN FINAL (2026-07-20) — BISECCIÓN DE 15 ESCALONES COMPLETA. Causa = el CÓDIGO de init del engine de Unity. NO hay fix app-side.
> ⚠ SUPERADA por RUNG 16 (arriba): el espía dio candidatos observables. Esta "conclusión final" era prematura — quedaba el ángulo de instrumentar Unity, que SÍ dio pistas.

**RUNG 15 AGUANTÓ >7:34.** Instanciar un módulo WASM con memoria 64MB→512MB (= config Unity) + commit de 64MB físicos → NO corta. → **NO es la memoria ni la maquinaria de instanciación WASM.**

### Tabla final de exclusión (cada línea = test A/B de UNA variable, reloj fiable del PC)
| # | Variable aislada | Resultado |
|---|---|---|
| 10 | Red / descarga 64MB | aguantó >6:02 ✅ |
| 11 | Contexto WebGL2 + render loop | aguantó >8:12 ✅ |
| 12 | Huella GPU (192MB texturas + draws) | aguantó >9:14 ✅ |
| 8 | Stall 1-core (7s) | aguantó ✅ |
| 13 | Saturación N-core (6 workers ×30s) | aguantó >6:26 ✅ |
| 14 | Compilar el .wasm real (WebAssembly.compile, **stall 18s**) | aguantó >7:31 ✅ |
| 15 | Instanciar WASM + memoria 64→512MB + commit | aguantó >7:34 ✅ |
| **2** | **Instanciar+correr el engine REAL de Unity** | **cortó 198s ❌** |

**→ Excluido TODO mecanismo genérico: red, WebGL, GPU, CPU (1 y N cores), bloqueo de hilo (hasta 18s), compilación WASM, memoria WASM, instanciación WASM. La ÚNICA variable que corta es el CÓDIGO del engine de Unity ejecutándose durante su init.** Ningún proxy JS lo reproduce (equivaldría a correr Unity = RUNG 2). Por RUNG 7 (Quit a 15s → cortó 153s con Unity muerto), es un daño de UNA vez al cargar, irreversible: **Unity, al arrancar, hace UNA acción específica que dispara a la plataforma Cast del Xiaomi a tirar el transporte ~2min después**, con el receiver 100% sano (mem plana, hilo suelto, media PLAYING, CAF sin log de desconexión).

### Veredicto
**NO existe fix receiver-side alcanzable sin modificar el build/código de Unity.** Se agotó la bisección barata (proxies JS). Quedan 3 caminos, ninguno barato:
1. **✅ ACEPTAR + RECONEXIÓN (recomendado, shippable ya):** el CastPlugin móvil ya reconecta ~5s; el receiver salta re-INIT en reconexión rápida. Pulir overlay "Reconectando…". Es la vía sancionada por la plataforma.
2. **Remote Debugger (diagnóstico, NO fix):** registrar el nº serie del Xiaomi en Cast Console + abrir 9222 → leer el código de cierre real del WebSocket. Solo CONFIRMARÍA "firmware/plataforma"; no da fix (el receiver ya se sabe sano). Perturba la medición.
3. **Rebuild con escena VACÍA (última narrowing con posible fix, pero cuesta rebuild 1-3h):** si un Unity de escena vacía TAMBIÉN corta → es el core del engine (infixeable). Si NO corta → es nuestro contenido/assets/init C# → habría fix. Único fork restante que podría dar arreglo, pero no es barato.

### ▶ RUNG 16 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20) — giro de estrategia: espiar el init de Unity
La bisección ADITIVA (10-15) llegó al techo (todo lo genérico aguanta; solo queda "el código de Unity"=RUNG 2). Giro: **Unity ON + hooks a las APIs globales** (`spyInit:true`) para capturar QUÉ toca Unity al arrancar que los proxies NO reprodujeron. Unity corre normal → **cortará ~198s** (esperado); el valor es el log `🕵` capturado hasta el corte. Hooks: `canvas.getContext`(+atributos, CLAVE — los proxies no pasaban atributos), `getExtension`(únicas), `AudioContext`, `Worker`(pthreads), `SharedArrayBuffer`, `requestFullscreen`/`pointerLock`/`wakeLock`, `WebAssembly.instantiate/compile(Streaming)`, `mediaSession`. Baseline al instalar: cores/crossOriginIsolated/SharedArrayBuffer disponible. Receiver R2 `index.html` (88639 bytes), radio RUNG 16 en sender (unity=true). **Leer:** cada `🕵` = acción real de Unity; las que no salieron en RUNG 11-15 son las candidatas → replicar una a una (aditivo DIRIGIDo) hasta que pete. Si el log no muestra nada que los proxies no hicieran → el disparador no es observable por JS → Remote Debugger o cerrar.

### ⚠ Limpieza pendiente al cerrar
- R2 `index.html` = receiver de DIAGNÓSTICO (`rcv-prod-config.html`, panel debug siempre visible + streaming). **Restaurar receiver de PRODUCCIÓN limpio antes de uso real.** Backup prod Unity ON: `scratchpad/r2-index-backup-KA9probe.html`.
- Harness (server node 3003) sigue vivo; matar al cerrar.

---

## ✅ RUNG 14 EJECUTADO (2026-07-20) — AGUANTÓ >7:31. NO es la compilación del WASM. Es la INSTANCIACIÓN/memoria.

`fetch` del .wasm real (44MB) + `WebAssembly.compile()` **sin instanciar**. La compilación causó el **STALL de hilo más grande de toda la investigación: 17.811 ms (~18s)** bloqueando el hilo principal — y aun así **aguantó >7:31 (451s+)** conectado. (Nota: el compile se disparó 3× por los 3 reenvíos de `RUNG_CONFIG` → aún MÁS estrés, y sobrevivió igual.)

**Dos hallazgos decisivos:**
1. **NO es la compilación del módulo.** Compilar el .wasm real de 44MB (3×) no corta.
2. **🔑 Un STALL de 18s del hilo principal SOBREVIVIÓ.** Esto **entierra por tercera y definitiva vez** la teoría "stall/starvation del hilo mata el heartbeat" (ya lo negaban RUNG 8 y RUNG 12; ahora un bloqueo de 18s lo confirma). El corte NO tiene NADA que ver con bloquear el hilo principal.

**→ La causa está en la INSTANCIACIÓN del módulo:** allocar/reservar la `WebAssembly.Memory` (Initial 64MB / Max 512MB) y/o el runtime emscripten y/o el código de init del engine de Unity al ejecutarse. NO es compilar, NO es red, NO es WebGL, NO es GPU, NO es CPU/hilo.

**▶ SIGUIENTE — RUNG 15 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20):** instancia un módulo WASM sintético (bytes hand-built, validados en node: memoria min 1024 pág=64MB / max 8192=512MB, idéntica a la config de Unity) + commit de todas las páginas (1 byte/4KB = 64MB físicos), SIN código de Unity. Guard `__wasmMemRan` evita el triple-run del RUNG_CONFIG (lección de RUNG 14). Receiver en R2 `index.html` (82712 bytes), radio RUNG 15 en `sender-video.html` (3003). Log: `✅ INSTANCIADO — WebAssembly.Memory 64MB` → `✅ COMMIT de 64MB hecho`. **Corta ~2min tras el COMMIT→es la memoria/instanciación** (fix concreto: bajar Initial Memory 64MB, rebuild). **Aguanta→es el código de init del engine de Unity en sí** — sin proxy JS posible (equivaldría a correr Unity=RUNG 2); solo quedarían knobs del build o el Remote Debugger. Tras RUNG 15 no hay más rungs baratos: o fix de memoria, o Remote Debugger, o reconexión.

---

## Síntoma

La sesión Cast se corta sola a **~140–152 s** de forma reproducible al castear desde la app Android (Appquarium) al Xiaomi TV Box S (Cast SDK 3.72.446070, App ID `8F6C873F`).

- El **receiver NO muere**: los peces siguen nadando, la overlay "Sender desconectado" aparece, el handler `SENDER_DISCONNECTED` se ejecuta con normalidad.
- El **sender** ve `onSessionEnded after ~152s — Cast controller status code 2055 (2055)`.
- El banner `⚠ ÚLTIMA CAÍDA` **sí aparece** en la siguiente sesión → confirma que el proceso WebGL NO hace OOM crash.
- `e.reason` en `SENDER_DISCONNECTED` es `null` → guardado como `"unknown"` / mostrado como `"desconocido"`.
- El timing varía ligeramente: 142 s en unas pruebas, 152 s en otras.
- **Auto-reconnect del CastPlugin funciona**: tras el corte, el sender reconecta en ~5 s automáticamente sin intervención del usuario.

---

## Evidencia clave (logcat sender, 2026-06-27)

```
06-27 23:17:02  D CastPlugin: onSessionStarted
06-27 23:19:02  D CastPlugin: Cast keepalive ping t=120s     ← PING fire-and-forget
06-27 23:19:34  W CastPlugin: onSessionEnded after 152s — Cast controller status code 2055 (2055)
06-27 23:19:34  D CastPlugin: WakeLock released
06-27 23:19:37  W CastPlugin: auto-reconnect attempt 1/3
```

**Lo que descarta el logcat:**
- Red / WiFi: ningún evento en el momento del corte.
- Canal degradado: sin `sendMessage FAILED` entre t=0 y t=152 s.
- Idle timeout estándar de Cast: sería exactamente 300 s.
- Doze/Android: sin `onSessionSuspended` previo; WakeLock estaba activo.

---

## CastPlugin.java — análisis (leído 2026-06-28, NO modificado)

Ruta: `D:\dev\appquarium-unity\Assets\Plugins\Android\appquarium.androidlib\src\main\java\com\appquarium\app\CastPlugin.java`

Claves:
- `KEEPALIVE_INTERVAL_MS = 60_000L` → PING en t=60s, t=120s, t=180s…
- `sendMessage("{\"type\":\"PING\",\"t\":" + elapsed + "}")` — **fire-and-forget**, solo comprueba si el envío falló, NO espera PONG.
- `onSessionEnded` lo dispara el **GMS (Google Mobile Services)**, no el CastPlugin. El plugin solo registra el código y lanza auto-reconnect.
- Auto-reconnect: 3 intentos, delay 3 s, busca la misma ruta por ID. **Confirmado funcionando.**
- WakeLock: `PARTIAL_WAKE_LOCK`, adquirido en `onSessionStarted`, liberado en `onSessionEnded`. ✅

**Conclusión**: el CastPlugin no tiene ninguna lógica que cause un corte a 150 s. El `onSessionEnded` lo genera el GMS.

---

## Hipótesis descartadas

| # | Hipótesis | Descartada por |
|---|---|---|
| H1 | OOM/crash del receiver WebGL | Screenshot: WASM 159 MB estable, FPS 54, receiver vivo. Banner ÚLTIMA CAÍDA aparece. |
| H2 | Idle timeout estándar Cast (300 s) | Ocurre a ~150 s, no 300 s. |
| H3 | PING sender esperaba PONG en 30 s | CastPlugin.java: PING es fire-and-forget, no hay timeout de respuesta. |
| H4 | `disableIdleTimeout` ignorado por objeto literal | Usamos `new CastReceiverOptions()` real desde rcv 2026-06-23a. Sin efecto. |
| H5 | Spike de RAM por carga paralela de bundles | Irrelevante: el receiver no crashea. Fix serial en TvSceneBootstrap.cs pendiente rebuild. |

---

## Todos los fixes aplicados — cronología completa

### rcv 2026-06-23a (pre-investigación)
- `new cast.framework.CastReceiverOptions()` real, `disableIdleTimeout=true`, `maxInactivity=3600`
- Panel debug, LAST_DISCO banner, SHUTDOWN handler, sello de versión
- **Resultado**: ❌ disconnect sigue a ~150 s.

### rcv 2026-06-26a / 2026-06-27a
- `opts.customNamespaces[NAMESPACE] = MessageType.JSON` antes de `ctx.start()`
- **Resultado**: ❌ sin cambio.

### rcv 2026-06-27b — `ctx.getPlayerManager()`
- Registrar el PlayerManager antes de `ctx.start()` para que el Cast infrastructure vea un receiver "media-aware".
- **Resultado**: ❌ disconnect sigue a ~150 s.

### rcv 2026-06-27c — `broadcastStatus()` + keepalive 30 s
- `_player.broadcastStatus()` cada 30 s → MEDIA_STATUS en `urn:x-cast:com.google.cast.media`.
- Custom KEEPALIVE en namespace propio cada 30 s (bajado de 60 s, sin esperar a Unity).
- **Resultado**: ❌ disconnect sigue a ~142 s.

### rcv 2026-06-27d — PING → PONG en JS
- Interceptar `PING` en `addCustomMessageListener`, responder `PONG` inmediatamente.
- Hipótesis inicial: 32 s entre PING (t=120s) y corte (t=152s) parecía timeout de respuesta.
- **Resultado**: ❌ disconnect sigue. Hipótesis invalidada al leer CastPlugin.java.

### rcv 2026-06-28a — `ctx.setApplicationStatus()`
- `ctx.setApplicationStatus('Appquarium Active')` cada 20 s + llamada inmediata en SENDER_CONNECTED.
- Envía RECEIVER_STATUS en `urn:x-cast:com.google.cast.receiver`.
- **Resultado**: ❌ disconnect sigue a ~142 s.

### rcv 2026-06-28b — audio silencioso como media element (blob URL)
- WAV de 4 s de silencio generado en JS, `setMediaElement()` antes de `ctx.start()`, `.play()` en SENDER_CONNECTED.
- Hipótesis: `disableIdleTimeout` solo aplica post-playback, no a receivers que nunca reproducen.
- **Resultado**: ❌ disconnect sigue. No se confirmó si el panel mostró `Silent audio playing` o `play failed` (autoplay policy podría haberlo bloqueado).

### rcv 2026-06-28c — reconnect seamless (skip INIT)
- Cambia estrategia: acepta el disconnect, hace el reconnect invisible.
- `_lastDiscoMs` + `_initForwarded`: si llega INIT con Unity ya cargado y han pasado < 30 s desde el disconnect → descarta el INIT (el acuario no se reinicia).
- CastPlugin auto-reconecta en ~5 s → overlay desaparece → acuario continúa.
- **Confirmado**: auto-reconnect del CastPlugin **sí funciona**. La sesión se restablece.
- **Resultado**: ❌ "sigue igual" — el usuario confirma que el ciclo disconnect/reconnect continúa cada ~150 s. Pendiente confirmar si el INIT skip funciona (¿se recarga el acuario o continúa?).

### rcv 2026-06-28d — `PlayerManager.load()` con silence.wav en R2
- `silence.wav` (32 KB, 4 s, 8 kHz, 8-bit mono) subido a R2: `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/silence.wav`
- En SENDER_CONNECTED: `_player.load(req)` con URL real de R2, `streamType=BUFFERED`, `autoplay=true`, `repeatMode=SINGLE`.
- Diferencia clave vs rcv 2026-06-28b: usa el flujo LOAD completo del protocolo Cast → `playerState=PLAYING` real.
- **Resultado**: ❌ disconnect sigue. **8/8 fixes receiver-side fallidos. Causa irremediable desde JS.**

### rcv 2026-06-28e — Overlay diferido 15s
- `_overlayTimer = setTimeout(showReconnect, 15000)` en SENDER_DISCONNECTED, cancelado en SENDER_CONNECTED.
- **Resultado**: ❌ overlay sigue apareciendo. El CastPlugin tarda >15s en reconectar (20-40s según condiciones).

### rcv 2026-06-28f — Overlay diferido 90s ← **EN R2 ACTUALMENTE**
- Mismo mecanismo, timer subido a 90s. Cubre tiempos de reconnect de hasta 90s.
- Template `Assets/WebGLTemplates/CastReceiver/index.html` sincronizado con todos los cambios de esta sesión.
- **Resultado**: ⏳ No probado antes del cierre de sesión. Irrelevante si el fix móvil resuelve la raíz.

---

## CONCLUSIÓN RECEIVER-SIDE: AGOTADO (9 fixes)

El timeout ~150s es firmware del Xiaomi TV Box S. El receiver JS no puede prevenirlo.  
La única palanca restante: **sender Android** via `RemoteMediaClient.load()`.  
Ver `CAST_DISCONNECT_MOBILE_HANDOFF.md` para el brief completo.

---

## Estado actual del receiver en R2

| Archivo | Versión activa |
|---|---|
| `index.html` | `rcv 2026-06-28f` |
| `silence.wav` | Subido 2026-06-28, 32 KB |
| `Assets/WebGLTemplates/CastReceiver/index.html` | ✅ Sincronizado 2026-06-28 |

---

## Hipótesis activa

El timeout a ~150 s es un **comportamiento del Cast runtime del Xiaomi TV Box S** (SDK 3.72.446070) que aplica un timeout de "no media playback" de ~2.5 min a receivers custom sin actividad media genuina. Este timeout ignora todas las señales de heartbeat de aplicación:

| Señal | Namespace | Resultado |
|---|---|---|
| `disableIdleTimeout=true` | (config) | ❌ ignorado |
| `maxInactivity=3600` | (config) | ❌ cubre sender inactivity, no media idle |
| Custom KEEPALIVE | `urn:x-cast:dev.unknownaerials.appquarium` | ❌ ignorado |
| `broadcastStatus()` → IDLE | `urn:x-cast:com.google.cast.media` | ❌ confirma que no hay media |
| `setApplicationStatus()` | `urn:x-cast:com.google.cast.receiver` | ❌ ignorado |
| `setMediaElement+play()` blob | `urn:x-cast:com.google.cast.media` | ❌ posible fallo autoplay |
| `PlayerManager.load()` URL real | `urn:x-cast:com.google.cast.media` | ⏳ por probar |

El GMS Cast service en Android recibe el cierre del receiver y genera `onSessionEnded` con código 2055 (interno, no documentado públicamente).

---

## Próximos pasos según resultado de rcv 2026-06-28d

### Si `Silence LOADED → state PLAYING` aparece en debug y la sesión dura > 3 min → ✅ RESUELTO
- Documentar fix definitivo.
- Sync cambios al template WebGLTemplates.
- Commit + push.

### Si `Silence load FAILED: <mensaje>` → diagnosticar el error
- Si el error es de CORS: verificar headers R2 para silence.wav.
- Si el error es de formato: probar con OGG Vorbis en lugar de WAV.
- Si el error es de permisos/autoplay: el Cast receiver debería poder cargar audio sin restricciones de autoplay, pero si no, alojar en otro dominio y verificar CORS.

### Si carga OK pero disconnect sigue → conclusión: firmware Xiaomi irremediable desde receiver
Dos opciones, en orden de preferencia:

**Opción A — Mobile side (requiere autorización user para tocar CastPlugin.java)**
- Reconexión proactiva a t=100s: el sender llama `endCurrentSession(false)` + `reconnect()` antes del timeout de 150s.
- Con el INIT skip del receiver (rcv 2026-06-28c), el acuario no se recarga. El usuario vería un parpadeo del overlay cada ~100s pero sin recargar.
- Esto resetea el contador del GMS antes de que llegue al límite.

**Opción B — Aceptar el ciclo, mejorar UX del overlay**
- El ciclo disconnect/reconnect cada ~150 s ya funciona automáticamente (CastPlugin auto-reconnect confirmado).
- Mejorar overlay: cambiar texto a "Reconectando…" con countdown, en lugar de "Sender desconectado" (que suena a error grave).
- Con INIT skip funcionando, el acuario no se recarga en cada ciclo — solo aparece el overlay ~5 s cada 2.5 min.

---

## Notas técnicas

- **Código 2055**: no documentado en `CastStatusCodes` públicos. `getStatusCodeString(2055)` devuelve "Cast controller status code 2055". Código interno del Cast channel protocol.
- **`disableIdleTimeout` scope real**: aplica a "when it becomes idle after active playback stops" (post-playback). Para receivers que nunca reproducen media, el Xiaomi aplica su propio timeout OEM.
- **`maxInactivity`**: tiempo que el receiver espera antes de desconectar un sender inactivo. No controla el media idle. Default: 10 s (SDK requiere > 5).
- **`broadcastStatus()`**: MEDIA_STATUS en `urn:x-cast:com.google.cast.media`. No envía RECEIVER_STATUS.
- **`setApplicationStatus()`**: RECEIVER_STATUS en `urn:x-cast:com.google.cast.receiver`. Satisface heartbeat de sesión pero no el media idle del Xiaomi.
- **Cast SDK Xiaomi**: 3.72.446070. No controlable desde el receiver.
- **silence.wav en R2**: `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/silence.wav`, 32 KB, `max-age=31536000`.

---

## ⭐ ACTUALIZACIÓN 2026-06-30 — La conclusión "firmware irremediable" era FALSA

La hipótesis de "timeout de firmware no removible" queda **refutada**. El problema NO era reproducir audio/estado: era reproducir **VÍDEO real**. Y el vídeo SÍ mantiene el sender.

### Pista del usuario
En el MISMO Xiaomi, YouTube/HBO no se cortan. Diferencia: reproducen **vídeo continuo**. Todos los 9 fixes previos eran audio o mensajes de estado — nunca vídeo.

### Pruebas nuevas (todas Xiaomi, mismo sender Appquarium)

| Versión | Setup | Corte del SENDER |
|---|---|---|
| Baseline prod (`28g`) | sin vídeo | ~152s |
| `rcv-30a` | vídeo añadido pero nunca reprodujo (`KA: sin <video>`) | 177s |
| `rcv-30c` | vídeo reproduciendo de verdad (`fb:▶ load:OK pm:PLAYING`) detrás del canvas Unity | **205s** |
| **`rcv-hold`** | vídeo + `pm.load`, **SIN Unity, SIN mensajes extra**, mide SENDER directamente | **360s+ y sigue conectado** ✅ |

- El primer "215s" fue ambiguo (el contador medía vida de la *página*, no del sender). El test `rcv-hold` lo corrigió midiendo `SENDER_CONNECTED/DISCONNECTED` reales → cartel rojo al caer el sender. **360s+ verde = el sender NO cae con vídeo.**

### Conclusión
- **El vídeo (cast-media-player + `pm.load` de mp4 negro en loop) mantiene el sender conectado.** No es firmware irremediable.
- El receptor **integrado** cae a ~205s por algo que el standalone NO tiene:
  1. **Unity WebGL** corriendo (¿starva el heartbeat Cast / stalle el decode del vídeo bajo carga GPU?).
  2. **Mensajes extra** receiver→sender: `setApplicationStatus('Appquarium Active')` cada 20s + custom `KEEPALIVE` cada 30s + PONG. **Sospecha nº1: `setApplicationStatus` cada 20s clobbering el estado "media activa"** (el standalone no lo manda y aguanta 360s+).

### La receta del vídeo que funciona
- Clip: `keepalive_black.mp4` (320x240, H.264 baseline + AAC silencioso, 10.5 KB) en R2 raíz, `max-age=604800`.
- `<cast-media-player>` + `ctx.getPlayerManager()` → en SENDER_CONNECTED `pm.load(LoadRequestData{media:video/mp4, autoplay:true})` + forzar `video.loop=true; muted=true; play()` DOM. Respaldo: `<video id=ka-fallback>` 8px explícito. Watchdog 3s.

### PLAN — bisección (1 cast por paso, mirando la TV)
1. **Integrado + vídeo, quitando los mensajes extra** (`setApplicationStatus` 20s + `KEEPALIVE` 30s + PONG). Aguanta >4min → eran los mensajes (borrarlos = fix). Cae ~205s → es Unity → paso 2.
2. **Integrado + vídeo + log `video.currentTime` cada 10s.** Si el `currentTime` se congela antes del corte → Unity starva el decode → mitigar (vídeo resiliente / bajar carga Unity). Si el vídeo sigue y cae igual → starvation del heartbeat por el hilo principal de Unity → mitigar.

### Estado actual en R2
- Producción **RESTAURADA** a `rcv 2026-06-28g` (limpio, sin experimento).
- `keepalive_black.mp4` queda en R2 (huérfano, inofensivo, listo para retomar).
- Backup `28g` (39956 bytes) en scratchpad de la sesión.

---

# ⭐⭐ BISECCIÓN DEFINITIVA — 2026-07-18 (harness PC, reloj fiable)

**Vuelco total de conclusiones anteriores.** Todo lo previo (firmware irremediable / cap duro del Xiaomi / vídeo como cura) queda **refutado** por una bisección limpia casteando **desde el PC** (Chrome, sin Android/Doze/CastPlugin, reloj fiable).

## Harness

- **Sender:** `Tools/sender-video.html` servido en `http://localhost:3003` (`Tools/sender-video-server.js`, arrancado DETACHED con `Start-Process node ... -WindowStyle Hidden` porque el harness mata los procesos background). Selector de escalón RUNG 0-9, cronómetro con `Date.now()` del PC (fiable), y escucha el canal Cast para pintar el log del receiver (`RCV ...`).
- **Receiver:** `Tools/rcv-prod-config.html` = receiver de PRODUCCIÓN (KA9-probe) con un interruptor. Un SOLO fichero en R2 `index.html` sirve todos los escalones; el sender manda `RUNG_CONFIG {unity,kaBig,killAudio,verbose,throttleRaf,quitUnity,stall}` al conectar y eso decide qué se activa. Unity se DIFIERE hasta ese mensaje.
- Backup de prod real (Unity ON): `scratchpad/r2-index-backup-KA9probe.html` (53896 bytes).
- **App ID reusado `8F6C873F`** (con backup); RUNG 0 usa el Default Media Receiver de Google `CC1AD845`.

## TABLA DE RESULTADOS — cada caso probado

| RUNG | Setup (única variable vs producción) | Corte | Qué prueba / descarta |
|---|---|---|---|
| **0** | Default Media Receiver de Google + mp4 negro loop. CERO código nuestro, sin Unity | **>8min ✅** | La plataforma del Xiaomi SÍ sostiene media indefinidamente → **NO es cap duro del device** |
| **1** | Receiver de PROD **Unity OFF** (vídeo keepalive + Cast bridge byte-idénticos a prod) | **>4:25 ✅** | Nuestro shell del receiver está limpio → el bug lo mete Unity |
| **2** | Receiver de PROD **Unity ON** (producción entera) | **198s ❌** | **A/B de 1 variable: UNITY ES LA CAUSA** |
| **3** | Unity ON + vídeo keepalive FULLSCREEN (opacity 0.02) | **196s ❌** | Descarta throttle de compositing / tamaño del vídeo |
| **4** | Unity ON + AudioContext de Unity suspendido en bucle (`suspendidos 1/1`) | **180s ❌** | Descarta foco de audio / media-session |
| **5** | Unity ON + `setLoggerLevel(DEBUG)` + captura de `console` → PC | **186s ❌** | **CAF SANO al morir, SIN razón de disconnect en el log** → no es idle timeout de CAF; es la **capa de TRANSPORTE** (WebSocket) por debajo de CAF |
| **6** | Unity ON + `requestAnimationFrame` estrangulado a 4fps | **163s ❌** | Throttle aplicó (STALL bajó 9.7s→3.9s) y cortó igual → **descarta carga de render / event-loop** |
| **7** | Unity ON, luego `Unity.Quit()` a los ~43s (destruye el contexto WebGL) | **153s ❌** | Unity destruido de verdad (JS 98→93MB) y cortó igual con Unity muerto 110s → **el daño se hace al CARGAR Unity, UNA VEZ, irreversible** |
| **8** | SIN Unity + bloqueo artificial (busy-wait) de 7s a los 15s (se disparó ×3 = ~13-21s acumulado) | **>4:43 ✅** | **Descarta el STALL del hilo**: incluso ~13-21s de bloqueo NO corta. No es starvation del heartbeat por el freeze de carga |
| **9** | Vídeo 240s (conexión asentada, sana) y luego **cargar Unity a los 240s** | **cortó 255s ❌** | **Mata la hipótesis "cargar más tarde ayuda"**: conexión de 4min sana MURIÓ 15s tras empezar a cargar Unity, **a mitad de carga (90%)**. No es la edad de la sesión, es el ACTO de cargar Unity — y a una conexión establecida la mata MÁS rápido (15s vs ~170s al inicio). Cut DURANTE la descarga/instanciación del WASM |
| **10** | Vídeo + **descargar los 64MB de Unity (.wasm+.data) por fetch a los 240s, SIN instanciar** | **>6:02 ✅** | **Descarta la RED/saturación**: descargó 64MB (×3 = ~192MB) y aguantó como RUNG 1. La descarga no corta → el trigger es la **instanciación WebGL/WASM**, no bajar los bytes |
| **11** | Vídeo + crear un **contexto WebGL2 crudo + render loop, SIN Unity/WASM** a los 240s | ⏳ **EMPEZAR AQUÍ MAÑANA** | Aísla lo último: ¿es el CONTEXTO WEBGL (render/GPU) o la instanciación del WASM? Si corta → el WebView del Xiaomi no tolera WebGL activo con Cast (≈ infixeable sin quitar WebGL → solo reconexión). Si aguanta → es el WASM/engine de Unity |

## Datos transversales (todos los RUNG con Unity)

- **Memoria SIEMPRE plana**: `WASM:64MB JS:98MB` sin moverse → **NO es OOM**.
- **Hilo responsivo AL MORIR**: `lag peor` 20-300ms en el corte. Los `STALL` grandes (3-9.7s) son SOLO al arrancar Unity (~30s), lejos del corte.
- **Vídeo keepalive `cmp:PLAYING` hasta el final** (clip de 10s hace END_OF_STREAM→reload cada ~10s vía REPEAT_SINGLE). Media activa todo el rato.
- **Cortes DECRECIENTES con casts repetidos**: 198→196→186→180→163→153. El Xiaomi acumula estado/calor entre pruebas → **reiniciar el device entre tests** para números limpios.

## Conclusión al día 2026-07-18

1. **NO es el device/firmware** (RUNG 0/1 aguantan). Semanas culpando al Xiaomi/Doze = pista falsa.
2. **NO es la media/audio/vídeo** (RUNG 3/4). Todo el hilo del "vídeo keepalive como cura" era tratar un problema de conexión disfrazado de media.
3. **NO es idle timeout de CAF** (RUNG 5: framework sano, sin log de razón). Es la **capa de transporte WebSocket** por debajo de CAF.
4. **NO es la carga de render ni la existencia continua del contexto WebGL** (RUNG 6/7).
5. **El daño se hace al CARGAR Unity, una vez, y es irreversible** (RUNG 7). Cargar más tarde no ayuda, es peor (RUNG 9: mata una conexión de 4min en 15s).
6. **NO es el bloqueo del hilo** (RUNG 8), **NI la red/descarga de assets** (RUNG 10).
7. **El trigger es la instanciación del motor WebGL/WASM de Unity** (crear contexto WebGL + instanciar WASM + arrancar engine). Es lo único que queda tras descartar red (RUNG 10) y todo lo anterior. RUNG 11 (contexto WebGL crudo sin Unity) separará "contexto WebGL/GPU" de "WASM/engine".

**Descartado por A/B sobre el código real:** device/firmware · OOM · media/audio/vídeo · idle timeout de CAF · carga de render/event-loop · existencia continua del contexto WebGL (destruido) · bloqueo del hilo principal · edad de la sesión · red/descarga de 64MB.

**El vídeo keepalive es IRRELEVANTE** al problema — ni causa ni cura el corte. Todo el trabajo previo de 9 fixes de vídeo/audio/estado (junio) atacaba un problema que en realidad es la instanciación WebGL de Unity.

---

## ✅ RUNG 11 EJECUTADO (2026-07-20) — AGUANTÓ >8:12. Es el WASM/engine, NO el contexto WebGL.

Casteado desde el PC (harness 3003), sello "CONECTADO (rung 11)" verificado. Contexto WebGL2 crudo + render loop (`clearColor`/`clear` por frame, SIN texturas/buffers/WASM) creado a los 15s. **Aguantó >8:12 conectado**, lag plano (3-66ms), `stream sent=190 fail=0`, KA PLAYING sin parar. → **El contexto WebGL2 + compositing NO es la causa.** Combinado con RUNG 10 (red, aguantó) → **la causa es instanciar/correr el motor WASM de Unity.**

⚠ **RUNG 11 usó un render loop TRIVIAL (solo clearColor, cero GPU allocation).** NO excluye la huella GPU de Unity (muchas texturas/FBOs). Quedan 2 sub-hipótesis: **(1) lado WASM/CPU** (instanciar 44MB + heap + ejecución) vs **(2) huella GPU** (texturas/buffers reales).

## ✅ RUNG 13 EJECUTADO (2026-07-20) — AGUANTÓ >6:26. NO es la saturación multi-core. Es INTRÍNSECO del WASM de Unity.

Device = 4 cores. Lanzó **6 workers** pegando todos los cores ~30s (`6 workers ARRANCADOS` a 15s → `6/6 TERMINADOS a los 30s`). Durante la ráfaga el hilo principal siguió suelto (lag 10-315ms) — condición realista. **Aguantó >6:26 (386s+) conectado.** → **Saturación multi-core DESCARTADA.**

**🏁 BISECCIÓN CERRADA. Excluido TODO lo replicable sin el WASM real de Unity:** red (10) · contexto WebGL (11) · huella GPU (12) · stall 1-core (8) · saturación N-core (13) · media/audio/render/edad. **La causa es intrínseca a instanciar el módulo WASM de Unity** (compilación del .wasm de 44MB / setup de la `WebAssembly.Memory` / algo del runtime emscripten), y por RUNG 7 es un daño de UNA vez al cargar, irreversible. **Ningún proxy lo reproduce → NO hay fix app-side sin tocar el build de Unity.**

**➡ CONCLUSIÓN (por framework acordado): cerrar receiver-side + aceptar reconexión** (el CastPlugin móvil ya reconecta ~5s; el receiver salta re-INIT en reconexión rápida). Pulir UX del overlay ("Reconectando…" con countdown).

**Opción fina restante — RUNG 14 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20):** `fetch` del .wasm REAL de Unity (`Build/webgl-output.wasm`, confirmado 44.250.183 B / application/wasm en R2) + `WebAssembly.compile()` SIN instanciar/correr (retiene el módulo en `window.__lastCompiledWasm`, sin memoria/runtime/GL). Delta de UNA variable sobre RUNG 10 (fetch+descartar, aguantó): el único añadido es el compile. Único proxy 100% fiel al pipeline del compilador V8 (los workers de RUNG 13 no lo reproducen). Receiver en R2 `index.html` (79874 bytes), radio RUNG 14 en `sender-video.html` (3003). Marcadores log: `descargando…` → `descargado NMB → WebAssembly.compile()…` → `✅ COMPILADO en Ns`. **Corta ~2min tras el `✅ COMPILADO`→es la compilación** (fix fino: wasm más pequeño / streaming). **Aguanta→es la instanciación/memoria** (fix fino: bajar Initial Memory 64MB). Ambos fixes = rebuild especulativo, odds bajos. Diagnóstico definitivo aparte = registrar Xiaomi en Cast Console + Remote Debugger 9222 (nuclear).

---

## ✅ RUNG 12 EJECUTADO (2026-07-20) — AGUANTÓ >9:14. NO es la huella GPU. Es el WASM.

Casteado desde el PC, sello "rung 12" verificado. `⚙ gpuHeavy: 12/12 texturas 2048² asignadas (≈192MB GPU) + render loop ACTIVO` confirmado a los 18.8s. **Aguantó >9:14 (554s) conectado**, SIN `WEBGLCONTEXTLOST` (el device sostuvo 192MB de GPU sin problema). → **La huella GPU (texturas + draws) NO es la causa.** El fix del research (WebGL1/LowPower/reducir texturas) queda DESCARTADO.

🔑 **Dato extra decisivo:** RUNG 12 tuvo lag recurrente de 430-975ms en el hilo principal (12 draws fullscreen/frame) — PEOR que el lag post-warmup de Unity (<240ms) — y AUN ASÍ aguantó >9min. **Con RUNG 8 (stall 7s), esto ENTIERRA la teoría de "starvation del hilo principal mata el heartbeat".** El corte NO es por lag del hilo principal, ni por GPU, ni por red, ni por contexto WebGL.

**→ La causa es específicamente el lado WASM de instanciar el engine de Unity**, y por RUNG 7 es un daño de UNA vez al cargar, irreversible. Lo único no replicado aún: (a) hilos de fondo de compilación WASM / pthreads de emscripten saturando cores (invisible a nuestro medidor de lag del hilo principal), (b) la `WebAssembly.Memory` grande, (c) algo intrínseco de Unity.

**▶ SIGUIENTE — RUNG 13 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20):** saturar TODOS los cores con (cores+2) Web Workers busy-wait ~30s a los 15s, SIN Unity/WASM. `coreSaturate:true`. Receiver en R2 `index.html` (77236 bytes), radio RUNG 13 en `sender-video.html` (3003). Log confirma workers ARRANCADOS/TERMINADOS + el medidor de lag del hilo principal debe seguir bajo (los workers son otros hilos). Prueba la hipótesis (a) — lo único que RUNG 8 (1 core) no cubrió. Corta→saturación multi-core durante el warmup→posible mitigación. Aguanta→intrínseco del WASM de Unity→sin fix app-side→cerrar con reconexión. ⚠ Si log dice "0 workers arrancados / Worker no soportado" → el WebView bloquea blob-workers, test inválido, pensar otra vía.

---

**RUNG 12 (✅ CONSTRUIDO Y DESPLEGADO 2026-07-20):** WebGL2 "pesado" sin Unity — asigna 12×2048² RGBA8 ≈192MB de texturas GPU reales + 12 draws/frame muestreando todas, sin WASM. `gpuHeavy:true`. Receiver en R2 `index.html` (74328 bytes, cache 30s), radio RUNG 12 en `sender-video.html` (3003). Listener `webglcontextlost` para distinguir GPU-OOM (otro fallo) del corte de transporte. Solo JS, SIN rebuild.
- CORTA ~3min → **huella GPU** → FIX REAL: `powerPreference LowPower` + WebGL1 + `★ Reduce TV Textures` (rebuild justificado).
- AGUANTA → **lado WASM/CPU** → no hay fix app-side → aceptar reconexión (móvil ya reconecta).
Tras RUNG 12 no quedan rungs baratos: o fix GPU, o cerrar con reconexión.

Pista del research (2026-07-20): la firma "muere sin CLOSE, invisible a CAF" encaja con el heartbeat de transporte Cast V2 (`cast.tp.heartbeat`) que la plataforma tira sin avisar. Diagnóstico definitivo = registrar el Xiaomi en Cast Console + Remote Debugger (9222) — opción nuclear.

---

## ▶▶ EMPEZAR AQUÍ LA PRÓXIMA SESIÓN (2026-07-19+)

**Estado:** receiver-side agotado por 11 tests A/B. El corte lo dispara instanciar el motor WebGL/WASM de Unity. Todo lo demás descartado.

**Harness listo y desplegado** (`Tools/sender-video.html` en `localhost:3003`, receiver configurable en R2 `index.html`). Selector RUNG 0-11. Server node arrancar DETACHED: `Start-Process node -ArgumentList "Tools\sender-video-server.js" -WorkingDirectory "D:\dev\appquarium-tv-unity" -WindowStyle Hidden`.
- ⚠ **Verificar SIEMPRE el sello "CONECTADO (rung N)"** antes de creer un run (el default es rung 1; casteando sin pinchar sale rung 1 y falsea el test — ya pasó con RUNG 9).
- ⚠ **Reiniciar el Xiaomi entre casts** — los cortes bajan por acumulación de estado/calor (198→153 en tests seguidos).

**Plan:**
1. **RUNG 11 (montado, pendiente)** — contexto WebGL2 crudo + render loop, SIN Unity. Si CORTA → es el contexto WebGL/GPU lo que el WebView del Xiaomi mata con Cast → prácticamente infixeable desde la app (solo reconexión). Si AGUANTA → es la instanciación del WASM/engine de Unity.
2. **Chrome Remote Debugger** (`chrome://inspect` o `IP:9222`) — ver el `close` real del WebSocket de transporte y su código. Requiere registrar el Xiaomi (nº serie) en la Cast Console + abrir 9222. Puede revelar un código con workaround conocido, o confirmar firmware.
3. **Fallback pragmático (shippable ya):** aceptar el corte + reconexión. El CastPlugin móvil ya reconecta (~5s) y el receiver ya salta re-INIT en reconexión rápida (`rcv 2026-06-28c`). Mejorar UX del overlay ("Reconectando…" con countdown).

**Ficheros del harness (Tools/):**
- `sender-video.html` + `sender-video-server.js` (3003).
- `rcv-prod-config.html` = receiver configurable (deployado en R2 `index.html`, cache 30s). Flags `RUNG_CONFIG`: `unity,kaBig,killAudio,verbose,throttleRaf,quitUnity,stall,delayedUnity,netLoad,rawWebGL`.
- `rcv-prod-noUnity.html` / `rcv-prod-unityON.html` = variantes con flag hardcoded (obsoletas, el config las sustituye).
- Backup prod real Unity ON: `scratchpad/r2-index-backup-KA9probe.html`.
- ⚠ **Restaurar prod** al cerrar tests: deploy de un receiver limpio (el config lleva panel debug + streaming, no es para producción).

## Próximos pasos

- **RUNG 8** (en curso): stall artificial sin Unity. Si corta → fix = evitar el bloqueo largo al cargar (WASM streaming/async, build más pequeño, diferir/trocear la carga).
- **RUNG 9** (idea del user): vídeo primero, Unity a los 4min. Aísla si el corte es "X min tras cargar Unity" (llegue cuando llegue) o si una conexión ya asentada sobrevive.
- Opción nuclear: **Chrome Remote Debugger** (`IP:9222`) para ver el cierre WebSocket real. Requiere registrar el Xiaomi en la Cast Console + abrir 9222 (cerrado).
- Fallback pragmático: aceptar el corte + reconexión (el CastPlugin móvil ya reconecta ~5s; el receiver ya salta re-INIT en reconexión rápida).

---

# ⚠ RUNG 22 (escena vacía) · 2026-07-21 — RESULTADO + CORRECCIÓN DEL VEREDICTO

> **CORRECCIÓN (misma sesión):** la primera redacción de esta sección decía "investigación CERRADA,
> es el engine core, infixeable". **Eso era una extrapolación no probada.** Lo que RUNG 22 demuestra
> es que **no es el contenido de la ESCENA** — nada más.
>
> Motivo: el build "vacío" **NO era un Unity mínimo**. Su `.wasm` pesa 44.249.290 B contra los
> 44.250.183 B de producción — es decir, **sigue conteniendo todo nuestro C# compilado, Addressables,
> URP entero**, y el `.data` sigue llevando `StreamingAssets/aa`. Solo se vació la escena.
>
> **Un Unity mínimo de verdad (proyecto limpio, hello-world, wasm ~8 MB, sin Addressables, sin URP,
> sin nuestro código) NUNCA se ha probado.** Esa es la bifurcación real y sigue abierta:
> - Unity mínimo CORTA → entonces sí es el engine core.
> - Unity mínimo AGUANTA → es NUESTRO BUILD (tamaño del wasm, nuestro C#, Addressables, URP) → HAY fix.
>
> La investigación **NO está cerrada**. Ver § "Frentes abiertos" al final.

## El test

Build WebGL de una **escena vacía** (cámara con clear azul + 1 cubo + 1 luz direccional; sin
acuario, sin peces, sin shaders nuestros, sin bundles), desplegado a R2 como
`Build/webgl-output-empty.*` y casteado con el mismo receiver de producción en RUNG 2 (`unity:true`).
Confirmado visualmente en la TV: **cubo azul, no el acuario** → se cargó el build vacío de verdad.

## El resultado

| Contenido | Corte |
|---|---|
| Acuario completo (RUNG 2 original) | 198 s |
| **Escena vacía (RUNG 22)** | **217.4 s** |

Mismo rango que toda la serie histórica (153–217 s). La diferencia de 19 s es ruido — los cortes ya
variaban entre 153 y 209 s en runs consecutivos con contenido idéntico.

## Estado del receiver AL MORIR (log completo capturado)

- `Unity loaded ✅` a 33.8 s · STALL de 5809 ms solo en el arranque del engine.
- **Memoria PLANA todo el run:** `WASM:64MB JS:98MB` desde MEM#1 hasta MEM#37 (sin fuga, sin OOM).
- Hilo **responsivo**: `lag peor=207ms` en el último tick.
- Vídeo keepalive **reproduciendo** hasta el final (`KA pm PLAYING ✅`, `cmp:PLAYING`).
- Canal de streaming **sano**: `sent=134 fail=0`.
- Sin eventos de página, sin errores, sin razón de cierre.

Es decir: **la conexión se tira con el receiver perfectamente vivo y sano.**

## Conclusión (acotada — ver corrección arriba)

El disparador **no está en la escena**. Sigue vivo en el build de Unity que ejecuta — no nuestro contenido, no nuestra
escena, no nuestros shaders, no los bundles. Una escena con un cubo corta igual que el acuario entero.

Combinado con la bisección de 21 escalones (ninguna operación aislada ni combinada desde JS lo
reproduce: contexto WebGL, huella GPU, fences, FBO 1440p, compilación WASM, instanciación con
memoria 64→512MB, saturación de cores, descarga de 64MB… todos aguantaron >5 min), la conclusión es:

> **Cap duro de la plataforma Cast del Xiaomi TV Box S, disparado por la ejecución del runtime
> WASM/emscripten de Unity. Infixeable desde la app.**

**No queda nada que bisecar en `TvScene`.** Cerrar la vía técnica.

## Qué queda (decisión de producto, no técnica)

1. **Aceptar el corte + reconexión** — el CastPlugin móvil ya reconecta (~5 s) y el receiver ya hace
   re-INIT en reconexión rápida. Coste: parpadeo cada ~3 min. *El user rechazó esta opción en su día
   por UX; ahora es esto o cambiar de stack.*
2. **Chrome Remote Debugger** (`IP:9222`) — vería el código de cierre real del WebSocket de
   transporte. Requiere registrar el Xiaomi en la Cast Console. Es diagnóstico, **no** un fix:
   como mucho confirma firmware o revela un workaround conocido de terceros.
3. **Cambiar de stack** — receiver no-Unity (three.js/WebGL nativo). Los RUNGs 11/12/17/19/20/21
   demuestran que WebGL crudo con carga GPU real aguanta >9 min. Reescritura completa del render.

## Artefactos del test (limpieza pendiente, inofensivos)

- R2: `Build/webgl-output-empty.*` (4 ficheros) — nadie los carga, se pueden borrar cuando se quiera.
- R2 `index.html`: **rollback ya hecho** y verificado byte-idéntico al backup previo.
  Producción `Build/webgl-output.wasm` (44.250.183 B, 23-jun) **nunca se tocó** — verificado por ETag
  antes y después del deploy.
- Local: `webgl-output-empty/` (gitignorado), `Assets/_EmptyCastTest/` (escena regenerable).
- Backup del receiver previo: `scratchpad/r2-index-backup-2026-07-21.html`.
- ⚠ **Pendiente real de producción:** el `index.html` vivo sigue siendo un receiver de DIAGNÓSTICO
  (panel debug visible + 22 rungs). Antes de cualquier uso real hay que desplegar un receiver limpio.

---

## 🔬 FRENTES ABIERTOS (2026-07-21) — la investigación NO está cerrada

Lo que la bisección de 22 escalones agotó es la **superficie JS-proxy** y el **contenido de la escena**.
Quedan ejes enteros sin tocar, y varios son baratos.

### Activo nuevo: tenemos un REPRODUCTOR MÍNIMO
El build de escena vacía **reproduce el corte** (217 s) y se rebuildeó en ~1 h. Eso convierte los
Player Settings en variables A/B testeables por primera vez — antes cada intento costaba el build
del acuario entero.

### F1 · ¿Es solo el Xiaomi? (coste ~0, valor de producto máximo)
Castear el receiver actual a **otro dispositivo** (Chromecast con Google TV, otra Android TV, Cast
Built-in de otra marca). Toda la investigación se ha hecho sobre UN device. Si el corte no ocurre en
otros, el problema es de firmware Xiaomi y el producto puede salir.

### F2 · Unity MÍNIMO de verdad (la bifurcación decisiva)
Proyecto Unity **nuevo y limpio**: hello-world, Built-in RP (no URP), sin Addressables, sin nuestro
C#. Objetivo: `.wasm` de ~8 MB en vez de 44 MB. Castearlo igual.
- **CORTA** → es el engine core / runtime emscripten → escalar a Unity y/o Google, o cambiar stack.
- **AGUANTA** → es **nuestro build**: tamaño del wasm, nuestro código C#, Addressables o URP → HAY fix
  y se bisecta por partes.

No toca este repo (proyecto aparte). Build mucho más rápido que el nuestro.

### F3 · Knobs de Player Settings sobre el rig vacío (1 rebuild cada uno)
Ninguno testeable por proxy JS; todos plausibles:
- **Disable Unity Audio** — RUNG 16 vio que Unity crea `AudioContext` real (~23 s). RUNG 4 solo lo
  *suspendía después* de crearlo, y RUNG 17 lo creó sin engine. **Nunca se ha probado un Unity que
  jamás cree AudioContext.** El foco de audio interactúa con el ciclo de vida de apps en Android TV.
- **`PlayerSettings.WebGL.powerPreference` → LowPower** — RUNG 17 lo falsó *aislado*, no combinado
  con el engine real.
- **WebGL 1.0 en vez de 2.0** — cambia el path GL entero.
- **Initial Memory 32 MB / otra Memory Growth Mode.**
- **Decompression fallback / Compression Format.**

### F4 · Versiones
- **Otra versión de Unity** (2022 LTS ⇒ emscripten distinto). Caro pero es un eje real.
- **Versión del CAF Receiver SDK** — ¿se puede pinnear `cast_receiver_framework.js`?

### F5 · Chrome Remote Debugger (`IP:9222`)
Infravalorado antes. Es la única vía de ver el **código de cierre real** del WebSocket de transporte.
Nombrar el mecanismo (heartbeat, kill del WebView, lifecycle) es lo que permite buscar el workaround.
Requiere registrar el nº de serie del Xiaomi en la Cast Console.

### F6 · Research externo
Casos conocidos de Unity WebGL sobre Cast, límites documentados de la plataforma, comportamiento del
`cast.tp.heartbeat`. (Lanzado 2026-07-21.)

---

## ▶▶ 2026-07-22 — EMPEZAR POR `CAST_NEXT_SESSION_2026-07-22.md`

Handoff turnkey con: el research que reabrió la investigación (WebGL no soportado oficialmente en
Web Receiver · el watchdog de Cast mide `MemAvailable` del SISTEMA, no el heap ⇒ nuestros
indicadores de "receiver sano" son ciegos · `maxInactivity:3600` contraproducente · heartbeat en el
proceso nativo · `STANDBY_CHANGED`/`VISIBILITY_CHANGED` sin loguear · adb Android TV = 4321/5555 no
9222 · **Cast Connect reaprovecha Unity entero**), el plan de tests baratos (captura forense adb —
script listo en `Tools/cast-adb-capture.sh`), el bloqueo actual (activar depuración por red en la
caja) y el estado completo del entorno.
