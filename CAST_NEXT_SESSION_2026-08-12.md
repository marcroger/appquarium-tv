# ▶▶ EMPEZAR AQUÍ — sesión 2026-08-12

> Escrito al cierre del 2026-08-11. La sesión anterior está en `CAST_NEXT_SESSION_2026-08-11.md`.
> **Estabilidad de Cast: CERRADA (4/4 a 660 s). Lote visual: hecho y verificado en el Editor,
> pendiente de un build de player para verlo en la tele.**

---

## 1. Lo primero que toca mañana

**Lanzar el build de player.** Todo el lote visual son shaders y C#: no llega a la TV sin él.

```
1. Unity → Appquarium TV → 📏 Ver Code Optimization del WASM     ← DEBE decir DiskSizeLTO
2. File → Build Settings → Build   (output en webgl-output/, 1-3 h)
3. Deploy del player a R2 (ver CLAUDE.md, sección "Solo player rebuild")
4. CAST_IP se descubre solo:  FISH=12 bash Tools/cast-run.sh 2 revision-visual
```

⚠ **Paso 1 no es opcional.** El Code Optimization vive en `Library/EditorUserBuildSettings.asset`,
que **no está en git**. Si se borró la Library, habrá vuelto a `BuildTimes`, el `.wasm` volverá a
44 MB y con él el disconnect que costó dos meses cerrar.

---

## 2. Qué se hizo el 2026-08-11

### 2.1 Validación de estabilidad — 4/4

Cuarta tanda del acuario completo (12 peces + 6 decos): **659,3 s, 0 crashes**, pico RSS 664,9 MB.
Idéntica a las tres del día anterior. La investigación del disconnect queda cerrada.

Hallazgo lateral: el **receiver de diagnóstico se comía ~35 MB de Native Heap** (26,6 MB con el
panel oculto frente a 61-67 con él visible). Repinta 40 divs por línea de log y el compositor los
rasteriza. El instrumento añadía presión al sistema que medía. Una sola muestra, pero la mecánica
cuadra: **razón de más para desplegar el receiver limpio**.

### 2.2 Las sombras de las decos NUNCA se vieron — arreglado

`PlanarShadow` aplastaba todos los vértices a un plano **horizontal** (`wp.y = _FloorY`). La cámara
del acuario es **ortográfica y mira en horizontal** (`ortho=True pos=(0,0,-10) rot=(0,0,0)`), así
que un plano horizontal se ve **de canto** ⇒ proyecta una línea de grosor cero ⇒ **0 píxeles**.
El shader se ejecutaba —renderer activo, visible, alpha 0,5— y no pintaba nada.

Fallaba igual en el Cast device **y en Chrome de escritorio**: nunca fue cosa del device. Llevaba
roto desde el 2026-06-23; se desplegó y nunca se verificó en pantalla.

La escena es **2.5D**: el suelo es un *sprite vertical* con la arena pintada en perspectiva, no
geometría tumbada. Ahora se aplana **hacia** el suelo sin colapsar (`_Flatten = 0,22`).

Tres problemas más que solo aparecieron al probarlo:

| Síntoma | Causa | Arreglo |
|---|---|---|
| Sombra moteada | ~100.000 triángulos aplanados se solapan cientos de veces por píxel y cada capa oscurece otra vez | **Stencil**: cada píxel se pinta una vez. De paso, mucho menos blending |
| Franja negra cruzando la roca | la sombra conservaba la Z de la deco y ganaba el ZTest en su cara frontal | `_ZPush = 0,35` la manda detrás |
| Sombra demasiado floja | con el stencil ya no se apila | alpha 0,50 → **0,78** |

**Descartado con experimento, no por teoría:** la cola de render NO era la causa. Se probó
2999 → 3000 y seguía sin verse.

**Dos intentos fallidos, anotados en el código para que nadie los repita:**
- Pegar la sombra a `bounds.min.y` ⇒ la propia deco la tapa entera.
- `bounds.min.y − margen` ⇒ la roca tiene la base **enterrada** (bounds hasta −3,80 con el suelo
  en −3,13) y su sombra se iba al sótano.
- La referencia correcta es la **superficie del suelo**; el grosor lo da `_Flatten`.

### 2.3 Sombra de los peces — nueva

