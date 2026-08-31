# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-31**. La anterior está en `CAST_NEXT_SESSION_2026-08-31.md`.
>
> **El día que se cerraron las tres cosas que llevaban semanas abiertas** — la ruta a R2, el campo
> `sex` y las 7 luces — **y en el que las dos falsas alarmas fueron mías, no del proyecto.**
>
> Tercer día con las dos sesiones de Claude hablando. Y por primera vez el trabajo fue **encadenado
> de verdad**: ellos montaron la escena en el móvil, midieron a la vez que yo, y **cada uno tumbó una
> conclusión del otro con datos**.

---

## ⏭ MAÑANA SE EMPIEZA AQUÍ

**No queda nada bloqueante.** Lo que hay es una decisión del user y dos cabos menores.

| | qué | de quién |
|---|---|---|
| 🟡 **dominio** | Custom Domain para el Worker. **Ya NO es urgente** (la ruta volvió sola) pero sigue siendo el seguro contra el próximo corte de Telefónica. El plan entero está en `CAST_NEXT_SESSION_2026-08-31.md` §3 y **no cuesta build de player** | **user** |
| 🟡 **`TankData.cs:19`** | Tooltip caducado en fichero **compartido**: dice «Ocean/Large=5.0 \| Starter=4.0 \| Micro=3.65» y **ningún tanque real responde a eso** salvo el ocean (`tank_l` **4.2** · `tank_m` 3.5 · `tank_nano` 2.5). Y el defecto del campo es `5f` ⇒ un `TankData` nuevo nace con el encuadre del ocean sin que nadie lo elija. Tocarlo obliga a re-sync con el móvil | **user** |
| 🟢 **precio de las luces** | `light_warm` **ilumina 13× más** que `light_purple` **y es el gratuito**. No es un bug ni un desajuste de publicidad (medido: la tienda no las vende como iluminación). Es una decisión de producto | **user** |
| 🔴 **peces pre-2026-03-09** | Bug **del MÓVIL**, sin arreglar: son todos `"Male"` y nadie les asigna sexo ⇒ **no pueden emparejarse entre sí**. Contenido de pago que un usuario antiguo no puede usar, sin ningún error | **repo móvil** |

⚠ **Producción NO está parada.** El handoff anterior decía «⏭ lo primero mañana: el casteo con la app
REAL … producción PARADA». Hoy se ha casteado con la APK real (`com.appquarium.qa` 1.2.5/40) sobre la
tele y **el acuario monta**. Eso queda cerrado.

---

## 1. ✅ LO QUE SE CERRÓ HOY

### 1.1 La ruta a R2 volvió sola — y los 80 bundles pasan de inferencia a medida

| prueba | 30-ago | 31-ago |
|---|---|---|
| Worker `/bundle/<x>` **sin** token | `000` | **`401`** ← el que discrimina |
| R2 público | 200 | 200 |
| endpoint S3 (boto3, perfil `r2assets`) | TCP sin conectar | **listado OK** |
| **escritura** en el bucket privado | imposible | **PUT + HEAD + DELETE OK** |
| los 80 bundles | *«inferencia, no medida»* | **82 objetos · 80 `.bundle` = 87,3 MB** |

### 1.2 El campo `sex` — consumido, desplegado y validado en la tele

**Sin build de player**: lo lee el `index.html` (`_leerAcuarioParaFrases`), no `CastDataTypes`.
Deploy verificado por **md5 real** (`2b7eea41…`, 89.517 B), sello `rcv 2026-08-31-sexo`.

- `"Male"`/`"Female"` → **concordancia gramatical** («está deseando que **lo** veas»). Nunca «el macho
  Nemo» ⇒ **el peor caso es un pronombre**.
- `"Unknown"` · `""` · ausente · **cualquier otra cosa** → banco **neutro**, sin normalizar a ciegas.
- 💰 **`"Male"` SÍ marca, y no se re-debate.** Desde el runtime **no se puede distinguir «macho de
  verdad» de «macho por defecto»** ⇒ mandarlo al neutro **no arregla el pez roto: degrada a todos los
  sanos**, que son mayoría desde el `RandomSex()` de marzo.
