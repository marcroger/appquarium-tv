# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-26**. La anterior está en `CAST_NEXT_SESSION_2026-08-26.md`.
>
> **Día sin tele** (el user no estaba en casa), así que se fue entero en lo que se puede
> comprobar en local. Salieron **tres bugs de la misma familia** —el receiver confirmaba cosas
> que no habían pasado— y, de regalo, **el rig de pruebas local llevaba roto desde el último
> build de player** sin que nadie lo supiera.
>
> **Mañana, lo primero: UNA tanda valida las dos cosas** (§1): el `renderScale 0,75` que quedó
> pendiente ayer y el arreglo de hoy.

---

## 1. ⚠ PENDIENTE DE VALIDAR EN LA TELE

Hay un player nuevo construido: **`rcv 2026-08-26 uid+pairs`**. Lleva dentro el `renderScale 0,75`
de ayer (que nunca llegó a verse en la tele) **y** los arreglos de hoy.

```bash
node Tools/cast-headless.js --stop --ip <IP>
node Tools/cast-headless.js --ip <IP> --fish 12 \
  --decos deco_anchor,deco_coral_corallium,deco_starfish_blue,deco_shell_lambis --diag \
  --update ambient=night@130 --update ambient=day@190 --duration 230
```

| en el log | esperado |
|---|---|
| el sello de la esquina | **`rcv 2026-08-26 uid+pairs`** — si dice otra cosa, la tele está cacheando |
| `peces: N (uid propios: M)` | **M = 0** si el móvil ya manda uid; **M = N** con un móvil viejo, y entonces no habrá parejas (es correcto, no es un fallo) |
| `pairs: N recibidas, M cableadas` | **N = M**. Si N≠M, falta algún pez en el tanque |
| `RP: TvRenderPipeline scale=0,75 …` | **0,75**, no 0,70 |
| `add_deco: … at …` (×4) | y **ningún** `ERR add_deco: … PlaceAt lo rechazó` |
| `AQUARIUM READY … shaders reapuntados al player: 4` | 4 con 4 decos |
| `ambient: Day → Night` y `Night → Day` | el ciclo sigue vivo |
| errores | **0** |

🛟 **Marcha atrás:** el player anterior está entero en el scratchpad de sesión,
`player-desplegado-escala75/` (`.wasm` md5 `5f886ff3…`). Y `renderScale` sigue siendo ajustable
en caliente: `--raw 'GRADE={"renderScale":0.70}@40'`.

✅ **DESPLEGADO en R2 el 26-ago a las 10:44** (el user lo pidió; valida mañana). Subí sólo
`Build/` (los 4 ficheros) + `index.html`, **nada de `StreamingAssets/`** y **sin `--delete`**.
Comprobado después:

| | |
|---|---|
| sello que sirve R2 | `rcv 2026-08-26 uid+pairs` |
| `{{{` sin sustituir en el `index.html` | **0** — es el procesado, no el template |
| md5 de `.wasm` / `.data` | **`566334a9…`**, idéntico al local — ⚠ **los dos números que puso aquí esta sesión el 26-ago eran falsos**; comprobado el 27-ago bajando el fichero entero de R2. El `ETag` que devuelve R2 acaba en `-3` (subida multiparte) y **no es el md5**, así que no sirve para comparar |
| `keepalive_black.mp4` y `silence.wav` | **siguen ahí** (la trampa del `--delete`, esquivada) |

🧭 El player desplegado se probó en local **contra el catálogo de R2**, no contra el del
disco, así que los hashes de bundle que pide son los que hay en producción.

⚠ De paso, una discrepancia con `CLAUDE.md`: dice que en `Build/` viven también los rigs de
diagnóstico (`webgl-min.*`, `webgl-output-empty.*`) y **ahí sólo hay los 4 ficheros del player**.
No los ha borrado este deploy (`sync` sin `--delete` no puede borrar nada); ya no estaban.

---

## 1.bis Lo que entró DESPUÉS del primer deploy (misma tarde)

El user dio luz verde a los tres pendientes y se hicieron enteros. **El player desplegado ya no es
el de `ids`, es el de `uid+pairs`.**

- 🐟 **Emparejamiento vivo** — uid del móvil adoptado, `activePairs` en el INIT y UPDATE `pairs`.
  Detalle y la carrera que hubo que arreglar: `CAST_CONTRACT_TV.md` §4.4.
