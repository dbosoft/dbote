#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$artifactsPath = Join-Path $PSScriptRoot 'artifacts'

# Push-Location ./SuperBus.Samples.Simple.Cloud
# dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'SuperBus.Samples.Simple.Cloud')
# Pop-Location

# Push-Location ./SuperBus.Samples.Simple.Connector
# dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'SuperBus.Samples.Simple.Connector')
# Pop-Location

Push-Location ../src/workers/src/SuperBus.SuperBusWorker
dotnet publish -c Release -r linux-x64 -o (Join-Path $artifactsPath 'SuperBus.SuperBusWorker')
Compress-Archive `
    -Path "$(Join-Path $artifactsPath 'SuperBus.SuperBusWorker')/*" `
    -DestinationPath (Join-Path $artifactsPath 'SuperBus.SuperBusWorker.zip') `
    -Force
Pop-Location
