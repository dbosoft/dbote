# BoteWorker configuration contract

The **BoteWorker** is a *generic* gateway. It contains no application-specific
logic — application logic lives in the cloud-side Rebus handlers and in the edge
clients, not in the worker. An application therefore adopts the worker **purely
through configuration**: the same worker container image is deployed once per
application, pointed at that application's own infrastructure. No code change and
no rebuild are required to onboard a new application.

This document is the worker's **public configuration surface** — the contract an
adopting application must satisfy.

## Deployment model (who owns what)

Per `CLAUDE.md` → *Deployment Model* → **"Shared Infrastructure (One per SaaS
Product)"**:

- **One application (SaaS product) = one worker deployment**, with its **own**
  Service Bus namespace, Storage account, Azure SignalR service and identity
  provider. Every `dbote:Worker:*` key below points at *that* product's resources.
- **Multi-tenancy happens *inside* one deployment** via per-client queues
  (`{Storage:Prefix}-{key}`, see [Client names](#client-names)), created on demand.
- Nothing is shared across applications, so the worker's **fixed** resource names
  (see [Fixed names](#fixed-names-not-configurable)) never collide between
  applications — each product has its own storage account and namespace.

```
one generic image  ──deploy N times, one per product──►  product A infra (SB / Storage / SignalR / IdP)
                                                          product B infra (…)
                                                          product C infra (…)
```

## Configuration resolution

Configuration is standard .NET configuration, resolved in this order (later wins):

1. **Environment variables / app settings** on the Function/Container app.
2. **Azure App Configuration** overlay — *if* the bootstrap keys in
   [section B](#b-azure-app-configuration-bootstrap-optional) are set. In
   production this is where the whole `dbote:Worker:*` tree lives, keeping the
   image itself free of product config.

Keys are written with `:` separators here. As environment variables use `__`
(double underscore), e.g. `dbote:Worker:ServiceBus:Connection` →
`dbote__Worker__ServiceBus__Connection`.

---

## A. Platform / Functions-host settings

Required by the Azure Functions host itself (not `dbote:*`-namespaced).

| Key | Required | Meaning |
| --- | --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | yes | Must be `dotnet-isolated`. |
| `AzureWebJobsStorage` | yes\* | Functions host storage **and** the `databus-copy-monitor` queue trigger + its Base64 `QueueServiceClient`. \*With managed identity, leave empty and set `AzureWebJobsStorage__accountName` (+ role assignments) instead. May be a **different** account from `dbote:Worker:Storage:Connection`. |
| `AzureWebJobsSecretStorageType` | prod | `keyvault` in production (function keys are not scoped per app — use a dedicated Key Vault). |
| `AzureWebJobsSecretStorageKeyVaultUri` | prod | Key Vault URI for the above. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | recommended | Application Insights telemetry (also settable via the host `siteConfig`). |

> **Gotcha:** any queue used with `[QueueTrigger]` (here `databus-copy-monitor`)
> must be created with the **Base64-encoded** `AzureWebJobsStorage` client, or
> messages silently poison after 5 attempts. This is already wired in `Program.cs`
> (named `AzureWebJobsStorage` client with `MessageEncoding = Base64`).

## B. Azure App Configuration bootstrap (optional)

If set, the worker loads its `dbote:Worker:*` tree from an Azure App
Configuration store at startup (`AddDboteAzureAppConfiguration`,
`src/Dbosoft.Bote.AppConfiguration/ConfigurationBuilderExtensions.cs`). If
**neither** `Endpoint` nor `Connection` is set, this layer is skipped and the
`dbote:Worker:*` keys must be provided directly (section C).

| Key | Required | Meaning |
| --- | --- | --- |
| `dbote:AppConfiguration:Endpoint` | one of | App Configuration endpoint URI (uses managed identity / `DefaultAzureCredential`). |
| `dbote:AppConfiguration:Connection` | one of | App Configuration connection string. **Mutually exclusive** with `Endpoint` — setting both throws. |
| `dbote:AppConfiguration:Environment` | if used | Label to select (the deployment environment, e.g. `dev`/`prod`). |
| `dbote:AppConfiguration:Prefix` | if used | Key prefix to select, e.g. `dbote:Worker`. |

## C. The `dbote:Worker:*` contract (per application)

Every key here points at the adopting application's **own** resources.

### Service Bus (`dbote:Worker:ServiceBus`)

| Key | Required | Meaning |
| --- | --- | --- |
| `…:Connection` | yes | Service Bus namespace connection. **Standard SKU** required (scheduled messages / defer). For managed identity use the `…:Connection:fullyQualifiedNamespace` form instead of a secret. |
| `…:Queues:Clients` | yes | Shared cloud→client queue. Also the source of the `ServiceBusReceivedMessageFunction` trigger. A client's message to its own queue `{Clients}-{clientId}` (how Rebus defers a message) goes through this queue back to the client. |
| `…:Queues:Cloud` | yes | client→cloud target queue: the inbound queue of the cloud side, a Rebus endpoint or a receiver built on `Dbosoft.Bote.Cloud` (see [cloud-without-rebus.md](cloud-without-rebus.md)). |
| `…:Queues:CloudIncoming` | yes | Worker-internal requeue/monitor queue: inbound messages whose DataBus attachment is not yet copied are parked here and re-checked by `CloudIncomingMonitor`. |
| `…:Queues:Error` | no | **Not used by the worker** — this is the cloud service's Rebus error queue. Present in the shared options type for convenience; safe to omit for the worker. |

### Storage (`dbote:Worker:Storage`)

| Key | Required | Meaning |
| --- | --- | --- |
| `…:Connection` | yes | Storage account for the data planes the worker uses: per-tenant **queues**, subscription **table**, and DataBus **blob** containers. Also the connection of the `CloudOutboxBlobDetected` blob trigger on `bote-outbox`, so the cloud side stages outgoing attachments here. With a managed identity, set `…:Connection:blobServiceUri`, `…:Connection:queueServiceUri` and `…:Connection:tableServiceUri` (the blob trigger does not read `serviceUri`) and give the identity Storage Blob Data Owner; not yet run in Azure, the reference infra passes a connection string. |
| `…:Prefix` | no (default `bote`) | Prefix of the client queue names → `{Prefix}-{key}` (+ `-error`). Lowercase letters, digits and single hyphens, at most 24 characters; checked at startup. Use a distinct prefix if several deployments ever share one account. |

### SignalR (`dbote:Worker:SignalR`)

| Key | Required | Meaning |
| --- | --- | --- |
| `…:Connection` | yes | Azure SignalR service connection. **Must be in Serverless mode**, with an upstream endpoint pointing at the worker's `/runtime/webhooks/signalr` endpoint. Managed identity is supported. |

### Identity / OpenID (`dbote:Worker:OpenId`)

Validates the client access tokens (`TokenValidationService`, `BoteHub.negotiate`).

| Key | Required | Meaning |
| --- | --- | --- |
| `…:Authority` | yes | Token authority / issuer base URL of the application's identity provider. |
| `…:JwksUri` | yes | JWKS endpoint used to validate token signatures. |
| `…:Audience` | yes | Expected `aud` of client tokens. |
| `…:RequiredScope` | yes | Scope every client token must carry (checked on negotiate). |

> Token claims consumed: `sub` → client id, `tid` → tenant id, `scope`. The
> included `BasicIdentityProvider` is **dev-only**; a production application
> supplies its own OIDC provider by setting the four keys above.

---

## Fixed names (not configurable)

Safe to hardcode because of the one-deployment-per-application isolation model:

| Name | Where | Purpose |
| --- | --- | --- |
| `databus-copy-monitor` | `AzureWebJobsStorage` queue | Blob-copy monitor queue trigger (Base64). |
| `bote-outbox` / `bote-inbox` | `Storage:Connection` blob containers | DataBus claim-check staging (`BoteStorageConstants`). Create them with the account: the outbox blob trigger needs its container from the start. |
| `{Storage:Prefix}-{key}` (+ `-error`) | `Storage:Connection` queues | Per-client queues, created on demand. |

## Client names

Everything the worker keeps per client is named after a key, not after the ids joined together
(`BoteClientNames` in `Dbosoft.Bote.Primitives`):

- **key** = the first 128 bits of SHA-256 over the tenant id (length-prefixed) and the client id,
  as 32 lowercase hex characters. Joining the ids with `-` let `a-b`/`c` and `a`/`b-c` share a
  queue, and a client named `c-error` read the error queue of `c`; a hash also fits any ids
  (GUIDs included) into the 63 characters of a queue name.
- client queue `{Prefix}-{key}`, its error queue `{Prefix}-{key}-error`, SignalR user `{key}`,
  subscriptions table partition = key of tenant and topic, row = key of the client.
- Tenant and client ids must not contain `/`, `\` or control characters, nor be `.` or `..`:
  the tenant also names the DataBus folder `{tenant}/{attachment}`. The worker refuses other ids
  at negotiate.
- The names are opaque; to find a client's queue, compute `BoteClientNames.ClientQueueName`.
  Changing the scheme orphans every client's queue and subscriptions (`The_key_is_stable` pins it).

## Managed identity

`Program.cs` uses `DefaultAzureCredential` for all Azure SDK clients, so in
production nothing needs a secret: Service Bus, Storage (queue/table/blob),
SignalR and App Configuration all accept the identity-based connection forms
(`…:fullyQualifiedNamespace`, `…:serviceUri`, `Endpoint`), and the function
identity needs the corresponding data-plane roles on each resource (see
`infra/core/dbote-gateway.ts` and `infra/core/dbote-worker.ts` for the reference role assignments).

## Adoption checklist (new application)

1. Provision the product's own: Service Bus namespace (**Standard**), Storage
   account, Azure SignalR (**Serverless**, upstream → worker), identity provider.
2. Deploy the worker image; grant its identity the data-plane roles.
3. Set section A (host) settings.
4. Either set section B (point at App Configuration) **or** provide the section C
   `dbote:Worker:*` keys directly.
5. Configure the SignalR upstream to the worker's `/runtime/webhooks/signalr`.

## Known dead config (cleanup candidates)

- `dbote:Worker:ServiceBus:Queues:Events` — appears in `local.settings.json` and
  some compose files but has **no** property on `ServiceBusQueuesOptions` and is
  never read. Unbound/ignored.
- `dbote:Worker:ServiceBus:Queues:Error` — bound but unused by the worker (see above).
