# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del 2026-08-15. La anterior está en `CAST_NEXT_SESSION_2026-08-15.md`.
> **El lote visual está DESPLEGADO en R2 y VALIDADO en la tele.** Se acabó la línea abierta
> desde junio: las sombras se ven.

---

## 1. Lo que pasó el 2026-08-15

### 1.1 Deploy — hecho

| Clave en R2 | Antes | Ahora |
|---|---|---|
| `Build/webgl-output.*` | player del 27-jul | **player del 12-ago** (lote visual) |
| `index.html` | receiver de diagnóstico, 117.470 B | **receiver limpio**, 54.678 B, `max-age=60` |
| bundles · `StreamingAssets/` | — | **sin tocar** (188 claves intactas) |

⚠ **El comando del handoff anterior era peligroso.** `aws s3 sync webgl-output/ … --delete
--exclude "bundles/*"` habría borrado de R2 `keepalive_black.mp4` (que el receiver referencia
2 veces — es el vídeo keepalive validado), `silence.wav` y los builds de diagnóstico
`webgl-min.*` / `webgl-output-empty.*`. **No usar `--delete` en la raíz del bucket.**
Lo correcto: subir solo `Build/` (sync sin `--delete`) + `index.html` por boto3.
`StreamingAssets/` no hacía falta: idéntico byte a byte (mismo `catalog.hash`).

### 1.2 Un fallo más del receiver "limpio" — arreglado (`b1ad2d9`)

El commit `cae1975` puso `#dbg-panel` en `display:none` + guarda en `dbg()`. **No bastaba:**
el volcado del historial de caídas hacía `panel.style.display = 'block'` incondicional si había
entradas en `localStorage.aq_discoLog` — y la caja arrastra historial de julio/agosto. Resultado:
el panel se habría encendido solo en cada arranque, anulando el fix y devolviendo los ~35 MB de
Native Heap. Arreglado en `webgl-output/index.html` **y** en el template. Sello → `rcv 2026-08-15 visual`.

### 1.3 Validación en la tele — todo pasa

Tanda: `node Tools/cast-headless.js --ip 192.168.1.33 --rung 2 --duration 900 --fish 12`
(**sin reiniciar la caja**, a propósito). Logs en `_cast_runs/revision-visual-2026-08-15/`.

| | |
|---|---|
| FPS | **avg 45 · lo 36 · hi 49** con 12 peces + 12 sombras skinneadas |
| WASM heap | **133 MB plano**, 175 muestras en 15 min |
| Sesión | **900,8 s**, cerrada por nuestro `STOP`, 0 cortes, 1 stall (el inicial) |
| Carga | 18/18 bundles OK, acuario montado en 47 s |

Sombras **contadas en píxeles** contra la arena limpia (la regla del proyecto: si se intuye, no está):

| | píxeles | contraste |
|---|---|---|
| ancla | 1.988 (36,8 %) | −106 |
| roca | 4.084 (61,4 %) | −130 |
| pez | 5.457 (65,0 %) | **−22** |

Las de peces son reales pero **5× más suaves** que las de decos — esperable, el pez está alto en
el agua. **El coste de las sombras skinneadas ya no es una incógnita: caben.** El blob elíptico
de reserva no hace falta.

⚠ **Falsa alarma a no repetir:** el log `FixMat hall_anchor: shader=Appquarium/FishUnlit` imprime
el shader **de ENTRADA** (`DecorationPlacer.cs:1707`), antes de que `unlitEnDeco` lo convierta a
DecoLit. El ancla está iluminada: gradiente +10,2 sobre media 28 (**36 %**) vs roca +5,3 sobre 82 (6,5 %).

### 1.4 Herramienta nueva: leer la pantalla sin humano delante

`adb exec-out screencap -p > shot.png` **captura el canvas WebGL**, no solo el DOM. De ahí salieron
el FPS y todas las medidas de arriba. DevTools **no** está disponible en este build de Cast (no hay
socket en `/proc/net/unix` ni puerto a la escucha), y el FPS **no viaja** por el canal Cast: el
screencap es la única vía de leerlo automáticamente.

---

## 1.5. Auditoría con subagentes (2026-08-15, tarde)

