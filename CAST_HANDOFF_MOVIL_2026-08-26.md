# ▶▶ PARA LA SESIÓN DEL REPO MÓVIL — leer entero antes de tocar el canal Cast

> Escrito por la sesión del repo **TV** (`D:\dev\appquarium-tv-unity`) el **2026-08-26**.
>
> La sesión móvil con la que se trabajó ese día **terminó antes de recibir esto**. Si eres una
> sesión nueva del repo móvil: aquí está lo que cambió de tu lado del contrato mientras no
> estabas, y lo que te toca a ti.
>
> **Nadie toca el repo del otro.** Esto es información, no instrucciones para editar aquí.

---

## ⏳ ESTADO DE ESTE DOCUMENTO — no está cerrado

Se escribió el **26-ago** y se entrega **cuando el trabajo de la TV esté validado contra el
device**. Mientras eso no pase, lo que dice sobre la parte TV es *«construido y probado en local»*,
que **no es lo mismo que funciona**.

Lo que hay que rellenar aquí antes de pasarlo:

- [ ] **La tanda en la tele** (ver `CAST_NEXT_SESSION_2026-08-27.md` §1). Valida de una vez el
      ciclo día/noche, `renderScale 0,75`, los ids y el emparejamiento. Al terminar: actualizar §7
      con lo que se vio de verdad, y **decir si `pairs` cableó en el device**, que es lo único que
      le importa al lado móvil.
- [ ] **Desplegar el Worker de la Fase 2**, o dejar dicho que sigue sin desplegar. Hasta entonces
      §1 describe un endpoint que **no existe todavía en producción**, y eso tiene que quedar
      claro o el otro lado programa contra un fantasma.
- [ ] Si algo de §1 cambia al desplegar, **corregirlo aquí antes de entregar**, no después.

⚠ **Lo que NO hay que hacer:** pasar este doc diciendo que el emparejamiento «funciona». Hoy lo
que se sabe es que **12/12 tests en local** y **0 errores de compilación**. El device no ha visto
nada de esto.

---

## 0. Los dos documentos que mandan

| Doc | Dónde | Qué es |
|---|---|---|
| `CAST_CONTRACT.md` | **tu repo** | Lo que el móvil MANDA. Lo escribiste tú. |
| [`CAST_CONTRACT_TV.md`](CAST_CONTRACT_TV.md) | repo TV | Lo que la TV ACEPTA, con `fichero:línea`. |
| [`CAST_R2_AUTH_MOVIL.md`](CAST_R2_AUTH_MOVIL.md) | repo TV | **§1.4 reescrita el 26-ago.** Es tu spec de la Fase 2 y ha cambiado. |

**Regla acordada entre los dos repos: todo cambio de protocolo es ADITIVO.** Nunca renombrar ni
borrar un campo ni un tipo. Se sostiene porque `JsonUtility` ignora lo que no conoce y el `switch`
de `ApplyUpdate` no tiene `default`.

⚠ El corolario incómodo: **un tipo mal escrito no da error, da silencio.** Un `chage_bg` no peta
en ningún lado, simplemente no hace nada.

---

## 1. ⚠⚠ DOS CAMBIOS DE CONTRATO QUE TE AFECTAN (Fase 2 del JWT)

El Worker de la Fase 2 está **escrito y probado** (42/42 en `Tools/r2-auth-worker/test-local.mjs`)
y **sin desplegar**. Al escribirlo se tomaron dos decisiones que **no estaban en el spec original**
y que cambian lo que tienes que programar. Detalle completo en `CAST_R2_AUTH_MOVIL.md` §1.4.

### 1.1 `/mint-token` ya NO es abierto: pide una credencial

```
POST https://appquarium-assets.appquarium.workers.dev/mint-token
Authorization: Bearer <MINT_TOKENS>          ← ⚠ ESTO ES NUEVO
Content-Type: application/json

{ "userId": "...", "isPremium": false,
  "ownedSpecies": [], "ownedDecoIds": [], "ownedPackIds": [] }
```
→ `200 {"token": "<jwt>", "exp": 1756300000}`

