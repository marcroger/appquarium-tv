# HANDOFF — Continuar mañana

**Trabajo nocturno completado:** 2026-05-22 ~22:00
**Próxima sesión esperada:** sábado 2026-05-23 AM

---

## Lo que YA está hecho (autónomo, sin Unity)

| Fase | Estado | Resultado |
|---|---|---|
| F1 Mover webgl-build/ fuera del mobile | ✅ | Ahora vive en `D:\dev\appquarium-tv-unity\` |
| F1.2 Cleanup legacy build artifacts | ✅ | Build/, TemplateData/, index.html legacy borrados |
| F2 Bootstrap Unity project structure | ✅ | ProjectSettings/, Packages/, Assets/ skeleton creados |
| F2.5 WebGLTemplate con Cast SDK | ✅ | `Assets/WebGLTemplates/CastReceiver/index.html` con Cast SDK bakeado |
| F3 Copy scripts + assets desde mobile | ✅ | ~25 .cs + 83 SOs + 1.7GB ThirdParty + Resources |
| F3.5 SYNC_NOTES.md | ✅ | Doc crítico de qué/cuándo re-sync |
| F7 Cleanup mobile (revert dead code) | ✅ | AppquariumEditor.cs limpio, .gitignore actualizado, CAST.md actualizado |
| F8 Commits | ✅ | 1 commit mobile (81efdf4) + 4 commits TV (78c3979 → 5c01ce3) |

## Lo que TÚ tienes que hacer (requiere Unity UI)

### Paso 1: Abrir el proyecto en Unity Hub

1. Unity Hub → tab "Projects" → **Add** → **Add project from disk**
2. Selecciona `D:\dev\appquarium-tv-unity\`
3. Confirma versión Unity (debería ser la misma que mobile — Unity 6 LTS)
4. Open

Unity tardará **5-10 min en abrir el proyecto la primera vez** (genera Library/ + imports assets).

### Paso 2: Resolver compile errors esperados

`Assets/Scripts/Core/AquariumManager.cs` referencia muchos scripts mobile-only que NO copiamos:
- `UIManager` (UI)
- `SaveSystem`
- `IAPService`
- `AdService`
- `BreedingManager`
- `WeeklyFishManager`, `WeeklyDecoManager`, `WeeklyEnvManager`
- `DailyRewardManager`
- `LocalizationManager`
- `ReviewPromptManager`
- `PackCatalog`
- `AssetPackManager`
- `NotificationService`
- `AnalyticsService`
- `ScreenshotShare`

**Opción A — Refactor manual:** abrir AquariumManager.cs, comentar/eliminar todo lo que referencia mobile-only stuff. Mantener solo:
- `Instance` singleton
- `SaveData` field (puede ser una struct simple, no usar SaveSystem)
- `allFishCatalog` field
- `currentTank` field
- `CurrentTankBounds` getter
- `FishSpeedMultiplier`
- `InitializeFromCastState()` y métodos relacionados con Cast

**Opción B — SlimAquariumManager nuevo:** crear `Assets/Scripts/Core/SlimAquariumManager.cs` desde cero, con SOLO lo que necesitan FishAgent, FishSpawner, TankController, etc. Renombrarlo a `AquariumManager` y borrar el original.

**Recomiendo Opción A** — el AquariumManager actual ya tiene la lógica de InitializeFromCastState validada, mejor que reinventar. Tiempo estimado: 1-2 h de comentar/quitar.

### Paso 3: Switch platform a WebGL

1. File > Build Profiles (Unity 6) — o File > Build Settings (Unity 5)
2. Web (WebGL) → Switch Platform
3. Tarda ~5 min (reimport de texturas para WebGL)

### Paso 4: Configurar PlayerSettings WebGL

Player Settings → Web → Publishing Settings:
- **Compression Format:** Gzip (o Brotli — más rápido para Cast)
- **Decompression Fallback:** OFF (la mayoría de browsers soportan Gzip)
- **WebAssembly 2023 features:** OFF (Cast Chromium no las soporta — incluye wasm-EH)
- **Exception Support:** **None** (CRÍTICO — sin esto, error "wasm-exceptions" en Cast)
- **Web Template:** **PROJECT:CastReceiver** (selecciona el que creamos en `Assets/WebGLTemplates/CastReceiver/`)

### Paso 5: Build settings

File > Build Profiles:
- **Scenes in Build:** SOLO `Assets/Scenes/TvScene.unity` (eliminar cualquier otra que aparezca)
- **Compression:** ya configurado en PlayerSettings
- **Development Build:** OFF

### Paso 6: Build

1. Click **Build** (no Build And Run)
2. Output folder: `webgl-output/` (sale junto al proyecto)
3. Tarda ~5-10 min

### Paso 7: Verificar tamaño

```powershell
Get-Item D:\dev\appquarium-tv-unity\webgl-output\Build\*.data.unityweb | Select-Object Name, @{N="MB";E={[math]::Round($_.Length/1MB,1)}}
```

**Target: <100 MB.** Si pasa, ✅. Si excede, hay que revisar qué assets están bloating (probablemente algún ScriptableObject referencia algo gigante).

### Paso 8: Deploy a R2

Mismo proceso que ayer:
```powershell
cd D:\dev\appquarium-tv-unity
aws s3 sync webgl-output/ s3://appquarium-tv/ `
    --profile r2 `
    --endpoint-url https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com `
    --exclude ".git/*" `
    --exclude "StreamingAssets/*"
```

