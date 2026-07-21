# Handoff — Drop de Cast a ~152s (investigado desde el sender 2026-06-27)

> Pega el bloque "PROMPT PARA MAÑANA" de abajo en una sesión de Claude Code **dentro del proyecto TV** (`D:\dev\appquarium-tv-unity`). Adjuntos en esta carpeta: `popup_cast_desconectado_148s.jpeg` (pantallazo del popup de debug del móvil) y `logcat_castplugin.txt` (logcat del sender).

---

## Resumen de la investigación (lado sender / móvil — ya hecho, NO repetir)

Síntoma: al castear desde la app Android (Appquarium) a la TV (Chromecast/Decodificador Xiaomi), la sesión **se corta sola a ~148–152s**, reproducible.

Diagnóstico desde el móvil (descarta todo salvo el receptor):
- **Móvil/canal: sano.** PING keepalive a t=120s OK; sin `sendMessage FAILED`; flag canal-muerto=0.
- **Red/WiFi: estable.** Cero eventos wifi/wlan/roam/supplicant en el segundo del corte.
- **No es idle-timeout estándar.** Sería 300s exactos; esto es ~150s, número no-redondo, determinista.
- **Código 2055 = genérico.** `CastStatusCodes.getStatusCodeString(2055)` devuelve el texto de fallback "Cast controller status code 2055" → 2055 no es un código documentado del SDK.
- Log clave: `onSessionEnded after 152s — Cast controller status code 2055 (2055)` (ver `logcat_castplugin.txt`).

**Conclusión: el corte lo provoca el RECEPTOR (WebGL en la TV), determinista a ~150s. Firma típica de OOM/crash del WebGL del receptor.**

## Bandera roja: el receptor desplegado en R2 está CADUCADO
- El banner "⚠ ÚLTIMA CAÍDA" **nunca aparece** en la TV, pero el código que lo escribe (`saveLastDisco` → localStorage) SÍ está en el template local `Assets/WebGLTemplates/CastReceiver/index.html` (etiqueta `rcv 2026-06-26b`, líneas ~185-225, 454, 487).
- O sea: lo que está VIVO en R2 es **anterior** a la instrumentación. Estamos depurando a ciegas contra un build viejo.
- El template local YA tiene `opts.disableIdleTimeout = true` + `opts.maxInactivity = 3600` (index.html ~500-501) y todo el instrumentado (panel HEAP, SHUTDOWN handler, banner). Pero **`rcv 2026-06-26b` figura como pendiente de rebuild WebGL + redeploy R2.**

---

## PROMPT PARA MAÑANA (pegar en sesión del proyecto TV)

```
Contexto: el Cast de Appquarium se corta solo a ~148-152s, reproducible. Ya investigué
desde el móvil (sender) y está descartado: NO es la red, NO es el canal del sender, NO es
idle-timeout estándar (sería 300s; esto es ~150s no-redondo). El receptor (WebGL en la TV)
cierra la sesión de golpe con código 2055 genérico → firma de OOM/crash del receptor.
Evidencia en _cast_drop_2026-06-27/ (pantallazo popup + logcat_castplugin.txt).

Sospecha extra: el receptor VIVO en R2 está caducado — el banner "ÚLTIMA CAÍDA" nunca
aparece, pero su código (saveLastDisco) ya está en el template local rcv 2026-06-26b.

Quiero que hagas, en orden:

1. VERIFICAR DESPLIEGUE. Confirma qué build del receptor está realmente servido en R2 vs el
   template local (Assets/WebGLTemplates/CastReceiver/index.html, rcv 2026-06-26b). Si está
   viejo: rebuild WebGL del receptor + redeploy a R2 con cache-bust, y dime el comando exacto
   y cómo verificar (al castear debe verse la etiqueta rcv 2026-06-26b arriba + el panel HEAP).

2. CAZAR EL LEAK/OOM. Audita los scripts Unity de la escena receptor (CastReceiver / TvScene /
   CastDataTypes / el handler de mensajes add_fish/add_deco/feed/startle/ambient/change_*).
   Busca el patrón que causaría un OOM determinista a ~150s: spawnear objetos en cada update/
   INIT/PING sin destruir los viejos (peces, comida/FoodItem que no despawnea, partículas,
   burbujas, materiales/texturas no liberados, listas que crecen). El sender manda PING cada
   60s + updates; algo en el receptor acumula memoria hasta reventar el heap de Unity WebGL.

3. CONFIRMAR. Tras desplegar el receptor bueno, propón cómo confirmar OOM: panel HEAP del
   receptor (¿sube hasta el techo antes de los 150s?) o Chrome remote debugging
   (chrome://inspect → inspeccionar receptor → pestaña Memory). 

Empieza por el punto 1 (saber qué hay desplegado) y el 2 (leak) en paralelo. No toques el
proyecto móvil (D:\dev\appquarium-unity): el sender ya está bien.
```

---

## Notas de referencia (proyecto TV)
- Template receptor: `Assets/WebGLTemplates/CastReceiver/index.html` (rcv 2026-06-26b).
- `CastReceiverOptions`: `disableIdleTimeout=true`, `maxInactivity=3600` (ya correctos en el template).
- Deploy R2: `aws s3 sync ServerData/WebGL/ s3://appquarium-tv/bundles/` (ver SYNC_NOTES.md / CAST_NETFLIX_SPEC.md del proyecto TV para el flujo exacto WebGL base vs bundles).
- Wire format ya alineado con el sender (ageScale, tankHalfWidth) — no es el problema.
- El sender (móvil) NO necesita cambios; `CastPlugin.java` ya tiene auto-reconexión + diagnóstico.
