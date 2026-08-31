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

### 0.1 Medido el 21-ago al intentar encender URP

Se creó un URP asset (`Assets/Settings/TvRenderPipeline.asset`, valores copiados del móvil) y se
comparó el mismo acuario con y sin él. Tres cosas:

1. ✅ **URP NO rompe el aspecto.** Las capturas con built-in y con URP son prácticamente iguales:
   sin magenta, decos y peces correctos, sombras en su sitio. Los 4 shaders propios están
   escritos sin `LightMode` y eso los hace válidos en los dos pipelines, como decían sus
   comentarios. **Es el riesgo grande del camino A, y se ha medido: no aparece.**
2. ⚠⚠ **Encender URP NO basta: hay una segunda causa.** `renderPostProcessing` de la cámara viene
   en **false** por defecto en URP y **nadie lo enciende en el código** (`TvSceneBootstrap` toca
   esa misma componente para poner SMAA y no lo pone). Sin esa línea, el grado sigue sin
   aplicarse aunque haya pipeline.
3. ⚠⚠ **Y una tercera:** al crear el `UniversalRendererData` por código, `postProcessData` se
   queda a **null** y URP **se salta todo el post-proceso en silencio**. `Create()` sólo recarga
   los recursos del pipeline, no los del renderer. `TvUrpSetup` ya lo rellena y **verifica**.

### ⚠ El barrido del Editor NO sirve para elegir los valores

Se intentó, y hay que decirlo claro para que nadie se fíe de esas PNG: con URP activo, las
luminancias medidas **alternan según el índice de captura** (los índices pares salen ~133 y los
impares ~123) **independientemente de los valores de cada variante**, y el resultado es
reproducible entre tandas. Eso mide el instrumento, no el grado: `SubmitRenderRequest` con
post-proceso no está devolviendo de forma fiable el frame ya procesado.

🧭 Conclusión práctica: **el grado hay que elegirlo sobre el player de verdad**, no en el Editor.
Y eso se puede hacer sin la tele: `Tools/local-test.js` abre el player real en Chrome y captura
consola y pantalla. La tele queda para la validación final y para el coste de GPU, que es lo
único que el PC no puede decir.

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

## 0.2 ✅ MEDIDO EN LA TELE (2026-08-21) — qué era y qué no era

Con URP activo, el post-proceso encendido y el Volume de la barra LED arreglado, se desplegó el
player y se midió sobre el device (12 peces + 6 decos, capturas por `adb exec-out screencap`
disparadas **por evento del log**, no por reloj: el del receiver va ~20-25 s por detrás del
sender y las primeras capturas salieron mal etiquetadas por eso).

**Lo que NO era el problema del fondo:**

| Sospechoso | Medido | Veredicto |
|---|---|---|
| El shader del fondo (`Sprites/Default` vs `URP/Unlit`) | Mismo fondo, mismo instante: lum **130,9 vs 127,6**, sat **0,955 vs 0,961** | ❌ Da igual. El apaño histórico ya no hace daño, pero cambiarlo no arregla nada |
| ¿Se carga el fondo equivocado? | Mediana de color contra el catálogo: tropical→`bg_tropical` (dist 7,5 vs 51,5 del segundo), kelp→`bg_kelp`, volcanic→`bg_volcanic` | ❌ Los fondos son los correctos |
| ¿Se destiñe el color? | Tropical en pantalla **0/168/168** contra **2/162/164** del PNG | ❌ Es fiel al original |

**Lo que SÍ es (idea del user, y confirmada):**

1. 🎯 **Sólo se ve el 62 % de la imagen.** Ajustando qué franja del PNG encaja con el perfil
   vertical de la tele: **del 0 % al 62 %**, correlación **0,972**. El 38 % inferior —la parte
   con más textura y profundidad de la foto— **no aparece nunca**.
2. **Va estirada 1,19× en horizontal**: las imágenes son 1536×1024 (3:2) y la pantalla es 16:9.
3. Con el override a 512 px, esa franja visible son ~317 px de alto reales estirados a ~840 px
   de pantalla: **2,6× de ampliación**. Ahí está el aspecto lavado.

🧭 O sea: **el problema del fondo es de encuadre y de resolución, no de color.** Y son dos cosas
que se arreglan juntas — recuperar el 38 % perdido hace que además sobre menos ampliación.

⚠ El código de geometría del fondo es **idéntico** al del móvil (el diff sólo difiere en el
shader y en los logs), así que la diferencia viene del aspecto de pantalla, no de una divergencia
del código.

---

## 0.3 ✅ ARREGLADO Y DESPLEGADO (2026-08-21)

Cinco causas, todas silenciosas. Las tres primeras dejaban la TV **sin grado de color**; las dos
últimas son las del fondo «grisáceo», y son un problema distinto.

| # | Causa | Arreglo |
|---|---|---|
| 1 | **No había render pipeline**: `GraphicsSettings` apuntaba a un URP asset inexistente | `Assets/Settings/TvRenderPipeline.asset` (valores del móvil) + `TvUrpSetup` para encender/apagar y comparar |
| 2 | **`renderPostProcessing` de la cámara en `false`** (default de URP) y nadie lo encendía | Una línea en `TvSceneBootstrap`, con `JsBridge.Log` para que se vea por el canal Cast |
| 3 | 🎯 **El Volume de la barra LED machacaba el grado entero**: `Add<ColorAdjustments>(true)` marca TODOS los parámetros como override, y va a prioridad 11 | `Add<ColorAdjustments>(false)`: sólo manda en los dos que declara |
| 4 | **Sólo se veía el 62 % del fondo** | `TankBackground` encaja la imagen entre el suelo y el borde superior |
| 5 | **Fondos a 512 px** con 2,6× de ampliación | Override de WebGL a **1024** |

