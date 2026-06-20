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

## Lo que el móvil necesita implementar (pendiente Fase B)

Todos los puntos de llamada son en `D:\dev\appquarium-unity\`. **No tocar ahora.**

### 1. `CastManager.SendUpdate()` — ya existe, nunca se llama

Firma actual en mobile:
```csharp
public void SendUpdate(string updateType, string value = "")
```

### 2. Calls a añadir en mobile

| Acción usuario | Clase mobile | Call a añadir |
|---------------|--------------|---------------|
| Comprar/añadir pez | `AquariumManager.AddFishToTank()` | `CastManager.Instance?.SendUpdate("add_fish", JsonUtility.ToJson(new TvAddFishPayload { speciesId = data.itemId, nickname = save.nickname }))` |
| Vender/quitar pez | `AquariumManager.RemoveFishFromTank()` | `CastManager.Instance?.SendUpdate("remove_fish", save.speciesId)` |
| Colocar deco | `DecorationPlacer.PlaceAt()` | `CastManager.Instance?.SendUpdate("add_deco", JsonUtility.ToJson(new TvAddDecoPayload { instanceId = instanceId, itemId = data.itemId, position = worldPos, scaleFactor = scaleFactor, flipped = flipped, rotationY = rotationY }))` |
| Quitar deco | `DecorationPlacer.Remove()` | `CastManager.Instance?.SendUpdate("remove_deco", instanceId)` |
| Cambiar fondo | `DecoPanel` / `TankBackground.SetPreset()` | `CastManager.Instance?.SendUpdate("change_bg", bgId)` |
| Cambiar sustrato | `DecoPanel` / `DecorationPlacer.SetSubstrate()` | `CastManager.Instance?.SendUpdate("change_sub", subId)` |
| Cambiar luz | `DecoPanel` / `TankLightingController.SetPreset()` | `CastManager.Instance?.SendUpdate("change_light", lightId)` |
| Modo ambiental | `AmbientModeController` botón | `CastManager.Instance?.SendUpdate("ambient", mode)` |
| Velocidad peces | `fishSpeedMultiplier` setter | `CastManager.Instance?.SendUpdate("speed", value.ToString(CultureInfo.InvariantCulture))` |
| Dar comida | `UIManager.SpawnFoodInTank()` | `CastManager.Instance?.SendUpdate("feed")` |

> **Nota:** `TvAddFishPayload` y `TvAddDecoPayload` deben añadirse al móvil (CastDataTypes.cs del mobile o inline en CastManager.cs). El TV ya los tiene.

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
