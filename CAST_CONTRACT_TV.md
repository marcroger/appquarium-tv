### 4.3 ~~En INIT no valido los ids de preset~~ ✅ CERRADO (26-ago)

`SanearEstado` (`TvSceneBootstrap.cs:211`) valida `bgId`, `subId`, `lightId` y `ambientMode` al
recibir el INIT. **Sanea en vez de rechazar**: un INIT es la escena entera y tirarla por un id malo
dejaría la tele vacía, así que se corrige el campo, se dice por el canal y se sigue.

🧭 **Vacío ≠ inválido.** Un campo vacío es un cliente viejo o un rig mandando un estado mínimo: se
calla. Sólo se reporta lo que llega con contenido y equivocado. Una guarda que grita por todo se
acaba ignorando, y entonces no sirve el día que grita con razón.

# CAST_CONTRACT_TV.md — lo que la TV ACEPTA por el canal Cast

> Lado **receiver** del contrato. El lado **sender** lo declara el proyecto móvil
> (`D:\dev\appquarium-unity`), en `CAST_CONTRACT.md`.
>
> Fuente de verdad de lo que entra en este repo. **Auditado contra el código el 2026-08-26**
> (no copiado de docs anteriores): cada fila con su `fichero:línea`.
>
> Protocolo en prosa y payloads: `CAST_UPDATES.md`. Esto es el contrato.

---

## 0. La regla, aceptada

**Todo cambio es aditivo. Nunca se renombra ni se borra un campo ni un tipo.** Confirmado desde
este lado:

- `TvSceneBootstrap.ApplyUpdate` (`:205`) es un `switch` **sin `default`** → un tipo desconocido
  se ignora sin ruido. Verificado: el único `default:` del fichero está dentro de
  `ApplyAmbientMode` (`:556`), no en el switch de tipos.
- `JsonUtility` deja en su valor por defecto los campos que faltan e ignora los que sobran.

⚠ El corolario que avisa el móvil es exacto, y **aquí duele el doble**: un tipo mal escrito no da
error, da silencio. Hoy no hay forma de distinguir «el móvil no lo mandó» de «lo mandó mal».

🧭 Si algún día se quiere cerrar ese agujero, el sitio es un `default:` en `:205` que logue
`ERR update: tipo desconocido '<x>'`. **No está hecho a propósito**: mientras el móvil pueda ser
más nuevo que la TV, un tipo desconocido es *esperable*, no un fallo. Lo razonable sería logearlo
como aviso, no como error.

---

## 1. INIT — campos de `TvAquariumState` que LEO de verdad

Entrada: `TvSceneBootstrap.InitializeFromState` (`:178`) → `LoadAndInitializeCoroutine` →
`AquariumManager.InitializeFromCastStateAsync` (`:89`).

ℹ Hasta el 26-ago esta lógica estaba **duplicada** en una copia síncrona que no llamaba nadie.
Se borró: al adoptar el uid del móvil había que tocar dos sitios y olvidarse de uno no daba
ningún error, sólo un comportamiento distinto según la ruta. **Ahora hay un solo camino.**