Verificar tamaño del .data via curl:
```powershell
Invoke-WebRequest -Uri "https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/Build/webgl-build.data.unityweb" -Method Head | Select-Object -ExpandProperty Headers
```

### Paso 9: Test Cast en Xiaomi

1. Reinicia Xiaomi Box (Settings → System → Restart)
2. Móvil → Appquarium → FAB → Cast → Xiaomi
3. **TV debería cargar el acuario en <30s** (con .data <100MB sobre Cast WiFi)

Si ves el acuario → 🎉 L3 conseguido, Cast funcional.

### Paso 10: Commit & push (cuando funcione)

```powershell
cd D:\dev\appquarium-tv-unity
git add -A
git commit -m "feat: AquariumManager slim + first successful WebGL build deployed"
git push origin main
```

⚠ El primer push transferirá ~1.7GB. En fibra normal ~3-5 min. GitHub puede pedir credentials.

---

## 📋 Archivos clave de referencia

| Archivo | Para qué |
|---|---|
| `D:\dev\appquarium-unity\CAST_L3_MIGRATION.md` | Plan completo de la migración |
| `D:\dev\appquarium-tv-unity\SYNC_NOTES.md` | Cuándo y cómo re-sync con mobile |
| `D:\dev\appquarium-tv-unity\HANDOFF.md` | Este documento |
| `D:\dev\appquarium-unity\CAST.md` | Doc Cast general (actualizado) |

## 🆘 Si algo va MUY mal

### Mobile project roto al abrir Unity
- Mobile NO debería estar afectado. Si tiene compile errors, los menu items que removí no se referenciaban desde scene → no debería fallar
- Workaround: `git revert 81efdf4` en mobile, todo vuelve al estado anterior

### TV project no compila tras refactor AquariumManager
- Es esperado tener que iterar varias horas. Es la parte más artesanal del L3
- Si te bloqueas mucho, alternativa: copiar TODOS los scripts del mobile (incluyendo UI) y arreglar errores mediante stubs

### Build WebGL sale enorme >100MB
- Inspeccionar Build Report (Window → Analysis → Build Report)
- Más probable: alguna referencia a Resources/ que arrastra cosas innecesarias
- Verificar PlayerSettings → Web → Publishing → "Strip Engine Code" = ON

### R2 upload falla
- Credentials están en `~/.aws/credentials` perfil `r2` (de ayer)
- Endpoint: `https://2aa2b7914f4ce7ce81e38d694b6219dc.r2.cloudflarestorage.com`
- Si signature errors → reintentar (bug intermitente conocido)

---

## ✅ Cuando termines y funcione

Actualizar:
- `CAST.md` mobile: marcar `QA Unity receiver completo` como ✅
- `MEMORY.md`: añadir sesión 2026-05-23 con éxito L3
- Volver a marketing: Andro4all pitch + r/SideProject post (sábado peak traffic)

Buenas noches y mucha suerte mañana 🐠
