param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ServerOnly
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$targetFramework = "net8.0"

$startupProject = Join-Path $root "src\Romestead.StartupHook\Romestead.StartupHook.csproj"
$installerProject = Join-Path $root "src\Romestead.ModLoader.Installer\Romestead.ModLoader.Installer.csproj"
$clientCoreProject = Join-Path $root "src\Romestead.ModLoader.ClientCore\Romestead.ModLoader.ClientCore.csproj"
$romodToolProject = Join-Path $root "src\Romestead.ModLoader.RomodTool\Romestead.ModLoader.RomodTool.csproj"

$artifactsRoot = Join-Path $root "artifacts"
$modsArtifactsRoot = Join-Path $artifactsRoot "mods"
$stagingRoot = Join-Path $artifactsRoot "_staging"
$stagingRunRoot = Join-Path $stagingRoot ("build-" + [Guid]::NewGuid().ToString("N"))

$startupStage = Join-Path $stagingRunRoot "startuphook"
$installerStage = Join-Path $stagingRunRoot "installer"
$clientCoreStage = Join-Path $stagingRunRoot "clientcore"
$romodToolStage = Join-Path $stagingRunRoot "romod-tool"
$modsStageRoot = Join-Path $stagingRunRoot "mods"

$startupOut = Join-Path $artifactsRoot "startuphook"
$installerOut = Join-Path $artifactsRoot "installer"
$clientCoreOut = Join-Path $artifactsRoot "clientcore"
$romodToolOut = Join-Path $artifactsRoot "romod-tool"

$romodsSource = Join-Path $root "romods"
$managedModProjects = @(
    @{ Name = "Romestead.NewItemsMod"; Project = Join-Path $root "mods\Romestead.NewItemsMod\Romestead.NewItemsMod.csproj"; IncludeAssets = $true; IncludeMaps = $false; SkipOnServerOnly = $false },
    @{ Name = "Romestead.SkipIntroMod"; Project = Join-Path $root "mods\Romestead.SkipIntroMod\Romestead.SkipIntroMod.csproj"; IncludeAssets = $false; IncludeMaps = $false; SkipOnServerOnly = $true },
    @{ Name = "Romestead.IconDumpMod"; Project = Join-Path $root "mods\Romestead.IconDumpMod\Romestead.IconDumpMod.csproj"; IncludeAssets = $false; IncludeMaps = $false; SkipOnServerOnly = $true }
)

function Invoke-BuildProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet build $ProjectPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function New-CleanDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -Recurse -Force -LiteralPath $Path
    }

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Expected directory '$Source' to exist."
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -Recurse -Force -LiteralPath $_.FullName -Destination $Destination
    }
}

function Stage-ProjectOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$StagePath
    )

    $projectDir = Split-Path -Parent $ProjectPath
    $outputDir = Join-Path $projectDir ("bin\" + $Configuration + "\" + $targetFramework)
    New-CleanDirectory -Path $StagePath
    Copy-DirectoryContent -Source $outputDir -Destination $StagePath
}

function Stage-ModArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$ModDefinition,
        [Parameter(Mandatory = $true)]
        [string]$StagePath
    )

    $projectPath = $ModDefinition.Project
    $projectDir = Split-Path -Parent $projectPath
    $outputDir = Join-Path $projectDir ("bin\" + $Configuration + "\" + $targetFramework)
    $excludePatterns = @(
        "Romestead.StartupHook.*",
        "Romestead.ModLoader.Abstractions.*",
        "Romestead.RomodFormat.*",
        "Tomlyn.*",
        "0Harmony.dll"
    )

    New-CleanDirectory -Path $StagePath

    Get-ChildItem -LiteralPath $outputDir -File | Where-Object {
        $name = $_.Name
        -not ($excludePatterns | Where-Object { $name -like $_ })
    } | ForEach-Object {
        Copy-Item -Force -LiteralPath $_.FullName -Destination $StagePath
    }

    Copy-Item -Force -LiteralPath (Join-Path $projectDir "mod.json") -Destination $StagePath

    if ($ModDefinition.IncludeAssets -and (Test-Path -LiteralPath (Join-Path $projectDir "assets"))) {
        Copy-Item -Recurse -Force -LiteralPath (Join-Path $projectDir "assets") -Destination (Join-Path $StagePath "assets")
    }

    if ($ModDefinition.IncludeMaps -and (Test-Path -LiteralPath (Join-Path $projectDir "maps"))) {
        Copy-Item -Recurse -Force -LiteralPath (Join-Path $projectDir "maps") -Destination (Join-Path $StagePath "maps")
    }
}

