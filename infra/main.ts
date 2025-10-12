import { Construct } from "constructs";
import { App, AzurermBackend, TerraformStack, Token } from "cdktf";
import { AzurermProvider } from "@cdktf/provider-azurerm/lib/provider";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { ServicebusNamespace } from "@cdktf/provider-azurerm/lib/servicebus-namespace";
import { SignalrService, SignalrServiceUpstreamEndpointList } from "@cdktf/provider-azurerm/lib/signalr-service";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { ServicebusQueue } from "@cdktf/provider-azurerm/lib/servicebus-queue";
import { AppConfiguration } from "@cdktf/provider-azurerm/lib/app-configuration";
import { AppConfigurationKey } from "@cdktf/provider-azurerm/lib/app-configuration-key";
import { DboteInsights } from "./constructs/dbote-insights";
import { Environnment } from "./environment";
import { DboteWorker } from "./constructs/dbote-worker";
import { StorageTable } from "@cdktf/provider-azurerm/lib/storage-table";
import { DboteBenchmark } from "./constructs/dbote-benchmark";
import { DboteIdentityProvider } from "./constructs/dbote-identity-provider";

const location = "westeurope"; // Define the location for resources
const resourceGroupName = "rg-dbote-test"; // Define the resource group name
const tenantId = 'cb0bb315-f38b-4ab4-ad2d-d3ed25d23b53';
const subscriptionId = '48b9d140-b453-4053-9fe1-a46509808c7f';

function formatName(type: string, name: string, env: string, sanitize: boolean = false): string {
  const formattedName = `${type}-${name}-${env}`;
  return sanitize ? sanitizeName(formattedName) : formattedName;
}

function sanitizeName(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]/g, '');
}


