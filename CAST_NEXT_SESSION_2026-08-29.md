# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-28**. La anterior está en `CAST_NEXT_SESSION_2026-08-28.md`.
>
> **El día que las dos sesiones de Claude se hablaron.** El user pidió que la sesión de este repo y
> la del repo móvil trabajaran en directo (`ListAgents` + `SendMessage`), y de ahí salió casi todo
> lo de abajo. Tres razonamientos tumbados por medidas, dos de ellos míos.
>
> ✅ **Y por la tarde los dos repos trabajaron a la vez** (§0.bis): se cerraron **seis cosas**, todas
> validadas en el device — el canal de vuelta, la §6.5, el volcado automático, la edición de decos,
> el `silence.wav` y el campo `lang`. **Nada quedó desplegado sin validar.**

---

## 0. ✅ LO QUE EL USER APROBÓ MIRANDO LA TELE

> ⚠⚠ **SUPERADO POR §0.ter LA MISMA NOCHE.** Lo de esta sección se aprobó **mirando el agua**,
> y **reventaba el suelo** (53,68 % de la banda clavada al blanco, contra 0,00 % el 27-ago).
> Lo desplegado hoy es `bloom 0.30 + tonemapping`, no lo que dice la tabla de abajo.
> **Las cifras de aquí siguen siendo válidas para el agua**; lo que faltaba era el resto del encuadre.

Reportó que la tele se veía «muy apagada» al lado del móvil. Medido separando palanca por palanca
sobre el device, y **aprobado por él con el teléfono al lado**: *«yo lo que está ahora casteándose
lo veo bien»*.

| zona | producción | **aprobado** | móvil (medido el mismo día) |
|---|---|---|---|
| suelo cercano | L\* 66.3 · C\* 14.6 | **L\* 72.4 · C\* 22.0** | L\* 73.7 · C\* 12.5 |
| agua alta | L\* 67.9 · C\* 38.8 | **L\* 75.9 · C\* 45.1** | L\* 76.0 · C\* 36.8 |
| agua honda | L\* 23.6 · C\* 24.4 | L\* 12.6 · C\* 27.1 | — |

**El agua alta queda clavada con el teléfono (75.9 contra 76.0)** y el suelo a 1,3 puntos, cuando
por la mañana estaban a 7,4. Con **más croma** que el móvil en las dos zonas.
⚠ **Peaje: el agua honda se oscurece ~13 L\***. Eso es quitar el tonemapping, y es criterio, no
medida.

**✅ DESPLEGADO Y VALIDADO** (sello `rcv 2026-08-28 visual`; en la tele: `agua: den=0.15 desat=0.16
dim=0.08 deco=0.12`). Horneado en dos sitios:

```
Assets/Scenes/TvScene.unity            enableTonemapping: 1 → 0 · vignetteIntensity: 0.095 → 0
Assets/Scripts/Core/TvSceneBootstrap.cs   NieblaDens 0.30→0.15 · DecoNiebla 0.25→0.12
                                          TonoDesat 0.32→0.16 · TonoDim 0.16→0.08
```

⚠⚠ **Dos trampas al hornearlo, las dos silenciosas:**
1. **Los valores del grado viven en la ESCENA, no en el `.cs`.** Tocar `PostProcessingSetup.cs` no
   habría hecho nada.
2. **El fichero de escena es CRLF.** Un `replace` con anclas terminadas en `\n` **no encuentra nada
   y devuelve 0 coincidencias sin error**.

⚠⚠⚠ **Y CADUCA — está anotado junto a las constantes, no sólo aquí:** la referencia fue el **móvil
del 28-ago**, que tiene su `saturation: -15` **muerto** (su `TankLightingController` hace
`Add<ColorAdjustments>(true)` a priority 11; aquí está arreglado desde el 21-ago, allí no). O sea
que esto iguala la tele a **«el móvil CON el bug»**. El día que allí lo arreglen, su croma bajará y
**este ajuste se descuadrará solo**: hay que **remedir**, no parchear a ojo.

---

---

## 0.ter 🚨✅ LA NOCHE: el ajuste de la mañana REVENTABA EL SUELO — arreglado y desplegado

⚠⚠ **Lo aprobado por la mañana (§0) tenía un defecto que nadie miró.** El user pidió barrer «fondos,
suelos, luces y todo eso» y el barrido lo encontró:

| banda del **suelo**, `bg_tropical` | 27-ago | 28-ago mañana (aprobado) |
|---|---|---|
| % de píxeles clavados al blanco | **0,00 %** | **53,68 %** |
| superficie con rango utilizable | 99 % | **18 %** |

🧭 **Se aprobó mirando el agua. Nadie miró el suelo** — ni el user ni yo. La aprobación de §0 sigue
siendo válida *en lo que se miró*; lo que faltaba era el resto del encuadre.

### La causa: un aportador y un ausente

Aislado en caliente con `GRADE`, sobre la misma escena y en la misma sesión:

```
bloom ON  · tm OFF   53,68 % clavado    <- lo que se desplego por la mañana
bloom OFF · tm OFF    3,81 %
bloom ON  · tm ON     0,00 %
bloom OFF · tm ON     0,00 %
```

**El tonemapping es la compuerta**: con él, clip 0 aunque el bloom vaya a tope. El bloom mete la
energía (3,8 → 53,7) y **el tonemapping era lo único que la absorbía**.

