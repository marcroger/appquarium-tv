# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del 2026-08-16. La anterior está en `CAST_NEXT_SESSION_2026-08-16.md`.
>
> **Hay UNA cosa a medio terminar y está desplegada sin validar.** Empezar por ahí (§1).

---

## 1. ⚠ LO PRIMERO: la estatua optimizada está en producción SIN VER

`deco_statue_greek_2` se optimizó (9,89 → 2,05 MB) y **el bundle y el catálogo ya están en R2**.
Falta la foto del «después»: la tele se apagó justo antes de poder hacerla.

**Si el prefab estuviera mal montado, ahora mismo esa estatua se ve mal en producción.**

### Cómo cerrarlo (5 minutos)

```bash
# 1. la caja tiene que estar encendida. La IP la descubre solo (el DHCP la mueve).
node Tools/cast-headless.js --ip <IP> --rung 2 --duration 180 --fish 2 --diag \
     --decos deco_statue_greek_2
```

Mientras carga, mirar en el log **dos cosas**:

- que cargue el bundle nuevo y no uno cacheado;
- que **NO aparezca ningún `FixMat`** sobre la estatua. Sus materiales ya son
  `Appquarium/DecoLit`, así que `FixNonURPMaterials` debe dejarlos pasar. Si sale un `FixMat`,
  el prefab no se aplicó y está cargando el GLB original → la comparación no valdría.

```bash
# 2. capturar y comparar contra la referencia del "antes"
adb -s <IP>:5555 exec-out screencap -p > deco-DESPUES.png
```

La referencia del «antes» está guardada en el scratchpad de la sesión del 16-ago:
`deco-ANTES.png` · `deco-ANTES-crop.png` · `deco-ANTES-zoom.png`, con el recorte
`(400, 760, 800, 950)` y estas cifras: **47.426 píxeles de estatua, luminancia media 155,69**.
Comparar píxel a píxel, no a ojo. Si el scratchpad ya no existe, rehacer el «antes» revirtiendo
primero (ver abajo).

### Marcha atrás, si se ve peor

`webgl-output/StreamingAssets/aa/catalog.bin` **sigue siendo el catálogo viejo** en local
(md5 `4f6bfb5684c9a45253d3511b838c5b17`). Volver a subirlo devuelve el puntero al bundle de
9,89 MB, que sigue intacto en R2:

```python
cl.put_object(Bucket='appquarium-tv', Key='StreamingAssets/aa/catalog.bin',
              Body=open('webgl-output/StreamingAssets/aa/catalog.bin','rb').read(),
              ContentType='application/octet-stream', CacheControl='public, max-age=60')
# y su hash: el viejo es 1536eab21c116a3f888a3b7a3ad87505
```

---

## 2. El plan de decos, ya con números reales

### 2.1 Lo medido, no estimado

| | |
|---|---|
| `statue_greek_2` | **9,89 → 2,05 MB (−79 %)** |
| Decos que el catálogo carga de verdad | **54 bundles, 150,7 MB** (media 2,8 MB) |
| Huérfanos en R2 de builds viejos | **46 bundles, 379,8 MB** que nadie carga |
| Decos de ≥5 MB | 18, y son el **81 %** del peso real |

⚠ **Corrección importante**: durante un rato se manejó la cifra de «375 MB de decos». **Es falsa** —
salía de coger el bundle más grande por nombre, y muchos eran huérfanos. El peso real es 150,7 MB.
Para contar bien: filtrar por los hashes que aparecen dentro de `catalog.bin`.

Si el −79 % se mantiene en las 18 pesadas: **150,7 → ~55 MB**.

### 2.2 Cómo se hace (ya está automatizado a medias)

1. **Python** extrae las texturas embebidas del GLB y escribe `mapeo.txt` (material→imagen).
   Parsear glTF en C# no aporta nada; el script está en el historial de la sesión del 16-ago.
2. **`Appquarium TV → 🗜 Optimizar deco`** (`Assets/Editor/TvDecoOptimize.cs`) hace el resto:
   import comprimido a DXT1, materiales `DecoLit` nuevos, prefab, y reapunta el `DecorationData`.
3. `★ New Build` y comparar el tamaño del bundle.

Hoy el menú tiene la ruta fija de `statue_greek_2` (era un prototipo). Para las demás hay que
parametrizarlo — es un cambio pequeño, `Optimizar()` ya recibe las tres rutas.

