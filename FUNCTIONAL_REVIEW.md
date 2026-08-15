# Functional Review — Cast TV vs Mobile App

> ⚠⚠ **OBSOLETO (2026-06-19) — su hallazgo principal es FALSO a día de hoy.**
> Dice «HALLAZGO CRÍTICO: el móvil nunca llama a `SendUpdate`». **Lo llama en 12 sitios**
> (verificado el 2026-08-15 en el repo móvil: `AmbientModeController`, `AquariumManager`,
> `InputHandler`, `DecorationPlacer`, `DecoPanel`, `UIManager`). Los **11 tipos de UPDATE están
> conectados en ambos extremos**, y el `ambient=night` se verificó ya en el device real.
> Otras filas falsas: «feed visual no implementado» (existe `TvFoodManager`), «burbujas no
> existe» (existe y está en git), «biolum y panel debug pendientes de deploy» (desplegados).
> **Peligro concreto:** alguien lo lee y reimplementa la Fase B en el móvil, que ya está hecha.

Mapa completo de funcionalidad: qué llega via Cast, qué está implementado en TV,
qué falta y qué está pendiente en el lado mobile.

Fecha: 2026-06-19 | Player: `dfa1ab4`

---

## Resumen ejecutivo

| Categoría | Estado |
|---|---|
| Estado inicial (INIT) | ✅ Completo |
| Audio | ⚠️ Falta `ambient_bubbles` |
| Updates en tiempo real | ❌ Mobile nunca llama `SendUpdate` |
| Reconexión | ✅ Funciona (mobile envía INIT al reconectar) |
| Reinicialización (2º INIT) | ✅ Corregido |
| Bioluminiscencia nocturna | ⚠️ Fix en código, pendiente de build |
| Panel debug | ⚠️ Fix en código, pendiente de build |
| Feed visual | ❌ No implementado en TV + mobile nunca lo envía |

---

## 1. Estado inicial (INIT al conectar)

El móvil llama `SendFullState()` en `OnCastConnected` — siempre que el usuario conecta
o reconecta el Cast, TV recibe un INIT completo con todo el estado actual.

| Campo | Descripción | TV implementado | Estado | Notas |
|---|---|---|---|---|
| `activeFish` | Peces en el tanque | `FishSpawner.SpawnFish` | ✅ Confirmado | Banggai + Moorish Idol validados |
| `decoJson` | Decoraciones y posiciones | `DecorationPlacer` | ✅ Confirmado | Anclas, cañón, columna validados |
| `bgId` | Fondo del tanque | `TankBackground.SetPreset` | ✅ Implementado | 11 presets con imagen PNG en Resources/Backgrounds/ |
| `subId` | Tipo de sustrato | `DecorationPlacer.SetSubstrate` | ✅ Implementado | 12 presets (arena, grava, musgo, etc.) — colores, sin textura |
| `lightId` | Preset de iluminación LED | `TankLightingController.SetPreset` | ✅ Implementado | 7 presets: white, warm, blue, deep, purple, sunset, cycle |
| `ambientMode` | Modo día/atardecer/noche | `AmbientModeController` | ✅ Implementado | `day` / `sunset` / `night` |
| `fishSpeed` | Multiplicador velocidad peces | `AquariumManager.FishSpeedMultiplier` | ✅ Implementado | Rango 0.5–2.0, aplicado en FishAgent |
| `selectedTankId` | Tamaño del tanque | `TankData` SOs | ✅ Implementado | 4 tanks: `tank_l`, `tank_m`, `tank_nano`, `tank_ocean` |

### Pendientes de validar en TV (implementado pero no confirmado visualmente)

