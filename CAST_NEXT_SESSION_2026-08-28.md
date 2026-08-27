# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-27**. La anterior está en `CAST_NEXT_SESSION_2026-08-27.md`.
>
> **Segundo día sin tele.** Se fue entero en lo que se puede cerrar y medir en local, y salió más
> de lo esperado: `remove_fish` por uid **hecho**, una herramienta para saber si el C# compila
> **sin Unity y sin build**, y —lo gordo— **la paridad visual medida**, que resulta que no era lo
> que creíamos.
>
> **Mañana hay tele.** El plan está en §1 y son **dos tandas**, no una.

---

## 0.bis ✅✅✅ Y UNA TERCERA TANDA: `rcv 2026-08-27 decorot` DESPLEGADO Y VALIDADO

Por la tarde entró un tramo entero más, salido de que **el repo móvil revisó nuestro contrato**.
Tres cosas que eran nuestras y no lo sabíamos, más una herramienta nueva.

| en el device (tanda de 216 s, **0 errores**, WASM 111 MB) | |
|---|---|
| `pairs: 1 recibidas, 1 cableadas` | ✅ |
| `add_deco: … +rot +tilt 12°` | ✅ el cuaternión llega y se aplica |
| `remove_fish: … uid=uid-dev-hembra (quedan 13 peces)` | ✅ |
| `DUMP` con 14 peces y 5 decos, parseable | ✅ |
| tras quitar la hembra: `uid-dev-macho … pareja=-` | ✅ se retira la pareja y se re-cablea |

### Lo que entró

1. **`add_deco` perdía datos.** Sólo llevaba 6 campos, pero tras la primera rotación la verdad
   vive en el cuaternión `DecoPlacement.userRot`, no en `rotationY`. Reemitirlo sincronizaba
   mover/escalar/voltear pero **no girar, inclinar ni montar** — justo los que se ven mal. Ahora
   lleva `tiltX`, `hasUserRot`, `quat*` y `mountedOnInstanceId`, con los **mismos nombres que
   `DecoPlacement`**. 🧭 El camino del INIT ya lo hacía bien desde siempre: se copió ése.
2. **La ventana ciega del `pairs`.** `SaveData` no existe hasta que acaban los bundles, y el
   `pairs` del móvil sólo se emite al cambiar: un cambio en esa ventana **se perdía para
   siempre**. Ahora se guarda y se reaplica al terminar la carga.
3. **Comida doble.** Su autoalimentador emite `feed` desde el 26-ago y el nuestro seguía soltando
   lo suyo cada 240 s. Ahora se aparta mientras el sender alimente, y caduca a 1,5 intervalos.
4. 🔬 **UPDATE `dump`** — vuelca el estado **montado** (posición del transform vivo, no del
   payload), ordenado por id y con precisión fija, **para diffear la tele contra el móvil**.

### ⚠⚠ Dos trampas que costó cazar, las dos de fallo silencioso

- **El volcado salía con coma decimal**: `pos=(4,12,-1,87,0,54)`. Con coma decimal Y coma
  separadora **no hay forma de saber dónde acaba un número**, y el volcado existe justo para que
  alguien lo parsee. **El test pasaba en verde**: contaba líneas y comprobaba que las cuentas
  cuadraran, no que los números se pudieran leer. **Se vio mirando la salida de verdad.**
  Arreglado con `InvariantCulture` y un test que exige punto decimal **y que rechaza el formato
  viejo** (idea del repo móvil, mejor que la nuestra: un patrón que acepta las dos formas no fija
  nada).
- **El player construido no llevaba el `dump`**: se añadió después de compilar. Desplegarlo así
  habría hecho que su `SendUpdate("dump")` llegara a un `switch` sin `default` → **silencio**.
  Cazado comparando el mtime del `.cs` contra el del `.wasm`.

### ⚠ Una carga colgada, y cómo se diagnosticó bien

La primera vuelta de la tanda **se colgó bajando el bundle 12 de 16** y nunca terminó: el volcado
salió con `peces=0 decos=0`. No era el Worker.

⚠⚠ **La primera comprobación fue la equivocada y daba un 404 engañoso**: se probó el bundle con el
hash **del disco local**, que NO es el que pide el catálogo desplegado. Con el nombre sacado del
catálogo de R2 responde **200 en 0,3 s, tres veces**. Repetida la tanda, cargó 16/16 en 0,6 s.

