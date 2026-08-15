# CAST_KEEPALIVE_VIDEO_SPEC — Vídeo silencioso en loop como keepalive de sesión

> Creada: 2026-07-02 | Branch: `feat/netflix-architecture`
> Estado: **PASO 1 listo para probar** (sin rebuild de Unity)
> Relacionado: `CAST_DISCONNECT_INVESTIGATION.md`, memoria `cast_video_keepalive_fix.md`

---

## 0. TL;DR

La sesión Cast se corta a **~150s** en el Xiaomi TV Box S (code 2055). 9 fixes receiver-side + 1 sender fallaron — **todos eran audio o mensajes de estado**. Un **vídeo real reproduciéndose** (mp4 negro en loop) SÍ mantiene el sender vivo: probado standalone (sin Unity) **360s+ y sin caer**. El integrado (con Unity) mejora a ~205s pero aún cae. Esta spec cierra las 2 incógnitas por bisección y, si se confirma, integra el vídeo en producción.

---

## 1. Veredicto de la investigación (2026-07-02)

**El vídeo keepalive NO es una solución oficial documentada** — es un hack. La búsqueda web confirmó:

- Google solo documenta `disableIdleTimeout` y `maxInactivity` para receivers sin media. **Ambos ya los usamos y fallan** en el Xiaomi (Chromecast built-in con timeout OEM propio). La doc dice que `disableIdleTimeout` aplica *"cuando queda idle después de que para la reproducción activa"* → está atado al concepto de reproducción de media.
- **Code 2055 no existe** en la doc pública de error codes → comportamiento interno no controlable por API.
- **Nadie documenta el truco del vídeo/audio en loop** como keepalive. No es canónico.

**Conclusión:** no hay solución "bendecida" por Google. Pero la medición empírica propia (vídeo real = 360s+ vs audio/estado = corte) es más fuerte que cualquier doc, y encaja con la heurística OEM "¿hay reproducción de vídeo activa?" (lo mismo que hace que YouTube/HBO no se corten en el mismo device). **Es el mejor — y único — lead que funciona.**