**Por qué.** El spec decía «el Worker se fía de lo que le manda el APK». Eso **sigue siendo
cierto para el CONTENIDO de los claims**: no se valida la compra contra Google Play, que es el
trade-off conocido del MVP. Pero un endpoint de emisión **sin ninguna credencial** es otra cosa:
cualquiera pide un token con `isPremium: true` y se baja el catálogo entero. Con eso la Fase 2
protegería **menos** que la Fase 1, que es exactamente lo contrario de lo que se busca.

**Qué te toca:** el APK tiene que **hornear su propia credencial**, igual que el receiver hornea
la suya. Es un secret **distinto** del `BUNDLE_TOKENS` del receiver, a propósito: dos clientes con
credenciales separadas, y rotar una no obliga a rebuildear al otro.

Errores del endpoint: `401` sin credencial o con una ajena · `400` sin `userId` o body que no es
JSON · `405` si no es POST · `503` si al Worker le faltan los secrets.

### 1.2 La propiedad se REGISTRA, no se aplica todavía

`OWNERSHIP_MODE` vale `log` (por defecto) o `enforce`.

En `log` **la firma y la caducidad se verifican de verdad**, pero si el usuario pide un bundle que
no consta como suyo **se le sirve igual** y la respuesta lleva `X-Aq-Ownership: would-deny`.

**Por qué.** Si los ids de los claims llegan con otro formato —sufijos de instancia, prefijos
distintos— el usuario **se queda sin SU acuario**, y eso se ve como una tele vacía: el síntoma más
caro de diagnosticar de este proyecto. Primero se mide con tráfico real; se pasa a `enforce`
cuando el contador de `would-deny` sea 0.

🧭 **Para ti esto es una red, no una excusa.** Los ids de los claims tienen que ser los `itemId`
del catálogo de Addressables igual que antes; la diferencia es que si te equivocas se verá en una
cabecera en vez de en una pantalla vacía.

### 1.3 Cómo se comporta el Worker con cada credencial

| lo que llega en `Authorization` | qué pasa |
|---|---|
| un `BUNDLE_TOKENS` (Fase 1) | se sirve. **Sigue vivo toda la migración** |
| JWT válido y con el ítem en sus claims | se sirve |
| JWT válido **sin** el ítem, modo `log` | se sirve + `X-Aq-Ownership: would-deny` |
| JWT válido **sin** el ítem, modo `enforce` | **403** |
| JWT caducado, mal firmado, `alg: none` o sin `exp` | **401 JWT invalido o caducado** |
| bundle de `audio` o `environments` | se sirve: no es de nadie |

⚠ Un token **con tres partes separadas por puntos se trata SIEMPRE como JWT** y no cae al camino
del token constante. Si no, un JWT caducado se compararía contra la lista y daría `403 Invalid
token`, un diagnóstico engañoso para algo que sólo necesita re-emitirse.

### 1.4 Está bloqueado por el user, no por ti

El Worker necesita **dos secrets nuevos** (`JWT_SECRET` y `MINT_TOKENS`) puestos en Cloudflare, y
eso lo hace el user. Hasta entonces no hay contra qué probar. El despliegue es aditivo: sin esos
secrets el camino nuevo devuelve `503` y el token constante sigue funcionando igual.

**La credencial de mint no viaja por chat entre sesiones.** Cuando el Worker esté desplegado, el
user te la dará por donde él decida.

---

## 2. El emparejamiento: hecho por el lado TV y desplegado

Contexto por si no lo tienes: **toda la maquinaria de parejas existía en la TV y no se usaba
nunca**, porque `TvAquariumState` no transportaba las parejas. Una pareja emparejada nadaba junta
en el móvil y **suelta** en la tele. Lo encontró la sesión móvil del 26-ago barriendo su app.

Lo que la TV acepta ya (player `rcv 2026-08-26 uid+pairs`, en R2):

| campo / tipo | forma | notas |
|---|---|---|
| `TvFishEntry.uid` | string | **Se adopta el uid del móvil.** Vacío → la TV genera uno y ese pez **no puede emparejarse** |
| `TvAddFishPayload.uid` | string | ⚠ **NO es opcional**: un pez añadido a mitad de sesión con uid propio no se empareja jamás |
| `TvAquariumState.activePairs` | `[{maleUid, femaleUid}]` | Sólo esos dos campos |
| UPDATE **`pairs`** | `{"items":[{maleUid,femaleUid},…]}` | Lista **completa**, no delta |

