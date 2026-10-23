# Working on dbote

What dbote is, its parts, flows and security model: `README.md`. Worker settings and naming:
`docs/worker-configuration.md`. Cloud side without Rebus: `docs/cloud-without-rebus.md`.

bote is a framework. Judge changes by what every application on it needs; fairness, sharding or
quotas per tenant are the application's business, not missing features of the gateway.

## Rules

### The worker is the trust boundary

- Tenant and client of a message come only from the validated token (`ClientIdentity.TryExtract`,
  `NegotiateAuthorizer`). Never read them from anything a client sends.
- A client may set no `dbote-*` header except `dbote-attachment-id` and `dbote-sender-client-id`
  (`BoteHeaderPolicy`, an allow-list). A new `dbote-*` header is therefore refused by default;
  keep it that way. `SendMessage` overwrites the sender header with the true sender.
- Where a client may send is `BoteQueuePolicy`: the cloud queue, its own queue (Rebus defer), and
  with the peer scope the queue of another client its token lists in `bote_peers` (`BotePeers`)
  that has connected once - always within the sender's tenant. Presence answers only about those. `ClientSendRules` stamps tenant, client, sender and the return address (always
  the sender's own queue, or a client could have the cloud or a peer answer to someone else) and
  refuses messages scheduled more than 7 days ahead or too large for a storage queue.
- Everything the worker keeps per client is named by `BoteClientNames` (hash of tenant and
  client). Never join ids into a name yourself. Never change how the key is derived: it orphans
  every deployed queue and subscription (`The_key_is_stable` pins it).
- Blob paths go through `DataBusPaths`; every id from outside through `BoteIds.IsPathSegment` /
  `BoteAttachmentIds`. The storage SDK resolves `..` in blob names, so an unchecked id can leave
  the tenant's folder.
- A client writes only below its own key (`{tenant}/upload/{key}/…`, inbox `{tenant}/{key}/…`)
  and only with GUID ids. A peer's attachment and the cloud's share `{tenant}/{id}`; neither copy
  may take the other's place (`PeerAttachments`, `DataBusCopyProcessor`). Reading a delivered
  attachment is tenant-wide by design (broadcasts); that is an accepted risk.
- The storage emulator fails a conditional copy onto an existing blob with an error, not 409/412:
  look first, keep the condition for the race.

### Wire format

The clients are Rebus clients. Anything that changes how messages look on Service Bus (headers,
content type, type names) must keep `Dbosoft.Bote.Cloud` and the Rebus integration in line;
`CloudRoundTripTests` fails when they drift apart.

### Azure Functions queue triggers need the Base64 client

