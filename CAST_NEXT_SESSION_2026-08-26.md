# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-25**. La anterior está en `CAST_NEXT_SESSION_2026-08-25.md`.
>
> **El día se fue en lo visual: la escena ya no se ve como assets separados.** Niebla de agua,
> tono de peces y `renderScale` 1:1 con la salida. Todo desplegado; **todo validado en la tele
> menos el último cambio** (`renderScale 0,75`), que se quedó sin tanda porque se apagó la tele.
>
> **Mañana, lo primero: validar `escala75` en el device** (§1). Es una tanda de 4 minutos.

---

## 1. ⚠ LO ÚNICO PENDIENTE DE VALIDAR

`renderScale` pasó de **0,70 a 0,75**. Está construido y **desplegado** (`rcv 2026-08-25 escala75`)
pero **sin ver en la tele**.

Riesgo: **bajo**. El valor ya se midió en el device por mensaje (avg 35, idéntico al 0,70), lo
único que cambia es que ahora viene por defecto en vez de por `GRADE`. Aun así hay que verlo.

```bash
node Tools/cast-headless.js --stop --ip <IP>
node Tools/cast-headless.js --ip <IP> --fish 12 \
  --decos deco_anchor,deco_coral_corallium,deco_starfish_blue,deco_shell_lambis --diag \
  --update ambient=night@130 --update ambient=day@190 --duration 230
```

Qué tiene que salir:

| en el log | esperado |
|---|---|
| `RP: TvRenderPipeline scale=0,75 …` | **0,75**, no 0,70 |
| `agua: niebla=… den=0.30 … desat=0.32 dim=0.16 deco=0.25` | los valores elegidos por el user |
| `AQUARIUM READY … shaders reapuntados al player: 4` | 4 con 4 decos |
| `ambient: Day → Night` y `Night → Day` | el ciclo sigue vivo |
| errores | **0** |

🛟 **Marcha atrás sin build:** `--raw 'GRADE={"renderScale":0.70}@40'`. Y el player anterior
(`escala`, con 0,70 por defecto) es el mismo binario salvo esa constante.

---

## 2. Lo que se hizo hoy, y por qué

El user reportó que en la tele **«se ve todo como assets separados»** y que en el móvil todo es
más nítido. Se midió en el device antes de tocar nada:

```
croma perceptual C*:   peces 42,6    decos 25,5    agua 23,1
```

**El problema eran los PECES, no el decorado.** Las decos ya estaban integradas. `FishUnlit` era
textura × `_Brightness 2.0` y nada más, y **ningún shader del proyecto leía la profundidad**, así
que un pez del fondo tenía el mismo contraste que uno pegado al cristal.

Además, la **juntura suelo/fondo** era un corte a cuchillo: el fondo cae a luminancia 1,9-10,6
justo donde el suelo arranca en 56 y sube a 100,9 — un salto de ×12 a ×30 en 40 píxeles.

### Lo entregado

- **Niebla de agua** por Z del mundo (⚠ la cámara es **ortográfica**: la distancia a cámara no
  sirve) en `FishUnlit`, `DecoLit` y **dos shaders nuevos**: `SubstrateFog` (el suelo) y
  `FishFin` (las aletas). Detalle y valores en `CLAUDE.md`.
- **Tono de peces** (`desat 0,32` / `dim 0,16`), elegido por el user sobre el device.
- **`renderScale 0,75`** — 1:1 con la salida real.
- **Mensaje `FOG`** y **`renderScale` dentro de `GRADE`**: los seis parámetros se afinan en
  caliente, sin gastar builds. El rollback total es un mensaje.
- **Sonda de render** (`TvSceneBootstrap.SondaDeRender`): **LEE** el estado real del render en vez
  de reportar lo que el código calcula. Se queda: descartó tres hipótesis en un solo build y cazó
  la trampa del GUID en 60 s.

---

## 3. ⚠⚠ Las tres trampas que costaron tiempo hoy

### 3.1 La línea base mal tomada (costó un build y una alarma falsa)

Durante horas pareció que **el ciclo día/noche no llegaba al device**: sunset daba ratio 1,00 y
en night «el azul subía», lo cual es imposible con una multiplicación. Se llegó a escribir en
memoria que Chrome no valía para validar render.

**Era un error de medición propio.** La captura de «día» se tomaba tras `AQUARIUM READY`, pero el
receiver arranca **~35-45 s por detrás del sender**, así que para entonces el sunset ya estaba
aplicado: se comparaba **sunset contra sunset**.

🧭 **«Capturar por evento» no basta si el evento de referencia es el equivocado.** La línea base
se toma **ANTES de mandar el primer UPDATE**. Y si un ratio sale imposible (un canal que **sube**
con una multiplicación), sospechar de la línea base antes que del motor.

### 3.2 GUID de shader no hexadecimal (costó un build)

