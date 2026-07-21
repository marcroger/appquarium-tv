# Handoff — Cast drop ~150s: probar VÍDEO en loop (la pista de HBO)

> Origen: proyecto móvil/sender (`D:\dev\appquarium-unity`), sesión 2026-06-30.
> Este trabajo es 100% del proyecto TV (receptor). El sender ya no toca nada.

---

## ⚡ ACTUALIZACIÓN 2026-06-30 — HIPÓTESIS CONFIRMADA (resultado de las pruebas)

- ✅ **Receptor mínimo SIN Unity + vídeo en loop → NO se desconecta.** El vídeo ES la cura. La pista de HBO era correcta.
- ❌ **CON Unity + vídeo → SÍ se desconecta** a ~150s igual.
- **Conclusión:** el vídeo mantiene viva la sesión, pero **Unity WebGL interfiere** con que el vídeo siga
  contando como "reproducción activa" para el OEM del Xiaomi.

**Foco actual (en curso):** hacer que el `<video>` siga reproduciendo DE VERDAD mientras Unity corre. Verificar:
- ¿`video.paused === true` o `video.currentTime` deja de avanzar en cuanto Unity arranca? (instrumentar el panel debug).
- **Throttling de vídeo ocluido/oculto:** el navegador puede pausar/throttlear un `<video>` tapado por el canvas
  de Unity o con `display:none`. Mantenerlo realmente decodificando: **visible al menos 1px, encima del canvas,
  `opacity` baja pero NO `display:none` ni `visibility:hidden`**. Probar también `requestVideoFrameCallback` para
  confirmar que sigue pintando frames.
- Contención con Unity: GL context, AudioContext suspendido por Unity, o presión de memoria que pausa el decode.
- Si el throttling es el problema: forzar repintado del vídeo, o re-`.play()` periódico, o moverlo a un layer que
  el compositor no considere oculto.

**Estado:** EN CURSO en el proyecto TV. Resto del documento = contexto e historia (sigue válido).

---

## TL;DR

La sesión Cast se corta **siempre a ~148–152s** en el Xiaomi TV Box S (code 2055).
Se han probado **9 fixes en el receptor + el silence.wav del sender (build 37): TODOS fallaron.**

**Pista nueva y decisiva (del usuario):** en el MISMO Xiaomi, **HBO y YouTube NO se cortan**.
→ El firmware del Xiaomi no mata sesiones porque sí — mata las que **no reproducen vídeo real**.
HBO/YouTube envían un stream de vídeo continuo → sesión viva indefinidamente.
Nuestro receptor (acuario WebGL) **no reproduce NINGÚN media** → el Xiaomi lo considera "parado" → corte OEM ~150s.

**Lo único que NO se ha probado nunca:** reproducir un **VÍDEO silencioso en loop** como media real,
imitando exactamente lo que hace HBO. Todos los intentos previos fueron **audio** o **mensajes de estado**.

**Tarea:** probar la hipótesis del vídeo con un **receptor de prueba mínimo** (sin rebuild de Unity),
en 2-3 casts. Si aguanta >3 min → integrarlo en el receptor real. Si no → confirmado irremediable, pasar a plan B.

---

## Síntoma exacto (confirmado 2026-06-30)

- Dispositivo: Xiaomi TV Box S, Cast SDK `3.72.446070`, App ID `8F6C873F` (Unlisted, sirve desde R2).
- Corte determinista a **148s** (varía 142–152s). Popup del sender: `session_ended:2055:148:0`.
  - `:0` final = canal del sender **sano hasta el último segundo** (no es red, no murió el canal).
- Receptor **NO muere**: HEAP/WASM estable, peces siguen, handler `SENDER_DISCONNECTED` corre. **NO es OOM.**
- code 2055 = **no documentado** en `CastStatusCodes` públicos (verificado por búsqueda web 2026-06-30). Interno del protocolo.

---

## Lo que YA se probó y FALLÓ — NO repetir

### Receptor (proyecto TV) — 9 intentos, ver `CAST_DISCONNECT_INVESTIGATION.md`
1. `disableIdleTimeout=true` + `maxInactivity=3600` (CastReceiverOptions reales) → ❌
2. `customNamespaces[NAMESPACE]=JSON` antes de `ctx.start()` → ❌
3. `ctx.getPlayerManager()` registrado (media-aware) → ❌
4. `broadcastStatus()` cada 30s (MEDIA_STATUS) + keepalive 30s → ❌
5. PING→PONG en JS → ❌
6. `ctx.setApplicationStatus()` cada 20s (RECEIVER_STATUS) → ❌
7. **Audio silencioso blob** vía `setMediaElement()` + `.play()` → ❌ (posible bloqueo de autoplay, sin confirmar)
8. **`PlayerManager.load()` con silence.wav** (R2, BUFFERED, autoplay, repeat SINGLE) → ❌
9. Overlay diferido (UX, no previene el corte) → estado actual en R2 = `rcv 2026-06-28f`