| Campo | Lo uso en | Qué hago si falta o es inválido |
|---|---|---|
| `activeFish` | `AquariumManager.cs:109` | `null` → 0 peces, sin error |
| `activeFish[].uid` | ídem | ✅ **Se ADOPTA el uid del móvil** (26-ago). Vacío → genero uno propio, y entonces **ese pez no puede emparejarse**. Se reporta: `peces: N (uid propios: M)` |
| `activePairs` | `AquariumManager.cs:133` → `FishAgent.WirePairsFromSave` | Lista de `{maleUid, femaleUid}`. `null`/vacía → sin parejas. Sólo se cablea una pareja si **sus dos peces están en el tanque** |
| `activeFish[].speciesId` | clave Addressable | Si su bundle no carga: `ERR fish load FAILED …` y **ese** pez no aparece; los demás sí |
| `activeFish[].nickname` | `FishAgent.SetNickname` (`:75`) | **No se pinta en ningún sitio** — ver §4.2 |
| `activeFish[].ageScale` | `TvStubs.GetAgeGroup` (`:55`) → `FishAgent.cs:185` | `<= 0` (o campo ausente) → **Adulto**. Umbrales: `<0,525` cría · `<0,825` juvenil · `<1,09` adulto · resto senior |
| `activeFish[].sex` | ✅ **lo manda el móvil desde la 1.2.6 / code 41** (30-ago) y **la TV ya lo consume desde el 31-ago**: lo lee el `index.html` (`_leerAcuarioParaFrases`), NO `CastDataTypes` ⇒ **no costó build de player**. Aditivo por los dos lados | Lo usarán las frases de la splash. Valores: **`"Male"` · `"Female"` · `"Unknown"`** (enum de C# con `.ToString()`, **mayúscula inicial**) y **`""`** de un cliente que no mande el campo. Cualquier otra cosa → tratar como desconocido, **sin normalizar a ciegas**. ⚠⚠ **CORREGIDO EL 30-ago — este contrato pedía lo contrario.** Decía que el emisor mandara `""` cuando no estuviera seguro, porque el save del móvil tiene `"Male"` por defecto. La sesión del móvil lo persiguió hasta el fondo y **la conclusión se da la vuelta**: `sex` sólo se escribe con un valor deliberado (`SaveSystem.AddFish:383`), así que ese `"Male"` por defecto sólo sale en peces adquiridos **antes del 2026-03-09** (⚠ **CORREGIDO EL 31-ago: este contrato decía «anteriores a la v1.2», y señalaba una ventana MÁS GRANDE que la real.** El campo `sex` entra en el commit `07b9091` «feat: age/sex identity», de **marzo**, no en la v1.2 de breeding — lo persiguió por git la sesión del repo móvil. Desde esa fecha la mitad de los peces nacen `Male` por un `RandomSex()` **deliberado**, y ésos son dato bueno) — y para esos **la propia app YA los trata como machos** (`FishInspectorUI:340` les pinta ♂, `FishStatusOverlay:34` les da color de macho, `BreedingManager:236` los empareja como machos). Mandar `""` haría que **la tele dijera una cosa y el móvil otra con las dos pantallas delante del usuario**, que es peor que el dato imperfecto. ⇒ **manda el valor guardado, tal cual.** 🧭 La regla «manda `""` si no estás seguro» era buena en abstracto, pero **aquí no existe el estado “no seguro”: la TV está exactamente igual de segura que la pantalla del móvil**.<br>**Lo que hace la TV desde el 31-ago:** `"Male"`/`"Female"` → frase con **concordancia gramatical** («está deseando que **lo** veas»), nunca una afirmación del tipo «el macho Nemo» ⇒ **el peor caso es un pronombre**. `"Unknown"`, `""`, ausente y cualquier otro valor → **banco neutro**. Las frases de **pareja** se quedan neutras siempre (dos peces pueden tener sexos distintos). El log del receiver cuenta los cuatro por separado: `sexo M1/F1/Unknown1/ausente1` (+`/RAROSn` si llega algo no reconocido) — ⚠ **`ausente` va aparte de `Unknown` a propósito**: el día que el emisor deje de mandar el campo por un bug, juntarlos lo leería como «peces sin sexo conocido» en vez de como **regresión del emisor** |
| `decoJson` | `AquariumManager.cs:121` | `""` o `"{}"` → sin decos. ✅ **Comprobación de forma antes de parsear** (26-ago): si no parece un objeto JSON → `ERR INIT decoJson: …` y se ignoran las decos, en vez de tumbar el INIT |
| `bgId` | `SanearEstado` → `SaveData.selectedBgId` | ✅ **Validado (26-ago)**: desconocido → `ERR INIT bgId: id desconocido 'x' — válidos: … — se usa 'bg_classic'`. **Vacío no es error**: es «no me lo mandó» y se calla |
| `subId` | `SanearEstado` → `SaveData.selectedSubId` | ✅ igual que `bgId`; por defecto `sub_sand` |
| `lightId` | `SanearEstado` → `SaveData.lightPresetId` | ✅ igual; por defecto `light_white`. Además `light_green` se migra a `light_white` |
| `ambientMode` | `SanearEstado` → `AquariumManager` | ✅ **Validado (26-ago)**: desconocido → `ERR INIT ambientMode: …` y se usa `day`. Vacío → `day`, callando |
| `fishSpeed` | `AquariumManager.cs:106` | `<= 0` → `1`. Después `Clamp(0,25 … 3)`. (Antes, un 0 dejaba **todos** los peces clavados sin ningún error) |
| `selectedTankId` | → `SaveData.selectedTankId` | vacío → el tanque por defecto |
| `tankHalfWidth` | `AquariumManager.cs:150` → `DecorationPlacer.cs:361` | Remapeo de X **sólo si** `> 0,1` **y** los bounds de la TV `> 0,1`. `0` = cliente viejo → sin remapeo |
| `castJwt` | `TvSceneBootstrap.cs:186` → `TvBundleAuth.SetSessionToken` (`:74`) | Vacío → **return inmediato**, sigue el token constante. Es la conducta correcta durante la migración |

**`state == null`** (el JSON no parsea) → `ERR: INIT state is null — JSON parse failed!` por el
canal Cast, y no se toca la escena.

**INIT repetido** (reconexión): `TvSceneBootstrap.cs:194` **para la corrutina de carga anterior**
antes de arrancar otra. Sin eso, dos cargas simultáneas se pisaban los handles.

---

## 2. UPDATE — los 12 tipos que tengo, y qué hago con basura

Estado **tras los commits `458c217` y `2dbac4c` del 26-ago** (player `rcv 2026-08-26 ids`).

| tipo | handler | valor inválido → |
|---|---|---|
| `ambient` | `ApplyAmbientMode` (`:545`) | `ERR ambient: modo desconocido 'x' (day\|sunset\|night)` |
| `speed` | `:214` | `ERR speed: valor ilegible 'x'`. Válido → `speed: xN aplicado a M peces` |
| `feed` | `:225` | no lleva valor. Confirma con el nº de peces |
| `startle` | `:230` | ídem |
| `refresh` | `:236` | **no-op que sólo logea** — ver §5.4 |
| `add_fish` | `AddFishAsync` (`:707`) | payload no-objeto → `ERR payload: …` · sin `speciesId` → `ERR add_fish: el payload no trae speciesId` · bundle que no carga → `ERR add_fish: load failed x` · **spawn nulo → `ERR add_fish: … SpawnFish devolvió null`**. ✅ Acepta `uid` (26-ago) — sin él, el pez **no puede emparejarse nunca** |
| **`remove_fish`** ⭐ | `:750` | **Acepta las dos formas (27-ago).** `{"uid":"…","speciesId":"…"}` quita **ese** pez (`FishSpawner.DespawnByUid`); una cadena suelta sigue quitando **el primero de la especie** y el log lo dice: `remove_fish: x por especie (cliente sin uid: quitado el primero)`. ⚠ uid que no está en el tanque → `ERR remove_fish: uid 'x' no esta en el tanque` y **no se quita nada** — no cae al camino de la especie a propósito |
| **`add_deco`** ⭐ | `AddDecoAsync` (`:775`) | sin `itemId` → `ERR add_deco: el payload no trae itemId` · bundle → `ERR add_deco: load failed x` · **`PlaceAt` que rechaza → `ERR add_deco: … PlaceAt lo rechazó`**. **Desde el 27-ago acepta `tiltX`, `hasUserRot` + `quatX/Y/Z/W` y `mountedOnInstanceId`** (los mismos nombres que `DecoPlacement`), y **reporta lo que aplicó**: `add_deco: x at … +rot +tilt 12° montada sobre y` |
| `remove_deco` | `:820` | `remove_deco: x (ok=False)` si no existía |
| `change_bg` | `:899` | **`ERR change_bg: id desconocido 'x' — válidos: bg_classic\|…`** |
| `change_sub` | `:923` | ídem con los 12 sustratos |
| `change_light` | `:946` | ídem con las 7 luces |
| **`dump`** 🔬 | `VolcarEstado` | **Diagnóstico, no cambia nada.** No lleva valor. Vuelca por el canal el estado **montado**: cabecera con `bounds`, `remapX`, `bg/sub/luz/ambiente`; una línea `DUMP pez <uid> <especie> escala= pos= pareja=`; y una `DUMP deco <instanceId> <itemId> pos= escala= flip= quat= sobre=`. Ordenado por id y con precisión fija, **para poder hacer diff contra el móvil**. Marca `⚠RECORTADA-AL-BORDE` la deco que el `Clamp` haya movido |
| **`pairs`** ⭐ | `AplicarParejas` (`:909`) | Lista **completa** de parejas, no un delta. Payload `{"items":[{maleUid,femaleUid},…]}`. Reporta `pairs: N recibidas, M cableadas` — y **N≠M se dice**. ⚠ Si llega **antes de que exista el acuario** ya no se pierde: se **guarda y se reaplica** al terminar la carga (`pairs: aun no hay acuario — guardadas…`) |

**Los tres `change_*` releen el estado** después de aplicar (`bg.CurrentPresetId`,
`placer.CurrentSubstrateId`, `lighting.CurrentPresetId`) y sólo entonces confirman, con la
transición real: `change_bg: bg_kelp → bg_classic`. Si el preset es válido pero no llega a
aplicarse: `ERR change_bg: 'x' es válido pero el fondo sigue en 'y'`.

🧭 **Lo que esto significa para el móvil:** desde este player, **un id malo se oye**. Antes no —
el receiver hacía eco del id y parecía aplicado. Sirve además como sonda: el `ERR` trae la lista
de válidos.

✅ **La asimetría UPDATE-se-oye / INIT-en-silencio está cerrada** desde el 26-ago: el INIT valida
los mismos ids (§1). ⚠ Con un matiz deliberado: **un campo vacío no es un error**, es «no me lo
mandó», y se calla. Sólo grita lo que llega **con contenido y equivocado**.

---

## 3. Vocabularios que reconozco

Los presets están **hardcodeados en este repo**, no vienen del catálogo. Confirmado el 26-ago
comparando los arrays de los dos repos: **coinciden exactamente**.

| | nº | fuente en TV |
|---|---|---|
| Fondos | **11** | `TankBackground.Presets` (`:35`) |
| Sustratos | **12** | `DecorationPlacer.SubstratePresets` (`:35`) |
| Luces | **7** | `TankLightingController.Presets` (`:44`) |
| Peces / decos | 25 / 54 | `itemId` de los catálogos + clave Addressable |

Guarda automática: **`node Tools/check_preset_ids.js`** lee los ids de los arrays de C# y revisa
receiver y herramientas. Sale 1 si aparece un id fantasma. (El 26-ago encontró **cinco**.)

⚠ Añadir un fondo/sustrato/luz en el móvil **no lo crea aquí**. Pide un cambio en este repo **y
un rebuild de player** (~6 min con caché caliente, no los 55 que dicen los docs viejos).

---

## 4. Lo que NO cumplo, dicho en voz alta

### 4.1 ~~Un `decoJson` malformado no está protegido~~ ✅ CERRADO (26-ago)

`AquariumManager` envuelve el parseo en un `try/catch` que **no protege en este build** (el player
va con `Exception Support: None`, así que la excepción se escapa como error de JS). Lo que protege
ahora es una **comprobación de forma antes de parsear**, en `SanearEstado`: si `decoJson` no
empieza por `{`, sale `ERR INIT decoJson: se esperaba un objeto JSON y llegó '…'` y se ignoran las
decos. Es la misma guarda que `SafeFromJson` ya hacía para los payloads de UPDATE.

### 4.2 El mote de los peces no se pinta

`SetNickname` (`FishAgent.cs:75`) sólo asigna `fishName` y el nombre del GameObject. **No hay UI
en la TV.** → El hueco «renombrar un pez no manda nada» (**§6.5 del móvil**) **no existe**: no hay
nada que actualizar. Si algún día la TV pinta motes, vuelve a existir.

### 4.4 ~~El emparejamiento está montado y VACÍO~~ ✅ IMPLEMENTADO (26-ago), sin validar en device

Lo encontró la sesión del repo móvil barriendo su app en busca de estado visual que no sale por el
canal. Toda la maquinaria existía aquí —`WirePairsFromSave`, `SaveData.activePairs`,
`BreedingPair`, `PairBond` con peso **1,8** en Idle y **1,2** en Explore— y no se usaba nunca,
porque `TvAquariumState` no transportaba las parejas. **Una pareja emparejada nadaba junta en el
móvil y suelta en la tele.**

Lo que hay ahora:

1. **`uid` adoptado del móvil**, en el INIT (`AquariumManager.cs:114`) y en `add_fish`
   (`TvSceneBootstrap.cs:731`). Antes se generaba aquí con `Guid.NewGuid()` en **tres** sitios; hoy
   quedan **dos**, y los dos son fallback para cliente viejo. El tercero desapareció con la copia
   síncrona muerta.
   ⚠ Por eso `uid` en `TvAddFishPayload` **no es opcional**: un pez que entra a mitad de sesión con
   uid propio **no puede emparejarse jamás**.
2. **`activePairs` en el INIT** y **UPDATE `pairs`** para los cambios en vivo (§2).
3. ⚠⚠ **La carrera, arreglada.** El móvil emite `pairs` justo detrás del `add_fish` que forma la
   pareja, pero `AddFishAsync` **espera una descarga de bundle** (0,3-1,5 s en local, más en el
   device y en frío) y un `FishAgent` no entra en `FishAgent.All` hasta su `OnEnable`. O sea que el
   `pairs` puede llegar **antes que el pez** y `All.Find` devuelve null: la pareja se descartaba en
   silencio, y como `pairs` es reemplazo y sólo se emite al cambiar, **no se volvía a mandar**.
   → Se re-empareja tras cada `add_fish` que termina bien. Es seguro repetirlo porque
   `WirePairsFromSave` limpia **todos** los partners antes de re-cablear: los dos lados son de
   reemplazo total por construcción.
4. **Se reporta lo cableado, no lo recibido**: `pairs: 3 recibidas pero sólo 2 cableadas`. No es lo
   mismo, y la diferencia es exactamente el síntoma de la carrera.

Verificado en local con el rig (12/12, tests 10-12): dos `add_fish` con uid explícito + un `pairs`
→ `pairs: 1 recibidas, 1 cableadas`. **Eso sólo sale si el uid del móvil se adopta de verdad.**
⚠ **Sin validar contra el device todavía.**

ℹ Lo que **no** es problema: un `PartnerUid` colgando es inofensivo. `GetPartner()` re-resuelve
buscando en `All` (`:45-47`) y devuelve `null` si el compañero ya no está, con lo que `PairBond`
no aporta fuerza. No hay referencia rota que limpiar al quitar un pez.

---

## 5. Reconciliación con `CAST_CONTRACT.md` del móvil

Verificado desde este lado, contra el código de los dos repos.

### 5.1 Lo que confirmo

- **Los 12 tipos casan 1:1**, y el `switch` **no tiene `default`**. ✅
- **Catálogos idénticos** salvo CRLF. Comprobado hasheando los 6 `.json` de `Resources/Data` con
  los `\r\n` normalizados: `decoration_catalog`, `fish_catalog`, `field_guide`, `weekly_deco`,
  `weekly_env`, `weekly_fish` — **los 6 idénticos**. ✅
- **Ids idénticos**: 11 fondos, 12 sustratos, **7 luces**. Comparado array contra array. ✅
- **`TvAddFishPayload` y `TvAddDecoPayload` idénticos campo a campo.** ✅ Y `TvAquariumState`
  también, **salvo `castJwt`**. ✅
- **`castJwt` sólo existe aquí** → llega vacío, `SetSessionToken` hace `return` y sigue el token
  constante. ✅

### 5.2 Lo que corrijo

1. 🔴 **§7: «`FishStatusOverlay.cs` usa `UIManager`, que no existe en la TV» — es FALSO.**
   `UIManager` **sí existe**, como stub en `Assets/Scripts/Stubs/TvStubs.cs`. Lo que le faltan
   son **cuatro miembros**: `C_SEX_MALE`, `C_SEX_FEMALE`, `OverlayCanvas` y `MakeLabel`.
   `FishSex` **sí** existe aquí (`FishData.cs:110`).
   **El aviso es correcto en el fondo** —un `-Yes` a ciegas deja la TV sin compilar— pero el
   arreglo no es «crear el stub», es ampliarlo o excluir el fichero. **Ya está excluido**:
   `Tools/SyncFromMobile.ps1` tiene ahora `$FilesToExclude` y el fichero no se copia.
2. 🟡 **§6.5 no es un hueco** — ver §4.2.
3. 🟡 **«un id nuevo llega y la TV lo rechaza»** (§5 del móvil) es cierto **sólo por la ruta
   UPDATE**. En INIT no. Ver §4.3.

### 5.3 `uid` — adoptado ✅, `remove_fish` lo acepta ✅, y el móvil YA lo manda ✅

El uid del móvil **ya se adopta** (§4.4), desde el **27-ago** `remove_fish` **sabe usarlo**, y el
**30-ago se verificó que el móvil YA lo manda** — `AquariumManager.cs:786` serializa
`TvRemoveFishPayload { uid, speciesId }` y sólo cae a la cadena de especie si el uid viene vacío.
Está en producción desde la 1.2.5 / code 40. **El caso de los 3 Banggai está cerrado por los dos lados.**

**Antes:** llegaba sólo un `speciesId` y `DespawnOneBySpecies` quitaba **la primera** instancia de
esa especie. Con 3 Banggai en el tanque, quitabas uno concreto y desaparecía otro — sin ningún
error, con el log diciendo que todo bien.

**Lo que hay ahora, y es ADITIVO** (el camino viejo sigue vivo, no hace falta coordinar versiones):

| lo que mandéis | qué hace la TV |
|---|---|
| `"fish_banggai"` (como hoy) | quita el primero de la especie. **El log lo dice**: `remove_fish: fish_banggai por especie (cliente sin uid: quitado el primero)` |
| `{"uid":"<uid>","speciesId":"fish_banggai"}` | quita **ese** pez. Log: `remove_fish: fish_banggai uid=<uid> (quedan N peces)` |

- `speciesId` en el JSON es **opcional**: si no viene, la TV lo saca del propio pez. Va sólo para
  el log y para poder soltar el bundle sin recorrer el tanque.
- ⚠ **Un uid que no está en el tanque NO cae al camino de la especie**: responde
  `ERR remove_fish: uid 'x' no esta en el tanque` y **no quita nada**. Quitar «alguno» sería
  reintroducir el mismo fallo por la puerta de atrás.
- De paso se arregló una contabilidad que llevaba rota desde siempre: `remove_fish` destruía el
  pez pero **no lo sacaba de `ownedFish`/`activeFishUids`**, así que el save transitorio sólo
  crecía. Ahora se limpia, y si el pez estaba emparejado **la pareja se retira y se re-cablea**
  (si no, `pairs` la contaría para siempre como «recibida pero no cableada»).

🧭 **Verificado en local (`Tools/test-updates.js`, 16/16), NO en la tele todavía.**

### 5.4 `refresh`: qué hace de verdad

El nombre engaña: **no re-pide nada**. Es un `Debug.Log` + un `JsBridge.Log`
(`TvSceneBootstrap.cs:236-239`) y nada más; el comentario dice «waiting for new INIT», o sea que
espera a que el móvil mande el INIT por su cuenta.

**Recomendación: dejarlo vivo, pero como lo que es** — un *ack* de liveness. Es la única forma
que tiene el móvil de preguntar «¿estás ahí?» y ver respuesta en el log. Cuesta 4 líneas. Lo que
no hay que hacer es anunciarlo como «resync», porque no lo es.

---

### 5.5 ⚠⚠ CORRECCIÓN AL §11.2 DE `CAST_CONTRACT.md` (2026-08-28): el receptor SÍ monta

El doc del móvil añadió el 27-ago una §11.2 titulada **«Por qué no se pudo: el receptor no
sobrevive a su propio arranque»**, con esta traza y este veredicto:

```
[1.5s] Sender CONNECTED …    ← 1,5s es SU tiempo de vida: se recarga entero cada vez
[1.7s] Cast msg: INIT
[1.7s]   buffered (1)        ← su Unity aún no está listo, encola
                             ← y nunca llega el AQUARIUM READY
Livelock: cae → reconectamos → el receptor reinicia → … Encaja con la hipótesis de OOM.
```

**Es falso, y se midió el 28-ago.** Con esa misma traza en su logcat, la tele estaba renderizando:

| medida | resultado |
|---|---|
| dos `adb exec-out screencap` consecutivos (sesión C) | **103.088 px de 2.073.600 cambiando** → escena viva |
| lo mismo tras un arranque en frío (sesión D) | **134.270 px** → escena viva |
| lo que se veía | cañón, estatua, ~7 peces **y sus sombras** — o sea bundles bajados y shaders reapuntados |

**Lo que de verdad pasa:** el receptor monta y renderiza; lo que muere es el **relay de logs** del
receiver hacia el sender, a los ~45 ms de que el emisor haga su `RemoteMediaClient.load()` del
`silence.wav` (4 sesiones de 4, Δ entre +3 y +57 ms). Con `cast-headless` **solo**, el mismo relay
emite **135 y 139 líneas** a lo largo de sesiones de 147 s; con el APK delante, **4 líneas y mudo a
los 4,2 s**.

⇒ **`AQUARIUM READY` sí se emite. No llega.** Y como el `dump` del móvil cuelga en exclusiva de esa
línea, el volcado era estructuralmente inalcanzable — que es lo que se leía como «no monta».

🧭 **La regla, y es la tercera vez que muerde en este proyecto:** *ausencia de líneas en un log no
es ausencia de eventos.* Hay que separar «no pasó» de «no me llegó», y aquí el desempate barato es
**mirar la pantalla** (`screencap`), no razonar sobre el log.

⚠ El §11.1 del mismo doc marca el `silence.wav` con un **✅ «la carga tiene éxito»**. Es cierto y no
significa nada: mide que el `load()` no da error, **no** que suprima el timeout — que es lo único
que ese truco pretendía. Y hoy sabemos que además tiene un coste que nadie había visto.

ℹ Los dos apuntes son del intercambio con la sesión del repo móvil, que fue quien encontró la §11.2
y quien pidió corregirla. **El fichero es suyo**; aquí queda la medición para que este lado no
construya encima del diagnóstico viejo.

## 6. Coste de los huecos, desde este lado

Estimación de trabajo **en el repo TV** (no incluye el móvil). Ninguno implementado: el user aún
no ha decidido.

### 6.1 Editar una deco colocada — **más barato de lo que parece**

🧭 **No hace falta un tipo `update_deco`.** `DecorationPlacer.PlaceAt` (`:320`) **ya reemplaza la
instancia si el `instanceId` existe** (`:335`, `RemoveGameObject` + recolocación), y su firma **ya
acepta `tiltX` y `savedUserRot` (Quaternion?)**. Lo único que falta es que el payload los
transporte.

- **Coste TV: bajo** (~1 h). Ampliar `TvAddDecoPayload` con `tiltX`, `hasUserRot`,
  `quatX/Y/Z/W` y `mountedOnInstanceId` —aditivo, un móvil viejo los deja a 0— y pasarlos en
  `AddDecoAsync`. Reutilizando `add_deco` **no hace falta ni un tipo nuevo ni tocar el switch**.
- ⚠ **La pega:** reemplazar **destruye y recrea** el GameObject. Para un cambio al soltar el
  gesto es invisible; en un arrastre continuo parpadearía. O sea que el aviso de «emitir al
  soltar, no por frame» **con este camino es obligatorio**, no una optimización.
- Si más adelante se quiere arrastre en vivo, entonces sí: `update_deco` que mute el transform
  sin recrear. **Coste TV: medio** (~3-4 h), con las decos montadas de por medio.

### 6.2 `change_tank`

- **Coste TV: medio.** Recibir `selectedTankId` + `tankHalfWidth` es trivial, pero **las decos ya
  colocadas se remapearon con el valor viejo**: hay que recolocarlas todas con el factor nuevo, o
  media escena queda desplazada. Eso es un `RepositionAll` que hoy no existe. ~3 h.

### 6.3 ~~`uid` + `activePairs`~~ ✅ HECHO (26-ago) · ~~`remove_fish` por uid~~ ✅ HECHO (27-ago)

Coste real: **~3 h**, clavado en la estimación. El `DespawnByUid` y el handler de `remove_fish`
entraron el **27-ago** (§5.3): **~40 min**, dentro de la estimación de 1 h. **Lo que queda del
punto es vuestro**: mandar el uid en el payload.

### 6.4 Orden preferido

1. **`remove_fish` por uid** — 🟡 **el lado TV ya está** (§5.3). Os queda mandar el uid, que es
   un campo en el payload que ya construís. Sigue siendo el primero porque es el único que
   produce hoy un resultado **incorrecto y visible** (quitas un Banggai concreto y desaparece
   otro); los otros dos producen algo *desactualizado*, que es menos grave.
2. **Editar una deco colocada** — el que más se nota al usarlo, y ha resultado barato.
3. **`change_tank`** — el más caro y el menos frecuente.

🧭 El método de su §8 es el bueno: **handler en la TV → validar con `cast-headless.js` → y sólo
entonces cablear el emisor**. Añado un escalón **anterior** a la tele, nuevo desde el 26-ago:
`node Tools/static-server.js` + `node Tools/test-updates.js` prueban los handlers en Chrome contra
el catálogo de R2, sin device. Un tipo nuevo debería llevar su test ahí.

---

## 7. Fase 2 (JWT) — dónde está el bloqueo de verdad

El Worker de hoy (`Tools/r2-auth-worker/src/index.js`) **sólo compara contra una lista de tokens
constantes** (`BUNDLE_TOKENS`, `:72`) y expone `/health` (`:83`) y la ruta de bundles. **No hay
`/mint-token`, y tampoco verificación HS256.**

O sea: aunque el móvil mandara un JWT mañana, **el Worker lo rechazaría con 401**. El bloqueo no
es sólo del móvil.

`CAST_R2_AUTH_MOVIL.md:90` dice que el endpoint se escribe «cuando la parte móvil esté lista».
**Recomiendo invertirlo** y escribir primero el Worker (verificación HS256 + `/mint-token`),
manteniendo `BUNDLE_TOKENS` vivo en paralelo:

- El cambio es **aditivo y sin riesgo**: el token constante sigue funcionando, así que la tele no
  se entera.
- Le da al móvil **algo contra lo que probar** desde el primer día, en vez de escribir a ciegas.
- Coste: ~2-3 h + despliegue del Worker, **sin rebuild de player** (`castJwt` ya está en el state
  y el hook ya lo prefiere).

Pendiente de que lo decida el user.
