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

Hay un player nuevo construido: **`rcv 2026-08-26 ids`**. Lleva dentro el `renderScale 0,75`
de ayer (que nunca llegó a verse en la tele) **y** los arreglos de hoy.

```bash
node Tools/cast-headless.js --stop --ip <IP>
node Tools/cast-headless.js --ip <IP> --fish 12 \
  --decos deco_anchor,deco_coral_corallium,deco_starfish_blue,deco_shell_lambis --diag \
  --update ambient=night@130 --update ambient=day@190 --duration 230
```

| en el log | esperado |
|---|---|
| el sello de la esquina | **`rcv 2026-08-26 ids`** — si dice `escala75`, la tele está cacheando |
| `RP: TvRenderPipeline scale=0,75 …` | **0,75**, no 0,70 |
| `add_deco: … at …` (×4) | y **ningún** `ERR add_deco: … PlaceAt lo rechazó` |
| `AQUARIUM READY … shaders reapuntados al player: 4` | 4 con 4 decos |
| `ambient: Day → Night` y `Night → Day` | el ciclo sigue vivo |
| errores | **0** |

🛟 **Marcha atrás:** el player anterior está entero en el scratchpad de sesión,
`player-desplegado-escala75/` (`.wasm` md5 `5f886ff3…`). Y `renderScale` sigue siendo ajustable
en caliente: `--raw 'GRADE={"renderScale":0.70}@40'`.

⚠ **Estado del deploy:** *(rellenar — a la hora de escribir esto el build estaba hecho y
verificado en local, pendiente de que el user diga si se sube a R2)*. Si se sube: sólo
`Build/` + `index.html`, **NADA de `StreamingAssets/`**.

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
| Rama | **`feat/ciclo-dia-noche`** (7 commits) — `main` **sin tocar**, nada pusheado |
| Sello construido | **`rcv 2026-08-26 ids`** |
| Player | `.data` **19.504.862** (+933) · `.wasm` **21.683.276** (+1.997) · LTO aplicado, `PreflightAudio` 3/3, 0 errores CS |
| Build | **6 minutos** — caché caliente. (El «55 min» de los docs es con caché fría de IL2CPP.) |
| Bundles | **sin tocar** |
| Verificado sin tele | `test-updates.js` **9/9** · `check_preset_ids.js` **limpio** · B×6 + S×4 → **10 cambios reales, 0 rechazos** (antes 6 de esas 10 no hacían nada) |
| Backup | `player-desplegado-escala75/` en el scratchpad (`.wasm` md5 `5f886ff3…`) |

---

## 5. Pendientes

- [ ] ⭐ **Una tanda en la tele** (§1) — valida `escala75` + los arreglos de hoy de golpe.
- [ ] 🎨 **Paridad de grado con el móvil** — lo que queda del «se ve más nítido en el teléfono»
      una vez descartada la resolución. TV: tonemapping + `sat +18`. Móvil: `bloom 1,2` /
      `sat -15`. **Se barre con `GRADE` sin gastar builds.**
- [ ] ❓ **Decidir si la sonda de render se queda en producción.** Cuesta ~9 líneas de log por
      cambio de ambiente. Recomendación: **dejarla** — hoy ha vuelto a pagar su precio (el
      `RP: … scale=0,75` es lo que dice qué build está corriendo de verdad).
- [ ] 🤔 **¿Hemos ganado o perdido FPS estos días?** Sigue sin poderse saber comparando `avg`
      entre sesiones. Si interesa: desplegar el backup, medir con el mismo protocolo y volver.
- [ ] De antes: **fondos del `.data` a Addressables** · **Fase 2 JWT** (repo móvil) · **editar
      una deco colocada no manda UPDATE** (repo móvil) · ❌ decimar mallas **descartado por el
      user**, no volver a proponerlo.

---

## 6. Deploy — sin cambios

**Sólo `Build/` + `index.html`. NADA de `StreamingAssets/`.** Comandos en `CLAUDE.md`.
⚠ El `index.html` que hay que subir es el **procesado** (`webgl-output/index.html`), nunca el
template.
