import { Construct } from "constructs";
import { Environnment } from "../environment";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { DataAzurermContainerRegistry } from "@cdktf/provider-azurerm/lib/data-azurerm-container-registry";
import { ContainerApp } from "@cdktf/provider-azurerm/lib/container-app";
import { ContainerAppEnvironment } from "@cdktf/provider-azurerm/lib/container-app-environment";
import { UserAssignedIdentity } from "@cdktf/provider-azurerm/lib/user-assigned-identity";

export interface DboteLoadTestControllerConfig {
    serviceBusConnectionString: string;
    serviceBusNamespaceName: string;
    storageConnectionString: string;
    cloudQueue: string;
    clientsQueue: string;
    errorQueue: string;
    appInsightsConnection: string;
    consolidatorUrl: string;
}

/**
 * The load-test Controller as a horizontally scalable Azure Container App. Replicas compete on the
 * shared Service Bus cloud queue; KEDA scales them out on that queue's backlog - the same signal
 * that piled up when a single local instance couldn't keep up.
 */
export class DboteLoadTestController extends Construct {

    constructor(scope: Construct, environment: Environnment, config: DboteLoadTestControllerConfig) {
        super(scope, environment.formatName('cdktf', 'ltctrl'));

        const acr = new DataAzurermContainerRegistry(this, 'acrdbotecmdev', {
            name: 'acrdbotecmdev',
            resourceGroupName: environment.resourceGroup,
        });

        const appEnvironment = new ContainerAppEnvironment(this, environment.formatName('cae'), {
            name: environment.formatName('cae'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            workloadProfile: [{ name: 'Consumption', workloadProfileType: 'Consumption' }],
        });

        const acrIdentity = new UserAssignedIdentity(this, environment.formatName('id', 'ltctrl'), {
            name: environment.formatName('id', 'ltctrl'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
        });

        const acrPull = new RoleAssignment(this, environment.formatName('role', 'ltctrl-acr'), {
            scope: acr.id,
            roleDefinitionName: 'AcrPull',
            principalId: acrIdentity.principalId,
        });

        new ContainerApp(this, environment.formatName('ca', 'ltctrl'), {
            name: environment.formatName('ca', 'ltctrl'),
            resourceGroupName: environment.resourceGroup,
            containerAppEnvironmentId: appEnvironment.id,
            revisionMode: 'Single',
            dependsOn: [acrPull],
            secret: [
                { name: 'sb-connection', value: config.serviceBusConnectionString },
                { name: 'st-connection', value: config.storageConnectionString },
            ],
            // Headless message handler - no ingress; it consumes the Service Bus queue and pushes
            // metrics to the consolidator.
            identity: {
                type: 'SystemAssigned, UserAssigned',
                identityIds: [acrIdentity.id],
            },
            registry: [{ server: acr.loginServer, identity: acrIdentity.id }],
            template: {
                minReplicas: 1,
                // Keep the real ceiling visible (do not cap low). Reaction is dampened via a larger
                // per-replica backlog target and scale-down cooldown instead: KEDA polls every 30s
                // and only adds a replica once the backlog exceeds 50 messages per replica, so brief
                // spikes are ignored. Scale-down waits 120s so it settles without flapping.
                maxReplicas: 10,
                pollingIntervalInSeconds: 30,
                cooldownPeriodInSeconds: 120,
                container: [{
                    name: environment.formatName('ca', 'ltctrl'),
                    image: `${acr.loginServer}/loadtest-controller:latest`,
                    cpu: 0.5,
                    memory: '1Gi',
                    env: [
                        { name: 'ASPNETCORE_URLS', value: 'http://+:8080' },
                        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: config.appInsightsConnection },
                        { name: 'dbote__Cloud__ServiceBus__Connection', secretName: 'sb-connection' },
                        { name: 'dbote__Cloud__Storage__Connection', secretName: 'st-connection' },
                        { name: 'dbote__Cloud__ServiceBus__Queues__Cloud', value: config.cloudQueue },
                        { name: 'dbote__Cloud__ServiceBus__Queues__Clients', value: config.clientsQueue },
                        { name: 'dbote__Cloud__ServiceBus__Queues__Error', value: config.errorQueue },
                        { name: 'LoadTest__ConsolidatorUrl', value: config.consolidatorUrl },
                    ],
                }],
                // Scale out on the shared cloud queue's backlog (KEDA azure-servicebus).
                customScaleRule: [{
                    name: 'cloud-queue-backlog',
                    customRuleType: 'azure-servicebus',
                    metadata: {
                        queueName: config.cloudQueue,
                        namespace: config.serviceBusNamespaceName,
                        messageCount: '50',
                    },
                    authentication: [{ secretName: 'sb-connection', triggerParameter: 'connection' }],
                }],
            },
        });
    }
}
