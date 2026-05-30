<#
.SYNOPSIS
    Downloads the xnbcli binary into tools/xnbcli/.

.DESCRIPTION
    xnbcli.exe (and its native .node libraries) are not committed to the repo
    (see .gitignore). This script fetches the pinned release and extracts the
    binaries into tools/xnbcli/ so the Mod Value Editor's "Convert XNB to PNG"
    feature works. Re-running it overwrites the existing binaries.
#>
[CmdletBinding()]
param(
    [string] $Version = "v1.0.7"
)

$ErrorActionPreference = "Stop"

$targetDir = Join-Path $PSScriptRoot "xnbcli"
$url = "https://github.com/LeonBlade/xnbcli/releases/download/$Version/xnbcli-windows-x64.zip"

$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "xnbcli-$Version.zip"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "xnbcli-$Version"

Write-Host "Downloading xnbcli $Version from $url"
Invoke-WebRequest -Uri $url -OutFile $tempZip

if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
Expand-Archive -Path $tempZip -DestinationPath $tempDir -Force

$exe = Get-ChildItem -Path $tempDir -Recurse -Filter "xnbcli.exe" | Select-Object -First 1
if ($null -eq $exe) {
    throw "xnbcli.exe not found in the downloaded archive ($url)."
}

# Copy the executable plus any native libraries / DLLs that sit beside it.
$sourceDir = $exe.Directory.FullName
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Get-ChildItem -Path $sourceDir -Include "xnbcli.exe", "*.node", "*.dll" -Recurse |
    ForEach-Object { Copy-Item $_.FullName -Destination $targetDir -Force }

Remove-Item $tempZip -Force
Remove-Item $tempDir -Recurse -Force

Write-Host "xnbcli installed to $targetDir"