- [ ] **Todos los fondos (`bgId`)** — solo `bg_kelp` validado en devtest. Probar `bg_classic`, `bg_tropical`, `bg_deep`, `bg_abyss`, `bg_cave`, `bg_arctic`, `bg_volcanic`, `bg_jungle`, `bg_wreck`, `bg_night`
- [ ] **Sustrato (`subId`)** — nunca validado en TV. Probar `sub_sand`, `sub_gravel`, `sub_volcanic`, etc.
- [ ] **Luz LED (`lightId`)** — probar `light_warm`, `light_blue`, `light_deep`, `light_purple`, `light_sunset`, `light_cycle`
- [ ] **Modo noche (`ambientMode: night`)** — bioluminiscencia, luz ambiental oscura. Fix de emission scale pendiente de build.
- [ ] **Modo atardecer (`ambientMode: sunset`)** — luz cálida, cielo naranja
- [ ] **Tank size** — solo validado con `tank_l` implícitamente. Probar `tank_m`, `tank_nano`, `tank_ocean`
- [ ] **light_cycle** — cicla colores HSV en `Update()`. ¿Se ve bien en TV?

---

## 2. Updates en tiempo real (durante sesión Cast)

### ⚠️ HALLAZGO CRÍTICO: Mobile nunca llama `SendUpdate`

`CastManager.SendUpdate()` está implementado en mobile pero **no se llama desde ningún sitio**
de la app. Las 4 acciones del usuario durante una sesión Cast (cambiar modo, velocidad, dar comida,
refrescar) NO llegan a la TV hasta que el usuario desconecte y reconecte.

| UPDATE type | TV implementado | Mobile lo envía | Estado real |
|---|---|---|---|
| `ambient` (day/sunset/night) | `AmbientModeController` ✅ | ❌ Nunca | Dead code en TV — la acción mobile no llega |
| `speed` (float) | `AquariumManager.FishSpeedMultiplier` ✅ | ❌ Nunca | Dead code en TV |
| `feed` | `FoodManager.Instance?.SpawnFood` ✅ | ❌ Nunca | No-op doble: mobile no envía + TV usa stub |
| `refresh` | Solo log ⚠️ | ❌ Nunca | Dead code en TV |

**Acción requerida (Fase B — en mobile, fuera de este repo):**
Conectar los eventos UI del móvil a `CastManager.SendUpdate(...)`:
- Botón "Día/Atardecer/Noche" → `SendUpdate("ambient", "day"|"sunset"|"night")`
- Slider velocidad peces → `SendUpdate("speed", speed.ToString())`
- Botón comida → `SendUpdate("feed")`

Hasta que esto esté en mobile, cualquier cambio solo se refleja en TV al reconectar.

---

## 3. Reconexión Cast

| Escenario | Comportamiento | Estado |
|---|---|---|
| Móvil reconecta tras disconnect | `CastManager.OnCastConnected` → `SendFullState()` → nuevo INIT a TV | ✅ Funciona |
| TV recibe 2º INIT (reinicialización) | `DespawnAll` + `RemoveAllDecos` → reinit | ✅ Corregido |
| Móvil desconecta (pantalla bloqueada, etc.) | `OnSenderDisconnected` → solo log → aquarium sigue | ✅ OK (no crash) |
| `disableIdleTimeout: true` | Cast SDK no cierra el receiver por inactividad | ✅ Implementado |

**Corregido 2026-06-19:**
- `AquariumManager.InitializeFromCastStateAsync` ahora llama `fishSpawner.DespawnAll()` + `DecorationPlacer.RemoveAllDecos()` antes de cada reinicialización.
- `DecorationPlacer.RemoveAllDecos()` destruye todos los GameObjects + limpia `_placed`, `_bioLumMats`, `_bioLumLights`.
- **Pendiente validar en TV:** conectar → desconectar → reconectar → confirmar que el acuario reinicializa limpio.

---

## 4. Audio

| Clip | Ruta en Resources | Archivo existe | Estado |
|---|---|---|---|
| Sonido agua | `Audio/ambient_water` | `ambient_water.wav` ✅ | ✅ Funciona |
| Música ambient | `Audio/ambient_music` | `ambient_music.mp3` ✅ | ✅ Funciona |
| Burbujas | `Audio/ambient_bubbles` | ❌ No existe | ❌ Silencio (AudioManager falla silenciosamente) |

**Acción:** Añadir `ambient_bubbles.ogg` (o `.mp3`/`.wav`) a `Assets/Resources/Audio/`.
Fuentes libres: freesound.org "aquarium bubbles" / pixabay.com.
No requiere rebuild de bundles — va en el player (`.data`).

---

## 5. Sistemas visuales

