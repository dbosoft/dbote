import { Construct } from "constructs";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { ServicebusNamespace } from "@cdktf/provider-azurerm/lib/servicebus-namespace";
import { ServicebusQueue } from "@cdktf/provider-azurerm/lib/servicebus-queue";
import { SignalrService } from "@cdktf/provider-azurerm/lib/signalr-service";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { StorageContainer } from "@cdktf/provider-azurerm/lib/storage-container";
import { StorageTable } from "@cdktf/provider-azurerm/lib/storage-table";
import { StorageManagementPolicy } from "@cdktf/provider-azurerm/lib/storage-management-policy";
import { Environnment } from "./environment";
import { DboteContainerRegistry, DboteWorker } from "./dbote-worker";

/**
 * The identity provider the worker validates client tokens against. The IdP itself belongs to
 * the application layer (BasicIdentityProvider is dev-only); the gateway only consumes it.
 */
export interface DboteOpenIdSettings {
    readonly authority: string;
    readonly jwksUri: string;
    readonly audience: string;
    readonly requiredScope: string;
}

/**
 * Inputs of the gateway. Shared platform services (container registry, App Configuration,
 * Application Insights) are handed in, not created here, so one platform can carry several
 * gateway deployments.
 */
export interface DboteGatewayProps {
    readonly subscriptionId: string;
    readonly registry: DboteContainerRegistry;
    readonly appConfigurationId: string;
    readonly appConfigurationEndpoint: string;
    readonly appInsightsConnection: string;
    readonly openId: DboteOpenIdSettings;
    /** Worker image in the registry; defaults `dbote-worker:latest`. */
    readonly workerImageRepository?: string;
    readonly workerImageTag?: string;
    /** Worker replica bounds; default 0..3. Keep min >= 1 where the cold start (~30s) matters. */
    readonly workerMinReplicas?: number;
    readonly workerMaxReplicas?: number;
}

/**
 * The reusable core of a bote deployment: the BoteWorker plus everything it needs - Service Bus
 * namespace with the gateway queues, serverless SignalR, the storage account for client queues,
 * subscriptions and DataBus blobs, and the worker's role assignments and settings.
 *
 * Applications build on the outputs: they send to {@link clientsQueueName}, receive from
 * {@link cloudQueueName} and are granted access to {@link serviceBusNamespace} themselves.
 */
export class DboteGateway extends Construct {

    public readonly worker: DboteWorker;
    public readonly serviceBusNamespace: ServicebusNamespace;
    public readonly fullyQualifiedNamespace: string;
    public readonly cloudQueue: ServicebusQueue;
    public readonly clientsQueue: ServicebusQueue;
    public readonly cloudQueueName: string;
    public readonly clientsQueueName: string;
    public readonly errorQueueName: string;
    /** Client queues, topic subscriptions and the DataBus inbox. */
    public readonly storageAccount: StorageAccount;
    /** Where the cloud stages outgoing DataBus attachments (`bote-outbox`, watched by the worker). */
    public readonly dataBusOutboxStorageAccount: StorageAccount;
    public readonly signalrService: SignalrService;

    constructor(scope: Construct, environment: Environnment, props: DboteGatewayProps) {
        super(scope, environment.formatName('cdktf', 'gateway'));

        const worker = new DboteWorker(this, environment, {
            subscriptionId: props.subscriptionId,
            appConfigurationEndpoint: props.appConfigurationEndpoint,
            appInsightsConnection: props.appInsightsConnection,
            registry: props.registry,
            imageRepository: props.workerImageRepository,
            imageTag: props.workerImageTag,
            minReplicas: props.workerMinReplicas,
            maxReplicas: props.workerMaxReplicas,
        });
        this.worker = worker;

        // Service Bus
        const serviceBusNamespaceName = environment.formatName('sbns');
        const serviceBusNamespace = new ServicebusNamespace(this, serviceBusNamespaceName, {
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            name: serviceBusNamespaceName,
            // Standard, not Basic: the worker reschedules not-ready attachments with
            // ScheduleMessageAsync (scheduled/deferred messages), which the Basic SKU does not support.
            sku: 'Standard',
        });
        this.serviceBusNamespace = serviceBusNamespace;

        // This is not optimal but there is no way to extract the hostname from
        // serviceBusNamespace.endpoint at the moment (as it is a CDKTF token
        // and there is no support for URL parsing at the moment).
        this.fullyQualifiedNamespace = `${serviceBusNamespace.name}.servicebus.windows.net`;

        // Duplicate detection: the worker sends with the client's message id, so a client's resend
        // after an unanswered send is dropped here. It cannot be switched on after creation.
        const cloudQueue = new ServicebusQueue(this, environment.formatName('sbq', 'cloud'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'cloud'),
            requiresDuplicateDetection: true,
            duplicateDetectionHistoryTimeWindow: 'PT10M',
        });