- ⚠⚠ **La frontera es el 2026-03-09** (commit `07b9091`), **no «pre-v1.2»** — ventana mucho más
  pequeña de lo que decían los docs. Corregido en `CLAUDE.md` y `CAST_CONTRACT_TV.md`.
- ⚠⚠ El log separa los cuatro: `sexo M1/F1/Unknown1/ausente1` (+`/RAROSn`). **`ausente` va aparte de
  `Unknown` a propósito**: el día que el emisor deje de mandar el campo por un bug, juntarlos leería
  una **regresión del emisor** como «peces sin sexo conocido».
- ⭐ **Y ese segmento no existía ayer** ⇒ leerlo en el log **prueba por sí solo qué versión corre**,
  sin depender del sello ni de la caché. Es la línea `HORNEADO:` otra vez.

Validado en frío contra el device, los cuatro caminos y los dos idiomas:
```
mandé: sexo[Male:1 Female:1 Unknown:1 ausente:1] lang=es → tele: sexo M1/F1/Unknown1/ausente1, lang=es -> es
mandé: sexo[Female:4]                            lang=en → tele: sexo M0/F4/Unknown0/ausente0, lang=en -> en
```

### 1.3 Las 7 luces, medidas — y con paridad a dos pantallas

**Ninguna fundida, en ninguna de las dos pantallas.** Detalle y método: `CAST_PARIDAD_VISUAL.md` §0.7.

💰 **La columna que decide lo comercial** (`ILUM`, ruido 2,2 · umbral 2,5):

| preset | de pago | total dE (lo que se VE) | **ILUM dE** |
|---|---|---|---|
| `light_warm` | no | 18.1 | **39.0** |
| `light_sunset` | **sí** | 26.7 | **19.2** |
| `light_deep` | **sí** | **47.3** | 8.7 |
| `light_blue` | **sí** | 24.1 | 5.4 |
| `light_purple` | **sí** | 36.0 | **3.0** |

⚠⚠ **Guardar las CIFRAS, no las etiquetas de la herramienta**: pone «ILUMINA de verdad» a `blue`
(5,4) igual que a `warm` (39,0), y **entre ellos hay un factor 7**.
🧭 **`total dE` es lo que el usuario VE; `ILUM` es lo que el usuario COMPRA.** `deep` y `purple` están
entre los más visibles del lote con aporte de luz casi nulo: **no son presets malos, son filtros
excelentes.** ✅ Y la tienda **no** los vende como iluminación (medido en `es.json` por la sesión del
móvil) ⇒ **no hay desajuste de publicidad**, como mucho de precio.

---

## 2. 🚩 LAS DOS FALSAS ALARMAS DEL DÍA, LAS DOS MÍAS

### 2.1 El acreditador dio VERDE a capturas tomadas con la sesión ya muerta

`barre-luces.sh` daba **45 s** a una ráfaga que tarda **~50** ⇒ la sesión moría a mitad y
`cycle_f8` (t=276 s) era **la pantalla negra del apagado** (692 colores) y `cycle_f9` **el lanzador de
Android TV**. Inflaron el rango de `light_cycle`: agua alta daba `L* min 4,6 / recorrido 63,1`; los
buenos son **40,7 y 37,9**.

- 🧭 **El acreditador comprobaba QUÉ píxeles (sha256 contra el acta) pero no CUÁNDO se tomaron.**
  *Atar el fichero al acta no basta si el acta no dice cuándo acabó la fiesta.*
- 🏆 **Lo delató una PREDICCIÓN que la propia herramienta imprime** y que no cuadraba con el número
  de al lado. Ni error, ni aviso, ni excepción. **Una herramienta que imprime lo que espera
  encontrar se audita sola.**
- La causa raíz era un **comentario que sumaba mal**: «9 capturas a ~2 s = ~18 s» contaba los `sleep`
  y **olvidaba los ~3,5 s del propio `screencap` por red**. 🧭 *Un cálculo de tiempos que sólo suma
  las esperas miente.*
