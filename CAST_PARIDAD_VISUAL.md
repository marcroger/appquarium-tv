# Paridad visual TV ↔ móvil — colores lavados y sombras sobre el fondo

> Abierto el **2026-08-21** a partir de dos observaciones del user en la tele, con el acuario
> real casteado desde el móvil. **Nada de esto está arreglado ni medido en la tele todavía**:
> este doc recoge lo que se ha comprobado leyendo el proyecto y deja el protocolo listo.
>
> ⚠ Todo lo del repo móvil que hay aquí se leyó **en sólo lectura**. No se ha tocado nada.

---

## 1. Lo que se ve (reportado por el user)

1. **Los colores no se ven tan nítidos ni tan bonitos como en la app.** El fondo en concreto se
   ve *«prácticamente blanco y negro»*.
2. **Las sombras de las decos caen sobre el FONDO** (el telón de atrás del todo), no sobre el
   suelo. De los peces no se fijó. Duda abierta del user: *«no sé si deberían mostrarse ahí, o
   igual sí, al ser una pecera»*.

---

## 2. Lo que YA está comprobado (leído del proyecto, no medido en pantalla)

### 2.1 🎯 El grado de color de TV y el del móvil son DISTINTOS a propósito

No es «lo mismo con menos calidad»: son dos ajustes diferentes. Valores **serializados en cada
escena** (no los defaults del script):

| | Móvil (`AquariumScene.unity`) | TV (`TvScene.unity`) |
|---|---|---|
| **Bloom** | **1,2** (y el script móvil **no tiene interruptor**: siempre activo) | **OFF** (`enableBloom: 0`) |
| **Tonemapping** | **no existe en el script móvil** | **Neutral, ON** |
| Saturation | **−15** | **+18** |
| Contrast | (no expuesto) | +10 |
| Post exposure | 0,1 | 0,05 |
| Vignette | 0,095 | 0,18 (default del script; sin override en escena) |
| Color filter | `(0.95, 0.98, 1.00)` | `(0.95, 0.98, 1.00)` — **idéntico** |

🧭 **Lo importante y lo contraintuitivo:** el móvil está **desaturado (−15)**, no saturado. Lo
que le da el aspecto vivo es el **bloom a 1,2**, que hace florecer las zonas brillantes. La TV
lo tiene **apagado** — a propósito, fue una de las tres palancas que la hicieron ir fluida en la
Xiaomi (bloom OFF + renderScale 0,7 + targetFrameRate 30) — y compensa subiendo saturación a
+18. **Subir saturación no devuelve el glow**: da color plano. Y encima la TV mete un
tonemapping Neutral que el móvil no tiene, que comprime los altos.

⚠ **Hipótesis, no medición:** que esto explique el «blanco y negro» del fondo es lo más probable,
pero no está comprobado en pantalla. Ver §4 antes de tocar un solo valor.

### 2.2 🎯 Los fondos van a la TV a 1/16 de píxeles

El PNG de origen es **el mismo fichero** en los dos repos (`bg_abyss.png`, 1.935.615 bytes en
ambos). Lo que cambia es el import:

| | maxTextureSize | override |
|---|---|---|
| Móvil (Android) | **2048** | `overridden: 0` |
| TV (WebGL) | **512** | `overridden: 1` |

512 frente a 2048 es **4× por eje = 16× menos téxeles**. Encima:

- `textureCompression: 1` + `compressionQuality: 50` → DXT en WebGL. Sobre un degradado suave,
  DXT1 hace **banding** y desplaza tonos: es justo el peor caso para un fondo de agua.
- `TvSceneBootstrap.cs:104` fuerza **`renderScale = 0.7`**: el frame entero se renderiza al 70 %
  y se reescala. En una tele 1080p el fondo acaba siendo 512 px estirados a 1920.
  ⚠ **Corregido el 21-ago:** el móvil **también** renderiza a escala reducida
  (`Mobile_RPAsset.asset: m_RenderScale: 0.8`), así que la diferencia real es 0,7 vs 0,8 — un
  12 %, no «la TV a 70 % contra el móvil a 100 %». Esta palanca pesa MUCHO menos de lo que
  parecía; el grueso de la nitidez perdida está en el **512 vs 2048** del fondo. (El
  `targetFrameRate` sí coincide: 30 en los dos, 24 en modo ambiente.)

El override de 512 no es un accidente: viene de `★ Reduce TV Textures`, que se usó para bajar el
tiempo de compresión del build de WebGL (documentado en `CLAUDE.md`: «512px → 4× menos tiempo»).
Se pagó nitidez por horas de build.

### 2.3 ✅ Descartado: el color space

`m_ActiveColorSpace: 1` (**Linear**) en **los dos** proyectos. No hay desajuste de gamma por ahí.
Los fondos se importan con `sRGBTexture: 1`, que es lo correcto.

### 2.4 ✅ Descartado (con matiz): el overlay de noche

Hubo un bug que dejaba quads de noche apilados a alpha 0,75 «para siempre» y oscurecía el acuario
en escalera hasta negro. **Se arregló el 2026-08-15** (`TankBackground.InitializeBackground`
ahora destruye también `TankNightOverlay`). ⚠ Matiz: eso arregla la **acumulación**, pero un
overlay de noche legítimo sigue existiendo. Si en la captura de comparación el modo ambiente no
es el mismo en móvil y en TV, la comparación no vale nada.

### 2.5 Por qué las sombras acaban sobre el fondo