⚠⚠ **Y una trampa que salió del propio arreglo 4, cazada por el user en la tele:** por debajo de
v=0 la textura está en Clamp y **repite su última fila hacia abajo**, lo que dibuja un rayado
vertical (cada columna arrastra su píxel). Con la fracción puesta a ojo (0,25) esa zona asomaba
por encima del suelo. Ahora se calcula de la **geometría real del suelo**
(`DecorationPlacer.FloorTopY`), así que cae exactamente debajo — y sigue cayendo ahí aunque la
cámara se mueva, porque el cálculo es en coordenadas de mundo.

🧭 Regla que deja esto: **un valor de encuadre «a ojo» es un bug esperando su momento.** Si hay
una geometría real de la que derivarlo, se deriva.

### Coste medido

| | antes de hoy | ahora | Δ |
|---|---|---|---|
| `.data` | 15.942.355 | **19.503.971** | +3,56 MB (2,0 de shaders URP + 1,56 de fondos a 1024) |
| `.wasm` | 21.664.370 | 21.668.206 | +3,8 KB |
| FPS (12 peces, sesión asentada) | 45 (15-ago) | **32-41** | ⚠ pendiente de tanda A/B propia |

⚠ El FPS **no está medido en serio todavía**: las primeras lecturas (`avg 29`) estaban
contaminadas por el pico de carga de bundles, y con la sesión asentada sube a 32-41. La
comparación con el histórico no es limpia porque aquél se tomó con otro build y otro protocolo.

### 0.4 Auditoría del protocolo y coste real (2026-08-21, tras la sesión de tarde)

**Los 11 tipos que manda la app, verificados uno a uno en la tele.** El móvil manda exactamente
estos y la TV los aplica todos: `add_fish · remove_fish · add_deco · remove_deco · change_bg ·
change_sub · change_light · ambient · speed · feed · startle`.

⚠ Dos hallazgos de la auditoría:

1. **`speed`, `feed`, `startle` y `refresh` no reportaban nada** por el canal Cast. Sus efectos
   son movimiento, que no se ve en una captura, así que un mensaje perdido era **indetectable**.
   Ahora cada uno confirma qué hizo y **sobre cuántos peces**, que es lo que distingue «no llegó»
   de «llegó y no había a quién aplicárselo».
2. ⚠⚠ **El `try/catch` NO protege en este build.** `add_fish` y `add_deco` con un payload
   malformado soltaban `JS ERR: Uncaught undefined` **con el catch puesto**: el player va con
   `Exception Support: None` y una excepción del runtime no se captura, se escapa como error de
   JS. `SafeFromJson` era una guarda decorativa. Ahora **valida la forma antes de parsear** (que
   sí funciona sin excepciones) y avisa por JsBridge.
   🧭 Regla: **en este proyecto, `try/catch` no es una red de seguridad. Validar antes.**

### Coste real de URP, medido con el protocolo del 19-ago (25 peces + 6 decos, 420 s)

| | 19-ago (sin URP) | hoy (con URP) |
|---|---|---|
| **FPS medio** | 37 | **37** |
| WASM heap | 159 MB | **191 MB** |
| Sesión | 421 s, 0 errores | 420 s, 0 errores, 0 JS ERR |

✅ **URP no cuesta FPS.** La alarma inicial (`avg 29`) era el pico de carga de bundles.

⚠ La memoria sube un escalón. Matiz que hay que tener presente: **el heap de WASM crece a saltos
geométricos** (0,2 de paso), y 159 × 1,2 = 191 — son **dos escalones consecutivos**. Que se pase
de uno a otro NO significa +32 MB de datos: significa que el uso cruzó el umbral. El coste real
está entre 1 y 32 MB y con esta instrumentación no se puede afinar más.

⚠⚠ **Hipótesis que falló, y por qué importa cómo falló:** apagar HDR y las sombras de main light
**no devolvió ni un MB**. La primera medición pareció confirmarlo... porque **el device servía el
build anterior de caché** (`max-age=3600` en `Build/`) y comparé el mismo build consigo mismo.
De ahí sale el **sello de pipeline** que ahora imprime el receiver al arrancar:

```
RP: TvRenderPipeline scale=0.70 hdr=OFF msaa=1 sombras=OFF
```

🧭 Regla: **si el device cachea, una medición A/B sin un sello que identifique el build no vale
nada.** Y para iterar, desplegar con `max-age=60` y **restaurar 3600 al terminar**.

ℹ HDR se queda **OFF** a propósito: con `m_ColorGradingMode: 0` (LDR) no aportaba nada. Si algún
día se enciende el bloom, hay que volver a ponerlo ON.

### Lo que queda por validar

- [x] ✅ **El arreglo del rayado, VALIDADO EN LA TELE** (2026-08-21, tras encender la caja):
      el auto-encaje calculó **0,233** a partir del suelo real (`y=-2.35`) — y ahí está la
      explicación exacta del defecto: mi valor a ojo era **0,25**, y ese 7 % de más era justo la
      franja repetida que asomaba. Tropical y kelp bajan limpios hasta la arena, sin rayado.
      FPS con sesión asentada: 35 (avg 33) en tropical, 40 (avg 35) en kelp.
- [ ] Tanda A/B de FPS con el protocolo del 15-ago para saber qué cuesta URP de verdad.
- [ ] Las 54 decos: la comprobación de que URP no rompe nada se hizo con 6.

