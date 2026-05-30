param(
    [string]$GameRoot
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "workspace-paths.ps1")

$modRoot      = $PSScriptRoot
$paths        = Resolve-RomesteadPaths -WorkspaceRoot $modRoot -GameRoot $GameRoot
$gameRoot     = $paths.GameRoot
$installerDll = Join-Path $modRoot "artifacts\installer\Romestead.ModLoader.Installer.dll"

if (-not (Test-Path $installerDll)) {
    throw "Installer not found at $installerDll. Run build.ps1 first."
}

Write-Host "[uninstall] Restoring original Romestead.dll from backup..."
& dotnet $installerDll uninstall $gameRoot
if ($LASTEXITCODE -ne 0) {
    throw "Installer reported failure (exit code $LASTEXITCODE)."
}

Write-Host ""
Write-Host "[uninstall] Done. Romestead.dll restored. Hook DLLs left in game folder (delete manually if you want)."
