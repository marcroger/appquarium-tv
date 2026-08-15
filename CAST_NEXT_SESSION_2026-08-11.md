# ▶▶ EMPEZAR AQUÍ — sesión 2026-08-11 · Cast disconnect

> Escrito al cierre del 2026-08-10. Contexto histórico en `CAST_DISCONNECT_INVESTIGATION.md`
> y `CAST_NEXT_SESSION_2026-07-28.md`.
> **El acuario completo aguanta la ventana entera de forma repetible. Falta tu validación visual.**

---

## 1. Qué pasó ayer, en cinco líneas

- Se validó el fix de julio con el **acuario real** (no la escena vacía): 12 peces cargados de R2
  aguantan **660 s**. La escena vacía también.
- Pero al añadir **6 decos** se caía **la mitad de las veces** (150 s / 660 s con configuración
  idéntica). Las decos eran el cuello de botella, no los peces.
- Causa: los 21 GLB llevan **texturas PBR que ningún shader lee**. `DecoLit` declara una sola
  textura (`_MainTex`) y `FixNonURPMaterials` solo transfiere esa.
- Se quitaron: **181,4 → 67,3 MB (−63 %)**, cero cambio visual por construcción.
- Resultado: heap WASM **191 → 159 MB** y **3 de 3 tandas** aguantando los 660 s.

## 2. ⏭ LO ÚNICO QUE FALTA POR MI PARTE: nada. Por la tuya: mirarlo

**Verificar en la tele que las decos se ven bien.** Es lo único que no puedo comprobar yo.

```bash
FISH=12 bash Tools/cast-run.sh 2 revision-visual
```

Castea el acuario completo durante 11 minutos. Mira el ancla, los dos corales, la roca, la concha
y la estrella de mar. **Si algo sale magenta, plano o sin color**, revertir es inmediato:

```bash
git checkout Assets/ThirdParty        # devuelve los 21 GLB originales
# y reconstruir bundles + re-desplegar (§5)
```

El razonamiento de que no cambia nada es sólido —el runtime ya descartaba esas texturas hoy— y el
log no dio un solo error de material en 5 tandas. Pero eso no es lo mismo que verlo.

## 3. Los números medidos (todo mismo device, mismo método)

| Escenario | `.wasm` | Heap WASM | Pico RSS | Duración |
|---|---|---|---|---|
| Producción original (julio) | 44,2 MB | — | 794 MB | 148-274 s ❌ |
| Recortado · escena vacía | 25,4 MB | 64 MB | 671 MB | 660,6 s ✅ |
| Recortado · 12 peces | 25,4 MB | 64 MB | 638 MB | 660,9 s ✅ |
| Recortado · 12 peces + 6 decos **antes** | 25,4 MB | **191 MB** | 664-685 MB | **150,4 s ❌ / 660,6 s ✅** |
| Recortado · 12 peces + 6 decos **después** | 25,4 MB | **159 MB** | 654-678 MB | **660,7 · 661,1 · 661,2 s ✅✅✅** |

⚠ **3/3 frente a 1/2 es alentador pero NO concluyente estadísticamente.** La presión del sistema
sigue en ~20-21 %, la misma banda donde ya vimos morir una tanda. Hay más holgura, no inmunidad.
**Lo probado son 6 decos**; un tanque con 15 volvería al filo.

## 4. Las dos palancas que quedan (por si hace falta más margen)

| Palanca | Ganancia | Coste de calidad |
|---|---|---|
| **Texturas a DXT** (extraerlas del GLB a assets sueltos) | ~5,3 → ~0,7 MB cada una | prácticamente nulo — es lo estándar, y los peces ya pasan por ahí |
| **Mallas 100k → ~8k triángulos** | ~3,2 MB y 600k tris de GPU | **geometría real → decisión del user** |

**Confirmado que las texturas van sin comprimir (RGBA32, no DXT)**, por aritmética sobre medidas
reales: `acropora` pasó de 12,49 MB (3 texturas) a 5,91 MB (1) ⇒ **3,29 MB por textura**. Con DXT1
serían 0,7 MB con mipmaps y LZ4 apenas las comprime más. Con RGBA32 son 5,3 MB → LZ4 ≈ 3,3 MB. Encaja.
Causa: el ScriptedImporter de GLTFast **no expone `maxTextureSize` ni formato**, así que el override
de WebGL solo llega a las texturas sueltas.

Las mallas están todas clavadas en **~100.000 triángulos** (fotogrametría). El acuario renderiza a
960×600 con renderScale 0,7; un coral ocupa ~150×150 px ⇒ **~4 triángulos por píxel**.
La excepción que lo confirma: `lambis_shell` tiene 12.498 triángulos y pesa 2,9 MB frente a 8-10 MB.

## 5. Estado del entorno al cerrar