---

## 0.5 ✅✅ MEDIDO EN LOCAL (2026-08-27) — no es el grado, y el fondo está pintado así

Barrido de las 8 variantes de `Tools/grade-tune.js` sobre el **player real** (`rcv 2026-08-27
rmuid`) y medido en **L\* / C\*** con `Tools/analiza_grado_lab.py`. Escena: `bg_kelp`, 1 pez,
6 decos, día.

⚠ **Primero, por qué las cifras de antes no valían.** `grade_contact_sheet.py` informa **media de
canales RGB** y **saturación HSV**, y las dos se equivocan justo en esto:

- La media RGB **sube** al desaturar un verde saturado (`0,150,0` → gris `107`): la variante de
  control `Z`, que desatura del todo, salió **la más clara de las ocho** sin serlo.
- La saturación HSV de un verde **oscuro** es alta, así que el fondo en penumbra puntuaba más
  «saturado» que el suelo vivo — al revés de lo que se ve en la captura.

### El barrido, en unidades que no engañan

| variante | fondo alto | fondo medio | suelo |
|---|---|---|---|
| **A** el build tal cual | L\* 20.8 · C\* 21.0 | L\* 8.4 · C\* 7.9 | L\* 34.3 · C\* 38.6 |
| **B** grado del móvil exacto | L\* 21.4 · C\* 15.2 | L\* 9.0 · C\* 7.0 | L\* 34.7 · C\* **25.1** |
| **F** sin bloom, sat +18 | L\* 20.8 · C\* 21.0 | L\* 8.2 · C\* 7.8 | L\* 34.3 · C\* 38.6 |
| **Z** control extremo | L\* 21.6 · C\* **0.0** | L\* 9.1 · C\* 0.0 | L\* 35.1 · C\* 0.0 |

- 🧭 **`F` reproduce `A` clavado** (±0.0 en las tres bandas). `F` son los valores que la escena ya
  tiene, así que esto demuestra que la ruta `GRADE` y el build dicen lo mismo: **el barrido mide**.
- ⚠⚠ **Copiar el grado del móvil PIERDE color**: −13.5 de croma en el suelo (**−35 %**) y −5.8 en
  el fondo alto (**−28 %**), a cambio de **+0.5 L\*** de claridad. Es exactamente lo contrario de
  lo que se buscaba. **No hacerlo.**
- **El bloom no aporta nada en escena oscura**: entre 1.2, 0.6, 0.35 y apagado la claridad varía
  **±0.1 L\***. Lo único que mueve el croma es el parámetro de saturación. ⚠ Reserva honesta:
  `bg_kelp` es oscuro y el bloom necesita zonas brillantes — **falta remedirlo en un fondo vivo**
  antes de dar el bloom por inútil.
- **El tonemapping es casi irrelevante aquí**: `G` (sin él) contra `F` = −0.6 L\*, croma igual.

### ⚠⚠ Y lo que de verdad cambia el diagnóstico: la TV NO apaga el color

Comparando el **PNG de origen** contra el **render del player**, en cuatro fondos que van de casi
negro a muy vivo (`Tools/medir-fondos.js`):

| fondo | PNG (banda alta) | render (banda alta) | diferencia |
|---|---|---|---|
| `bg_abyss` | L\* 0.9 · C\* 1.6 | L\* 1.1 · C\* 2.7 | +0.2 L\* · **+1.1 C\*** |
| `bg_kelp` | L\* 23.5 · C\* 19.3 | L\* 20.8 · C\* 21.0 | −2.7 L\* · **+1.6 C\*** |
| `bg_tropical` | L\* 63.4 · C\* 36.0 | L\* 60.8 · C\* 35.9 | −2.5 L\* · −0.1 C\* |
| `bg_classic` | L\* 55.4 · C\* 39.3 | L\* 53.4 · C\* 40.1 | −2.0 L\* · +0.8 C\* |

**El croma se conserva entero** —incluso sube un poco, que es el `sat +18` trabajando— y la
claridad baja de forma **constante** ~2 L\* en los cuatro. Eso no es «apagado»: es un velo
pequeño y uniforme, coherente con la viñeta.

### 🎨 «El fondo casi en blanco y negro» es el arte, no el pipeline

Croma del **PNG de origen** de los 11 fondos:

| fondo | L\* | C\* | | fondo | L\* | C\* |
|---|---|---|---|---|---|---|
| `bg_abyss` | 1.4 | **2.4** | | `bg_wreck` | 13.9 | 11.2 |
| `bg_cave` | 5.0 | 3.6 | | `bg_deep` | 7.0 | 15.4 |
| `bg_jungle` | 7.3 | 5.5 | | `bg_arctic` | 25.0 | 17.4 |
| `bg_volcanic` | 6.4 | 6.7 | | `bg_tropical` | 48.5 | 29.3 |
| `bg_night` | 3.8 | 9.9 | | `bg_classic` | 37.7 | **37.2** |
| `bg_kelp` | 12.7 | 10.9 | | | | |

**Siete de once están por debajo de croma 12 en el fichero.** Son cuevas, abismos y noche: están
pintados así. `bg_classic` tiene **15× más croma** que `bg_abyss`.

🧭 **Consecuencia para la comparación con el móvil:** si las dos pantallas no tenían **el mismo
preset**, la comparación no dice nada sobre el pipeline. Es el punto 1 del protocolo de §4, y es
el que hay que asegurar antes de tocar nada.

### Lo que queda vivo de este documento

Sólo la **nitidez**, y es independiente del color:

| | resolución | píxeles |
|---|---|---|
| Móvil | 1536×1024 | 1,57 Mpx |
| TV | 1024×683 | 0,70 Mpx |

**2,25× en píxeles (1,5× lineal).** ⚠ §2.2 decía 512 y «9×»: **falso desde el 21-ago**, cuando
`de033c9` subió los 11 fondos de 512 a 1024 y este doc no se enteró. La conclusión de §3.1 (ir por
Addressables y no por subir el import) **sigue siendo la buena**, pero el premio es menor de lo
que se creía y ahora compite con un refactor de carga asíncrona.

---

---

## 0.6 ⚠ MEDIDO EN LAS DOS PANTALLAS A LA VEZ (2026-08-28) — PARCIALMENTE CADUCADO, ver §0.7

> ⚠⚠ **Leer §0.7 antes de usar esta sección.** Se midió con la tele en OTRO estado de tonemapping
> (por la mañana del 28-ago, antes del arreglo del suelo de esa noche) y con **zonas emparejadas**,
> no con bandas por fracción de filas.
> - ✅ **Lo del SUELO sigue vigente**: remedido el 31-ago da **2° de tono y 4,9 L\*** contra los
>   **0° y 7,4 L\*** de aquí. Misma conclusión.
> - ❌ **Lo del AGUA ya no describe la tele de hoy.** El §0.7 mide **25° de tono** en el agua, no 0°.
>   La causa es la **niebla de agua**, que esta sección sólo vio a medias («pierde la mitad del
>   croma») sin darse cuenta de que **también mueve el TONO**.
>
> 🧭 Es el modo de fallo por defecto de la documentación: **la nota sobrevive al estado que
> describía, y lee igual de convincente.**


Primera medición con **capturas simultáneas por `adb` de la tele y del teléfono**, mismo instante,
mismo estado real (`bg_classic` + `sub_gravel`, casteando desde `com.appquarium.qa`). Y con los
valores de post-proceso del móvil **verificados por la sesión del repo móvil**, no deducidos.

### 0.6.1 El color, midiendo zonas EMPAREJADAS (no bandas por fracción de filas)

| zona | TELE | MÓVIL | Δ (móvil − tele) |
|---|---|---|---|
| suelo cercano | L\* 66.3 · C\* 15.3 · **h 82°** | L\* 73.7 · C\* 12.5 · **h 82°** | **+7.4 L\*** · −2.8 C\* · **0° de tono** |
| agua alta | L\* 71.2 · C\* 41.5 · **h 191°** | L\* 76.0 · C\* 36.8 · **h 191°** | +4.9 L\* · −4.7 C\* · **0°** |
| agua honda | L\* 25.7 · C\* 8.1 | L\* 34.1 · C\* 15.3 | +8.3 L\* · +7.2 C\* |

- **El tono es IDÉNTICO** (0° en las dos zonas fiables). Confirma y refuerza el §0.5: la TV **no
  cambia los colores**.
- La TV está **más oscura en todo** (−4.9 a −8.3 L\*), no sólo en lo brillante.
- La TV está **más saturada en lo iluminado** (+2.8 / +4.7 C\*) y **menos en el agua honda**
  (−7.2 C\*). Eso último es la niebla de agua del 25-ago, que el móvil no tiene.
  ⚠ Ese «más saturada» sale de **+18 contra 0**, no de +18 contra −15: ver §0.6.4.bis.
- El móvil pinta el suelo **prácticamente igual que el PNG** (L\* 73.7 contra 73.1 del fichero).

### 0.6.2 ⚠⚠ Los −26 L\* del 27-ago estaban inflados por la BANDA, no por el pipeline

La banda «suelo» de `Tools/analiza_grado_lab.py` es el **25 % inferior**, y ahí dentro se promedian
el suelo cercano y el lejano, que va **fuertemente niebleado** por `SubstrateFog`. En la MISMA
captura:

| medida del suelo | contra el PNG |
|---|---|
| suelo cercano (último 10 % de filas) | **−9.7 L\*** |
| banda «suelo» (25 % inferior) | **−21 L\*** |

El hueco real contra el móvil es **7.4 L\***, no 17.4. 🧭 **Regla: en esta escena el suelo tiene un
degradado de niebla front-to-back; promediarlo entero mide la niebla, no el grado.**

### 0.6.3 El barrido de grado sobre el suelo — el grado explica algo más de la MITAD

Cuatro variantes en caliente sobre el player real (`rcv 2026-08-27 decorot`), suelo cercano,
`sub_gravel`. **Control extremo incluido: C\* → 0.0 en las tres bandas**, así que el grado llega a
los píxeles y el barrido mide.

| variante | L\* del suelo cercano | gana |
|---|---|---|
| build tal cual | 63.4 | — |
| sin tonemapping | 66.9 | **+3.5** |
| sin tm, sat 0 | 67.0 | +3.6 |
| **todo plano y sin viñeta** | **67.6** | **+4.2** |

De los 7.4 L\* de hueco, **el grado explica 4.2** (tonemapping 3.5 · **viñeta 2.0** · sat/contraste
0.7). ⚠ **La viñeta se había quedado fuera de todos los barridos anteriores** y vale la mitad de
todo el grado: es 0.095 y muerde justo en el **borde inferior** del frame, que es donde está el
suelo.

### 0.6.4 🐛 El campo `exposure` del mensaje `GRADE` NO HACE NADA (y en el móvil tampoco)

```
PostProcessingSetup        Volume priority 10 · Add<ColorAdjustments>(true)
TankLightingController     Volume priority 11 · .Override(colorFilter) + .Override(postExposure)
```

