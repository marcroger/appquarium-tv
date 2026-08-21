# Paridad visual TV ↔ móvil — colores lavados y sombras sobre el fondo

> Abierto el **2026-08-21** a partir de dos observaciones del user en la tele, con el acuario
> real casteado desde el móvil. **Nada de esto está arreglado ni medido en la tele todavía**:
> este doc recoge lo que se ha comprobado leyendo el proyecto y deja el protocolo listo.
>
> ⚠ Todo lo del repo móvil que hay aquí se leyó **en sólo lectura**. No se ha tocado nada.

---

## 0. 🚨 CAUSA RAÍZ (2026-08-21): la TV no tiene render pipeline

**El proyecto TV no está usando URP. Renderiza con el pipeline built-in.** Y como el
post-proceso de este proyecto está montado sobre el *Volume framework* de URP, en la tele **no se
aplica NINGÚN grado de color**: ni bloom, ni tonemapping, ni saturación, ni contraste, ni viñeta.

No es que la TV tenga un grado distinto al del móvil (que es lo que decía la primera versión de
este doc, §2.1): es que **no tiene grado**. Eso es exactamente lo que el user describe como
«se ve más falso y sin tanto colorcete».

### Las cuatro pruebas

| | Qué se midió | Resultado |
|---|---|---|
| 1 | Sonda en el Editor (`TvRenderProbe`) | `currentRenderPipeline` = **NULL** · asset de pipeline = **NULL** · **0** callbacks de `beginCameraRendering` en 25 s · `SupportsRenderRequest` = **False** |
| 2 | `ProjectSettings/GraphicsSettings.asset` | `m_CustomRenderPipeline` apunta al guid `4b83569d67af61e458304325a23e5dfd`, que **no existe en ningún `.meta`** del proyecto: referencia colgada |
| 3 | Ficheros del proyecto | **No hay ningún `UniversalRenderPipelineAsset`**. Sólo están `UniversalRenderPipelineGlobalSettings.asset` y `DefaultVolumeProfile.asset`, que los crea el paquete solo |
| 4 | 🎯 **El player DESPLEGADO** (build del 20-ago, corriendo en Chrome) | `[TvScene] renderScale SKIP — rp=null` |

La cuarta es la que cierra el caso: no es una rareza del Editor, **es lo que hay en producción**.

### Por qué no se detectó en dos años

```
[PostFX] ✅ Bloom + Color + Vignette activos (3 efectos). [P]=toggle [O]=estado
```

Ese log sale en cada arranque, también en el player desplegado — y **no significa que el
post-proceso funcione**: se imprime al final de `BuildVolume()` pase lo que pase. Sólo dice «he
creado un Volume». Lo mismo con la línea `PostFX: bloom=OFF tm=Neutral sat=18 con=10` del panel
de diagnóstico, que se escribe en el mismo sitio. Dos «confirmaciones» de algo que nunca corrió.

🧭 Es otra vez el patrón de la casa: **no petaba, simplemente no pasaba lo que creíamos**.

### Lo que esto explica hacia atrás

- ⚠ **`renderScale = 0.7` NUNCA se ha aplicado.** El propio log lo dice (`SKIP`). De las «tres
  palancas que hicieron que fuera fluido en la Xiaomi» (bloom OFF + renderScale 0,7 +
  targetFrameRate 30), **la única que existió de verdad es el targetFrameRate**; y «bloom OFF»
  no era una palanca, porque el bloom no se aplicaba de todas formas. **Esto hay que remedirlo.**
- ⚠ **SMAA tampoco hace nada** (`[TvScene] SMAA Low enabled` es otra línea optimista): es una
  característica de URP.
- 🧩 **Los shaders CG legacy y lo «morado».** Los comentarios de `DecoLit`, `FishUnlit`,
  `PlanarShadow` y `FishShadow` dicen que los pases `LightMode="UniversalForward"` **no se
  ejecutan** «en el renderer del Cast device» y que sólo funcionan los CG sin `LightMode`. Eso no
  es una limitación del device: **es exactamente lo que hace el pipeline built-in**, en cualquier
  máquina. El síntoma llevaba años bien observado y mal atribuido.
- ✅ Lo que **no** cambia: el acuario se ve, y los shaders CG están escritos para built-in, así
  que **hoy son correctos**. Esto no es un incendio: es una palanca enorme sin estrenar.

### ⚠ Antes de «arreglarlo»

Asignar un `UniversalRenderPipelineAsset` **no es un cambio de una línea**: encendería de golpe
el pipeline para el que los shaders propios NO están escritos, y cambiaría el coste de GPU en un
device que ya va a 37 fps. Hay al menos dos caminos y la decisión es del user:

