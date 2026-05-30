$ErrorActionPreference = "Stop"

$modRoot = $PSScriptRoot
. (Join-Path $modRoot "workspace-paths.ps1")
$gameRoot = (Resolve-RomesteadPaths -WorkspaceRoot $modRoot).GameRoot
$gameExe  = Join-Path $gameRoot "Romestead.exe"

if (-not (Test-Path $gameExe)) {
    throw "Could not find Romestead.exe at $gameExe"
}

# The mod loader is patched into Romestead.dll itself (via install.ps1), so it
# activates regardless of how the game is launched. This script is just a
# convenience wrapper for non-Steam launches; launching via Steam works too.
& $gameExe
