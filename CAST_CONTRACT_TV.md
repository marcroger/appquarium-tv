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

⚠ **Ojo al mantener esto:** la lógica está **duplicada** en `InitializeFromCastStateAsync`
(`:89-190`) y en `InitializeFromCastState` (`:197-256`). Tocar una y no la otra es un bug
esperando. Las líneas de abajo son las de la versión **async**, que es la que corre.

| Campo | Lo uso en | Qué hago si falta o es inválido |
|---|---|---|
| `activeFish` | `AquariumManager.cs:109` | `null` → 0 peces, sin error. Cada entrada genera un **uid nuevo en la TV** (`Guid.NewGuid()`); el móvil no manda uid — ver §5.3 |
| `activeFish[].speciesId` | clave Addressable | Si su bundle no carga: `ERR fish load FAILED …` y **ese** pez no aparece; los demás sí |
| `activeFish[].nickname` | `FishAgent.SetNickname` (`:75`) | **No se pinta en ningún sitio** — ver §4.2 |
| `activeFish[].ageScale` | `TvStubs.GetAgeGroup` (`:55`) → `FishAgent.cs:185` | `<= 0` (o campo ausente) → **Adulto**. Umbrales: `<0,525` cría · `<0,825` juvenil · `<1,09` adulto · resto senior |
| `decoJson` | `AquariumManager.cs:121` | `""` o `"{}"` → sin decos. **JSON malformado: ver §4.1, riesgo abierto** |
| `bgId` | → `SaveData.selectedBgId` → `TankBackground.SetPreset` | ⚠ **NO se valida en INIT**: un id desconocido cae al preset por defecto **en silencio**. Ver §4.3 |
| `subId` | → `SaveData.selectedSubId` → `DecorationPlacer.SetSubstrate` | ⚠ igual que `bgId` |
| `lightId` | → `SaveData.lightPresetId` → `TankLightingController.SetPreset` | ⚠ igual. Además `light_green` se migra a `light_white` (`AquariumManager.cs:310`) |
| `ambientMode` | `AquariumManager.cs:177` | `switch` con `default: SetDay()` → cualquier valor raro **es día, en silencio** |
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
| `add_fish` | `AddFishAsync` (`:707`) | payload no-objeto → `ERR payload: …` · sin `speciesId` → `ERR add_fish: el payload no trae speciesId` · bundle que no carga → `ERR add_fish: load failed x` · **spawn nulo → `ERR add_fish: … SpawnFish devolvió null`** |
| `remove_fish` | `:750` | **id desconocido → `remove_fish: x (removed=0)`**. Ya reportaba el efecto real. Quita **una** instancia (`FishSpawner.DespawnOneBySpecies`, `:215`) |
| `add_deco` | `AddDecoAsync` (`:775`) | sin `itemId` → `ERR add_deco: el payload no trae itemId` · bundle → `ERR add_deco: load failed x` · **`PlaceAt` que rechaza → `ERR add_deco: … PlaceAt lo rechazó`** |
| `remove_deco` | `:820` | `remove_deco: x (ok=False)` si no existía |
| `change_bg` | `:899` | **`ERR change_bg: id desconocido 'x' — válidos: bg_classic\|…`** |
| `change_sub` | `:923` | ídem con los 12 sustratos |
| `change_light` | `:946` | ídem con las 7 luces |

**Los tres `change_*` releen el estado** después de aplicar (`bg.CurrentPresetId`,
`placer.CurrentSubstrateId`, `lighting.CurrentPresetId`) y sólo entonces confirman, con la
transición real: `change_bg: bg_kelp → bg_classic`. Si el preset es válido pero no llega a
aplicarse: `ERR change_bg: 'x' es válido pero el fondo sigue en 'y'`.

🧭 **Lo que esto significa para el móvil:** desde este player, **un id malo se oye**. Antes no —
el receiver hacía eco del id y parecía aplicado. Sirve además como sonda: el `ERR` trae la lista
de válidos.

⚠ **Asimetría que hay que conocer:** todo esto vale para la ruta **UPDATE**. En **INIT** los ids
siguen cayendo en silencio (§1, §4.3).

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

### 4.1 Un `decoJson` malformado no está protegido

`AquariumManager.cs:119` envuelve el parseo en `try/catch`, **y ese `try/catch` no protege en
este build**: el player va con `Exception Support: None` (obligatorio, ver `CLAUDE.md`), así que
la excepción se escapa como error de JS en vez de entrar en el `catch`. Medido el 21-ago con el
payload de un UPDATE.

En la ruta de UPDATE eso ya se resolvió con una comprobación de **forma** antes de parsear
(`SafeFromJson`, `TvSceneBootstrap.cs:968`). **La ruta de INIT no la tiene.** Riesgo real pero
pequeño: `decoJson` lo genera `JsonUtility` en el móvil, no un humano.

### 4.2 El mote de los peces no se pinta

`SetNickname` (`FishAgent.cs:75`) sólo asigna `fishName` y el nombre del GameObject. **No hay UI
en la TV.** → El hueco «renombrar un pez no manda nada» (**§6.5 del móvil**) **no existe**: no hay
nada que actualizar. Si algún día la TV pinta motes, vuelve a existir.

### 4.4 El emparejamiento está montado y VACÍO (encontrado por la sesión móvil, 26-ago)

