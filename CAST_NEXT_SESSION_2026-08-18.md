# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del 2026-08-17. La anterior está en `CAST_NEXT_SESSION_2026-08-17.md`.
>
> **No hay nada a medias.** Todo lo desplegado hoy está validado en la tele. Es la primera vez
> en varias sesiones que se cierra sin un «desplegado sin ver».

---

## 1. Lo que se cerró hoy

### 1.1 La estatua del 16-ago — validada (era el pendiente que bloqueaba)

Llevaba un día en producción sin comprobar. Estatua sola y centrada, recorte `(400,760,800,950)`:

| | |
|---|---|
| Δ píxeles de estatua | **+0,32 %** |
| Δ luminancia media | **−0,00** |
| Píxeles con diferencia >10 | 554 de 76.000, y son los peces moviéndose |

Segunda captura 35 s después: +0,31 % / +0,15. **9,89 → 2,05 MB (−79 %) sin coste visual.**

### 1.2 Las 18 decos pesadas — hechas, desplegadas y validadas

| | antes | ahora | Δ |
|---|---|---|---|
| Las 18 optimizadas | 122,27 MB | **47,63 MB** | **−61,0 %** |
| Las 54 decos | 149,80 MB | **75,15 MB** | **−49,8 %** |
| Vivos en R2 (todo) | 176,6 MB | **102,0 MB** | −42 % |

**El patrón vale más que el total:**
- **Estatuas y columnas** (3 texturas): **−70 a −78 %**.
- **Corales y conchas** (1 textura): **−45 a −52 %**.

La diferencia es la proporción textura/malla. En las decos de una sola textura **lo que queda es
malla**, así que esto responde la duda que había sobre la segunda palanca: la geometría ES el peso
restante ahí. Ya no hace falta suponerlo.

⚠ La proyección de «→ ~55 MB» asumía el −79 % en todas y se quedó corta. El −79 % era el caso
bueno (3 texturas), no la media.

Validado en la tele con una deco de cada tipo: sin magenta, texturas y relieve intactos, y **el
detalle de las ramas finas del coral sobrevive a DXT1**. Cero `FixMat` (la señal de que los
prefabs optimizados están en uso). Sesión de 210,9 s completa, 0 errores, WASM plano 64→77 MB,
FPS avg 46 noche / 37 día.

### 1.3 La bioluminiscencia — llevaba meses muerta, ahora emite

Estaba muerta por **tres** causas independientes:

1. **Ningún SO tenía el flag.** El JSON marca 6 corales, pero `CatalogLoader` **no lo llama nadie
   en TV**; la fuente en runtime son los SOs de los bundles, y a 53 de 54 les falta el campo
   siquiera serializado → default de C# (`false`) sin un aviso.
2. **Ningún shader declaraba `_EmissionColor`**, así que el filtro `HasProperty` de
   `DecorationPlacer` daba lista vacía — y la luz puntual se crea **dentro** de ese `if`.
3. **`DecoLit` usa una luz direccional fija hardcodeada**, así que una `Light` de Unity no puede
   afectarle: el enfoque de luz puntual nunca podía funcionar (no era el límite de *additional
   lights* de URP que suponía la memoria).

Arregladas la 1 y la 2. Medido tras desplegar:

| | día | noche | Δ |
|---|---|---|---|
| Agua | 107,64 | 55,88 | −48,1 % |
| **Suelo (control)** | 196,43 | 196,43 | **0,00 exacto** |
| **Coral** | 101,72 | **122,77** | **+20,7 %** |

Luminancia **absoluta** subiendo mientras la escena cae un 48 %, con el suelo clavado. Horas antes
del fix la misma medida daba **−0,2 %**.

⚠ **El «✅ funciona» del 16-ago era un artefacto**: se midió el ratio coral/agua, que sube solo al
oscurecerse el agua. 🧭 Regla: para «¿esto emite?», **luminancia absoluta del objeto**, nunca
contraste contra un fondo que también cambia.

Señal en caliente: la línea `DecoCatalog: 1 decos parcheadas, 1 con bioluminiscencia`. Si no sale,
el flag no está llegando.

### 1.4 Herramientas que antes no existían

- **`Tools/extract_glb_textures.py`** — el extractor de texturas de GLB sólo existía en el
  historial de una sesión. Ya en disco, validado (reproduce el mapeo del prototipo) y con
  `--todas` / `--dry-run` / `--forzar`.
- **`TvDecoOptimize` parametrizado** — lote de 18, selección por GLB, preflight que aborta si
  faltan texturas, y `OptimizarLoteBatch` para batchmode.