Gana el 11. Y `light_white` tiene `filterColor (1,1,1)` y `exposureOffset 0.00`, así que con esa luz
puesta **el `postExposure` de `PostProcessingSetup` se sustituye por 0**.

**Probado en píxeles**, con las dos variantes que sólo difieren en saturación y exposure:

| banda | `exposure 0.00` | `exposure −1.00` | Δ |
|---|---|---|---|
| fondo alto | 64.6 | 64.6 | **0.0** |
| fondo medio | 48.1 | 48.2 | **+0.1** |

Sale **igual**, que es justo lo que predice «está pisado». El receiver loguea `exp=-1.00` tan
tranquilo — falla en silencio.

⚠ **La primera lectura de este dato fue errónea y conviene que quede escrito:** se dijo que la
imagen salía «más clara», comparando el control contra la referencia de producción — que **también
difiere en tonemapping y viñeta**. Con la línea base equivocada, un campo inerte parece un campo
con el signo invertido. La objeción la levantó la sesión del repo móvil.
🧭 **Para aislar un parámetro hay que comparar contra la variante que sólo difiere en ÉL.**

### 0.6.4.bis ⚠⚠ En el MÓVIL el destrozo es MAYOR: son cuatro campos, no dos

`TankLightingController.cs` es fichero **compartido**, pero las dos copias **no son iguales**:

| | TV | MÓVIL |
|---|---|---|
| `profile.Add<ColorAdjustments>(…)` | **`(false)`** ← arreglado el 21-ago | **`(true)`** |
| qué pisa a priority 11 | sólo `colorFilter` y `postExposure` | **TODO el ColorAdjustments** |

`Add<T>(true)` hace `SetAllOverridesTo(true)`, así que en el móvil ganan también `saturation`,
`contrast` y `hueShift` **con sus valores por defecto**. Estado efectivo del móvil con `light_white`:

| campo | en su inspector | EFECTIVO |
|---|---|---|
| colorFilter | (0.75, 0.90, 1.00) | **blanco** |
| postExposure | +0.1 | **0** |
| **saturation** | **−15** | **0** |
| bloom · vignette | 1.2 / 0.6 / 0.75 / HQ · 0.095 | **vivos** (son otros componentes) |

⚠⚠ **Esto corrige el §2.1 y el §0.6.1 de este mismo documento:** la diferencia de saturación entre
pantallas **no es +18 contra −15, es +18 contra 0**. Quien ajuste a la baja contando con que el
móvil resta 15 **se quedará corto**.
ℹ Alcance honesto: lo del `colorFilter` está **medido** (los 0° de tono). Lo de `saturation` es
**deducción de la API**, aportada por la sesión del móvil y sin contraprueba en píxeles todavía.

- ⚠ **«Arreglarlo» subiendo la prioridad NO es un fix**: activaría de golpe un filtro azul y +0.1 EV
  que llevan quién sabe cuánto sin aplicarse. Es un cambio de aspecto, y lo decide el user.
- ℹ **Pisar `colorFilter` y `postExposure` es DELIBERADO** y está razonado en el propio fichero
  (líneas 186-187 en la copia del móvil): el preset de luz es el dueño del tinte del frame,
  precisamente para alcanzar los shaders **unlit** (`TankBackground`, decos GLB) que un
  ColorAdjustments normal no tocaría. **Eso no se toca.** El daño colateral es `saturation` y
  `contrast`, que el comentario no promete y el `(true)` se lleva por delante.
- 🧭 Es la **misma familia** del bug del `Add<T>(true)` del 21-ago que ya está en `CLAUDE.md` —
  literalmente el mismo bug, que en la TV se arregló y en el móvil sigue.

### 0.6.4.ter No hay UN colorFilter del móvil: hay SIETE

Es función del preset de luz, que elige el usuario y que llega por `change_light`:

| preset | colorFilter | exposureOffset |
|---|---|---|
| `light_white` | (1.00, 1.00, 1.00) | 0.00 |
| `light_warm` | (1.00, 0.90, 0.76) | −0.10 |
| `light_blue` | (0.72, 0.86, 1.00) | −0.30 |
| `light_deep` | (0.55, 0.65, 1.00) | **−0.60** |
| `light_purple` | (0.88, 0.72, 1.00) | −0.25 |
| `light_sunset` | (1.00, 0.82, 0.65) | −0.10 |
| `light_cycle` | **animado (HSV, 0.07 Hz)** | −0.15 |

⚠⚠ **Con `light_cycle` puesto, dos capturas no son comparables entre sí**: reescribe `colorFilter`
cada frame. El campo `luz=` de la cabecera del `DUMP` dice cuál está activo — es lo que convierte
esta tabla en un número.

### 0.6.4.quater 💡 LAS 7 LUCES SIGUEN SIN MEDIR — pero el procedimiento ya está (2026-08-30)

Fondos (§0.7) y sustratos ya están medidos; **las luces no**, y **5 de las 7 son de pago**. El 30-ago
se montó el procedimiento y **la tanda se perdió por un corte de rutado del ISP**, no por el método.
Herramientas: `Tools/barre-luces.sh` + `Tools/mide_luces.py`. Detalle en
`CAST_NEXT_SESSION_2026-08-31.md` §1-2. Lo que hay que saber para leer el resultado cuando llegue:

⚠⚠ **Una luz no se mide como un fondo.** Actúa por **dos caminos**: los **spots** (sólo alcanzan a lo
iluminado) y el **post** `colorFilter`+`postExposure` a priority 11 (alcanza al frame entero,
**incluidos los unlit**). El telón es unlit ⇒ **la banda de agua (0.12-0.50) aísla el post y la del
suelo (0.90-1.00) suma los dos. La diferencia ES la descomposición.**