        // The worker's CloudIncomingMonitor trigger reads from this queue (attachment reschedule path).
        const cloudIncomingQueue = new ServicebusQueue(this, environment.formatName('sbq', 'cloud-incoming'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'cloud-incoming'),
        });

        const clientsQueue = new ServicebusQueue(this, environment.formatName('sbq', 'clients'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'clients'),
        });

        // Not used by the worker: the error queue of the cloud services built on this gateway.
        const errorQueue = new ServicebusQueue(this, environment.formatName('sbq', 'error'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'error'),
        });

        this.cloudQueue = cloudQueue;
        this.clientsQueue = clientsQueue;
        this.cloudQueueName = cloudQueue.name;
        this.clientsQueueName = clientsQueue.name;
        this.errorQueueName = errorQueue.name;

        // SignalR
        const signalrServiceName = environment.formatName('signalr', 'dbote');
        const signalrService = new SignalrService(this, signalrServiceName, {
            name: signalrServiceName,
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            sku: {
                name: 'Standard_S1',
                capacity: 1,
            },
            identity: {
                type: 'SystemAssigned',
            },
            serviceMode: 'Serverless',
            upstreamEndpoint: [
                {
                    urlTemplate: worker.signalREndpoint,
                    eventPattern: ['*'],
                    hubPattern: ['*'],
                    categoryPattern: ['*']
                }
            ]
        });
        this.signalrService = signalrService;

        // SignalR resolves the Key Vault reference in the upstream URL with its own identity.
        new RoleAssignment(this, environment.formatName('role', 'signalr-func-kv'), {
            scope: worker.keyVaultId,
            roleDefinitionName: 'Key Vault Secrets User',
            principalId: signalrService.identity.principalId,
        });

        // Storage
        const storageAccount = new StorageAccount(this, environment.formatSafeName('st'), {
            name: environment.formatSafeName('st'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            accountTier: "Standard",
            accountReplicationType: "LRS",
        });
        this.storageAccount = storageAccount;
        // The worker's CloudOutboxBlobDetected trigger watches bote-outbox in this account.
        this.dataBusOutboxStorageAccount = storageAccount;

        // DataBus staging containers. The worker's outbox blob trigger needs its container up
        // front, or the trigger (and on ACA its scaler) fails until the first attachment.
        for (const container of ['bote-outbox', 'bote-inbox']) {
            new StorageContainer(this, environment.formatName('stc', container), {
                name: container,
                storageAccountId: storageAccount.id,
            });
        }

        // Topic subscriptions of the clients
        new StorageTable(this, environment.formatName('stt', 'subscriptions'), {
            storageAccountName: storageAccount.name,
            name: 'subscriptions',
        });

        // DataBus attachment lifecycle. Attachments are staged as blobs and copied
        // between the outbox/inbox/client-datastore containers. Success paths delete their
        // source blob eagerly, but the remaining orphan classes cannot be deleted inline:
        //   - failed/partial copies (a retried message must still find its attachment), and
        //   - cloud->client broadcast blobs stored per-tenant at {tenantId}/{attachmentId}
        //     and shared by every subscriber, so no single client may delete them.
        // A bounded TTL is the retry-safe backstop that reaps every orphan. Only blob
        // containers live in this account (queues + the subscriptions table are unaffected).
        new StorageManagementPolicy(this, environment.formatName('stmp', 'databus-ttl'), {
            storageAccountId: storageAccount.id,
            rule: [{
                name: 'databus-attachment-ttl',
                enabled: true,
                filters: {
                    blobTypes: ['blockBlob'],
                },
                actions: {
                    baseBlob: {
                        deleteAfterDaysSinceModificationGreaterThan: 7,
                    },
                },
            }],
        });

        // role assignments - worker
        new RoleAssignment(this, environment.formatName('role', 'func-sbns'), {
            scope: serviceBusNamespace.id,
            roleDefinitionName: 'Azure Service Bus Data Owner',
            principalId: worker.functionPrincipalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'func-appcs'), {
            scope: props.appConfigurationId,
            roleDefinitionName: 'App Configuration Data Reader',
            principalId: worker.functionPrincipalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'func-signalr'), {
            scope: signalrService.id,
            roleDefinitionName: 'SignalR Service Owner',
            principalId: worker.functionPrincipalId,
        });

        // Covers all queues and tables of the account, the subscriptions table included.
        new RoleAssignment(this, environment.formatName('role', 'func-stq'), {
            scope: storageAccount.id,
            roleDefinitionName: 'Storage Queue Data Contributor',
            principalId: worker.functionPrincipalId,
        });

        new RoleAssignment(this, environment.formatName('role', 'func-stt'), {
            scope: storageAccount.id,
            roleDefinitionName: 'Storage Table Data Contributor',
            principalId: worker.functionPrincipalId,
        });

        // DataBus containers and the outbox blob trigger (which needs owner with an identity-based
        // connection). The worker uses the connection string below today; this is for the switch.
        new RoleAssignment(this, environment.formatName('role', 'func-stb'), {
            scope: storageAccount.id,
            roleDefinitionName: 'Storage Blob Data Owner',
            principalId: worker.functionPrincipalId,
        });

        // The SignalR and Azure Service Bus configuration cannot be placed in the App
        // Configuration Service as the Azure Function host parses them to set up the triggers.
        // TODO use managed identity for table and queue storage
        worker.addAppSetting('dbote__Worker__Storage__Connection', storageAccount.primaryConnectionString);
        worker.addAppSetting('dbote__Worker__ServiceBus__Connection__fullyQualifiedNamespace', this.fullyQualifiedNamespace);
        worker.addAppSetting('dbote__Worker__ServiceBus__Connection__credential', 'managedidentity');
        worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Cloud', cloudQueue.name);
        worker.addAppSetting('dbote__Worker__ServiceBus__Queues__CloudIncoming', cloudIncomingQueue.name);
        worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Clients', clientsQueue.name);
        worker.addAppSetting('dbote__Worker__SignalR__Connection__serviceUri', `https://${signalrServiceName}.service.signalr.net`);
        worker.addAppSetting('dbote__Worker__SignalR__Connection__credential', 'managedidentity');
        worker.addAppSetting('dbote__Worker__OpenId__Authority', props.openId.authority);
        worker.addAppSetting('dbote__Worker__OpenId__JwksUri', props.openId.jwksUri);
        worker.addAppSetting('dbote__Worker__OpenId__Audience', props.openId.audience);
        worker.addAppSetting('dbote__Worker__OpenId__RequiredScope', props.openId.requiredScope);
    }
}
