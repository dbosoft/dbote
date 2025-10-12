#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Push-Location ./Dbosoft.Bote.Benchmark.Cloud
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Benchmark.Connector
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Benchmark.Service
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Benchmark.Runner
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BoteWorker
dotnet publish -c Release -r linux-x64
Pop-Location

docker compose build
docker compose up
