# Deployment to Azure

## How to deploy locally
You need podman (or docker), Azure CLI, Terraform and Node 20 with npm. Run the scripts from PowerShell:
Git Bash rewrites arguments starting with `/subscriptions/...` into file paths.

1. Install packages: `npm install`
2. Login to Azure: `az login` and select the subscription (`main.ts` names tenant and subscription)
3. Once per subscription: `./Initialize-Platform.ps1` creates the resource group, the storage account
   for the Terraform state, the container registry and gives you the data roles terraform needs
4. Build the artifacts: `./Prepare-Artifacts.ps1` (pushes the worker image, zips the identity provider)
5. Synthesize: `$env:TS_NODE_PREFER_TS_EXTS='true'; npx cdktf synth`
6. In `./cdktf.out/stacks/infra`: `terraform init`, `terraform apply` (the identity provider's Flex
   Consumption app takes 10-15 minutes)
7. Once after the first apply: put a P-256 key as PEM into the secret `idp-signing-key` of
   `kv-dbote-idp-cmdev`, which the identity provider signs with

## Debugging
The following environment variables can be configured to generate debugging output:
- `$env:CDKTF_LOG_LEVEL='debug'`
- `$env:TF_LOG='DEBUG'`
