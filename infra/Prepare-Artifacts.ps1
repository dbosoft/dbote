#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$acr = 'acrsuperbustest-czbddsczaug6adgm.azurecr.io'
$artifactsPath = Join-Path $PSScriptRoot 'artifacts'
try {
    Push-Location ../src/workers/src/SuperBus.SuperBusWorker
    dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'SuperBus.SuperBusWorker')
    Compress-Archive `
        -Path "$(Join-Path $artifactsPath 'SuperBus.SuperBusWorker')/*" `
        -DestinationPath (Join-Path $artifactsPath 'SuperBus.SuperBusWorker.zip') `
        -Force
    Pop-Location

    Push-Location ../test/benchmark/SuperBus.Benchmark.Cloud
    dotnet publish -c Release -r linux-x64
    docker build --tag "$acr/superbus-benchmark/cloud" ./
    docker push "$acr/superbus-benchmark/cloud"
    Pop-Location

    Push-Location ../test/benchmark/SuperBus.Benchmark.Service
    dotnet publish -c Release -r linux-x64
    docker build --tag "$acr/superbus-benchmark/service" ./
    docker push "$acr/superbus-benchmark/service"
    Pop-Location
} finally {
    Pop-Location
}
