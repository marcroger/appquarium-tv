# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-21**. La anterior está en `CAST_NEXT_SESSION_2026-08-21.md`.
>
> **La TV recuperó el color.** Llevaba desde siempre sin aplicar ningún grado, y no por un ajuste
> mal puesto: por **cinco causas encadenadas, todas silenciosas**. Todo desplegado y validado en
> la tele, y mergeado a `main`.

---

## 1. Lo que se arregló (y por qué nadie lo vio antes)

El user reportó dos cosas: *«se ve más falso y sin tanto colorcete que en el móvil»* y *«el fondo
se ve grisáceo»*. Resultaron ser **dos problemas distintos**.

### 1.1 Sin grado de color — tres causas en cadena

| # | Causa | Por qué era invisible |
|---|---|---|
| 1 | **No había render pipeline.** `GraphicsSettings` apuntaba a un URP asset inexistente → la TV renderizaba con **built-in**, donde el `Volume` de URP no hace nada | El log `[PostFX] ✅ Bloom + Color + Vignette activos` se imprime **pase lo que pase**: sólo dice «he creado un Volume» |
| 2 | **`renderPostProcessing` de la cámara en `false`** (default de URP) y nadie lo encendía | El mismo bloque de código tocaba esa componente para poner SMAA y se dejaba lo importante |
| 3 | **El Volume de la barra LED machacaba el grado entero**: `Add<ColorAdjustments>(true)` marca **todos** los parámetros como override, y va a prioridad 11 | Nada falla: simplemente gana el otro Volume |

🧭 Cualquiera de las tres **por separado** bastaba para dejar la tele sin color. Por eso arreglar
una y medir no habría dado señal — y de hecho no la dio hasta arreglar la tercera.

### 1.2 El fondo «grisáceo» — dos causas más, y no eran de color

| # | Causa | Arreglo |
|---|---|---|
| 4 | **Sólo se veía el 62 % de la imagen** (idea del user). El 38 % de abajo, el que tiene textura y profundidad, no aparecía nunca | `TankBackground` encaja la foto entre el borde real del suelo (`DecorationPlacer.FloorTopY`) y el borde superior |
| 5 | **Fondos a 512 px** con 2,6× de ampliación | Override de WebGL a **1024** |

**Descartado con medida, no con opinión:**
- El shader del fondo (`Sprites/Default` vs `URP/Unlit`): mismo fondo, mismo instante, **lum
  130,9 vs 127,6 y sat 0,955 vs 0,961**. Da igual.
- Que se cargara el fondo equivocado: la mediana de color encaja con el `bgId` pedido en los tres
  probados, con el segundo candidato lejísimos.
- Que el color se destiñera: tropical en pantalla **0/168/168** contra **2/162/164** del PNG.

---

## 2. Auditoría del protocolo: 11 de 11

El móvil manda exactamente estos y la TV los aplica **todos**, verificados uno a uno **en el
device**: `add_fish · remove_fish · add_deco · remove_deco · change_bg · change_sub ·
change_light · ambient · speed · feed · startle`.

Dos arreglos salieron de ahí:

1. **`speed`, `feed`, `startle` y `refresh` no reportaban nada** por el canal Cast. Sus efectos
   son movimiento, que no se ve en una captura: un mensaje perdido era **indetectable**. Ahora
   confirman qué hicieron **y sobre cuántos peces**.
2. ⚠⚠ **El `try/catch` NO protege en este build.** `add_fish` con un payload malformado soltaba
   `JS ERR: Uncaught undefined` **con el catch puesto**: el player va con `Exception Support:
   None` y una excepción del runtime no se captura. `SafeFromJson` era una guarda decorativa;
   ahora **valida la forma antes de parsear**.

⚠ Recordatorio para probar a mano: `add_fish` y `add_deco` llevan **JSON**, no el id suelto.
```
add_fish  → {"speciesId":"...","nickname":"...","ageScale":1.0}
add_deco  → {"instanceId":"...","itemId":"...","position":{...},"scaleFactor":1.0}
```

---

## 3. Coste, medido con el protocolo del 19-ago (25 peces + 6 decos, 420 s)

| | 19-ago (sin URP) | hoy (con URP) |
|---|---|---|
| **FPS medio** | 37 | **37** |
| WASM heap | 159 MB | 191 MB |
| `.data` | 15,94 MB | **19,50 MB** |
| `.wasm` | 21.664.370 | 21.668.206 |
| Sesión | 421 s, 0 errores | 420 s, **0 errores, 0 JS ERR** |

✅ **URP no cuesta FPS.**
⚠ La memoria sube un escalón — pero **el heap crece a saltos geométricos** (0,2 de paso) y
159 × 1,2 = 191: son dos escalones consecutivos. **No son +32 MB de datos**, es que el uso cruzó
el umbral. El coste real está entre 1 y 32 MB y con esta instrumentación no se afina más.

---

