#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter()]
    [string]$IdentityProviderUrl = "http://localhost:7250"
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Write-Host "Registering connectors for Simple sample..." -ForegroundColor Cyan
Write-Host ""

$connectorsJsonPath = Join-Path $PSScriptRoot "connectors.json"
$registerScript = Join-Path $PSScriptRoot ".." ".." "dev" "Register-Connectors.ps1"

& $registerScript -IdentityProviderUrl $IdentityProviderUrl -ConnectorsJsonPath $connectorsJsonPath

if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}
