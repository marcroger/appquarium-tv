# Cast Disconnect Investigation — Appquarium TV

> Iniciada: 2026-06-27 | Última actualización: 2026-06-28  
> Branch activo: `feat/netflix-architecture`

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
