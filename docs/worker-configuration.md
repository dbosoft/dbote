# Configuring the BoteWorker

The BoteWorker is the gateway between an application's cloud side and its clients. It holds no
application logic, so every application runs the same worker image and only configures it: which
Service Bus namespace, storage account, SignalR service and identity provider it works with.

## Deployment model

One worker deployment serves one application (one SaaS product) and all of its tenants. The
application brings its own Service Bus namespace, storage account, Azure SignalR service and
identity provider; the worker settings point at those. Tenants share the deployment and are kept
apart by the worker: every client has queues of its own, and every message carries the tenant and
client the worker took from the client's token.

The reference deployment in `infra/` (`infra/core/dbote-gateway.ts`) creates all of this for one
application and is the easiest way to see a complete configuration.

## How settings are read

Standard .NET configuration: app settings or environment variables of the container app, then,
if configured, Azure App Configuration on top (later wins). In environment variables `:` is written
as `__`, e.g. `dbote__Worker__ServiceBus__Connection`.

## Functions host

| Setting | Required | Meaning |
| --- | --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | yes | `dotnet-isolated` |
| `AzureWebJobsStorage` | yes | Storage of the Functions host. It also holds the worker's `databus-copy-monitor` queue. With a managed identity set `AzureWebJobsStorage__accountName` instead. May be a different account from the gateway storage below. |
| `AzureWebJobsSecretStorageType` | in Azure | `keyvault`. The host keeps its keys there, among them the key SignalR calls the worker with; the default file storage of container apps loses them when the worker scales to zero. |
| `AzureWebJobsSecretStorageKeyVaultUri` | in Azure | The key vault for the above, one per worker. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | recommended | Application Insights. |

## Azure App Configuration (optional)

With one of these set, the worker loads its `dbote:Worker:*` settings from an App Configuration
store, so the container app itself carries only the bootstrap settings.

| Setting | Meaning |
| --- | --- |
| `dbote:AppConfiguration:Endpoint` | Store endpoint, read with the worker's managed identity. |
| `dbote:AppConfiguration:Connection` | Store connection string, instead of `Endpoint` (not both). |
| `dbote:AppConfiguration:Environment` | Label to read, e.g. `dev` or `prod`. |
| `dbote:AppConfiguration:Prefix` | Key prefix to read, e.g. `dbote:Worker`. |

## Worker settings

### Service Bus (`dbote:Worker:ServiceBus`)

| Setting | Required | Meaning |
| --- | --- | --- |
| `Connection` | yes | The namespace, Standard tier or higher (scheduled messages). With a managed identity: `Connection:fullyQualifiedNamespace`. |
| `Queues:Clients` | yes | The one queue the cloud side sends to for all clients. The worker delivers from here to each client's storage queue. |
| `Queues:Cloud` | yes | Where the worker puts what clients send: the cloud side's input queue, a Rebus endpoint or a receiver built on `Dbosoft.Bote.Cloud` ([cloud-without-rebus.md](cloud-without-rebus.md)). Create it with duplicate detection. |
| `Queues:CloudIncoming` | yes | The worker's own queue for client messages whose attachment is still being copied. |

### Storage (`dbote:Worker:Storage`)

| Setting | Required | Meaning |
| --- | --- | --- |
| `Connection` | yes | The gateway storage: the clients' queues, the topic subscriptions table and the attachment containers. The cloud side stages outgoing attachments here as well. With a managed identity: `Connection:blobServiceUri`, `Connection:queueServiceUri` and `Connection:tableServiceUri` (the blob trigger does not read `serviceUri`); this form has not run in Azure yet, the reference deployment uses a connection string. |
| `Prefix` | no, default `bote` | Start of the clients' queue names. Lowercase letters, digits and single hyphens, at most 24 characters; the worker refuses to start otherwise. |

### SignalR (`dbote:Worker:SignalR`)

| Setting | Required | Meaning |
| --- | --- | --- |
| `Connection` | yes | The Azure SignalR service, in Serverless mode. With a managed identity: `Connection:serviceUri`. |

The service needs an upstream to the worker: `https://<worker>/runtime/webhooks/signalr?code=<key>`,
where the key is the host's `signalr_extension` system key. The reference deployment keeps the URL
as a Key Vault reference, which SignalR resolves with its own identity.

### Identity provider (`dbote:Worker:OpenId`)

