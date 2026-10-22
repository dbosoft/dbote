# @dbosoft/bote-infra

CDKTF constructs for one bote gateway deployment. One application = one CDKTF stack that uses
these constructs, hands in the shared platform and adds its own application layer.

| Construct | What |
|---|---|
| `DboteGateway` | The gateway: BoteWorker (Functions on Container Apps), Service Bus namespace with the `cloud`, `cloud-incoming`, `clients` and `error` queues, serverless SignalR, storage (client queues, subscriptions, DataBus), the worker's roles and settings |
| `DboteCloudFunctionReceiver` | Grants an application's identity what a cloud side without Rebus needs (e.g. an Azure Functions backend): receive on `cloud`, send on `clients`, write the DataBus outbox; exposes the settings |
| `DboteWorker` | The worker alone; created by `DboteGateway` |
| `DboteInsights` | Log Analytics + Application Insights, if the platform has none |
| `AzapiProvider`, `AzapiResource` | Minimal azapi bindings (no prebuilt package exists; the worker needs `kind=functionapp`) |
| `Environnment` | Location, resource group and naming: `{type}-{appName}[-{suffix}]-{environment}` |

## Inputs from the platform

Not created by the gateway: the container registry holding the worker image (`registry`, AcrPull is
granted to the worker), App Configuration (`appConfigurationId`/`Endpoint`), the Application
Insights connection and the identity provider (`openId`).

## Use

```ts
new AzurermProvider(this, "azurerm", { ... });
new AzapiProvider(this, "azapi");

const environment = new Environnment(tenantId, location, resourceGroup, 'prod', 'myapp');
const gateway = new DboteGateway(this, environment, {
  subscriptionId,
  registry: { id: acr.id, loginServer: acr.loginServer },
  appConfigurationId: appcs.id,
  appConfigurationEndpoint: appcs.endpoint,
  appInsightsConnection,
  openId: { authority, jwksUri, audience, requiredScope: 'bote' },
  workerMinReplicas: 1,
});

// A Functions backend as the cloud side
const receiver = new DboteCloudFunctionReceiver(this, environment, 'backend', gateway, backend.principalId);
// receiver.settings: fullyQualifiedNamespace, cloudQueue, clientsQueue, dataBusOutboxBlobEndpoint
// go into the backend's configuration.
```

A Rebus cloud service instead gets "Azure Service Bus Data Owner" on `gateway.serviceBusNamespace`
(Rebus creates and inspects its queues) and the queue names from the gateway's outputs.

The DataBus outbox and inbox live in the gateway storage (`gateway.dataBusOutboxStorageAccount`,
the same account as `gateway.storageAccount`), where the worker's outbox blob trigger watches
`bote-outbox`. Without Rebus, the backend uses `Dbosoft.Bote.Cloud`; see
`docs/cloud-without-rebus.md`.

## Build

`npm run build` in this folder, with `cdktf`, `constructs` and `@cdktf/provider-azurerm` installed
next to it (peer dependencies).
