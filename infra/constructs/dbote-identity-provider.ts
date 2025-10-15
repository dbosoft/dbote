import { Construct } from "constructs";
import { Environnment } from "../environment";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { StorageContainer } from "@cdktf/provider-azurerm/lib/storage-container";
import { StorageBlob } from "@cdktf/provider-azurerm/lib/storage-blob";
import { ServicePlan } from "@cdktf/provider-azurerm/lib/service-plan";
import { FunctionAppFlexConsumption } from "@cdktf/provider-azurerm/lib/function-app-flex-consumption";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { AssetType, TerraformAsset } from "cdktf";
import { KeyVault } from "@cdktf/provider-azurerm/lib/key-vault";

export class DboteIdentityProvider extends Construct {

    public readonly endpoint: string;
    public readonly functionPrincipalId: string;
    public readonly keyVaultId: string;

    private readonly functionApp: FunctionAppFlexConsumption;

    constructor(scope: Construct, environment: Environnment, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'idp'));

        // Microsoft recommends to use a dedicated key vault for each function app
        // as the function secrets are not scoped.
        // See https://learn.microsoft.com/en-us/azure/azure-functions/functions-app-settings#azurewebjobssecretstoragekeyvaulturi
        const functionKeyVault = new KeyVault(this, environment.formatName('kv', 'idp'), {
            name: environment.formatName('kv', 'idp'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            skuName: 'standard',
            tenantId: environment.tenantId,
            purgeProtectionEnabled: false,
            // Explicitly disable access policies
            // TODO required?
            //accessPolicy: [],
            enableRbacAuthorization: true,
        });
        this.keyVaultId = functionKeyVault.id;

        const functionStorageAccount = new StorageAccount(this, environment.formatSafeName('st', 'idp'), {
            name: environment.formatSafeName('st', 'idp'),
            location:  environment.location,
            resourceGroupName: environment.resourceGroup,
            accountTier: "Standard",
            accountReplicationType: "LRS",
        });

        const functionStorageContainer = new StorageContainer(this, environment.formatSafeName('stc', 'idp-packages'), {
            name: environment.formatSafeName('stc', 'idp-packages'),
            storageAccountId: functionStorageAccount.id,
            containerAccessType: "private",
        });

        const asset = new TerraformAsset(this, environment.formatName('cdktf', 'idp-asset'), {
            path: './artifacts/Dbosoft.Bote.BasicIdentityProvider.zip',
            type: AssetType.FILE,
        })

        new StorageBlob(this, environment.formatSafeName('stb', 'idp-package'), {
            // OneDeploy expects excactly this name for the function app package
            // See https://learn.microsoft.com/en-us/azure/azure-functions/functions-infrastructure-as-code?pivots=flex-consumption-plan&tabs=bicep%2Cwindows%2Cdevops#deployment-package
            name: 'released-package.zip',
            storageAccountName: functionStorageAccount.name,
            storageContainerName: functionStorageContainer.name,
            source: asset.path,
            type: 'Block',
        });

        const functionServicePlan = new ServicePlan(this, environment.formatName('asp', 'idp'), {
            name: environment.formatName('asp', 'idp'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            skuName: 'FC1',
            osType: 'Linux',
        });

        const functionAppName = environment.formatName('func', 'idp');
        this.functionApp = new FunctionAppFlexConsumption(this, functionAppName, {
            name: functionAppName,
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            servicePlanId: functionServicePlan.id,

            runtimeName: 'dotnet-isolated',
            runtimeVersion: '8.0',
            instanceMemoryInMb: 512,
            storageContainerType: 'blobContainer',
            storageAuthenticationType: 'SystemAssignedIdentity',
            storageContainerEndpoint: `${functionStorageAccount.primaryBlobEndpoint}${functionStorageContainer.name}`,
            siteConfig: {
                applicationInsightsConnectionString: appInsightsConnection,
            },
            identity: {
                type: 'SystemAssigned',
            },
            appSettings: {
                // Workaround for missing support in the Terraform provider.
                // Taken from https://github.com/Azure-Samples/azure-functions-flex-consumption-samples/blob/ec5ba183e89569f58771dfb5f3a6c41fca37269b/IaC/terraformazurerm/main.tf#L84
                // Can be removed when https://github.com/hashicorp/terraform-provider-azurerm/pull/29099 is released
                'AzureWebJobsStorage': '',
                'AzureWebJobsStorage__accountName': functionStorageAccount.name,
                'AzureWebJobsSecretStorageType': 'keyvault',
                'AzureWebJobsSecretStorageKeyVaultUri': functionKeyVault.vaultUri,
                'BasicIdentityProvider__Authority': `https://${functionAppName}.azurewebsites.net/api`,
                // Audience will be set by main.ts after worker is created
            },
        });

        this.functionPrincipalId = this.functionApp.identity.principalId;

        new RoleAssignment(this, environment.formatName('role', 'idp-st-blob'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Blob Data Contributor',
            principalId: this.functionApp.identity.principalId,
        });

        // Microsoft recommends table storage access for writing certain diagnostic events
        // See note in https://learn.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob&pivots=programming-language-csharp#connecting-to-host-storage-with-an-identity
        new RoleAssignment(this, environment.formatName('role', 'idp-st-table'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Table Data Contributor',
            principalId: this.functionApp.identity.principalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'idp-kv'),{
            scope: functionKeyVault.id,
            roleDefinitionName: 'Key Vault Secrets Officer',
            principalId: this.functionApp.identity.principalId,
        })

        this.endpoint = `https://${this.functionApp.defaultHostname}/api`
    }

    addAppSetting(key: string, value: string) {
        this.functionApp.appSettingsInput![key] = value;
    }
}