| Setting | Required | Meaning |
| --- | --- | --- |
| `Authority` | yes | Issuer of the client tokens. |
| `JwksUri` | yes | Where the issuer's signing keys are published. |
| `Audience` | yes | The audience the tokens must name. |
| `RequiredScope` | yes | The scope a token must carry, exactly; without it the worker refuses every client. |

The worker takes the client from `sub` and the tenant from `tid`. It accepts ES256 tokens only,
reloads the keys when a token names an unknown key (at most every 30 seconds) and keeps using the
known keys while the issuer is unreachable. Tenant and client ids must not contain `/`, `\` or
control characters, nor be `.` or `..`. `BasicIdentityProvider` is for development; an application
brings its own issuer.

## Resources the worker expects

| Name | In | Purpose |
| --- | --- | --- |
| `Queues:Clients`, `Queues:Cloud`, `Queues:CloudIncoming` | Service Bus | As above. |
| `bote-outbox`, `bote-inbox` | gateway storage, blob containers | Attachments from the cloud side, and for it. Create them with the account: the worker's blob trigger on `bote-outbox` and its scaler fail without it. |
| `databus-copy-monitor` | `AzureWebJobsStorage`, queue | Copies of attachments still in progress. Create it with the account for the same reason. |
| `subscriptions` | gateway storage, table | Topic subscriptions, created on first use. |
| `{Prefix}-ds` | gateway storage, blob container | The clients' attachments, created on first use. |
| client queues | gateway storage | Created on first use, see below. |

A lifecycle policy that deletes attachment blobs after a few days is expected; the worker deletes
what it has copied, but a message that is retried has to find its attachment again, so nothing
deletes an attachment the moment it is read.

## What the worker keeps per client

Per client the worker keeps a storage queue, an error queue, a SignalR user and its topic
subscriptions. All are named after a key derived from tenant and client (`BoteClientNames` in
`Dbosoft.Bote.Primitives`): the first 128 bits of SHA-256 over the length-prefixed tenant id and
the client id, as 32 hex characters. The ids are never joined directly, so no two clients share a
name, and any ids, GUIDs included, fit the 63 characters of a queue name.

| What | Name |
| --- | --- |
| client queue | `{Prefix}-{key}` |
| error queue | `{Prefix}-{key}-error` |
| SignalR user | `{key}` |
| subscriptions | partition = key of tenant and topic, row = key of the client |

The names are not readable; `BoteClientNames.ClientQueueName(prefix, tenant, client)` gives a
client's queue. Changing how the key is derived would orphan every client's queues and
subscriptions; a test pins it.

Attachments lie in folders of their tenant:

| Container | Path | What |
| --- | --- | --- |
| `{Prefix}-ds` | `{tenant}/{attachment}` | attachments the cloud sent; any client of the tenant may read one whose id it knows |
| `{Prefix}-ds` | `{tenant}/upload/{key}/{attachment}` | a client's uploads |
| `bote-inbox` | `{tenant}/{key}/{attachment}` | a client's attachments, copied for the cloud |
| `bote-outbox` | `{tenant}/{attachment}` | what the cloud side stages for its clients |

A client writes only below its own key, so it cannot overwrite another client's attachments.
Attachment ids are checked like tenant ids before they become part of a path.

## Roles for a managed identity

What the reference deployment grants the worker's identity:

| Resource | Role |
| --- | --- |
| Service Bus namespace | Azure Service Bus Data Owner |
| gateway storage | Storage Queue Data Contributor, Storage Table Data Contributor, Storage Blob Data Owner |
| Functions host storage | Storage Blob, Queue and Table Data Contributor |
| the worker's key vault | Key Vault Secrets Officer |
| SignalR | SignalR Service Owner |
| App Configuration | App Configuration Data Reader |
| container registry | AcrPull (a user-assigned identity) |

SignalR's own identity needs Key Vault Secrets User on the worker's key vault to resolve the
upstream URL. The rights the cloud side needs are in [cloud-without-rebus.md](cloud-without-rebus.md).

## Setting up a new application

1. Create the Service Bus namespace with the three queues, the gateway storage with `bote-outbox`
   and `bote-inbox`, a SignalR service in Serverless mode, a key vault, and an identity provider.
2. Deploy the worker image as a container app (`kind=functionapp`) with the host settings and the
   worker settings, directly or through App Configuration; grant its identity the roles above.
3. Point SignalR's upstream at the worker.
4. Give the cloud side send rights on `Queues:Clients` and receive rights on `Queues:Cloud`.
