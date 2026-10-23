# dbote

dbosoft bote is a multi-tenant messaging gateway between a cloud application on Azure Service Bus
and clients on customer premises. The clients only need HTTPS: they receive through Azure Storage
Queues and are notified through Azure SignalR, so they neither poll empty queues nor need AMQP or a
VPN. A shared worker sits in between as the security boundary: it authenticates the clients, sets
their verified tenant and client on every message and keeps the tenants apart.

```
cloud (Rebus or Azure Functions) ──Service Bus──► BoteWorker ──Storage Queue + SignalR──► client (Rebus)
cloud ◄──Service Bus── BoteWorker ◄──SignalR── client
```

## Architecture

### Parts

- **Cloud side** - the application's own services on Azure Service Bus. Either Rebus endpoints with
  `.EnableBote()` (`Dbosoft.Bote.Rebus.Integration`) or any other backend, e.g. Azure Functions,
  with `Dbosoft.Bote.Cloud` ([docs/cloud-without-rebus.md](docs/cloud-without-rebus.md)).
- **BoteWorker** - an Azure Functions app, run as a container on Azure Container Apps. One generic
  image; each application deploys it once for all of its tenants
  ([docs/worker-configuration.md](docs/worker-configuration.md)).
- **Clients** - Rebus endpoints on customer premises with the bote transport
  (`Dbosoft.Bote.Rebus`). They reach Azure over HTTPS only: SignalR to send and to be notified,
  their own Storage Queue to receive.
- **Identity provider** - the application's own OAuth server. A client proves itself with a JWT
  signed by its own ECDSA P-256 key and gets an access token naming its tenant (`tid`) and
  client (`sub`). What it has to do: [docs/identity-provider.md](docs/identity-provider.md).
  `BasicIdentityProvider` in this repository is for development only.

### Queues and storage

| | Where | |
| --- | --- | --- |
| clients queue | Service Bus | everything the cloud sends to clients, all tenants in one queue |
| cloud queue | Service Bus | everything clients send, with tenant and client set by the worker |
| client queue | Storage Queue, one per client | what the worker delivers to that client |
| subscriptions | Table Storage | which clients of a tenant listen to which topic |
| attachments | Blob Storage | large payloads (Rebus DataBus) next to the messages |

One Service Bus queue for all clients keeps the worker on a fixed Service Bus trigger, which scales
by itself; per-client queues on Service Bus would need polling or Event Grid (Premium only).
Storage queues cost next to nothing per client, so a tenant adds no fixed cost: 1000 tenants still
run on one namespace, one worker and one storage account.

### Message flow

- **Cloud → client**: the cloud sends with the tenant and the receiving client (or a topic) in the
  headers to the clients queue. The worker writes the message to the client's Storage Queue (for a
  topic: of every subscribed client of the tenant) and notifies the client through SignalR, which
  then fetches it. A subscription lasts until the client unsubscribes, so a client that was
  offline finds the broadcasts in its queue when it comes back.
- **Client → cloud**: the client sends through SignalR. The worker refuses every tenant, client or
  other `dbote-*` header the client set, sets tenant and client from the client's token and puts
  the message on the cloud queue.
- **Client → client**: a client whose token carries the worker's peer scope may also send to
  the clients of its own tenant its token lists, e.g. an application instance in the cloud asking an agent on
  the customer's premises. The worker names the sender in the message; the tenant stays the
  sender's, so there is no way to address another tenant.
- **Attachments** travel as blobs: the sender uploads, the worker copies to where the receiver may
  read, and hands out short-lived SAS URIs. A client only ever writes below its own key.

### Security model

- Clients never see Service Bus or anything else inside the cloud; they get SAS URIs for their own
  queue and for attachments.
- Tenant and client of a message always come from the worker, from a validated token; a client
  cannot set them, nor any other `dbote-*` header.
- Every name the worker keeps per client (queues, SignalR user, subscriptions) is derived from a
  hash of tenant and client, so no two clients share one, whatever ids the identity provider
  allows.
- Within a tenant, any client may read an attachment delivered to that tenant if it knows its id
  (a GUID it only learns from the message); this is an accepted trade-off for broadcasts.

## Packages

The libraries are published to the dbosoft Public feed
(`https://pkgs.dev.azure.com/dbosoft/public/_packaging/Public/nuget/v3/index.json`): every build of
`main` as `X.Y.Z-ci.N`, a release from a `vX.Y.Z` tag (`azure-pipelines.yml`, versions from
GitVersion).

