# Cast Disconnect — Mobile Handoff Brief

> Creado: 2026-06-28 | TV project: `D:\dev\appquarium-tv-unity` | Mobile project: `D:\dev\appquarium-unity`

Este documento es un brief completo para continuar la investigación/fix del disconnect Cast **en el proyecto móvil**. Todo el trabajo receiver-side ya está agotado (8 fixes, todos fallaron). El fix pendiente está en Android.

---

## Contexto del problema

La app Android (Appquarium) castea a un Xiaomi TV Box S. La sesión Cast se cae sola a **~150 segundos** de forma 100% reproducible. El receiver (Unity WebGL en la TV) no crashea — sobrevive el disconnect y sigue corriendo. El CastPlugin ya tiene auto-reconnect que funciona, pero el usuario percibe el ciclo.

**Evidencia logcat (2026-06-27):**
```
23:17:02  D CastPlugin: onSessionStarted
23:19:02  D CastPlugin: Cast keepalive ping t=120s
23:19:34  W CastPlugin: onSessionEnded after 152s — Cast controller status code 2055 (2055)
23:19:34  D CastPlugin: WakeLock released
23:19:37  W CastPlugin: auto-reconnect attempt 1/3
```

Código 2055 = código interno del GMS, no documentado. El GMS de Android cierra la sesión porque el receiver Cast nunca entró en estado `PLAYING` (reproduciendo media). El Xiaomi TV Box S tiene un timeout OEM de ~150s para receivers sin media activa.

---

## Lo que YA está hecho y NO tocar

### En el receiver (TV — NO MODIFICAR, ya exhausto)
El receiver `webgl-output/index.html` ya tiene todos estos fixes acumulados (ninguno resolvió el disconnect):

| Fix | Descripción |
|---|---|
| `disableIdleTimeout=true` + `maxInactivity=3600` | Config estándar Cast |
| `getPlayerManager()` | Registrar media player antes de `ctx.start()` |
| `broadcastStatus()` | MEDIA_STATUS cada 30s |
| PING→PONG | El receiver responde PONG a los PING del sender |
| `setApplicationStatus()` | RECEIVER_STATUS cada 20s |
| `setMediaElement` + `play()` | Audio silencioso blob (posible fallo autoplay) |
| `PlayerManager.load()` | silence.wav desde R2 — flujo LOAD completo |
| Overlay diferido 90s | No muestra el overlay hasta 90s sin reconnect |
| INIT skip | Si INIT llega <30s tras disconnect y Unity ya cargado → se descarta (no recarga el acuario) |

**Conclusión**: el Xiaomi TV Box S ignora todos estos intentos a nivel de firmware. El timeout es irremediable desde el receiver JS.

### En el sender (CastPlugin.java — ya implementado)
```
D:\dev\appquarium-unity\Assets\Plugins\Android\appquarium.androidlib\src\main\java\com\appquarium\app\CastPlugin.java
```
- `PARTIAL_WAKE_LOCK` — adquirido en `onSessionStarted`, liberado en `onSessionEnded` ✅
- PING cada 60s (`KEEPALIVE_INTERVAL_MS = 60_000L`) — fire-and-forget, no espera PONG ✅
- Auto-reconnect 3 intentos × 3s delay — **CONFIRMADO FUNCIONANDO** ✅

---

## El fix a implementar

### Hipótesis

El GMS de Android cierra la sesión porque ve el receiver en estado `IDLE` (sin media) durante ~150s. La solución es que el **sender Android** cargue un ítem de media falso via `RemoteMediaClient`, poniendo la sesión en estado `PLAYING` desde el punto de vista del GMS.

Esto es diferente de lo que hemos hecho en el receiver (donde `PlayerManager.load()` carga media desde el lado JS del receiver). Aquí el **sender Android** envía un request LOAD al receiver a través del protocolo Cast oficial, que el GMS monitoriza directamente.

### Cambio a implementar en CastPlugin.java

Añadir esto en `onSessionStarted`, después de adquirir el WakeLock:

```java
// Imports necesarios (añadir al top del fichero si no están):
import com.google.android.gms.cast.MediaInfo;
import com.google.android.gms.cast.MediaLoadOptions;
import com.google.android.gms.cast.framework.media.RemoteMediaClient;

// En onSessionStarted, después de acquireWakeLock():
private void loadSilenceMedia(CastSession session) {
    RemoteMediaClient mediaClient = session.getRemoteMediaClient();
    if (mediaClient == null) {
        Log.d(TAG, "RemoteMediaClient null — no media load");
        return;
    }
    MediaInfo mediaInfo = new MediaInfo.Builder(
            "https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/silence.wav")
        .setContentType("audio/wav")
        .setStreamType(MediaInfo.STREAM_TYPE_BUFFERED)
        .build();
    MediaLoadOptions loadOptions = new MediaLoadOptions.Builder()
        .setAutoplay(true)
        .setPlayPosition(0)
        .build();
    mediaClient.load(mediaInfo, loadOptions)
        .setResultCallback(new ResultCallback<RemoteMediaClient.MediaChannelResult>() {
            @Override
            public void onResult(@NonNull RemoteMediaClient.MediaChannelResult result) {
                if (result.getStatus().isSuccess()) {
                    Log.d(TAG, "Silence media loaded → GMS sees PLAYING");
                } else {
                    Log.w(TAG, "Silence media load FAILED: " + result.getStatus().getStatusCode());
                }
            }
        });
}
```

