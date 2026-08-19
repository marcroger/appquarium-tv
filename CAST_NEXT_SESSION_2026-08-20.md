# ▶▶ EMPEZAR AQUÍ — próxima sesión

> Escrito al cierre del 2026-08-19. La anterior está en `CAST_NEXT_SESSION_2026-08-18.md`.
>
> **No hay nada a medias.** Todo lo desplegado está validado en la tele y R2 está limpio.

---

## 1. Lo que se cerró

### 1.1 Las 2 decos que faltaban + los 11 fondos fuera de Addressables

| | antes | ahora | Δ |
|---|---|---|---|
| `deco_shell_lambis` | 4,30 MB | **1,00 MB** | −76,9 % |
| `deco_starfish_blue` | 4,45 MB | **2,81 MB** | −36,8 % |

La diferencia la explica entera la malla: 12.498 triángulos frente a **100.000**, con texturas
idénticas. 🧭 **El % que rinde el paso a DXT1 lo predice la proporción textura/malla.**

Los **11 fondos** salieron de Addressables: se cargan siempre por `Resources.Load`
(`TankBackground.cs:207` y `:296`) y en todo el proyecto no hay ni un `LoadAssetAsync<Texture2D>`.
⚠ **No bastaba con borrar las entradas: `★ Setup Addressables` las recreaba** en cada ejecución.
La copia horneada en el `.data` (~0,7 MiB) **sí se usa y se queda**.

### 1.2 Los materiales de las 21 decos no-GLB → DecoLit

**Unity sólo empaqueta las texturas de propiedades que declara el shader ACTIVO.** Los materiales en
URP/Lit metían normal + metallic/smoothness + AO + emission en el bundle, y el runtime **ya los
tiraba** (`FixNonURPMaterials` reconstruye como DecoLit, que sólo declara `_MainTex`/`_Color`).

Validado en la tele: **`FixMat` 127 → 0** en los 6 grupos y **las 21 decos pixel-idénticas**
(máximos Δ −2,25 y −2,02, que son el pez y las burbujas).

**Total acumulado: las 54 decos de 149,8 → 61,03 MB (−59,3 %).**

### 1.3 Censo de mallas (herramienta nueva)

`Appquarium TV → 📐 Informe de mallas por deco` (`Assets/Editor/TvDecoMeshReport.cs`), CSV en
`deco-mallas.csv`. 42 decos con malla + 12 sustratos, **1.429.275 triángulos**, mediana 13.536.

### 1.4 R2 limpio y el script mejorado

**37,5 MB liberados.** R2 queda en **80 bundles vivos = 87,3 MB** + **3 locales** (0,5 MB), 0
huérfanos. `Tools/r2_huerfanos.py` ahora revisa **también** `StreamingAssets/aa/WebGL/` — era su
punto ciego y costó 6 decos rotas en producción.

### 1.5 R2 de producción limpio y techo remedido

Borrados **56,72 MB** de artefactos de diagnóstico que estaban servidos en producción:
`webgl-output-empty.*`, `webgl-min.*` e `index_test.html`. Son reproducibles con
`TvEmptyTestBuild.cs`. **R2 queda en 97 objetos / 126,07 MB** (eran 106 / 182,79).

⚠⚠ Se borraron con **lista explícita, NUNCA con `--delete`**: en la raíz está
`keepalive_black.mp4`, que es lo que mantiene viva la sesión.

**Techo de carga remedido** con el protocolo del 15-ago (25 peces + 6 decos, 420 s), que de paso
verifica que la limpieza no rompió nada:

| | 15-ago | 19-ago |
|---|---|---|
| WASM heap | 191 MB | **159 MB (−16,8 %)** |
| FPS medio | 37 | **37** |
| Sesión | 420 s | **421 s, 0 errores, 0 `FixMat`** |

Los 32 MB de heap ganados son exactamente el peso que perdieron las decos.

---

## 2. Lo que queda

### 2.0 🔴 LO ÚNICO BLOQUEANTE: el bucket de R2 está abierto

```
curl https://pub-…r2.dev/StreamingAssets/aa/catalog.hash   ->  HTTP 200, sin auth
```

Cualquiera con la URL se baja el catálogo y **todos los assets**. El riesgo es **doble**: fuga de
ingresos (el Premium de 25 € deja de tener sentido) y **fuga de licencias** — el Pack 24 y los
modelos de Sketchfab no-CC0 **prohíben redistribuir los assets crudos**.

