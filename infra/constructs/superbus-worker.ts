import { Construct } from "constructs";
import { Environnment } from "../environment";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { StorageContainer } from "@cdktf/provider-azurerm/lib/storage-container";
import { StorageBlob } from "@cdktf/provider-azurerm/lib/storage-blob";
import { ServicePlan } from "@cdktf/provider-azurerm/lib/service-plan";
import { FunctionAppFlexConsumption } from "@cdktf/provider-azurerm/lib/function-app-flex-consumption";
import { DataAzurermFunctionAppHostKeys } from "@cdktf/provider-azurerm/lib/data-azurerm-function-app-host-keys";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { AssetType, TerraformAsset } from "cdktf";

export class SuperBusWorker extends Construct {

    public readonly signalREndpoint: string;
    public readonly functionPrincipalId: string;
    public readonly superBusEndpoint: string;

    constructor(scope: Construct, environment: Environnment, appConfigEndpoint: string, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'worker'));

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
            path: './artifacts/SuperBus.SuperBusWorker.zip',
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

        const functionApp = new FunctionAppFlexConsumption(this, environment.formatName('func'), {
            name: environment.formatName('func'),
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
                'SuperBus__AppConfiguration__Endpoint': appConfigEndpoint,
                'SuperBus__AppConfiguration__Environment': environment.environment,
                'SuperBus__AppConfiguration__Prefix': 'SuperBus:Worker',
            },
        });
        
        this.functionPrincipalId = functionApp.identity.principalId;

        new RoleAssignment(this, environment.formatName('role', 'func-st'), {
            scope: functionStorageContainer.id,
            roleDefinitionName: 'Storage Blob Data Contributor',
            principalId: functionApp.identity.principalId,
        });

        const functionAppKeys = new DataAzurermFunctionAppHostKeys(this, environment.formatName('cdktf', 'func-keys'), {
            name: functionApp.name,
            resourceGroupName: functionApp.resourceGroupName,
        });

        this.superBusEndpoint = `https://${functionApp.defaultHostname}/api`
        this.signalREndpoint = `https://${functionApp.defaultHostname}/runtime/webhooks/signalr?code=${functionAppKeys.defaultFunctionKey}`
    }
}
