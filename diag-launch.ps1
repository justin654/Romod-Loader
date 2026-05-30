$ErrorActionPreference = "Continue"

$modRoot  = $PSScriptRoot
. (Join-Path $modRoot "workspace-paths.ps1")
$gameRoot = (Resolve-RomesteadPaths -WorkspaceRoot $modRoot).GameRoot
$gameExe  = Join-Path $gameRoot "Romestead.exe"

$diagDir  = Join-Path $modRoot "artifacts\diag"
New-Item -ItemType Directory -Force -Path $diagDir | Out-Null

$traceFile  = Join-Path $diagDir "corehost-trace.log"
$stdoutFile = Join-Path $diagDir "romestead-stdout.log"
$stderrFile = Join-Path $diagDir "romestead-stderr.log"

# Remove old logs so we capture only this run.
Remove-Item -Force -ErrorAction SilentlyContinue $traceFile, $stdoutFile, $stderrFile

# Enable the .NET host's verbose tracing. This logs every assembly resolution attempt,
# probing path, dependency contract, and failure reason.
$env:COREHOST_TRACE        = "1"
$env:COREHOST_TRACE_VERBOSITY = "4"
$env:COREHOST_TRACEFILE    = $traceFile

# Bypass Steam's app-restart so we can run the game directly and capture its output.
# We're patching the same call from inside the mod, but Steam-relaunch happens before
# our hook in this scenario, so we set steam_appid.txt as a fallback if it's missing.
$steamAppIdFile = Join-Path $gameRoot "steam_appid.txt"
if (-not (Test-Path $steamAppIdFile)) {
    Write-Host "[diag] (note) No steam_appid.txt in game folder. If launch fails with a Steam error, you may need one."
}

Write-Host "[diag] Launching: $gameExe"
Write-Host "[diag] Capturing stdout -> $stdoutFile"
Write-Host "[diag] Capturing stderr -> $stderrFile"
Write-Host "[diag] Host trace      -> $traceFile"
Write-Host ""

$proc = Start-Process -FilePath $gameExe -PassThru `
    -RedirectStandardOutput $stdoutFile `
    -RedirectStandardError  $stderrFile `
    -NoNewWindow
$proc.WaitForExit()

Write-Host ""
Write-Host "[diag] Process exited with code $($proc.ExitCode)."
Write-Host "[diag] Trace file size:  $((Get-Item $traceFile  -ErrorAction SilentlyContinue).Length) bytes"
Write-Host "[diag] stdout file size: $((Get-Item $stdoutFile -ErrorAction SilentlyContinue).Length) bytes"
Write-Host "[diag] stderr file size: $((Get-Item $stderrFile -ErrorAction SilentlyContinue).Length) bytes"
