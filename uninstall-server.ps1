param(
    [string]$ServerRoot
)

$ErrorActionPreference = "Stop"

$modRoot = $PSScriptRoot
. (Join-Path $modRoot "workspace-paths.ps1")
$paths = Resolve-RomesteadPaths -WorkspaceRoot $modRoot -ServerRoot $ServerRoot
$ServerRoot = $paths.ServerRoot
$installerDll = Join-Path $modRoot "artifacts\installer\Romestead.ModLoader.Installer.dll"

if (-not (Test-Path $ServerRoot)) {
    throw "Dedicated server root not found at $ServerRoot."
}

if (-not (Test-Path $installerDll)) {
    throw "Installer not found at $installerDll. Run build.ps1 first."
}

Write-Host "[uninstall-server] Restoring original Server.dll from backup..."
& dotnet $installerDll uninstall-target $ServerRoot Server.dll Server.deps.json
if ($LASTEXITCODE -ne 0) {
    throw "Installer reported failure (exit code $LASTEXITCODE)."
}

Write-Host ""
Write-Host "[uninstall-server] Done. Server.dll restored. Hook DLLs and romestead_modding folder were left in place."
