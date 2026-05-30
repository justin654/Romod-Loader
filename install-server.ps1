param(
    [switch]$SkipBuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ServerRoot
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "workspace-paths.ps1")

$modRoot = $PSScriptRoot
$paths = Resolve-RomesteadPaths -WorkspaceRoot $modRoot -ServerRoot $ServerRoot
$ServerRoot = $paths.ServerRoot
$startupOut = Join-Path $modRoot "artifacts\startuphook"
$modsOut = Join-Path $modRoot "artifacts\mods"
$workspaceConfig = Join-Path $modRoot "mods.json"
$installerDll = Join-Path $modRoot "artifacts\installer\Romestead.ModLoader.Installer.dll"
$serverModRoot = $paths.ServerModRoot
$serverArtifactsDir = Join-Path $serverModRoot "artifacts"
$serverModsDir = Join-Path $serverModRoot "artifacts\mods"
$serverLoaderDir = Join-Path $serverModRoot "artifacts\loader"

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

if (-not (Test-Path $ServerRoot)) {
    throw "Dedicated server root not found at $ServerRoot."
}
if (-not (Test-Path (Join-Path $ServerRoot "Server.dll"))) {
    throw "Server.dll not found in $ServerRoot. Is this really the dedicated server install root?"
}

if (-not $SkipBuild) {
    Write-Host "[install-server] Building mod loader, installer, and server mods (ServerOnly, $Configuration)..."
    & (Join-Path $modRoot "build.ps1") -Configuration $Configuration -ServerOnly
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

if (-not (Test-Path $installerDll)) {
    throw "Installer not found at $installerDll. Run build.ps1 first or omit -SkipBuild."
}

if (-not (Test-Path $modsOut)) {
    throw "Built mod artifacts were not found at $modsOut. Run build.ps1 first."
}

New-Item -ItemType Directory -Force -Path $serverModRoot | Out-Null
New-Item -ItemType Directory -Force -Path $serverArtifactsDir | Out-Null
New-Item -ItemType Directory -Force -Path $serverLoaderDir | Out-Null
if (Test-Path -LiteralPath $serverModsDir) {
    Remove-Item -Recurse -Force -LiteralPath $serverModsDir
}

Copy-DirectoryContent -Source $modsOut -Destination $serverModsDir
if (Test-Path -LiteralPath $workspaceConfig) {
    Copy-Item -Force -LiteralPath $workspaceConfig -Destination (Join-Path $serverModRoot "mods.json")
}

Write-Host "[install-server] Synced mods/config into $serverModRoot"
Write-Host "[install-server] Patching Server.dll and copying hook into dedicated server folder..."
& dotnet $installerDll install-target $ServerRoot $startupOut Server.dll Server.deps.json Server.Program Main
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

Write-Host "[install-server] Verifying startup hook files in dedicated server folder..."
Copy-VerifiedFiles -SourceDirectory $startupOut -FileNames $startupFileNames -DestinationDirectory $ServerRoot -Label "startup hook"

Write-Host ""
Write-Host "[install-server] Done. Launch the dedicated server normally and the mod loader will activate."
Write-Host "[install-server] Server mod root: $serverModRoot"
Write-Host "[install-server] Log: $(Join-Path $serverModRoot 'artifacts\loader\romestead-loader.log')"