🧭 **Regla:** si el acuario sale vacío o a medias, **repetir la tanda antes de tocar código**, y
para probar un bundle sacar el nombre **del catálogo desplegado**, nunca de `ServerData/` local.

---

## 0. ✅✅ LAS DOS TANDAS: HECHAS Y LIMPIAS (2026-08-27, por la tarde)

El user llegó a casa y encendió la tele el mismo día. **Las dos tandas de §1 se ejecutaron y
pasaron**, y el player `rcv 2026-08-27 rmuid` **está desplegado y validado**. La caja estaba en
**192.168.1.37** (el DHCP la vuelve a mover; ni ping ni 8008 la identifican — hay que leer el
nombre por `eureka_info`).

| | tanda 1 (`uid+pairs`) | tanda 2 (`rmuid`, ya desplegado) |
|---|---|---|
| sello leído **en pantalla** | `rcv 2026-08-26 uid+pairs` | **`rcv 2026-08-27 rmuid`** |
| `RP: … scale=` | **0.75** ✅ | **0.75** ✅ |
| decos / shaders reapuntados | 4/4 · 4 ✅ | 4/4 · 4 ✅ |
| ciclo día/noche | ✅ ambos sentidos | ✅ ambos sentidos |
| WASM | **111 MB plano** (50 s → 231 s) | **111 MB plano** |
| errores | **0** | **0** (sólo el `ERR` que se buscaba) |
| sesión | 231 s | 221 s |

### Lo que la tanda 2 probó, y que hasta hoy sólo se había visto en Chrome

| | en el device |
|---|---|
| **`pairs: 1 recibidas, 1 cableadas`** | ✅ **el emparejamiento cablea de verdad en la tele** |
| `add_fish` con uid del sender | ✅ adoptado (13 y 14 peces) |
| `remove_fish` uid inexistente | ✅ `ERR … no se quita nada` **y el contador no baja** |
| `remove_fish` por uid | ✅ `fish_goby_firefish uid=uid-dev-hembra (quedan 13 peces)` |
| `pairs` tras quitarla | ✅ `1 recibidas pero sólo 0 cableadas` — el save se limpia |
| `remove_fish` cadena suelta | ✅ `por especie (cliente sin uid: quitado el primero)` |

🧭 **Cómo se probaron las parejas sin la app:** `--update` de `cast-headless.js` acepta un **valor
JSON**, así que se mandaron `add_fish` con uid y el `pairs` a mano
(`--update 'pairs={"items":[{"maleUid":"…","femaleUid":"…"}]}@84'`). ⚠ Eso valida **el lado TV**;
que el emisor del móvil mande esos campos bien **sigue sin estar probado**.

### ⚠⚠ El deploy tuvo un susto que hay que recordar

`aws s3 sync` subió `.data`, `.wasm` y `framework.js` y **falló en `loader.js`** con
`SignatureDoesNotMatch` — el bug conocido de AWS CLI con ficheros pequeños, que `CLAUDE.md`
documenta para «<5 KB» y aquí mordió con **27 KB**. Durante unos segundos R2 sirvió un player
**mezclado** (3 ficheros nuevos + el `loader.js` viejo).

- ⚠⚠ **Y `EXIT=0`**: el fallo iba dentro de una tubería a `tail`, así que el código de salida era
  el de `tail`. **Un deploy roto puede terminar en verde.**
- Se arregló subiendo `loader.js` con boto3 y **verificando los 5 ficheros por md5 contra R2**.
- 🧭 **Regla: tras cualquier deploy, comprobar md5 fichero a fichero.** El `ETag` no sirve si
  acaba en `-N` (multiparte). Comprobado además que `keepalive_black.mp4` y `silence.wav` siguen
  ahí y que `StreamingAssets/` no se tocó (catálogo del 20-ago intacto).

**Lo que queda de este documento:** §2 (la comparación de fondos con el mismo preset en las dos
pantallas) y los pendientes de §6, empezando por el **Worker de la Fase 2**, que sigue sin
desplegar.

---

## 1. ⭐ LO PRIMERO: dos tandas, en este orden

Hay **dos** players sin validar, y conviene no mezclarlos. El de ayer lleva un mes de trabajo
encima; el de hoy sólo toca `remove_fish`. Validar por separado es lo que permite saber **qué**
rompió algo si algo se rompe.

### Tanda 1 — contra lo que YA está desplegado (`rcv 2026-08-26 uid+pairs`)

