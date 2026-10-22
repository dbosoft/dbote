#Requires -Version 7.4
<#
.SYNOPSIS
    Builds what terraform deploys: the worker image (pushed to the registry) and the identity
    provider's package (artifacts/Dbosoft.Bote.BasicIdentityProvider.zip, uploaded by terraform).
    Uses podman when it is there, docker otherwise.
#>
[CmdletBinding()]
param(
    [string]$Registry = 'acrdbotecmdev',
    [string]$Tag = 'latest'
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$artifactsPath = Join-Path $PSScriptRoot 'artifacts'
$src = Join-Path $PSScriptRoot '../src/workers/src'
$container = (Get-Command podman -ErrorAction SilentlyContinue) ? 'podman' : 'docker'
$loginServer = az acr show --name $Registry --query loginServer --output tsv

# The worker: its Dockerfile copies the linux-x64 publish output.
$worker = Join-Path $src 'Dbosoft.Bote.BoteWorker'
dotnet publish $worker -c Release -r linux-x64 --self-contained false -nodeReuse:false
$image = "$loginServer/dbote-worker:$Tag"
& $container build --platform linux/amd64 --tag $image $worker
az acr login --name $Registry --expose-token --output tsv --query accessToken |
    & $container login $loginServer --username 00000000-0000-0000-0000-000000000000 --password-stdin
& $container push $image

# The identity provider: a zip for Flex Consumption.
$idpOut = Join-Path $artifactsPath 'Dbosoft.Bote.BasicIdentityProvider'
Remove-Item $idpOut -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $src 'Dbosoft.Bote.BasicIdentityProvider') -c Release -r linux-x64 --self-contained false `
    -nodeReuse:false -o $idpOut
Compress-Archive -Path "$idpOut/*" -DestinationPath (Join-Path $artifactsPath 'Dbosoft.Bote.BasicIdentityProvider.zip') -Force