| | Qué | Riesgo |
|---|---|---|
| **A** | Crear y asignar un URP asset (como el móvil, que tiene `Mobile_RPAsset`/`PC_RPAsset`) | Alto: los 4 shaders CG dejan de ser la vía buena, hay que revisarlos uno a uno; y el bloom, que es lo que da el «colorcete», es el efecto más caro en el Mali-G31 |
| **B** | Quedarse en built-in y hacer el grado con Post Processing Stack v2, que **ya está instalado** (`UNITY_POST_PROCESSING_STACK_V2` está en los defines del proyecto) | Menor: no toca shaders. Pero es una pila distinta a la del móvil, así que la paridad sería «parecido», no «lo mismo» |

En los dos casos hace falta rebuild de player y validación en la tele.

---

## 1. Lo que se ve (reportado por el user)

1. **Los colores no se ven tan nítidos ni tan bonitos como en la app.** El fondo en concreto se
   ve *«prácticamente blanco y negro»*.
2. **Las sombras de las decos caen sobre el FONDO** (el telón de atrás del todo), no sobre el
   suelo. De los peces no se fijó. Duda abierta del user: *«no sé si deberían mostrarse ahí, o
   igual sí, al ser una pecera»*.

---

## 2. Lo que YA está comprobado (leído del proyecto, no medido en pantalla)

### 2.1 🎯 El grado de color de TV y el del móvil son DISTINTOS a propósito

No es «lo mismo con menos calidad»: son dos ajustes diferentes. Valores **serializados en cada
escena** (no los defaults del script):

| | Móvil (`AquariumScene.unity`) | TV (`TvScene.unity`) |
|---|---|---|
| **Bloom** | **1,2** (y el script móvil **no tiene interruptor**: siempre activo) | **OFF** (`enableBloom: 0`) |
| **Tonemapping** | **no existe en el script móvil** | **Neutral, ON** |
| Saturation | **−15** | **+18** |
| Contrast | (no expuesto) | +10 |
| Post exposure | 0,1 | 0,05 |
| Vignette | 0,095 | 0,18 (default del script; sin override en escena) |
| Color filter | `(0.95, 0.98, 1.00)` | `(0.95, 0.98, 1.00)` — **idéntico** |

⚠⚠ **CORREGIDO EL 21-ago — leer §0 primero.** La columna «TV» de esta tabla son los valores
**configurados**, no los aplicados: sin URP, el Volume no afecta al render y en la tele **no se
aplica ninguno**. La comparación que sigue explica qué *pretendía* hacer cada proyecto, pero la
diferencia real con el móvil no es de valores: es que allí el grado se aplica y aquí no.

🧭 **Lo importante y lo contraintuitivo:** el móvil está **desaturado (−15)**, no saturado. Lo
que le da el aspecto vivo es el **bloom a 1,2**, que hace florecer las zonas brillantes. La TV
lo tiene **apagado** — a propósito, fue una de las tres palancas que la hicieron ir fluida en la
Xiaomi (bloom OFF + renderScale 0,7 + targetFrameRate 30) — y compensa subiendo saturación a
+18. **Subir saturación no devuelve el glow**: da color plano. Y encima la TV mete un
tonemapping Neutral que el móvil no tiene, que comprime los altos.

⚠ **Hipótesis, no medición:** que esto explique el «blanco y negro» del fondo es lo más probable,
pero no está comprobado en pantalla. Ver §4 antes de tocar un solo valor.

### 2.2 🎯 Los fondos van a la TV a 1/16 de píxeles

El PNG de origen es **el mismo fichero** en los dos repos (`bg_abyss.png`, 1.935.615 bytes en
ambos). Lo que cambia es el import:

| | maxTextureSize | resolución real en el device | override |
|---|---|---|---|
| Móvil (Android) | **2048** | **1536×1024** (el PNG entero) | `overridden: 0` |
| TV (WebGL) | **512** | **512×341** | `overridden: 1` |

⚠ **Corregido el 21-ago:** los PNG de origen miden **1536×1024**, no 2048². El `maxTextureSize`
recorta el lado mayor, así que el móvil no escala nada — se queda el original — y la TV baja a
512×341. La diferencia es de **9× en píxeles** (1,57 Mpx contra 0,175), no de 16× como decía la
primera versión de este doc. Sigue siendo el factor grande de nitidez, pero conviene tenerlo bien.

Encima:

- `textureCompression: 1` + `compressionQuality: 50` → DXT en WebGL. Sobre un degradado suave,
  DXT1 hace **banding** y desplaza tonos: es justo el peor caso para un fondo de agua.