⚠⚠ **Mecanismo (lo aportó la sesión del repo móvil): el pipeline de la TV va con HDR APAGADO.** En
LDR todo lo que pasa de 1.0 **se clava al escribir**. El móvil corre bloom sin tonemapping y no se
quema **porque sí tiene HDR**. ⇒ *En LDR el tonemapping no es estética, es el paracaídas.*
Y explicaba una rareza ya medida: **los 11 fondos daban cifras idénticas**, `bg_abyss` —negro—
incluido. Un clip por saturación del búfer **no depende del contenido**.

### Lo elegido por el user, entre cuatro imágenes: `bloom 0.30 + tonemapping`

```
bloom   agua L*  suelo L*  suelo con rango  textura
OFF       56.7     69.5        100 %          91 %
0.30      58.9     72.4         88 %          86 %   <- LA A, elegida
0.90      61.6     76.6         62 %          79 %
1.20      61.7     78.6         52 %          78 %
```

**No hay rodilla: es un intercambio suave.** Era elección del user, no un óptimo calculable.
⚠ `exposure` **no compensa: es inerte en la TV** (Volume de la barra LED, prioridad 11).

🎯 **Y no es una vuelta atrás**: la A queda **más clara que el 27-ago en las dos bandas**
(agua 55.5 → 58.9, suelo 67.2 → 72.4). La claridad la daba **la niebla**, que se queda intacta.

### ✅ DESPLEGADO Y VERIFICADO EN EL DEVICE

Sello **`rcv 2026-08-28 tmA`** · commit `ffe29dc`.

```
                      medido en vivo    DESPLEGADO
agua L*                    58.9         58.3 / 60.0
suelo clavado             0,00 %          0,00 %
textura del suelo           86 %         83 / 86 %
```

⭐ **Prueba de artefacto nueva**: `PostProcessingSetup` emite por **JsBridge** (canal Cast, no
`Debug.Log`, que no viaja) la línea

```
HORNEADO: bloom=0.30 thr=0.60 tm=Neutral sat=18 con=10 exp=0.05 vig=0.00
```

🧭 **Su texto lo generan los valores del build**, así que una sola lectura demuestra a la vez *que
corre el binario nuevo* **y** *que se horneó lo que se midió*. Mejor que un sello: un sello dice
«soy la versión X» y hay que fiarse; esto **dice lo que hace**.

⇒ De paso descartó la caché: los `Build/*` van con `max-age=3600` y el device **podría** haber
servido el player viejo durante una hora. No lo hizo.

### 🎨 El diferencial (criterio del user: «como en la app móvil»)

**Fondos: ninguno fundido**, y la tele los separa **más** que el arte (`bg_tropical` ΔE 33.2 → 37.3).
**Sustratos: sólo `sub_sand`/`sub_white`** fundidos (ΔE 1.3), y **ya venían a 2.2 en el arte**.

⇒ **Tres pares apretados, los tres de origen en el arte y ninguno arreglable por render:**

```
sub_sand / sub_white     arte 2.2 -> pantalla 1.3   FUNDIDOS   (los dos gratis)
bg_abyss / bg_cave       arte 4.4 -> pantalla 3.3   casi       (los dos de pago)
bg_deep  / bg_night      arte 6.4 -> pantalla 5.5   casi       <- CRUZA PRECIO
```

⚠ Y el aviso de la sesión del móvil: **`colorA`/`colorB` es código muerto** para los sustratos que
tienen PNG (`BuildFloorMaterial` carga el PNG y gana). Quien vaya a separarlos tocando esos valores
no cambiará nada. **El arreglo son los PNG.**

Herramienta: `Tools/mide_diferencial.py bg` · `sub` (compara ΔE del arte contra ΔE en pantalla).

### 🚻 Frases de la splash: neutras en género

El user preguntó si se tiene en cuenta el sexo. **No, y no por descuido: el dato no viaja** —
`TvFishEntry` trae `speciesId`, `nickname`, `uid`, `ageScale` y nada más. Un macho salía como
*«Nemo está deseando que LA veas»*. **7 de 26 plantillas** estaban marcadas; ya son neutras en los
dos idiomas, con **guarda en `Tools/test-frases.js` validada en rojo**.

⚠ Campo `sex` **pedido al móvil** y documentado en `CAST_CONTRACT_TV.md` con sus valores exactos
(`"Male"` / `"Female"` / `"Unknown"` / `""`) y **la trampa del `"Male"` por defecto**, que no
significa «es macho» sino «nadie ha dicho lo contrario»: el emisor debe mandar `""` si no está
seguro.

### ⚠⚠ CUATRO TRAMPAS DE MEDIDA QUE COSTARON TANDAS ENTERAS

1. **Métrica global sobre el encuadre entero** → los 11 fondos idénticos. El suelo, más brillante
   que todos, dominaba la cola alta. **Medir por bandas** (`Tools/mide_bandas.py`).
2. **La métrica de textura dio «OK 86 %» a la imagen reventada**: los bordes del clip son gradientes
   enormes que sostienen la desviación. **Medir sólo donde queda rango**, máscara erosionada
   (`Tools/mide_textura_suelo.py`).
