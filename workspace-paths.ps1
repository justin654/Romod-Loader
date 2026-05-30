$ErrorActionPreference = "Stop"

function Get-WorkspaceConfigValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $configPath = Join-Path $WorkspaceRoot "Workspace.local.props"
    if (-not (Test-Path -LiteralPath $configPath)) {
        return $null
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $configPath
        $propertyNode = $xml.Project.PropertyGroup.$PropertyName | Select-Object -First 1
        if ($null -eq $propertyNode) {
            return $null
        }

        $value = $propertyNode.ToString().Trim()
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $null
        }

        return $value
    } catch {
        throw "Failed to read workspace config '$configPath': $($_.Exception.Message)"
    }
}

function Resolve-ConfiguredPath {
    param(
        [string]$ExplicitValue,
        [string]$ConfigValue,
        [string]$EnvironmentVariableName,
        [Parameter(Mandatory = $true)]
        [string]$DefaultValue
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitValue)) {
        return [System.IO.Path]::GetFullPath($ExplicitValue)
    }

    if (-not [string]::IsNullOrWhiteSpace($ConfigValue)) {
        return [System.IO.Path]::GetFullPath($ConfigValue)
    }

    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentVariableName)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return [System.IO.Path]::GetFullPath($environmentValue)
    }

    return [System.IO.Path]::GetFullPath($DefaultValue)
}

function Resolve-RomesteadPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot,
        [string]$GameRoot,
        [string]$ServerRoot
    )

    $configGameRoot = Get-WorkspaceConfigValue -WorkspaceRoot $WorkspaceRoot -PropertyName "RomesteadGameRoot"
    $configServerRoot = Get-WorkspaceConfigValue -WorkspaceRoot $WorkspaceRoot -PropertyName "RomesteadServerRoot"

    $resolvedGameRoot = Resolve-ConfiguredPath `
        -ExplicitValue $GameRoot `
        -ConfigValue $configGameRoot `
        -EnvironmentVariableName "ROMESTEAD_GAME_ROOT" `
        -DefaultValue "C:\Program Files (x86)\Steam\steamapps\common\romestead"

    $resolvedServerRoot = Resolve-ConfiguredPath `
        -ExplicitValue $ServerRoot `
        -ConfigValue $configServerRoot `
        -EnvironmentVariableName "ROMESTEAD_SERVER_ROOT" `
        -DefaultValue "C:\Program Files (x86)\Steam\steamapps\common\romestead-server"

    [pscustomobject]@{
        WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
        GameRoot = $resolvedGameRoot
        ServerRoot = $resolvedServerRoot
        GameModRoot = Join-Path $resolvedGameRoot "romestead_modding"
        ServerModRoot = Join-Path $resolvedServerRoot "romestead_modding"
    }
}