- `TvSceneBootstrap.cs:104` fuerza **`renderScale = 0.7`**: el frame entero se renderiza al 70 %
  y se reescala. En una tele 1080p el fondo acaba siendo 512 px estirados a 1920.
  ⚠ **Corregido el 21-ago:** el móvil **también** renderiza a escala reducida
  (`Mobile_RPAsset.asset: m_RenderScale: 0.8`), así que la diferencia real es 0,7 vs 0,8 — un
  12 %, no «la TV a 70 % contra el móvil a 100 %». Esta palanca pesa MUCHO menos de lo que
  parecía; el grueso de la nitidez perdida está en el **512 vs 2048** del fondo. (El
  `targetFrameRate` sí coincide: 30 en los dos, 24 en modo ambiente.)

El override de 512 no es un accidente: viene de `★ Reduce TV Textures`, que se usó para bajar el
tiempo de compresión del build de WebGL (documentado en `CLAUDE.md`: «512px → 4× menos tiempo»).
Se pagó nitidez por horas de build.

### 2.3 ✅ Descartado: el color space

`m_ActiveColorSpace: 1` (**Linear**) en **los dos** proyectos. No hay desajuste de gamma por ahí.
Los fondos se importan con `sRGBTexture: 1`, que es lo correcto.

### 2.4 ✅ Descartado (con matiz): el overlay de noche

Hubo un bug que dejaba quads de noche apilados a alpha 0,75 «para siempre» y oscurecía el acuario
en escalera hasta negro. **Se arregló el 2026-08-15** (`TankBackground.InitializeBackground`
ahora destruye también `TankNightOverlay`). ⚠ Matiz: eso arregla la **acumulación**, pero un
overlay de noche legítimo sigue existiendo. Si en la captura de comparación el modo ambiente no
es el mismo en móvil y en TV, la comparación no vale nada.

### 2.5 Por qué las sombras acaban sobre el fondo

La sombra de deco es la malla real aplanada contra un plano horizontal de mundo
(`Appquarium/PlanarShadow`, `_FloorY`), y ese plano sale de:

```
floorY = max( FloorSurfaceY(deco.z), FloorSurfaceY(0) ) + 0.02
FloorSurfaceY(z) = _floorMeshBaseY + t·_floorMeshRiseY     // t = 0 delante, 1 al fondo
```

O sea: **cuanto más «al fondo» está la deco, más ARRIBA en pantalla cae su sombra**. Eso es la
convención 2.5D de esta escena — el suelo es un **sprite vertical** y «más lejos» se dibuja «más
alto» (la misma trampa que tuvo las sombras invisibles hasta el 11-ago). La zona de suelo ocupa
sólo el **20 % inferior** del tanque (`ComputeZFromY`), así que una deco colocada atrás proyecta
su sombra por encima de esa banda, y detrás de todo eso lo único que hay es el fondo
(`sortingOrder −100`).

**No es un bug de código: es la consecuencia directa de la geometría.** Que *deba* verse así es
una decisión de arte. En una pecera real, con luz frontal, la sombra sí cae sobre la pared del
fondo — pero si se lee como «pegatina flotando», habrá que decidir entre:

- recortar la sombra a la banda de suelo (deja de haber sombra en decos del fondo),
- atenuarla con la profundidad (más al fondo → más transparente), o
- dejarla como está.

⚠ Ojo con el efecto secundario: `TvFishShadows` usa `GetFloorSurfaceY(z)` **por el mismo camino**,
así que lo que se toque para decos afecta a los peces salvo que se separe explícitamente.

---

## 3. La lista de palancas, con su precio

⚠⚠ **Y todas presuponen que el post-proceso se aplique, que hoy NO es el caso** (§0): antes de
tocar un solo valor hay que decidir el camino A o B.

⚠⚠ **Todas cuestan un rebuild de player (~55 min)**: los valores de post-proceso están
serializados en `TvScene`, el `renderScale` está en código, y los fondos viven en
`Assets/Resources/` → todo va horneado en el `.data`/`.wasm`. No hay ninguna que se despliegue
sólo subiendo un fichero a R2.

| Palanca | Qué devuelve | Qué cuesta |
|---|---|---|
| **Bloom ON** (móvil: 1,2) | Es lo que hace que el móvil se vea «vivo» | FPS. Se apagó por eso; la Xiaomi va a 37 fps medios con 25 peces |
| **Tonemapping Neutral OFF** | Altos sin comprimir, como el móvil | Riesgo de highlights quemados (por eso se puso Neutral, no ACES) |
| **Saturación +18 → −15** | Igualar el grado del móvil | Sólo tiene sentido **junto** al bloom; sola, apaga el resultado |
| **Fondos 512 → 1024** | 4× téxeles (de 9× que faltan) | **+3,8 MB** en el `.data` (hoy 15,94 MB) |
| **Fondos a 1536 (paridad)** | Lo mismo que ve el móvil | **+10,2 MB** en el `.data`: lo dejaría en ~26 MB, un **+64 %** |
| 🎯 **Fondos a Addressables** | Paridad **sin** engordar el `.data`: sólo se descarga el que se usa (~1 MB) y los otros 10 dejan de viajar | Convertir `Resources.Load` síncrono en asíncrono + volver a meterlos en un grupo. Riesgo: el primer frame sin fondo si no se cubre |
| ~~**renderScale 0,7 → 0,8**~~ | **No aplica: el renderScale NUNCA se ha aplicado** (ver §0). Es una propiedad del URP asset, y no hay URP asset | — |

