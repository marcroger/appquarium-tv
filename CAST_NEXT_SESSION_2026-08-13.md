# ▶▶ EMPEZAR AQUÍ — sesión 2026-08-13

> Escrito al cierre del 2026-08-12, con **el build de player todavía corriendo**.
> La sesión anterior está en `CAST_NEXT_SESSION_2026-08-12.md`.

---

## 1. Lo primero: ¿sobrevivió el build?

La sesión del 12-ago lanzó el build de player en batchmode y **se cerró antes de que terminara**.
El proceso colgaba del shell de esa sesión, así que puede haber muerto con ella.

```powershell
# ¿sigue vivo?
tasklist /FI "IMAGENAME eq Unity.exe"
# ¿en qué punto está / cómo acabó?
Get-Content build-prod.log -Tail 20
```

- **Si `build-prod.log` acaba en `[ProdBuild] Succeeded · … bytes`** → el build terminó. Ir al §3.
- **Si Unity ya no corre y el log se corta a media frase** → murió. Relanzar tal cual:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe" \
  -batchmode -quit -nographics -projectPath . -buildTarget WebGL \
  -executeMethod TvProdBuild.BuildProd -logFile build-prod.log
```

El import de assets ya está cacheado en `Library/`, así que un relanzamiento **no** repite la fase
de `… M / ~36238M prepared` (12 min recorridos el 12-ago). Lo que cuesta es IL2CPP + emscripten.

⚠ Unity Editor debe estar **cerrado**: el batchmode choca con el lock del proyecto.

---

## 2. Preflight — ya verificado el 12-ago, no hace falta repetirlo salvo que se borre la Library

| Comprobación | Resultado |
|---|---|
| `Appquarium TV → 📏 Ver Code Optimization del WASM` | ✅ **`DiskSizeLTO`** |
| Shaders en Always Included (`ProjectSettings/GraphicsSettings.asset`) | ✅ los 4 |
| Consola de Unity | ✅ sin errores |
| `TvScene` dirty / working tree | ✅ limpios |

GUIDs confirmados en Always Included — **`FishShadow` era el riesgo real** (si falta, las sombras de
los peces se ven en el Editor y **no** en la TV):

```
FishUnlit      60c4ee7717958bf408b5b7f628166d09
PlanarShadow   46a24ba3b30170c4fb557014c220c79c
DecoLit        dec011710000000000000000000000ab
FishShadow     b8ea1c3a213d80948b8e737b56f46d30   ← el nuevo
```

`TvProdBuild.BuildProd` llama a `TvWasmOptimize.SetDiskSizeLTO()` por código, así que **la ruta de
batchmode fuerza el nivel correcto sola**. Es la razón para preferirla al build por GUI.

**Copia de seguridad del player vivo** (build de 27-jul, `.wasm` 25,4 MB — el que está en R2 ahora):
`…/scratchpad/player-backup-2026-08-12/Build/`. Restaurar = volver a subir esos 4 ficheros.

---

## 3. Cuando el build termine

### 3.1 Arreglar el receiver ANTES de subirlo — el template NO es el receiver limpio

Esto contradice lo que asumía el handoff del 12-ago y es el hallazgo de la sesión.

`Assets/WebGLTemplates/CastReceiver/index.html` es limpio de **harness** (`RUNG_CONFIG` = 0,
ninguna función de los 23 escalones), pero trae la **UI de diagnóstico encendida**:

- `#dbg-panel` con `display:block`, y `dbg()` fuerza `el.style.opacity = '1'` en **cada** línea
  mientras reescribe `innerHTML` con 40 `<div>`. Ese repintado es el mecanismo que se midió
  costando **~35 MB de Native Heap** (26,6 MB oculto vs 61-67 visible).
- `#fps-meter` y el sello `#rcv-tag`, también fijos encima del acuario.

`Tools/rcv-visual-2026-08-11.html` (= **exactamente** lo que está vivo en R2: 117.470 bytes,
sello `rcv 2026-08-11`) apaga la UI con `window.__cleanUI` → **`opacity = '0'`, no `display:none`**:
ahorra la rasterización pero **sigue construyendo los 40 divs** en cada log.

**Qué hacer sobre el `webgl-output/index.html` recién generado** (editarlo ahí directamente;
⚠ **NUNCA** copiar el template encima — deja los `{{{ }}}` sin procesar → "Error de red"):

1. `#dbg-panel` → `display:none` **y** salir temprano del `innerHTML` en `dbg()` cuando esté oculto.
   Ojo: bajar la opacidad **no** basta, hay que dejar de construir los divs.
2. `#fps-meter` → **dejarlo encendido para la tanda de validación**. Uno de los pendientes es medir
   si las sombras skinneadas (12 draw calls extra) penalizan el framerate, y sin el medidor no hay
   respuesta. Apagarlo **después** de validar → ese es el receiver definitivo.