- ✅ **Los tres huecos que el contrato declaraba míos, cerrados**: validación de ids en el INIT
  (§4.3), comprobación de forma del `decoJson` (§4.1) y borrada la copia síncrona muerta de
  `InitializeFromCastState` (65 líneas que nadie llamaba y que había que mantener en paralelo).
- 🔐 **Fase 2 del JWT escrita en el Worker** (HS256 + `/mint-token`), **probada (42/42) y SIN
  DESPLEGAR**: necesita dos secrets que sólo puede poner el user — ver §7.

Verificado en local: **`test-updates.js` 12/12** con tres tests nuevos que añaden dos peces con uid
explícito y los emparejan. ⚠ **Nada de esto se ha visto en el device.**

---

## 2. Lo que se arregló, y por qué importa

### 2.1 El receiver confirmaba cosas que no habían pasado (3 sitios)

El patrón es siempre el mismo, y es el que más caro ha salido en este proyecto: **no peta,
simplemente no pasa lo que crees**, y el log dice que sí.

| dónde | qué hacía |
|---|---|
| `change_bg` / `change_sub` / `change_light` | Confirmaban **cualquier** id. `SetPreset` y `SetSubstrate` se plantan en un `Debug.LogWarning` —que **no viaja por el canal Cast**— y vuelven sin tocar nada, pero el handler logueaba `change_sub: sub_black` igual, y guardaba el id fantasma en `SaveData`. |
| `add_fish` | Decía `<id> spawned` **aunque `SpawnFish` devolviera null**. El `if (agent != null)` protegía las dos llamadas de debajo; el log salía igual. |
| `add_deco` | **Tiraba el `bool` que devuelve `PlaceAt`**: una deco rechazada (sin sitio) se confirmaba como colocada. |

Y había **seis ids de preset fantasma** repartidos por el proyecto: `bg_ocean`, `bg_reef`,
`bg_sunset`, `sub_black`, `sub_coral`. (`light_green` también aparece, pero ése **es legítimo**:
un preset retirado que `AquariumManager` migra a `light_white`.) Consecuencias **medidas**:

- La tecla **B** del `?devtest=1` no hacía nada en **3 de cada 6** pulsaciones, y la **S** en
  **2 de cada 4**. Con eso se dio por buena una prueba entera el 25-ago.
- **`Tools/test-updates.js` llevaba MESES en verde** mandando `bg_ocean`. Y la prueba de que no
  cambiaba nada estaba **en la línea de al lado**, sin que nadie la mirara:

```
[C#] agua: niebla=… (bg_kelp)      ← el fondo NO cambió
[C#] change_bg: bg_ocean
  ✅ change_bg                      ← verde igual
```

Ahora los handlers **releen el estado** después de aplicar en vez de reportar la intención
—mismo criterio que la sonda de render del 25-ago— y el log enseña la transición real:
`change_bg: bg_kelp → bg_classic`.

### 2.2 El rig local llevaba roto desde el último build de player

Los 7 bundles daban **404** y el acuario salía vacío. **No era el token, ni CORS, ni el
anti-bot de Cloudflare**: el Worker respondía **404 con la cabecera CORS puesta y el preflight
en 204**, o sea «ese bundle no está en el bucket».

Era la trampa del catálogo mordiendo al rig local: `static-server.js` servía
`webgl-output/StreamingAssets/aa/catalog.bin` **del disco**, que pide hashes que un build de
player regeneró y que **nunca se despliegan**.

⚠⚠ **Los dos catálogos pesan EXACTAMENTE lo mismo: 44.826 bytes.** Comparar por tamaño —o el
«suele ser idéntico y no hace falta tocarlo» que decía `CLAUDE.md`— **no lo detecta**.

Arreglado: el servidor sirve `/StreamingAssets/aa/*` **desde R2**. `--local-catalog` vuelve a
lo de antes.

---

## 3. Qué se puede comprobar YA sin la tele (esto es nuevo)

```bash
node Tools/static-server.js       # receiver en localhost:3001, catálogo de R2
node Tools/test-updates.js        # 9 tests de los handlers UPDATE
node Tools/check_preset_ids.js    # ids de preset fantasma (sin Unity, sin navegador)
```

Contra el player de hoy: **9/9** y **0 ids fantasma**. Los tests 7-9 son **negativos** (mandan
un id inexistente y exigen el `ERR`); contra un player anterior al 26-ago fallan a propósito.