### Sender (proyecto móvil) — NO repetir tampoco
- WakeLock + keepalive PING 60s → ❌
- **build 37: `loadSilenceMedia()` con `RemoteMediaClient.load()` de silence.wav** → ❌ (probado 2026-06-30, sigue cortando)

**Patrón común de TODOS los fallos:** audio o mensajes de estado. **Nadie ha probado un VÍDEO.**

---

## Hipótesis a probar: VÍDEO silencioso en loop como media real

La diferencia entre HBO (no se corta) y nuestro acuario (se corta) es **video playback genuino**.
Intento #8 cargó silence.wav (audio) y dijo llegar a `playerState=PLAYING`, pero igual cortó.
Sospechas de por qué el audio no bastó:
- El Xiaomi puede distinguir **audio-only** de **vídeo** para su heurística de "media activa".
- O el autoplay bloqueó la reproducción real (el `<audio>` nunca sonó de verdad).

**Plan:** un `<video muted loop playsinline autoplay>` con un clip negro de 1px / pocos KB, reproduciéndose
**de verdad y en bucle infinito**, registrado como el media element del CAF PlayerManager (o simplemente
reproduciéndose en el DOM si el OEM mira el estado de media del dispositivo). `muted` es **obligatorio**
para esquivar la autoplay policy. Verificar en el panel debug que el vídeo **realmente está reproduciendo**
(no `paused`, `currentTime` avanzando).

⚠ Riesgos honestos (decírselo al usuario, está MUY quemado tras 200+ ciclos):
- No garantizado. El Xiaomi podría distinguir un loop trucado de un stream real.
- Integrarlo en el receptor real sin romper el render WebGL del acuario es fiddly (capas: vídeo oculto detrás del canvas).

---

## Estrategia de test BARATA (clave — el usuario no aguanta más rebuilds de Unity)

NO rebuildear el WebGL de Unity para cada iteración. En su lugar:

1. Crear un **receptor de prueba mínimo standalone**: un solo `test_receiver.html` con SOLO
   - el Cast Receiver Framework (`cast_receiver_framework.js`)
   - `ctx.start()` con las opciones actuales
   - el `<video muted loop autoplay>` con un clip negro diminuto (puede ser un data-URI o un .mp4 de pocos KB en R2)
   - un panel de debug minúsculo que muestre: segundos vivos, `video.paused`, `video.currentTime`, eventos de sesión
2. Servirlo en la **misma URL que el App ID `8F6C873F` ya apunta** (R2 — reemplazar temporalmente el `index.html`
   o usar un sub-path si el Cast console lo permite). **Guardar el index.html real antes** y restaurarlo después.
   - R2: bucket `appquarium-tv`, endpoint `https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com`,
     dominio público `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/`, profile `r2`.
     ⚠ AWS CLI 2.23+ rompe con R2 (CRC64NVME) — ver `BUILD_REPORT_2026-06-19.md` para el workaround.
3. El usuario castea **una vez** y observa la TV:
   - ✅ **Aguanta >3 min** → ¡es el vídeo! Integrar en el `index.html` real (template
     `Assets/WebGLTemplates/CastReceiver/index.html`), rebuild WebGL, redeploy, confirmar, y cerrar el caso.
   - ❌ **Corta igual a ~150s** → confirmado 100% irremediable desde el receptor. Restaurar index.html y pasar a plan B.

**Criterio de aceptación:** sesión Cast **viva >3 min** sin disconnect, con el vídeo reproduciendo (verificado en panel).

---

## Si el vídeo TAMPOCO funciona → Plan B (esto es del proyecto MÓVIL, no del TV)

Aceptar que el corte es firmware del Xiaomi (irremediable) y hacerlo **invisible**:
- El auto-reconnect del sender ya funciona en ~5s (confirmado).
- Suprimir el popup `ShowCastError` ("Cast desconectado") cuando hay un auto-reconnect en curso/exitoso
  (`CastManager.cs` ~línea 205 `OnCastDisconnected`).
- Opcional: reconexión proactiva del sender a ~t=120s + INIT-skip del receptor (`rcv 2026-06-28c`)
  para que el acuario no se recargue.
Esto se implementa en `D:\dev\appquarium-unity` (sender), NO aquí.

---

## Referencias en este proyecto (TV)
- `CAST_DISCONNECT_INVESTIGATION.md` — historia completa de los 9 fixes.
- `Assets/WebGLTemplates/CastReceiver/index.html` — receptor real (rcv 2026-06-28f), instrumentado (panel HEAP, banner ÚLTIMA CAÍDA, SHUTDOWN handler). `ctx.start()` ~línea 524.
- `CAST_DISCONNECT_MOBILE_HANDOFF.md` — brief previo (el de la vía sender, ya agotada).
- silence.wav (audio, ya descartado): `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/silence.wav`
- Namespace custom: `urn:x-cast:dev.unknownaerials.appquarium`
- Deploy R2 del player/receptor: ver `BUILD_REPORT_2026-06-19.md` y `BUILD_REPORT_2026-06-02.md`.