3. **Sincronizar contra una línea que no distingue lo que buscas**: se usó `BLOOM:`, que es idéntica
   con el bloom encendido y apagado, teniendo `GRADE: bloom=OFF` al lado. Con `sleep` encadenados de
   más, **las 6 etiquetas de la primera tanda eran falsas** (una captura salió 7 s *después* de
   acabar la sesión). 🧭 Capturar **por evento**, y anotar en un acta el segundo de cada captura.
4. ⚠⚠ **`adb exec-out screencap` puede devolver un FOTOGRAMA CONGELADO** con la app viva y el log
   sano (`stream sent=379 fail=0`). Cuatro capturas byte a byte iguales; iba a colar **«dos sustratos
   fundidos a ΔE 0.1»**. 🧭 *Un cero perfecto casi nunca es un resultado; casi siempre es un fallo de
   tubería.* Guarda de md5 ya puesta en las dos herramientas de barrido.

Y dos más, de proceso:

- ⚠ **NO editar un script mientras se ejecuta.** Bash lo lee por trozos: se le añadió una guarda en
  vuelo y reventó a media función, dejando la tanda **sin una sola captura**. **`bash -n` daba
  verde** — el fichero era válido, lo inválido era el estado del intérprete.
- ⚠⚠ **Un build de player REVIERTE un deploy de `index.html`**: lo regenera desde el template. Hoy se
  desplegó **5 veces** sin pasar por el template; **comprobar los marcadores en el template ANTES de
  cada build** (se hizo, y estaban los 8).

### 🐛 Y una afirmación mía que era FALSA, corregida

Dije que el blanco se había tragado una deco («una concha ha desaparecido»). **Falso**: comparé
contra una captura del 27-ago que tenía **4 decos** (`anchor`, `coral_corallium`, `shell_lambis`,
`starfish_blue`) mientras la mía tenía **2**. La concha nunca estuvo en mi escena. La otra sesión ya
lo había escalado a «pérdida de contenido / bug de producto» y hubo que retirarlo.

🧭 **Mirar la imagen resuelve «¿qué hay ahí?». No resuelve «¿comparado con qué?».**
🧭 Prueba barata que lo habría cazado: **si afirmas que X desapareció por culpa de Y, comprueba que X
está presente donde Y no está.** La concha tampoco estaba en las variantes con clip 0.

### 🔧 De paso: `check_preset_ids.js` gritaba en falso

Partía por el salto de línea **a secas** sobre un fichero **CRLF**, así que la comparación con el
cierre del método **nunca cortaba**: se comía el switch de `ApplyAmbientMode` y reportaba `sunset` y
`night` como tipos de UPDATE sin documentar. Contaba **17 en vez de 14**.
🧭 *Una guarda que grita en falso acaba ignorándose.*

---

## ⏭ MAÑANA SE EMPIEZA AQUÍ

1. ⭐ **El casteo con la app REAL no se hizo.** La tele quedó lista y verificada; el user cerró antes.
   Sigue sin probarse `com.appquarium.app` firmado **con los asset packs servidos por Play** (todo lo
   de hoy fue `com.appquarium.qa` con `--local-testing`). 🔎 **Señal de que el pack no se resolvió:
   esferas en vez de peces.** ⇒ **Producción sigue parada, y correctamente.**
2. **Las 7 luces sin medir.** Fondos (11) y sustratos (12) sí. **Marcado como NO hecho**, no como
   «probablemente bien».
3. **Los tres pares apretados** — decisión de **contenido** del user, no de render. El que cruza
   precio (`bg_deep` 0,49 € contra `bg_night` gratis) es del lado móvil.
4. **El campo `sex`** — aditivo, barato, sin fecha. Entra gratis en el siguiente build del móvil.

⚠ **Respaldo del player anterior** en `_rollback_2026-08-28/` (ignorado por git), verificado contra
los ETag de R2. Rollback = un comando.

⚠ **`main` tiene commits sin pushear** (`ffe29dc` el último). El repo es **público**: antes de
pushear, que no viaje `TvBundleAuthSecret.cs`.

---

## 0.bis ✅✅ LA TARDE: los dos repos trabajando a la vez, y seis cosas cerradas

El user dio luz verde a las dos sesiones para tocar sus repos y probarlo juntos. **Todo lo de abajo
está validado en el device**, y ninguno quedó ambiguo — que es lo que más trabajo costó.

| | cómo se cerró |
|---|---|
| **§6.5 del contrato** (uid + parejas) | diff de los dos volcados: 6 uid **byte a byte**, `uid propios: 0`, pareja cableada en los dos lados |
| **El canal de vuelta** | **603 mensajes, 0 fallos** en 15 min. Antes: 1-4 por sesión |
| **Volcado automático** | `cast_dump_tv.txt` **existe por primera vez** |
| **Editar una deco colocada** | `quat` y `escala` **idénticos** en los dos volcados |
| **El `silence.wav` del emisor** | apagado → nuestro `pm.load` pasa de **RECHAZADO** a **RESUELTO** |
| **El campo `lang`** | `lang=es -> es` en device, **sin `DIAG`**, en el segundo 1,7 |

### ⭐ El `silence.wav`: una hipótesis que nació mal, se debilitó… y acabó siendo cierta de OTRA cosa

Recorrido completo, porque es el mejor ejemplo del día de por qué **separar afirmaciones pegadas**:

1. Nació como *«su `load()` cancela nuestro `pm.load` y por eso muere el relay»* — **dos cosas en una**.
2. La sesión del móvil la debilitó ella misma: en **2 de 6** el rechazo llegaba **antes** que su `load()`.
3. Se redujo a lo único defendible: *«con su APK el relay muere en ~2 s; sin él aguanta 400 s»* (200×).
4. El relay resultó ser **otra cosa** (el `gms_cast_mrp`, §1) y se curó sin tocar su APK.
5. Y al quitar el `silence.wav`: **7 sesiones seguidas con `pm.load` RECHAZADO → 1 RESUELTO a la
   primera.** La competencia por la sesión de media **era real**, sólo que no causaba lo que creíamos.

🧭 **Si nos hubiéramos aferrado a la versión fuerte**, habríamos «arreglado» el relay quitando el
`silence.wav` y el bug de la difusión seguiría ahí. **Si la hubiéramos tirado entera**, su `pm.load`
seguiría rechazado sin que nadie lo supiera. **Separar las dos afirmaciones salvó las dos.**

### 🖼 Frases rotativas en la pantalla de carga (pedido del user)

53 fijas en **castellano e inglés** (30 ambiente + 18 info + 5 espera) más 16 plantillas
personalizadas con los **motes reales** que el móvil ya mandaba y que **no se pintaban en ningún
sitio** (`CAST_CONTRACT_TV.md` §4.2). En device salió *«Escarlata pregunta si has traído comida.»*

⚠⚠ **Y un fallo propio que conviene no repetir:** durante un rato el idioma se **leía**, se
**logueaba**… y se **ignoraba** — `_fuentes()` usaba `FRASES.es` a pelo. Un usuario en inglés habría
visto castellano **y el log habría dicho `lang=en -> en` tan tranquilo**.
🧭 **Infraestructura multiidioma con contenido de un solo idioma es PEOR que no tenerla, porque
parece que funciona.** Cubierto con 11 tests, incluido uno que exige que los dos bancos tengan el
**mismo número de frases por tipo** — si no, un idioma tendría menos variedad sin que nadie lo notara.

### 🧹 Producción limpia

El user: *«sin ese debug es pro normal; no debería verse nada del número de versión ni fps».* El
**sello** y el **HUD del relay** ahora sólo salen con `DIAG`.
⚠ Eso **cambia el protocolo de diagnóstico**: «mándame una captura» ya no vale a secas.
🧭 Lo bueno: `DIAG` viaja por el **canal de ida**, el único que no se rompió en todo el día, así que
el diagnóstico sigue siendo alcanzable justo cuando el retorno falla.

### 🧹 Y producción quedó limpia

El **sello** y el **HUD del relay** sólo salen con `DIAG` (antes el sello salía siempre y el HUD los
primeros 60 s y ante cualquier fallo). ⚠ Eso cambia el protocolo: «mándame una captura» ya no vale a
secas. 🧭 Pero `DIAG` va por el **canal de ida**, así que sigue alcanzable cuando el retorno falla.

### ⚠ Dos avisos operativos que salieron de la tarde

- **Volcados: hace falta ARRANQUE EN FRÍO**, no una reconexión — hasta que su disparador de respaldo
  esté desplegado. Ver §2.bis.
- **Su respaldo iba a dispararse a los 45 s** y nuestros arranques tardan **33-50 s**: habría volcado
  un acuario a medias, y eso en el diff **no se lee como «llegué pronto», se lee como desajuste del
  protocolo**. Subido a 90 s. 🧭 **Fabricar evidencia falsa es peor que no tener ninguna.**
  Pendiente nuestro: guarda de `montado=si/no` en el `dump`.
- ⚠ **Su push está bloqueado por GitHub** (blob de 123 MB en la historia, 64 commits atascados).
  **El nuestro no**: mayor blob **47,1 MB**, ninguno > 100. Comprobado, no supuesto.

## 1. ✅✅ EL RELAY: RESUELTO EL MISMO DIA (era el sender equivocado)

> Esta sección se escribió como «lo primero que hay que hacer mañana» y se cerró dos horas
> después. Se deja entera abajo porque el **camino** vale más que el resultado.

**La causa, con nombre y milisegundo:**

```
12:13:52.860  Sender CONNECTED #1: …:com.appquarium.qa-43
12:13:52.861  Sender CONNECTED #2: …:gms_cast_mrp-42      ← 1 ms después
```

Cuando el emisor abre la sesión de media, **GMS registra su Media Route Provider como segundo
sender**. `_lastSenderId` pasaba a apuntarle y todas las líneas se enviaban a un sender **vivo y
válido que no escucha nuestro namespace**. Enviar ahí **no lanza**.

🧭 **Por qué llevaba tanto oculto: el bug se escondía a sí mismo.** El `dbg('Sender CONNECTED
#2…')` se emite **después** de reasignar `_lastSenderId`, así que **el aviso de que había un
segundo sender viajaba al segundo sender**. La primera vez que se vio ese `#2` fue al curarlo.

**Arreglo, una línea:**
```js
ctx.sendCustomMessage(NAMESPACE, undefined, payload);   // era _lastSenderId || undefined
```

**Medido: 134 líneas en 120 s, contra 1-4 antes.** Desplegado y verificado.

### ✅ Y con el canal abierto se cerró la §6.5 del móvil

