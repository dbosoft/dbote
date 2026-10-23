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
| `Connection` | yes | Connection string of the gateway storage: the clients' queues, the topic subscriptions table and the attachment containers. The cloud side stages outgoing attachments here as well. It has to be a connection string with the account key: the worker signs the SAS URIs it hands to clients with it, and Storage Queues have no SAS a managed identity could sign. |
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
| `RequiredScope` | yes | A scope every token must carry (scopes are a space-separated set); without it the worker refuses every client. |
| `PeerScope` | no | A client whose token carries this scope too may send to the clients of its tenant its token lists in `bote_peers`, e.g. `bote.peer`. Unset, clients only talk to the cloud. |

The worker takes the client from `sub` and the tenant from `tid`. It accepts ES256 tokens only,
reloads the keys when a token names an unknown key (at most every 30 seconds) and keeps using the
known keys while the issuer is unreachable. Tenant and client ids must not contain `/`, `\` or
control characters, nor be `.` or `..`. `BasicIdentityProvider` is for development; an application
brings its own issuer, which has to meet [identity-provider.md](identity-provider.md).

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
| `{Prefix}-ds` | `{tenant}/{attachment}` | attachments sent to clients by the cloud or another client; any client of the tenant may read one whose id it knows |
| `{Prefix}-ds` | `{tenant}/upload/{key}/{attachment}` | a client's uploads |
| `bote-inbox` | `{tenant}/{key}/{attachment}` | a client's attachments, copied for the cloud |
| `bote-outbox` | `{tenant}/{attachment}` | what the cloud side stages for its clients |

A client writes only below its own key, so it cannot overwrite another client's attachments, and
names what it uploads with GUIDs (as Rebus does), so nobody can guess an id or take it first. An
attachment one client sends to another is copied from the sender's inbox copy to `{tenant}/{attachment}`,
never over an attachment that is already there.
Attachment ids are checked like tenant ids before they become part of a path.

## Roles for a managed identity

What the reference deployment grants the worker's identity. It is a user-assigned identity, whose
id is known before the app exists and stays the same when the app changes, so its role
assignments are never replaced. With one, set `AZURE_CLIENT_ID`,
`AzureWebJobsSecretStorageKeyVaultClientId` and the `clientId` of each identity-based connection to
its client id.

| Resource | Role |
| --- | --- |
| Service Bus namespace | Azure Service Bus Data Owner |
| gateway storage | Storage Queue Data Contributor, Storage Table Data Contributor, Storage Blob Data Owner (not used while the worker has the account key, see above) |
| Functions host storage | Storage Blob, Queue and Table Data Contributor |
| the worker's key vault | Key Vault Secrets Officer |
| SignalR | SignalR Service Owner |
| App Configuration | App Configuration Data Reader |
| container registry | AcrPull (a user-assigned identity) |

SignalR's own identity needs Key Vault Secrets User on the worker's key vault to resolve the
upstream URL. The rights the cloud side needs are in [cloud-without-rebus.md](cloud-without-rebus.md).

## Clients talking to each other

With `OpenId:PeerScope` set, a client whose token carries that scope may send to
`{Queues:Clients}-{otherClientId}` for every client its token lists in the `bote_peers` claim
(`*` for all clients of the tenant). The worker keeps the sender's tenant, so a client can never reach
another tenant, puts the message on the clients queue and delivers it like a message from the cloud,
with the sender in `dbote-sender-client-id`. The receiver answers the same way (a Rebus reply goes to
the sender's queue), so both need the scope, and each the other in its list. Use it for a client that has to ask another one, e.g. an
application instance in the cloud that needs a system on the customer's premises through the agent
there.

- The identity provider decides who may reach whom; a token with the scope but without the list
  reaches no one. The receiver has its own say: `BoteTransportOptions.AcceptPeer` refuses messages
  from other clients before any handler sees them and moves them to the error queue.
- `BoteTransportOptions.Presence` tells whether a client among its peers is connected now.
  The cloud side asks the SignalR service (hub `BoteHub`) whether the user
  `BoteClientNames.SignalRUserId(tenant, client)` exists.
- On a message a client deferred to itself the header is what that client passed on, not
  confirmed by the worker.
- A receiver that never connected is refused at the sender; so is a message too large for a
  storage queue.
- Answers go to the sender's queue whatever return address the sender wrote.
- Messages to clients are not deduplicated: a sender that retries after a timeout may deliver
  twice. Make handlers idempotent on the message id.

## Tracing

The worker keeps a message in its sender's trace. A client's message that carries a W3C
`traceparent` header is sent on to Service Bus within that trace (as `Diagnostic-Id`, which the
Functions trigger on the cloud side continues), and the worker's logs for it belong to it. A message
to a client gets the trace of the cloud's send in `traceparent`, unless it carries one already.

## Metrics

`GET /api/Metrics` (with the host's master key) returns the worker's timings and the count of
messages between clients: sent, and refused because the sender may not reach the receiver or the
receiver never connected. Each one sent is also logged with sender, receiver and message id.

## Limits

| | |
| --- | --- |
| Message to a client | below 64 KiB as stored (about 45 KiB of payload); larger as an attachment. A client's message to a client above that is refused when sent. |
| Message to the cloud | the Service Bus limit (256 KiB on Standard). |
| Scheduling | a client may schedule a message at most 7 days ahead. |
| Time to live | a message's remaining time to live carries over to the client's storage queue; without one it waits 7 days. |
| Attachment ids | at most 128 characters and a single path segment; GUIDs for what a client uploads. |

## Upgrading from earlier versions

- Client queues, error queues, SignalR users and subscription rows are named by the hashed key now.
  Messages still waiting in queues of the old `{prefix}-{tenant}-{client}` form are not delivered
  any more, and subscriptions are made again when clients subscribe. Drain the old queues before
  switching, or accept their loss.
- Attachments: uploads moved below the client's key, so an upload that is under way while the
  worker is replaced has to be repeated.
- The worker refuses to start with a queue prefix that is not lowercase letters, digits and single
  hyphens or is longer than 24 characters, and refuses tokens whose tenant or client id contains
  `/`, `\` or control characters or ends in a dot.
- `ITenantStorageResolver` has a separate `ResolveErrorQueueClient`.
- A client with the peer scope reaches only the clients its token lists in `bote_peers`; tokens
  without the claim reach no other client.
- The reference deployment runs the worker as its user-assigned identity, not a system-assigned
  one. The switch replaces the worker's role assignments once.

## Setting up a new application

1. Create the Service Bus namespace with the three queues, the gateway storage with `bote-outbox`
   and `bote-inbox`, a SignalR service in Serverless mode, a key vault, and an identity provider.
2. Deploy the worker image as a container app (`kind=functionapp`) with the host settings and the
   worker settings, directly or through App Configuration; grant its identity the roles above.
3. Point SignalR's upstream at the worker.
4. Give the cloud side send rights on `Queues:Clients` and receive rights on `Queues:Cloud`.
