#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter()]
    [string]$IdentityProviderUrl = "http://localhost:7250",

    [Parameter()]
    [string]$ConnectorsJsonPath = "$PSScriptRoot/config/connectors.json"
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Write-Host "Registering connectors with BasicIdentityProvider at $IdentityProviderUrl" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $ConnectorsJsonPath)) {
    Write-Error "Connectors configuration file not found: $ConnectorsJsonPath"
    exit 1
}

try {
    $connectors = Get-Content $ConnectorsJsonPath -Raw | ConvertFrom-Json
    Write-Host "Found $($connectors.Count) connector(s) to register" -ForegroundColor Yellow
    Write-Host ""

    $successCount = 0
    $failureCount = 0

    foreach ($connector in $connectors) {
        try {
            $payload = @{
                TenantId = $connector.TenantId
                Id = $connector.ConnectorId
                PublicKey = $connector.PublicKey
            } | ConvertTo-Json

            $response = Invoke-WebRequest `
                -Uri "$IdentityProviderUrl/api/register-connector" `
                -Method POST `
                -Body $payload `
                -ContentType "application/json" `
                -ErrorAction Stop

            if ($response.StatusCode -eq 200) {
                Write-Host "✓ Registered connector $($connector.TenantId)/$($connector.ConnectorId)" -ForegroundColor Green
                $successCount++
            }
        }
        catch {
            Write-Host "✗ Failed to register connector $($connector.TenantId)/$($connector.ConnectorId): $($_.Exception.Message)" -ForegroundColor Red
            $failureCount++
        }
    }

    Write-Host ""
    Write-Host "Registration complete: $successCount succeeded, $failureCount failed" -ForegroundColor $(if ($failureCount -eq 0) { "Green" } else { "Yellow" })

    if ($failureCount -gt 0) {
        exit 1
    }

    exit 0
}
catch {
    Write-Error "Failed to process connectors file: $_"
    exit 1
}