🧭 **La regla que sale de todo esto: no comprobar nunca que el receiver REPITA lo que le
mandaste.** Comprobar contra algo que lea el estado real — para el fondo, la línea
`agua: … (<id>)`, que sale de `bg.CurrentPresetId`.

---

## 4. Estado

| | |
|---|---|
| Rama | **`feat/ciclo-dia-noche`** (21 commits) — `main` **sin tocar**, nada pusheado |
| Sello construido | **`rcv 2026-08-26 uid+pairs`** |
| Player | `.data` **19.505.585** · `.wasm` **21.684.934** · LTO aplicado, `PreflightAudio` 3/3, 0 errores CS |
| Build | **6 minutos** — caché caliente. (El «55 min» de los docs es con caché fría de IL2CPP.) |
| Bundles | **sin tocar** |
| Verificado sin tele | `test-updates.js` **12/12** (incluye uid y parejas) · Worker `test-local.mjs` **42/42** · `check_preset_ids.js` **limpio** · B×6 + S×4 → **10 cambios reales de 10** |
| Verificado EN device | **NADA de lo de hoy.** La caja estaba apagada |
| Backup | `player-desplegado-escala75/` en el scratchpad (`.wasm` md5 `5f886ff3…`) |

---

## 5. Pendientes

- [ ] ⭐ **Una tanda en la tele** (§1) — valida `escala75` + los arreglos de hoy de golpe.
- [ ] 🎨 **Paridad de grado con el móvil** — lo que queda del «se ve más nítido en el teléfono»
      una vez descartada la resolución. TV: tonemapping + `sat +18`. Móvil: `bloom 1,2` /
      `sat -15`. **Se barre con `GRADE` sin gastar builds.**
- [ ] 🔐 **Desplegar el Worker de la Fase 2** (§7 de `CAST_R2_AUTH_MOVIL.md`). El código está
      escrito y probado; faltan `JWT_SECRET` y `MINT_TOKENS`, que sólo puede poner el user. Es
      aditivo: sin esos secrets el camino nuevo da 503 y el token constante sigue igual.
- [ ] 🐟 **`remove_fish` por uid** (~1 h). Ahora es barato: los uid ya son los buenos en los dos
      lados. Falta `DespawnByUid` aquí y que el móvil mande el uid.
- [ ] ❓ **Decidir si la sonda de render se queda en producción.** Cuesta ~9 líneas de log por
      cambio de ambiente. Recomendación: **dejarla** — hoy ha vuelto a pagar su precio (el
      `RP: … scale=0,75` es lo que dice qué build está corriendo de verdad).
- [ ] 🤔 **¿Hemos ganado o perdido FPS estos días?** Sigue sin poderse saber comparando `avg`
      entre sesiones. Si interesa: desplegar el backup, medir con el mismo protocolo y volver.
- [ ] De antes: **fondos del `.data` a Addressables** · **Fase 2 JWT** (repo móvil) · **editar
      una deco colocada no manda UPDATE** (repo móvil) · ❌ decimar mallas **descartado por el
      user**, no volver a proponerlo.

---

## 5.bis 📲 El traspaso al repo MÓVIL

Todo lo que el otro lado necesita está en **[`CAST_HANDOFF_MOVIL_2026-08-26.md`](CAST_HANDOFF_MOVIL_2026-08-26.md)**,
escrito para esa sesión. **No está cerrado a propósito**: lleva arriba una lista de lo que hay que
rellenar antes de entregarlo, y lo primero es **la tanda de §1**.

🧭 **La regla al entregarlo:** hoy lo único que se sabe del emparejamiento es que pasa **12/12 en
local** y compila. Eso **no es «funciona»**. Si se entrega antes de la tanda, hay que decirlo con
esas palabras — es exactamente el error que este proyecto lleva un mes pagando (dar por bueno lo
que sólo se vio en el Editor o en Chrome).

Orden natural: **tanda → actualizar §7 del handoff con lo que se vio → entregar.** El despliegue
del Worker puede ir antes o después; lo que no puede es quedar sin decir en qué estado está.

---

## 6. Deploy — sin cambios

**Sólo `Build/` + `index.html`. NADA de `StreamingAssets/`.** Comandos en `CLAUDE.md`.
⚠ El `index.html` que hay que subir es el **procesado** (`webgl-output/index.html`), nunca el
template.