function Assert-ModArtifactLayout {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$ModDefinition,
        [Parameter(Mandatory = $true)]
        [string]$StagePath
    )

    $requiredFiles = @(
        "mod.json",
        ($ModDefinition.Name + ".dll")
    )
    $forbiddenPatterns = @(
        "Romestead.StartupHook.*",
        "Romestead.ModLoader.Abstractions.*",
        "Romestead.RomodFormat.*",
        "Tomlyn.*",
        "0Harmony.dll"
    )

    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagePath $requiredFile))) {
            throw "Expected mod artifact '$($ModDefinition.Name)' to contain '$requiredFile'."
        }
    }

    foreach ($pattern in $forbiddenPatterns) {
        $matches = Get-ChildItem -LiteralPath $StagePath -File -Filter $pattern
        if ($matches) {
            $names = ($matches | Select-Object -ExpandProperty Name) -join ", "
            throw "Mod artifact '$($ModDefinition.Name)' unexpectedly contains runtime dependency file(s): $names"
        }
    }
}

function Remove-BackupArtifact {
    param([string]$BackupPath)

    if ($BackupPath -and (Test-Path -LiteralPath $BackupPath)) {
        try {
            Remove-Item -Recurse -Force -LiteralPath $BackupPath
        } catch {
            Write-Warning "Promoted new artifact successfully, but failed to remove backup '$BackupPath': $($_.Exception.Message)"
        }
    }
}

function Promote-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagePath,
        [Parameter(Mandatory = $true)]
        [string]$FinalPath
    )

    $parent = Split-Path -Parent $FinalPath
    $name = Split-Path -Leaf $FinalPath
    $backupPath = $null

    New-Item -ItemType Directory -Force -Path $parent | Out-Null

    try {
        if (Test-Path -LiteralPath $FinalPath) {
            $backupPath = Join-Path $parent ($name + ".bak-" + [Guid]::NewGuid().ToString("N"))
            Move-Item -LiteralPath $FinalPath -Destination $backupPath
        }

        Move-Item -LiteralPath $StagePath -Destination $FinalPath
        Remove-BackupArtifact -BackupPath $backupPath
    } catch {
        if ($backupPath -and (Test-Path -LiteralPath $backupPath) -and -not (Test-Path -LiteralPath $FinalPath)) {
            Move-Item -LiteralPath $backupPath -Destination $FinalPath
        }

        throw "Failed to promote artifact directory '$FinalPath'. Close the game or any process holding files there and retry. Staged build output remains under '$stagingRunRoot'. Inner error: $($_.Exception.Message)"
    }
}

function Promote-File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagePath,
        [Parameter(Mandatory = $true)]
        [string]$FinalPath
    )

    $parent = Split-Path -Parent $FinalPath
    $name = Split-Path -Leaf $FinalPath
    $backupPath = $null

    New-Item -ItemType Directory -Force -Path $parent | Out-Null

    try {
        if (Test-Path -LiteralPath $FinalPath) {
            $backupPath = Join-Path $parent ($name + ".bak-" + [Guid]::NewGuid().ToString("N"))
            Move-Item -LiteralPath $FinalPath -Destination $backupPath
        }

        Move-Item -LiteralPath $StagePath -Destination $FinalPath
        Remove-BackupArtifact -BackupPath $backupPath
    } catch {
        if ($backupPath -and (Test-Path -LiteralPath $backupPath) -and -not (Test-Path -LiteralPath $FinalPath)) {
            Move-Item -LiteralPath $backupPath -Destination $FinalPath
        }

        throw "Failed to promote artifact file '$FinalPath'. Close the game or any process holding files there and retry. Staged build output remains under '$stagingRunRoot'. Inner error: $($_.Exception.Message)"
    }
}

New-CleanDirectory -Path $stagingRunRoot
New-Item -ItemType Directory -Force -Path $modsStageRoot | Out-Null

Invoke-BuildProject -ProjectPath $startupProject -FailureMessage "Failed to build startup hook project."
Invoke-BuildProject -ProjectPath $installerProject -FailureMessage "Failed to build installer project."