- **Arreglado en las dos capas**: `DUR` pasa a `TCYCLE+90`, el **final de sesión va al acta**, y
  `mide_luces.py` **excluye y denuncia** toda captura posterior.

### 2.2 🏆 Un detector que inventó 160 px de desajuste geométrico

Comparando pantallas hacía falta el borde del suelo. Mi detector —*«la fila con el mayor salto de
luminancia en la mitad baja»*— dio **tele 0.9273 · móvil 0.7792**, o sea **160 px**, con dispersión
**0.0000**. Estuvo a punto de irse a la nota como «las dos pantallas encuadran el tanque distinto»,
que además **habría abierto una investigación sobre el tamaño de los peces**.

**Era falso.** En el móvil el mayor salto de luz **es** el borde agua/grava; **en la tele es la banda
oscura del fondo** (niebla + viñeta), más fuerte. Midiendo por **color** (agua `B>R` → grava `R>B`,
que una viñeta no mueve): **0.7921 contra 0.7792 ⇒ 14 px. No había desajuste.**

- 🧭 **No es que el instrumento no viera la magnitud: veía OTRA distinta en cada entrada y reportaba
  las dos en las mismas unidades.** Peor que uno mudo, y peor que uno ruidoso: **la dispersión
  `0.0000` reforzaba la confianza en el artefacto.** *Un detector que se equivoca de forma
  perfectamente estable produce un número más creíble que uno bueno con ruido.*
- ⭐ **El arreglo NO fue cambiar de criterio.** `mide_luces.py` usa ahora **los dos** y **se calla**
  si discrepan más de 0,02. 🧭 *Callarse es más informativo que acertar por casualidad; un borde mal
  medido es un dato que apunta a una conclusión que no existe.*
- **Quien lo tumbó fue la sesión del móvil, con álgebra, no con una medida:** *«con una cámara
  ortográfica, "el alto de mundo es idéntico" y "el borde cae a 160 px" no pueden ser ciertas a la
  vez»*. 🧭 **Dos resultados propios que se contradicen se resuelven ANTES de reportar ninguno.**
- ⚠ **Cae con ello** mi aviso de que un 27 % de la banda `suelo cercano 0.90-1.00` era agua: con el
  borde real en 0,79 **la banda está limpia**, y `ILUM` nunca estuvo contaminado. La recomprobación
  que hice «con banda limpia» **no probaba lo que parecía** — el resultado vale, el argumento no.

### 2.3 ⚠ Y el corolario que más caro puede salir

Yo escribí, con la línea delante: *«`orthographicSize = worldHalfHeight` y el ancho sale del aspect
⇒ el alto de mundo es IDÉNTICO en las dos pantallas»*. **La línea es correcta y la conclusión no se
sigue**: dice que el alto **no depende del aspect**, no que sea el mismo en dos aparatos. (Resultó
que sí lo era — los dos `tank_l` valen 4.2 — pero eso lo dijo la medida, no el código.)

🧭 **El código dice la INTENCIÓN; sólo el aparato dice el RESULTADO.** Prima hermana del
`selectedLightId` del otro repo, donde `JsonUtility` aceptó un campo inventado sin rechistar y el
código que lo leía era impecable.

---

## 3. 📚 CUATRO DOCUMENTOS CADUCADOS EN UN DÍA

Todos decían algo cierto **cuando se escribió**, y ninguno daba señal de haber dejado de serlo:

1. `CLAUDE.md` — el corte de Telefónica **en presente**, medio día después de volver la ruta.
2. `CLAUDE.md` + `CAST_CONTRACT_TV.md` — la frontera «pre-v1.2» en vez del **2026-03-09**.
3. `CAST_PARIDAD_VISUAL.md` §0.6 — su medida del **agua** ya no describe la tele de hoy (el **suelo**
   sí: 2° contra 0°, 4,9 contra 7,4 L\*). Marcado como **parcialmente caducado**.
4. `TankData.cs:19` — el tooltip de los tanques, en **fichero compartido**. Sin tocar: decide el user.