```bash
node Tools/cast-headless.js --stop --ip <IP>
node Tools/cast-headless.js --ip <IP> --fish 12 \
  --decos deco_anchor,deco_coral_corallium,deco_starfish_blue,deco_shell_lambis --diag \
  --update ambient=night@130 --update ambient=day@190 --duration 230
```

| en el log | esperado |
|---|---|
| el sello de la esquina | **`rcv 2026-08-26 uid+pairs`** — si dice otra cosa, la tele cachea |
| `RP: TvRenderPipeline scale=0,75 …` | **0,75**, no 0,70 |
| `peces: N (uid propios: M)` | **M = 0** si el móvil manda uid; **M = N** con móvil viejo (correcto, no es fallo) |
| `pairs: N recibidas, M cableadas` | **N = M** |
| `add_deco: … at …` (×4) | y **ningún** `ERR add_deco: … PlaceAt lo rechazó` |
| `AQUARIUM READY … shaders reapuntados al player: 4` | 4 con 4 decos |
| `ambient: Day → Night` y `Night → Day` | el ciclo sigue vivo |
| errores | **0** |

### Tanda 2 — sólo si la 1 sale limpia: desplegar `rcv 2026-08-27 rmuid` y validarlo

El player está **construido y sin desplegar** en `webgl-output/`. Deploy: **sólo `Build/` +
`index.html`**, nada de `StreamingAssets/`, sin `--delete` (comandos en `CLAUDE.md`).

Después, la misma tanda, más lo único nuevo: mandar un `remove_fish` con un uid inventado y otro
con una cadena suelta.

| esperado | |
|---|---|
| el sello | **`rcv 2026-08-27 rmuid`** |
| uid que no existe | `ERR remove_fish: uid 'x' no esta en el tanque — no se quita nada`, **y el nº de peces NO baja** |
| cadena suelta | `remove_fish: <id> por especie (cliente sin uid: quitado el primero) — quedan N peces` |

🛟 **Marcha atrás del deploy:** el player que hoy sirve producción está entero en el scratchpad de
sesión, `player-antes-rmuid/`. Su `.wasm` es md5 **`566334a9…`**, **comprobado bit a bit contra lo
que sirve R2** (bajado entero, no por `ETag` — ver §5).

---

## 2. 🎨 La paridad visual: medida, y no era el grado

Lo más importante del día. Barrido de las 8 variantes de `grade-tune.js` sobre el **player real**,
medido en **L\*/C\*** con `Tools/analiza_grado_lab.py`. Detalle y tablas:
**`CAST_PARIDAD_VISUAL.md` §0.5**.

1. ⚠⚠ **Copiar el grado del móvil PIERDE color:** −35 % de croma en el suelo y −28 % en el fondo,
   a cambio de **+0.5 L\*** de claridad. Es lo contrario de lo que se buscaba. **No hacerlo.**
2. **El bloom no aporta nada en escena oscura:** entre 1.2, 0.6, 0.35 y apagado, la claridad varía
   **±0.1 L\***. ⚠ Reserva honesta: `bg_kelp` es oscuro y el bloom necesita altas luces — **falta
   remedirlo en un fondo vivo** antes de darlo por inútil.
3. ⚠⚠ **La TV NO apaga el color.** Contra el PNG de origen, en cuatro fondos que van de L\* 0.9 a
   L\* 63.4, **el croma se conserva entero** (−0.1 a +1.6) y la claridad baja **~2 L\* constantes**
   en los cuatro — un velo pequeño y uniforme, coherente con la viñeta.
4. 🎨 **El «fondo casi en blanco y negro» es el arte:** **7 de los 11 fondos** están por debajo de
   croma 12 **en el propio fichero**. `bg_abyss` tiene croma **2.4**; `bg_classic`, **37.2** —
   **15× de diferencia**. Son cuevas, abismos y noche: están pintados así.

**Lo único que queda vivo** es la **nitidez**: 1024×683 en la TV contra 1536×1024 en el móvil,
**2,25× en píxeles**. Nada que ver con el color.

### ⚠ Lo que hay que hacer ANTES de decidir nada de nitidez

El user **no recuerda qué fondo tenía cada pantalla** cuando dijo que el móvil se ve mejor — lo
mirará mañana con la tele delante. Con **15×** de diferencia de croma entre presets, esa
comparación **no vale hasta repetirla con el mismo preset en los dos lados**. Protocolo completo
en `CAST_PARIDAD_VISUAL.md` §4:

1. Mismo **preset de fondo** y mismo **modo ambiente** en móvil y TV.
2. TV: `adb exec-out screencap`. Móvil: captura del teléfono (la hace el user).
3. Dejar la del teléfono como `_gradetune/ref_movil.png` y medir con `analiza_grado_lab.py`.

🧭 Si con el mismo preset el user **sigue** viendo mejor el móvil, es **nitidez** y toca §3.1 de
ese doc (Addressables, **no** subir el import). Si no lo ve, **no hay nada que arreglar** y se
cierra el tema.

---

## 3. Lo que se hizo hoy

### 3.1 🐟 `remove_fish` por uid — y una contabilidad rota desde siempre

`remove_fish` sólo transportaba la especie, así que quitaba **el primero** de esa especie: con 3
Banggai, quitabas uno concreto en el móvil y desaparecía otro en la tele. Sin error, y con el log
diciendo que todo bien.

Es **aditivo**, y el camino viejo **se identifica en el log**. Contrato: `CAST_CONTRACT_TV.md`
§5.3. Lo que le toca al móvil: **mandar el uid**, que es un campo en un payload que ya construye.

⚠ **Un uid que no está en el tanque NO cae al camino de la especie.** Quitar «alguno» sería
reintroducir el mismo fallo por la puerta de atrás.

⚠⚠ **De regalo, otra:** `remove_fish` destruía el pez pero **no lo sacaba de `ownedFish` /
`activeFishUids`**. `add_fish` los alimentaba y nadie los limpiaba, así que el save transitorio
sólo crecía y divergía del tanque. Ya se limpia, y si el pez estaba emparejado **se retira la
pareja y se re-cablea** — si no, `pairs` la contaría para siempre como «recibida pero no
cableada».

### 3.2 🧰 Saber si el C# compila, sin Unity y sin build

```bash
bash Tools/compile-check.sh          # runtime, ~15 s
bash Tools/compile-check.sh Editor
```

Salió de un susto: el único Unity abierto era de **otro proyecto** (`D:\dev\Distill`), así que el
MCP del **8091 hablaba con él**, `recompile_scripts` se quedó **7 minutos «working»** y
`Library/ScriptAssemblies/Assembly-CSharp.dll` seguía siendo del día anterior.

🧭 **Una Console vacía porque no ha compilado se parece demasiado a una Console limpia.**

Validado **en los dos sentidos**: verde con el código bueno y **rojo con un `CS0029` metido a
propósito**. Una herramienta de verificación que sólo se ha visto en verde no está verificada. Las
tres trampas silenciosas que costó montarla están en `CLAUDE.md`.

### 3.3 ⚠⚠ `waitForLog` miraba TODO el log acumulado

En `test-updates.js`, una línea de un test **anterior** daba por bueno un test posterior. No es
teórico: los dos tests nuevos de `remove_fish` **habrían pasado con líneas de los tests 2 y 12**.
Ahora hay marcas (`desde()`) y sólo se mira de ahí en adelante.

🧭 Misma familia que el `bg_ocean` que tuvo ese fichero meses en verde: **el test pasaba, y no
comprobaba nada.**

---

## 4. Estado

| | |
|---|---|
| Rama | **`feat/ciclo-dia-noche`** — `main` **sin tocar**, nada pusheado |
| Player construido | **`rcv 2026-08-27 rmuid`** · `.wasm` 21.687.176 · `.data` 19.506.551 · build **5:56**, 0 errores, audio 3/3, LTO aplicado |
| Player desplegado | **`rcv 2026-08-26 uid+pairs`** — sigue el de ayer, **a propósito** (§5) |
| Verificado sin tele | `test-updates.js` **16/16** · `check_preset_ids.js` limpio · Worker `test-local.mjs` **42/42** · `smoke-test.sh` contra el Worker vivo **12/12** · `compile-check` verde en los dos assemblies |
| Verificado EN device | **nada, ni de ayer ni de hoy.** La caja lleva dos días apagada |
| Bundles | sin tocar |
| Backup | `player-antes-rmuid/` en el scratchpad |

---

## 5. 🧭 Por qué el player de hoy NO se desplegó

Lo propuse («una tanda valida todo») y **el user preguntó si no nos cargaríamos algo, que está en
producción**. Tenía razón, y el argumento de la comodidad no compensaba:

- Lo que sirve producción ahora mismo **tampoco está validado en device** — se desplegó ayer y la
  caja no se ha encendido desde entonces. Añadir otra capa encima sólo empeora el diagnóstico.
- Si algo va mal, el síntoma aquí suele ser **la tele vacía**, que es lo más caro de encontrar.
- La segunda tanda, con la caja ya encendida, cuesta **4 minutos**.

⚠ **Y de paso salió un dato falso del handoff de ayer:** decía que el `.wasm` desplegado tenía md5
`ba33d26a…`. Es **`566334a9…`**. La comprobación buena está hecha —se bajó el fichero entero de R2
y es idéntico al local— pero **el `ETag` de R2 acaba en `-3` (subida multiparte) y NO es el md5**,
así que no sirve para comparar. Corregido en el doc de ayer.

---

## 6. Pendientes

- [x] ✅ **Las dos tandas** (§1) — hechas y limpias, más una tercera (§0.bis).
- [ ] ⭐⭐ **LA SESIÓN CON EL MÓVIL REAL.** El repo móvil compila el APK con uid, parejas,
      `remove_fish` por uid, `change_tank`, edición de decos y su propio volcado. **Una sola
      sesión cierra las dos cosas que quedan**: su §6.5 (`peces: N (uid propios: 0)` y
      `pairs: N recibidas, N cableadas`) **y** la comparación visual de §2, porque por fin habrá
      el **mismo estado en las dos pantallas**. Protocolo: mismo preset de fondo y mismo modo
      ambiente, `adb exec-out screencap` de la tele, captura del teléfono, y **diff de los dos
      volcados** — que debería enseñar sólo las líneas de cabecera que sabemos que difieren.
- [ ] ⭐ **La comparación de fondos con el mismo preset** (§2) — se hace en esa misma sesión.
- [ ] 🔐 **Desplegar el Worker de la Fase 2.** Escrito, **42/42** en local y **sin desplegar**:
      faltan `JWT_SECRET` y `MINT_TOKENS`, que sólo puede poner el user. El despliegue está ahora
      también en `Tools/r2-auth-worker/README.md`. Es aditivo (sin secrets → `503`, y el token
      constante sigue igual) y **se comprueba en el momento con `smoke-test.sh`, sin tele**.
      Marcha atrás: `npx wrangler rollback`.
- [ ] 🎨 **Editar una deco colocada** — el hueco más barato que queda. `PlaceAt` ya reemplaza por
      `instanceId`; basta ampliar `TvAddDecoPayload`. Ver `CAST_CONTRACT_TV.md` §6.1.
- [ ] 🖼 **Nitidez del fondo**, sólo si §2 lo confirma: por Addressables (§3.1 de
      `CAST_PARIDAD_VISUAL.md`) el `.data` **adelgaza** ~5 MB **y** da paridad; subir el import
      cuesta **+6,4 MB** en el `.data`.
- [ ] 🔎 **Remedir el bloom en un fondo vivo** (§2, punto 2).
- [ ] ❓ **La sonda de render, ¿se queda en producción?** Recomendación: **sí** — hoy ha vuelto a
      pagar su precio (`RP: … scale=0,75` es lo que dice qué build corre de verdad).
- [ ] De antes: **`change_tank`** (móvil) · ❌ decimar mallas **descartado por el user**, no volver
      a proponerlo.

---

## 6.bis 📲 El traspaso al repo MÓVIL

**[`CAST_HANDOFF_MOVIL_2026-08-26.md`](CAST_HANDOFF_MOVIL_2026-08-26.md)** — actualizado hoy con
`remove_fish` por uid (su §3, punto 2). **Sigue sin cerrarse a propósito**: la checklist de su
cabecera pide la tanda y la decisión sobre el Worker.

⚠ **La regla al entregarlo, que no ha cambiado:** de todo lo del emparejamiento y del
`remove_fish`, lo único que se sabe es que pasa **16/16 en local** y que compila. Eso **no es
«funciona»**. Si se entrega antes de la tanda, hay que decirlo con esas palabras.

---

## 7. Deploy — sin cambios

**Sólo `Build/` + `index.html`. NADA de `StreamingAssets/`.** Comandos en `CLAUDE.md`.
⚠ El `index.html` que se sube es el **procesado** (`webgl-output/index.html`), nunca el template.
