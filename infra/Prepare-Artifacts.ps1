#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$acr = 'acrbotetest.azurecr.io'
$acrName = 'acrbotetest'
$artifactsPath = Join-Path $PSScriptRoot 'artifacts'

az acr login --name $acrName

try {
    Push-Location ../src/workers/src/Dbosoft.Bote.BoteWorker
    dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'Dbosoft.Bote.BoteWorker')
    Compress-Archive `
        -Path "$(Join-Path $artifactsPath 'Dbosoft.Bote.BoteWorker')/*" `
        -DestinationPath (Join-Path $artifactsPath 'Dbosoft.Bote.BoteWorker.zip') `
        -Force
    Pop-Location

    Push-Location ../src/workers/src/Dbosoft.Bote.BasicIdentityProvider
    dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'Dbosoft.Bote.BasicIdentityProvider')
    Compress-Archive `
        -Path "$(Join-Path $artifactsPath 'Dbosoft.Bote.BasicIdentityProvider')/*" `
        -DestinationPath (Join-Path $artifactsPath 'Dbosoft.Bote.BasicIdentityProvider.zip') `
        -Force
    Pop-Location

    Push-Location ../test/benchmark/Dbosoft.Bote.Benchmark.Cloud
    dotnet publish -c Release -r linux-x64
    docker build --tag "$acr/dbote-benchmark/cloud" ./
    docker push "$acr/dbote-benchmark/cloud"
    Pop-Location

    Push-Location ../test/benchmark/Dbosoft.Bote.Benchmark.Service
    dotnet publish -c Release -r linux-x64
    docker build --tag "$acr/dbote-benchmark/service" ./
    docker push "$acr/dbote-benchmark/service"
    Pop-Location
} finally {
    Pop-Location
}
