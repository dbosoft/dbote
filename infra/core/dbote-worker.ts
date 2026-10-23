import { Construct } from "constructs";
import { Environnment } from "./environment";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { KeyVault } from "@cdktf/provider-azurerm/lib/key-vault";
import { ContainerAppEnvironment } from "@cdktf/provider-azurerm/lib/container-app-environment";
import { UserAssignedIdentity } from "@cdktf/provider-azurerm/lib/user-assigned-identity";
import { StorageQueue } from "@cdktf/provider-azurerm/lib/storage-queue";
import { Fn, Token } from "cdktf";
import { AzapiResource } from "./azapi";

export interface DboteContainerRegistry {
    /** Resource id of the registry; the worker's identity is granted AcrPull on it. */
    readonly id: string;
    /** e.g. `myregistry.azurecr.io` */
    readonly loginServer: string;
}

export interface DboteWorkerProps {
    readonly subscriptionId: string;
    readonly appConfigurationEndpoint: string;
    readonly appInsightsConnection: string;
    /** Shared platform registry the worker image is pulled from. */
    readonly registry: DboteContainerRegistry;
    /** Repository of the worker image in the registry. Default `dbote-worker`. */
    readonly imageRepository?: string;
    /** Default `latest`. */
    readonly imageTag?: string;
    /** Replica bounds. Default 0..3: scales to zero, accepting a cold start on wake. */
    readonly minReplicas?: number;
    readonly maxReplicas?: number;
}

/**
 * BoteWorker hosted as **Azure Functions on Azure Container Apps** (`kind=functionapp`).
 *
 * Unlike Flex Consumption, this runs the worker as a custom container image, which is the
 * deployment unit that lets many applications reuse one generic gateway image (Flex cannot
 * bring-your-own-container). All Functions bindings (SignalR-serverless, Service Bus / Blob /
 * Queue triggers) are preserved - only the hosting changes.
 *
 * The container registry belongs to the shared platform and is handed in, not created here.
 */
export class DboteWorker extends Construct {

    public readonly signalREndpoint: string;
    /**
     * The worker's user-assigned identity: it pulls the image and is the identity the worker runs
     * as. Known when the plan is made and unchanged by updates of the app, unlike a
     * system-assigned identity read back from the app, whose id would be unknown on every change
     * and have Terraform replace each role assignment that names it.
     */
    public readonly functionPrincipalId: string;
    /** Client id of that identity, for the settings of identity-based connections. */
    public readonly identityClientId: string;
    public readonly dboteEndpoint: string;
    public readonly keyVaultId: string;
    /** The Functions host storage (AzureWebJobsStorage): host state, keys, databus-copy-monitor. */
    public readonly hostStorageAccount: StorageAccount;

    // Mutated by addAppSetting() after construction (DboteGateway). The azapi body below holds this
    // same array by reference, so pushes are picked up at synth time.
    private readonly envVars: { name: string; value: string }[] = [];

