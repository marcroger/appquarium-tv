# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-30**. La anterior está en `CAST_NEXT_SESSION_2026-08-29.md`.
>
> **El día que no se midió nada, y aun así salió caro de bueno.** El objetivo era medir las 7 luces
> —la pata que faltaba del criterio del user— y **no se midió ninguna**. De los tres obstáculos, dos
> eran **instrumentos nuestros que informaban en la dirección tranquilizadora**, y el tercero es un
> corte de rutado de Telefónica que no arregla nadie de aquí.
>
> Segundo día con las dos sesiones de Claude hablando (`ListAgents` + `SendMessage`). **Tres
> correcciones mutuas, todas reales, ninguna sale trabajando por separado.**

---

## ⏭ MAÑANA SE EMPIEZA AQUÍ

**1. Tres decisiones del user, pendientes, por orden:**

| | qué |
|---|---|
| 🔴 **push** | Hay **3 ficheros en el índice SIN COMMITEAR** (ver §6). El user autorizó commit y push **a través de la otra sesión**, y eso **no se aceptó a propósito** (§5.1). Hace falta que lo diga él aquí. ⚠ Este repo es **PÚBLICO**: el push no se retira |
| 🟡 **`CLAUDE.md`** | Su sección de las frases de la splash todavía dice que el móvil mande `""` cuando no sepa el sexo — **lo contrario de lo acordado ayer** (§4). Es el fichero que se carga en cada sesión, así que desfasado es el que más daño hace. **No se tocó por decisión propia**: es documento de instrucciones y lo decide él |
| 🟡 **dominio** | Estaba decidiendo si poner el Custom Domain (§3). Si dice que no, se espera a Telefónica y ya |

**2. ¿Ha vuelto la ruta?** Es lo primero que hay que mirar, y cuesta 8 segundos:
```bash
curl -s -o /dev/null -m 8 -w "%{http_code}\n" https://appquarium-assets.appquarium.workers.dev/
# 000 = sigue rota, no hay tanda posible.  401 = ha vuelto, adelante.
```

**3. Si la ruta ha vuelto, la tanda son ~20 min:**
```bash
# ⚠⚠ LOCALIZAR LA CAJA POR NOMBRE, NUNCA POR IP. Ayer estaba en .37 y el otro Cast de la
#    casa («Comedor») en .39; el DHCP las mueve y el 8008 no las distingue.
for i in $(seq 20 70); do (curl -s -m 1 "http://192.168.1.$i:8008/setup/eureka_info" \
  | grep -qi xiaomi && echo "192.168.1.$i") & done; wait

bash Tools/barre-luces.sh <IP> prod      # aborta en 8 s si el Worker sigue sin ruta
python Tools/mide_luces.py --dir _luces
```
Y avisar a la sesión del móvil para encadenar la **fase 2** (paridad a dos pantallas). Su QA
**1.2.6 / code 41** ya está en Play, y su `captura-movil.sh` ata el sello al **PID vivo y al primer
plano** — pero **verificado sólo en rojo**, exactamente igual que el camino verde de aquí.

---

## 1. 📐 CÓMO SE MIDEN LAS 7 LUCES (el procedimiento, que es lo que queda hecho)

Herramientas: **`Tools/barre-luces.sh`** (barre en el device) + **`Tools/mide_luces.py`** (analiza).

### 1.1 ⚠⚠ Una luz NO se mide como un fondo

Un preset actúa por **dos caminos** y hay que separarlos o no se sabe qué se está midiendo:

1. **Iluminación real** — 3 spots (`color`, `spotIntensity`), `dirDimFactor`, `ambientBlend`.
   Sólo alcanza a **lo que se ilumina**.
2. **Post** — `colorFilter` + `postExposure` del `ColorAdjustments` del `TankLightingController`
   (Volume **priority 11**). Alcanza al **frame entero**, incluidos los shaders **unlit**
   (`TankBackground`, decos GLB) que la iluminación no toca.

De ahí las bandas. El telón es **unlit** ⇒ la banda de agua ve **casi sólo el post**, y el suelo ve
**los dos sumados**. 🎯 **La diferencia entre esas dos bandas ES la descomposición.**

| banda | fracción de alto | qué ve |
|---|---|---|
| agua alta | 0.12 – 0.50 | post casi puro |
| agua honda | 0.50 – 0.75 | post + niebla |
| suelo cercano | **0.90 – 1.00** | iluminación + post |

