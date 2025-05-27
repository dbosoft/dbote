#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Push-Location ./SuperBus.Benchmark.Cloud
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./SuperBus.Benchmark.Connector
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./SuperBus.Benchmark.Service
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./SuperBus.Benchmark.Runner
dotnet publish -c Release -r linux-x64
Pop-Location


Push-Location ../../src/workers/src/SuperBus.SuperBusWorker
dotnet publish -c Release -r linux-x64
Pop-Location

docker compose build
docker compose up