if (-not $ServerOnly) {
    Invoke-BuildProject -ProjectPath $clientCoreProject -FailureMessage "Failed to build client core project."
} else {
    Write-Host "[build] -ServerOnly: skipping Romestead.ModLoader.ClientCore (references client-only Romestead.dll)."
}

foreach ($mod in $managedModProjects) {
    if ($ServerOnly -and $mod.SkipOnServerOnly) {
        Write-Host "[build] -ServerOnly: skipping $($mod.Name) (client-only mod)."
        continue
    }

    Invoke-BuildProject -ProjectPath $mod.Project -FailureMessage ("Failed to build mod project '" + $mod.Name + "'.")
}

Invoke-BuildProject -ProjectPath $romodToolProject -FailureMessage "Failed to build romod CLI tool."

Stage-ProjectOutputDirectory -ProjectPath $startupProject -StagePath $startupStage
Stage-ProjectOutputDirectory -ProjectPath $installerProject -StagePath $installerStage
if (-not $ServerOnly) {
    Stage-ProjectOutputDirectory -ProjectPath $clientCoreProject -StagePath $clientCoreStage
}
Stage-ProjectOutputDirectory -ProjectPath $romodToolProject -StagePath $romodToolStage

foreach ($mod in $managedModProjects) {
    if ($ServerOnly -and $mod.SkipOnServerOnly) {
        continue
    }

    $stagePath = Join-Path $modsStageRoot $mod.Name
    Stage-ModArtifact -ModDefinition $mod -StagePath $stagePath
    Assert-ModArtifactLayout -ModDefinition $mod -StagePath $stagePath
}

$romodToolDll = Join-Path (Join-Path (Split-Path -Parent $romodToolProject) ("bin\" + $Configuration + "\" + $targetFramework)) "romestead-mod.dll"
if (Test-Path -LiteralPath $romodsSource) {
    Get-ChildItem -LiteralPath $romodsSource -Directory | ForEach-Object {
        $packageName = $_.Name
        $stageRomod = Join-Path $modsStageRoot ($packageName + ".romod")
        Write-Host "[build] Packing romod: $($_.FullName) -> $stageRomod"
        & dotnet $romodToolDll pack $_.FullName -o $stageRomod
        if ($LASTEXITCODE -ne 0) {
            throw ("Failed to pack .romod package '" + $packageName + "'.")
        }
    }
}

foreach ($required in @("Romestead.RomodFormat.dll", "Romestead.ModLoader.Abstractions.dll", "Tomlyn.dll")) {
    $check = Join-Path $startupStage $required
    if (-not (Test-Path -LiteralPath $check)) {
        throw "Expected $required to be present in $startupStage after StartupHook build."
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $modsArtifactsRoot | Out-Null

Promote-Directory -StagePath $startupStage -FinalPath $startupOut
Promote-Directory -StagePath $installerStage -FinalPath $installerOut
if (-not $ServerOnly) {
    Promote-Directory -StagePath $clientCoreStage -FinalPath $clientCoreOut
}
Promote-Directory -StagePath $romodToolStage -FinalPath $romodToolOut

foreach ($mod in $managedModProjects) {
    if ($ServerOnly -and $mod.SkipOnServerOnly) {
        continue
    }

    Promote-Directory -StagePath (Join-Path $modsStageRoot $mod.Name) -FinalPath (Join-Path $modsArtifactsRoot $mod.Name)
}

if (Test-Path -LiteralPath (Join-Path $modsArtifactsRoot "Romestead.ModLoader.Core")) {
    Remove-Item -Recurse -Force -LiteralPath (Join-Path $modsArtifactsRoot "Romestead.ModLoader.Core")
}

if (Test-Path -LiteralPath $romodsSource) {
    Get-ChildItem -LiteralPath $romodsSource -Directory | ForEach-Object {
        $packageName = $_.Name + ".romod"
        Promote-File -StagePath (Join-Path $modsStageRoot $packageName) -FinalPath (Join-Path $modsArtifactsRoot $packageName)
    }
}

if (Test-Path -LiteralPath $stagingRunRoot) {
    Remove-Item -Recurse -Force -LiteralPath $stagingRunRoot
}

Write-Host "Build complete."
Write-Host "Startup hook: $startupOut"
Write-Host "Installer:    $installerOut"
if (-not $ServerOnly) {
    Write-Host "Client core:  $clientCoreOut"
}
Write-Host "Romod tool:   $romodToolOut"
Write-Host "Romods dir:   $modsArtifactsRoot"
