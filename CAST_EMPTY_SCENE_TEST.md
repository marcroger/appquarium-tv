# Test de ESCENA VACÍA — Cast disconnect ~3min (RUNG 22)

> Preparado 2026-07-20 · retomar 2026-07-21. Ver `CAST_DISCONNECT_INVESTIGATION.md` para el contexto completo (bisección de 21 escalones).

## Por qué este test

Tras 21 escalones, **el corte a ~3min lo dispara el motor WASM de Unity ejecutándose, y NO es reproducible desde JavaScript** (ni combinando todo: contexto+GPU+fences+FBO 1440p+present 1080p → RUNG 21 aguantó >5min). El disparador vive dentro del engine compilado / runtime emscripten / present real.

**Última bifurcación con posible fix:** ¿es el **engine core** de Unity (cualquier build WebGL corta) o **nuestro contenido** (la escena TvScene: acuario, peces, shaders, bundles)?

- Escena vacía **CORTA ~3min** → engine core → **infixeable desde la app** (aceptar reconexión / cambiar de stack).
- Escena vacía **AGUANTA >4min** → **es nuestro contenido → HAY fix** (iterar sobre la escena: qué subsistema, shader o patrón de render lo dispara).

## ⚠ Rollback-safe por diseño (por qué NO puede romper producción)

- El build vacío va a la carpeta **aparte** `webgl-output-empty/`; **NO toca** `webgl-output/` (prod).
- Los ficheros se llaman **`webgl-output-empty.*`** → **NO colisionan** con `webgl-output.*` de prod, ni en disco ni en R2 `/Build/` (coexisten).
- El player de prod en R2 `/Build/webgl-output.*` (build 06-23b, 44.250.183 B) queda **intacto** — se verificó byte-idéntico al local `webgl-output/`.
- Solo se intercambia **`/index.html`** (que YA es un receiver de test, no producción).
- **Rollback total = 1 comando** (re-desplegar un receiver limpio a `/index.html`). El player de prod nunca se toca.

## Estado preparado (2026-07-20)

- ✅ `Assets/Editor/TvEmptyTestBuild.cs` — menú **`Appquarium TV → 🧪 Build Empty Cast Test (rollback-safe)`**. Crea escena mínima (cámara azul + cubo + luz), la guarda en `Assets/_EmptyCastTest/EmptyCastTest.unity`, restaura la escena abierta, y hace `BuildPipeline.BuildPlayer` SOLO de esa escena a `webgl-output-empty/` con los PlayerSettings activos. NO modifica EditorBuildSettings/PlayerSettings.
  - ⚠ **COMPILACIÓN SIN VERIFICAR** — el `recompile_scripts` de MCP hizo timeout (Unity ocupado). **Paso 0 de mañana: confirmar que compila sin errores** (Console limpia) antes de construir.
- ✅ `Tools/rcv-empty-test.html` — receiver de test, sintaxis validada (0 errores). Idéntico al diagnóstico `rcv-prod-config.html` pero con `createUnityInstance` apuntando a `Build/webgl-output-empty.*`. Se castea **RUNG 2 (unity:true)** para cargar el Unity vacío.
- Harness: `Tools/sender-video.html` en `localhost:3003` (arrancar server node DETACHED si está caído — ver abajo).

## PASOS MAÑANA (turnkey)

### 0. Verificar compilación
En Unity: Console sin errores de `TvEmptyTestBuild.cs`. Si hay error de API, corregir antes de seguir.

### 1. Confirmar target WebGL + construir
- `File > Build Settings` → target = **WebGL** (el script aborta con diálogo si no).
- Ejecutar menú **`Appquarium TV → 🧪 Build Empty Cast Test (rollback-safe)`** → aceptar el diálogo.
- Salida esperada: `webgl-output-empty/Build/webgl-output-empty.{loader.js,data,framework.js,wasm}`.
- ⏱ Debería tardar bastante MENOS que el acuario (no hay assets/bundles; solo el engine). SBP cache ayuda.

