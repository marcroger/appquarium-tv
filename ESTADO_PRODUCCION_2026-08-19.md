# Estado de producción — 2026-08-19

> Foto completa del proyecto al cierre del 19-ago, y una valoración honesta de qué está listo para
> una subida a producción y qué no.
>
> **Resumen:** el receiver está **funcionalmente listo y validado**. Lo que falta para una subida
> «a pro» de verdad **no es el motor, es la infraestructura**: el bucket está abierto al mundo y
> es que **el bucket está abierto al mundo**. Todo lo demás de la lista quedó cerrado el 19-ago. Detalle en §4.

---

## 1. Qué hay desplegado ahora mismo

### R2 — `appquarium-tv` · 97 objetos · 126,07 MB

| Ruta | Objetos | Peso | Qué es |
|---|---|---|---|
| `bundles/` | 82 | **87,37 MB** | 80 bundles vivos + catálogo. **0 huérfanos** |
| `Build/` | 4 | **38,03 MB** | Sólo el player de producción |
| `StreamingAssets/` | 8 | 0,57 MB | Catálogo + 3 bundles locales |
| raíz | 3 | 0,10 MB | `index.html`, `keepalive_black.mp4`, `silence.wav` |

✅ **Limpiado el 19-ago: 56,72 MB de rigs de diagnóstico e `index_test.html` fuera.** Verificado
después con 25 peces + 6 decos cargando sin un error.

### El contenido

| | |
|---|---|
| Catálogo servido | md5 `7f3d9ee5…` · hash `52cfa262…` |
| Bundles vivos | **80** = 25 peces + 54 decos + 1 audio |
| Decos | **61,03 MB** (eran 149,8 → **−59,3 %**) |
| Peces | 20,43 MiB · Audio | 4,65 MiB |
| Player | `.wasm` **21,66 MB** · `.data` **15,94 MB** (build del 17-ago) |
| Receiver | sello `rcv 2026-08-17 decos` · HUD de diagnóstico **apagado** por defecto ✅ |

### Configuración crítica que NO hay que tocar

- `Code Optimization = DiskSizeLTO` — ⚠ **no está en git**, lo fuerza `TvProdBuild` por código
- `Managed Stripping = High` (`WebGL: 3`), con `Assets/link.xml` preservando los tipos de URP
- `m_DisableCatalogUpdateOnStart = true` · `NonRecursiveBuilding = true`
- Audio con `loadType: 2` — con `0` hay **OOM y pantalla azul**; `TvProdBuild.PreflightAudio()` aborta el build si falla
- `ctx.start({ disableIdleTimeout: true, maxInactivity: 3600 })`

---

## 2. Qué está validado en hardware real (Xiaomi TV Box S)

| Qué | Cuándo | Resultado |
|---|---|---|
| Estabilidad de sesión larga | 15-ago | 900 s y 420 s, **0 cortes** |
| Carga alta | 15-ago | 25 peces + 6 decos: FPS 37 medio / 17 peor, WASM 191 MB plano |
| Carga normal | 15-ago | 12 peces + 6 decos: FPS 45 / 36, WASM 133 MB plano |
| Audio (3 canales) | 16-ago | Entra por fin. Dado por bueno **de oído**, no medido |
| Stripping High | 16-ago | `.wasm` −14,8 %, sin `TypeLoadException` |
| 20 decos a DXT1 | 17/18-ago | Sin magenta, relieve y detalle intactos |
| Bioluminiscencia | 17-ago | Coral **+20,7 %** de luminancia absoluta |
| 21 materiales a DecoLit | **19-ago** | **`FixMat` 127 → 0** y las 21 decos **pixel-idénticas** |
| Acuario realista final | **19-ago** | 8 peces + 6 decos, 240,7 s, 0 errores, WASM 92 MB, FPS 39 |
| **Techo de carga remedido** | **19-ago** | 25 peces + 6 decos, **421 s**, 0 errores, **WASM 159 MB** (eran 191), FPS avg 37 |

**La causa raíz de los cortes de sesión está resuelta** y era presión de memoria del sistema por el
tamaño del `.wasm` — no Android Doze, ni un cap duro del device, ni el vídeo keepalive.

---

## 3. Lo que SÍ está listo

- **El receiver funciona y es estable.** Sesiones largas sin cortes, memoria plana, FPS aceptables.
- **El contenido está optimizado**: 91 → 80 bundles y las decos a menos de la mitad, sin pérdida
  visual medida.
- **El pipeline tiene red de seguridad**: el build aborta si falta audio o si tiene el `loadType`
  malo; el LTO y el stripping se fuerzan por código; el script de huérfanos revisa los dos frentes
  de R2 y avisa si falta una dependencia local.
- **El HUD de diagnóstico sale apagado** en producción; sólo se enciende con el mensaje `DIAG`.
- **El repo está sincronizado** con GitHub.

---

## 4. ⚠ Lo que NO está listo para una subida «a pro»

### 4.1 🔴 El bucket de R2 está abierto al mundo

```
curl https://pub-…r2.dev/StreamingAssets/aa/catalog.hash   ->  HTTP 200, sin auth
```

**Cualquiera con la URL puede descargarse el catálogo entero y todos los assets** — los 21 GLB de
fotogrametría, los 25 peces, los fondos. Son assets de pago del proyecto.

