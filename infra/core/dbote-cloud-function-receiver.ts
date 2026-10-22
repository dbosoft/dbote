import { Construct } from "constructs";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { Environnment } from "./environment";
import { DboteGateway } from "./dbote-gateway";

/**
 * The settings a cloud receiver needs to talk to the gateway. Names follow the Functions
 * identity-based connection convention (`<Connection>__fullyQualifiedNamespace`).
 */
export interface DboteCloudReceiverSettings {
    readonly fullyQualifiedNamespace: string;
    /** The receiver's `[ServiceBusTrigger]` queue: messages from the clients. */
    readonly cloudQueue: string;
    /** Where the receiver sends messages for the clients. */
    readonly clientsQueue: string;
    /** Blob endpoint of the account holding the DataBus outbox (`bote-outbox`). */
    readonly dataBusOutboxBlobEndpoint: string;
}

/**
 * Lets an application's Azure Functions backend (or any other identity-based cloud service that is
 * not a Rebus endpoint) act as the cloud side of the gateway: receive from the cloud queue, send to
 * the clients queue, and stage outgoing DataBus attachments in the outbox. Incoming attachments are
 * read through the SAS the worker puts on each message, so the inbox needs no role.
 *
 * Grants are scoped to the two queues and the outbox container, not the namespace or account. The application owns its compute and
 * passes the principal; the gateway stays unaware of which applications use it.
 */
export class DboteCloudFunctionReceiver extends Construct {

    public readonly settings: DboteCloudReceiverSettings;

    constructor(scope: Construct, environment: Environnment, name: string, gateway: DboteGateway, principalId: string) {
        super(scope, environment.formatName('cdktf', `receiver-${name}`));

        // Owner, not only Receiver: the Functions Service Bus trigger with an identity also reads the
        // queue's runtime properties (message count) to scale.
        new RoleAssignment(this, environment.formatName('role', `${name}-sbq-cloud`), {
            scope: gateway.cloudQueue.id,
            roleDefinitionName: 'Azure Service Bus Data Owner',
            principalId,
        });

        new RoleAssignment(this, environment.formatName('role', `${name}-sbq-clients`), {
            scope: gateway.clientsQueue.id,
            roleDefinitionName: 'Azure Service Bus Data Sender',
            principalId,
        });

        new RoleAssignment(this, environment.formatName('role', `${name}-st-outbox`), {
            scope: `${gateway.dataBusOutboxStorageAccount.id}/blobServices/default/containers/bote-outbox`,
            roleDefinitionName: 'Storage Blob Data Contributor',
            principalId,
        });

        this.settings = {
            fullyQualifiedNamespace: gateway.fullyQualifiedNamespace,
            cloudQueue: gateway.cloudQueueName,
            clientsQueue: gateway.clientsQueueName,
            dataBusOutboxBlobEndpoint: gateway.dataBusOutboxStorageAccount.primaryBlobEndpoint,
        };
    }
}
