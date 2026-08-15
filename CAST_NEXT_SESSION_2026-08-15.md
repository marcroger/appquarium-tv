# Sesión 2026-08-15 — COMPLETADA (histórico)

> ✅ **Todo lo que pedía este documento está hecho**: desplegado en R2 y validado en la tele
> el 2026-08-15. El punto de entrada actual es **`CAST_NEXT_SESSION_2026-08-16.md`**.
>
> ⚠⚠ **El comando de deploy de §1 era PELIGROSO** — ver el aviso ahí mismo. No usarlo.

> Escrito al cierre del 2026-08-12. La sesión anterior está en `CAST_NEXT_SESSION_2026-08-12.md`.
> **El build está HECHO y el receiver PREPARADO. Falta subir a R2 y ver el resultado en la tele.**
> Nada de esto se ha desplegado: R2 sigue exactamente como estaba.

---

## 1. Lo primero: subir a R2

Todo está listo en local. Es un solo paso y no requiere reconstruir nada.

⚠⚠ **EL COMANDO QUE HABÍA AQUÍ ERA DESTRUCTIVO — NO USARLO.** Se conserva tachado como registro:

~~`aws s3 sync webgl-output/ s3://appquarium-tv/ --delete --exclude "bundles/*"`~~

**Por qué el `--exclude` no bastaba** (y por qué la regla se propagó a 4 documentos durante
meses sin fallar): el incidente original de mayo fue que `--delete` borraba `bundles/`, y se
generalizó mal — «añadir `--exclude "bundles/*"`» en vez de «no usar `--delete` en la raíz».
Funcionó mientras la raíz sólo tenía el player. Hoy la raíz **también** tiene ficheros que no
están en `webgl-output/` y que `--delete` se lleva por delante:

| Clave en R2 | Qué es |
|---|---|
| `keepalive_black.mp4` | el vídeo keepalive: el receiver lo referencia 2×. Sin él se caen las sesiones largas |
| `silence.wav` | idem, audio |
| `Build/webgl-min.*` · `Build/webgl-output-empty.*` | los rigs de diagnóstico del disconnect |

El comando correcto (acotado a `Build/`, sin `--delete`) está en `CLAUDE.md` → «Comandos clave».
⚠ `index.html` y `settings.json` van aparte **con boto3** (ver `CLAUDE.md`): el `aws s3 cp` de la
CLI 2.23+ falla en ficheros pequeños con `SignatureDoesNotMatch`.

❌ **NO usar `Tools/restore-production-receiver.sh`**. Su guarda hace
`grep -q "Build/webgl-output.loader.js"`, pero el receiver construye esa URL en dos trozos
(`var buildUrl = 'Build'` + `'/webgl-output.loader.js'`), así que la guarda **da falso negativo y
aborta**. O se arregla la guarda, o se sube a mano.

### Qué cambia en producción

| Clave en R2 | Antes | Después |
|---|---|---|
| `Build/webgl-output.*` | player del 27-jul | **player del 12-ago** (lote visual) |
| `index.html` | receiver de **diagnóstico**, 117.470 B | **receiver limpio**, 54.368 B |

Los **bundles no se tocan**: el lote visual son shaders y C#, que viajan en el player. Los GLB ya se
rebuildearon y desplegaron el 10-ago (`669c5c5`).

### Cómo revertir

- Player de julio → `…/scratchpad/player-backup-2026-08-12/Build/` (⚠ scratchpad de sesión: si ya no
  existe, el player de junio está en `scratchpad/prod-backup-2026-07-27/`).
- Receiver que está vivo ahora mismo en R2 → es **exactamente** `Tools/rcv-visual-2026-08-11.html`
  (117.470 B, verificado por tamaño y sello). Está en el repo.

---

## 2. Después: validar en la tele

```bash
FISH=12 bash Tools/cast-run.sh 2 revision-visual
```

La IP la descubre el script solo (⚠ el DHCP la cambia; el **ping no vale** como señal: el 10-ago
la `.33` respondía con otro cacharro detrás y costó una tanda entera).
Comprobar que la medición es buena: `grep -c "DURACIÓN DE SESIÓN" sender.log` debe dar **1**.

Confirmar que corre lo nuevo: el sello de la esquina inferior derecha debe decir
**`rcv 2026-08-12 visual`**. Si dice `rcv 2026-07-17 KA9-probe` o `rcv 2026-08-11`, el device está
sirviendo el index cacheado (`disableIdleTimeout` lo permite) y lo que se ve NO es este deploy.

Qué mirar, en este orden:

1. **¿Se ven las sombras de las decos?** ⚠ **Si "se intuyen", no están** — es el error que se cometió
   dos veces. Ante la duda: pintarlas de un color imposible y contar píxeles.
2. **¿Se ven las sombras de los peces?** Silueta real, no blob. Es lo que estrena este build.
3. **FPS** con 12 peces → el coste de los 12 draw calls skinneados, que **nunca se ha medido en el
   device**. Por eso el FPS meter se deja encendido en este deploy. Si penaliza, el blob elíptico
   sigue en el código como reserva y se cambia con una línea.
4. Ancla con volumen (ya no silueta negra), comida a un tercio, sin overlay amarillo.

Cuando esté validado: apagar también el `#fps-meter` → ese es el receiver definitivo.

---

## 3. Lo que se hizo el 2026-08-12

### 3.1 Build de player — HECHO

`[ProdBuild] Succeeded · 64581545 bytes · 00:55:13 · errores=0`, en **batchmode**:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe" \
  -batchmode -quit -nographics -projectPath . -buildTarget WebGL \
  -executeMethod TvProdBuild.BuildProd -logFile build-prod.log