## 4. 🧭 Reglas nuevas, todas pagadas hoy

1. ⚠⚠ **`try/catch` no es una red de seguridad aquí** (`Exception Support: None`). **Validar
   antes de parsear.**
2. ⚠⚠ **Sin un sello que identifique el build, una medición A/B contra el device NO VALE.** El
   player va con `max-age=3600` y la caja sirve de caché: se compararon dos tandas de memoria
   creyendo que eran builds distintos y era **el mismo**. De ahí el `RP: … scale= hdr= …` que
   ahora imprime el receiver. Para iterar, desplegar con `max-age=60` y **restaurar 3600 al
   terminar**.
3. **Un valor de encuadre «a ojo» es un bug esperando su momento.** El 0,25 puesto a mano dejaba
   asomar una franja repetida de textura; el valor derivado del suelo real es **0,233**, y ese
   7 % era exactamente el defecto.
4. **Las capturas del device se disparan por EVENTO del log, no por reloj**: el reloj del
   receiver va ~20-25 s por detrás del sender y las primeras tandas salieron mal etiquetadas.
5. **Un barrido cuyas variantes salen idénticas no es un barrido roto: es un resultado.** Las 8
   PNG iguales del Editor eran la respuesta (no había post-proceso), no un fallo del arnés.
6. ⚠ **El barrido del Editor (`TvGradeSweep`) NO sirve para elegir valores de grado**: sus
   capturas alternan según el índice al margen de las variantes. Para el grado, `Tools/grade-tune.js`
   sobre el player real.

---

## 5. Herramientas nuevas

| | |
|---|---|
| `Assets/Editor/TvUrpSetup.cs` | Crea el pipeline, lo enciende/apaga para comparar, y **verifica `postProcessData`** |
| `Assets/Editor/TvRenderProbe.cs` | Sonda: qué pipeline hay, si se renderiza, y si `ScreenCapture` produce algo |
| `Tools/grade-tune.js` | Afina el grado sobre el **player real** en Chrome, mandando mensajes `GRADE` |
| `Tools/grade_contact_sheet.py` | Hoja de contactos + luminancia/saturación por bandas, con guarda de «esto no mide nada» |
| `cast-headless.js --raw` | Manda un `CastMessage` cualquiera a una hora dada. Permite conmutar cosas **en la misma sesión** de la tele en vez de gastar un build por variante |

**Mensaje `GRADE`** (nuevo): cambia en caliente `bloom`, `bloomIntensity`, `tonemapping`,
`saturation`, `contrast`, `exposure`, `vignette`, `bgShader`, `bgFit` y `shadowFade`. Los campos
ausentes no se tocan.
```
node Tools/cast-headless.js --ip <IP> --fish 12 --raw 'GRADE={"saturation":0}@60'
```

---

## 6. Lo que queda

- [ ] 🎨 **Decidir la sombra sobre el fondo** (P3, la segunda observación del user). Ya está
      implementado y comparado en la tele: `shadowFade` desvanece la parte de la sombra que sube
      por encima del borde del suelo. **0 = como hoy.** Falta elegir valor.
- [ ] 🎯 **Las MALLAS** — 11 decos a 100.000 triángulos. Ahora tiene doble motivo: peso **y**
      memoria, que hoy subió un escalón.
- [ ] 💡 **Sacar los fondos del `.data` a Addressables**: adelgazaría el player **y** permitiría
      resolución completa (1536) sin pagarla en el `.data`. Ver `CAST_PARIDAD_VISUAL.md` §3.1.
      ⚠ Primero el loader asíncrono, después el grupo — al revés se repite el bundle muerto del
      18-ago.
- [ ] **Fase 2 — JWT por usuario** (repo móvil). Contrato en `CAST_R2_AUTH_MOVIL.md`.
- [ ] **Halo de la bioluminiscencia** · **`ageScale`** (falta build móvil) · **editar una deco
      colocada no manda UPDATE** (pide tocar el móvil).
- [ ] ⚠ `main` está **15+ commits por delante de `origin` y sin pushear**.

---

## 7. Estado desplegado

| | |
|---|---|
| Sello receiver | **`rcv 2026-08-21 urp`** |
| Pipeline | `TvRenderPipeline` · scale 0,70 · hdr OFF · msaa 1 · sombras OFF |
| Player | `.wasm` 21.668.206 · `.data` 19.503.971 |
| Bundles | 80 = 87,3 MB en `appquarium-tv-assets` (privado), **sin tocar hoy** |
| Rama | `feat/urp-pipeline` **mergeada a `main`** |
| Backup | `D:\dev\_backups\appquarium-tv\backup-antes-urp-2026-08-21\` con md5 (player del 20-ago) |

⚠ **El `.data` de producción va con `max-age=3600`.** Si en una sesión se baja a 60 para iterar,
**hay que restaurarlo** — si no, cada espectador se rebaja 38 MB cada minuto.
