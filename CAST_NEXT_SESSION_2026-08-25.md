# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del **2026-08-24**. La anterior está en `CAST_NEXT_SESSION_2026-08-22.md`.
>
> **El ciclo día/noche por fin llega a las decos y a los peces.** Llevaba desde siempre sin
> tocarlas: sólo se apagaban el fondo y el agua. Está construido y verificado en el player real,
> pero **NO desplegado**. Encima lleva un cambio más (la arena, §1.1) escrito y compilado
> **pero sin construir**: el player del `ciclo2` ya no vale. **Mañana: elegir un valor,
> construir y desplegar** — el orden exacto está en §9.

---

## 1. Lo que se arregló

| # | Qué | Cómo se supo |
|---|---|---|
| 1 | **Las decos y los peces ignoraban el ciclo día/noche** | 8 capturas del ciclo: suelo y decos daban **92,09 y 47,98 en los OCHO momentos**, idénticos a dos decimales, mientras el fondo caía a 0,45× y el agua a 0,23× |
| 2 | **El atardecer era invisible** | Todo su efecto era el fondo un −11 %. Se arregló **solo**, sin tocar un valor (§3) |
| 3 | **`ambient` no reportaba nada por el canal Cast** | Era el único de los 11 UPDATE sin confirmación; la auditoría del 21-ago se lo saltó |
| 4 | **`sunLight` se resolvía sin filtrar por tipo** | `FindFirstObjectByType<Light>()` a secas, mientras `TankLightingController:147` sí filtra |
| 5 | `_modoManual` para que el reloj local no pise una orden del móvil | ⚠ **No arreglaba nada vivo** — ver §5 |

### 1.1 La arena: NO es paridad, es una mejora deliberada sobre el móvil

Se resolvió **leyendo el repo móvil**, que era lo que había que hacer desde el principio en vez de
pedirle al user que mirase el teléfono (aquí todo se valida en el Cast):

| Sospechoso | Qué dice el código del móvil | Veredicto |
|---|---|---|
| `SubstrateShadow` | `color.rgb *= lerp(1-_ShadowStrength, 1, shadowAtten)` y nada más | ❌ No multiplica por la luz |
| `TankNightOverlay` | Idéntico en los dos proyectos, pero vive en `z = BGZOffset - 0.01` = **4,99**: delante del fondo (z=5,0) y **detrás de todo lo demás** (decos en z≈2) | ❌ Sólo oscurece el telón |
| `DecorationPlacer.OnAmbientChanged` | Línea por línea igual que el de TV: sólo el fundido de biolum | ❌ No tiñe el suelo |

**En el móvil la arena también se queda encendida de noche.** Así que apagarla en TV **se aparta
de la app a propósito**, y la razón es que en la tele la arena es la superficie más grande del
encuadre —en el teléfono el tanque va pequeño y con UI alrededor— y con el fondo, las decos y los
peces ya apagados, se comía la noche entera.

Implementado en `DecorationPlacer.AplicarLuzAlSustrato` vía `_Color` de `Sprites/Default`, que es
lo que usa el suelo en TV (el `Shader.Find("Appquarium/SubstrateShadow")` de `BuildFloorMaterial`
no encuentra nada, porque ese shader no existe en este proyecto). **Sin shader nuevo.**

⚠ No puede ir por un global como las decos y los peces: `_Color` es una propiedad **del material**
y por tanto **gana al global**. Se escribe directamente, y `SetSubstrate` lo vuelve a aplicar
porque construye un material nuevo.

### La causa raíz del 1

`DecoLit` y `FishUnlit` calculan la luz con una **dirección hardcodeada** y un `_Ambient` que es
propiedad del *material*. Ni `RenderSettings.ambientLight` ni la intensidad del `Light`
direccional —que es lo que anima `AmbientModeController`— tenían **ningún camino** hasta ellos.

En el móvil las decos (`DecorationPlacer.cs:1654`) y los peces (`FishSpawner.cs:306`) usan
**`Universal Render Pipeline/Lit`**, que sí lee la escena. De ahí el desajuste que reportó el user.

**El suelo ya estaba a la par por casualidad:** TV llama a `Shader.Find("Appquarium/SubstrateShadow")`,
que **no existe en TV**, y cae a `Sprites/Default`. El `SubstrateShadow` del móvil tampoco
multiplica por la luz (sólo por `shadowAttenuation`). Los dos suelos ignoran el ciclo.

### El arreglo

Un global de shader que publica `AmbientModeController` en cada frame de la transición, en vez de
leer los globals de luz del SRP (en un pass CG sin `LightMode` no hay garantía de que el pipeline
los tenga bindeados).