| Package | For |
| --- | --- |
| `Dbosoft.Bote.Rebus` | Clients: the Rebus transport over Storage Queues and SignalR, DataBus |
| `Dbosoft.Bote.Client.Authentication`, `Dbosoft.Bote.Client.Options` | Clients: signed client assertions, settings |
| `Dbosoft.Bote.Rebus.Integration` | Cloud side on Rebus: `.EnableBote()`, `.EnableBoteDataBus()` |
| `Dbosoft.Bote.Cloud` | Cloud side without Rebus, e.g. Azure Functions |
| `Dbosoft.Bote.Primitives` | Wire format, header policy, queue and attachment naming shared by all of the above |
| `Dbosoft.Bote.Options`, `Dbosoft.Bote.AppConfiguration`, `Dbosoft.Bote.Transport.Abstractions`, … | Shared settings and contracts |

## Using it

Cloud side on Rebus:

```csharp
configure
    .Options(o => o.EnableBote(clientsQueue))   // tenant checks, routes {clients}-{clientId} to the clients queue
    .Transport(t => t.UseAzureServiceBus(connection, cloudQueue))
    .Serialization(s => s.UseSystemTextJson());

// reply to the client that sent the current message
await bus.Send(new Pong(), new Dictionary<string, string>
{
    [BoteHeaders.TenantId] = MessageContext.Current.Headers[BoteHeaders.TenantId],
    [BoteHeaders.ClientId] = MessageContext.Current.Headers[BoteHeaders.ClientId],
});
```

A broadcast names a topic (`BoteHeaders.Topic`) instead of a client; clients subscribe with
`bus.Advanced.Topics.Subscribe("chat")`.

Client:

```csharp
configure
    .Transport(t => t.UseBote(workerEndpoint, $"{clientsQueue}-{clientId}", credentials, httpClientFactory))
    .Serialization(s => s.UseSystemTextJson())
    .DataBus(d => d.UseBoteDataBus())
    .Routing(r => r.TypeBased().Map<Ping>(cloudQueue));
```

A client with the peer scope sends to another client of its tenant by its queue,
`bus.Advanced.Routing.Send($"{clientsQueue}-{otherClientId}", message)`, and finds the sender in
the `dbote-sender-client-id` header. `BoteTransportOptions.AcceptPeer` limits whom a client takes
messages from, `BoteTransportOptions.Presence.IsConnected(otherClientId)` asks whether a peer is
online. A message with an attachment names it in
`dbote-attachment-id`.

## Local development

### Prerequisites
- .NET 10 SDK
- Docker or Podman (with `podman compose`)

### Emulators

```bash
docker compose up -d
```

starts the emulators the samples and the worker use:

- **Azure Service Bus emulator** - 5672 (AMQP), 5300 (health), 80 (management API) and its SQL Server (1433)
- **Azurite** - 10000 (Blob), 10001 (Queue), 10002 (Table); `UseDevelopmentStorage=true`
- **Azure SignalR emulator** - 8888

### Samples

Each sample brings the worker and the BasicIdentityProvider along:

- `samples/simple` - ping/pong, deferred messages and pushes; `./Start-Services.ps1 -Functions`
  runs its cloud side as Azure Functions without Rebus
- `samples/chat` - several clients per tenant, topic broadcasts, tenant isolation, attachments
- `samples/loadtest` - many simulated clients, DataBus in both directions (`./Start-LoadTest.ps1 -Edges 30`)

### Tests

```bash
dotnet test Dbote.sln
```

The integration tests start Azurite with Testcontainers; with Podman see `test/README.md`.

### Basic Identity Provider

`src/workers/src/Dbosoft.Bote.BasicIdentityProvider` issues access tokens to registered clients
(`POST /api/register-client` with tenant, id, public key and optional extra scopes such as the
peer scope) and publishes its keys as JWKS. It signs with a key from configuration
(`BasicIdentityProvider:SigningKeyPem`, from Key Vault in Azure) or an ephemeral one. Registration
is open to anyone: development only.

## Azure

`infra/` deploys a test environment with CDKTF: the gateway as a reusable construct library
(`infra/core`, `@dbosoft/bote-infra`) and the samples on top of it. See [infra/README.md](infra/README.md).

## Further reading

- [docs/worker-configuration.md](docs/worker-configuration.md) - worker settings, the resources it expects, client and attachment naming
- [docs/cloud-without-rebus.md](docs/cloud-without-rebus.md) - cloud side without Rebus, wire format
- [docs/databus-concept.md](docs/databus-concept.md) - DataBus concept

## Technical decisions

- **ECDSA client assertions, not API keys** - clients run for years; a key per client can live in
  the Windows key store and could later sign messages too.
- **Application Insights SDK, not OpenTelemetry** - the OpenTelemetry SDK for Azure Functions is
  still in preview and would mean mixing SDKs.