🧭 **`light_white` no es la referencia que parece:** neutro **sólo en post**; su spot es
`(1.00,0.97,0.93)` y su `spotIntensity` **1.0** contra 2.5-3.5 del resto ⇒ **en el suelo el post
negativo de los demás pelea contra ×3 de luz y esa banda no aísla para nadie.**

⚠⚠ **`light_cycle` se mide APARTE, al final y como RANGO** sobre un periodo entero (14,3 s), fuera de
la tabla de ΔE al vecino: no tiene un valor, tiene un recorrido. Es la consecuencia operativa del
aviso de arriba.

🏆 **Y para repartir entre los dos caminos NO se restan deltas de Lab**: daba 4,4-16,5 de
«iluminación» sobre fixtures donde la respuesta era **0**, porque Lab es no lineal. El post es un
producto por canal y sólo se separa en **espacio lineal** — con suelo de ruido medido (**2,2**) y
umbral (**2,5**), o el número es un artefacto.

### 0.6.5 ⚠⚠ EL BLOOM: la prueba que lo descartó no estaba probando el bloom

Valores reales, verificados a los dos lados:

| | TELE | MÓVIL |
|---|---|---|
| bloom | **OFF** (intensity 0.35) | **ON, intensity 1.2** |
| **bloomThreshold** | **0.92** | **0.60** |
| scatter / HQ filtering | 0.6 / — | 0.75 / **true** |
| **tonemapping** | **Neutral** | **NINGUNO** |
| contraste / saturación | +10 / **+18** (vivos) | 0 / **0 efectivo** (su −15 está pisado, ver §0.6.4.bis) |
| **HDR** | **OFF** | **ON** |
| renderScale | 0.75 | 0.8 |

El §0.5 dio por cerrado que «el bloom no aporta nada en escena oscura (±0.1 L\*)». Pero ese barrido
usa `Tools/grade-tune.js`, que **sólo manda `bloom` y `bloomIntensity` y NUNCA el umbral**: las ocho
variantes corrieron a **threshold 0.92**, que en escena submarina no lo cruza casi ningún píxel.
**La conclusión medía el umbral, no el bloom.**

⚠ El mensaje `GRADE` **tampoco expone** `bloomThreshold` / `bloomScatter` / `highQualityFiltering`,
así que esto **no se puede probar en caliente**: hace falta ampliar `GradePayload` y entra en el
build pendiente. Es el cambio de mayor retorno que queda.

🧭 Misma familia que el `bg_ocean` de meses en verde: **la prueba pasaba y no comprobaba lo que
decía comprobar.**

### 0.6.6 ⚠⚠ LA NITIDEZ: la TV no se ve más BORROSA, se ve más DURA — el §2.2 está al revés

Medido con las dos capturas al **mismo tamaño en píxeles** (escala verificada por correlación
cruzada sobre el cañón: **1.00×**), energía de alta frecuencia como fracción del espectro:

| región | TELE | MÓVIL | móvil/tele |
|---|---|---|---|
| **grava del suelo** (mucho detalle real) | 0.166 | 0.163 | **0.98× — empatan** |
| **agua plana honda** (sin detalle que dibujar) | 0.0698 | 0.0136 | **0.19×** |
| telón de fondo | 0.005 / 0.017 | 0.004 / 0.007 | 0.92× / 0.42× |

O sea: **donde hay detalle real, empatan**. Lo que la TV tiene de más es **5.13× de energía de alta
frecuencia en zonas donde no hay nada**, o sea **grano**. Ampliadas ×6 se ve qué es: las partículas
del telón salen en **bloques cuadrados de alto contraste** en la tele y **suaves** en el móvil.

Dos causas descartadas **con medida**:

| candidato | resultado |
|---|---|
| ¿lo crea el grado? | **no** — con el grado aplanado entero: 0.0111 → 0.0105 |
| ¿lo explica la resolución (1024 vs 1536)? | **no, va al revés**: la cadena de 1024 sin comprimir predice **0.32×** (más suave); se mide 5.13× (más duro). Discrepancia de ~16× y **de signo contrario** |

Lo que queda en esa cadena es el **formato de compresión**:

```
TV  (WebGL)      maxTextureSize 1024 · textureFormat -1  → DXT1     (4 colores por bloque de 4x4)
MÓVIL (Android)  maxTextureSize 2048 · textureFormat 50  → ASTC 6x6
```

⚠⚠ **NO está probado.** No se ha generado una versión DXT1 para comparar píxel a píxel. Lo que hay
es que **resolución y grado quedan descartados con medida** y el formato es lo único que queda.

⚠ **Objeción abierta, de la sesión del repo móvil:** que su `renderScale 0.8` con filtro de subida
bilineal sea un paso bajo que se coma la alta frecuencia en zonas planas. Los framebuffers físicos
(`adb shell wm size`) dicen que va al revés:

| | render | cadena hasta el panel |
|---|---|---|
| TV | 1920x1080 (Unity reporta `Screen 2560x1440` × 0.75) | **sube a 2560x1440 y el compositor la baja a 1920x1080** |
| móvil | 864x1920 (1080x2400 × 0.8) | sube a 1080x2400, sin bajada |