3. `#rcv-tag` → dejarlo (1 div estático, coste cero) y **bumpear el sello** a `rcv 2026-08-13`:
   con `disableIdleTimeout` el device puede servir el index cacheado, y el sello es la única forma
   de confirmar cuál corre.
4. `console.log` se queda: `chrome://inspect` sigue siendo la vía de diagnóstico.

### 3.2 Deploy — solo player (los bundles NO se tocan)

Los cambios del lote visual son **shaders y C#**: van en el player. Los GLB ya se rebuildearon y
desplegaron el 10-ago (commit `669c5c5`). **No hay que reconstruir bundles.**

```powershell
$env:AWS_REQUEST_CHECKSUM_CALCULATION = "when_required"
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = "when_supported"

aws s3 sync webgl-output/ s3://appquarium-tv/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --delete --exclude "bundles/*" --cache-control "public, max-age=3600"
```

⚠ `--exclude "bundles/*"` es obligatorio con `--delete` o se borran los 92 bundles.
⚠ Los ficheros pequeños (`catalog.hash`, `settings.json`, y el propio `index.html`) fallan con
`aws s3 cp` → subirlos con **boto3**. Ver `CLAUDE.md` y `Tools/restore-production-receiver.sh`,
que ya trae las guardas ("¿tiene `{{{`?", "¿apunta a `Build/webgl-output.loader.js`?").

### 3.3 Validar en la tele

```bash
FISH=12 bash Tools/cast-run.sh 2 revision-visual
```

La IP la descubre el script solo (⚠ el DHCP la cambia; el **ping no vale** como señal).
Comprobar que la medición es buena: `grep -c "DURACIÓN DE SESIÓN" sender.log` debe dar **1**.

Qué mirar, en este orden:
1. **¿Se ven las sombras de las decos?** Si "se intuyen", no están.
2. **¿Se ven las sombras de los peces?** (silueta, no blob). Es lo que estrena este build.
3. **FPS** con 12 peces → coste de los 12 draw calls skinneados. Si penaliza, el blob elíptico
   sigue en el código como reserva y se cambia con una línea.
4. Ancla con volumen (ya no silueta negra), comida a un tercio, sin overlay amarillo.

---

## 4. Aclaración: el vídeo keepalive SÍ estaba activo en las 4 tandas de 660 s

Me hizo dudar ver `⚙ VÍDEO KEEPALIVE DESACTIVADO` en el receiver de agosto. **Falsa alarma**:
en `Tools/cast-headless.js`, `noKa: rn === '23'` — solo el escalón 23 lo apaga. Las tandas de
validación fueron el **escalón 2** (`FISH=12 bash Tools/cast-run.sh 2`), con el vídeo **activo**.

Conclusión: el template coincide con la configuración validada — mismos `disableIdleTimeout=true`
y `maxInactivity=3600`, mismo keepalive de 30s receiver→sender eliminado, mismo vídeo. Lo único
que sobra en el de agosto es el harness (`Wrapped`, `bindNew`, `patchAudioContext`, `skipUnity`,
`startUnityNow`, `slog`, `wrap`…), todo diagnóstico. `patchAudioContext` en particular **no** es un
fix de producción: es el escalón que suspendía el AudioContext de Unity.

---

## 5. Estado del repo

`feat/netflix-architecture` — **7 commits locales, sin push, sin mergear a main**. El lote visual
sigue verificado **solo en el Editor**: por eso no se mergea hasta verlo en la tele.

---

## 6. Pendientes

### Bloqueantes de esta línea de trabajo
- [ ] Terminar el build (§1) → arreglar receiver (§3.1) → deploy (§3.2) → validar (§3.3).
- [ ] **Medir el coste de las sombras skinneadas.** Sin medir en el device.
- [ ] Apagar el FPS meter una vez validado → receiver definitivo.

### Abiertas (heredadas)
- [ ] **¿La Y que manda el móvil encaja con el suelo del TV?** `PlaceAt(fromSave:true)` respeta la Y
      sin snap. Si no encaja, las decos flotarían en producción. No verificado con datos reales.
- [ ] Medir **sin reinicio previo** de la caja (la de uso diario tiene menos memoria libre).
- [ ] Palanca de memoria sin usar: **texturas de decos a DXT** (~5,3 → ~0,7 MB cada una).
      Las mallas de 100k triángulos son decisión del user — y son la única fuente de relieve,
      porque `DecoLit` no lee normal maps.
- [ ] **Sombras sobre otras decos**: imposible con esta arquitectura (pide shadow mapping → pase
      URP que no corre en Cast). Salidas reales: Cast Connect o falsear contacto.
