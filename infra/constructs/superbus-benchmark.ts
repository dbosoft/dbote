import { Construct } from "constructs";
import { Environnment } from "../environment";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { StorageContainer } from "@cdktf/provider-azurerm/lib/storage-container";
import { AssetType, TerraformAsset } from "cdktf";
import { StorageBlob } from "@cdktf/provider-azurerm/lib/storage-blob";
import { ServicePlan } from "@cdktf/provider-azurerm/lib/service-plan";
import { LinuxWebApp } from "@cdktf/provider-azurerm/lib/linux-web-app";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";

export class SuperBusBenchmark extends Construct {

    public readonly cloudPrincipalId: string;
    public readonly servicePrincipalId: string;

    constructor(scope: Construct, environment: Environnment, appConfigConnection: string, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'benchmark'));
        
        const appStorageAccount = new StorageAccount(this, environment.formatSafeName('st', 'app'), {
            name: environment.formatSafeName('st', 'app'),
            location:  environment.location,
            resourceGroupName: environment.resourceGroup,
            accountTier: "Standard",
            accountReplicationType: "LRS",
        });

        const appStorageContainer = new StorageContainer(this, environment.formatSafeName('stc', 'app-cloud-packages'), {
            name: environment.formatSafeName('stc', 'app'),
            storageAccountId: appStorageAccount.id,
            containerAccessType: "private",
        });

        const cloudAsset = new TerraformAsset(this, environment.formatName('cdktf', 'app-cloud-asset'), {
            path: './artifacts/SuperBus.Benchmark.Cloud.zip',
            type: AssetType.FILE,
        })

        const cloudPackageBlob = new StorageBlob(this, environment.formatSafeName('stb', 'app-cloud-package'), {
            name: environment.formatSafeName('stb', 'app-cloud-package'),
            storageAccountName: appStorageAccount.name,
            storageContainerName: appStorageContainer.name,
            source: cloudAsset.path,
            type: 'Block',
        });

        const serviceAsset = new TerraformAsset(this, environment.formatName('cdktf', 'app-service-asset'), {
            path: './artifacts/SuperBus.Benchmark.Service.zip',
            type: AssetType.FILE,
        })

        const servicePackageBlob = new StorageBlob(this, environment.formatSafeName('stb', 'app-service-package'), {
            name: environment.formatSafeName('stb', 'app-service-package'),
            storageAccountName: appStorageAccount.name,
            storageContainerName: appStorageContainer.name,
            source: serviceAsset.path,
            type: 'Block',
        });

        const appServicePlan = new ServicePlan(this, environment.formatName('asp', 'app'), {
            name: environment.formatName('asp', 'app'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            skuName: 'B1',
            osType: 'Linux',
        });

        const cloudApp = new LinuxWebApp(this, environment.formatName('app', 'cloud'), {
            name: environment.formatName('app', 'cloud'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            servicePlanId: appServicePlan.id,
            siteConfig: {},
            identity: {
                type: 'SystemAssigned',
            },
            appSettings: {
                'WEBSITE_RUN_FROM_PACKAGE': `https://${appStorageAccount.primaryBlobEndpoint}/${appStorageContainer.name}/${cloudPackageBlob.name}`,
                'APPLICATIONINSIGHTS_CONNECTION_STRING ': appInsightsConnection,
                'SuperBus__AppConfiguration__Endpoint': appConfigConnection,
                'SuperBus__AppConfiguration__Environment': environment.environment,
                'SuperBus__AppConfiguration__Prefix': 'SuperBus:Cloud',
            }
        });

        this.cloudPrincipalId = cloudApp.identity.principalId;

        const serviceApp = new LinuxWebApp(this, environment.formatName('app', 'service'), {
            name: environment.formatName('app', 'service'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            servicePlanId: appServicePlan.id,
            siteConfig: {},
            identity: {
                type: 'SystemAssigned',
            },
            appSettings: {
                'WEBSITE_RUN_FROM_PACKAGE': `https://${appStorageAccount.primaryBlobEndpoint}/${appStorageContainer.name}/${servicePackageBlob.name}`,
                'APPLICATIONINSIGHTS_CONNECTION_STRING': appInsightsConnection,
                'SuperBus__AppConfiguration__Endpoint': appConfigConnection,
                'SuperBus__AppConfiguration__Environment': environment.environment,
                'SuperBus__AppConfiguration__Prefix': 'SuperBus:Service',
            }
        });

        this.servicePrincipalId = serviceApp.identity.principalId;

        new RoleAssignment(this, environment.formatName('role', 'app-cloud-st'), {
            scope: appStorageAccount.id,
            roleDefinitionName: 'Storage Blob Data Reader',
            principalId: cloudApp.identity.principalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'func-st'), {
            scope: appStorageAccount.id,
            roleDefinitionName: 'Storage Blob Data Reader',
            principalId: serviceApp.identity.principalId,
        });
    }
}