Los peces nunca la tuvieron aquí. **En el móvil sí**, pero por *shadow mapping real*, que en el Cast
no existe: al pez se le fuerza `FishUnlit` (CG legacy, sin pase ShadowCaster) y el suelo es un
sprite que no puede recibir sombras. No es que falte código: la técnica del móvil no existe aquí.

Solución: **clonar el `SkinnedMeshRenderer` compartiendo malla y HUESOS**. El clon se deforma solo
con la animación (skinning en GPU, sin coste de CPU ni memoria extra) y `PlanarShadow` aplana esos
vértices ya skinneados. **Silueta de pez real, no un blob.**

⚠ **Cuesta un draw call skinneado por pez. SIN MEDIR en el device.** Si penaliza, el blob elíptico
sigue en el código como reserva y se cambia con una línea.

Dos callejones sin salida recorridos:
- **`Sprites/Default` NO pinta materiales creados en runtime.** Probado a lo bestia: quad **rojo
  opaco**, renderer activo, dentro del viewport, forzado a `sortingOrder 500` y cola 4000 —el
  último de todos en dibujarse— ⇒ **0 píxeles rojos**. No era tapado ni orden. De ahí
  `Appquarium/FishShadow`, CG legacy propio.
- La sombra caía en el **borde inferior de la pantalla** (viewport y = 0,02) por no copiar el
  `Max(FloorSurfaceY(z), FloorSurfaceY(0))` que `DecorationPlacer.UpdateShadow` ya hacía.

### 2.4 Resto del lote visual

- **El ancla no recibía luz**: venía con `Appquarium/FishUnlit` y `FixNonURPMaterials` la dejaba
  pasar por "ya es device-safe". Salía como silueta negra mientras roca y coral tenían volumen.
- **`DecoLit` con ambiente hemisférico** (más luz por arriba, menos por abajo) en vez de un `0,45`
  constante que lavaba el relieve de unos corales con 100.000 triángulos. `_Ambient` → **0,32**.
- **Fuera el overlay amarillo** de debug sobre el acuario, tras un flag del inspector.
- **Comida a un tercio**: el pellet central medía 0,45-0,52 unidades con un pez de ~1 ⇒ era medio
  pez, ~25 px en la Xiaomi. Ahora ~0,16 (~8 px).

---

## 3. La herramienta que hizo esto posible

`Assets/Editor/TvShadowDiag.cs` — **bucle de iteración de 1 minuto en el Editor en vez de un build
de 3 horas**. Menús (ASCII a propósito: el bridge MCP falla con emojis en la ruta):

```
Appquarium TV/ShadowDiag 0 Play         entra en play mode
Appquarium TV/ShadowDiag 0b Stop        sale
Appquarium TV/ShadowDiag 1 Inject       inyecta un acuario de prueba (3 decos + 1 pez)
Appquarium TV/ShadowDiag 2 Dump         vuelca suelo, sombras, decos, cámara y viewport
Appquarium TV/ShadowDiag 3 Shot         captura el Game view a _shadowdiag/shot.png
Appquarium TV/ShadowDiag 4 RegistrarShader
Appquarium TV/ShadowDiag 5 Feed         suelta comida a mano (el auto-feed va cada 4 min)
```

Sin el **volcado numérico** no se habría encontrado nada de esto: el `[cam] ortho=True rot=(0,0,0)`
fue lo que destapó la causa raíz, y el `viewport y=0,02` lo que explicó la sombra del pez.

---

## 4. Trampas del entorno que costaron tiempo

- **La IP de la caja la da el DHCP y CAMBIA.** El 10-ago estaba en `.33`, el 11 en **`.34`**. Peor:
  la `.33` seguía respondiendo a **ping** con otro cacharro detrás, así que parecía viva y el script
  esperó 7 minutos a una caja que no estaba. **El ping no vale como señal**; el 8008 abierto +
  `eureka_info` diciendo "xiaomi", sí. `cast-run.sh` ya lo descubre solo.
- **Unity en segundo plano no procesa la cola del bridge MCP.** Y si hay un **diálogo modal**
  esperando (p. ej. "¿guardar la escena?"), por fuera se ve "responde, ocioso" y todas las llamadas
  dan timeout. Si el bridge no contesta: mirar si Unity tiene una ventana esperando.