⚠⚠ **Es un `_AqDecoDarken` / `_AqFishDarken`, o sea un DARKEN y no un TINT, y es deliberado.**
Un global que nadie publica vale **0**, no 1. Con un tint, cualquier escena sin
`AmbientModeController` renderizaría **todo en negro**; con darken el default significa «no toques
nada». **Fallar hacia lo de antes, no hacia lo roto.**

El factor se normaliza contra el día, así que **el día sale exactamente (1,1,1)** y la imagen
diurna validada en agosto no se mueve un píxel: la regresión no es improbable, es imposible por
construcción. Verificado: día 24,63 antes y 24,63 después, en las tres anclas.

---

## 2. ⚠⚠ LA TRAMPA CARA: el shader viene horneado en el bundle

**El primer build salió con todo esto "hecho" y las decos siguieron planas.**

Un material que sale de un **AssetBundle trae su PROPIA copia del shader**, compilada cuando se
construyó el bundle. Se sigue llamando `Appquarium/DecoLit`, así que la guarda de
`FixNonURPMaterials` lo dejaba pasar por «ya es device-safe» — pero era el bytecode del **19-ago**,
que no conoce `_AqDecoDarken`.

🧭 **REGLA: tocar `DecoLit`/`FishUnlit` NO se despliega con un build de player mientras el material
viva en un bundle.** O se reapunta el shader en runtime, o hay que reconstruir los 80 bundles
(68 min + 87 MB de subida).

Lo más útil del hallazgo: **`FishSpawner.cs:341-360` YA resolvía esto para los peces**, con este
mismo razonamiento escrito en su comentario desde hace meses:

> *«El bytecode compilado en el bundle puede no coincidir con el shader del proyecto actual.»*

A las decos nunca se les aplicó. `Shader.Find` devuelve la copia del **player** (los shaders dentro
de bundles no se registran ahí), así que reapuntar basta. Ahora `DecorationPlacer` lo hace y lo
**cuenta**: `AQUARIUM READY … | shaders reapuntados al player: 6`.

---

## 3. El atardecer se arregló solo

No se tocó ni un valor. Con las decos leyendo la fase, los colores que ya estaban en la escena
desde siempre producen:

| fase | factor RGB | brillo |
|---|---|---|
| día | 1,000 / 1,000 / 1,000 | 100 % |
| atardecer | **0,719 / 0,433 / 0,272** | 47 % |
| noche | 0,202 / 0,216 / 0,263 | 23 % |

Naranja cálido a mitad de brillo, que es lo que un atardecer debe hacer. **Nunca estuvo mal
configurado: estaba bien y no tenía dónde aterrizar.**

---

## 4. Medido en el player real (build `ciclo2`)

Cajas ceñidas al cuerpo de cada ancla. 1920×1080, luminancia absoluta.

| región | día | atardecer | noche | vuelta a día | noche/día |
|---|---|---|---|---|---|
| ancla negra | 24,63 | 15,26 | **8,73** | 24,63 | 0,355 |
| ancla oxidada | 15,65 | 10,00 | **5,61** | 15,65 | 0,358 |
| ancla vieja | 37,00 | 25,94 | **14,85** | 37,00 | 0,401 |
| heliopora (biolum) | 28,97 | 24,20 | **92,47** | 28,97 | 3,19 |
| fondo | 51,84 | 46,34 | 23,37 | 51,79 | 0,451 |
| suelo (control) | 91,97 | 91,97 | 91,97 | 91,97 | **1,000** |

La bioluminiscencia sigue intacta y **destaca más**, porque compite contra decos apagadas en vez de
contra decos a pleno día. El ciclo vuelve al valor **exacto** de partida: no hay deriva.

El instrumento nuevo lo confirma sin necesidad de píxeles:
```
luz: deco=1,00/1,00/1,00      ← día
luz: deco=0,72/0,43/0,27      ← atardecer (predicho 0,719/0,433/0,272)
luz: deco=0,36/0,29/0,28 …    ← noche, bajando
luz: deco=1,00/1,00/1,00      ← vuelta a día, exacto
```

---

## 5. 🧭 Reglas nuevas, todas pagadas hoy

1. ⚠⚠ **Instrumentar el MECANISMO, no sólo el EFECTO.** El primer build salió con el ciclo «hecho»
   y las decos planas, y no había forma de distinguir «el factor publicado es 1» de «el factor baja
   pero la deco no se entera» **sin gastar otro build de 55 min**. Había log de «cambié de modo» y
   ninguno de «estoy publicando este factor». Es el error de método más caro del día.
2. ⚠⚠ **Antes de dar por bueno un número, comprobar QUÉ hay dentro de la caja de medida.** Se
   predijo «anclas en noche ~10,38» y salió 37,68; no era el código, es que la caja abarcaba los
   **huecos de suelo entre las anclas**, que no se apagan. Con cajas ceñidas: 8,73. El número
   estaba mal medido, no mal calculado.
