import { Construct } from "constructs";
import { ServicebusQueue } from "@cdktf/provider-azurerm/lib/servicebus-queue";
import { AppConfigurationKey } from "@cdktf/provider-azurerm/lib/app-configuration-key";
import { Environnment } from "../core";
import { DboteGateway } from "../core";

export interface DboteSampleAppProps {
    readonly gateway: DboteGateway;
    readonly appConfigurationId: string;
    readonly appInsightsConnection: string;
}

/**
 * The application layer of the reference deployment: the samples' own queues and the
 * `dbote:Cloud:*` / `dbote:Service:*` settings their cloud services read from App Configuration.
 * A real application replaces this construct with its own; the gateway stays as is.
 *
 * Writing App Configuration keys needs "App Configuration Data Owner" on the store for the
 * identity running terraform.
 */
export class DboteSampleApp extends Construct {

    constructor(scope: Construct, environment: Environnment, props: DboteSampleAppProps) {
        super(scope, environment.formatName('cdktf', 'sample-app'));

        const gateway = props.gateway;
        const serviceBusNamespace = gateway.serviceBusNamespace;

        // Queues of the benchmark service/runner and its events.
        new ServicebusQueue(this, environment.formatName('sbq', 'runner'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'runner'),
        });

        const serviceQueue = new ServicebusQueue(this, environment.formatName('sbq', 'service'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'service'),
        });

        const eventsQueue = new ServicebusQueue(this, environment.formatName('sbq', 'events'), {
            namespaceId: serviceBusNamespace.id,
            name: environment.formatName('sbq', 'events'),
        });

        const key = (id: string, key: string, value: string) =>
            new AppConfigurationKey(this, environment.formatName('appcsk', id), {
                configurationStoreId: props.appConfigurationId,
                key,
                value,
                label: environment.environment,
            });

        // app config - benchmark cloud
        key('app-cloud-servicebus-namespace', 'dbote:Cloud:ServiceBus:Connection:fullyQualifiedNamespace', gateway.fullyQualifiedNamespace);
        key('app-cloud-servicebus-credentialtype', 'dbote:Cloud:ServiceBus:Connection:credential', 'managedidentity');
        key('app-cloud-servicebus-queue-cloud', 'dbote:Cloud:ServiceBus:Queues:Cloud', gateway.cloudQueueName);
        key('app-cloud-servicebus-queue-clients', 'dbote:Cloud:ServiceBus:Queues:Clients', gateway.clientsQueueName);
        key('app-cloud-servicebus-queue-error', 'dbote:Cloud:ServiceBus:Queues:Error', gateway.errorQueueName);
        key('app-cloud-servicebus-queue-service', 'dbote:Cloud:ServiceBus:Queues:Service', serviceQueue.name);
        key('app-cloud-servicebus-queue-events', 'dbote:Cloud:ServiceBus:Queues:Events', eventsQueue.name);

        // app config - benchmark service
        key('app-service-servicebus-namespace', 'dbote:Service:ServiceBus:Connection:fullyQualifiedNamespace', gateway.fullyQualifiedNamespace);
        key('app-service-servicebus-credentialtype', 'dbote:Service:ServiceBus:Connection:credential', 'managedidentity');
        key('app-service-servicebus-queue-cloud', 'dbote:Service:ServiceBus:Queues:Cloud', gateway.cloudQueueName);
        key('app-service-servicebus-queue-error', 'dbote:Service:ServiceBus:Queues:Error', gateway.errorQueueName);
        key('app-service-servicebus-queue-service', 'dbote:Service:ServiceBus:Queues:Service', serviceQueue.name);

        // The benchmark load generator and the load-test controller/dashboard are not deployed at
        // the moment (the benchmark needs the acrbotetest registry of the old subscription). To
        // bring them back, create them here and grant their identities access, e.g.:
        /*
        const benchmark = new DboteBenchmark(this, environment, appConfigurationEndpoint, props.appInsightsConnection);
        new RoleAssignment(this, environment.formatName('role', 'app-cloud-sbns'), {
            scope: serviceBusNamespace.id,
            roleDefinitionName: 'Azure Service Bus Data Owner',
            principalId: benchmark.cloudPrincipalId,
        });
        new RoleAssignment(this, environment.formatName('role', 'app-cloud-appcs'), {
            scope: props.appConfigurationId,
            roleDefinitionName: 'App Configuration Data Reader',
            principalId: benchmark.cloudPrincipalId,
        });
        // ... and the same for benchmark.servicePrincipalId ('app-service-sbns' / 'app-service-appcs').

        const ltDashboard = new DboteLoadTestDashboard(this, environment, {
            serviceBusConnectionString: serviceBusNamespace.defaultPrimaryConnectionString,
            storageConnectionString: gateway.storageAccount.primaryConnectionString,
            cloudQueue: gateway.cloudQueueName,
            clientsQueue: gateway.clientsQueueName,
            appInsightsConnection: props.appInsightsConnection,
        });
        new DboteLoadTestController(this, environment, {
            serviceBusConnectionString: serviceBusNamespace.defaultPrimaryConnectionString,
            serviceBusNamespaceName: serviceBusNamespace.name,
            storageConnectionString: gateway.storageAccount.primaryConnectionString,
            cloudQueue: gateway.cloudQueueName,
            clientsQueue: gateway.clientsQueueName,
            errorQueue: gateway.errorQueueName,
            appInsightsConnection: props.appInsightsConnection,
            consolidatorUrl: ltDashboard.url,
        });
        */
    }
}