### 2. Desplegar (boto3, rollback-safe — nombres distintos, NO pisan prod)
```bash
cd /d/dev/appquarium-tv-unity
export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"
export AWS_RESPONSE_CHECKSUM_VALIDATION="when_supported"
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
cl = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
B='webgl-output-empty/Build/'
files=[
  ('webgl-output-empty.loader.js','Build/webgl-output-empty.loader.js','application/javascript'),
  ('webgl-output-empty.framework.js','Build/webgl-output-empty.framework.js','application/javascript'),
  ('webgl-output-empty.data','Build/webgl-output-empty.data','application/octet-stream'),
  ('webgl-output-empty.wasm','Build/webgl-output-empty.wasm','application/wasm'),
]
for local,key,ct in files:
    cl.put_object(Bucket='appquarium-tv', Key=key, Body=open(B+local,'rb').read(), ContentType=ct, CacheControl='public, max-age=3600')
    print('OK Build:', key)
# receiver de test → /index.html (reemplaza el diagnóstico; prod /Build/ intacto)
cl.put_object(Bucket='appquarium-tv', Key='index.html', Body=open('Tools/rcv-empty-test.html','rb').read(),
    ContentType='text/html', CacheControl='public, max-age=30')
print('OK receiver: index.html = rcv-empty-test')
"
```

### 3. Castear
- Arrancar server si está caído: `Start-Process node -ArgumentList "Tools\sender-video-server.js" -WorkingDirectory "D:\dev\appquarium-tv-unity" -WindowStyle Hidden`
- **Reiniciar el Xiaomi** (número limpio + bustear caché).
- `http://localhost:3003` → **RUNG 2** (Unity ON) → cast → Xiaomi. Verificar **"CONECTADO (rung 2)"**.
- **En la TV: azul + un cubo** (NO el acuario) = confirma que carga el build VACÍO. Si sale el acuario → caché viejo, reiniciar Xiaomi.
- Dejar >4 min o hasta que corte.

### 4. Leer resultado
- **CORTA ~180-216s** → engine core de Unity → no hay fix desde la app. Cerrar: aceptar reconexión (o replantear stack). Fin de la investigación.
- **AGUANTA >4min** → 🎯 **es nuestro contenido** → hay fix. Siguiente: bisecar la escena TvScene (quitar post-processing / agua / peces / cámara / subsistemas uno a uno en rebuilds hasta encontrar el culpable).

### 5. Rollback (tras leer el resultado)
```bash
# Restaurar un receiver de PRODUCCIÓN en /index.html (el player de prod /Build/webgl-output.* nunca se tocó)
cd /d/dev/appquarium-tv-unity
export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"
export AWS_RESPONSE_CHECKSUM_VALIDATION="when_supported"
python -c "
import boto3, configparser, os
c = configparser.ConfigParser(); c.read([os.path.expanduser('~/.aws/credentials')])
cl = boto3.client('s3', endpoint_url='https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com',
    aws_access_key_id=c.get('r2','aws_access_key_id'), aws_secret_access_key=c.get('r2','aws_secret_access_key'), region_name='auto')
cl.put_object(Bucket='appquarium-tv', Key='index.html', Body=open('scratchpad/r2-index-backup-KA9probe.html','rb').read(),
    ContentType='text/html', CacheControl='public, max-age=60')
print('OK: index.html restaurado (KA9-probe backup)')
"
```
⚠ El backup `scratchpad/r2-index-backup-KA9probe.html` es un receiver de DIAGNÓSTICO (panel debug visible). Para producción REAL de verdad hace falta un receiver limpio (auto-hide del panel + sin los 22 rungs) — tarea de finalización aparte, NO bloquea el test. Los ficheros `webgl-output-empty.*` en R2 se pueden borrar luego (inofensivos).

## Hipótesis (prior)
Dado que los 21 rungs apuntan al engine, lo MÁS probable es que la escena vacía **también corte** (→ engine core). Pero si aguanta, es el mejor resultado posible: nuestro contenido es fixeable. Merece la pena verificarlo, no asumirlo.

---

# 🏁 RESULTADO — 2026-07-21

**CORTÓ a 217.4s** (acuario completo = 198s). Mismo rango → **es el engine core de Unity, NO nuestro contenido.**

Cubo azul confirmado en la TV (no era caché). Receiver 100% sano al morir: memoria plana 64/98MB,
hilo responsivo, vídeo keepalive playing, streaming sin fallos.

**Investigación CERRADA.** No queda nada que bisecar en TvScene. Veredicto completo, log y opciones
de producto en `CAST_DISCONNECT_INVESTIGATION.md` § VEREDICTO FINAL.

Rollback ejecutado y verificado byte-idéntico. Prod `Build/webgl-output.*` nunca se tocó (ETag
verificado antes y después). Backup usado: `scratchpad/r2-index-backup-2026-07-21.html` (el que citaba
este spec, `r2-index-backup-KA9probe.html`, NO existía — se creó descargando el vivo de R2).
