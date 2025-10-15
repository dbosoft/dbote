# Deployment to Azure

## How to deploy locally
You need docker, Azure CLI and Node 20 with npm.

1. Install packages: `npm install`
2. Login to Azure: `az login` (make sure to select the correct subscription)
3. Build the artifacts: `./Prepare-Artifacts.ps1` (build the Azure function zip and pushes the containers to the ACR)
4. Assign yourself `Storage Blob Data Contributor` for the storage account `stdbotestfstate` (contains the Terraform state)
5. Assign yourself `App Configuration Data Owner` for the resource group `rg-dbote-test`
6. Run CDKTF with `npm run build` and `npm run synth`
7. Go to: `./cdktf.out/stacks/infra`
8. Run `terraform apply` (might take several minutes)

## Debugging
The following environment variables can be configured to generate debugging output:
- `$env:CDKTF_LOG_LEVEL='debug'`
- `$env:TF_LOG='DEBUG'`