📄 **Spec completo y ejecutable: [`CAST_R2_AUTH_SPEC.md`](CAST_R2_AUTH_SPEC.md)** (13 secciones).
Worker de Cloudflare como portero + JWT HS256 con claims de propiedad.

| | |
|---|---|
| Coste | **0 €/mes** hasta ~3.000 usuarios/día · 5 $/mes después · <20 $/mes a 10.000 |
| Esfuerzo | ~3 días (1 TV + 1 móvil + 1 pruebas) |
| ⚠ Requisitos | Toca **los dos repos**, y **el Worker lo tiene que crear el user** en su cuenta de Cloudflare (login propio) |

**Mientras el proyecto sea privado no corre prisa. En cuanto se promocione, sí.**

### 2.1 🎯 Las MALLAS — la palanca grande que queda

Con el censo ya no hay que suponer nada:

- **11 decos clavadas en ~100.000 triángulos exactos** (tope de decimación del proveedor de
  fotogrametría): 7 corales, 2 conchas, la estrella y el casco.
- Son el **77 % de los triángulos** y el **52 % del peso**; en ellas la malla es **~79 % de su
  bundle**.
- Decimar a 50k → **−14 MB**; a 25k → **−21 MB** (estimado, no medido).

⚠ **Cuesta calidad y se ve**, a diferencia del cambio de formato de textura → **decisión del user**.

### 2.2 💡 Llevar el método al repo MÓVIL

📄 **Está todo escrito en [`DECOS_PESO_PARA_MOVIL.md`](DECOS_PESO_PARA_MOVIL.md)** — las tres
palancas, qué ficheros se reutilizan, qué hay que cambiar (⚠ **DXT1 no vale en Android → ASTC 6x6 o
ETC2**), cómo medir con Play Asset Delivery, y las 7 trampas ya pagadas aquí.

