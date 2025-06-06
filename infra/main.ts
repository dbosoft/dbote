import { Construct } from "constructs";
import { App, AzurermBackend, TerraformStack } from "cdktf";
import { AzurermProvider } from "@cdktf/provider-azurerm/lib/provider";
import { StorageAccount } from "@cdktf/provider-azurerm/lib/storage-account";
import { ServicebusNamespace } from "@cdktf/provider-azurerm/lib/servicebus-namespace";
import { SignalrService } from "@cdktf/provider-azurerm/lib/signalr-service";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { ServicebusQueue } from "@cdktf/provider-azurerm/lib/servicebus-queue";
import { AppConfiguration } from "@cdktf/provider-azurerm/lib/app-configuration";
import { AppConfigurationKey } from "@cdktf/provider-azurerm/lib/app-configuration-key";
import { SuperBusInsights } from "./constructs/superbus-insights";
import { Environnment } from "./environment";
import { SuperBusWorker } from "./constructs/superbus-worker";
import { StorageTable } from "@cdktf/provider-azurerm/lib/storage-table";
import { StorageTableEntity } from "@cdktf/provider-azurerm/lib/storage-table-entity";

const location = "westeurope"; // Define the location for resources
const resourceGroupName = "rg-superbus-test"; // Define the resource group name

function formatName(type: string, name: string, env: string, sanitize: boolean = false): string {
  const formattedName = `${type}-${name}-${env}`;
  return sanitize ? sanitizeName(formattedName) : formattedName;
}

function sanitizeName(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]/g, '');
}


class SuperBusStack extends TerraformStack {
  constructor(scope: Construct, id: string) {
    super(scope, id);

    new AzurermProvider(this, "azurerm", {
      subscriptionId: '48b9d140-b453-4053-9fe1-a46509808c7f',
      resourceProviderRegistrations: 'none',
      features: [
        {
          appConfiguration: [{ purgeSoftDeleteOnDestroy: false, recoverSoftDeleted: true }],
        }
      ],
    });

    const environment = new Environnment(location, resourceGroupName, 'cmdev');

    const insights = new SuperBusInsights(this, environment);

    const appConfiguration = new AppConfiguration(this, environment.formatName('appcs', '3'), {
      name: environment.formatName('appcs', '3'),
      location: location,
      resourceGroupName: resourceGroupName,
      sku: 'free',

    });

    const worker = new SuperBusWorker(this, environment, appConfiguration.endpoint, insights.connection);

    // Service Bus
    const serviceBusNamespaceName = formatName('sbns', 'superbus', 'cmdev', false);
    const serviceBusNamespace = new ServicebusNamespace(this, serviceBusNamespaceName, {
      location,
      resourceGroupName,
      name: serviceBusNamespaceName,
      sku: 'Standard',
    });

    const cloudQueue = new ServicebusQueue(this, environment.formatName('sbq', 'cloud'), {
      namespaceId: serviceBusNamespace.id,
      // TODO fix name after fixing naming scheme in code
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

    // SignalR
    const signalrServiceName = formatName('signalr', 'superbus', 'cmdev', false);
    const signalrService = new SignalrService(this, signalrServiceName, {
      name: signalrServiceName,
      location,
      resourceGroupName,
      sku: {
        name: 'Free_F1',
        capacity: 1,
      },
      serviceMode: 'Serverless',
      upstreamEndpoint: [
        {
          // TODO Use proper authentication instead of function key
          urlTemplate: worker.signalREndpoint,
          eventPattern: ['*'],
          hubPattern: ['*'],
          categoryPattern: ['*']
        }
      ]
    });

    // Storage
    const storageAccount = new StorageAccount(this, environment.formatSafeName('st'), {
      name: environment.formatSafeName('st'),
      location: environment.location,
      resourceGroupName: environment.resourceGroup,
      accountTier: "Standard",
      accountReplicationType: "LRS",
    });

    const table = new StorageTable(this, environment.formatName('stt'), {
      storageAccountName: storageAccount.name,
      name: 'superbus',
    })

    new StorageTableEntity(this, environment.formatName('stte'), {
      storageTableId: table.id,
      partitionKey: 'MY-TENANT',
      rowKey: 'MY_CONNECTOR',
      entity:  {
        'SigningKey': ''
      }
    });

    // role assignments - worker
    new RoleAssignment(this, environment.formatName('role', 'func-sbns'), {
      scope: serviceBusNamespace.id,
      roleDefinitionName: 'Azure Service Bus Data Owner',
      principalId: worker.functionPrincipalId,
    });

    new RoleAssignment(this, environment.formatName('role', 'func-appcs'), {
      scope: appConfiguration.id,
      roleDefinitionName: 'Azure Configuration Data Reader',
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

    // app config - worker
    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-storage-connection'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:Storage:Connection',
      value: storageAccount.primaryConnectionString,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-connection'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:ServiceBus:Connection',
      value: `Endpoint=sb://${serviceBusNamespace.name}.servicebus.windows.net/`,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-cloud'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:ServiceBus:Queues:Cloud',
      value: cloudQueue.name,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-connectors'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:ServiceBus:Queues:Connectors',
      value: connectorsQueue.name,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-servicebus-queue-error'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:ServiceBus:Queues:Error',
      value: errorQueue.name,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-signalr-connection'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:SignalR:Connection',
      // TODO use connection string instead?
      value: `Endpoint=${signalrService.hostname};Version=1.0;`,
    });

    new AppConfigurationKey(this, environment.formatName('appcsk', 'func-openid-authority'), {
      configurationStoreId: appConfiguration.id,
      key: 'SuperBus:Worker:OpenId:Authority',
      value: worker.superBusEndpoint,
    });
  }
}

const app = new App();
const stack = new SuperBusStack(app, "infra");

new AzurermBackend(stack, {
  resourceGroupName: 'rg-superbus-test',
  storageAccountName: 'stsuperbustfstate',
  containerName: 'tfstate',
  key: 'superbus.tfstate',
});
app.synth();