⚠ `JsonUtility` casa por **nombre de CAMPO, no de clase**: da igual que tu clase se llame
`TvPairEntry` y la de la TV `BreedingPair`. Lo que tiene que coincidir es `activePairs`, `maleUid`
y `femaleUid`. Si alguno se llamara distinto llegaría una lista de objetos con los campos a null
**y sin un solo error**.

### 2.1 La carrera — arreglada en la TV, NO toques el emisor

`FishAgent` entra en `FishAgent.All` en su `OnEnable`, o sea sólo cuando el prefab existe. Y
`AddFishAsync` **espera una descarga de bundle** (0,3-1,5 s en local, más en device y en frío).

Tú emites `pairs` desde el final de `CheckBreedingPairs`, y `OvularioPanel` lo llama justo después
de `AddFishToTank`. O sea que el `pairs` sale pisándole los talones al `add_fish`:

```
add_fish(pez nuevo)   → la TV empieza a bajar el bundle
pairs(lista con ese pez) → llega ANTES de que exista → All.Find devuelve null
                         → la pareja se descarta EN SILENCIO
```

Y como `pairs` es reemplazo y **sólo se emite al cambiar**, esa pareja no se vuelve a mandar.

**Arreglado en la TV**: se re-empareja tras cada `add_fish` que termina bien, con la última lista
recibida. Es seguro repetirlo porque `WirePairsFromSave` limpia **todos** los partners antes de
re-cablear: los dos lados son de reemplazo total por construcción.

🧭 **Cualquier apaño desde el emisor sería peor** (retrasar el `pairs`, reemitirlo a ciegas). Está
documentado como carrera conocida en los dos repos para que nadie lo «arregle» desde tu lado.

### 2.2 Dos líneas del log que te sirven de sonda

- `peces: N (uid propios: M)` → si **M ≠ 0**, ese INIT traía peces sin uid.
- `pairs: N recibidas, M cableadas` → **N ≠ M** significa que a alguna pareja le falta un pez en
  el tanque. Se reporta lo **cableado**, no lo recibido, justamente para que esa diferencia se vea.

---

## 3. Lo que te toca a ti (por orden)

1. 🔐 **La credencial de mint en el APK** (§1.1) y llamar a `/mint-token` con ella. Bloqueado
   hasta que el user despliegue el Worker.
2. 🐟 **`remove_fish` por uid — el lado TV YA ESTÁ (27-ago). Sólo falta que mandes el uid.**

   Lo que la TV acepta ahora, y es **aditivo** (no hay que coordinar versiones):

   | lo que mandes | qué hace la TV |
   |---|---|
   | `"fish_banggai"` (como hoy) | quita el primero de la especie. **El log lo dice**: `remove_fish: fish_banggai por especie (cliente sin uid: quitado el primero)` |
   | `{"uid":"<uid>","speciesId":"fish_banggai"}` | quita **ese** pez: `remove_fish: fish_banggai uid=<uid> (quedan N peces)` |

   - `speciesId` dentro del JSON es **opcional**; si no viene, la TV lo saca del propio pez.
   - ⚠ **Un uid que no está en el tanque NO cae al camino de la especie**: responde
     `ERR remove_fish: uid 'x' no esta en el tanque` y **no quita nada**. Quitar «alguno» sería
     reintroducir por detrás el fallo que esto arregla (con 3 Banggai, quitabas uno concreto y
     desaparecía otro).
   - 🧭 **Verificado en local (`test-updates.js` 16/16), NO en la tele.** Ver §7.
3. 🎨 **Editar una deco colocada** (§6.1 de `CAST_CONTRACT_TV.md`). 🧭 **No necesita un tipo
   `update_deco`**: `PlaceAt` ya reemplaza la instancia si el `instanceId` existe, y su firma ya
   acepta `tiltX` y `savedUserRot`. Basta ampliar `TvAddDecoPayload` (aditivo) y reemitir
   `add_deco` con el mismo `instanceId`. ⚠ Reemplazar destruye y recrea el GameObject: **emitir al
   soltar el gesto, nunca por frame** — con este camino es obligatorio, no una optimización.
4. 🪟 **`change_tank`.** El más caro y el menos frecuente. Lo caro cae del lado TV: las decos ya
   colocadas se remapearon con el `tankHalfWidth` viejo y hay que recolocarlas todas.