⚠ **El trabajo se hace desde `D:\dev\appquarium-unity\`, nunca desde TV.**

⚠ Un aviso importante que está en ese doc: la **palanca del shader NO se traslada tal cual**. En TV
quitar los mapas PBR era gratis porque el runtime ya los descartaba; en el móvil corre URP de verdad
y seguramente **sí se usan** → allí es «bajar calidad a cambio de MB», no una optimización gratis.

### 2.3 Heredados

- [ ] **Halo de la bioluminiscencia** — quad aditivo con shader CG legacy propio en Always Included.
      ⚠ `Sprites/Default` está medido que no pinta materiales de runtime en este device.
- [ ] **Sacar los fondos del `.data`** (~0,7 MiB): pide convertir carga síncrona en asíncrona +
      rebuild de player. Mala relación premio/riesgo salvo que se junte con otro cambio de player.
- [ ] **Editar una deco ya colocada** (girar, escalar, voltear) no manda ningún UPDATE — pide tocar
      el móvil.
- [ ] `ageScale` de peces: parte TV lista, falta build móvil.
- [ ] **Contradicción `maxInactivity`**: el research de julio dice que fijarlo es contraproducente;
      en disco está 3600 y es con lo que están validadas las sesiones largas. No tocar sin A/B.
- [ ] `supportPointLocal` sin estrenar en los 54 SOs — la perilla si una deco se ve mal apoyada.
- [x] ~~commits locales sin push~~ **empujados el 2026-08-19**: `origin/main` = `2b528e9`.
- [ ] 🎯 **Cast Connect** — salida arquitectónica (app nativa Android TV reaprovechando Unity).

---

## 3. Estado desplegado

| | |
|---|---|
| Catálogo | md5 `7f3d9ee5…` · hash `52cfa262…` |
| Bundles | **80 vivos = 87,3 MB**, 0 huérfanos |
| R2 completo | **97 objetos / 126,07 MB** (limpiado el 19-ago) |
| Locales | 3 en `StreamingAssets/aa/WebGL/` (0,5 MB) |
| `.wasm` / `.data` | 21,66 / 15,94 MB (player del 17-ago, sin rebuild) |
| Sello receiver | `rcv 2026-08-17 decos` |
| Rendimiento | 25 peces: WASM 159 MB, FPS 37, 421 s sin errores |

---

## 4. Trampas nuevas aprendidas — las caras

### 4.1 ⚠⚠ El bundle LOCAL cambia de hash y hay que subirlo

`shared_local_assets_all_<hash>.bundle` cambió de hash **en cada uno de los 4 builds** de la sesión.
Vive en `StreamingAssets/aa/WebGL/`, **se sirve por HTTP desde R2** (o sea que **NO hace falta
rebuild de player**), y si el catálogo pide uno que no está, las decos que dependan de él fallan con
**`Dependency Exception`** — pero **sólo esas**, así que una verificación con pocas decos lo da por
bueno. Costó 6 decos rotas en producción.

✅ `Tools/r2_huerfanos.py` y el script de deploy ya lo comprueban.

### 4.2 🧭 Los tres fallos de la conversión de materiales fueron EL MISMO error

**Verificar el efecto secundario en vez del principal.** Costó 3 vueltas completas de
conversión + build + deploy + verificación:

| # | Qué hice | Qué comprobé | Qué se rompió |
|---|---|---|---|
| 1 | `m.shader = decoLit` | textura y color | `_EmissionColor` se arrastró en blanco → **9 bloques blancos** |
| 2 | `CopyPropertiesFromMaterial` | la emisión | **ese método NO asigna el shader** → 21 materiales seguían en URP/Lit |
| 3 | reejecutar sobre los dañados | el shader | **URP/Lit declara `_BaseMap`, no `_MainTex`** → **21 decos blancas sin textura** |

Detalle del 3: el intento 2 vació `_BaseMap` (la propiedad **viva**) dejando `_MainTex` como
**huérfana en el YAML** — visible al leer el fichero, pero `HasProperty("_MainTex")` es `false` en
URP/Lit, así que el *fallback* nunca corría.

🛡 `TvDecoMaterialFix` tiene ahora **tres guardas que abortan**, una por fallo: el shader queda en
DecoLit · la emisión queda en negro · **la textura base no es nula**.

🧭 Reglas que salen de aquí:
- **Ejecutar siempre sobre materiales limpios de git.** El estado intermedio miente.
- **`FixMat` a 0 NO basta como prueba**: la vuelta 2 dio 0 y las decos estaban rotas.
- **Un `LogWarning` en un lote es invisible.** El aviso saltó **20 veces** sin que nadie lo mirara.
- ⚠ **Una cifra que mejora de más es sospechosa**: el «−13,0 %» era **peor** que el −9,5 % real —
  faltaban las texturas albedo.

### 4.3 ⚠ La línea de `FixMat` imprime el shader de ENTRADA

`FixMat Cliff_fancy: Cliff_fancy [Universal Render Pipeline/Lit] → Appquarium/DecoLit`

**Contarlas no basta: hay que leer qué shader dicen.** Eso fue lo que destapó el fallo 2, después de
perseguir en falso una caché del device que no existía (reinicio, `pm clear` de mediashell y del
WebView — nada de eso era).

### 4.4 🔬 Diagnóstico decisivo: borrar UN bundle huérfano

Cuando se sospeche que el device usa bundles viejos: **borrar de R2 el bundle huérfano viejo de una
sola deco** y castearla. Si carga, está en el catálogo nuevo. Reversible (hay copia local) y con
radio de acción de una deco. Fue lo que cortó horas de teoría.

### 4.5 ⚠ Los builds del harness mueren a los ~10 min

Un build largo lanzado con la herramienta de tareas se corta. Lanzarlo **desacoplado** con
`Start-Process` de PowerShell y vigilar el log aparte.

### 4.6 ✅ La caja NO se apaga sola — la apaga el user

⚠ **Creencia falsa corregida el 2026-08-19.** Estaba documentado desde el 17-ago como un fallo del
device («se apaga sola 3 veces pese a `stay_on_while_plugged_in 7`») y se perdió tiempo
diagnosticándolo. **La apaga el user cuando no la usa.**

🧭 Si no responde: lo más probable es que esté apagada. **Pedirle que la encienda** en vez de barrer
la subred buscando un fallo. ⚠ El ping NO sirve para descartarlo: otro cacharro coge la IP libre y
responde (pasó el 19-ago). Lo que vale es `curl http://IP:8008/setup/eureka_info | grep -i xiaomi`.

ℹ Tras encenderla, el primer intento de castear da `LAUNCH_ERROR: NOT_FOUND` durante ~1-2 min: hay
que esperar a que el registro de apps Cast esté listo.
