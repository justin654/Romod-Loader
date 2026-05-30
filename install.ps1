param(
    [switch]$SkipBuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$GameRoot
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "workspace-paths.ps1")

$modRoot        = $PSScriptRoot
$paths          = Resolve-RomesteadPaths -WorkspaceRoot $modRoot -GameRoot $GameRoot
$gameRoot       = $paths.GameRoot
$gameModRoot    = $paths.GameModRoot
$startupOut     = Join-Path $modRoot "artifacts\startuphook"
$clientCoreOut  = Join-Path $modRoot "artifacts\clientcore"
$modsOut        = Join-Path $modRoot "artifacts\mods"
$workspaceConfig = Join-Path $modRoot "mods.json"
$installerDll   = Join-Path $modRoot "artifacts\installer\Romestead.ModLoader.Installer.dll"
$gameArtifactsDir = Join-Path $gameModRoot "artifacts"
$gameModsDir    = Join-Path $gameArtifactsDir "mods"
$gameLoaderDir  = Join-Path $gameArtifactsDir "loader"

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

function Copy-VerifiedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string[]]$FileNames,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    foreach ($fileName in $FileNames) {
        $sourcePath = Join-Path $SourceDirectory $fileName
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "Expected $Label file '$sourcePath' to exist."
        }

        $destinationPath = Join-Path $DestinationDirectory $fileName
        Copy-Item -Force -LiteralPath $sourcePath -Destination $destinationPath

        $sourceHash = (Get-FileHash -LiteralPath $sourcePath).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destinationPath).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "$Label file '$fileName' did not copy cleanly to '$destinationPath'."
        }

        Write-Host "  copied + verified: $fileName"
    }
}

if (-not $SkipBuild) {
    Write-Host "[install] Building mod loader, installer, and mod..."
    & (Join-Path $modRoot "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

if (-not (Test-Path $installerDll)) {
    throw "Installer not found at $installerDll. Run build.ps1 first or omit -SkipBuild."
}

if (-not (Test-Path $modsOut)) {
    throw "Built mod artifacts were not found at $modsOut. Run build.ps1 first."
}

if (-not (Test-Path $gameRoot)) {
    throw "Game root not found at $gameRoot."
}

if (-not (Test-Path (Join-Path $gameRoot "Romestead.dll"))) {
    throw "Romestead.dll not found in $gameRoot. Update Workspace.local.props or pass -GameRoot explicitly."
}

New-Item -ItemType Directory -Force -Path $gameModRoot | Out-Null
New-Item -ItemType Directory -Force -Path $gameArtifactsDir | Out-Null
New-Item -ItemType Directory -Force -Path $gameLoaderDir | Out-Null

if (Test-Path -LiteralPath $gameModsDir) {
    Remove-Item -Recurse -Force -LiteralPath $gameModsDir
}

Copy-DirectoryContent -Source $modsOut -Destination $gameModsDir
if (Test-Path -LiteralPath $workspaceConfig) {
    Copy-Item -Force -LiteralPath $workspaceConfig -Destination (Join-Path $gameModRoot "mods.json")
}

Write-Host "[install] Synced mods/config into $gameModRoot"
Write-Host "[install] Patching Romestead.dll and copying hook into game folder..."
& dotnet $installerDll install $gameRoot $startupOut
if ($LASTEXITCODE -ne 0) {
    throw "Installer reported failure (exit code $LASTEXITCODE)."
}

$startupFileNames = @(
    "0Harmony.dll",
    "Romestead.RomodFormat.dll",
    "Romestead.RomodFormat.pdb",
    "Romestead.StartupHook.deps.json",
    "Romestead.StartupHook.dll",
    "Romestead.StartupHook.pdb",
    "Tomlyn.dll"
)

Write-Host "[install] Verifying startup hook files in game folder..."
Copy-VerifiedFiles -SourceDirectory $startupOut -FileNames $startupFileNames -DestinationDirectory $gameRoot -Label "startup hook"

if (Test-Path $clientCoreOut) {
    Write-Host "[install] Verifying ClientCore artifacts in game folder..."
    $clientCoreNames = @(
        "0Harmony.dll",
        "Romestead.ModLoader.ClientCore.deps.json",
        "Romestead.ModLoader.ClientCore.dll",
        "Romestead.ModLoader.ClientCore.pdb"
    )
    Copy-VerifiedFiles -SourceDirectory $clientCoreOut -FileNames $clientCoreNames -DestinationDirectory $gameRoot -Label "client core"
} else {
    Write-Host "[install] WARN: ClientCore artifacts not found at $clientCoreOut; client-side patches will not run."
}

Write-Host ""
Write-Host "[install] Done. Launch Romestead via Steam (or any normal shortcut) and the mod loader will activate."
Write-Host "[install] Log: $gameModRoot\artifacts\loader\romestead-loader.log"
