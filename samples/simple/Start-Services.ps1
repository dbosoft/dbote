#Requires -Version 7.4
[CmdletBinding()]
param(
    # Run the cloud side as Azure Functions with Dbosoft.Bote.Cloud instead of the Rebus service
    [switch]$Functions
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$cloudProject = $Functions ? './Dbosoft.Bote.Samples.Simple.Functions' : './Dbosoft.Bote.Samples.Simple.Cloud'
Push-Location $cloudProject
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ./Dbosoft.Bote.Samples.Simple.Client
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BasicIdentityProvider
dotnet publish -c Release -r linux-x64
Pop-Location

Push-Location ../../src/workers/src/Dbosoft.Bote.BoteWorker
dotnet publish -c Release -r linux-x64
Pop-Location

$composeFiles = @('-f', 'docker-compose.yaml', '-f', 'docker-compose.override.yaml')
if ($Functions) {
    $composeFiles += @('-f', 'docker-compose.functions.yaml')
}

docker compose @composeFiles build
docker compose @composeFiles up
