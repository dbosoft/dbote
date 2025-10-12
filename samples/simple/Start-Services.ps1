#Requires -Version 7.4
[CmdletBinding()]
param()

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

Push-Location ./Dbosoft.Bote.Samples.Simple.Cloud
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Samples.Simple.Connector
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BasicIdentityProvider
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BoteWorker
dotnet publish -c Release -r linux-x64
Pop-Location

docker compose build
docker compose up