3. ⚠ **Un acuario vacío no da error, da números.** La primera verificación midió el suelo creyendo
   que medía anclas: el acuario no había cargado (§7) y las regiones devolvían fondo. **Mirar la
   captura antes de fiarse de la tabla.**
4. ✅ **Una sonda en el Editor cuesta 2 minutos y un build 55.** `TvDecoDarkenProbe` (temporal,
   borrado) renderizaba el mismo material variando sólo el global: demostró que el mecanismo
   funcionaba y mandó la investigación al sitio correcto. **Se descartó así una segunda hipótesis
   equivocada** (que `SetGlobalColor` gamma-convirtiera en Linear): `SetGlobalColor` y
   `SetGlobalVector` dan **idéntico** (0,2667 los dos). Ese falso positivo habría costado el tercer
   build.
5. **`localhost` y `127.0.0.1` NO son el mismo origin.** El Worker de los bundles permite el
   primero y no el segundo (`ALLOWED_ORIGINS` en `wrangler.toml`) → 403 al preflight CORS.
6. **`#dbg-panel` va con `display:none` en producción**, así que `innerText` está VACÍO: un arnés
   que espere `AQUARIUM READY` mirando el panel se queda colgado. Escuchar la consola.
7. **El overlay «Sender desconectado» reaparece solo.** Es un velo `rgba(4,14,26,0.72)` a
   z-index 400: sin quitarlo, toda luminancia medida es la del velo. No basta ocultarlo — hay que
   **sacarlo del DOM** antes de cada captura (`#rc-timer`.parentElement.remove()).
8. ⚠ **Leer una clase suelta no dice lo que hace el sistema.** Se reportó `autoFollowRealTime` como
   bug vivo mirando el `AmbientModeController` y los valores de la escena. No lo era:
   `TvSceneBootstrap.AplicarAjustesAmbiente()` ya pone `alwaysAmbient=true` **y**
   `autoFollowRealTime=false` desde `Awake`. El pestillo `_modoManual` que se dejó es un cinturón
   sobre unos tirantes que ya estaban puestos.

---

## 6. ⏭ Lo que queda — DECISIONES DEL USER

- [x] 🎨 **El suelo — RESUELTO Y ESCRITO, falta construirlo.** Se comprobó **leyendo el repo
      móvil**, no mirando el teléfono: allí la arena **tampoco** se apaga (§1 y §6.1). O sea que
      estábamos a la par y la pregunta era de gusto. **El user decidió apagarla igualmente**
      (2026-08-24). Ya está implementado y compilado; falta el build.
- [ ] ❓ **Elegir el valor de `sueloSustratoNoche`** (hoy **0,45**). Se le pasaron al user cuatro
      candidatos simulados sobre la captura real de noche (0,45 · 0,30 · 0,18 · sin tocar).
      **Poner el elegido ANTES de lanzar el build**, en `AmbientModeController`.
- [ ] 🚀 **CONSTRUIR Y DESPLEGAR** (queda para el 25-ago). El player del `ciclo2` ya no vale: hay
      código nuevo encima. Ver §7 para el comando de deploy y §9 para el orden completo.
- [ ] 📺 **Validar en la tele.** Todo lo de hoy está medido en Chrome de escritorio con
      SwiftShader. Para *lógica de render* vale (mismo shader, números deterministas), pero **no
      dice nada del coste de GPU en el Mali-G31**. Falta una tanda con el protocolo del 19-ago.
- [ ] Lo que ya venía de antes: **las mallas** ❌ **descartado por el user (24-ago)** — «si va a
      perder calidad no lo quiero», no volver a proponerlo · **fondos del `.data` a Addressables** ·
      **Fase 2 JWT** (repo móvil) · **editar una deco colocada no manda UPDATE** (repo móvil).

---

## 7. ⚠⚠ El deploy: SÓLO `Build/` — el catálogo local ya NO cuadra con R2

El build de player **regeneró el catálogo local** con hashes de bundle distintos a los de R2:

| | fish_banggai_cardinalfish | deco_anchor |
|---|---|---|
| catálogo de **R2** | `724dbae8…` | `858307d4…` |
| catálogo **local nuevo** | `b5a9bb42…` | `9c21c45d…` |

**Si se sube `StreamingAssets/`, los 80 bundles dejan de encontrarse** y la tele sale vacía. Es
exactamente la trampa que avisa `CLAUDE.md`, y hoy se vio en vivo: la primera verificación local
falló con `RemoteProviderException` en 7/7 bundles por esto.

