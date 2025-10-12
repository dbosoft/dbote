# Deployment to Azure

## How to deploy locally
You need docker, Azure CLI and Node 20 with npm.

1. Install packages: `npm install`
2. Login to Azure: `az login` (make sure to select the correct subscription)
3. Build the artifacts: `./Prepare-Artifacts.ps1` (build the Azure function zip and pushes the containers to the ACR)
4. Assign yourself `Storage Blob Data Contributor` for the storage account `stdbotestfstate` (contains the Terraform state)
5. Run CDKTF with `npm run build` and `npm run synth`
6. Go to: `./cdktf.out/stacks/infra`
7. Run `terraform apply` (might take several minutes)
