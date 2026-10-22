#Requires -Version 7.4
<#
.SYNOPSIS
    Creates what the reference deployment expects to exist before terraform runs: the resource
    group, the storage account holding the terraform state and the container registry. Runs in the
    subscription `az` is signed in to; safe to run again.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-dbote-test',
    [string]$Location = 'swedencentral',
    [string]$StateAccount = 'stdbotetfstatesp',
    [string]$Registry = 'acrdbotecmdev'
)

$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'

az group create --name $ResourceGroup --location $Location --output none

az storage account create --name $StateAccount --resource-group $ResourceGroup --location $Location `
    --sku Standard_LRS --min-tls-version TLS1_2 --allow-blob-public-access false --output none
az storage container create --name tfstate --account-name $StateAccount --auth-mode login --output none

az acr create --name $Registry --resource-group $ResourceGroup --location $Location --sku Basic --output none

# The one running terraform reads the state with its own identity and writes App Configuration keys.
$me = az ad signed-in-user show --query id --output tsv
$group = az group show --name $ResourceGroup --query id --output tsv
foreach ($role in 'Storage Blob Data Contributor', 'App Configuration Data Owner') {
    az role assignment create --assignee-object-id $me --assignee-principal-type User `
        --role $role --scope $group --output none
}