La cadena de la TV tiene **un paso bajo MÁS**, no menos. Pero un remuestreo no entero
arriba-y-abajo puede generar aliasing que parece grano, así que no está cerrado.
🧭 **Test decisivo y gratis:** `GRADE={"renderScale":1.0}` en la tele → renderiza 2560x1440 y el
compositor lo baja a 1920x1080, o sea **supersampling limpio**. Si el grano del agua plana se
desploma, era el remuestreo; si aguanta, es la textura.

### 0.6.7 ⚠ Consecuencia para el §3.1: la palanca podría ser la EQUIVOCADA

El §3.1 propone subir el import a 1536 (+6,4 MB en el `.data`) o sacar los fondos a Addressables,
y **las dos apuntan a resolución**. Si la causa es el formato, **subir el tamaño dejando DXT1 no
arregla nada**. El experimento barato es tocar `textureCompression` en los 11 fondos.
⚠ Cuesta un build de player: los fondos van **horneados en el `.data`**.

### 0.6.8 🧰 Cómo se midió (para poder repetirlo)

```bash
# capturas simultáneas de las dos pantallas (el instante importa: hay peces moviéndose)
adb -s <IP_TV>:5555 exec-out screencap -p > tv.png  &
adb -s <SERIAL_MOVIL> exec-out screencap -p > movil.png  &
wait
```

⚠⚠ **Dos senders NO pueden convivir.** Lanzar `cast-headless` con la app del móvil aún conectada
deja el receiver en `buffered` y **no monta la escena para nadie** (pasó el 28-ago: `Sender
CONNECTED #1 … sender-0` y `#2 … com.appquarium.qa-4` en el mismo segundo, y un
`pm.load RECHAZADO LOAD_CANCELLED` que parecía un bug del player y no lo era). Antes de castear
desde aquí, **el teléfono tiene que desconectar**.

⚠ Las bandas por fracción de filas **no valen** para comparar pantallas de distinta relación de
aspecto (1920x1080 contra 2400x1080). Hay que **detectar la línea del suelo** en cada captura y
medir zonas emparejadas.

⚠ Al comparar nitidez hace falta un **control sin detalle** (agua plana). Sin él, «más energía de
alta frecuencia» se lee como «más nítido» cuando puede ser ruido — que es justo lo que pasaba aquí.

## 1. Lo que se ve (reportado por el user)

1. **Los colores no se ven tan nítidos ni tan bonitos como en la app.** El fondo en concreto se
   ve *«prácticamente blanco y negro»*.
2. **Las sombras de las decos caen sobre el FONDO** (el telón de atrás del todo), no sobre el
   suelo. De los peces no se fijó. Duda abierta del user: *«no sé si deberían mostrarse ahí, o
   igual sí, al ser una pecera»*.

---

## 2. Lo que YA está comprobado (leído del proyecto, no medido en pantalla)

### 2.1 🎯 El grado de color de TV y el del móvil son DISTINTOS a propósito ⚠ **ver §0.6.4.bis**

> ⚠⚠ **28-ago: la mitad de los valores que compara esta sección NO SE APLICAN.** En el móvil,
> `colorFilter`, `postExposure` y **`saturation −15`** están pisados por el Volume de
> `TankLightingController` (priority 11, `Add<T>(true)`). La diferencia real de saturación es
> **+18 contra 0**. Ver **§0.6.4.bis**.

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

### 2.2 ~~🎯 Los fondos van a la TV a 1/16 de píxeles~~ ⚠⚠ **DESFASADO Y DEL REVÉS — ver §0.5 y §0.6.6**

> ⚠⚠ **28-ago: medido al mismo tamaño en píxeles, la TV NO se ve menos nítida** — empata con el
> móvil en la grava del suelo (0.98×). Lo que tiene de más es **grano** en zonas planas (5.13×), y
> la resolución **predice lo contrario** (0.32×). Toda esta sección razona sobre un síntoma que no
> es el que se mide. Ver **§0.6.6**.

> ⚠⚠ **Esta sección dice 512 y «9×», y las dos cifras son falsas desde el 21-ago.** Ese día,
> `de033c9` subió los 11 fondos de **512 a 1024** en el override de WebGL y el doc no se
> enteró. La diferencia real con el móvil es **2,25× en píxeles (1,5× lineal)**, no 9×. Lo
> que sigue vale como historia de cómo se llegó aquí, no como estado.

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

## 5. Estado — 2026-08-28

- [x] ✅ **URP encendido y desplegado** (21-ago, §0.3). El grado se aplica de verdad.
- [x] ✅ **Medido en el player real** (27-ago, §0.5) y **en las dos pantallas a la vez** (28-ago,
      §0.6). La TV **no cambia los colores** — el tono sale a **0° de diferencia**.
- [x] ✅ **Explicado el «fondo casi en B/N»**: 7 de los 11 fondos están por debajo de croma 12
      **en el PNG de origen**. Es el arte.
- [x] ✅ **La comparación honesta con el mismo estado** — hecha (§0.6.1), con capturas simultáneas
      por `adb` de las dos pantallas.
- [x] ✅ **La nitidez, medida** (§0.6.6): **al mismo tamaño en píxeles la TV EMPATA** con el móvil
      donde hay detalle real (0.98×). El §2.2 razonaba sobre un síntoma que no es el que se mide.
- [ ] ⭐⭐ **LO DE MAYOR RETORNO QUE QUEDA: el bloom, y hace falta tocar C#.** El móvil bloomea a
      **threshold 0.60** con intensity 1.2, scatter 0.75 y HQ filtering; la TV lo tiene **OFF con
      threshold 0.92**. El «el bloom no aporta nada» del §0.5 se midió **sin tocar el umbral**
      (`grade-tune.js` no lo manda), así que medía el umbral, no el bloom. `GRADE` tampoco expone
      `bloomThreshold`/`bloomScatter`/`HQ` → **ampliar `GradePayload`** y entra en el build
      pendiente; después ya se barre en caliente sin gastar más builds.