La sombra de deco es la malla real aplanada contra un plano horizontal de mundo
(`Appquarium/PlanarShadow`, `_FloorY`), y ese plano sale de:

```
floorY = max( FloorSurfaceY(deco.z), FloorSurfaceY(0) ) + 0.02
FloorSurfaceY(z) = _floorMeshBaseY + t·_floorMeshRiseY     // t = 0 delante, 1 al fondo
```

O sea: **cuanto más «al fondo» está la deco, más ARRIBA en pantalla cae su sombra**. Eso es la
convención 2.5D de esta escena — el suelo es un **sprite vertical** y «más lejos» se dibuja «más
alto» (la misma trampa que tuvo las sombras invisibles hasta el 11-ago). La zona de suelo ocupa
sólo el **20 % inferior** del tanque (`ComputeZFromY`), así que una deco colocada atrás proyecta
su sombra por encima de esa banda, y detrás de todo eso lo único que hay es el fondo
(`sortingOrder −100`).

**No es un bug de código: es la consecuencia directa de la geometría.** Que *deba* verse así es
una decisión de arte. En una pecera real, con luz frontal, la sombra sí cae sobre la pared del
fondo — pero si se lee como «pegatina flotando», habrá que decidir entre:

- recortar la sombra a la banda de suelo (deja de haber sombra en decos del fondo),
- atenuarla con la profundidad (más al fondo → más transparente), o
- dejarla como está.

⚠ Ojo con el efecto secundario: `TvFishShadows` usa `GetFloorSurfaceY(z)` **por el mismo camino**,
así que lo que se toque para decos afecta a los peces salvo que se separe explícitamente.

---

## 3. La lista de palancas, con su precio

⚠⚠ **Todas cuestan un rebuild de player (~55 min)**: los valores de post-proceso están
serializados en `TvScene`, el `renderScale` está en código, y los fondos viven en
`Assets/Resources/` → todo va horneado en el `.data`/`.wasm`. No hay ninguna que se despliegue
sólo subiendo un fichero a R2.

| Palanca | Qué devuelve | Qué cuesta |
|---|---|---|
| **Bloom ON** (móvil: 1,2) | Es lo que hace que el móvil se vea «vivo» | FPS. Se apagó por eso; la Xiaomi va a 37 fps medios con 25 peces |
| **Tonemapping Neutral OFF** | Altos sin comprimir, como el móvil | Riesgo de highlights quemados (por eso se puso Neutral, no ACES) |
| **Saturación +18 → −15** | Igualar el grado del móvil | Sólo tiene sentido **junto** al bloom; sola, apaga el resultado |
| **Fondos 512 → 1024** | Nitidez del fondo (4× téxeles) | ~+2-3 MB en el `.data` (hoy 15,94 MB) y más tiempo de build |
| **renderScale 0,7 → 0,8** | Poco: el móvil está en **0,8**, o sea un 12 % de diferencia | Fill-rate: la palanca más cara en FPS de las tres del 19-jun. **Mala relación coste/beneficio** |

---

## 4. ⚠ Protocolo de comparación — hacerlo ANTES de tocar valores

Idea del user, y es la correcta: **capturar la app y castear exactamente ese estado**, comparando
en vez de opinando. Este proyecto ya tiene todas las piezas menos la del móvil:

1. **Mismo estado en los dos lados.** Castear desde el móvil el acuario real (no
   `cast-headless.js --fish N`, que manda un estado sintético y **no** reproduce el fondo ni el
   suelo que el user está viendo). El fondo y el suelo tienen que ser los mismos presets.
2. **Mismo modo ambiente** (día/noche/atardecer) en ambos — ver §2.4, o la comparación no vale.
3. **Captura de la TV:** `adb exec-out screencap` contra la Xiaomi captura el canvas WebGL
   (validado; DevTools no existe en ese build). Confirmar en el panel de diagnóstico la línea
   `BG: <id> IMAGE shader=… tex=WxH` — **ahí se lee el tamaño real de la textura en el device**,
   que debería salir 512 y es la prueba directa de §2.2.
4. **Captura del móvil:** la hace el user (screenshot del teléfono). No hace falta tocar el repo
   móvil para esto.
5. **Comparar con números, no a ojo:** media y desviación de saturación por regiones (fondo,
   suelo, deco, pez) y luminancia **absoluta** de cada región.

🧭 **Regla de oro que ya costó un diagnóstico falso** (la bioluminiscencia del 16-ago): medir el
valor **absoluto** de la región, nunca su contraste contra un fondo que también cambia. Si se
compara «coral contra agua» y el agua se oscurece, el ratio sube solo y parece que funciona.

⚠ **Trampa de escala:** las dos capturas tendrán resoluciones distintas (teléfono vs 1080p).
Comparar histogramas normalizados por región, no píxel a píxel.

💡 **Recomendación fuerte para no gastar 55 min por variante:** antes de la tanda, añadir al
mensaje `DIAG` del receiver la posibilidad de cambiar **en caliente** bloom / tonemapping /
saturación / contraste (y, si se puede, `renderScale`). Con **un solo rebuild** se pueden probar
todas las combinaciones en la tele y capturar cada una. Sin eso, cada variante es un build.

---

## 5. Estado

- [ ] Nada tocado. Ninguna medición hecha en pantalla todavía.
- [ ] Decisión del user pendiente en dos frentes: **cuánto FPS está dispuesto a pagar** por
      acercarse al look del móvil, y **qué debe hacer la sombra** de una deco del fondo.
