# SYNC_NOTES — Sincronización con appquarium-unity mobile

**Proyecto TV (este):** receiver Cast Unity WebGL — `github.com/marcroger/appquarium-tv`
**Proyecto mobile (separado):** `D:\dev\appquarium-unity\` — `github.com/marcroger/appquarium-unity` (no público)

Este proyecto NO comparte código vía submódulo o package. Es una **copia local** de los scripts/assets necesarios del mobile. Esto significa:

> ⚠ **Cuando cambias algo en mobile que afecta al receiver, hay que re-copiar manualmente.**

---

## Snapshot inicial

**Fecha:** 2026-05-22
**Commit mobile de origen:** `481485e62680164a2e20c9b91a3391d012eb27d8`
**Tag:** `feat(cast): WebGL receiver build con strip auto + Twitter lessons + Cast icon`

Toda la copia inicial se hizo desde ese commit.

---

## Inventario de archivos copiados desde mobile

### Scripts (Assets/Scripts/)

| Categoría | Archivos copiados | Razón |
|---|---|---|
| `Fish/` | FishAgent, FishBrain, FishProceduralAnimator, NeedsModule, SteeringController | Comportamiento de peces |
| `Tank/` | BubbleSystem, DecorationPlacer, TankBackground, TankController, TankLightingController, WaterSurface | Tank visual + decoración |
| `Data/` | DecorationData, FishData, TankData | Definiciones ScriptableObject |
| `Core/` | AmbientModeController, AquariumCameraController, AquariumManager, AudioManager, CastReceiver, CastDataTypes, FishSpawner, FoodItem, PostProcessingSetup, TvSceneBootstrap | Core runtime (sin BreedingManager, FoodManager, InputHandler, CastManager). **CastManager.cs borrado 2026-05-25** — era el sender móvil, duplicado por error. Los 5 data types compartidos están ahora en `CastDataTypes.cs`. |
| `Utils/` | AppFlags, AppVersion, CatalogLoader | Mínimo necesario |

### Scripts NO copiados (mobile-only)

Mantener fuera del TV project:
- `Scripts/UI/*` — todo (UIManager, paneles, FishInspectorUI, etc.)
- `Scripts/Core/BreedingManager.cs` — no breeding en TV
- `Scripts/Core/FoodManager.cs` — solo input mobile spawnea comida (en TV viene vía Cast event)
- `Scripts/Core/InputHandler.cs` — no input en TV
- `Scripts/Utils/AdService.cs`, `IAPService.cs`, `AnalyticsService.cs`, `DailyRewardManager.cs`, `NotificationService.cs`, `PackCatalog.cs`, `ReviewPromptManager.cs`, `SaveSystem.cs`, `ScreenshotShare.cs`, `WeeklyDecoManager.cs`, `WeeklyEnvManager.cs`, `WeeklyFishManager.cs`, `LocalizationManager.cs`, `AssetPackManager.cs`, `AssetBundleLoader.cs`, `UITextures.cs`
- `Assets/Editor/AppquariumEditor.cs` — menu items mobile-specific (este proyecto tiene su propio Editor script)

### Assets

| Tipo | Origen mobile | Destino TV |
|---|---|---|
| FishData SOs | `ScriptableObjects/Fish/` (25 archivos) | igual |
| Decoration/Substrate SOs | `ScriptableObjects/Decorations/` (54 archivos) | igual |
| TankData SOs | `ScriptableObjects/Tanks/` (4 archivos) | igual |
| Fish catalog JSON | `Resources/Data/fish_catalog.json` | igual |
| Decoration catalog JSON | `Resources/Data/decoration_catalog.json` | igual |
| Background textures | `Resources/Backgrounds/` (24 MB) | igual |
| Substrate textures | `Resources/Substrates/` (31 MB) | igual |
| Audio loops | `Resources/Audio/` (7 MB — ambient_water + ambient_music) | igual |
| Pack 24 fish models | `ThirdParty/Mikhail Nesterov/Global Reef Fish Pack/` (~1.4 GB, sin SourceFiles ni BuiltinRP) | igual |
| Emperor angelfish | `ThirdParty/Mikhail Nesterov/Emperor Angelfish/` (65 MB) | igual |
| Decoraciones third-party | `ThirdParty/Animated PBR Chest Demo, Cannon, Corals, GreekColumns, GreekStatues, HQ Rocks, Props, Shells, Stylized Rock Pack` | igual |
| Prefabs | `Prefabs/Fish/FishBehavior.prefab`, `Prefabs/FoodItem.prefab` | igual |

### Excluido por defecto al copiar ThirdParty

Por gitignore + tamaño git-incompatible:
- `**/SourceFiles/` (zips Blender/Maya, no runtime)
- `**/Render Pipeline Content - Built-in/` (variante Built-in RP, no usamos)
- `**/*.zip`

---

## Cuándo re-sincronizar

### 🔄 Cambia behavior de peces

Triggers (en mobile):
- Edit `Scripts/Fish/FishAgent.cs`, `FishBrain.cs`, `SteeringController.cs`, `NeedsModule.cs`, `FishProceduralAnimator.cs`
- Edit `Scripts/Data/FishData.cs` (class def)
- Edit cualquier `ScriptableObjects/Fish/*.asset` (stats, prefab refs)

Acción: re-copiar el archivo modificado (.cs o .asset + .meta) a este proyecto.

### 🔄 Cambia tank/decoración behavior

Triggers:
- Edit `Scripts/Tank/*.cs`
- Edit `Scripts/Core/AmbientModeController.cs`, `AquariumCameraController.cs`, `PostProcessingSetup.cs`
- Edit `ScriptableObjects/Decorations/*.asset`
- Edit `Resources/Data/decoration_catalog.json` o `fish_catalog.json`

Acción: re-copiar el archivo + .meta.

### 🔄 Cambia el sistema Cast

Triggers:
- Edit `Scripts/Core/CastManager.cs` en mobile (data types: `TvAquariumState`, `CastMessage`, `TvUpdateMessage`, `TvFishEntry`, `DecoPlacementList`) — IMPORTANTE: estos data types deben coincidir entre sender (mobile, en `CastManager.cs`) y receiver (TV, en `CastDataTypes.cs`). Si cambian, **copiar SOLO los `[Serializable]` data types** a `CastDataTypes.cs` del TV, NUNCA copiar la clase `CastManager` entera (es sender-only).
- Edit `Scripts/Core/CastReceiver.cs` en mobile → copiar al TV.
- Edit `Scripts/Core/TvSceneBootstrap.cs` en mobile → copiar al TV (si aún existe en mobile; tras la separación del 22-may puede que solo viva en TV).
- Edit `Plugins/Android/.../CastPlugin.java` en mobile → no afecta al TV.

Acción: re-copiar el .cs cambiado. Si es un cambio de protocolo de mensajes, verificar compatibilidad con senders previos (usuarios con app vieja del Play Store).

### 🔄 Añades especie nueva al Pack 24 (o pez 26)

Acción:
1. Copiar el nuevo prefab visual desde `Assets/ThirdParty/Mikhail Nesterov/...`
2. Copiar el nuevo FishData `.asset` + `.meta` a `ScriptableObjects/Fish/`
3. Actualizar `Resources/Data/fish_catalog.json`

### 🔄 Cambias decoración nueva

Acción:
1. Copiar el GLB/FBX al subfolder de `ThirdParty/`
2. Copiar el nuevo DecorationData `.asset` + `.meta`
3. Actualizar `decoration_catalog.json`

### ❌ NO sincronizar (UI mobile)

Estos NO afectan al receiver, NO re-sincronizar:
- Cualquier cambio en `Scripts/UI/`
- `UIManager`, `FishInspectorUI`, `ShopPanel`, `CollectionPanel`, `OvularioPanel`, `SettingsPanel`, `FieldGuidePanel`, etc.
- `Resources/FishPortraits/`, `Resources/DecoPortraits/`, `Resources/FieldGuidePhotos/`, `Resources/BreedingUI/`, `Resources/NavIcons/`, `Resources/Fonts/`, `Resources/Localization/`
- `Scripts/Core/BreedingManager.cs`, `FoodManager.cs`, `InputHandler.cs`
- Cualquier IAP / Ad / Save / Analytics / Notification / Daily Reward / Weekly Manager / Pack Catalog code
- `Plugins/Android/*` (excepto CastPlugin.java mencionado arriba)
- `Assets/Editor/AppquariumEditor.cs` (mobile-specific menu items)

---

## Comando de re-sync rápido (PowerShell)

Caso típico: cambiaste `FishBrain.cs` en mobile, copialo al TV project.

```powershell
$src = "D:\dev\appquarium-unity\Assets"
$dst = "D:\dev\appquarium-tv-unity\Assets"
$file = "Scripts\Fish\FishBrain.cs"
Copy-Item "$src\$file" "$dst\$file" -Force
Copy-Item "$src\$file.meta" "$dst\$file.meta" -Force
Write-Output "Synced $file"
```

Para cambios mayores, usar el script `SCRIPTS/resync.ps1` (TODO crear).

---

## Auditar drift (futuro)

Si dudas si los archivos están sincronizados, comparar:

```powershell
$mobile = "D:\dev\appquarium-unity\Assets"
$tv = "D:\dev\appquarium-tv-unity\Assets"
foreach ($f in @("Scripts\Fish\FishBrain.cs","Scripts\Tank\TankController.cs")) {
    $h1 = (Get-FileHash "$mobile\$f").Hash
    $h2 = (Get-FileHash "$tv\$f").Hash
    if ($h1 -ne $h2) { Write-Output "DRIFT: $f" }
    else { Write-Output "OK: $f" }
}
```

---

## Historial de syncs

| Fecha | Commit mobile | Notas |
|---|---|---|
| 2026-05-22 | `481485e` | Snapshot inicial. Copia completa. |
| _futuro_ | — | — |

Actualizar esta tabla cada vez que se hace un re-sync significativo.
