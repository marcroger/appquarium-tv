# SyncFromMobile.ps1 — Re-sincroniza scripts/SOs/recursos compartidos desde el proyecto mobile.
#
# Uso:
#   .\Tools\SyncFromMobile.ps1               # Modo interactivo: lista diffs, pide confirmación
#   .\Tools\SyncFromMobile.ps1 -DryRun       # Solo lista diffs, no copia nada
#   .\Tools\SyncFromMobile.ps1 -Yes          # Copia todo lo que difiere sin preguntar
#   .\Tools\SyncFromMobile.ps1 -Verbose      # Logs detallados por archivo
#
# Diseñado para invocarse desde:
#   - PowerShell directamente
#   - El menu item Unity TV: `Appquarium TV → 🔄 Sync from Mobile`
#
# IMPORTANTE: Cierra Unity TV antes de ejecutar — algunos scripts modificados pueden
# disparar recompilación parcial y dejar Library/ en estado inconsistente.
#
# Ver SYNC_NOTES.md para la justificación de qué se sincroniza y qué no.

[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'

# ── Configuración ────────────────────────────────────────────────────────────

$MobileRoot = "D:\dev\appquarium-unity"
$TvRoot     = "D:\dev\appquarium-tv-unity"

if (-not (Test-Path $MobileRoot)) {
    Write-Error "Mobile project no encontrado en $MobileRoot. Aborto."
    exit 1
}
if (-not (Test-Path $TvRoot)) {
    Write-Error "TV project no encontrado en $TvRoot. Aborto."
    exit 1
}

# Carpetas que se sincronizan COMPLETAS (todo su contenido recursivo, incluye .meta)
#
# ⚠⚠ 2026-08-15 — SE HAN QUITADO DE AQUÍ:
#
#   Assets\Scripts\Tank          -> contiene DecorationPlacer.cs, TankBackground.cs y
#                                   WaterSurface.cs, los tres con arreglos TV-only. La
#                                   versión móvil llama a CastManager.Instance, que en TV
#                                   NO EXISTE (ni hay stub) -> el proyecto deja de compilar.
#                                   Además se perdería el lote visual validado el 15-ago.
#   Assets\ScriptableObjects\*   -> regla dura del proyecto: "NUNCA sync de SOs en bloque".
#                                   Rompe las refs a prefab ({guid} -> {fileID:0}); obligaba
#                                   a re-ejecutar "★ Assign Fish Prefabs" cada vez y bastaba
#                                   olvidarlo una sola vez para romperlo todo.
#
# Si necesitas algo de esas carpetas, cópialo A MANO, fichero a fichero, mirando el diff.
$FoldersToSync = @(
    "Assets\Scripts\Fish",
    "Assets\Scripts\Data",
    "Assets\Resources\Data"
)

# Archivos específicos que se sincronizan individualmente (con su .meta)
$FilesToSync = @(
    "Assets\Scripts\Core\AquariumCameraController.cs",
    "Assets\Scripts\Core\AudioManager.cs",
    "Assets\Scripts\Utils\AppFlags.cs",
    "Assets\Scripts\Utils\CatalogLoader.cs"
)

# Archivos a INSPECCIONAR (no copiar) — el TV tiene versión slim/modificada.
# Si difieren del mobile, mostrar warning pero NO sobreescribir automáticamente.
#
# ⚠ 2026-08-15 — ampliada tras auditar fichero a fichero. Lo que se perdía en cada caso:
#   PostProcessingSetup.cs   bloom OFF + Tonemapping Neutral + sat/contrast -> vuelven los 7 fps
#   DecorationPlacer.cs      tint dual _Color/_BaseColor, BioLumEmissionScale 0.25, remap X,
#                            RemoveAllDecos, GetFloorSurfaceY (de ahí cuelgan las sombras)
#   FishSpawner.cs           DespawnOneBySpecies (lo llama TvSceneBootstrap -> no compila)
#   AmbientModeController.cs la versión móvil llama a CastManager -> no compila
#   TankBackground.cs        orden de shaders para el color space de WebGL
#   WaterSurface.cs          idem
#   FoodItem.cs              mesh/material compartidos; sin ellos, fuga de GPU por pellet
#   AppVersion.cs            el móvil va por 1.2.2/37; subirlo renombra el catálogo remoto
#                            a catalog_1.2.2 y el player desplegado deja de encontrarlo
$FilesToWarn = @(
    "Assets\Scripts\Core\AquariumManager.cs",
    "Assets\Scripts\Core\CastReceiver.cs",
    "Assets\Scripts\Core\CastDataTypes.cs",
    "Assets\Scripts\Core\TvSceneBootstrap.cs",
    "Assets\Scripts\Core\PostProcessingSetup.cs",
    "Assets\Scripts\Core\FishSpawner.cs",
    "Assets\Scripts\Core\FoodItem.cs",
    "Assets\Scripts\Core\AmbientModeController.cs",
    "Assets\Scripts\Tank\DecorationPlacer.cs",
    "Assets\Scripts\Tank\TankBackground.cs",
    "Assets\Scripts\Tank\WaterSurface.cs",
    "Assets\Scripts\Utils\AppVersion.cs"
)

# ── Helpers ──────────────────────────────────────────────────────────────────

function Get-FolderFileList {
    param([string]$Root, [string]$RelFolder)
    $abs = Join-Path $Root $RelFolder
    if (-not (Test-Path $abs -PathType Container)) { return @() }
    $files = Get-ChildItem -Path $abs -Recurse -File -ErrorAction SilentlyContinue
    return $files | ForEach-Object { $_.FullName.Substring($Root.Length).TrimStart('\') }
}

function Format-Bytes {
    param([long]$Bytes)
    if ($Bytes -lt 1KB)  { return "$Bytes B" }
    if ($Bytes -lt 1MB)  { return ("{0:N1} KB" -f ($Bytes / 1KB)) }
    return ("{0:N1} MB" -f ($Bytes / 1MB))
}

function Compare-File {
    param([string]$RelPath)
    $mobileFile = Join-Path $MobileRoot $RelPath
    $tvFile     = Join-Path $TvRoot $RelPath
    if (-not (Test-Path $mobileFile -PathType Leaf)) {
        return @{ Status = 'MissingInMobile'; RelPath = $RelPath }
    }
    if (-not (Test-Path $tvFile -PathType Leaf)) {
        return @{ Status = 'NewInMobile'; RelPath = $RelPath;
                  MobileSize = (Get-Item $mobileFile).Length }
    }
    $h1 = (Get-FileHash -Path $mobileFile -Algorithm SHA1).Hash
    $h2 = (Get-FileHash -Path $tvFile     -Algorithm SHA1).Hash
    if ($h1 -ne $h2) {
        return @{ Status = 'Differs'; RelPath = $RelPath;
                  MobileSize = (Get-Item $mobileFile).Length;
                  TvSize     = (Get-Item $tvFile).Length }
    }
    return @{ Status = 'Same'; RelPath = $RelPath }
}

function Copy-FileWithMeta {
    param([string]$RelPath, [switch]$WhatIf)
    $src = Join-Path $MobileRoot $RelPath
    $dst = Join-Path $TvRoot $RelPath
    $dstDir = Split-Path $dst -Parent
    if ($WhatIf) {
        Write-Host "  [DRY] $RelPath" -ForegroundColor DarkGray
        return
    }
    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }
    Copy-Item -Path $src -Destination $dst -Force
    Write-Host "  ✓ $RelPath" -ForegroundColor Green
    # Copiar .meta si existe (Unity lo necesita)
    $srcMeta = "$src.meta"
    if (Test-Path $srcMeta) {
        Copy-Item -Path $srcMeta -Destination "$dst.meta" -Force
        Write-Host "  ✓ $RelPath.meta" -ForegroundColor DarkGreen
    }
}

# ── Recopilar diffs ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "═══ Sync from Mobile → TV ═══" -ForegroundColor Cyan
Write-Host "  Source: $MobileRoot"
Write-Host "  Dest:   $TvRoot"
if ($DryRun) { Write-Host "  Mode:   DRY-RUN (no se copia nada)" -ForegroundColor Yellow }
Write-Host ""

# Detectar Unity TV abierto (lock file)
$lockFile = Join-Path $TvRoot "Temp\UnityLockfile"
if (Test-Path $lockFile -ErrorAction SilentlyContinue) {
    Write-Warning "Unity TV parece estar abierto (Temp\UnityLockfile existe)."
    Write-Warning "Recompilación parcial puede dejar Library/ en estado inconsistente."
    if (-not $Yes -and -not $DryRun) {
        $resp = Read-Host "Continuar igualmente? (y/N)"
        if ($resp -ne 'y' -and $resp -ne 'Y') { exit 0 }
    }
}

# Construir lista completa de archivos candidatos
$candidates = New-Object System.Collections.Generic.List[string]
foreach ($folder in $FoldersToSync) {
    $absMobile = Join-Path $MobileRoot $folder
    if (-not (Test-Path $absMobile -PathType Container)) {
        Write-Warning "  ! Folder no existe en mobile: $folder (skip)"
        continue
    }
    Get-FolderFileList -Root $MobileRoot -RelFolder $folder | ForEach-Object {
        # Ignorar .meta — se copia junto al archivo principal
        if ($_ -notlike "*.meta") { $candidates.Add($_) }
    }
}
foreach ($file in $FilesToSync) {
    $candidates.Add($file)
}

# Clasificar
$diffs    = New-Object System.Collections.Generic.List[hashtable]
$newOnes  = New-Object System.Collections.Generic.List[hashtable]
$sameCnt  = 0
foreach ($rel in $candidates) {
    $cmp = Compare-File -RelPath $rel
    switch ($cmp.Status) {
        'Differs'        { $diffs.Add($cmp) }
        'NewInMobile'    { $newOnes.Add($cmp) }
        'Same'           { $sameCnt++ }
        'MissingInMobile'{ Write-Warning "  ! $rel no existe en mobile" }
    }
}

# Warnings (sin copiar)
$warnDiffs = New-Object System.Collections.Generic.List[hashtable]
foreach ($file in $FilesToWarn) {
    $cmp = Compare-File -RelPath $file
    if ($cmp.Status -eq 'Differs') { $warnDiffs.Add($cmp) }
}

# ── Reporte ──────────────────────────────────────────────────────────────────

Write-Host "Resumen:" -ForegroundColor Cyan
Write-Host "  $sameCnt archivos OK (iguales)"
Write-Host "  $($newOnes.Count) archivos NUEVOS en mobile" -ForegroundColor Yellow
Write-Host "  $($diffs.Count) archivos DIFIEREN"            -ForegroundColor Yellow
Write-Host "  $($warnDiffs.Count) archivos NO-SYNC con cambios en mobile (warn)" -ForegroundColor Magenta
Write-Host ""

if ($newOnes.Count -gt 0) {
    Write-Host "NUEVOS en mobile (no existen en TV):" -ForegroundColor Yellow
    foreach ($n in $newOnes) {
        Write-Host ("  + {0,-70} {1}" -f $n.RelPath, (Format-Bytes $n.MobileSize))
    }
    Write-Host ""
}

if ($diffs.Count -gt 0) {
    Write-Host "DIFIEREN (mobile más reciente probablemente):" -ForegroundColor Yellow
    foreach ($d in $diffs) {
        Write-Host ("  ~ {0,-70} mobile={1} tv={2}" -f $d.RelPath,
            (Format-Bytes $d.MobileSize), (Format-Bytes $d.TvSize))
    }
    Write-Host ""
}

if ($warnDiffs.Count -gt 0) {
    Write-Host "⚠ Archivos con versión slim/modificada en TV — revisar a mano:" -ForegroundColor Magenta
    foreach ($w in $warnDiffs) {
        Write-Host "  ! $($w.RelPath)" -ForegroundColor Magenta
    }
    Write-Host "  (Estos NO se copian automáticamente. Comparar con tu herramienta favorita.)" -ForegroundColor Magenta
    Write-Host ""
}

if ($diffs.Count -eq 0 -and $newOnes.Count -eq 0) {
    Write-Host "✅ Nada que sincronizar. TV al día con mobile." -ForegroundColor Green
    exit 0
}

# ── Ejecutar copia ───────────────────────────────────────────────────────────

if ($DryRun) {
    Write-Host "[DRY-RUN] No se copia nada. Termina." -ForegroundColor Yellow
    exit 0
}

if (-not $Yes) {
    $total = $diffs.Count + $newOnes.Count
    $resp = Read-Host "Copiar $total archivo(s) desde mobile a TV? (y/N)"
    if ($resp -ne 'y' -and $resp -ne 'Y') {
        Write-Host "Aborto por usuario." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host ""
Write-Host "Copiando..." -ForegroundColor Cyan

foreach ($d in $diffs)   { Copy-FileWithMeta -RelPath $d.RelPath }
foreach ($n in $newOnes) { Copy-FileWithMeta -RelPath $n.RelPath }

# ── Registrar en SYNC_NOTES.md ───────────────────────────────────────────────

$mobileCommit = & git -C $MobileRoot rev-parse --short HEAD 2>$null
if (-not $mobileCommit) { $mobileCommit = "unknown" }
$now = Get-Date -Format "yyyy-MM-dd HH:mm"
$logEntry = "| $now | ``$mobileCommit`` | Sync auto: $($diffs.Count) modificados, $($newOnes.Count) nuevos. |"

$syncNotes = Join-Path $TvRoot "SYNC_NOTES.md"
if (Test-Path $syncNotes) {
    # SYNC_NOTES.md está en UTF-8 SIN BOM. PowerShell 5.1's Get-Content asume
    # ANSI/Windows-1252 si no hay BOM → mojibakea los chars especiales (→, —, ⚠, etc.).
    # Solución: leer y escribir vía .NET API con encoding UTF-8 explícito sin BOM.
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $content = [System.IO.File]::ReadAllText($syncNotes, $utf8NoBom)

    # Insertar entry encima de la línea "| _futuro_ |" (placeholder al final de la tabla)
    $pattern    = '\|\s*_futuro_\s*\|[^\r\n]*'
    $newContent = [System.Text.RegularExpressions.Regex]::Replace(
        $content, $pattern, "$logEntry`n| _futuro_ | — | — |")

    if ($newContent -eq $content) {
        # Patrón _futuro_ no encontrado — append al final del fichero
        Write-Warning "  ! No encontré placeholder | _futuro_ | en SYNC_NOTES.md — appendeo al final"
        $newContent = $content.TrimEnd() + "`n$logEntry`n"
    }

    [System.IO.File]::WriteAllText($syncNotes, $newContent, $utf8NoBom)
    Write-Host ""
    Write-Host "  ✓ SYNC_NOTES.md actualizado con entry de hoy ($mobileCommit)" -ForegroundColor Green
}

Write-Host ""
Write-Host "═══ Sync completo ═══" -ForegroundColor Cyan
Write-Host "  → Abre Unity TV y deja que reimporte (Assets → Refresh si necesario)"
Write-Host "  → Compila + verifica que no haya errores"
Write-Host "  → Si tocaste FishData / DecorationData, los bundles cambiados"
Write-Host "    requerirán rebuild de Player Content + redeploy a R2."
Write-Host ""