```powershell
# SOLO el player. NADA de StreamingAssets/. NADA de --delete.
aws s3 sync webgl-output/Build/ s3://appquarium-tv/Build/ `
  --profile r2 `
  --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
  --exclude "*" `
  --include "webgl-output.data" --include "webgl-output.wasm" `
  --include "webgl-output.framework.js" --include "webgl-output.loader.js" `
  --cache-control "public, max-age=3600"
# index.html aparte, con boto3 (ver CLAUDE.md), max-age=60
```

⚠ Si algún día hace falta que el catálogo local y R2 vuelvan a cuadrar, la vía es un **New Build**
de Addressables + redespliegue de los 80 bundles, no subir el catálogo suelto.

---

## 8. Los tres suelos del ciclo

Cada familia conserva un brillo mínimo distinto en la noche cerrada. El cálculo puro daría ~0,03
para todo — fiel a la física y visualmente inservible.

| | campo | suelo | brillo en noche | por qué |
|---|---|---|---|---|
| Decos | `sueloDecoNoche` | 0,18 | 21,6 % | Son el decorado: pueden quedar en silueta |
| Peces | `sueloPecesNoche` | 0,35 | 37,9 % | Son el protagonista; con el suelo de las decos la noche se los comía |
| Arena | `sueloSustratoNoche` | **0,45** ❓ | 47,4 % | La superficie más grande del encuadre; bajarla mucho deja un agujero negro |

⚠ **El 0,45 está sin validar** — es el valor que se propuso de palabra. Ver §6.

---

## 9. Orden para mañana (25-ago)

1. **Elegir `sueloSustratoNoche`** con la hoja de candidatos y ponerlo en
   `AmbientModeController`.
2. **Bump del sello** en `Assets/WebGLTemplates/CastReceiver/index.html` (`ciclo2` → `ciclo3`).
   Sin sello nuevo, la A/B contra el device no vale.
3. **Build**: `-executeMethod TvProdBuild.BuildProd` (~55 min). Preflights de audio y auth van
   solos.
4. **Verificar en local ANTES de subir**, con el arnés descrito en §10: `Build/` nuevo +
   `StreamingAssets/` **bajado de R2**. Comprobar en el log `luz: … arena=…` y
   `shaders reapuntados al player: N` (con 6 decos debe dar 6).
5. **Desplegar sólo `Build/` + `index.html`** (§7). NADA de `StreamingAssets/`.
6. **Validar en la tele** con el protocolo del 19-ago: FPS y memoria. Es lo único que el PC no
   puede decir.
7. Merge a `main` **con confirmación del user**, y push.

## 10. Estado

| | |
|---|---|
| Rama | **`feat/ciclo-dia-noche`** (2 commits) — `main` **sin tocar** |
| Sello del build | **`rcv 2026-08-24 ciclo2`** |
| Player construido | `.data` **19.513.337** · `.wasm` **21.673.888** (LTO aplicado, preflights 3/3 + auth OK) |
| **Desplegado en R2** | **NO.** Producción sigue con el player del 21-ago (`rcv 2026-08-21 urp`, `.data` 19.511.185 · `.wasm` 21.670.726) |
| Bundles | **Sin tocar.** El cambio es de shader del player; los 80 bundles siguen igual |
| Backup | `player-backup-2026-08-21/` en el scratchpad, con md5 (`.wasm` `a29eec57…`, `.data` `949eef58…`) |

### Otras dos cosas cerradas hoy

- ✅ **`max-age` restaurado a 3600** en `.data`, `.wasm`, `.framework.js` y `.loader.js`. Llevaban
  desde el 21-ago en **60** (se bajó para iterar y no se restauró): cada espectador se
  re-descargaba **~41 MB cada minuto**.
- ✅ **`main` empujada a `origin`** (`21f2132..9f4daaf`, 17 commits). Se comprobó antes que el
  token de los bundles no viajaba en el diff — sólo aparecen las líneas del `.gitignore`.

### El arnés de verificación, para repetirlo

`Tools/` no lo lleva (era temporal), pero el método es el que hay que repetir:

1. Servir en **`localhost:3001`** un directorio con el **`Build/` nuevo** + el
   **`StreamingAssets/` bajado de R2** — eso replica producción tras subir sólo `Build/`.
2. Puppeteer con **User-Agent de Chrome normal** (Cloudflare bloquea `HeadlessChrome` con
   `error code 1010` antes de llegar al Worker).
3. Esperar `AQUARIUM READY` **por consola**, no por `#dbg-panel`.
4. **Sacar el overlay del DOM** antes de cada captura.
5. Mandar los UPDATE con
   `window.unityInstance.SendMessage('CastReceiver','OnMessageReceived', …)`.