| | |
|---|---|
| los 6 uid, **byte a byte** los del móvil | `uid propios = 0` ✅ |
| la pareja **cableada en los dos lados** | `pairs recibidas = cableadas` ✅ |
| ancho del tanque | `9.33 × 0.8 = 7.47` **exacto** ✅ |
| X de las decos | `-0.43→-0.35` y `1.83→1.47`, exactas ✅ |
| punto decimal en todo | la cultura invariante, **validada en device** ✅ |

⚠ **Un bug del móvil que esto destapó**: su `line.StartsWith("DUMP")` no casa nunca, porque
nuestro `JsBridge.jslib` decora la línea (`[36.8s] [C#] DUMP pez …`). Lo arreglan ellos con un
`IndexOf`, que **no depende de que nosotros no volvamos a decorar**.

### ⚠ Y una lectura que NO hay que sacar

El `MEM#` (heap de WASM/JS) sale **plano** durante 6 minutos. Eso **no refuta** la causa raíz del
2055: la fuga de julio era del **`Native Heap` del renderer** (+20-26 MB/min, pico **778 MB de
proceso**), medida **desde fuera** con `dumpsys meminfo`. `MEM#` mide lo que Unity reserva
**dentro** de la página y **no puede ver** esa magnitud. Para eso: `Tools/cast-run.sh`, que ya
muestrea `MemAvailable` cada 2 s (umbral de peligro ~10 %).

---

## 1.bis 🗃 CÓMO SE LLEGÓ (el camino, que es lo que vale)

Falta un solo dato para cerrar un bug abierto de producción. **No cuesta build ni molestar al user
si se espera a que la sesión caiga sola.**

### El bug

Cada línea de `dbg()` del receiver viaja al sender por el canal Cast. **Ese relay muere y no dejaba
rastro**: dos `try/catch` se comían el fallo y el único informe de contadores salía **por el mismo
canal roto**.

| sender | líneas que llegan | última |
|---|---|---|
| `cast-headless` **solo** | **315** | a los 400 s (`sent=312 fail=0`) |
| APK del móvil delante | **3-5** | **a los ~2 s** |

**200× de contraste.** Y ⚠⚠ **el acuario sigue montando y renderizando**: se comprobó con dos
`adb screencap` consecutivos (103.088 y 134.270 px cambiando) y midiendo que un `GRADE`/`FOG`
enviado con el relay muerto **se había aplicado exactamente**. ⇒ **El canal de IDA está sano; sólo
muere el de vuelta.**

### El instrumento, ya desplegado

`RLY <version> env:N fallos:M snd:K off:<motivo>@<s>` **en pantalla**, legible con
`adb exec-out screencap`. Decide entre cuatro causas:

| lo que se lea | causa |
|---|---|
| `env` sube · `fallos:0` · `snd:1` | se pierde **dentro del SDK**, sin lanzar |
| `fallos` sube | **excepción** en `sendCustomMessage` |
| `env` congelado · `fallos:0` · **`snd:0`** | `_logSink` sale por `if (senderCount <= 0) return;` **antes del `try`** |
| `env` congelado · `fallos:0` · `snd:1` | no es el relay: **`dbg()` dejó de llamarse** |

### El protocolo (y por qué el primer intento no decidió)

1. **Esperar a que la sesión caiga sola** y el APK se auto-reenganche → el receiver recarga el
   `index.html`.
2. La sesión vale si el log del móvil dice **`Sender CONNECTED #1`** con uptime bajo (`[~2s]`).
   Si dice `#2`, **hay un intruso: abortar y repetir**, no gastarla.
3. Buscar la línea **`ctx.start() OK — rcv html 2026-08-28c-relayhud`**. Si no aparece, el device
   sirve un html cacheado y el HUD **no existe**: su ausencia no significaría nada.
4. `adb exec-out screencap` y leer la esquina **inferior izquierda**.

⚠ El primer intento salió **inconcluso** justo por el paso 3: se parcheó el receiver **sin cambiar
ningún texto de log**, así que un html cacheado era indistinguible del nuevo. Ya está el marcador
de versión, y **sube cada vez que se toque el fichero**.

### ⚠ Lo que NO hay que concluir

- La sesión del repo móvil sostiene la versión fuerte: *«con nuestro APK el relay muere en ~2 s;
  sin él aguanta 400 s»*. **Eso aguanta.**
- **NO** sostener que la causa sea su `RemoteMediaClient.load()`: en **2 de 6** sesiones el
  `LOAD_CANCELLED` llegó **antes** que su `load()`, y el Δ del relay va de **+3 a +96 ms** (rango de
  ×32). Todo ocurre en los mismos ~2 s de arranque, donde pasan diez cosas a la vez.
  🧭 **Es perfectamente compatible con que las dos cosas sean efecto de un tercero.**
- El `off:<motivo>@<segundo>` es lo que rompe el empate: **nombra al que baja el contador**, no hay
  que inferir de proximidad temporal.

---

## 2. Lo medido hoy, y lo que tumbó

### 2.1 ✅ El bloom NO cuesta fps — el número que lo descartaba era de junio

```
bloom ON   n=12  media 40.0 fps        bloom OFF  n=9  media 37.9 fps
diferencia: +2.1 fps A FAVOR del encendido, banda ±2.7   → consistente con CERO
```

