# Peso de las decos — qué llevarse al repo MÓVIL

> Escrito el 2026-08-19 desde el repo TV, tras bajar las 54 decos de **149,8 → 61,03 MB (−59,3 %)**
> con dos palancas independientes, ambas validadas en el Xiaomi TV Box S.
>
> ⚠⚠ **El trabajo se hace desde `D:\dev\appquarium-unity\`. Desde TV NO se toca el repo móvil.**
> Este documento es para leerlo *allí*, con Claude, y decidir qué aplica.

---

## 0. Por qué esto aplica al móvil

En TV el peso importa por **descarga desde R2 y memoria del device**. En el móvil importa por el
**tamaño de instalación de la app**, que es un número que ve el usuario en la ficha de la store.

Las dos palancas aplican porque **ninguna es específica de WebGL**:

- La **palanca 1** es un problema del importador de **GLTFast**, no del target.
- La **palanca 2** es cómo **Unity decide qué texturas empaqueta**, que es igual en Android.

Y sobre todo: **los 21 GLB y los packs FBX son los mismos ficheros** — TV los sincroniza *desde* el
móvil, así que allí están intactos y sin optimizar.

---

## 1. Palanca 1 — texturas embebidas en GLB (la grande)

### El problema

GLTFast decodifica las texturas embebidas en un `.glb` a **RGBA32** (4 bytes/píxel) y **no expone
compresión**. Como son texturas *dentro* de un asset importado por un ScriptedImporter, **se saltan
el override de plataforma**: da igual lo que pongas en los Import Settings, no les llega.

Comprobado en TV: `TvBuildTools.ApplyTextureOverride()` recorría `t:Texture2D` y hacía
`AssetImporter.GetAtPath(path) as TextureImporter`; para un `.glb` ese cast da **null** y se las
saltaba en silencio. **La reducción de texturas nunca tocó las decos GLB.**

### La solución

1. **Extraer** las texturas del GLB a ficheros sueltos: `Tools/extract_glb_textures.py`
   (genera `tex_N.jpg` + un `mapeo.txt` de material → índice de imagen). **Es agnóstico de
   plataforma: se reutiliza tal cual.**
2. **Importarlas comprimidas** y construir un **prefab nuevo** que use las mallas del GLB con
   materiales que apunten a esas texturas.
3. **Reapuntar** el `DecorationData` a ese prefab.

La lógica está en `Assets/Editor/TvDecoOptimize.cs`.

### ⚠⚠ El único cambio obligatorio: el formato

**DXT1 es de escritorio/WebGL y NO vale en Android.** Hay que cambiar el
`SetPlatformTextureSettings` a:

```csharp
ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
    name           = "Android",              // <- NO "WebGL"
    overridden     = true,
    maxTextureSize = 1024,
    format         = TextureImporterFormat.ASTC_6x6,   // o ETC2_RGB4
    textureCompression = TextureImporterCompression.Compressed,
});
```

- **ASTC 6x6** — recomendado. Buena calidad, ~3,56 bits/píxel.
- **ETC2 RGB** — más compatible con equipos viejos, 4 bits/píxel.

Frente a los 4 **bytes**/píxel de RGBA32 eso es un factor de **4,5× a 8×**, o sea que el orden de
magnitud del ahorro se mantiene — pero **el número exacto hay que medirlo allí**.

### Lo que rindió en TV

| | antes | después | Δ |
|---|---|---|---|
| Las 20 optimizadas | ~131 MB | ~51 MB | **−61 %** |
| Estatuas y columnas (3 texturas) | | | −70 a −78 % |
| Corales y conchas (1 textura) | | | −45 a −52 % |

🧭 **El % lo predice la proporción textura/malla.** Medido con texturas idénticas (1024² RGBA32):
`lambis_shell` con 12.498 triángulos dio **−76,9 %**; `linckia_laevigata` con 100.000 dio
**−36,8 %**. **Cuenta triángulos antes de estimar nada.**

---

## 2. Palanca 2 — el shader del material decide qué texturas viajan

### El problema

**Unity sólo empaqueta las texturas correspondientes a propiedades que declara el shader ACTIVO del
material.** Un material en URP/Lit declara albedo, normal, metallic/smoothness, AO, detail y
emission; si el shader del juego no usa esos mapas, viajan igual y ocupan.

En TV se descubrió por una anomalía: las 3 anclas comparten prefab y malla (2.512 triángulos) pero
pesaban **0,150 / 0,708 / 0,721 MB**. La diferencia era el shader del `overrideMaterial`.

### ⚠ Aquí es donde el móvil se parece MENOS a TV

En TV el runtime ya tiraba esos mapas (`FixNonURPMaterials` reconstruye el material como `DecoLit`,
que sólo declara `_MainTex`/`_Color`), así que quitarlos era **gratis, sin coste visual**.

**En el móvil eso probablemente NO se cumple**: allí corre URP de verdad y esos mapas seguramente
**sí se usan** (normal map = relieve real, metallic/smoothness = brillo). Antes de tocar nada:

1. ¿Qué shader usan realmente las decos en el móvil?
2. ¿Ese shader **lee** el normal / metallic / AO?
3. Si los lee, **quitarlos SÍ cambia el aspecto** → es una decisión de calidad, no una optimización
   gratis.

🧭 Regla: en TV esta palanca era «quitar lo que ya se descartaba». En el móvil, mientras no se
compruebe lo contrario, hay que tratarla como **«bajar calidad a cambio de MB»**.

### Lo que sí es directamente aprovechable

El **método de auditoría**: `Assets/Editor/TvDecoMaterialFix.cs` en modo informe recorre los
`DecorationData`, resuelve prefab + `overrideMaterial`, y saca un CSV con material, shader, mapas
asignados y qué decos lo usan. Eso allí dice **exactamente cuánto peso hay en mapas PBR**, y a
partir de ahí se decide.

---

## 3. Palanca 3 — las mallas (sin explotar en ningún sitio)

Medido en TV con `Assets/Editor/TvDecoMeshReport.cs` (cuenta triángulos y vértices del prefab de
cada `DecorationData`, y también sirve tal cual en el móvil):

- 42 decos con malla + 12 sustratos sin malla. **1.429.275 triángulos**, mediana 13.536.
- ⚠ **11 decos están clavadas en ~100.000 triángulos exactos** — es el tope de decimación con el
  que vinieron del proveedor de fotogrametría, no una casualidad. Son 7 corales, 2 conchas, la
  estrella y el casco.
- Esas 11 son el **77 % de los triángulos** y el **52 % del peso**, y en ellas la malla es **~79 %
  de su bundle**.
- Decimarlas a 50k → **−14 MB**; a 25k → **−21 MB** (estimado, no medido).

**En el móvil esto vale igual o más**: la geometría pesa en el APK/AAB *y* en memoria *y* en GPU. Y
100.000 triángulos para un coral en una pantalla de móvil es aún más generoso que en una tele.

⚠ Cuesta calidad y **se ve** — a diferencia del cambio de formato de textura. Es decisión del user.

---

## 4. Cómo medir allí (el equivalente de los bundles de R2)

En TV la unidad de medida es el `.bundle` de cada deco. En el móvil los equivalentes son:

- **Play Asset Delivery**: poner `assetBundleName` por grupo (p. ej. `pack_decos_greek`) y medir el
  tamaño de cada pack.
- **El AAB entero**, con el *Build Report* de Unity para el desglose por asset.

⚠⚠ **La trampa de medición que costó dos cifras falsas en TV:** no midas con `ls -S` ni cogiendo el
fichero más grande por nombre — coge huérfanos de builds viejos. **Filtra siempre por lo que el
build actual referencia de verdad, en AMBOS lados de la comparación.**

⚠ Y las cifras de estos documentos son **MB decimales (10⁶)**. Medir en MiB da un ~4,9 % menos y
parece que las cosas no cuadran.

---

## 5. Ficheros que se llevan tal cual

| Fichero (en el repo TV) | Reutilizable | Qué tocar |
|---|---|---|
| `Tools/extract_glb_textures.py` | **Sí, tal cual** | nada — es agnóstico de plataforma |
| `Assets/Editor/TvDecoOptimize.cs` | La lógica | **formato → ASTC/ETC2**, `name = "Android"`, el shader destino y la lista de decos |
| `Assets/Editor/TvDecoMeshReport.cs` | **Sí, casi tal cual** | sólo la ruta de los `DecorationData` |
| `Assets/Editor/TvDecoMaterialFix.cs` | El **modo informe** | ⚠ el modo convertir NO: asume que el runtime descarta los mapas PBR, y en móvil no es así |

---

## 6. Las trampas que ya se pagaron aquí — no repetirlas

1. **Cambiar el shader de un material NO parte de cero.** Unity conserva las propiedades cuyo
   nombre coincide entre el shader viejo y el nuevo. Si el destino declara `_EmissionColor` y el
   origen lo traía en blanco, el objeto sale **como un bloque blanco reventado**. Pasó con 9 decos.
2. **`CopyPropertiesFromMaterial` NO asigna el shader**, sólo copia valores. Un material así
   *parece* convertido —emisión limpia, slots vacíos, el bundle hasta adelgaza— y **sigue en el
   shader viejo**. Asigna el shader **primero**.
3. **URP/Lit declara `_BaseMap`, NO `_MainTex`.** Leer la textura base por el nombre equivocado
   devuelve null sin error, y el resultado son objetos **blancos sin textura**. Pasó con 20 de 21.
4. **Ejecutar siempre sobre materiales limpios** (restaurados de git). Un material que quedó a
   medias en un intento anterior **miente** en la siguiente lectura.
5. **Un `LogWarning` dentro de un lote es invisible.** El aviso de «sin textura base» saltó **20
   veces** y nadie lo miró, porque sólo se contaban errores. Si algo invalida el resultado, **tiene
   que abortar**.
6. 🧭 **El patrón común de los tres fallos: verificar el efecto secundario en vez del principal.**
   Emisión en negro ✅ pero shader sin cambiar. Shader ✅ pero textura perdida. Comprueba
   **exactamente lo que querías conseguir**, y en el resultado final, no en el intermedio.
7. ⚠ **Una cifra que mejora de más es sospechosa.** Un intento dio «−13,0 %» en vez de −9,5 % y
   parecía mejor: faltaban las texturas albedo.

---

## 7. Orden sugerido allí

1. **Medir primero, sin tocar nada.** Portar `TvDecoMeshReport` y el modo informe de
   `TvDecoMaterialFix`. Con eso sabes, por deco, cuánto es malla y cuánto textura.
2. **Palanca 1 sobre una sola deco** (una estatua griega, que en TV fue el mejor caso: −79 %).
   Medir el AAB antes y después. Validar en un móvil real.
3. Si sale, **lote completo** de las decos de GLB.
4. **Palanca 2 sólo tras comprobar si el shader del juego lee esos mapas.** Si los lee, es decisión
   de calidad.
5. **Mallas** al final, y con el user delante: es la única que se ve.