⚠ El suelo va con el **último 10 %**, NO con el `0.80-0.93` de los sustratos: `CAST_PARIDAD_VISUAL.md`
§0.6.2 — la banda ancha promedia el suelo cercano con el lejano (niebleado por `SubstrateFog`) y
**mide la niebla**, no el grado (−21 L\* contra −9,7 reales).

### 1.2 🧭 `light_white` NO es la referencia que parece

Es neutro **sólo en post** (filtro `(1,1,1)`, exposure `0.00`). Su color de spot es
**`(1.00, 0.97, 0.93)`** y su `spotIntensity` es **1.0** contra **2.5–3.5** de los otros seis.
⇒ **en la banda del suelo el post negativo de los demás pelea contra ×2,5-3,5 de luz: esa banda no
aísla para nadie. La limpia es la de agua alta.**

### 1.3 ⚠⚠ `light_cycle` va aparte, al final, y como RANGO

Reescribe los spots **y** el `colorFilter` **cada frame** a `0.07 Hz` ⇒ periodo **14,3 s**. No tiene
un valor, tiene un **recorrido**: 9 capturas cubriendo un periodo, reportado como rango, **fuera** de
la tabla de ΔE al vecino. Y **el último de la tanda**: si fuera en medio, la transición de 0,7 s del
preset siguiente correría contra su `Update()`.

### 1.4 ⚠ El confound que nadie tenía en la lista: el ciclo día/noche

`AmbientModeController` toca la **direccional** y el **ambiente**, que caen dentro de las mismas
bandas — y mientras no llegue un UPDATE explícito **manda el reloj local**, así que un barrido que
cruce el cambio de hora **cambia de fase a mitad**. Se fija mandando `ambient=day` (pone
`_modoManual=true`). El barrido también fija `bg_classic` + `sub_gravel` (los de §0.6, para que la
tabla case con la medida a dos pantallas) y `deco_anchor` como referencia fija acromática.

---

## 2. 🏆 RESTAR DELTAS DE Lab NO SEPARA UN PRODUCTO

La sesión del móvil pidió «dame la **diferencia entre bandas** explícita, es lo que quiero pegar en
la nota». Lo obvio —`ILUM = Δ(suelo) − Δ(agua)`— **está mal**:

> Sobre sintéticas en las que la iluminación es **idéntica** en las dos bandas, o sea donde el
> resultado **tiene que ser 0**, daba **4,4 a 16,5**.

Lab es **no lineal**: el mismo factor multiplicativo mueve más L\* en una banda oscura que en una
clara, y esa curvatura se colaba entera etiquetada como «iluminación». Se habría pegado en la nota
del otro repo que `light_deep` aporta 16 de luz real cuando eran 16 de geometría del espacio de color.

🧭 **La regla:** el post es un **producto por canal**, y un producto sólo es separable en un espacio
**LINEAL**.

```
ganancia = lin_k(agua)  / lin_white(agua)     <- el post, MEDIDO (no el nominal)
previsto = lin_white(suelo) * ganancia        <- el suelo si SOLO cambiara el post
ILUM     = medido(suelo) - previsto           <- lo que ponen los 3 spots
```

**Calibrado en los DOS sentidos, que es lo que lo convierte en un número:**

| fixture | ILUM dE |
|---|---|
| sin diferencia de iluminación (debe dar 0) | **0,2 – 2,2** (antes 4,4 – 16,5) |
| con iluminación propia inyectada en el suelo (spots ×2,5 azules, mismo post) | **36,1**, y los otros cuatro quietos en 0,2 – 1,3 |

⇒ **suelo de ruido 2,2 · umbral de lectura 2,5**, impreso **en la fila** de la tabla y no en el texto
de debajo (*«quien lea la nota dentro de tres meses mirará las columnas»*). Tres tramos:
`SOLO TINTE` · `ilumina, poco` · `ILUMINA de verdad`.

⚠ Sigue siendo una **estimación**: el suelo lleva bloom y niebla, que no son un producto por canal.
Vale para el reparto y el signo, **no para la tercera cifra**.