A queue read by a `[QueueTrigger]` must be written with the named `AzureWebJobsStorage` queue client
(`MessageEncoding = Base64`, registered in the worker's `Program.cs`), never with the default
injected `QueueServiceClient` (no encoding, gateway storage). With the wrong client the trigger
fails to decode, the message goes to poison after 5 attempts, and the function logs nothing.

```csharp
var queue = queueClientFactory.CreateClient("AzureWebJobsStorage").GetQueueClient("databus-copy-monitor");
```

`DataBusCopyProcessor` writes `databus-copy-monitor` this way; `DataBusCopyFile.DataBusCopyFileMonitor`
reads it. The client queues (`{prefix}-{key}`) are plain application queues on the default client.

### Trigger sources must exist before the worker starts

On Container Apps the platform creates a KEDA scaler per trigger. A missing queue or container
makes its scaler fail, which keeps the worker from scaling to zero. Infra creates `bote-outbox`,
`bote-inbox` and `databus-copy-monitor` up front; do the same for any new trigger.

### Packages and compatibility

- Every project under `src/` is packed and pushed by `azure-pipelines.yml`. An application
  (worker, identity provider, tools) sets `<IsPackable>false</IsPackable>`.
- Libraries reference Rebus `>= 8.8.0` (SAPId uses 8.8); apps and tests may use newer.
- Changes to `ITenantStorageResolver` and other public extension points break implementers; say so.

### Docs

Docs are English reference text for people using the code. No session notes, no review trackers,
no finding ids (H1, M4, …) in code, tests or docs.

## Build and test

```powershell
$env:MSBUILDDISABLENODEREUSE = '1'   # MSBuild nodes otherwise keep DLLs locked between builds
dotnet build Dbote.sln
dotnet test Dbote.sln                # integration tests start Azurite via Testcontainers
```

- Podman instead of Docker: `podman compose`; Testcontainers needs `DOCKER_HOST` on the podman
  pipe, Ryuk off and Azurite pinned (`test/README.md`).
- The Service Bus emulator's management API needs port 80 inside the podman machine:
  `podman machine ssh 'sudo sysctl -w net.ipv4.ip_unprivileged_port_start=80'` once per boot.
- Container images copy `bin/Release/net10.0/linux-x64/publish`: `dotnet publish -c Release -r linux-x64`
  before `compose build`, or the image is stale. The sample scripts (`Start-Services.ps1`,
  `Start-LoadTest.ps1`) do it.
- `dev/asrs` (SignalR emulator) stays on the .NET 8 SDK; the emulator tool needs it.
- The installed Azure Functions Core Tools may be too old for .NET 10; run Functions apps in their
  container image (`4-dotnet-isolated10.0`).

## Verifying a change

- Unit tests: `test/Dbosoft.Bote.BoteWorker.Tests` (tokens, negotiate, names, paths, no services),
  `test/Dbosoft.Bote.Rebus.Tests` (client transport).
- Integration tests: `test/Dbosoft.Bote.Rebus.IntegrationTests` (Azurite; cloud round trip, DataBus
  copy, poison handling, policies).
- Anything in the worker's hub (SignalR, routing, attachments) is only covered end to end: run a
  sample. `samples/loadtest` exercises attachments in both directions; `samples/simple` ping/pong,
  defer and push; `samples/chat` broadcasts. Check the worker log for `fail:` lines - the 409
  `ContainerAlreadyExists` and 404 `BlobNotFound` SDK lines are expected.

## Code map

| Area | Where |
| --- | --- |
| Worker functions | `src/workers/src/Dbosoft.Bote.BoteWorker/BoteHub.cs` (negotiate, queue SAS), `BoteHub.Messages.cs` (client sends, delivery, attachment readiness), `BoteHub.DataBus.cs` (attachment SAS, uploads, peer copies), `BoteHub.Subscriptions.cs` (topics), `DataBusCopyFile.cs` (outbox trigger, copy monitor) |
| Worker auth | `TokenValidationService.cs`, `NegotiateAuthorizer.cs`, `ClientIdentity.cs`, `OpenIdOptions.cs`; what the IdP must do: `docs/identity-provider.md` |
| Shared rules | `src/Dbosoft.Bote.Primitives`: `BoteHeaders`, `BoteHeaderPolicy`, `BoteQueuePolicy`, `BoteClientNames`, `BoteIds`, `BotePeers`, `BoteAttachmentIds`, `BoteStorageConstants`, `DataBusSasUriValidator` |
| Cloud side, Rebus | `src/Dbosoft.Bote.Rebus.Integration`: `BoteConfigurationExtensions` (`EnableBote`, `EnableBoteDataBus`), `BoteNameFormatter` (`{clients}-{id}` → clients queue), `Pipeline/*` |
| Cloud side, no Rebus | `src/Dbosoft.Bote.Cloud`: `BoteIncomingMessage`, `BoteCloudSender`, `RebusWireFormat` |
| Client transport | `src/client/src/Dbosoft.Bote.Rebus`: `Transport/BoteTransport.cs`, `SignalRClient.cs`, `PendingMessagesIndicator.cs`, `BoteDataBus.cs`, `Subscriptions/BoteSubscriptionStorage.cs` |
| Dev identity provider | `src/workers/src/Dbosoft.Bote.BasicIdentityProvider` - development only; don't grow it towards production |
| Infra | `infra/core` (gateway constructs, npm `@dbosoft/bote-infra`), `infra/app` (samples, dev IdP), `infra/main.ts` |

## Message details

- Service Bus application properties set by the worker: `dbote-tenant-id`, `dbote-client-id`
  (sender on the cloud queue, receiver on the clients queue), `dbote-sender-client-id` (peer
  messages), `dbote-topic` (broadcast), `dbote-attachment-id`, `dbote-cloud-inbox-sas` (read SAS of a
  client attachment for the cloud), `dbote-reschedule-cnt`.
- Delivery to a client strips every `dbote-*` header except the sender of a peer message, so a
  client that defers what it received is not refused for headers it may not set.
- A message whose attachment is not copied yet is parked: client → cloud on `CloudIncoming`
  (`CloudIncomingMonitor`), cloud/peer → client rescheduled on the clients queue; both give up and
  dead-letter after about two hours.
- Storage queue messages are limited to 64 KB; larger payloads go as attachments.
- Rebus defer from a client is a send to its own queue with a delivery time; it travels through the
  clients queue back to the client. Attachments cannot be deferred.

## Azure

Test stack: subscription `998590b9-…`, tenant `5a6e4bb8-…`, `swedencentral`, resource group
`rg-dbote-test`. Steps in `infra/README.md`. Run the scripts from PowerShell: Git Bash rewrites
arguments starting with `/subscriptions/…` into file paths. Synthesize with
`$env:TS_NODE_PREFER_TS_EXTS='true'; npx cdktf synth` (compiled `.js` next to the `.ts` would win
otherwise). The Azure DevOps organization (feeds, pipeline) still lives in tenant `cb0bb315-…`.