Cuatro revisiones en paralelo: C#, capa Cast, build/deploy y documentación. **El patrón de casi
todo lo encontrado es el mismo: fallos silenciosos.** Nada petaba; simplemente no pasaba lo que
creíamos. Arreglado en `41bf883` (compilación verificada en batchmode, 0 errores CS).

| Bug | Efecto real |
|---|---|
| `remove_fish` → `DespawnBySpecies` | Sacas 1 pez de 3 en el móvil → desaparecen los 3 en la tele |
| `FromJson` del payload fuera del `try` | Un UPDATE malformado **aborta el runtime IL2CPP** (`Exception Support: None`) → tele congelada, sin mensaje |
| `TankLightingController` sin limpiar | 3 spots + Volume nuevos **por INIT**; 10 reconexiones = 30 spots |
| `TankNightOverlay` fuera de la limpieza | El acuario se oscurece en escalera hasta negro |
| Carrera `Start()` ambiente | Castear a las 22:00 → tele en modo noche con el móvil en día |
| Corrutina de carga sin parar la anterior | Reconectar durante la carga → dos cargas pisándose |
| Biolum reaplicada antes de `_placed` | Corales apagados toda la sesión si reconectas de noche |
| `fishSpeed` sin guarda | Un `0` congela todos los peces |

**Verificado end-to-end en el device por primera vez:** `ambient=night` bajó la luminancia media
del agua de **170 a 92**. Hasta hoy los 11 UPDATEs sólo estaban probados en Chrome local.

**Producción más limpia:** los HUD de FPS/stats van apagados y se encienden en caliente con el
mensaje `DIAG` (`cast-headless --diag`). Ya no hace falta editar y resubir el `index.html` para
dejar de mostrarlos. Receiver vivo: `rcv 2026-08-15b limpio`.

## 2. Siguiente

- [x] ~~Decidir el merge a main~~ ✅ hecho: `325a931` (local, sin push).
- [x] ~~Apagar el `#fps-meter`~~ ✅ hecho de otra forma: apagado por defecto + mensaje `DIAG`.
- [ ] ⭐ **REBUILD DEL PLAYER + DEPLOY.** Nada de lo arreglado hoy en C# se ve en la tele hasta
      rebuildear (~55 min). Incluye el audio (los 3 canales) y los 8 bugs de arriba.
      Ruta: `-executeMethod TvProdBuild.BuildProd` con el Editor cerrado.
- [ ] ⭐ **Decidir: Managed Stripping a High.** Está en `Minimal` (`WebGL: 4`), no en High como
      decía `CLAUDE.md`. Comprobado empíricamente: `Unity.Addressables.dll` no encoge nada al
      strippear. High reduciría el `.wasm`, que es **la causa raíz de los cortes de sesión**.
      Riesgo: `TypeLoadException` en runtime. Si se hace, va en el mismo rebuild.
- [ ] **Los 11 fondos viajan dos veces**: horneados en el `.data` vía `Resources/` **y** como
      bundles remotos que ningún código pide. Sacar `Backgrounds/` y `Audio/` de `Resources/`.
- [ ] **Hueco del protocolo (pide tocar el móvil):** editar una deco ya colocada (girar, escalar,
      voltear) NO manda ningún UPDATE. La tele se queda con la orientación del primer frame.
- [ ] Arreglar la guarda de `Tools/restore-production-receiver.sh` (falso negativo: busca
      `Build/webgl-output.loader.js` literal y el receiver arma esa URL en dos trozos).
- [ ] Decidir si `Assets/AddressableAssetsData/link.xml` (+`.meta`) va a git o a `.gitignore`.
      Sigue sin trackear desde el build del 12-ago.

### Abiertas (heredadas, sin cambios)

- [ ] **¿La Y que manda el móvil encaja con el suelo del TV?** `PlaceAt(fromSave:true)` respeta la
      Y sin snap. No verificado con datos reales del móvil.
- [ ] Palanca de memoria sin usar: texturas de decos a DXT (~5,3 → ~0,7 MB cada una).
- [ ] Mallas de ~100k triángulos por coral — decisión de calidad del user.
- [ ] **Sombras sobre otras decos**: imposible con esta arquitectura (pide shadow mapping → pase
      URP que no corre en Cast). Salidas reales: Cast Connect o falsear contacto.