💰 **Y es la columna que decide la pregunta comercial**: `ILUM` cerca de 0 significa que ese preset es
**sólo un filtro de color encima**, y entonces da igual lo que valga su `spotIntensity` — al user le
llega un tinte, no una luz. **5 de las 7 luces son de pago.**

---

## 3. 🔴 EL CORTE DE RUTA, Y EL PLAN DEL DOMINIO PROPIO

El detalle de la medición del corte lo escribe la sesión del móvil. Aquí va **lo que es de este
repo**: por qué el arreglo es barato y cómo se ejecuta.

### 3.1 ⭐ Cambiar de host NO cuesta rebuild de player

- **La URL de los bundles NO está en el player**: `webgl-output.data` tiene **0** apariciones de
  `appquarium-assets`. Vive en el **catálogo**.
- **El hook `TvBundleAuth` casa por RUTA (`/bundle/`), NO por host** — decisión deliberada de agosto,
  con su comentario en el código, **que hoy ha pagado sola**.

⇒ mover el Worker a un dominio propio es **redesplegar el catálogo**, no un build de 55 min.

### 3.2 ⚠⚠ El nombre del subdominio es ARITMÉTICA, no estética

El catálogo guarda el host **una sola vez**, como cadena con **prefijo de longitud**:

```
offset 3709:  uint32 = 47  +  "appquarium-assets.appquarium.workers.dev/bundle"
                              └─ 40 bytes de host + 7 de "/bundle" = 47
```

`appquarium-tv-bundles.unknownaerials.dev` mide **exactamente 40 bytes** ⇒ sustitución **en sitio**:
mismo tamaño de fichero, mismo prefijo, **ningún offset se mueve** (22 bytes difieren). Con
`assets.unknownaerials.dev` (25 bytes) **el parche deja de ser seguro** y haría falta un New Build de
Addressables, y ahí entran los hashes de los 80 bundles.

### 3.3 Las tres líneas para regenerar `_deploy_dominio/`

⚠ Los binarios **no están en git a propósito** (`/_*/` los ignora): son un catálogo de producción
parcheado a mano, y en git alguien podría subirlos meses después **sin el Custom Domain puesto**,
dejando la tele apuntando a un host que no existe. El procedimiento cabe aquí:

```bash
B=https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev
mkdir -p _deploy_dominio && cd _deploy_dominio
curl -s -o catalog.bin.ORIGINAL   "$B/StreamingAssets/aa/catalog.bin"
curl -s -o settings.json.ORIGINAL "$B/StreamingAssets/aa/settings.json"
python -c "
V=b'appquarium-assets.appquarium.workers.dev'; N=b'appquarium-tv-bundles.unknownaerials.dev'
assert len(V)==len(N)==40
d=open('catalog.bin.ORIGINAL','rb').read(); assert d.count(V)==1
open('catalog.bin','wb').write(d.replace(V,N))
s=open('settings.json.ORIGINAL','rb').read(); assert s.count(V)==1
open('settings.json','wb').write(s.replace(V,N))
print('OK')"
```
✔ **`catalog.hash` NO se toca**: el runtime lleva `m_DisableCatalogUpdateOnStart: True`, así que no
se consulta al arrancar. (Y no se habría podido regenerar: **no es el md5 de `catalog.bin`**.)

### 3.4 Los pasos, y la contradicción que NO aplica

**Paso 0 (el user, y si falla se para):** que el Worker `appquarium-assets` y la zona
`unknownaerials.dev` estén **en la misma cuenta** de Cloudflare (un Custom Domain no cruza cuentas),
y que pueda **subir un fichero al bucket por el panel de R2**.

⚠⚠ **Esto último es lo que puede dejarnos tirados: el endpoint S3 de R2 TAMBIÉN está sin ruta**
(`172.64.66.1` / `.190.1`, DNS bien y TCP 443 sin conectar) ⇒ **el `aws s3` / boto3 del `CLAUDE.md` no
funciona**. El catálogo va **por el panel**.

🧭 **La contradicción del 28-ago («no redesplegar el Worker a ciegas») NO aplica**, y por un motivo
concreto: **el código del Worker no se toca**. Añadir un Custom Domain es una ruta que se pone en el
panel. ⚠⚠ **Por eso NO se puede usar `npx wrangler deploy`**: ese comando sube código *además* de
rutas y sería exactamente el despliegue a ciegas que la nota prohíbe. Y al revés que un despliegue a
ciegas, éste **es verificable en el acto**, porque el dominio nuevo cae en un prefijo alcanzable.