```

**Batchmode es la ruta preferida** al build por GUI: `TvProdBuild.BuildProd` llama a
`TvWasmOptimize.SetDiskSizeLTO()` por código, así que fuerza el nivel correcto solo — no depende de
que nadie se acuerde de mirarlo. Requiere el Editor **cerrado** (choca con el lock del proyecto).

| | julio | **ahora** |
|---|---|---|
| `.wasm` | 25.430.429 B | **25.435.320 B** (+4,9 KB) |
| `.data` | 16.874.702 B | **16.876.157 B** (+1,5 KB) |

`[WasmOpt] DiskSizeLTO → DiskSizeLTO` ✅ — ni rastro de la vuelta a los 44 MB.

**El lote visual entró — comprobado contra el backup de julio, no supuesto.** El crecimiento de
+4,9 KB era lo bastante pequeño como para desconfiar, así que se contaron los símbolos dentro del
`.data`:

| símbolo | julio | ahora |
|---|---|---|
| `Appquarium/FishShadow` | **0** | 2 |
| `TvFishShadows` | **0** | 4 |

Preflight verificado antes de lanzar: Code Optimization = `DiskSizeLTO`, consola sin errores,
`TvScene` no dirty, y los **4 shaders en Always Included** —`FishShadow`
(`b8ea1c3a213d80948b8e737b56f46d30`) era el riesgo real: si falta, las sombras de los peces salen
en el Editor y **no** en la TV.

### 3.2 El template NO era el receiver limpio

El handoff anterior daba por hecho «template = limpio». **Falso, y por poco se despliega así.**
`Assets/WebGLTemplates/CastReceiver/index.html` es limpio de *harness* (`RUNG_CONFIG` = 0) pero traía
la **UI de diagnóstico encendida**: `#dbg-panel` con `display:block` y `dbg()` reescribiendo
`innerHTML` con 40 `<div>` en **cada** línea de log — el mecanismo que se midió costando ~35 MB de
Native Heap (26,6 MB oculto vs 61-67 visible).

`Tools/rcv-visual-2026-08-11.html` (= lo vivo en R2) lo apaga con `window.__cleanUI` →
**`opacity = '0'`, no `display:none`**: ahorra la rasterización pero **sigue construyendo los divs**.

Arreglado en **los dos sitios**, porque `webgl-output/` está en `.gitignore` y se perdería:

- `webgl-output/index.html` — el que se sube mañana. Copia en git: `Tools/rcv-limpio-2026-08-12.html`.
- `Assets/WebGLTemplates/CastReceiver/index.html` — el template, para que los próximos builds ya
  salgan bien. Procesado da el mismo fichero.

Qué se hizo: `#dbg-panel` → `display:none` **y** guarda `if (el && el.style.display !== 'none')` en
`dbg()` para no tocar el DOM; sello → `rcv 2026-08-12 visual`; `#fps-meter` **se queda encendido**
para poder medir las sombras skinneadas.

**No se pierde diagnóstico:** el log sigue yendo a `console.log` (`chrome://inspect`) y al PC por
`_logSink` a través del canal Cast. Lo único que desaparece es el repintado del DOM.

### 3.3 Falsa alarma aclarada — el vídeo keepalive SÍ estaba activo en las 4 tandas

Ver `⚙ VÍDEO KEEPALIVE DESACTIVADO` en el receiver de agosto hizo pensar que las tandas de 660 s se
habían medido sin él y que el template no coincidía con lo validado. **No es así**: en
`Tools/cast-headless.js`, `noKa: rn === '23'` — solo el escalón 23 lo apaga, y las tandas fueron el
**escalón 2**. El template coincide con la configuración validada: mismos `disableIdleTimeout=true`
y `maxInactivity=3600`, mismo keepalive de 30 s receiver→sender eliminado, mismo vídeo.

Lo único que sobra en el receiver de agosto es harness (`Wrapped`, `bindNew`, `skipUnity`,
`startUnityNow`, `slog`, `wrap`, `patchAudioContext`…). ⚠ `patchAudioContext` **no** es un fix de
producción, por si tienta portarlo: es el escalón que suspendía el AudioContext de Unity.

---

## 4. Estado del repo

`feat/netflix-architecture` — **commits locales, sin push, sin mergear a main**. El lote visual sigue
verificado **solo en el Editor**; no se mergea hasta verlo en la tele.

⚠ El build dejó sin trackear `Assets/AddressableAssetsData/link.xml` (+`.meta`), generado por
Addressables/IL2CPP. No se ha commiteado: decidir si va a git o a `.gitignore`.

---

## 5. Pendientes

### Esta línea de trabajo
- [ ] Subir a R2 (§1) → validar en la tele (§2).
- [ ] **Medir el coste de las sombras skinneadas** (12 draw calls extra). Sin medir en el device.
- [ ] Apagar el `#fps-meter` una vez validado → receiver definitivo.
- [ ] Arreglar la guarda de `Tools/restore-production-receiver.sh` (falso negativo, §1).

### Abiertas (heredadas)
- [ ] **¿La Y que manda el móvil encaja con el suelo del TV?** `PlaceAt(fromSave:true)` respeta la Y
      sin snap. Si no encaja, las decos flotarían en producción. No verificado con datos reales.
- [ ] Medir **sin reinicio previo** de la caja (la de uso diario tiene menos memoria libre).
- [ ] Palanca de memoria sin usar: **texturas de decos a DXT** (~5,3 → ~0,7 MB cada una).
      Las mallas de 100k triángulos son decisión del user — y son la única fuente de relieve,
      porque `DecoLit` no lee normal maps.
- [ ] **Sombras sobre otras decos**: imposible con esta arquitectura (pide shadow mapping → pase URP
      que no corre en Cast). Salidas reales: Cast Connect o falsear contacto.
