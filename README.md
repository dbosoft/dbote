# dbote

dbosoft bote is a multi-tenant messaging gateway between a cloud application on Azure Service Bus
and clients on customer premises. The clients only need HTTPS: they receive through Azure Storage
Queues and are notified through Azure SignalR, so they neither poll empty queues nor need AMQP or a
VPN. A shared worker (Azure Functions) sits in between as the security boundary: it authenticates
the clients, sets their verified tenant and client on every message and keeps the tenants apart.

```
cloud (Rebus or Azure Functions) ──Service Bus──► BoteWorker ──Storage Queue + SignalR──► client (Rebus)
cloud ◄──Service Bus── BoteWorker ◄──SignalR── client
```

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
| `Dbosoft.Bote.Cloud` | Cloud side without Rebus, e.g. Azure Functions ([docs/cloud-without-rebus.md](docs/cloud-without-rebus.md)) |
| `Dbosoft.Bote.Primitives` | Wire format, header policy, queue and attachment naming shared by all of the above |
| `Dbosoft.Bote.Options`, `Dbosoft.Bote.AppConfiguration`, `Dbosoft.Bote.Transport.Abstractions`, … | Shared settings and contracts |

The worker itself is not a package but a container image (`src/workers/src/Dbosoft.Bote.BoteWorker`),
one generic image for every application; its settings are in
[docs/worker-configuration.md](docs/worker-configuration.md).

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

`src/workers/src/Dbosoft.Bote.BasicIdentityProvider` is a minimal reference identity provider for
development and tests: it issues access tokens to clients that authenticate with a client
assertion (a JWT signed with the client's ECDSA P-256 key) and publishes its keys as JWKS. It signs
with a key from configuration (`BasicIdentityProvider:SigningKeyPem`, from Key Vault in Azure) or an
ephemeral one. Registration is open to anyone. Production deployments use their own identity
provider; the worker only needs its issuer, JWKS endpoint, audience and scope.

## Azure

`infra/` deploys a test environment with CDKTF: the gateway as a reusable construct library
(`infra/core`, `@dbosoft/bote-infra`) and the samples on top of it. See [infra/README.md](infra/README.md).

## Design

### Flow of a message

#### Cloud -> Client
1. Cloud: sends to `{clients}-{clientId}` with the tenant header (Rebus), or with `BoteCloudSender`
2. The Rebus name formatter redirects it to the single clients queue `bote-clients`
3. Worker: takes it from `bote-clients` and writes it to the client's Storage Queue `{prefix}-{key}`,
   key = a hash of tenant and client (`BoteClientNames`), then notifies the client through SignalR
4. Client: fetches the message from its Storage Queue

#### Client -> Cloud
1. Client: sends through SignalR
2. Worker: refuses tenant and client headers set by the client, sets them from the verified token
   and forwards the message to the cloud queue

Attachments (DataBus) travel as blobs next to the messages; the worker copies them between the
cloud's and the client's side and hands out short-lived SAS URIs.

### Further reading

- [docs/worker-configuration.md](docs/worker-configuration.md) - worker settings, the resources it expects, client and attachment naming
- [docs/cloud-without-rebus.md](docs/cloud-without-rebus.md) - cloud side without Rebus, wire format
- [docs/databus-concept.md](docs/databus-concept.md) - DataBus concept

## Important Technical Decisions

### Use single ASB queue for all clients
The messages for all clients of all tenants are handled by a single ASB queue.

#### Reasons
- The ASB trigger in Azure functions requires a fixed name. We want to use the ASB trigger
  to ensure that we can make use of the automatic scaling and function activation by Azure.
- Polling dynamically defined ASB queues would require the activation of the function with
  event grid triggers. Event grid triggers are only available in the premium SKU of ASB.

### Client authentication
The clients authenticate to the dbote infrastructure with a client assertion JWT which
is signed with a ECDSA key which is unique to the client.

#### Reasons
- The client might be deployed for extended periods and hence a static API key might not be
  considered secure enough.
- The ECDSA key could protected e.g. by the Windows key store if required
- The ECDSA key could be used to sign the messages themselves in case additional authentication
  is required.

### Use Application Insights SDK instead of Open Telemetry SDK
There are two SDKs to integrate with Azure Monitor: Application Insights and Open Telemetry.
Microsoft recommends to use the new Open Telemetry SDK for ASP.NET Core. For now, we using
the Application Insights SDK.

#### Reasons
- The Open Telemetry SDK for Azure Functions is still in Preview and missing features.
  This means we would need to mix SDKs.
- The Visual Studio integration seemingly no longer works with the Open Telemetry SDK.
  We would need to use a separate dashboard for Open Telemetry (e.g. from .NET Aspire)
  but that would not support the Application Insights SDK in the function app.
