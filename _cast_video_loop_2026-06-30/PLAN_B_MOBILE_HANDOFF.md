> ⚠ SUPERADO 2026-06-30 (noche). Este doc daba el corte por "firmware irremediable".
> FALSO: se probó que un VÍDEO en loop mantiene el sender conectado 360s+ (standalone sin Unity).
> El corte del integrado (~205s) es por Unity y/o mensajes extra del receptor → tiene arreglo receiver-side.
> Ver `CAST_DISCONNECT_INVESTIGATION.md` §"ACTUALIZACIÓN 2026-06-30" y memoria `cast_video_keepalive_fix.md`.
> Este Plan B queda como FALLBACK solo si la bisección receiver-side no cierra el caso.

# Plan B — Hacer INVISIBLE el corte Cast (lado MÓVIL) [FALLBACK]

> Origen: proyecto TV (`D:\dev\appquarium-tv-unity`), sesión 2026-06-30.
> Este trabajo es 100% del proyecto MÓVIL/sender (`D:\dev\appquarium-unity`).
> El receptor (TV) ya está agotado y restaurado a producción limpia (`rcv 2026-06-28g`).

---

## Veredicto definitivo (no re-investigar)

El corte de la sesión Cast a **~150-205s** en el Xiaomi TV Box S (code **2055**) es **firmware del device, IRREMEDIABLE desde el receptor.**

Probado y DESCARTADO en el receptor (no repetir):
- 9 fixes receiver-side (disableIdleTimeout, maxInactivity=3600, keepalive, broadcastStatus, setApplicationStatus, audio silence.wav, etc.).
- **Vídeo keepalive (2026-06-30):** un mp4 negro en loop reproduciendo DE VERDAD como media real del CAF PlayerManager (`fb:▶ load:OK pm:PLAYING`). **El sender SIGUIÓ cayendo a 205s.** El vídeo solo ESTIRA el margen (152 → 177 → 205s) y mantiene viva la *página* del receptor, pero NO impide el corte 2055 del sender.

**Comportamiento confirmado por el usuario (2026-06-30):**
1. A ~200s sale el **popup de "desconectado" de siempre** en el móvil.
2. **Auto-reconecta solo** y funciona otros ~203s. Ciclo infinito.

→ La cura realista NO es evitar el corte, sino **transparentar el ciclo reconnect**: matar el popup.

---

## Objetivo

**Suprimir el popup `ShowCastError` ("Cast desconectado") cuando el corte es el firmware (~200s) y hay auto-reconnect en curso.** Solo mostrar error si la reconexión REALMENTE falla (app cerrada, WiFi caído).

Resultado deseado: cada ~200s el móvil reconecta solo, **sin popup**, y el acuario en la TV no se recarga (el receptor ya hace INIT-skip para reconexiones <30s). El usuario no se entera del corte.

---

## Dónde tocar (verificar líneas, pueden haber cambiado)

`D:\dev\appquarium-unity\` — `CastManager.cs`, handler **`OnCastDisconnected`** (~línea 205), que actualmente llama a `ShowCastError(...)`.

Plugin nativo: `CastPlugin.java` — el auto-reconnect (3 intentos, delay 3s) **YA funciona** (confirmado). NO hace falta tocarlo salvo para exponer el estado de reconexión si no está accesible desde C#.

---

## Lógica propuesta

En `OnCastDisconnected(reason, code)`:

1. **Distinguir el tipo de corte:**
   - `code == 2055` (o reason desconocido/no `REQUESTED_BY_SENDER`) → **corte de firmware → NO mostrar popup**, entrar en modo "reconnecting".
   - `REQUESTED_BY_SENDER` / usuario pulsó "dejar de transmitir" → comportamiento normal (teardown, sin reconnect).

2. **Modo reconnecting (corte firmware):**
   - NO llamar a `ShowCastError`.
   - (Opcional) mostrar un indicador SUTIL no-modal ("Reconectando…") o nada.
   - Lanzar/dejar correr el auto-reconnect del CastPlugin (~5s, ya existe).
   - Arrancar un **timer de gracia** (p.ej. 30-45s).

3. **Resolución:**
   - Si `OnCastConnected`/`onSessionStarted` llega DENTRO de la gracia → cancelar timer, ocultar indicador. **Cero popup.** El acuario sigue (INIT-skip en TV).
   - Si expira la gracia SIN reconectar → AHORA sí `ShowCastError` (desconexión real: app muerta, WiFi, device apagado).

4. **Anti-bucle / anti-spam:** evitar que cada ciclo de ~200s reinicie estado visible. El indicador (si lo hay) solo aparece si el reconnect tarda > X s.

---

## Notas / gotchas

- El receptor (TV) ya difiere su propio overlay 90s y hace INIT-skip <30s → **lado TV ya está listo**, no toca nada allí.
- `reason` puede llegar `null`/`unknown` en el corte firmware (visto en TV). No asumir que `2055` siempre viene limpio; tratar "desconocido" como firmware-disconnect.
- NO usar WakeLock/keepalive nuevos como "fix" — ya se probaron y no evitan el 2055.
- Validar con: castear, esperar 2-3 ciclos de ~200s, confirmar que **no sale popup** y el acuario sigue sin recargarse.

---

## Criterio de aceptación

Castear al Xiaomi y dejar 10 min: el acuario sigue, **el popup "desconectado" NO aparece** en ninguno de los ~3 ciclos de reconnect, y solo aparece error si se mata la app / se corta el WiFi a propósito.
