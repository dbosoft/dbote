#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter()]
    [string]$IdentityProviderUrl = "http://localhost:7250",

    [Parameter()]
    [string]$ClientsJsonPath = "$PSScriptRoot/config/clients.json"
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Write-Host "Registering clients with BasicIdentityProvider at $IdentityProviderUrl" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $ClientsJsonPath)) {
    Write-Error "Clients configuration file not found: $ClientsJsonPath"
    exit 1
}

try {
    $clients = Get-Content $ClientsJsonPath -Raw | ConvertFrom-Json
    Write-Host "Found $($clients.Count) client(s) to register" -ForegroundColor Yellow
    Write-Host ""

    $successCount = 0
    $failureCount = 0

    foreach ($client in $clients) {
        try {
            $payload = @{
                TenantId = $client.TenantId
                Id = $client.ClientId
                PublicKey = $client.PublicKey
            } | ConvertTo-Json

            $response = Invoke-WebRequest `
                -Uri "$IdentityProviderUrl/api/register-client" `
                -Method POST `
                -Body $payload `
                -ContentType "application/json" `
                -ErrorAction Stop

            if ($response.StatusCode -eq 200) {
                Write-Host "✓ Registered client $($client.TenantId)/$($client.ClientId)" -ForegroundColor Green
                $successCount++
            }
        }
        catch {
            Write-Host "✗ Failed to register client $($client.TenantId)/$($client.ClientId): $($_.Exception.Message)" -ForegroundColor Red
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
    Write-Error "Failed to process clients file: $_"
    exit 1
}