class DboteStack extends TerraformStack {
  constructor(scope: Construct, id: string) {
    super(scope, id);

    new AzurermProvider(this, "azurerm", {
      tenantId,
      subscriptionId,
      resourceProviderRegistrations: 'none',
      features: [
        {
          appConfiguration: [{ purgeSoftDeleteOnDestroy: false, recoverSoftDeleted: true }],
        },
      ],
    });

    const environment = new Environnment(tenantId, location, resourceGroupName, 'cmdev');

    const insights = new DboteInsights(this, environment);

    const appConfiguration = new AppConfiguration(this, environment.formatName('appcs'), {
      name: environment.formatName('appcs'),
      location: location,
      resourceGroupName: resourceGroupName,
      sku: 'developer',
    });

    // Deploy BasicIdentityProvider (with in-memory connector storage)
    const identityProvider = new DboteIdentityProvider(this, environment, insights.connection);

    const worker = new DboteWorker(this, environment, appConfiguration.endpoint, insights.connection);

    // Audience is just a logical identifier that must match between issuer and validator
    const audience = 'http://dbote-worker/api';

    identityProvider.addAppSetting('BasicIdentityProvider__Audience', audience);

    const benchmark = new DboteBenchmark(this, environment, appConfiguration.endpoint, insights.connection);

    // Service Bus
    const serviceBusNamespaceName = formatName('sbns', 'dbote', 'cmdev', false);
    const serviceBusNamespace = new ServicebusNamespace(this, serviceBusNamespaceName, {
      location,
      resourceGroupName,
      name: serviceBusNamespaceName,
      sku: 'Basic',
    });
    
    // This is not optimal but there is no way to extract the hostname from
    // serviceBusNamespace.endpoint at the moment (as it is a CDKTF token
    // and there is no support for URL parsing at the moment).
    const fullyQualifiedNamespace = `${serviceBusNamespace.name}.servicebus.windows.net`

    const cloudQueue = new ServicebusQueue(this, environment.formatName('sbq', 'cloud'), {
      namespaceId: serviceBusNamespace.id,
      name: environment.formatName('sbq', 'cloud'),
    });

    const connectorsQueue = new ServicebusQueue(this, environment.formatName('sbq', 'connectors'), {
      namespaceId: serviceBusNamespace.id,
      name: environment.formatName('sbq', 'connectors'),
    });

    const errorQueue = new ServicebusQueue(this, environment.formatName('sbq', 'error'), {
      namespaceId: serviceBusNamespace.id,
      name: environment.formatName('sbq', 'error'),
    });

    const runnerQueue = new ServicebusQueue(this, environment.formatName('sbq', 'runner'), {
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

    // SignalR
    const signalrServiceName = environment.formatName('signalr', 'dbote');
    const signalrService = new SignalrService(this, signalrServiceName, {
      name: signalrServiceName,
      location,
      resourceGroupName,
      sku: {
        name: 'Standard_S1',
        capacity: 1,
      },
      identity:{
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

    new RoleAssignment(this, environment.formatName('role', 'signalr-func-kv'), {
      scope: worker.keyVaultId,
      roleDefinitionName: 'Key Vault Secrets User',
      principalId: signalrService.identity.principalId,
    })

    // Storage
    const storageAccount = new StorageAccount(this, environment.formatSafeName('st'), {
      name: environment.formatSafeName('st'),
      location: environment.location,
      resourceGroupName: environment.resourceGroup,
      accountTier: "Standard",
      accountReplicationType: "LRS",
    });

    // Create subscriptions table for BoteWorker topic subscriptions
    new StorageTable(this, environment.formatName('stt', 'subscriptions'), {
      storageAccountName: storageAccount.name,
      name: 'subscriptions',
    });

    // role assignments - worker
    new RoleAssignment(this, environment.formatName('role', 'func-sbns'), {
      scope: serviceBusNamespace.id,
      roleDefinitionName: 'Azure Service Bus Data Owner',
      principalId: worker.functionPrincipalId,
    });

    new RoleAssignment(this, environment.formatName('role', 'func-appcs'), {
      scope: appConfiguration.id,
      roleDefinitionName: 'App Configuration Data Reader',
      principalId: worker.functionPrincipalId,
    });

    new RoleAssignment(this, environment.formatName('role', 'func-signalr'), {
      scope: signalrService.id,
      roleDefinitionName: 'SignalR Service Owner',
      principalId: worker.functionPrincipalId,
    });

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

    // Note: The above role assignment covers access to all tables in the storage account,
    // including the subscriptions table created above

    // app config - worker

    // The SignalR and Azure Service Bus configuration cannot be placed
    // in the App Configuration Service as the Azure Function host parses
    // them to setup the triggers.
    // TODO use managed identity for table and queue storage
    worker.addAppSetting('dbote__Worker__Storage__Connection', storageAccount.primaryConnectionString);
    worker.addAppSetting('dbote__Worker__ServiceBus__Connection__fullyQualifiedNamespace', fullyQualifiedNamespace);
    worker.addAppSetting('dbote__Worker__ServiceBus__Connection__credential', 'managedidentity');
    worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Cloud', cloudQueue.name);
    worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Connectors', connectorsQueue.name);
    worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Error', errorQueue.name);
    worker.addAppSetting('dbote__Worker__ServiceBus__Queues__Events', eventsQueue.name);
    worker.addAppSetting('dbote__Worker__SignalR__Connection__serviceUri', `https://${signalrServiceName}.service.signalr.net`);
    worker.addAppSetting('dbote__Worker__SignalR__Connection__credential', 'managedidentity');
    worker.addAppSetting('dbote__Worker__OpenId__Authority', identityProvider.endpoint);
    worker.addAppSetting('dbote__Worker__OpenId__JwksUri', `${identityProvider.endpoint}/.well-known/jwks.json`);
    worker.addAppSetting('dbote__Worker__OpenId__Audience', audience);
    worker.addAppSetting('dbote__Worker__OpenId__RequiredScope', 'bote');

/*
    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-storage-connection'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:Storage:Connection',
      value: storageAccount.primaryConnectionString,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-namespace'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:ServiceBus:Connection:fullyQualifiedNamespace',
      value: fullyQualifiedNamespace,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-credentialtype'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:ServiceBus:Connection:credential',
      value: 'managedidentity',
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-cloud'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:ServiceBus:Queues:Cloud',
      value: cloudQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-connectors'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:ServiceBus:Queues:Connectors',
      value: connectorsQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-error'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:ServiceBus:Queues:Error',
      value: errorQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-signalr-serviceuri'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:SignalR:Connection:serviceUri',
      value: `https://${signalrService.hostname}`,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-signalr-credentialtype'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:SignalR:Connection:credential',
      value: 'managedidentity',
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-openid-authority'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Worker:OpenId:Authority',
      value: worker.dboteEndpoint,
      label: environment.environment,
    });
*/

    // role assignments - benchmark cloud
    new RoleAssignment(this, environment.formatName('role', 'app-cloud-sbns'), {
      scope: serviceBusNamespace.id,
      roleDefinitionName: 'Azure Service Bus Data Owner',
      principalId: benchmark.cloudPrincipalId,
    });

    new RoleAssignment(this, environment.formatName('role', 'app-cloud-appcs'), {
      scope: appConfiguration.id,
      roleDefinitionName: 'App Configuration Data Reader',
      principalId: benchmark.cloudPrincipalId,
    });


    // app config - benchmark cloud
    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-namespace'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Connection:fullyQualifiedNamespace',
      value: fullyQualifiedNamespace,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-credentialtype'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Connection:credential',
      value: 'managedidentity',
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-queue-cloud'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Queues:Cloud',
      value: cloudQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-queue-connectors'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Queues:Connectors',
      value: connectorsQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-queue-error'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Queues:Error',
      value: errorQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-queue-service'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Queues:Service',
      value: serviceQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-cloud-servicebus-queue-events'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Cloud:ServiceBus:Queues:Events',
      value: eventsQueue.name,
      label: environment.environment,
    });

    // role assignments - benchmark service
    new RoleAssignment(this, environment.formatName('role', 'app-service-sbns'), {
      scope: serviceBusNamespace.id,
      roleDefinitionName: 'Azure Service Bus Data Owner',
      principalId: benchmark.servicePrincipalId,
    });

    new RoleAssignment(this, environment.formatName('role', 'app-service-appcs'), {
      scope: appConfiguration.id,
      roleDefinitionName: 'App Configuration Data Reader',
      principalId: benchmark.servicePrincipalId,
    });

    // app config - benchmark service
    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-service-servicebus-namespace'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Service:ServiceBus:Connection:fullyQualifiedNamespace',
      value: fullyQualifiedNamespace,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-service-servicebus-credentialtype'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Service:ServiceBus:Connection:credential',
      value: 'managedidentity',
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-service-servicebus-queue-cloud'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Service:ServiceBus:Queues:Cloud',
      value: cloudQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-service-servicebus-queue-error'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Service:ServiceBus:Queues:Error',
      value: errorQueue.name,
      label: environment.environment,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'app-service-servicebus-queue-service'), {
      configurationStoreId: appConfiguration.id,
      key: 'dbote:Service:ServiceBus:Queues:Service',
      value: serviceQueue.name,
      label: environment.environment,
    });
  }
}

const app = new App();
const stack = new DboteStack(app, "infra");

new AzurermBackend(stack, {
  resourceGroupName: 'rg-dbote-test',
  storageAccountName: 'stdbotestfstate',
  containerName: 'tfstate',
  key: 'dbote.tfstate',
});
app.synth();