`SyncFromMobile.ps1:87` dice *«bloom OFF + Tonemapping Neutral + sat/contrast → vuelven los 7 fps»*.
⚠⚠ Eso significa **se vuelve A 7 fps**, no *cuestan 7*: verificado en `HANDOFF_2026-06-13.md:37` y
`BUILD_REPORT_2026-06-19.md:22`, **7 fps era el framerate ABSOLUTO** del device en junio, con el
`.wasm` de 44 MB, sin LTO, sin decos optimizadas y sin URP. Y **no está aislado** (se atribuye a
sustituir el `PostProcessingSetup` entero).

⚠ **Reserva:** medido a **threshold 0.92**. Que el coste no dependa del umbral es un **razonamiento**
sobre la pirámide de mips, **no una medida**. A **0.60** (el del móvil) sigue sin medirse.

**⇒ Lo más prometedor que queda para lo visual**, y necesita una línea de C#: `GRADE` no expone
`bloomThreshold` / `bloomScatter` / `highQualityFiltering`. Ampliar `GradePayload` y después se
barre en caliente sin gastar más builds.

### 2.2 ⚠⚠ La nitidez estaba del revés en el doc

Al **mismo tamaño en píxeles** (escala verificada por correlación cruzada: 1.00×):

| región | TELE | MÓVIL | móvil/tele |
|---|---|---|---|
| grava del suelo (detalle real) | 0.166 | 0.163 | **0.98× — empatan** |
| agua plana honda (nada que dibujar) | 0.0698 | 0.0136 | **0.19×** |

**La tele no se ve más borrosa: se ve más dura.** Lo de más es **grano**. Descartados con medida:
- ¿el grado? **No** — con el grado aplanado: 0.0111 → 0.0105.
- ¿la resolución? **No, va al revés** — 1024 sin comprimir predice ×0.32 (más suave).
- ¿el `renderScale`? **No** — a escala 1.0 el ruido **sube ×10.6**; el 0.75 lo estaba **tapando**.
  (Y el detalle sube ×1.39, con control de ida y vuelta perfecto: 0.1658 → 0.1658.)

Queda el **formato**: TV `DXT1` contra móvil `ASTC 6x6`. **NO probado** — haría falta generar una
versión DXT1, y eso cuesta un build.

### 2.3 🐛 El Volume de la luz mata campos del grado — en los DOS repos

`TankLightingController` monta un Volume a **priority 11** que pisa a `PostProcessingSetup` (10).

- **En TV** (`Add<ColorAdjustments>(false)`, arreglado el 21-ago): mata sólo `colorFilter` y
  `postExposure` ⇒ **el campo `exposure` del mensaje `GRADE` es inerte**. Probado en píxeles:
  `exposure 0.00` → 64.6 / 48.1 · `exposure −1.00` → 64.6 / 48.2.
- **En el móvil** (`Add<ColorAdjustments>(true)`, sin arreglar): mata **todo el ColorAdjustments**
  ⇒ su **`saturation: -15` es 0 efectivo**. La diferencia de saturación entre pantallas es
  **+18 contra 0**, no +18 contra −15.

ℹ Pisar `colorFilter`/`postExposure` es **deliberado**: el preset de luz es el dueño del tinte del
frame, para alcanzar los shaders **unlit**. El daño colateral es `saturation`/`contrast`.
⚠ «Arreglarlo» subiendo la prioridad **no es un fix**: encendería un filtro azul y +0.1 EV muertos
desde hace tiempo. **Es un cambio de aspecto y lo decide el user.**

---

## 2.bis ⚠⚠ SI HAY QUE REPETIR UNA SESIÓN DE VOLCADOS: ARRANQUE EN FRIO, NO RECONEXIÓN

Su `DumpRoutine` cuelga **en exclusiva** de ver `AQUARIUM READY`, y esa línea sólo se emite cuando
la TV **construye** el acuario. ⇒ **En cualquier reconexión a un receptor ya montado, su volcado es
inalcanzable por diseño.** El 28-ago funcionó porque se forzaron tres arranques en frío.

**Hasta que arreglen el disparador** (es su tarea, ya en su lista):

```bash
node Tools/cast-headless.js --stop --ip <IP>    # cierra el receptor -> arranque en frio
# y que el móvil reconecte: su APK se reengancha solo en ~3-5 s
```

⚠ Desconectar el móvil **no basta**: si la caída fue de menos de 30 s, el receiver **descarta el
INIT** a propósito (`index.html`, «Quick reconnect») y no reconstruye → no hay `AQUARIUM READY` → no
hay volcado. Ver [[reconexion_pierde_estado_y_logs]].

⚠⚠ **Y desde el 28-ago, con producción limpia, «mandadme una captura» ya no vale a secas**: el
sello y el HUD del relay sólo salen con `DIAG`. Hay que castear con `--diag`.
🧭 Lo bueno: `DIAG` viaja por el **canal de ida**, que es el único que no se rompió en todo el día,
así que el diagnóstico sigue siendo alcanzable justo cuando el retorno falla.

---

## 3. 🤝 Lo que le toca al repo MÓVIL (se lo plantean ellos)

1. **Compilar el tope de reconexión** de `768964a` — previene un livelock documentado ayer, y de
   paso deja de recargar nuestro receiver en bucle. ⚠ Es **prevención**, no la explicación de nada
   de hoy: las sesiones montaron bien.