🧭 **La nota que describe un bloqueo SOBREVIVE al bloqueo y lee igual de convincente que cuando era
cierta.** Antídoto operativo: **al desbloquear algo, buscar quién lo daba por bloqueado.**

---

## 4. 🧰 HERRAMIENTAS NUEVAS Y CAMBIADAS

```bash
node Tools/cast-headless.js --fish 5 --sex ciclo --lang en --dry-init   # imprime el INIT y sale
python Tools/mide_luces.py --dir _luces --aspect-ref 1.7778             # comparar dos pantallas
node Tools/test-frases.js                                               # 56 comprobaciones
```

- **`--sex`** (`ciclo` reparte **los cuatro** caminos) y **`--lang`**. ⚠⚠ **El defecto es `ninguno`**
  a propósito: cambiar el INIT por defecto haría que una tanda de hoy **no fuera comparable** con las
  de agosto, y ese desfase **no da ningún error**. Verificado: el resto del INIT es idéntico a `HEAD`.
- **`--dry-init`**: comprueba el `--sex` contra el **JSON real**, no contra una copia del código.
- ⚠⚠ **El arnés emite lo que se le teclee** ⇒ verlo funcionar prueba que **el `index.html` parsea**,
  NO que **el móvil manda**. El aviso está **dentro del fichero**.
- **`--aspect-ref R`**: recorta cada imagen **centrada** al aspect R. Se deriva del aspect de **cada
  imagen**, no de un factor tecleado ⇒ un móvil distinto se corrige solo. Validado en los dos
  sentidos con un fixture de bordes **magenta**: sin el flag contamina, con él vuelve **idéntico
  hasta el último decimal** a la tabla real.
- `test-frases.js`: **56** comprobaciones, con las 4 guardas de género **validadas en rojo**.

---

## 5. 📦 ESTADO

- **Árbol:** 8 ficheros modificados, **sin commitear**. `main` sigue por delante de `origin` sin
  pushear (último commit `7fa97bd`). ⚠ Repo **PÚBLICO**: el push no se retira.
- **Desplegado hoy:** `index.html` (`rcv 2026-08-31-sexo`, md5 verificado). El player **no** se tocó
  (sigue `rcv 2026-08-28 tmA`) — el `sex` no costó build.
- **R2:** operativo, 80 bundles / 87,3 MB medidos, 0 huérfanos.
- **Tele:** `192.168.1.40` (⚠ el DHCP la mueve y **el user la apaga**; localizar **por nombre**).
- **Copia de seguridad** del `index.html` anterior en `_luces/index.html.ANTES-del-31ago`.
- **Informe de las luces:** `_luces/informe_prod.txt` + acta + 13 PNG.

---

## 6. 🧭 REGLAS DE MÉTODO QUE SALIERON HOY

1. **Un instrumento puede ver OTRA magnitud en cada entrada y reportarlas en las mismas unidades.**
   Peor que mudo, y **la estabilidad del error refuerza la confianza en él**.
2. **Callarse es más informativo que acertar por casualidad.** Ante dos criterios que discrepan, no
   elegir: no reportar.
3. **El código dice la intención; sólo el aparato dice el resultado.**
4. **Una herramienta que imprime lo que espera encontrar se audita sola.**
5. **Un cálculo de tiempos que sólo suma las esperas miente.**
6. **La nota que describe un bloqueo sobrevive al bloqueo.** Al desbloquear, buscar quién lo daba por
   bloqueado.
7. **El acreditador es sospechoso en las DOS direcciones** — pero sólo una es silenciosa: decir NO
   ACREDITADA sobre algo bueno **te para**; decir ACREDITADA sobre algo malo **te deja publicar**.
8. **Mirar la imagen resuelve «¿qué hay ahí?», no «¿es exactamente el que pedí?»** — `bg_tropical`
   parece `bg_classic` a ojo, y por poco se mide la paridad contra el fondo equivocado.
9. **`x = 0` es la única coordenada que significa lo mismo en dos pantallas de aspect distinto.**