Los `.meta` de `SubstrateFog` y `FishFin` se crearon con GUIDs «legibles» (`5ub57ra7e…`,
`f15hf1n0…`) y **`u`, `r`, `h`, `n` no son hex**. Unity **reescribió las entradas de
`m_AlwaysIncludedShaders` como `{fileID: 0}`**, stripeó los shaders y `Shader.Find` devolvió
`null`. Detalle en `CLAUDE.md`.

✅ No se rompió nada: el `FallBack "Sprites/Default"` y la cadena de `Shader.Find` hicieron que
el suelo se comportara **como antes** en vez de salir magenta. **Fallar hacia lo de antes.**

### 3.3 El `FPS avg` del HUD es acumulativo

Barrer las 4 escalas dentro de una sesión **no vale**: el `avg` arrastra el arranque y sube
monótonamente. El control lo delató — «0,70 → 28 fps» al principio y «0,70 → 41 fps» al final de
la misma tanda. Para comparar hacen falta **sesiones separadas leídas al mismo `SESSION`**.

---

## 4. Dos afirmaciones mías que resultaron falsas

Van aquí porque están escritas en sitios que se leen:

1. **«La TV renderiza a 1344×756 y estira a 1080p»** — falso. **`Screen` es 2560×1440**, así que
   `0,70` daba **1792×1008**, el **93 % lineal** de 1080p. La `renderScale` apenas costaba
   nitidez. El comentario del propio código llevaba años diciendo «49 % de píxeles» sin que nadie
   lo comprobara. **Ya corregido en el código y en `CLAUDE.md`.**
2. **«Los fondos están a 512»** — están a **1024**.

🧭 Si la diferencia de nitidez con el móvil sigue ahí, hay que buscarla en el **grado**: la TV
lleva tonemapping + `sat +18`, el móvil `bloom 1,2` / `sat -15`.

---

## 5. Estado

| | |
|---|---|
| Rama | **`feat/ciclo-dia-noche`** (5 commits) — `main` **sin tocar**, nada pusheado |
| Sello desplegado | **`rcv 2026-08-25 escala75`** |
| Player | `.data` **19.503.940** · `.wasm` **21.681.279** (LTO + preflights OK) |
| Bundles | **sin tocar** — todo el trabajo es de shaders y player |
| Validado en la tele | ciclo día/noche ✅ · niebla y tono ✅ (215 s, 0 errores, WASM 111 MB plano, FPS avg 32-35) · **`renderScale 0,75` ❌ pendiente** |
| Backup | `player-backup-prod-2026-08-25/` en el scratchpad (`.wasm` `a29eec57…`, `.data` `949eef58…`, sello `rcv 2026-08-21 urp`) |

⚠ La caja estaba hoy en **192.168.1.40** — la IP que un doc viejo daba como «otro Cast llamado
Comedor». El DHCP la mueve: **leer siempre el nombre**, nunca fiarse del ping ni del puerto.

---

## 6. Pendientes

- [ ] ⭐ **Validar `escala75` en la tele** (§1). 4 minutos.
- [ ] 🎨 **Paridad de grado con el móvil** — es lo que queda del «se ve más nítido en el
      teléfono» una vez descartada la resolución. TV: tonemapping + `sat +18`. Móvil: `bloom 1,2`
      / `sat -15`. **Se puede barrer con `GRADE` sin gastar builds.**
- [ ] 🐟 Dos bugs menores encontrados de paso, sin arreglar:
      · `sub_black` y `sub_coral` **no existen** y el bloque `?devtest=1` los usa en `DEV_SUBS`
        → la tecla S no hace nada en dos de cada cuatro pulsaciones.
      · **`change_sub` confirma un id inexistente sin avisar** (logueó `change_sub: sub_black` y
        no cambió nada). Con eso se dio por válida una prueba entera. Debería decir «id
        desconocido», como ya hace `ambient` con los modos.
- [ ] ❓ **Decidir si la sonda se queda en producción.** Cuesta ~9 líneas de log por cambio de
      ambiente. Hoy ha pagado su precio dos veces; yo la dejaría.
- [ ] 🤔 **¿Hemos ganado o perdido FPS hoy?** No se puede saber comparando `avg` entre sesiones.
      Si interesa: desplegar el backup del 21-ago, medir con el mismo protocolo y volver.
- [ ] Lo que ya venía de antes: **fondos del `.data` a Addressables** · **Fase 2 JWT** (repo
      móvil) · **editar una deco colocada no manda UPDATE** (repo móvil) · ❌ decimar mallas
      **descartado por el user**, no volver a proponerlo.

---

## 7. Deploy — sin cambios respecto a ayer

**Sólo `Build/` + `index.html`. NADA de `StreamingAssets/`** (el catálogo local no cuadra con R2
y dejaría la tele vacía). Comandos en `CLAUDE.md`.
