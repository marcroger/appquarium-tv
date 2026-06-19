# TODO — Appquarium TV

Checklist de issues confirmados, funcionalidad incompleta y mejoras pendientes.
Ordenado por prioridad dentro de cada bloque. Actualizar con fecha al cerrar cada item.

---

## 🐛 Bugs confirmados en TV

- [ ] **Coral emission overflow** — con bloom OFF, `_EmissionColor` HDR de corales bioluminiscentes
  (`hasBioLuminescence=true`) se aplana a color puro saturado en vez de glow suave. Los 6 corales
  afectados (heliopora, distichopora, pocillopora, corallium, etc.) se ven con colores "quemados"
  de noche. Fix: reducir `BioLumEmissionScale` (<0.3) para emision sin bloom, o activar bloom
  con threshold alto (0.95+) solo para emission.
  > `DecorationPlacer.cs:91` — `BioLumEmissionScale = 0.75f`

- [ ] **Doble-slash en settings.json generado por Unity** — `TvAddressablesSetup.cs` genera
  `bundles//catalog_1.2.1.hash` en el CatalogHash path. El archivo local `webgl-output/` ya está
  parcheado manualmente, pero el generador sigue siendo incorrecto. Si alguien hace un New Build
  + Player sin el parche... vuelve a romperse.
  > `Assets/Editor/TvAddressablesSetup.cs` — buscar `CacheHash` o `CatalogHash` en la
  configuración de `ContentCatalogProvider`.

- [ ] **Panel debug visible en producción** — el `#dbg-panel` con logs de arranque es visible
  para el usuario final en la TV durante la carga. Debería ocultarse automáticamente tras N
  segundos (o al recibir `AQUARIUM READY`).
  > `Assets/WebGLTemplates/CastReceiver/index.html` — añadir `setTimeout` o evento desde Unity.

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

- [ ] **Audio** — bundles en R2 existen (`Audio_Remote` group, 2 clips). ¿El AudioManager
  arranca y reproduce en TV? Verificar: ¿se inicializa en `AquariumManager.InitializeFromCastStateAsync`?
  ¿Se oye algo al castear? Sin feedback del device = desconocido.

- [ ] **Feed fish** — `TvSceneBootstrap.ApplyUpdate` maneja `case "feed"` via
  `FoodManager.Instance?.SpawnFood(...)`. `FoodManager` es mobile-only (está en stubs?).
  Verificar si el mensaje llega desde el móvil y si el efecto visual se ve en TV.
  > `Assets/Scripts/Stubs/TvStubs.cs` — ¿tiene FoodManager stub?

- [ ] **`refresh` UPDATE** — el móvil puede enviar `type: "refresh"` que debería reinicializar
  el acuario con el estado nuevo. Actualmente solo loggea: "waiting for new INIT". Verificar si
  el móvil manda un INIT después del refresh, o si TV necesita pedirlo.

- [ ] **Overscan panel** — el index.html tiene CSS de overscan (safe area). Verificar visualmente
  que el debug panel y el aquarium no quedan cortados en TV con overscan real.

---

## 🐟 Contenido pendiente

- [ ] **24 peces restantes** — materiales ya corregidos en disco (FishUnlit body + Sprites/Default
  fins). Para añadir cada pez: `★ Setup Addressables` → `★ New Build` → deploy bundle+catalog.
  Sin player rebuild. Ver workflow en `CLAUDE.md`.

  Prioridad sugerida (más distintivos visualmente primero):
  1. Clownfish (Nemo — icónico)
  2. Blue Tang (Dory)
  3. Lionfish (crines especiales)
  4. Pufferfish
  5. ... (resto según catálogo del móvil)

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

- [ ] **Fix permanente de settings.json** — el archivo local `webgl-output/StreamingAssets/aa/settings.json`
  está parcheado manualmente. Opciones: (A) fix en `TvAddressablesSetup.cs` para generar el
  path correcto, (B) post-process script en el pipeline de build que parchea el archivo al salir.
  Opción A preferida — corrige el origen.

- [ ] **Script de deploy unificado** — actualmente el deploy requiere recordar 4 comandos
  con el orden correcto y el boto3 workaround. Crear `Tools/deploy.ps1` o `Tools/deploy.sh`
  que haga todo: sync player → boto3 settings.json → boto3 catalog.hash → verificar R2.

- [ ] **Sync desde mobile** — último sync: 2026-05-26. Si el móvil ha tenido cambios en
  `DecorationData`, `FishData`, `DecorationPlacer`, `TankBackground`, etc., TV puede estar
  desactualizado. Ejecutar `.\Tools\SyncFromMobile.ps1 -DryRun` para ver diffs pendientes.

---

## 🔒 Fase B (post-launch / cuando haya usuarios reales)

- [ ] **Seguridad R2** — actualmente el bucket es público sin autenticación. Cualquiera puede
  listar y descargar los bundles. Implementar Cloudflare Worker + JWT firmado por el móvil antes
  de cualquier marketing a tier-1. Spec lista en `project_r2_security.md` (memoria).

- [ ] **Reconexión Cast robusta** — si el usuario cierra y reabre la app móvil, el receiver
  debe aceptar el nuevo INIT sin recargar la página. Verificar que `OnSenderConnected` +
  nuevo INIT reinicializa el acuario correctamente sin acumular peces/decos del INIT anterior.

- [ ] **Bioluminiscencia corales — 6 pendientes** — los 6 SOs con `hasBioLuminescence=true`
  necesitan validación visual. Listado: heliopora, distichopora, pocillopora, corallium + 2 más.
  Verificar que el glow queda bien una vez resuelto el bug de emission overflow.

- [ ] **selectedTankId** — `TvAquariumState` tiene `selectedTankId` pero no se usa para cambiar
  la forma/geometría del tanque en TV (solo hay un tanque estándar). Si se añaden tanques
  alternativos en mobile, TV necesita soporte.