2. **Dejar FUERA la versión B del `silence.wav`** y quitar las **dos líneas**
   (`CastPlugin.java:249` y `:294`). Su B es **nuestro keepalive copiado** con el vídeo cambiado por
   el audio que junio ya descartó, compitiendo por la misma sesión de media.
   🧭 El experimento limpio es **quitar el keepalive del emisor**, no cambiarlo de forma. Y da dato
   haga lo que haga. Es el paquete de **QA**: la app real del user no se toca.
3. **Corregir su `CAST_CONTRACT.md` §11.2**, que da por establecido que «el receptor no sobrevive a
   su propio arranque». **Es falso y se midió hoy** — corregido desde este lado en
   `CAST_CONTRACT_TV.md` §5.5.
4. **Poner el aviso recíproco encima de su `Add<ColorAdjustments>(true)`**: la tele está calibrada
   contra el resultado de ese bug. 🧭 *El aviso va donde está la causa, no donde está el efecto* —
   el nuestro sólo avisa a quien no rompe nada.

**Tres versiones del `silence.wav` en juego** — y el APK instalado hoy es la **A**:

| | qué es | estado |
|---|---|---|
| **A** `load()` de un tiro | audio que termina a los segundos | **medido y fallado en junio** · es lo que corre |
| **B** `queueLoad` REPEAT_SINGLE | audio en bucle | commiteado ayer, **nunca compilado** |
| **C** `keepalive_black.mp4` (TV) | **vídeo real** | la cura que su análisis de junio predijo |

---

## 4. 🚩 El patrón del día: el artefacto y la fuente divergen, y nada avisa

Cinco veces en dos días:

1. El player construido **sin el `dump`** (cazado por el mtime del `.cs` contra el del `.wasm`).
2. El `loader.js` viejo servido por R2 en un player mezclado, con `EXIT=0`.
3. **El APK del móvil un commit por detrás** — cazado por una **cadena de texto**: el código dice
   `"Silence media EN BUCLE"`, el aparato loguea `"Silence media loaded"`.
4. El `silence.wav` en tres versiones.
5. **Nuestro `index.html`**: se parcheó sin cambiar ningún log, así que un html cacheado era
   indistinguible del nuevo. **Costó la lectura del HUD de hoy.**

⭐ **EL ANTÍDOTO, y es barato: que un `Log` cambie de texto cuando cambia la lógica.** Convierte
cualquier registro en prueba de versión sin instrumentar nada. Ya aplicado al receiver
(`rcv html <version>`). **Falta en el emisor**: la sugerencia es que `AppVersion` lleve el hash
corto del commit y lo imprima al arrancar.

### 4.1 ⚠⚠ Y su primo: parar la tarea NO para lo que la tarea lanzó

Un supervisor en segundo plano relanzaba `cast-headless` cada ~8 min. `TaskStop` paró la **tarea**,
no el **árbol de procesos**: quedaron **dos `sup.sh` y tres `vigia.sh`** vivos, y contaminaron la
sesión de la otra pantalla como `Sender CONNECTED #2`.

⚠ Pasó **dos veces el mismo día**, la segunda con la regla ya escrita en memoria: la primera vez se
contó el proceso lanzado y no al que lanzaba.
⚠ Y el comando de limpieza llevaba los patrones **en la línea de comandos**, así que **se
autodetectó y se mató a sí mismo** (exit 255). Vive ahora en un `.ps1` en el scratchpad
(`limpia.ps1`, con `matar` mata y recuenta).

🧭 **En fases de medición ajena, NADA en segundo plano.** Un vigía que relanza es un intruso
esperando turno. Capturar a mano con `adb exec-out screencap`, que no abre sesión de Cast.

---

## 5. 🧭 Reglas de método que salieron hoy

- **Para aislar un parámetro, comparar contra la variante que SÓLO difiere en él.** Dos conclusiones
  cayeron por línea base equivocada: el `exposure` «más claro» (era **igual**) y los «−26 L\*» del
  suelo (la banda del 25 % promediaba la niebla del suelo lejano; el hueco real era **7.4**).
- **Una media sólo vale si la región es homogénea.** El suelo tiene degradado de niebla
  front-to-back.
- **Ausencia de líneas en un log NO es ausencia de eventos.** Separar «no pasó» de «no me llegó»;
  el desempate barato aquí es **mirar la pantalla**.
- **Un barrido necesita variante de control**, y si mide nitidez, **región sin detalle**.
- **`grep -c` devuelve 1 cuando no hay coincidencias** — y «no hay coincidencias» era el resultado
  bueno. Mordió a las dos sesiones el mismo día. 🧭 *El caso de éxito y el código de error
  coincidiendo es una trampa preciosa.*
- **El evento también puede llegar tarde**: el stdout de node va con **buffer de bloque** al
  redirigir a fichero, así que un evento en los últimos ~30 s no se vuelca a tiempo. Costó una
  medida, y el síntoma fue **una captura del menú de Google TV** en vez de un error.
- **Un instrumento tiene que poder denunciar su propia avería.** El HUD llevaba un `catch {}` mudo
  (un tragadero **dentro del parche que quita tragaderos**) y además se ocultaba salvo con `DIAG`
  — o sea, **invisible justo en la avería para la que existía**. Los dos reflejos —«que no reviente»
  y «que no moleste»— nacen de proteger al usuario de la información.
- **Escribir la regla no la ejecuta.**

### ⭐ Y la conclusión del día, que es lo más transferible