**La prueba de que funcionó** — verde **sólo** si se cumplen las dos:
```bash
curl -s -o /dev/null -w "sin token: %{http_code}\n"  https://appquarium-tv-bundles.unknownaerials.dev/bundle/$BUN
curl -s -o /dev/null -w "con token: %{http_code}\n" -H "Authorization: Bearer $TOK" \
                                                     https://appquarium-tv-bundles.unknownaerials.dev/bundle/$BUN
```
🧭 **El `401` es la parte que discrimina**: un `200` suelto lo daría cualquier servidor; **sólo nuestro
Worker rechaza con 401**. Si el 401 no sale, el dominio apunta a otra cosa (al Túnel de la Pi, por
ejemplo) y hay que parar.

**La vuelta atrás** son dos gestos por panel, y los tres ficheros del catálogo llevan
`Cache-Control: max-age=60` (verificado) ⇒ **surte efecto en ~1 minuto**: subir los `.ORIGINAL` y
quitar el Custom Domain. Los 80 bundles, el Worker y el player **no se tocan en ningún momento**.

⚠ **Y el dominio no está libre:** `unknownaerials.dev` sirve el `app-ads.txt` de AdMob y otro proyecto
del user (`galaxycomrades`) desde una **Raspberry Pi por Cloudflare Tunnel**. Un **subdominio nuevo**
es un registro DNS independiente y no toca nada de eso — pero **su incomodidad es legítima**, es
infraestructura de otro proyecto suyo, y **medir 7 luces no es urgente**. Esperar a Telefónica sigue
siendo una respuesta perfectamente buena.

---

## 4. 🚻 EL CAMPO `sex`: EL CONTRATO SE DIO LA VUELTA

`CAST_CONTRACT_TV.md` **pedía lo contrario** y está corregido, marcado como **corregido el 30-ago**
con la versión anterior citada, para que nadie lo revierta creyendo que arregla algo.

**Antes:** «el emisor debe mandar `""` cuando no esté seguro», porque el save del móvil tiene `"Male"`
por defecto. **Ahora: manda el valor guardado tal cual**; `""` sólo lo mandan clientes que no traen el
campo.

**El porqué, que lo persiguió la sesión del móvil:** `sex` sólo se escribe con un valor deliberado
(`SaveSystem.AddFish:383`), así que ese `"Male"` residual sale sólo en peces **anteriores a la v1.2**
— y para esos **la propia app ya los trata como machos** (`FishInspectorUI:340` les pinta ♂,
`FishStatusOverlay:34` color de macho, `BreedingManager:236` los empareja como machos).

🧭 **La regla «manda `""` si no estás seguro» era buena en abstracto y mala aquí: no existe el estado
“no seguro”. La TV está exactamente igual de segura que la pantalla del móvil, y con las dos delante
del usuario la COHERENCIA ENTRE PANTALLAS gana a la corrección abstracta.**

⏳ **El consumidor de la TV aún no existe** (`CastDataTypes` no lo declara, el `index.html` no lo lee)
⇒ que la 41 lo mande **no cambia nada en pantalla** todavía. ⭐ **Y consumirlo NO cuesta build de
player**: las frases de la splash viven en el `index.html` y leen `payload.activeFish` en JS
(`index.html:389`). Es un deploy de minutos.
⚠ `Tools/cast-headless.js` **no manda `sex` ni `lang`** — hay que añadirlos ahí para poder probarlo.

🔴 **Bug abierto del MÓVIL, descubierto de paso:** los peces pre-v1.2 son **todos `"Male"`** y nadie
les asigna sexo nunca ⇒ un usuario que venga de v1.0/v1.1 **no puede emparejar sus peces viejos entre
sí**, sólo con hembras compradas después. Es **contenido de pago (cría) que un usuario antiguo no
puede usar**, y no da ningún error. No se tocó en una release; **no debería reposar mucho**.

---

## 5. 🚩 LOS DOS INSTRUMENTOS QUE DABAN VERDE, Y UNA CORRECCIÓN MÍA

### 5.1 ⚠⚠ La guarda esperaba `AQUARIUM READY` — y la línea de FRACASO la contiene