Toda la maquinaria de parejas existe aquí: `FishAgent.WirePairsFromSave` (`:55`),
`SaveData.activePairs` (`TvStubs.cs:98`), `BreedingPair {maleUid, femaleUid}` (`TvStubs.cs:33`) y
`SteeringController.PairBond()` con peso **1,8** en Idle y **1,2** en Explore (`:116`, `:127`).

**Y no se usa nunca**, porque `TvAquariumState` no transporta las parejas: `activePairs` está
siempre vacío y `WirePairsFromSave` se va por su primera línea (`FishAgent.cs:60`). Una pareja
emparejada nada junta en el móvil y **suelta** en la tele.

Tres cosas que hay que saber antes de arreglarlo:

1. **Depende del uid, igual que `remove_fish`.** Empareja por `pair.maleUid`/`femaleUid` contra
   `FishAgent.Uid`, y hoy los uid los genero yo. Mandar parejas sin mandar uid antes no sirve
   de nada.
2. ⚠⚠ **Genero uid en TRES sitios, no en uno:** `AquariumManager.cs:112` (INIT async, el que
   corre), `AquariumManager.cs:218` (INIT sync, la lógica **duplicada** de §1) y
   **`TvSceneBootstrap.cs:726` (`AddFishAsync`)**. Ese tercero es el que se escapa: un pez que
   entra por `add_fish` a mitad de sesión recibe un uid mío, así que **`uid` en
   `TvAddFishPayload` no es opcional** — sin él, un pez añadido durante la sesión no puede
   emparejarse jamás.
3. ⚠ **`WirePairsFromSave` se llama en UN solo sitio** (`AquariumManager.cs:349`, dentro del
   INIT). O sea que `activePairs` en el INIT arregla el caso «reconecto y las parejas salen
   bien», pero **NO** el caso «empareja dos peces con el Cast puesto»: eso no llega hasta el
   siguiente INIT. Cerrar eso pide un UPDATE propio (`pair`/`unpair`) o re-llamar a
   `WirePairsFromSave` tras cada `add_fish` — pero re-llamarlo sólo sirve si `SaveData.activePairs`
   está al día, o sea que **el UPDATE hace falta igual**. Hueco aparte, no lo tapa el INIT.

ℹ Lo que **no** es problema: un `PartnerUid` colgando es inofensivo. `GetPartner()` re-resuelve
buscando en `All` (`:45-47`) y devuelve `null` si el compañero ya no está, con lo que `PairBond`
no aporta fuerza. No hay referencia rota que limpiar al quitar un pez.

### 4.3 En INIT no valido los ids de preset

El arreglo del 26-ago cubre **sólo** `change_bg`/`change_sub`/`change_light`. Un `bgId` inválido
en el INIT sigue cayendo al preset por defecto sin decir nada, y un `ambientMode` raro se vuelve
día en silencio. **Coste de cerrarlo: ~15 líneas y el próximo build.**

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

### 5.3 `remove_fish` por uid — el grueso sí cae de este lado

Confirmado: hoy los uid **los genera la TV** (`AquariumManager.cs:113`, `Guid.NewGuid()`), así
que el uid del móvil no existe aquí. Para arreglarlo: `uid` en `TvFishEntry` **y** en
`TvAddFishPayload` (aditivo), la TV lo adopta en vez de generarlo, e indexa por uid.
`DespawnOneBySpecies` (`FishSpawner.cs:215`) pasa a ser `DespawnByUid`.

### 5.4 `refresh`: qué hace de verdad

El nombre engaña: **no re-pide nada**. Es un `Debug.Log` + un `JsBridge.Log`
(`TvSceneBootstrap.cs:236-239`) y nada más; el comentario dice «waiting for new INIT», o sea que
espera a que el móvil mande el INIT por su cuenta.

**Recomendación: dejarlo vivo, pero como lo que es** — un *ack* de liveness. Es la única forma
que tiene el móvil de preguntar «¿estás ahí?» y ver respuesta en el log. Cuesta 4 líneas. Lo que
no hay que hacer es anunciarlo como «resync», porque no lo es.

---

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

### 6.3 `uid` + `activePairs` (fundidos en un solo trabajo)

- **Coste TV: ~3 h.** El `uid` solo son ~2 h (§5.3); las parejas suman **~1 h** encima, porque una
  vez adoptado el uid `activePairs` es un campo más del INIT y `WirePairsFromSave` ya existe.
- **Forma que quiero recibir:** `activePairs` como lista de `{maleUid, femaleUid}` y nada más.
  Encaja 1:1 con `BreedingPair` (`TvStubs.cs:33`) sin una línea de pegamento.
  ⚠ `JsonUtility` casa por **nombre de campo, no de clase**: da igual que en el móvil se llame
  `BreedingPairRecord` mientras los campos se llamen `maleUid` y `femaleUid` y la lista
  `activePairs`.
- Aditivo y sin riesgo: mientras el móvil no mande `uid`, la TV sigue con el camino de hoy.
- ⚠ **Esto arregla el caso «reconecto», no el caso «empareja con el Cast puesto»** — ver §4.4.3.

### 6.4 Orden preferido

1. **`remove_fish` por uid** — es el único de los tres que produce hoy un resultado **incorrecto
   y visible** (quitas un Banggai concreto y desaparece otro). Los otros dos producen algo
   *desactualizado*, que es menos grave.
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