| Cosa | Estado |
|---|---|
| R2 `/bundles/` | **21 bundles nuevos**, verificados byte a byte (21/21). Los viejos siguen ahí (nombre distinto por hash), no se borró nada |
| R2 `/StreamingAssets/aa/catalog.{bin,hash}` | **actualizado** — sin esto el player seguiría cargando los bundles viejos |
| R2 `/Build/webgl-output.*` | el build recortado del 27-jul (`.wasm` 25.430.429). Sin tocar |
| R2 `/index.html` | ⚠ sigue siendo el receiver de **DIAGNÓSTICO** |
| Caja Xiaomi | **192.168.1.33**, `stay_on_while_plugged_in 7` activado (persiste tras reinicio) |
| Player | **NO rebuildeado** — no hizo falta, no cambió C# |

### ⚠ Tras reiniciar la caja, adb pide autorización otra vez
Sale un diálogo en la TV. Hay que aceptarlo **marcando «permitir siempre»** o cada reinicio corta la
tanda. `cast-run.sh` ahora lo detecta y lo dice por pantalla en vez de abortar en seco.

## 6. Cambios en el repo (sin commit, sin push)

| Fichero | Qué |
|---|---|
| `Assets/ThirdParty/**/*.glb` (21) | texturas PBR muertas eliminadas. **Revertible con `git checkout`** |
| `Tools/cast-headless.js` | `--fish N` → manda un `INIT` con `TvAquariumState` real (12 especies, 6 decos, `bg_tropical`, `sub_sand`) |
| `Tools/cast-run.sh` | `FISH=N`; reintenta el shell adb 2 min distinguiendo `unauthorized`; reaplica stay-awake; mata senders huérfanos y aborta si el `sender.log` sigue abierto |

Script del recorte: `scratchpad/strip-unused-textures.mjs` (usa `@gltf-transform/core`).

### ⚠⚠ Dos cosas que se pierden solas

1. **Un `SyncFromMobile` devuelve los GLB gordos.** `SYNC_NOTES.md:58` lista `Corals`,
   `GreekColumns`, `GreekStatues` y `Shells` entre lo que se copia del móvil. Tras cada sync hay que
   volver a pasar el recorte.
2. **El Code Optimization del WASM no está en git** (`Library/EditorUserBuildSettings.asset`). Si se
   borra la Library vuelve a `BuildTimes` y el problema regresa. Comprobar con
   `Appquarium TV → 📏 Ver Code Optimization del WASM` antes de cada release.

## 7. Trampas del harness que costaron mediciones ayer

- **Sin `--fish N` se mide una escena VACÍA.** `cast-headless.js` solo mandaba `RUNG_CONFIG`; Unity
  arrancaba y se quedaba esperando. Señal en el log: `Unity READY — flushing 0 msgs`. La instrucción
  del handoff de julio («medir el acuario real con `cast-run.sh 2`») **era inejecutable**.
- **La clave Addressable de una deco sale del `instanceId`, no del `itemId`.**
  `TvSceneBootstrap.ParseDecoItemIds()` (~línea 416) ignora `itemId` y le quita al `instanceId` el
  sufijo `_N`. Un `instanceId` sin guion bajo ⇒ `InvalidKeyException` y **0 decos**, pero los peces
  cargan igual y parece que va bien.
- **Dos senders a la vez entrelazan el `sender.log`.** En Windows `rm -rf` no borra un fichero
  abierto, así que un huérfano y el nuevo escriben encima del mismo. Señal: **2 bloques FIN** y
  líneas partidas a mitad. Ya hay guardia, pero comprobar siempre:
  `grep -c "DURACIÓN DE SESIÓN" sender.log` **debe dar 1**.
- **El `settings.json` que genera el build trae `m_DisableCatalogUpdateOnStart: false`** y una URL con
  doble slash. `TvBuildPostprocess` los parchea tras cada build de player. **Nunca subir el crudo de
  `Library/`** — ese `false` crashea el WASM. Ayer solo se subieron `catalog.bin` y `catalog.hash`.

## 8. Corrección a la teoría de julio

La nota decía: «presión sostenida por debajo del **25 %** → la plataforma se lleva la app en
150-275 s». **Falso tal cual está escrito.** El acuario vive estable en **22-24 %** y estuvo ~450 s
por debajo del 25 % sin que lo mataran. Lo que mata es la presión **profunda**: el build viejo se
quedaba clavado en **6-10 %**. El indicador de peligro real es acercarse a ~10 %, no a 25 %.

## 9. Pendientes que vienen de antes

- ⚠ Desplegar un **receiver de producción limpio** — el `index.html` vivo en R2 es el de diagnóstico.
- ⚠ **Discrepancia**: la memoria decía que `FishUnlit`/`PlanarShadow` son CG legacy. `DecoLit` **sí**
  es CG legacy (verificado ayer). Los otros dos, sin comprobar.
- ⚠ **Contradicción**: el research dice que fijar `maxInactivity` es contraproducente; choca con
  `CLAUDE.md` y la memoria del proyecto.
- Medir **sin reinicio previo** — las tandas arrancan con 41-45 % libre; una caja de uso diario tiene
  menos.