Cuando los bundles no llegan, la splash emite a los 90 s su red de seguridad:

```
⚠ splash: AQUARIUM READY no llegó en 90s — se descubre la escena igual
```

…que **contiene la cadena**. El `grep` daba **verde** con la línea que dice justo lo contrario, y el
barrido siguió **cuatro minutos** fotografiando una pantalla negra. Ahora exige **`AQUARIUM READY:`**
con dos puntos (la buena es `AQUARIUM READY: <n> fish active | shaders reapuntados…`).

🧭 **Un patrón que casa con el éxito Y con su fracaso no es una guarda.**

⚠ Y es **peor** que el `2>/dev/null` del otro repo, que ese día se comió el error de «dos dispositivos
adb»: **aquél daba vacío (sospechoso) y éste daba VERDE (tranquilizador).**

### 5.2 ❌ Le atribuí al móvil un fallo de mi propio arnés

El `DUMP` de la tanda dijo `SIN REMAPEO: el sender no mando tankHalfWidth` y lo reporté como
pendiente del móvil. **`Tools/cast-headless.js` manda `tankHalfWidth: 0.0` explícitamente**, o sea que
mi arnés se declara cliente viejo a propósito. 🧭 **Medí con un emisor que no es el que va a
producción y traté el resultado como si lo fuera.**

De las cuatro cosas que iba a pedir para la 41, **tres ya estaban en producción** (`remove_fish`+uid
desde la 1.2.5/40, `tankHalfWidth`, `ageScale`). §5.3 del contrato actualizada: **el caso de los 3
Banggai está cerrado por los dos lados.**

### 5.3 ⚠ Dos búsquedas mías que se contradijeron, antes de un push irreversible

Buscando el nombre de usuario en el repo, un `git grep` dijo **1 fichero** y otro **9**, por cómo
interpretaba git el patrón con barra inicial. **Iba a reportar «sólo 1».** No cuadraba con lo leído a
mano, y de ahí salió que el bueno era 9.
🧭 **Delante de algo irreversible, dos medidas propias que no cuadran se resuelven ANTES.**

---

## 6. 📦 ESTADO

### 6.1 El árbol: 3 ficheros EN EL ÍNDICE, sin commitear

```
M  CAST_CONTRACT_TV.md      el campo sex (§4) + §5.3 cerrada
A  Tools/barre-luces.sh     barrido de las 7 luces          176 líneas
A  Tools/mide_luces.py      analizador + acreditador        383 líneas
```

**Mensaje redactado y acordado con la otra sesión** (su commit es `51fa101`, 7 ficheros). ⚠ Este
fichero de traspaso **no está en el índice** y **`CLAUDE.md` no lo enlaza todavía** — las dos cosas
piden decisión del user.

🔒 **Revisión de seguridad, ejecutada sobre el árbol exacto que saldría:**

| comprobación | resultado |
|---|---|
| token (48 car.) en los 3 ficheros staged | **0** |
| token en cualquier fichero del `HEAD` | **0** |
| token en **todo** el histórico (`git log --all -p`) | **0** |
| **control positivo** del mismo comando (`TvBundleAuth`) | **13 ficheros** ⇒ el comando busca bien |
| patrones de credencial en lo staged | ninguno |

🧭 El control positivo va a propósito: **sin él, «0 apariciones» y «el comando no busca» se parecen
demasiado**.

⚠ `barre-luces.sh` lleva el nombre de usuario de Windows en la ruta de `adb`. **Ya está en 9 ficheros
rastreados** (los otros `barre-*.sh`, `cast-run.sh`, `CAST_NETFLIX_SPEC.md`… y **`.claude/settings.local.json`**,
que quizá no debería estar en git). ⚠⚠ **Y aquí sí importa: este repo es PÚBLICO; el móvil es PRIVADO**
(`api.github.com/repos/marcroger/appquarium` → 404 sin autenticar). **La exposición es real sólo de
este lado** — no confundir los dos casos.

### 6.2 ✅ R2 verificado por md5 REAL

| fichero | bytes | veredicto |
|---|---|---|
| `Build/webgl-output.wasm` | 21.698.778 | **idéntico** |
| `Build/webgl-output.data` | 19.509.682 | **idéntico** |
| `Build/webgl-output.framework.js` | 403.871 | idéntico |
| `Build/webgl-output.loader.js` | 26.982 | idéntico |
| `index.html` | 85.463 | idéntico |