**Cuatro conclusiones falsas entre las dos sesiones**, todas con la misma forma —*datos correctos,
hipótesis razonable, mal emparejadas*— y las cuatro cazadas igual:

| lo que se creía | lo que faltaba mirar |
|---|---|
| «el `exposure` sale más claro» | la variante que sólo difería en **él** |
| «el receptor no monta» | **la pantalla**, no el log |
| «el `bloomHQ` aporta +1.2 L\*» | **varios bloques** por estado, no una captura |
| «el giro no se aplica» | **la serie entera**, no dos fotos |

**Ninguna se cazó pensando más. Se cazaron mirando más.** Y en las cuatro **la ventana que faltaba
estaba disponible y era barata**: no hacía falta un instrumento nuevo, se miraba por el que ya
estaba abierto.

⇒ 🧭 **Cuando una hipótesis y unos datos no encajan, lo primero no es pensar mejor: es preguntarse
qué ventana no se está mirando.** Las dos sesiones incumplimos hoy reglas que teníamos
  apuntadas.

---

## 6. Estado

| | |
|---|---|
| Rama | **`feat/ciclo-dia-noche`** — `main` sin tocar, nada pusheado |
| Player desplegado | **`rcv 2026-08-28 visual`** — 5 ficheros verificados por md5 bajados enteros |
| `index.html` desplegado | con el **arreglo del relay** y la **splash hasta `AQUARIUM READY`** |
| Sin desplegar | **nada** |
| Verificado hoy en device | HUD del relay · ajuste visual · coste del bloom · `renderScale` · techo del relay |
| Bundles | sin tocar |

**Modificado y sin commitear:** `TvScene.unity`, `TvSceneBootstrap.cs`, el `index.html` del template,
`CAST_CONTRACT_TV.md`, `CAST_PARIDAD_VISUAL.md`, `CLAUDE.md`, `.gitignore`.

⚠ **`.gitignore`**: mis carpetas de capturas sumaban **123 MB sin ignorar** en un repo que ya se
comió 115 MB de PNG una vez. Añadidas con el porqué escrito. **Comprobar que las nuevas se añaden**
— no fiarse de acordarse al hacer `git add`.

---

## 7. Pendientes

- [x] ✅ **El relay** (§1) — resuelto, desplegado y validado. Y con él, la §6.5 del móvil.
- [x] ✅ **El build de player** — hecho en **6:24**, desplegado y verificado por md5. Lleva la
      cultura invariante, el ajuste visual y las palancas del bloom en `GRADE`.
- [x] ✅ **El bloom: barrido, decidido y DESPLEGADO.** A umbral **0.92 era invisible** (dentro de
      la dispersión de las referencias apagadas) — de ahí el «no aporta nada» de agosto. A **0.60**
      la escena sube **+8 L\*** sin coste medible. Elegido por el user viendo las cuatro variantes:
      *«más brillo y vida que azul profundo»*. Va en `TvScene.unity`, no en el `.cs`.
- [x] ❌ **`bloomHQ`: descartado con medida.** −0.1 L\* en el agua, +0.1 en la escena — **nada
      visible** — y hasta **−2.5 fps**. ⚠ Corrige una estimación previa de «+1.2 L\*» que salía de
      comparar **dos capturas sueltas**: la dispersión entre bloques del mismo estado ya es ±0,5 L\*.
      🧭 **Una diferencia menor que la dispersión del propio estado no es una diferencia.**
- [x] ✅ **Frases rotativas en la pantalla de carga** (pedido del user): 53 fijas + personalizadas
      con los **motes** que el móvil ya manda y que no se pintaban en ningún sitio. Cuotas por tipo
      (34 % personal), cola sin repetición, 7,5 s, cursiva. `Tools/test-frases.js` 18/18.
- [ ] 🌐 **El campo `lang`** en `TvAquariumState` — pedido al repo móvil, aditivo, un campo y una
      asignación. Sin él, castellano. 🧭 Las frases viven en el `index.html` a propósito: cambiarlas
      es un deploy de minutos en vez de un build de Android.
- [ ] 🎨 **Remedir si el repo móvil arregla su `Add<ColorAdjustments>`** — el ajuste de §0 se
      descuadra solo.
- [ ] 🖼 **El grano: DXT1 contra ASTC**, sin probar. ⚠ La palanca del §3.1 de
      `CAST_PARIDAD_VISUAL.md` apunta a **resolución** y podría ser la equivocada.
- [ ] 🔐 **Desplegar el Worker de la Fase 2** — escrito, 42/42 en local, **sin desplegar**. Faltan
      `JWT_SECRET` y `MINT_TOKENS`, que sólo pone el user.
- [x] ✅ **Los volcados y su §6.5** — **CERRADOS** el mismo día, en cuanto se curó el relay:
      6 uid byte a byte, pareja cableada en los dos lados, `9.33 × 0.8 = 7.47` exacto y punto
      decimal. Con el APK real y un acuario real, no con scripts.
- [ ] 🎨 **Editar una deco colocada** no manda UPDATE (pide el móvil).
- [ ] 📊 **La serie del 2055**: limpios **720 → 356 → 287 s**. ⚠ Tres puntos no son tendencia, y
      **caduca entera** si se compilan las dos líneas del `silence.wav`. No diseñar tandas para
      engordarla.
- [ ] ❌ Decimar mallas: **descartado por el user**, no volver a proponerlo.