Hay una spec lista para Worker + JWT en la memoria `project_r2_security`, anotada como *«implementar
antes de marketing tier-1»*. **Mientras el proyecto es privado da igual; en cuanto se promocione,
no.**

🧭 **Este es el único punto que yo consideraría bloqueante** para una subida con difusión.

### 4.2 ✅ Artefactos de diagnóstico — RESUELTO el 19-ago

Borrados **56,72 MB**: `webgl-output-empty.*` (42,75) y `webgl-min.*` (13,94) de `Build/`, más
`index_test.html` de la raíz. Son **reproducibles** (`TvEmptyTestBuild.cs` los reconstruye).

⚠⚠ Se borraron con **lista explícita, nunca con `--delete`**: en la raíz están
`keepalive_black.mp4` —que es lo que mantiene viva la sesión— y `silence.wav`. Borrar el primero
revienta las sesiones largas, que es justo lo que costó meses arreglar. Antes de borrar se comprobó
que el `index.html` de producción sólo referencia `webgl-output.data` y `.wasm`, y después se
verificaron los 7 ficheros críticos uno a uno.

### 4.3 🟡 Cosas validadas «de oído» o sin medir

- **El audio** se dio por bueno escuchándolo, no midiendo. Las burbujas van a 0,08 de volumen.
- ✅ ~~La carga máxima real~~ **REMEDIDA el 19-ago** con el mismo protocolo del 15-ago (25 peces +
  6 decos, 420 s): **WASM 159 MB frente a los 191 MB de entonces — 32 MB menos de heap (−16,8 %)**,
  FPS avg 37 (igual), 421 s sin un solo error. El margen mejoró justo lo que pesaban las decos.

### 4.4 🟡 Huecos que dependen del móvil

- **Editar una deco ya colocada** (girar, escalar, voltear) **no manda ningún UPDATE** → la tele se
  queda desincronizada hasta un reinicio de sesión. Es el hueco funcional más visible.
- `ageScale` de peces: la parte TV está lista, falta build móvil.

### 4.5 🟢 Contradicción abierta, sin riesgo inmediato

`maxInactivity`: el research de julio dice que fijarlo es contraproducente; en disco está a 3600 y
es la configuración con la que se han validado las sesiones de 900 s. **No tocar sin una tanda A/B.**

---

## 5. Si mañana hubiera que publicar

**Mínimo imprescindible:**
1. 🔴 **Cerrar el bucket** (Worker + JWT) — §4.1. **Es lo único bloqueante que queda.**
   Spec completo en [`CAST_R2_AUTH_SPEC.md`](CAST_R2_AUTH_SPEC.md) (13 secciones). **0 €/mes** hasta
   ~3.000 usuarios/día; ~3 días de trabajo; toca **los dos repos** y el Worker lo tiene que crear el
   user en su cuenta de Cloudflare.
2. ✅ ~~Borrar los rigs de diagnóstico~~ **HECHO el 19-ago** (56,72 MB).

**Recomendable antes de difundir:**
3. ✅ ~~Volver a medir el techo de carga~~ **HECHO el 19-ago**: mejor que antes (−32 MB de heap).
4. Cerrar el hueco de editar decos colocadas (requiere el repo móvil).

**Opcional, mejora de producto:**
5. Decimar las 11 mallas de fotogrametría: −14 a −21 MB. **Decisión de calidad del user.**
6. Halo de la bioluminiscencia.

---

## 6. Dónde está cada cosa

| Doc | Para qué |
|---|---|
| [`CAST_NEXT_SESSION_2026-08-20.md`](CAST_NEXT_SESSION_2026-08-20.md) | ⭐ Empezar aquí la próxima sesión: pendientes y trampas |
| [`DECOS_PESO_PARA_MOVIL.md`](DECOS_PESO_PARA_MOVIL.md) | 📄 Para leer en `D:\dev\appquarium-unity\` |
| [`CLAUDE.md`](CLAUDE.md) | Contexto permanente, pipeline de build y comandos de deploy |
| [`CAST_UPDATES.md`](CAST_UPDATES.md) | Protocolo UPDATE en tiempo real |
| Este doc | Foto de estado y valoración para producción |

**Herramientas clave:** `Tools/r2_huerfanos.py` (huérfanos + dependencias locales) ·
`Tools/cast-headless.js` (castear sin navegador) · `Appquarium TV → 📐 Informe de mallas por deco` ·
`Appquarium TV → 🎨 Informe de materiales de decos`

---

## 7. Una nota de método, que es lo más caro que se aprendió

El 19-ago la conversión de materiales costó **tres vueltas completas** de build + deploy +
verificación, con producción rota dos veces. Los tres fallos fueron **el mismo error**: verificar el
efecto secundario en vez del principal.

> Emisión en negro ✅ pero el shader sin cambiar. Shader ✅ pero la textura perdida.

Lo que los atrapó fue **comparar las 21 decos una a una contra un «antes» capturado**. Con dos o
tres decos habrían pasado los tres — de hecho, ninguna de las 3 anclas (que eran el caso de estudio
del que salió todo el trabajo) falló en ninguna de las vueltas.

🧭 **Regla para este proyecto:** cuando un cambio toca N assets, hay que mirar los N. Y en un
proyecto donde los fallos **no dan error** —audio mudo 2 meses, sombras invisibles desde junio,
bioluminiscencia muerta— eso no es exceso de celo: es la única forma de saber.
