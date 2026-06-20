# TODO — Appquarium TV

Checklist de issues confirmados, funcionalidad incompleta y mejoras pendientes.
Ordenado por prioridad dentro de cada bloque. Actualizar con fecha al cerrar cada item.

---

## 🐛 Bugs confirmados en TV

- [x] **Coral emission overflow** ✅ 2026-06-19 — `BioLumEmissionScale` reducido de 0.75 → 0.25.
  Sin bloom en TV, HDR >0.5 quedaba como color saturado plano. Con 0.25 el glow es sutil y correcto.
  ⚠ Si sync desde mobile restaura 0.75, volver a poner 0.25 (móvil usa bloom, TV no).
  > `DecorationPlacer.cs:91`

- [x] **Doble-slash en settings.json generado por Unity** ✅ 2026-06-19 — `TvBuildPostprocess.cs`
  parchea automáticamente `StreamingAssets/aa/settings.json` tras cada WebGL Player Build.
  Corrige `bundles//` → `bundles/` y `m_DisableCatalogUpdateOnStart: false` → `true`.
  Ya no hace falta parchear manualmente tras deploy.
  > `Assets/Editor/TvBuildPostprocess.cs` (nuevo)

- [x] **Panel debug visible en producción** ✅ 2026-06-19 — Panel se oculta con fade 1.5s,
  8 segundos después de recibir el mensaje "AQUARIUM READY" desde C#. En error (JS ERR /
  ERROR): cancela el hide y vuelve visible.
  > `Assets/WebGLTemplates/CastReceiver/index.html`

---

## ⚙️ Funcionalidad incompleta

- [ ] **Bioluminiscencia — fade día/noche no conectado** — `DecorationPlacer` tiene `FadeBioLum()`
  pero el trigger en TV depende de que `AmbientModeController` llame al placer con el modo
  correcto. Verificar si el fade in/out se activa al cambiar a `night` via Cast UPDATE.
  > `AquariumManager.cs:168` / `DecorationPlacer.cs:99`

- [ ] **Disconnect sender → aquarium congelado** — cuando el móvil se desconecta (o bloquea),
  `OnSenderDisconnected` solo loggea. El acuario sigue moviéndose pero nadie puede reconectar
  sin cerrar y reabrir Cast. Implementar: al recibir disconnect → mostrar overlay "Conecta desde
  Appquarium" o al menos `alwaysAmbient=true` para que siga animado indefinidamente.
  > `Assets/Scripts/Core/CastReceiver.cs:67` — `OnSenderDisconnected`

- [ ] **Audio** — `ambient_water.wav` + `ambient_music.mp3` + `ambient_bubbles.wav` incluidos en build.
  Pendiente: verificar que se oyen en Xiaomi tras deploy.

- [x] **Feed visual** ✅ 2026-06-19 — `TvFoodManager.cs` sustituye el stub null.
  `FeedAll()` spawna 2–5 FoodItems procedurales en la superficie. FishBrain los detecta
  via `CheckForFood()→GetNearestFood()→TriggerFeed()`. Auto-feed cada 4 min.
  Mando TV: botón Enter = startle, F/MediaPlayPause = feed. Build 2026-06-19 activo.
  > `Assets/Scripts/Core/TvFoodManager.cs` (nuevo)

- [ ] **`refresh` UPDATE** — `CastManager.SendUpdate("refresh")` existe en mobile pero nunca
  se llama. En TV solo loggea "waiting for new INIT". Cuando mobile lo implemente (Fase B),
  TV ya tiene el handler correcto: si mobile manda INIT después del refresh, funciona.
  Si solo manda refresh sin INIT → TV necesita pedir reinit explícitamente.
  > Mobile: `CastManager.cs:156` | TV: `TvSceneBootstrap.cs` `case "refresh"`

- [ ] **Overscan panel** — el index.html tiene CSS de overscan (safe area). Verificar visualmente
  que el debug panel y el aquarium no quedan cortados en TV con overscan real.

---

## 🐟 Contenido pendiente

- [x] **25 peces** ✅ 2026-06-08 — todos buildeados y en R2. Catalog referenciado correctamente.
  (El TODO anterior decía "24 restantes" — estaba desactualizado; el build del 08-jun los completó todos.)

---

## 🚀 Rendimiento y calidad

- [ ] **Medir FPS exacto** — confirmado "fluido" pero sin número. Añadir `Time.unscaledDeltaTime`
  en el panel debug o usar el FPS meter de `Tools/fps-check.js`. Dato útil para decidir si
  subir renderScale.

- [ ] **Evaluar renderScale 0.85** — si FPS aguanta por encima de 25fps estable, subir de 0.7
  a 0.85 mejora nitidez notable (72% píxeles vs 49%). Requiere player rebuild.
  > `TvSceneBootstrap.cs:65` — `urpAsset.renderScale = 0.7f`

- [ ] **Bloom selectivo para bioluminiscencia** — bloom OFF global es correcto para rendimiento,
  pero podría habilitarse solo con threshold muy alto (0.95) para que solo los objetos HDR lo
  capturen. Evaluar si el coste es asumible en Mali-G31.

- [ ] **Arranque frío** — medido 27s en Xiaomi (timeout Cast = 30s). Nuevo player debería ser
  mejor por async init, pero no medido. Si sigue cerca de 30s → riesgo de disconnect.
  Solución: aumentar timeout en `ctx.start()` si la API lo permite, o mostrar "Connecting..."
  en el receiver antes de que Unity cargue.

---

## 🏗 Deploy / infraestructura