### 2.3 Por qué no hay atajo (comprobado, no supuesto)

- El importador de GLTFast **no expone compresión**: su `.glb.meta` sólo tiene `generateMipMaps`,
  `texturesReadable`, filtros y anisotropía.
- **No declara `SupportsRemappedAssetType`** → el remapeo estándar de Unity (`externalObjects`)
  tampoco sirve para sustituir sus texturas por assets externos.
- Apagar mipmaps sólo quitaría el factor 1,333 y empeora la calidad a distancia.

### 2.4 Limpieza gratis pendiente

Los **379,8 MB de bundles huérfanos** en R2 se pueden borrar: no están en el catálogo, nadie los
pide. Se acumularon porque el deploy usa `sync` sin `--delete` (que es lo correcto para no borrar
lo vivo). Requiere borrado selectivo por lista, no un `rm --recursive`.

---

## 3. Lo que se cerró el 2026-08-16

### 3.1 Build con Managed Stripping High — VALIDADO ✅

18h46 de build (caché fría por el merge; el caso «16h en frío» de `CLAUDE.md`).

| | antes | ahora |
|---|---|---|
| `.wasm` | 25.435.320 | **21.659.452** (−3,78 MB, **−14,8 %**) |
| `.data` | 16.876.157 | **15.938.078** (−938 KB, *sumando* 1,8 MB de audio) |

Validado en la tele: **sin `TypeLoadException`**, nada roto visualmente, 18/18 bundles,
sesión de 420 s con 0 errores, 23 % de memoria libre del sistema, RSS 285 MB, FPS instantáneo 48.

⚠ El nivel real era `Minimal` (`WebGL: 4`), no High, pese a que `CLAUDE.md` lo afirmaba. Ahora está
en `ProjectSettings` (versionado) **y** forzado por código en `TvProdBuild` antes de construir.

### 3.2 Audio — los 3 canales, por fin

Confirmado en el log del build: `ambient_music.mp3` 4,8 MB + `ambient_bubbles.wav` 1,2 MB +
`ambient_water.wav` 625,7 kb. El user lo dio por bueno de oído.
⚠ Las burbujas van a **0,08** de volumen (música 0,22): son muy sutiles a propósito.

### 3.3 Verificado por medición, no a ojo

- **Modo noche** por UPDATE: luminancia del agua **170 → 93,3**.
- **Bioluminiscencia** (era uno de los 8 bugs): el coral rojo **sube** de 44,6 a 52,1 de
  luminancia mientras la escena entera cae un 45 % → emite. Ratio coral/agua 26 % → 56 %.

---

## 4. Pendientes

### De esta línea
- [ ] **§1: foto del «después» de la estatua.** Lo único a medias.
- [ ] Parametrizar `TvDecoOptimize` y pasarlo a las 18 decos pesadas.
- [ ] Borrar los 379,8 MB de bundles huérfanos de R2.
- [ ] Decidir sobre las **mallas** (segunda palanca, la que sí cuesta calidad). Ojo: las de la
      estatua ya se llamaban `mesh_low_part_XX`, así que puede que no todas sean de 100k triángulos
      como se creía — conviene medirlo antes de decidir.

### Heredados
- [ ] **Los 11 fondos viajan dos veces**: horneados en el `.data` vía `Resources/` **y** como
      bundles remotos que ningún código pide.
- [ ] **Hueco del protocolo (pide tocar el móvil):** editar una deco ya colocada (girar, escalar,
      voltear) no manda ningún UPDATE.
- [ ] `origin/main` sigue en `4064e61`. Hay **50+ commits locales sin push**.

---

## 5. Trampas nuevas aprendidas hoy

1. **Dos proyectos Unity abiertos comparten `Editor.log` y el segundo lo TRUNCA.** Perdí la traza
   del build a media, y los errores de compilación del OTRO proyecto parecían míos y dispararon
   una falsa alarma de «build fallido». Para vigilar un build largo: mirar el mtime del `.wasm`,
   no el log.
2. **`ls -S` para medir bundles engaña**: coge el mayor por nombre, que suele ser un huérfano.
   Filtrar siempre por los hashes presentes en `catalog.bin`.
3. **La IP de la caja cambia y la caja se apaga sola.** Dos tandas abortadas hoy por eso.
   El descubrimiento por 8008 + `eureka_info` funciona; el ping sigue sin valer.
