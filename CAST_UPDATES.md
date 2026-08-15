# CAST_UPDATES.md — Real-time Cast UPDATE protocol

Documenta el protocolo de mensajes UPDATE para sincronización en tiempo real entre el móvil y el receiver TV.

---

## Arquitectura

El móvil envía mensajes al TV por el canal Cast Custom Channel.
Cada mensaje tiene el formato `CastMessage { type, payload }`.

| Campo   | Valor |
|---------|-------|
| `type`  | `"INIT"` o `"UPDATE"` |
| `payload` | JSON serializado del estado o del update |

Para INIT: `payload = JsonUtility.ToJson(TvAquariumState)`.
Para UPDATE: `payload = JsonUtility.ToJson(TvUpdateMessage { type, value })`.

```
Mobile CastManager.SendUpdate(updateType, value)
  └── Sends: { "type": "UPDATE", "payload": "{\"type\":\"...\",\"value\":\"...\"}" }
TV CastReceiver.OnMessageReceived(json)
  └── CastMessage.type == "UPDATE" → TvUpdateMessage → TvSceneBootstrap.ApplyUpdate()
```

---

## Tipos de UPDATE implementados en TV (2026-06-20)

### Existentes antes de esta sesión

| type | value | Descripción |
|------|-------|-------------|
| `ambient` | `"day"`, `"sunset"`, `"night"` | Modo ambiental (luz + ambiente) |
| `speed` | `"1.5"` (float) | Multiplicador velocidad peces |
| `feed` | `""` | Spawna comida visual, peces van a comer |
| `startle` | `""` | Peces huyen (scatter) |
| `refresh` | `""` | TV espera nuevo INIT (no hace nada solo) |

### Nuevos en 2026-06-20

| type | value | Descripción |
|------|-------|-------------|
| `add_fish` | JSON `TvAddFishPayload` | Spawna un pez. Carga bundle si no estaba en INIT. |
| `remove_fish` | `speciesId` (string) | Elimina pez(ces) de esa especie. Libera bundle si era runtime. |
| `add_deco` | JSON `TvAddDecoPayload` | Coloca una deco. Carga bundle si no estaba en INIT. |
| `remove_deco` | `instanceId` (string) | Quita una instancia de deco. Libera bundle si no queda ninguna del tipo. |
| `change_bg` | `bgId` (string) | Cambia fondo. Sin Addressables — presets hardcoded en TankBackground. |
| `change_sub` | `subId` (string) | Cambia sustrato. Sin Addressables — presets hardcoded en DecorationPlacer. |
| `change_light` | `lightId` (string) | Cambia preset de iluminación. Sin Addressables — presets hardcoded en TankLightingController. |

---

## Structs de payload

### `TvAddFishPayload` (serializado en `TvUpdateMessage.value`)

```csharp
[Serializable]
public class TvAddFishPayload {
    public string speciesId;  // "fish_moorish_idol"
    public string nickname;   // "Nemo"
}
```

Mobile serializa: `value = JsonUtility.ToJson(new TvAddFishPayload { speciesId, nickname })`

### `TvAddDecoPayload` (serializado en `TvUpdateMessage.value`)

```csharp
[Serializable]
public class TvAddDecoPayload {
    public string  instanceId;  // "deco_coral_brain_0"
    public string  itemId;      // "deco_coral_brain" (clave Addressable)
    public Vector3 position;    // coordenadas mundo del tanque
    public float   scaleFactor; // 1.0 = escala normal
    public bool    flipped;     // espejo horizontal
    public float   rotationY;   // rotación en grados
}
```

---

## Gestión de memoria en TV (Addressables)

El TV mantiene dos registros de handles:

| Registro | Descripción |
|----------|-------------|
| `_initFishHandles / _initDecoHandles` | Handles de assets cargados en INIT. Se liberan en el siguiente INIT (reconexión). |
| `_runtimeFishHandles / _runtimeDecoHandles` | Handles cargados en runtime por `add_fish` / `add_deco`. Se liberan en `remove_*` cuando no quedan instancias, o en el siguiente INIT. |

**Regla de oro:** un bundle de pez/deco se libera cuando:
1. No quedan instancias activas del asset **Y**
2. El handle era runtime (no de INIT) — los handles de INIT se liberan en bloque en el siguiente INIT.

`change_bg`, `change_sub`, `change_light` → **sin Addressables** — usan presets hardcoded, son síncronos e instantáneos.

---

## Fase B — implementada en mobile (2026-06-22)

`CastManager.SendUpdate()` ya se llama en todos los puntos de acción del usuario.
`TvAddFishPayload` y `TvAddDecoPayload` añadidos en `CastManager.cs` del mobile.

| Update type  | Fichero mobile            | Método / punto de llamada                                   |
|--------------|---------------------------|-------------------------------------------------------------|
| `add_fish`   | `AquariumManager.cs`      | `AddFishToTank()` — tras `SaveSystem.Save`                  |
| `remove_fish`| `AquariumManager.cs`      | `RemoveFishFromTank()` — tras `SaveSystem.Save`             |
| `speed`      | `AquariumManager.cs`      | `FishSpeedMultiplier` setter — tras `SaveSystem.Save`       |
| `add_deco`   | `DecorationPlacer.cs`     | `PlaceAt()` — al final, solo si `!fromSave`                 |
| `remove_deco`| `DecorationPlacer.cs`     | `Remove()` — tras `RemoveGameObject`                        |
| `feed`       | `UIManager.cs`            | `SpawnFoodInTank()` — tras el loop de spawn                 |
| `startle`    | `InputHandler.cs`         | `StartleNearbyFish()` — una vez por acción de usuario       |
| `ambient`    | `AmbientModeController.cs`| `SetMode()` — tras `OnModeChanged?.Invoke`                  |
| `change_bg`  | `DecoPanel.cs`            | lambda `btn1Action` en `BuildBgContent()` — tras `SaveSystem.Save`     |
| `change_sub` | `DecoPanel.cs`            | lambda `btn1Action` en `BuildSubContent()` — tras `SaveSystem.Save`    |
| `change_light`| `DecoPanel.cs`           | lambda `btn1Action` en `BuildLightsContent()` — tras `SaveSystem.Save` |

**Nota `add_deco`:** posición enviada es `go.transform.position` del frame 0 (antes del micro-ajuste de `RefineFloorSnapNextFrame`). Diferencia sub-pixel, invisible en práctica. El INIT en reconexión siempre sincroniza la posición exacta guardada en disco.

---

## Testing local (sin Xiaomi)

Con `?devtest=1` en la URL local (`http://localhost:3001/?devtest=1`):

| Tecla | Acción |
|-------|--------|
| Enter | Startle |
| F     | Feed |
| A     | add_fish (moorish idol) |
| Z     | remove_fish (moorish idol) |
| B     | change_bg (cicla presets) |
| S     | change_sub (cicla sustratos) |

---

## Archivos modificados (2026-06-20)

| Archivo | Cambio |
|---------|--------|
| `Assets/Scripts/Core/CastDataTypes.cs` | Añadidos `TvAddFishPayload`, `TvAddDecoPayload` + `using UnityEngine` |
| `Assets/Scripts/Core/FishSpawner.cs` | Añadido `DespawnBySpecies(string speciesId)` |
| `Assets/Scripts/Core/TvSceneBootstrap.cs` | Handle registry + release en reconexión + 7 nuevos casos UPDATE |
| `Assets/WebGLTemplates/CastReceiver/index.html` | Devtest shortcuts A/Z/B/S + `sendCastUpdate()` helper |