- **Un script nuevo no existe para Unity hasta que lo importa.** Sin `.meta` no hay menú, y
  `recompile_scripts` no basta: hace falta `Assets/Refresh`.
- **`ApplyModifiedProperties` + `SaveAssets` NO persiste ProjectSettings.** El registro de
  `Appquarium/FishShadow` en Always Included se quedó en memoria y el `.asset` del disco seguía
  igual. Hizo falta `SetDirty` + `SaveAssetIfDirty`. **Comprobar siempre con grep del GUID**: si
  falla, el shader se strippea del build y las sombras funcionan en el Editor pero **no en la TV**.

---

## 5. Estado del repo

**6 commits nuevos en `feat/netflix-architecture`, locales, sin push. Working tree limpio.**

```
355297f  fix(tv): la comida se veia enorme -- pellets a un tercio
13c8608  fix(tv): las sombras de las decos NUNCA se veian + sombra de peces nueva
0b2e458  docs(diag): investigacion Cast CERRADA -- 4/4 tandas a 660s
5bf6016  tools(diag): harness autonomo de medicion Cast + receiver limpio
669c5c5  perf(deco): quitar texturas PBR muertas de 21 GLB -- 181,4 a 67,3 MB
00a87e9  perf(build): -7 paquetes de runtime + DiskSizeLTO -> WASM 44,2 a 25,4 MB
```

### ⚠ Ficheros sincronizados con cambios TV — revisar tras cada `SyncFromMobile`

| Fichero | Qué se le hizo |
|---|---|
| `Assets/ThirdParty/**/*.glb` (21) | texturas PBR muertas eliminadas |
| `Assets/Scripts/Tank/DecorationPlacer.cs` | `FixNonURPMaterials` rescata FishUnlit, `GetFloorSurfaceY`, comentarios de `UpdateShadow` |
| `Assets/Shaders/PlanarShadow.shader` | el arreglo entero |
| `Assets/Shaders/DecoLit.shader` | ambiente hemisférico |

Lo TV-only (`TvFishShadows`, `TvFoodManager`, `FishShadow.shader`, `TvShadowDiag`) está a salvo
del sync **a propósito**: por eso la escala de la comida se aplica en `TvFoodManager` y no en
`FoodItem.cs`, que existe en el móvil con los mismos valores.

---

## 6. Pendientes

### Para el próximo build
- [ ] Comprobar Code Optimization = `DiskSizeLTO` **antes** de construir.
- [ ] Build de player + deploy + castear con `FISH=12 bash Tools/cast-run.sh 2`.
- [ ] **Medir el coste de las sombras skinneadas** (12 draws extra). Si penaliza: blob de reserva.
- [ ] Desplegar el **receiver limpio** — el `index.html` vivo en R2 sigue siendo el de diagnóstico
      (`Tools/rcv-visual-2026-08-11.html` es la versión con la UI apagada).

### Abiertas
- [ ] **¿La Y que manda el móvil encaja con el suelo del TV?** El TV usa `PlaceAt(fromSave:true)`,
      que respeta la Y tal cual sin hacer snap. Si no encaja, las decos flotarían también en
      producción. No verificado con datos reales de un móvil.
- [ ] Medir **sin reinicio previo** de la caja (una de uso diario tiene menos memoria libre).
- [ ] Palanca de memoria sin usar: **texturas de decos a DXT** (~5,3 → ~0,7 MB cada una, coste de
      calidad casi nulo). Las mallas de 100k triángulos siguen siendo decisión del user — y ahora
      con más motivo: **son la única fuente de relieve**, porque `DecoLit` no lee normal maps.
      Bajarlas sin añadir normal map empeoraría el aspecto.
- [ ] **Sombras sobre otras decos**: imposible con esta arquitectura. Pide shadow mapping, que
      necesita el pase iluminado de URP, que no se ejecuta en el Cast. Salidas reales: Cast Connect
      (app nativa Android TV) o falsear contacto. No planificar contando con ello.

---

## 7. Verificado con capturas, no deducido

Todo el lote visual se comprobó con capturas del Game view (`ShadowDiag 3 Shot`) y, donde el ojo no
llegaba, midiendo luminancia de píxeles. Dos veces di por buena una sombra que no estaba —el user
la echó en falta las dos— hasta que la prueba del **rojo opaco** zanjó la discusión.
**Si una sombra "se intuye", no está.**