- [ ] ⭐ **El grano del agua plana** (§0.6.6): 5.13× más que el móvil, con **grado y resolución
      descartados con medida**. Queda el formato (DXT1 contra ASTC 6x6) y **no está probado**.
      Test gratis pendiente: `GRADE={"renderScale":1.0}` como supersampling limpio.
- [ ] ⚠ **La palanca del §3.1 podría ser la equivocada** (§0.6.7): apunta a resolución, y si la
      causa es el formato, subir el import no arregla nada.
- [ ] ❓ **Decisión del user, y son cambios de ASPECTO, no bugs:**
      · resucitar `saturation`/`contrast` en el móvil (`Add<T>(false)`, §0.6.4.bis)
      · el `postExposure` de la TV, hoy inerte (§0.6.4)
      · bajar la **viñeta** (0.095, vale **2.0 L\*** en el suelo, §0.6.3)
- [ ] Decisión del user: **qué debe hacer la sombra** de una deco del fondo.
- [ ] ⚠ **Reportado por el user el 28-ago y aún sin cerrar:** «en el teléfono se ve precioso, en la
      tele muy apagado; le falta nitidez, color y vida». La fotometría dice que **apagado = más
      oscuro** (−4.9 a −8.3 L\*) y que el agua honda pierde **la mitad del croma** (C\* 8.1 contra
      15.3) por la niebla. Su hipótesis —«la capa azul está demasiado fuerte»— **está sin medir**:
      el barrido de niebla del 28-ago se perdió por la colisión de dos senders (§0.6.8).


---

## 0.7 ✅ LAS 7 LUCES EN LAS DOS PANTALLAS (2026-08-31) — y la niebla explica el color

Tele `rcv 2026-08-28 tmA` (`HORNEADO: bloom=0.30 thr=0.60 tm=Neutral sat=18 con=10`) contra móvil
`com.appquarium.qa` **1.2.5 / code 40**. Misma escena, montada en el móvil y **verificada por save**:
`bg_classic` + `sub_gravel` + `tank_l` + `deco_anchor` en `x=0` + `ambient=day`.

### 0.7.1 La estructura del catálogo se mide IGUAL en las dos

| par | tele (agua alta / honda / suelo) | móvil |
|---|---|---|
| `warm` / `sunset` | 8.2 · 5.9 · 9.2 | 7.4 · 6.2 · 8.2 |
| `deep` / `purple` | 8.8 · 3.5 · **23.5** | 13.5 · 6.6 · **22.1** |

**Los mismos dos pares, en el mismo orden de cercanía**, con dos aparatos, dos pipelines y dos
analizadores escritos por separado. **Ninguna luz fundida en ninguna de las dos.**

### 0.7.2 🎯 La única diferencia real: la NIEBLA DE AGUA, y es deliberada

`light_white`, regiones normalizadas:

| región | tele | móvil | Δ |
|---|---|---|---|
| **suelo** | L\* 66.8 · C\* 15.7 · **h 79** | L\* 71.7 · C\* 12.0 · **h 81** | **4.9 L\*** · **2°** |
| **agua** | L\* 54.6 · C\* 34.5 · **h 252** | L\* 66.1 · C\* 35.3 · **h 227** | **11.5 L\*** · **25°** |

**El suelo coincide en tono; los 25° viven sólo en el agua** ⇒ la **niebla de agua del 25-ago** (§ de
`tv_niebla_de_agua`), que es de la TV y sólo de la TV, y tiñe hacia el color del agua.

⚠⚠ **NO reportar «la tele es 12,6 L\* más oscura».** Son **~5 L\* de pantalla MÁS la niebla**: dos
hechos distintos, y juntarlos inventa un número que no describe ninguno de los dos.

### 0.7.3 ⚠ Cómo se comparan dos pantallas sin engañarse

- **`--aspect-ref 1.7778` en las DOS tandas.** Tele 1.78 · móvil **2.22** ⇒ sin recortar, el móvil
  integra un **25 % más de mundo** a los lados. Es neutro donde no hace falta.
- ⚠ El `screencap` de la tele sale **1920x1080** aunque Unity reporte `Screen` 2560x1440.
- ⚠⚠ **`x = 0` es la única coordenada X que significa lo mismo en dos aspects distintos**: los
  bounds X son `worldHalfHeight × aspect` (7.47 = 4.20 × 1.778). Vino gratis porque `--decos` con una
  sola deco la pone en 0 — **con dos decos habríamos medido mal**.
- ✅ El eje **Y sí coincide**: los dos `tank_l` valen `worldHalfHeight = 4.2` (leído del asset en los
  **dos** repos), y el borde del suelo cae en **0.7921** contra **0.7792** ⇒ **14 px**.
- ⚠⚠ **Falsa alarma que costó una investigación:** el primer detector de borde daba **160 px** y era
  falso — medía el mayor salto de luminancia, que en el móvil ES el borde agua/grava y **en la tele
  es la banda oscura del fondo**. 🧭 *No es que el instrumento no viera la magnitud: veía OTRA en
  cada entrada y las reportaba en las mismas unidades*, con dispersión `0.0000` que **reforzaba la
  confianza en el artefacto**. `mide_luces.py` usa ahora **dos criterios** y **se calla** si
  discrepan. Detalle en el fichero de memoria `el_instrumento_no_ve_la_magnitud`.
