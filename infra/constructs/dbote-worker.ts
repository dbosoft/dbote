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

export class DboteWorker extends Construct {

    public readonly signalREndpoint: string;
    public readonly functionPrincipalId: string;
    public readonly dboteEndpoint: string;
    public readonly keyVaultId: string;

    private readonly functionApp: FunctionAppFlexConsumption;

    constructor(scope: Construct, environment: Environnment, appConfigEndpoint: string, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'worker'));

        // Microsoft recommends to use a dedicated key vault for each function app
        // as the function secrets are not scoped.
        // See https://learn.microsoft.com/en-us/azure/azure-functions/functions-app-settings#azurewebjobssecretstoragekeyvaulturi
        const functionKeyVault = new KeyVault(this, environment.formatName('kv', 'func'), {
            name: environment.formatName('kv', 'func'),
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

        const functionStorageAccount = new StorageAccount(this, environment.formatSafeName('st', 'func'), {
            name: environment.formatSafeName('st', 'func'),
            location:  environment.location,
            resourceGroupName: environment.resourceGroup,
            accountTier: "Standard",
            accountReplicationType: "LRS",
        });

        const functionStorageContainer = new StorageContainer(this, environment.formatSafeName('stc', 'func-packages'), {
            name: environment.formatSafeName('stc', 'func-packages'),
            storageAccountId: functionStorageAccount.id,
            containerAccessType: "private",
        });

        const asset = new TerraformAsset(this, environment.formatName('cdktf', 'func-asset'), {
            path: './artifacts/Dbosoft.Bote.BoteWorker.zip',
            type: AssetType.FILE,
        })

        new StorageBlob(this, environment.formatSafeName('stb', 'func-package'), {
            // OneDeploy expects excactly this name for the function app package
            // See https://learn.microsoft.com/en-us/azure/azure-functions/functions-infrastructure-as-code?pivots=flex-consumption-plan&tabs=bicep%2Cwindows%2Cdevops#deployment-package
            name: 'released-package.zip',
            storageAccountName: functionStorageAccount.name,
            storageContainerName: functionStorageContainer.name,
            source: asset.path,
            type: 'Block',
        });
    
        const functionServicePlan = new ServicePlan(this, environment.formatName('asp', 'func'), {
            name: environment.formatName('asp', 'func'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            skuName: 'FC1',
            osType: 'Linux',
        });

        const functionAppName = environment.formatName('func');
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
                'dbote__AppConfiguration__Endpoint': appConfigEndpoint,
                'dbote__AppConfiguration__Environment': environment.environment,
                'dbote__AppConfiguration__Prefix': 'dbote:Worker',
                'dbote__Worker__OpenId__Authority': `https://${functionAppName}.azurewebsites.net/api`
                // worker.addAppSetting('dbote__Worker__OpenId__Authority', worker.dboteEndpoint);
            },
        });
        
        this.functionPrincipalId = this.functionApp.identity.principalId;

        new RoleAssignment(this, environment.formatName('role', 'func-st-blob'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Blob Data Contributor',
            principalId: this.functionApp.identity.principalId,
        });

        // Microsoft recommends table storage access for writing certain diagnostic events
        // See note in https://learn.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob&pivots=programming-language-csharp#connecting-to-host-storage-with-an-identity
        new RoleAssignment(this, environment.formatName('role', 'func-st-table'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Table Data Contributor',
            principalId: this.functionApp.identity.principalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'func-kv'),{
            scope: functionKeyVault.id,
            roleDefinitionName: 'Key Vault Secrets Officer',
            principalId: this.functionApp.identity.principalId,
        })

        this.dboteEndpoint = `https://${this.functionApp.defaultHostname}/api`
        // By convention, the function key for the SignalR triggers is called signalr_extension.
        // Hence, we can hardcode the name (the 095 is just an escaped version of _). This also
        // breaks the dependency cycle between the function app and the Azure SignalR service.
        const signalRKeyName = 'host--systemKey--signalr-095extension';
        this.signalREndpoint = `https://${this.functionApp.defaultHostname}/runtime/webhooks/signalr?code={@Microsoft.KeyVault(SecretUri=${functionKeyVault.vaultUri}secrets/${signalRKeyName})}`
    }

    addAppSetting(key: string, value: string) {
        this.functionApp.appSettingsInput![key] = value;
    }
}
