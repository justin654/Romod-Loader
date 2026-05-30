param(
    [ValidateSet("Client", "Server", "Both")]
    [string]$Target = "Client",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$GameRoot,
    [string]$ServerRoot,
    [switch]$SkipBuild,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "workspace-paths.ps1")

$workspaceRoot = $PSScriptRoot
$paths = Resolve-RomesteadPaths -WorkspaceRoot $workspaceRoot -GameRoot $GameRoot -ServerRoot $ServerRoot

function Start-IfPresent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath)) {
        Write-Warning "$Label executable not found at $ExecutablePath"
        return
    }

    Start-Process -FilePath $ExecutablePath | Out-Null
    Write-Host ("[dev-install] Launched {0}: {1}" -f $Label, $ExecutablePath)
}

$clientInstall = Join-Path $workspaceRoot "install.ps1"
$serverInstall = Join-Path $workspaceRoot "install-server.ps1"

$clientInstallArgs = @{
    Configuration = $Configuration
    GameRoot = $paths.GameRoot
}

$serverInstallArgs = @{
    Configuration = $Configuration
    ServerRoot = $paths.ServerRoot
}

if ($SkipBuild) {
    $clientInstallArgs.SkipBuild = $true
    $serverInstallArgs.SkipBuild = $true
}

switch ($Target) {
    "Client" {
        Write-Host "[dev-install] Installing client mods ($Configuration)..."
        & $clientInstall @clientInstallArgs
    }
    "Server" {
        Write-Host "[dev-install] Installing server mods ($Configuration)..."
        & $serverInstall @serverInstallArgs
    }
    "Both" {
        Write-Host "[dev-install] Installing client mods ($Configuration)..."
        & $clientInstall @clientInstallArgs

        Write-Host "[dev-install] Installing server mods ($Configuration)..."
        & $serverInstall @serverInstallArgs
    }
}

if ($Launch) {
    switch ($Target) {
        "Client" {
            Start-IfPresent -ExecutablePath (Join-Path $paths.GameRoot "Romestead.exe") -Label "client"
        }
        "Server" {
            Start-IfPresent -ExecutablePath (Join-Path $paths.ServerRoot "Server.exe") -Label "server"
        }
        "Both" {
            Start-IfPresent -ExecutablePath (Join-Path $paths.GameRoot "Romestead.exe") -Label "client"
            Start-IfPresent -ExecutablePath (Join-Path $paths.ServerRoot "Server.exe") -Label "server"
        }
    }
}

Write-Host "[dev-install] Complete."