- [x] **Fix permanente de settings.json** ✅ 2026-06-19 — `TvBuildPostprocess.cs` parchea
  automáticamente tras cada WebGL Player Build. No hace falta intervención manual.
  > `Assets/Editor/TvBuildPostprocess.cs`

- [ ] **Script de deploy unificado** — actualmente el deploy requiere recordar 4 comandos
  con el orden correcto y el boto3 workaround. Crear `Tools/deploy.ps1` o `Tools/deploy.sh`
  que haga todo: sync player → boto3 settings.json → boto3 catalog.hash → verificar R2.

- [ ] **Sync desde mobile** — último sync: 2026-05-26. Si el móvil ha tenido cambios en
  `DecorationData`, `FishData`, `DecorationPlacer`, `TankBackground`, etc., TV puede estar
  desactualizado. Ejecutar `.\Tools\SyncFromMobile.ps1 -DryRun` para ver diffs pendientes.

---

## 📡 Updates en tiempo real

> **2026-06-20:** TV ya tiene handlers completos para todos los tipos de UPDATE.
> Handle registry implementado — los bundles se liberan correctamente en remove y en reconexión.
> Pendiente: conectar llamadas en el lado mobile (Fase B). Ver `CAST_UPDATES.md` para el protocolo completo.

### TV ✅ — pendiente mobile

- [x] **TV handler: add_fish** ✅ 2026-06-20 — carga bundle Addressable si no estaba en INIT, spawna pez.
  > `TvSceneBootstrap.cs` `case "add_fish"` → `AddFishAsync()`
  > Mobile: añadir call en `AquariumManager.AddFishToTank()` — ver `CAST_UPDATES.md §2`

- [x] **TV handler: remove_fish** ✅ 2026-06-20 — despawna pez, libera bundle si era runtime.
  > `TvSceneBootstrap.cs` `case "remove_fish"` → `RemoveFish()`
  > Mobile: añadir call en `AquariumManager.RemoveFishFromTank()` — ver `CAST_UPDATES.md §2`

- [x] **TV handler: add_deco** ✅ 2026-06-20 — carga bundle, coloca en posición enviada por mobile.
  > `TvSceneBootstrap.cs` `case "add_deco"` → `AddDecoAsync()`
  > Mobile: añadir call en `DecorationPlacer.PlaceAt()` — ver `CAST_UPDATES.md §2`

- [x] **TV handler: remove_deco** ✅ 2026-06-20 — quita instancia, libera bundle si no quedan más.
  > `TvSceneBootstrap.cs` `case "remove_deco"` → `RemoveDeco()`
  > Mobile: añadir call en `DecorationPlacer.Remove()` — ver `CAST_UPDATES.md §2`

- [x] **TV handler: change_bg** ✅ 2026-06-20 — `TankBackground.SetPreset(bgId)`. Sin Addressables.
  > `TvSceneBootstrap.cs` `case "change_bg"` → `ChangeBg()`
  > Mobile: añadir call en DecoPanel/TankBackground — ver `CAST_UPDATES.md §2`

- [x] **TV handler: change_sub** ✅ 2026-06-20 — `DecorationPlacer.SetSubstrate(subId)`. Sin Addressables.
  > `TvSceneBootstrap.cs` `case "change_sub"` → `ChangeSub()`
  > Mobile: añadir call en DecoPanel/DecorationPlacer — ver `CAST_UPDATES.md §2`

- [x] **TV handler: change_light** ✅ 2026-06-20 — `TankLightingController.SetPreset(lightId)`. Sin Addressables.
  > `TvSceneBootstrap.cs` `case "change_light"` → `ChangeLight()`
  > Mobile: añadir call en DecoPanel/TankLightingController — ver `CAST_UPDATES.md §2`

- [ ] **ambient** — mobile: conectar botón modo día/noche a `SendUpdate("ambient", mode)`
  TV: ✅ implementado (`AmbientModeController`)

- [ ] **speed** — mobile: conectar slider velocidad peces a `SendUpdate("speed", value)`
  TV: ✅ implementado (`AquariumManager.FishSpeedMultiplier`)

- [ ] **feed** — mobile: conectar botón comida a `SendUpdate("feed")`.
  TV: ✅ `TvFoodManager` implementado 2026-06-19.

---

## 🔒 Fase B (post-launch / cuando haya usuarios reales)

- [ ] **Seguridad R2** — actualmente el bucket es público sin autenticación. Cualquiera puede
  listar y descargar los bundles. Implementar Cloudflare Worker + JWT firmado por el móvil antes
  de cualquier marketing a tier-1. Spec lista en `project_r2_security.md` (memoria).

- [x] **Reconexión Cast robusta** ✅ 2026-06-19 — `InitializeFromCastStateAsync` ahora llama
  `fishSpawner.DespawnAll()` + `DecorationPlacer.RemoveAllDecos()` antes de cada INIT.
  Un 2º INIT (reconexión) limpia el estado anterior sin duplicar peces ni decos.
  > `AquariumManager.cs:132` + `DecorationPlacer.cs:202`

- [ ] **Bioluminiscencia corales — 6 pendientes** — los 6 SOs con `hasBioLuminescence=true`
  necesitan validación visual. Listado: heliopora, distichopora, pocillopora, corallium + 2 más.
  Verificar que el glow queda bien una vez resuelto el bug de emission overflow.

- [ ] **selectedTankId** — `TvAquariumState` tiene `selectedTankId` pero no se usa para cambiar
  la forma/geometría del tanque en TV (solo hay un tanque estándar). Si se añaden tanques
  alternativos en mobile, TV necesita soporte.
