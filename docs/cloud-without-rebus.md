# The cloud side without Rebus (`Dbosoft.Bote.Cloud`)

The cloud side of a bote application does not have to be a Rebus endpoint. An application
whose backend is, for example, an Azure Functions app with its own request pipeline reads the
messages of its clients straight from the cloud queue and sends to its clients with
`Dbosoft.Bote.Cloud`. The clients stay as they are: Rebus over the bote transport.

```
client (Rebus) ──SignalR──► BoteWorker ──► cloud queue ──► [ServiceBusTrigger] BoteIncomingMessage
client (Rebus) ◄──storage queue── BoteWorker ◄── clients queue ◄── BoteCloudSender
```

Nothing changes in the worker: it already works on plain Service Bus messages. What the
Rebus integration (`.EnableBote()`, `.EnableBoteDataBus()`) does in the Rebus pipeline, the
package does in its two classes.

## Receiving

```csharp
[Function("Cloud")]
public async Task Receive(
    [ServiceBusTrigger("%dbote:Cloud:ServiceBus:Queues:Cloud%", Connection = "dbote:Cloud:ServiceBus:Connection")]
    ServiceBusReceivedMessage message,
    CancellationToken cancellationToken)
{
    var incoming = BoteIncomingMessage.From(message, gatewayStorage); // storage: only for attachments

    if (incoming.Is<PingMessage>())
    {
        var ping = incoming.Deserialize<PingMessage>();
        // incoming.TenantId / incoming.ClientId: set by the worker from the client's token
        await sender.ReplyAsync(incoming, new PongMessage(), cancellationToken: cancellationToken);
    }
}
```

- `TenantId` and `ClientId` come from the worker, which takes them from the client's verified
  token and refuses messages in which the client sets them. A message without a tenant is
  refused.
- `Is<T>()` compares the Rebus type name the client sent (`Namespace.Type, Assembly`), so the
  message contracts are shared between the clients and the backend, as with Rebus.
- `Deserialize<T>()` uses the settings of Rebus' System.Text.Json serializer, which the clients
  use (`s.UseSystemTextJson()`); pass other options if the clients are configured differently.
- Errors: let them throw. Service Bus delivers the message again and dead-letters it after the
  queue's max delivery count; there is no Rebus error queue. The worker sends with the client's
  message id, and the cloud queue detects duplicates (`RequiresDuplicateDetection`).
- **Attachments**: if the client sent a DataBus attachment, `Attachment` gives it: read it with
  `OpenReadAsync`/`GetMetadataAsync`, keep it where the application keeps its data, then
  `DeleteAsync` it from the inbox. The SAS URI is accepted only for the gateway storage passed to
  `From` (SSRF guard, the same check as in the Rebus integration).

## Sending

```csharp
var sender = BoteCloudSender.Create(serviceBusClient, clientsQueue, cloudQueue, gatewayStorage);

await sender.SendToClientAsync(tenantId, clientId, new PushMessage());
await sender.BroadcastAsync(tenantId, "chat", new ChatMessage());     // subscribed clients only
await sender.ReplyAsync(incoming, new PongMessage());                  // sender of incoming, correlated
await sender.SendToClientAsync(tenantId, clientId, new FileMessage { FileId = attachment.Id },
    new BoteSendOptions { Attachment = attachment });                  // attachment staged first
```

- The cloud queue is the messages' return address (`rbs2-return-address`), so a client can
  answer with `bus.Reply`, as it can to a Rebus cloud service.
- `BoteSendOptions` sets the message id (for duplicate detection on resend), correlation id,
  time to live, a delivery time and further headers. `dbote-*` headers are set by the sender only.
- An attachment (`BoteOutgoingAttachment.Create(stream, metadata)`) is uploaded to
  `bote-outbox/{tenantId}/{attachmentId}` in the gateway storage **before** the message is sent,
  as the Rebus integration does; the worker copies it to the client. The message refers to the
  attachment by its id; the client reads it through its bote DataBus.

## Wire format

What the clients expect, and what `RebusWireFormat` writes and reads:

| On the Service Bus message | Value |
| --- | --- |
| `MessageId` | Rebus `rbs2-msg-id` |
| `ContentType` | `application/json;charset=utf-8` (Rebus `rbs2-content-type`) |
| `CorrelationId` | Rebus `rbs2-corr-id`; the message id when a message starts a conversation |
| property `rbs2-msg-type` | `Namespace.Type, Assembly` (generic arguments in brackets) |
| property `rbs2-intent` | `p2p` |
| property `rbs2-senttime` | ISO 8601 |
| property `rbs2-corr-seq` | position in the conversation (0, a reply one more) |
| property `rbs2-return-address` | the cloud queue |
| property `dbote-tenant-id` | tenant (both directions) |
| property `dbote-client-id` | sending client (to the cloud) / target client (to clients) |
| property `dbote-topic` | broadcast topic, instead of a client |
| property `dbote-attachment-id` | DataBus attachment id |
| property `dbote-cloud-inbox-sas` | read SAS of an incoming attachment (worker-issued) |
| body | UTF-8 JSON |

`test/Dbosoft.Bote.Rebus.IntegrationTests/CloudRoundTripTests.cs` runs both directions through
the worker's conversion and a real Rebus client, so a change that breaks this contract fails
there.

## Infrastructure

The receiving app needs, on the gateway's resources: Service Bus Data Owner on the cloud queue
(the trigger reads the queue's message count to scale), Data Sender on the clients queue, and
Blob Data Contributor on the `bote-outbox` container for attachments. The cloud queue detects
duplicates (`requiresDuplicateDetection`), which can only be set when it is created. `DboteCloudFunctionReceiver` in `infra/core` grants exactly that for a principal
and returns the settings (namespace, queue names, blob endpoint).

## Sample

`samples/simple/Dbosoft.Bote.Samples.Simple.Functions` is the simple sample's cloud side as
Functions: `./Start-Services.ps1 -Functions` in `samples/simple`.