- **`Tools/r2_huerfanos.py`** — informe de bundles huérfanos. Compara contra el catálogo bajado
  **de R2**, no el local, y se niega a borrar si más del 60 % del bucket sale huérfano.

---

## 2. Lo que queda

### 2.1 ~~Limpieza de R2~~ — HECHA

**540,1 MB liberados.** El bucket pasa de 642,1 a **102,0 MB (−84 %)**, de 206 a **91 bundles**,
0 huérfanos. Los 91 vivos verificados **uno a uno con HTTP 200** por la URL pública que usa el
device, y `keepalive_black.mp4` / `silence.wav` / `index.html` / el player intactos.

✅ **Y comprobado EN LA TELE después de borrar**: sesión de 131,7 s completa con 5 peces y 4 decos,
**9/9 bundles OK**, 4/4 decos colocadas, 0 errores, WASM 64→92 MB, FPS avg 40.

ℹ Control bonito de esa tanda: se metió a propósito una deco **sin optimizar** (`deco_rock_hq_1`) y
el log dio **exactamente un `FixMat`, el suyo**. Las tres optimizadas no dan ninguno porque sus
materiales ya son `DecoLit`. **La presencia de `FixMat` discrimina optimizada vs sin optimizar**, y
la roca se ve igual de bien que las demás: el cambio de formato no se nota.

⚠ Al verificar los 91 por HTTP, el primer intento dio `000` en los 91: era mi propio bucle
descargando los cuerpos completos (~102 MB) a ráfaga y r2.dev cortando. Con `HEAD` y reintentos,
91/91 limpios. **Para comprobar muchos objetos: `HEAD`, conexión reutilizada y reintentos.**
⚠ Y cuidado al contar errores en un log: `grep -i fail` casca 7 falsos positivos porque las líneas
de estadísticas llevan `fail=0`.

```bash
python Tools/r2_huerfanos.py            # informe (ahora da 0)
python Tools/r2_huerfanos.py --borrar   # pide escribir SI
```

### 2.2 Dos decos que quedaron fuera

`deco_starfish_blue` (4,45 MB) y `deco_shell_lambis` (4,30 MB) — hoy **las dos más gordas**, porque
estaban justo bajo el corte de 5 MB. Sus texturas **ya están extraídas**; añadirlas a
`TvDecoOptimize.DecosPesadas` es un minuto. Requiere build de bundles (~68 min) + deploy.

### 2.3 El halo de la bioluminiscencia (opcional)

Hoy el coral **brilla**, pero no hay halo en el agua alrededor. Si se quiere, la vía es un quad
aditivo hijo del coral. ⚠ **NO vale `Sprites/Default`**: hay medido que no pinta materiales de
runtime en este device (0 píxeles). Hace falta un shader CG legacy propio **registrado en Always
Included** con el patrón de `TvShadowDiag.RegistrarShader()` (`SetDirty` + `SaveAssetIfDirty`;
`SaveAssets` solo NO persiste ProjectSettings). Se aplazó a propósito: shader nuevo de runtime =
riesgo de magenta, y no se quiso meter en el mismo build que cambiaba 18 decos.

### 2.4 💡 Llevar esto al repo MÓVIL (idea del user, 17-ago)