| Sistema | Implementado | Estado | Notas |
|---|---|---|---|
| BubbleSystem | `BubbleSystem.cs` + ParticleSystem | ✅ Implementado | Inicializado en TankController. Partículas, no audio. |
| WaterSurface (tint) | `WaterSurface.SetTint` | ✅ Implementado | Tint viene del sustrato seleccionado |
| PostProcessing | `PostProcessingSetup.cs` | ✅ Implementado | Color filter neutro. Bloom OFF (targetea Cast device) |
| Sombras peces | `FishAgent` shadow | ✅ Implementado | Planar shadow, ajustada por `ShadowPlacer` |
| Bioluminiscencia corales | `DecorationPlacer.SetBioLumStrength` | ⚠️ Fix pendiente deploy | Scale 0.75→0.25 corregido, falta build+deploy |
| Panel debug | `#dbg-panel` en index.html | ⚠️ Fix pendiente deploy | Auto-hide 8s tras AQUARIUM READY, falta build+deploy |
| light_cycle (LED arcoíris) | `TankLightingController.Update()` HSV | ✅ Implementado | Cicla hue a 0.07Hz. Sin probar en TV. |

---

## 6. Funcionalidades mobile NO aplicables en TV

Estas features NO deben replicarse en el receiver — por diseño son mobile-only:

| Feature | Motivo |
|---|---|
| IAP / tienda | TV es solo render, no transacciones |
| Sistema de guardado | TV no persiste estado (stateless receiver) |
| BreedingManager | Sin UI de gestión |
| FoodManager real (partículas de comida) | Posible Fase B si se añade animación de comida |
| InputHandler / toques | TV no tiene pantalla táctil |
| FieldGuide | Sin UI |
| Localización | TV asume inglés/idioma fijo |
| Notificaciones | No aplica en TV |
| Ads | No aplica en TV |

---

## 7. Acción inmediata — antes del próximo build

- [ ] **Añadir `ambient_bubbles.ogg`** a `Assets/Resources/Audio/` — no requiere bundle rebuild, va en el player
- [x] **Corregir 2º INIT duplicados** ✅ 2026-06-19 — `DespawnAll` + `RemoveAllDecos` antes de reinicializar.
  > `AquariumManager.cs:132` + `DecorationPlacer.cs:202`

## 8. Fase B — mobile + TV coordinado

- [ ] Conectar `SendUpdate` desde UIManager del móvil para ambient, speed, feed
- [ ] Feed visual en TV: simple efecto partículas "comida cae desde arriba" cuando llega `type: "feed"`
- [ ] `OnSenderDisconnected` en TV: mostrar overlay "Conecta desde Appquarium" con logo animado en lugar de simplemente continuar

---

## Checklist de sesión de validación en TV (con Xiaomi)

Conectar el móvil con cada configuración y verificar que la TV refleja el estado:

### Backgrounds
- [ ] bg_classic (azul marino estándar)
- [ ] bg_tropical
- [ ] bg_deep
- [ ] bg_abyss
- [ ] bg_cave
- [ ] bg_arctic
- [ ] bg_volcanic
- [ ] bg_jungle
- [ ] bg_wreck
- [ ] bg_night
- [ ] bg_kelp ✅ (validado en devtest)

### Luces LED
- [ ] light_white ✅ (usada implícitamente)
- [ ] light_warm
- [ ] light_blue
- [ ] light_deep
- [ ] light_purple
- [ ] light_sunset
- [ ] light_cycle (arcoíris)

### Sustrato
- [ ] sub_sand
- [ ] sub_white
- [ ] sub_gravel
- [ ] sub_volcanic
- [ ] sub_lava
- [ ] sub_ice
- [ ] sub_moss ✅ (validado en devtest)
- [ ] sub_mud

### Modo ambiente
- [ ] Día ✅ (default)
- [ ] Atardecer
- [ ] Noche (verificar biolum, oscuridad)

### Reconexión
- [ ] Conectar → desconectar → reconectar → ¿estado correcto sin duplicados?
- [ ] Bloquear pantalla móvil → ¿TV sigue corriendo?

### Audio
- [ ] ¿Se oye sonido de agua? (ambient_water.wav)
- [ ] ¿Se oye música? (ambient_music.mp3)