Y llamarlo desde `onSessionStarted`:
```java
@Override
public void onSessionStarted(CastSession session, String sessionId) {
    // ... código existente ...
    acquireWakeLock();
    loadSilenceMedia(session);   // ← AÑADIR ESTA LÍNEA
    startKeepalive(session);
}
```

### Alternativa si RemoteMediaClient no está disponible

Si `session.getRemoteMediaClient()` devuelve null (puede pasar en algunos builds), la alternativa es registrar la app como media-aware en el `CastOptions`:

```java
// En la clase que configura CastOptions (buscar OptionsProvider):
@Override
public CastOptions getCastOptions(Context context) {
    return new CastOptions.Builder()
        .setReceiverApplicationId("8F6C873F")
        // Esto registra la app como compatible con media — el GMS no aplica
        // el timeout de "no media" a sesiones con LaunchOptions correctos
        .setLaunchOptions(new LaunchOptions.Builder()
            .setRelaunchIfRunning(false)
            .build())
        .build();
}
```

---

## El fichero silence.wav

Ya está subido a R2 y accesible públicamente:
```
https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/silence.wav
```
- 32 KB, 4 segundos, 8 kHz, 8-bit, mono, WAV PCM
- `Cache-Control: public, max-age=31536000`
- CORS: `AllowedOrigins: ["*"]` en el bucket R2

No hay que subir nada más. El fichero ya existe.

---

## Contexto del receiver — lo que hace tras el disconnect

Para que el fix sea transparente al usuario, el receiver ya tiene:

1. **INIT skip**: si el sender reconecta en <30s y el acuario ya está corriendo → el INIT del nuevo sender se descarta silenciosamente. El acuario NO recarga.

2. **Overlay diferido 90s**: el overlay de "Sender desconectado" no aparece hasta 90s sin reconnect. Si el CastPlugin reconecta antes (incluso si tarda 60s), el usuario no ve nada.

3. **PONG**: el receiver responde PONG a los PING del sender inmediatamente (sin esperar a Unity).

Si el fix de `RemoteMediaClient` funciona → el disconnect nunca ocurre → el INIT skip y el overlay diferido son moot (pero los dejamos como safety net para cualquier otro tipo de disconnect).

---

## Ruta exacta del fichero a modificar

```
D:\dev\appquarium-unity\Assets\Plugins\Android\appquarium.androidlib\src\main\java\com\appquarium\app\CastPlugin.java
```

Hay una segunda copia (Gradle build output, NO tocar):
```
D:\dev\appquarium-unity\Library\Bee\Android\Prj\IL2CPP\Gradle\unityLibrary\appquarium.androidlib\src\main\java\com\appquarium\app\CastPlugin.java
```

---

## Cómo verificar que funciona

1. Instalar build nuevo en el móvil
2. Castear a Xiaomi TV Box S
3. Dejar corriendo **más de 3 minutos** (>180s) sin tocar nada
4. El debug panel del receiver (esquina inferior derecha de la TV) debe mostrar:
   - `Silence LOADED → state PLAYING` — confirma que el load llegó
   - NO debe aparecer el overlay de "Sender desconectado" en ningún momento
5. Si a los 150s sigue sin disconnect → fix funciona

Si el debug panel muestra `Silence LOADED → state PLAYING` pero el disconnect sigue → la hipótesis es incorrecta y el problema tiene otra causa.

---

## Reglas hard del proyecto móvil

- El proyecto mobile está en `D:\dev\appquarium-unity\` — proyecto **separado** del TV
- Los cambios en CastPlugin.java requieren rebuild del proyecto Android (no solo Unity)
- No sincronizar SOs/prefabs del mobile al TV sin revisión quirúrgica (hay un historial de bugs por sync en bloque)
- El TV project en `D:\dev\appquarium-tv-unity\` tiene su propia CLAUDE.md con reglas

---

## Referencias cruzadas

- `CAST_DISCONNECT_INVESTIGATION.md` (en TV project) — log completo de todos los fixes probados
- `CAST_UPDATES.md` (en TV project) — protocolo de mensajes Cast (INIT, UPDATE, PING/PONG)
- `CastPlugin.java` — leer antes de modificar para entender el ciclo de vida completo