Si el método quita la mitad del peso aquí, en el móvil reduciría el **tamaño de instalación de la
app**. **El trabajo se haría desde `D:\devppquarium-unity\` con Claude — desde TV NO se toca
el repo móvil.**

Aplica porque **la causa es de GLTFast, no de WebGL**: su importador decodifica las texturas
embebidas a RGBA32 y no expone compresión, así que **se salta el override de plataforma sea WebGL
o Android**. Y los **21 GLB son los mismos** (TV los sincroniza DESDE el móvil).

⚠⚠ **El único cambio obligatorio es el formato: DXT1 es de escritorio/WebGL y NO vale en Android.**
Hay que poner `SetPlatformTextureSettings(name = "Android")` con **ASTC 6x6/8x8** (recomendado) o
**ETC2 RGB**. Frente a los 4 byte/píxel de RGBA32 dan un factor de 4,5× a 8×, o sea que **el orden
de magnitud del ahorro se mantiene**, pero el número exacto hay que medirlo allí.

Se reaprovechan tal cual `Tools/extract_glb_textures.py` (agnóstico de plataforma) y la lógica de
`TvDecoOptimize`; hay que cambiarle el formato, el shader destino (allí será el URP del juego) y la
lista de decos. Para medir, el equivalente de los bundles son los packs de **Play Asset Delivery**
(`assetBundleName`, p. ej. `pack_decos_greek`) y el tamaño del AAB.

Detalle completo, con las comprobaciones para no engañarse: memoria `deco_metodo_portable_a_movil`.

### 2.5 La segunda palanca: las mallas

Ahora hay dato para decidir. Los corales se quedaron en −45/−52 % porque **lo que les queda es
malla** (fotogrametría). Bajarlas cuesta calidad → decisión del user. ℹ Las mallas de la estatua
se llamaban `mesh_low_part_XX`, así que conviene **medir triángulos por deco** antes de asumir que
todas tienen ~100k.

### 2.6 Heredados

- [ ] **Los 11 fondos viajan dos veces**: horneados en el `.data` vía `Resources/` **y** como
      bundles remotos que ningún código pide.
- [ ] **Hueco del protocolo (pide tocar el móvil):** editar una deco ya colocada (girar, escalar,
      voltear) no manda ningún UPDATE.
- [ ] `origin/main` sigue en `4064e61`. **50 commits locales sin push** (lo de hoy es `9744ca4`,
      249 ficheros). ⚠ La rama activa es **`main`**, no `feat/netflix-architecture` como decía la
      memoria: se mergeó y desde entonces los commits van directos a main.
- [ ] `supportPointLocal` está sin estrenar en los 54 SOs → si un coral se ve mal apoyado, esa es
      la perilla (el fallback coge el punto más bajo del AABB, que puede ser la punta de una rama).

---

## 3. Estado desplegado

Sello **`rcv 2026-08-17 decos`**. El `index.html` procesado salió **idéntico** a
`Tools/rcv-limpio-2026-08-16.html` salvo el sello → la trampa del template no apareció.
Copia en git: `Tools/rcv-limpio-2026-08-17.html`.

| | |
|---|---|
| `.wasm` | 21.661.216 (+1.764 vs 16-ago) |
| `.data` | 15.941.422 (+3.344) |
| bundles en R2 | **91** (tras limpiar 115 huérfanos) |
| LTO / PreflightAudio | `DiskSizeLTO` ✅ / 3 de 3 ✅ |

Marcha atrás en el scratchpad de la sesión: `player-backup-2026-08-16/` (`.wasm` md5
`2649b37e…`) y `r2-backup-antes-deploy/` (catálogo viejo md5 `e268970e…`). Los bundles viejos
siguen en R2, así que devolver `StreamingAssets/aa/catalog.bin` revierte las 54 decos de golpe.

---

## 4. Trampas nuevas aprendidas hoy

1. **El receiver sobrevive al sender.** Si lanzas una tanda con el receiver aún vivo, hereda su
   cuenta atrás y muere a los pocos segundos (una tanda murió a los 13,6 s). Se detecta porque el
   reloj `RCV` **arranca alto**. Hacer `cast-headless.js --stop` antes de cada tanda.
2. **`--decos` reparte las decos** con `x = -2.6 + 5.2k/(n-1)`. Con dos o más, la deco se mueve y
   **se sale del recorte de referencia**. Para comparar contra un «antes», castear la deco SOLA.
3. **Casi repetí la trampa del `ls -S`.** Un script mío cogía el bundle más grande por nombre para
   la columna «antes» y daba **−83,8 %** en vez de −61,0 %: eran huérfanos de builds viejos.
   Para cualquier comparación de tamaño, **filtrar por los hashes del `catalog.bin` en AMBOS lados**.
4. **El interruptor de producción es `StreamingAssets/aa/catalog.bin`**, no `bundles/catalog_*.bin`.
   El `sync` de `ServerData/` ya sube el de `bundles/` de paso, pero nada cambia hasta el de
   `StreamingAssets`. Se puede aprovechar: subir bundles y player antes, y accionar al final.
5. **El umbral de luminancia importa para comparar con el histórico.** El que reproduce las cifras
   de referencia de la estatua (47.426 px / 155,69) es **88**; no estaba anotado y hubo que barrerlo.
6. **La caja se apaga sola** (tres veces hoy, pese a `stay_on_while_plugged_in 7`) y el DHCP le
   mueve la IP. ⚠⚠ **Ni el ping ni el puerto 8008 bastan para identificarla**: el ping falla porque
   otro cacharro coge la IP libre, y hoy apareció un `192.168.1.40` con el 8008 abierto que era
   **otro Cast de la casa, «Comedor»**. Hay que leer el nombre:
   `curl http://IP:8008/setup/eureka_info | grep -i xiaomi`. `cast-run.sh` ya lo hace bien; un
   escaneo manual por puerto no, y te puede llevar a castear al dispositivo equivocado.
