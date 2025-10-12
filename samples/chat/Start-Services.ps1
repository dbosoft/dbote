#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Write-Host "Building Bote Chat Sample..." -ForegroundColor Cyan
Write-Host ""

Push-Location ./Dbosoft.Bote.Samples.Chat.Cloud
Write-Host "Publishing Cloud service..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Samples.Chat.Connector
Write-Host "Publishing Chat Connector..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BasicIdentityProvider
Write-Host "Publishing BasicIdentityProvider..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BoteWorker
Write-Host "Publishing BoteWorker..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-x64
Pop-Location

Write-Host ""
Write-Host "Building Docker images..." -ForegroundColor Yellow
docker compose build

Write-Host ""
Write-Host "Starting services..." -ForegroundColor Yellow
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Bote Chat Multi-Tenant Demo" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Once started, open these URLs in your browser:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tenant A - Connector A: " -NoNewline -ForegroundColor White
Write-Host "http://localhost:5000" -ForegroundColor Yellow
Write-Host "  Tenant A - Connector B: " -NoNewline -ForegroundColor White
Write-Host "http://localhost:5001" -ForegroundColor Yellow
Write-Host "  Tenant B - Connector A: " -NoNewline -ForegroundColor White
Write-Host "http://localhost:5002" -ForegroundColor Yellow
Write-Host ""
Write-Host "Demo: Messages between 5000 and 5001 work (same tenant)" -ForegroundColor Green
Write-Host "      Messages between 5000 and 5002 are isolated (different tenants)" -ForegroundColor Green
Write-Host ""
Write-Host "Press Ctrl+C to stop all services" -ForegroundColor Gray
Write-Host ""

docker compose up