---

### 3.1 🎯 Por qué P2 debería ir por Addressables y no por subir el import

Medido el 21-ago (DXT1 + mipmaps, 11 fondos):

| | resolución | peso de los 11 en el `.data` |
|---|---|---|
| Hoy (max 512) | 512×341 | ~1,3 MB |
| max 1024 | 1024×683 | ~5,1 MB (**+3,8**) |
| max 1536 = paridad con el móvil | 1536×1024 | ~11,5 MB (**+10,2**) |

Subir el import es la vía fácil, pero **paga los 11 fondos para usar 1**: todos viajan horneados
en el `.data`, que hoy son 15,94 MB y que el device descarga entero antes de arrancar.

Sacarlos a un bundle remoto invierte la cuenta: el `.data` **adelgaza** ~1,3 MB y el fondo activo
se baja aparte (~1 MB a resolución completa). Encima ya está la infraestructura hecha — bucket
privado, Worker y `TvBundleAuth` — así que el fondo iría autenticado como los demás bundles.

⚠ Esto es exactamente el pendiente que ya estaba en la lista («sacar los fondos del `.data`») y
que se dejó porque **pide convertir carga síncrona en asíncrona**. Ahora tiene una razón de peso
para hacerse: no es sólo ahorrar 0,7 MB, es la única forma de tener el fondo nítido sin inflar el
player.
⚠⚠ Y ojo con el historial: los 11 fondos **se sacaron** de Addressables el 18-ago porque nadie
los descargaba (`TankBackground` los pide por `Resources.Load`). Volver a meterlos **sin**
convertir el loader repetiría exactamente aquel bundle muerto. El orden correcto es: primero el
loader asíncrono, después el grupo.

---

## 4. ⚠ Protocolo de comparación — hacerlo ANTES de tocar valores

Idea del user, y es la correcta: **capturar la app y castear exactamente ese estado**, comparando
en vez de opinando. Este proyecto ya tiene todas las piezas menos la del móvil:

1. **Mismo estado en los dos lados.** Castear desde el móvil el acuario real (no
   `cast-headless.js --fish N`, que manda un estado sintético y **no** reproduce el fondo ni el
   suelo que el user está viendo). El fondo y el suelo tienen que ser los mismos presets.
2. **Mismo modo ambiente** (día/noche/atardecer) en ambos — ver §2.4, o la comparación no vale.
3. **Captura de la TV:** `adb exec-out screencap` contra la Xiaomi captura el canvas WebGL
   (validado; DevTools no existe en ese build). Confirmar en el panel de diagnóstico la línea
   `BG: <id> IMAGE shader=… tex=WxH` — **ahí se lee el tamaño real de la textura en el device**,
   que debería salir 512 y es la prueba directa de §2.2.
4. **Captura del móvil:** la hace el user (screenshot del teléfono). No hace falta tocar el repo
   móvil para esto.
5. **Comparar con números, no a ojo:** media y desviación de saturación por regiones (fondo,
   suelo, deco, pez) y luminancia **absoluta** de cada región.

🧭 **Regla de oro que ya costó un diagnóstico falso** (la bioluminiscencia del 16-ago): medir el
valor **absoluto** de la región, nunca su contraste contra un fondo que también cambia. Si se
compara «coral contra agua» y el agua se oscurece, el ratio sube solo y parece que funciona.

⚠ **Trampa de escala:** las dos capturas tendrán resoluciones distintas (teléfono vs 1080p).
Comparar histogramas normalizados por región, no píxel a píxel.

💡 **Recomendación fuerte para no gastar 55 min por variante:** antes de la tanda, añadir al
mensaje `DIAG` del receiver la posibilidad de cambiar **en caliente** bloom / tonemapping /
saturación / contraste (y, si se puede, `renderScale`). Con **un solo rebuild** se pueden probar
todas las combinaciones en la tele y capturar cada una. Sin eso, cada variante es un build.

---

## 5. Estado

- [ ] Nada tocado. Ninguna medición hecha en pantalla todavía.
- [ ] Decisión del user pendiente en dos frentes: **cuánto FPS está dispuesto a pagar** por
      acercarse al look del móvil, y **qué debe hacer la sombra** de una deco del fondo.