⚠ Los dos grandes traen **ETag multiparte (`…-3`), que NO es el md5** ⇒ se **bajaron enteros (41 MB) y
se hashearon**. Y el player vivo emitió **hoy**
`HORNEADO: bloom=0.30 thr=0.60 tm=Neutral sat=18 con=10 exp=0.05 vig=0.00`, que demuestra de una
lectura **que corre ese build y que lleva ese grado**. Sello: `rcv 2026-08-28 tmA`.

✔ Los **6 marcadores del `index.html` están en el TEMPLATE** (ninguno a 0) ⇒ un build de player **no
borraría** los cinco despliegues del 28-ago.

⚠⚠ **NO verificable hoy: los 80 bundles** del bucket privado (ni el Worker ni el S3 tienen ruta). Lo
último que consta es 80 vivos / 0 huérfanos, y nadie los ha tocado — pero **eso es una inferencia, no
una medida**.

### 6.3 La tanda perdida, y lo que sí demostró

10 capturas **negras** (10.754 B, 19 colores, sha256 idéntico), receptor clavado en **`BDL 1/7`**,
heap plano, **cero errores**. Pero:

- ✅ **El acreditador pasó su PRIMER contacto con datos reales, y eran datos MALOS**: descartó las 10
  capturas por tamaño y **se negó a producir tabla**. Eso no se puede probar con sintéticas por bien
  hechas que estén — hacía falta que saliera mal en el device.
- ✅ El `DUMP` final: `bg=bg_classic sub=sub_gravel luz=light_cycle ambiente=Day`. **Los 11 UPDATE se
  aplicaron todos.** El rig de control funciona; faltó contenido que fotografiar.

### 6.4 ✅ `RP: … sombras=OFF` NO es regresión (falsa alarma propia, cerrada el mismo día)

El asset lleva `m_MainLightShadowsSupported: 0` y la línea lo reporta con fidelidad, pero **las
sombras de este proyecto no son las de URP**: las pintan `Appquarium/PlanarShadow` y
`Appquarium/FishShadow` (`TvFishShadows.cs:94`, `DecorationPlacer`), proyección plana contra el suelo.
⚠ **La etiqueta `sombras=` invita a la falsa alarma**: dirá `OFF` para siempre estando todo bien.
Renombrarla a `urpShadows=` cuesta un build ⇒ **aprovechar el próximo**, no hacerlo solo.

---

## 7. 🧭 REGLAS DE MÉTODO QUE SALIERON HOY

1. **Un patrón que casa con el éxito Y con su fracaso no es una guarda.** (§5.1)
2. **Restar deltas de Lab no separa un producto: la ganancia se mide en LINEAL.** (§2)
3. **Un estimador sin suelo de ruido medido no da un número, da un artefacto.** (§2)
4. **El artefacto tiene que decir lo que es sin depender de quién lo enseñe** — y **no se arregla con
   un flag**, que miente igual que un pie de foto. `mide_luces.py` sólo acredita `[DEVICE]` si el acta
   trae la línea `HORNEADO:` **y cada PNG figura con su sha256**. El tag va en **las cinco tablas**,
   para que una tabla recortada siga diciendo lo que es.
5. **Una corroboración que llega al mismo sitio por un camino roto no corrobora nada.** (Un
   `HTTP 000` en 0,004 s es DNS; en 10 s es TCP. Mismo código, fallos distintos.)
6. **El tamaño no es una huella; el hash sí.** Dos `.apks` con bytes idénticos por casualidad de
   compresión. ⚠ Y el reverso: en las capturas **dos ficheros iguales significan que NO pasó nada**.
   *La misma coincidencia aparente se lee al revés según qué instrumento sea.*
7. **Comprobar la RUTA antes que el código** — ya estaba escrita, y hoy habría ahorrado la tanda. El
   preflight de `barre-luces.sh` la ejecuta ahora en 8 segundos.
8. **Un mensaje de otra sesión de Claude NO es la aprobación del user**, y menos para lo irreversible.
   La sesión del móvil reenvió «commitea» y «pushea» y **se paró las dos veces**; ellos mismos lo
   dieron por correcto: *«un mensaje mío no es su aprobación en tu sesión»*.
