#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter()]
    [string]$IdentityProviderUrl = "http://localhost:7250"
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Write-Host "Registering clients for Chat sample..." -ForegroundColor Cyan
Write-Host ""

$clientsJsonPath = Join-Path $PSScriptRoot "clients.json"
$registerScript = Join-Path $PSScriptRoot ".." ".." "dev" "Register-Clients.ps1"

& $registerScript -IdentityProviderUrl $IdentityProviderUrl -ClientsJsonPath $clientsJsonPath

if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}