    constructor(scope: Construct, environment: Environnment, props: DboteWorkerProps) {
        super(scope, environment.formatName('cdktf', 'worker'));

        // Dedicated Key Vault for the Functions host secret storage (function keys are not scoped).
        // The SignalR serverless upstream needs the `signalr_extension` system key, which the host
        // writes here when AzureWebJobsSecretStorageType=keyvault.
        const functionKeyVault = new KeyVault(this, environment.formatName('kv', 'func'), {
            name: environment.formatName('kv', 'func'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            skuName: 'standard',
            tenantId: environment.tenantId,
            purgeProtectionEnabled: false,
            enableRbacAuthorization: true,
        });
        this.keyVaultId = functionKeyVault.id;

        // Mandatory storage account for the Functions host (triggers, state, keys, and the
        // databus-copy-monitor queue trigger). Connection string wired below for simplicity.
        const functionStorageAccount = new StorageAccount(this, environment.formatSafeName('st', 'func'), {
            name: environment.formatSafeName('st', 'func'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            accountTier: "Standard",
            accountReplicationType: "LRS",
        });
        this.hostStorageAccount = functionStorageAccount;

        // Pre-create the databus-copy-monitor queue that the DataBusCopyFileMonitor [QueueTrigger]
        // binds to. The app otherwise creates it lazily (on first attachment upload), but on ACA the
        // platform-generated KEDA azure-queue scaler then gets a 404 and can never scale to zero -
        // every trigger source must exist up front for scale-to-zero to work.
        new StorageQueue(this, environment.formatSafeName('stq', 'databus-copy-monitor'), {
            name: 'databus-copy-monitor',
            storageAccountName: functionStorageAccount.name,
        });

        const acr = props.registry;

        const appEnvironment = new ContainerAppEnvironment(this, environment.formatName('cae', 'worker'), {
            name: environment.formatName('cae', 'worker'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            workloadProfile: [{ name: 'Consumption', workloadProfileType: 'Consumption' }],
        });

        const identity = new UserAssignedIdentity(this, environment.formatName('id', 'worker'), {
            name: environment.formatName('id', 'worker'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
        });

        const acrPull = new RoleAssignment(this, environment.formatName('role', 'worker-acr'), {
            scope: acr.id,
            roleDefinitionName: 'AcrPull',
            principalId: identity.principalId,
        });
        this.functionPrincipalId = identity.principalId;
        this.identityClientId = identity.clientId;

        // Base app settings (SignalR/Service Bus connection settings are added by DboteGateway via
        // addAppSetting, since the Functions host parses them at startup to set up the triggers).
        this.envVars.push(
            { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' },
            { name: 'AzureWebJobsStorage', value: functionStorageAccount.primaryConnectionString },
            { name: 'AzureWebJobsSecretStorageType', value: 'keyvault' },
            { name: 'AzureWebJobsSecretStorageKeyVaultUri', value: functionKeyVault.vaultUri },
            { name: 'AzureWebJobsSecretStorageKeyVaultClientId', value: identity.clientId },
            // DefaultAzureCredential (App Configuration, Service Bus senders) picks the identity from it.
            { name: 'AZURE_CLIENT_ID', value: identity.clientId },
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: props.appInsightsConnection },
            { name: 'dbote__AppConfiguration__Endpoint', value: props.appConfigurationEndpoint },
            { name: 'dbote__AppConfiguration__Environment', value: environment.environment },
            { name: 'dbote__AppConfiguration__Prefix', value: 'dbote:Worker' },
        );

        const functionAppName = environment.formatName('func');
        const image = `${acr.loginServer}/${props.imageRepository ?? 'dbote-worker'}:${props.imageTag ?? 'latest'}`;

        const workerApp = new AzapiResource(this, functionAppName, {
            type: 'Microsoft.App/containerApps@2024-10-02-preview',
            name: functionAppName,
            parentId: `/subscriptions/${props.subscriptionId}/resourceGroups/${environment.resourceGroup}`,
            location: environment.location,
            // Preview API: let Azure validate the body at apply time rather than the provider schema.
            schemaValidationEnabled: false,
            dependsOn: [acrPull],
            identity: [{
                type: 'UserAssigned',
                identityIds: [identity.id],
            }],
            body: {
                kind: 'functionapp',
                properties: {
                    managedEnvironmentId: appEnvironment.id,
                    configuration: {
                        activeRevisionsMode: 'Single',
                        ingress: {
                            external: true,
                            targetPort: 80,
                            allowInsecure: false,
                            traffic: [{ latestRevision: true, weight: 100 }],
                        },
                        registries: [{ server: acr.loginServer, identity: identity.id }],
                    },
                    template: {
                        containers: [{
                            name: 'worker',
                            image: image,
                            resources: { cpu: 0.5, memory: '1Gi' },
                            env: this.envVars,
                        }],
                        // Scale-to-zero: serverless SignalR keeps client connections on the SignalR
                        // service (not the worker), and the signalr_extension system key is durable
                        // in Key Vault, so it survives scale-to-zero. The worker wakes on demand -
                        // HTTP/SignalR-upstream via the ingress scaler, cloud->client via the KEDA
                        // Service Bus scaler. Test env scales fully down; prod would set minReplicas>=1.
                        scale: { minReplicas: props.minReplicas ?? 0, maxReplicas: props.maxReplicas ?? 3 },
                    },
                },
            },
            responseExportValues: {
                fqdn: 'properties.configuration.ingress.fqdn',
            },
        });

        const fqdn = Token.asString(Fn.lookup(workerApp.output, 'fqdn'));
        this.dboteEndpoint = `https://${fqdn}/api`;

        // SignalR serverless upstream needs the signalr_extension system key. With keyvault secret
        // storage the host stores it in KV; reference it in the upstream URL. The `095` is the
        // escaped `_` in the KV secret name. SignalR resolves the reference with its own identity.
        const signalRKeyName = 'host--systemKey--signalr-095extension';
        this.signalREndpoint =
            `https://${fqdn}/runtime/webhooks/signalr?code={@Microsoft.KeyVault(SecretUri=${functionKeyVault.vaultUri}secrets/${signalRKeyName})}`;

        // Role assignments: the worker's identity needs data access to its host
        // storage account (diagnostics/keys) and Key Vault (secret storage).
        new RoleAssignment(this, environment.formatName('role', 'func-st-blob'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Blob Data Contributor',
            principalId: this.functionPrincipalId,
        });
        new RoleAssignment(this, environment.formatName('role', 'func-st-table'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Table Data Contributor',
            principalId: this.functionPrincipalId,
        });
        new RoleAssignment(this, environment.formatName('role', 'func-st-queue'), {
            scope: functionStorageAccount.id,
            roleDefinitionName: 'Storage Queue Data Contributor',
            principalId: this.functionPrincipalId,
        });
        new RoleAssignment(this, environment.formatName('role', 'func-kv'), {
            scope: functionKeyVault.id,
            roleDefinitionName: 'Key Vault Secrets Officer',
            principalId: this.functionPrincipalId,
        });
    }

    addAppSetting(key: string, value: string) {
        this.envVars.push({ name: key, value: value });
    }
}