Fuentes: [CastReceiverOptions](https://developers.google.com/cast/docs/reference/web_receiver/cast.framework.CastReceiverOptions) · [Core Features](https://developers.google.com/cast/docs/web_receiver/core_features) · [Custom player CAF (rbf.dev)](https://rbf.dev/blog/2023/01/custom-player-cast-receiver-framework/) · [Error codes](https://developers.google.com/cast/docs/web_receiver/error_codes)

---

## 2. La receta que funciona (validada standalone 2026-06-30)

- **Clip:** `keepalive_black.mp4` — 320×240, H.264 baseline + AAC silencioso, 10.5 KB. Ya en R2 raíz (`max-age=604800`, verificado 200 OK 2026-07-02).
  `https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/keepalive_black.mp4`
- **Elemento:** `<cast-media-player>` + `ctx.getPlayerManager()` → en `SENDER_CONNECTED`, `pm.load(LoadRequestData{media: video/mp4, autoplay:true})`.
- **Forzar reproducción real** en el `<video>` interno: `loop=true; muted=true; play()`.
- **Respaldo:** un `<video id="ka-fallback">` explícito de pocos px con `src` directo al mp4 (frames reales garantizados aunque el CAF player falle).
- **Watchdog cada 3s:** re-arranca si `paused`, reintenta `pm.load` hasta 8×.

### ⚠ Regla de oclusión (crítica)
El navegador **throttlea/pausa un `<video>` ocluido**. El vídeo DEBE estar:
- **Visible ≥1px, encima del canvas de Unity** (`z-index` > canvas), `opacity` casi 0.
- **NUNCA** `display:none`, `visibility:hidden`, ni detrás con `z-index:-1`.

Esta es la causa probable de que `rcv-30c` (vídeo detrás del canvas) solo llegara a 205s: el compositor lo consideraba oculto.

---

## 3. Las 2 incógnitas (por qué el integrado cae a 205s y no 360s+)

1. **Mensajes extra** receiver→sender que el standalone NO tiene: `setApplicationStatus('Appquarium Active')` cada 20s + custom `KEEPALIVE` cada 30s + `PONG`. Sospecha nº1: `setApplicationStatus` pisa el estado "media activa".
2. **Unity WebGL** starva el decode del vídeo o el heartbeat de Cast bajo carga GPU/main-thread.

---

## 4. Plan de bisección (1 cast por paso, mirando la TV)

### PASO 1 — integrado + vídeo, SIN mensajes extra + log de `currentTime`  ← **listo**
Vídeo activo (receta §2). **Quitados**: `setApplicationStatus` (inmediato + interval 20s), `KEEPALIVE` 30s, `PONG`. **Añadido**: log de `video.currentTime` (cast-media-player + fallback) cada 10s.

- Fichero: `webgl-output/index_ka_step1.html` (sello `rcv 2026-07-02 KA1`).
- **Un solo cast**, observar la TV y el panel debug. Este paso resuelve las DOS incógnitas a la vez:

| Resultado | Interpretación | Siguiente |
|---|---|---|
| Aguanta **>4min** | Los mensajes extra eran la causa | → §5 integrar (borrar mensajes + vídeo) |
| Cae **~205s**, `currentTime` **congelado** antes del corte | Unity starva el decode del vídeo | → PASO 2a (mitigar oclusión/re-play) |
| Cae **~205s**, `currentTime` **avanza** hasta el corte | Unity starva el heartbeat Cast (main thread) | → PASO 2b (yield / bajar targetFrameRate en chequeos) |

### PASO 2a — si el decode se congela
Reubicar el vídeo a un layer que el compositor no considere oculto (probar tamaño mayor, distinto z-index, o `requestVideoFrameCallback` para confirmar frames). Re-`.play()` más agresivo.

### PASO 2b — si el heartbeat se starva
Investigar contención del main thread de Unity con el hilo de Cast. Opciones: reducir `targetFrameRate` durante ventanas de keepalive, o yield explícito.

---

## 5. Integración en producción (SOLO tras confirmar §4)

1. Portar los cambios ganadores del `index_ka_step1.html` a **`webgl-output/index.html`** (deploy inmediato sin rebuild).
2. ⚠ **Sincronizar el template** `Assets/WebGLTemplates/CastReceiver/index.html` — está DRIFTED en `rcv 2026-06-26b` vs live `28g`. Portar TODO (fixes acumulados + vídeo) para que el próximo rebuild de player no revierta nada.
3. Bump del sello `rcv` a fecha de integración.
4. Vigilar efecto secundario: posible **notificación de media en el móvil** (como YouTube). Confirmar con el user si molesta.
5. Avisar al Claude del móvil (está al día y en espera): si el vídeo resuelve el corte, el Plan B (suprimir popup) queda como fallback innecesario.

---

## 6. Deploy del PASO 1 a R2 (sin rebuild)

`index.html` es independiente del player → se sube directo. **Guardar el 28g antes** (backup: `webgl-output/index.html` local sigue siendo el 28g limpio).

```bash
# Subir el receiver del paso 1 como /index.html en R2 (boto3 — evita bug CRC64NVME de AWS CLI 2.23+)
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
client = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
client.put_object(Bucket='appquarium-tv', Key='index.html',
    Body=open('webgl-output/index_ka_step1.html','rb').read(),
    ContentType='text/html', CacheControl='public, max-age=60')
print('OK: index.html (KA step1)')
"
```

### Restaurar producción limpia (28g) tras el test
```bash
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
client = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
client.put_object(Bucket='appquarium-tv', Key='index.html',
    Body=open('webgl-output/index.html','rb').read(),
    ContentType='text/html', CacheControl='public, max-age=60')
print('OK: index.html restaurado a 28g')
"
```

> ⚠ El Cast puede cachear el index viejo. Confirmar en la TV el sello `rcv 2026-07-02 KA1` (esquina inf. derecha). Si sigue mostrando `28g`, reiniciar el Xiaomi antes de dar el test por válido.

---

## 7. Criterios de aceptación del PASO 1

- La TV muestra el sello **`rcv 2026-07-02 KA1`**.
- Panel debug muestra `KA: cmp▶ ct=…` avanzando (vídeo reproduciendo de verdad).
- **Éxito claro:** sesión Cast viva **>4min** sin popup en el móvil → los mensajes eran la causa.
- **Diagnóstico si cae ~205s:** anotar si `ct` (currentTime) estaba congelado o avanzando en el último log antes del corte → decide PASO 2a vs 2b.
