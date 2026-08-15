# Cast Events — Arquitectura y Backlog

> ⚠ **Parcialmente obsoleto.** El backlog de eventos «no implementados ni en el sender ni en
> el receiver» (`FEED`, `AMBIENT_TOGGLE`, `ADD_FISH`/`REMOVE_FISH`) **está cerrado**: los 11
> tipos funcionan en ambos extremos. Tampoco es cierto ya el «sin día/noche reactivo».
> Estado real y protocolo vigente: `CAST_UPDATES.md` y `TODO.md`.

**Actualizado:** 2026-06-08

---

## Cómo funciona Cast — no es mirroring

El receiver TV **no es un espejo del móvil**. Son dos apps independientes:

```
Móvil                                TV (Xiaomi / Chromecast)
──────                               ───────────────────────
[tu acuario móvil corriendo]         [Unity WebGL corriendo SOLO]
      │                                      │
      └── Cast SDK ──→ JSON mensaje ──→ CastReceiver.cs ──→ TvSceneBootstrap
```

- El móvil envía el **estado del tanque** al arrancar (`INIT`) y cambios puntuales (`UPDATE`).
- A partir de ahí, la TV corre Unity de forma completamente autónoma.
- El móvil puede cerrarse, bloquearse o perder WiFi: **el acuario TV sigue vivo** hasta que cae la sesión Cast.
- Todo el comportamiento de los peces (AI, movimiento, animaciones, sombras, partículas) es Unity corriendo en el device. No viene del móvil.

**Excepción:** si la sesión Cast cae (timeout, móvil desconectado), la TV se queda sin sesión activa. Eso es el bug de disconnect de Fase B.

---

## Mensajes implementados actualmente

| Tipo | Qué hace | Estado |
|---|---|---|
| `INIT` | Parsea `TvAquariumState` JSON → carga bundles → inicializa acuario | ✅ Implementado |
| `UPDATE` | Aplica cambios en caliente (añadir pez, cambiar deco, etc.) | ✅ Implementado |
| cualquier otro | `Debug.LogWarning("Unknown message type")` — ignorado | — |

Ver `Assets/Scripts/Core/CastReceiver.cs` — el `switch (msg.type)` es el entry point.

---

## Eventos interactivos — Backlog Fase B

Estos eventos **podrían** enviarse desde el móvil vía Cast SDK, pero actualmente no están implementados ni en el sender (móvil) ni en el receiver (TV):

| Evento | Descripción | Trabajo necesario |
|---|---|---|
| `FISH_TAP` | Tap en pez en el móvil asusta al pez en TV | Mobile sender → `castSession.sendMessage(CHANNEL, {type:"FISH_TAP"})` + TV receiver `case "FISH_TAP": FishBrain.Startle()` |
| `FEED` | Tap "alimentar" en móvil → comida cae en TV | Idem |
| `AMBIENT_TOGGLE` | Cambio día/noche sincronizado | Idem |
| `ADD_FISH` / `REMOVE_FISH` | Cambio de tanque en caliente | Parcialmente en `UPDATE` — sin validar |

**El canal ya está abierto** (Custom Channel `urn:x-cast:dev.unknownaerials.appquarium`). Solo falta añadir los `case` en CastReceiver y el `sendMessage` en el sender móvil.

---

## Por qué los peces se comportan igual que en móvil

Todos los scripts de comportamiento son los mismos (sincronizados desde mobile vía `SyncFromMobile.ps1`):
- `FishAgent.cs`, `FishBrain.cs`, `SteeringController.cs`, `NeedsModule.cs`
- `FishProceduralAnimator.cs`

La TV los corre con los mismos parámetros que le llegan en `TvAquariumState`. El resultado visual debería ser indistinguible del móvil salvo por la iluminación (TV usa AmbientMode sin UI, sin día/noche reactivo aún).

---

## Sombras y efectos visuales

Todo lo que está configurado en `TvScene.unity` se renderiza en la tele:
- Sombra proyectada en el suelo: depende de si el pez prefab tiene `ShadowCastingMode` activo y el plano de suelo recibe sombras (`receiveShadows=true`).
- Burbujas: `BubbleSystem.cs` — independiente, corre en TV.
- Reflejo del agua (`WaterSurface.cs`): activo con `Sprites/Default`.
- Post-processing: `PostProcessingSetup.cs` configura URP volume en runtime.

Si algo no se ve en TV que sí se ve en móvil → probable diferencia de configuración en la TvScene o material stripping. Ver `BUILD_REPORT_2026-06-02.md §Shaders`.