---

## 4. Cosas que se verificaron y NO son huecos

No las persigas, ya están medidas:

- **Renombrar un pez.** La TV **no pinta motes** en ningún sitio (`SetNickname` sólo asigna
  `fishName` y el nombre del GameObject; no hay UI). No hay nada que actualizar.
- **`ageScale` desincronizándose.** No puede pasar durante una sesión: `ageInDays` sólo avanza en
  el arranque de la app. Y la TV reconstruye el grupo por umbrales con margen (`<0,525` / `<0,825`
  / `<1,09`), así que ni un drift del float cambiaría de grupo.
- **`PartnerUid` colgando.** Inofensivo: `GetPartner()` re-resuelve y devuelve `null`, con lo que
  `PairBond` no aporta fuerza. No hay referencia rota que limpiar al quitar un pez.
- **`refresh`.** Tiene handler en la TV pero **no re-pide nada**: es un log. Queda vivo como *ack*
  de liveness. No lo documentes como «resync».

---

## 5. Correcciones que se le hicieron a `CAST_CONTRACT.md`

La sesión móvil del 26-ago las aceptó y las marcó con ✏ en su doc. Por si se perdieron:

1. 🔴 **«`FishStatusOverlay.cs` usa `UIManager`, que no existe en la TV» era FALSO.** `UIManager`
   **sí existe**, como stub en `TvStubs.cs`. Lo que le faltan son **cuatro miembros**:
   `C_SEX_MALE`, `C_SEX_FEMALE`, `OverlayCanvas` y `MakeLabel`. `FishSex` también existe allí.
   El aviso era correcto en el fondo (un `-Yes` a ciegas deja la TV sin compilar) pero el arreglo
   que se deducía, no. **Ya está resuelto en el repo TV**: `SyncFromMobile.ps1` tiene un
   `$FilesToExclude` con ese fichero dentro.
2. 🟡 **«un id nuevo llega y la TV lo rechaza»** era cierto sólo por la ruta UPDATE. **Ya no**:
   desde el 26-ago el INIT también valida. Con un matiz: **un campo vacío no es un error** («no me
   lo mandó», se calla); sólo grita lo que llega con contenido y equivocado.

---

## 6. Cómo verificar sin la tele

El repo TV tiene un rig que prueba los handlers en Chrome, sin device:

```bash
node Tools/static-server.js       # receiver en localhost:3001, catálogo servido DESDE R2
node Tools/test-updates.js        # 12 tests de los handlers UPDATE (incluye uid + parejas)
node Tools/check_preset_ids.js    # ids fantasma + que el contrato no mienta
```

⚠ **`static-server.js` sirve `StreamingAssets/aa/` desde R2, no del disco**, a propósito: el
catálogo local pide bundles con hashes que nunca se despliegan y daba **404 en los siete**. Los dos
catálogos pesan **exactamente lo mismo** (44.826 B), así que compararlos por tamaño no lo detecta.

**Orden para cualquier tipo nuevo:** handler en la TV → test en `test-updates.js` (sin device) →
`cast-headless` contra la tele → y sólo entonces cablear el emisor en el móvil. Un ciclo
compilar-castear en vez de quince.

🧭 **Dos guardas que valen la pena tener en los dos lados**, y que ya se han pagado solas:
- Comprobar que **cada tipo de mensaje esté documentado**. La versión móvil cazó `pairs` sin
  documentar **una hora después de cablearlo**; la versión TV se cazó a sí misma el mismo día.
  No es descuido: la ventana entre escribir código y escribir el doc siempre existe.
- Comprobar que **las listas de ids del doc cuadren con el código**. Un doc con ids escritos a
  mano es exactamente el bug que la guarda persigue.

---

## 7. Estado del lado TV, al cierre del 26-ago

| | |
|---|---|
| Player desplegado | **`rcv 2026-08-26 uid+pairs`** · `.wasm` 21.684.934 · `.data` 19.505.585 |
| Verificado sin device | `test-updates.js` **12/12** · Worker **42/42** · 0 errores CS |
| Verificado EN device | **nada de esto todavía** — la caja estaba apagada |
| Worker Fase 2 | escrito y probado, **sin desplegar** (faltan los secrets) |
| Rama | `feat/ciclo-dia-noche`, **sin push**, `main` intacto |
