using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.Cloud;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Extensions;
using Rebus.Messages;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

/// <summary>
/// Guards the contract between a cloud side without Rebus (<see cref="BoteCloudSender"/>,
/// <see cref="BoteIncomingMessage"/>) and the edge clients, which run Rebus: messages pass the
/// worker's real Service Bus conversion and a real Rebus client over the bote transport, so a
/// change on either side that breaks the other fails here.
/// </summary>
[Collection(AzuriteCollection.Name)]
public class CloudRoundTripTests
{
    private const string TenantId = "tenant-a";
    private const string ClientId = "client-a";

    private readonly AzuriteFixture _azurite;

    public CloudRoundTripTests(AzuriteFixture azurite) => _azurite = azurite;

    [Fact]
    public void Wire_format_matches_Rebus()
    {
        RebusWireFormat.MessageIdHeader.Should().Be(Headers.MessageId);
        RebusWireFormat.TypeHeader.Should().Be(Headers.Type);
        RebusWireFormat.ContentTypeHeader.Should().Be(Headers.ContentType);
        RebusWireFormat.CorrelationIdHeader.Should().Be(Headers.CorrelationId);
        RebusWireFormat.CorrelationSequenceHeader.Should().Be(Headers.CorrelationSequence);
        RebusWireFormat.SentTimeHeader.Should().Be(Headers.SentTime);
        RebusWireFormat.IntentHeader.Should().Be(Headers.Intent);
        RebusWireFormat.ReturnAddressHeader.Should().Be(Headers.ReturnAddress);
        RebusWireFormat.PointToPointIntent.Should().Be(Headers.IntentOptions.PointToPoint);

        RebusWireFormat.TypeName(typeof(Ping)).Should().Be(typeof(Ping).GetSimpleAssemblyQualifiedName());
        RebusWireFormat.TypeName(typeof(Envelope<Ping>)).Should().Be(typeof(Envelope<Ping>).GetSimpleAssemblyQualifiedName());
    }

    [Fact]
    public async Task Cloud_message_reaches_a_Rebus_client_handler()
    {
        var inbound = await CreateQueueAsync("cloud-to-client");
        var sender = new CapturingSender();
        var cloud = new BoteCloudSender(sender, returnAddress: "cloud");

        await cloud.SendToClientAsync(TenantId, ClientId, new Ping { Text = "hello", Count = 3 },
            new BoteSendOptions { CorrelationId = "conversation-1" });

        var sent = sender.Messages.Should().ContainSingle().Subject;
        sent.ApplicationProperties[BoteHeaders.TenantId].Should().Be(TenantId);
        sent.ApplicationProperties[BoteHeaders.ClientId].Should().Be(ClientId);

        // What the worker does with a message from the clients queue
        var boteMessage = new MessageConverter().ToBote(Received(sent));
        await inbound.SendMessageAsync(JsonSerializer.Serialize(boteMessage));

        var received = new TaskCompletionSource<(Ping Message, IDictionary<string, string> Headers)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var activator = new BuiltinHandlerActivator();
        activator.Handle<Ping>((_, context, message) =>
        {
            received.TrySetResult((message, context.Headers));
            return Task.CompletedTask;
        });

        using var bus = StartClient(activator, inbound, new CapturingSignalRClient(inbound));

        var (ping, headers) = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
        ping.Text.Should().Be("hello");
        ping.Count.Should().Be(3);
        headers[Headers.MessageId].Should().Be(sent.MessageId);
        headers[Headers.CorrelationId].Should().Be("conversation-1");
    }

    [Fact]
    public async Task Client_replies_to_a_cloud_message_through_the_cloud_queue()
    {
        var inbound = await CreateQueueAsync("client-reply");
        var sender = new CapturingSender();
        await new BoteCloudSender(sender, returnAddress: "cloud")
            .SendToClientAsync(TenantId, ClientId, new Ping { Text = "question" });

        var sent = sender.Messages.Should().ContainSingle().Subject;
        // Without a correlation id given, the message starts a conversation as with Rebus
        sent.CorrelationId.Should().Be(sent.MessageId);
        sent.ApplicationProperties[Headers.CorrelationSequence].Should().Be("0");

        await inbound.SendMessageAsync(JsonSerializer.Serialize(new MessageConverter().ToBote(Received(sent))));

        var signalR = new CapturingSignalRClient(inbound);
        using var activator = new BuiltinHandlerActivator();
        activator.Handle<Ping>((bus, _, message) => bus.Reply(new Ping { Text = $"re: {message.Text}" }));
        using var client = StartClient(activator, inbound, signalR);

        await WaitUntilAsync(() => !signalR.Sent.IsEmpty, TimeSpan.FromSeconds(30));
        var (queue, reply) = signalR.Sent.Should().ContainSingle().Subject;
        queue.Should().Be("cloud");
        reply.Headers[Headers.CorrelationId].Should().Be(sent.MessageId);
        reply.Headers[Headers.CorrelationSequence].Should().Be("1");
    }

    [Fact]
    public async Task Client_message_is_read_from_the_cloud_queue()
    {
        var inbound = await CreateQueueAsync("client-to-cloud");
        var signalR = new CapturingSignalRClient(inbound);
        using var activator = new BuiltinHandlerActivator();
        using var bus = StartClient(activator, inbound, signalR);

        await bus.Send(new Ping { Text = "from the edge", Count = 7 });

        var (queue, boteMessage) = signalR.Sent.Should().ContainSingle().Subject;
        queue.Should().Be("cloud");

        // What the worker does with a message a client sent over SignalR
        var serviceBusMessage = new MessageConverter().ToServiceBus(boteMessage);
        serviceBusMessage.ApplicationProperties[BoteHeaders.TenantId] = TenantId;
        serviceBusMessage.ApplicationProperties[BoteHeaders.ClientId] = ClientId;

        var incoming = BoteIncomingMessage.From(Received(serviceBusMessage));

        incoming.TenantId.Should().Be(TenantId);
        incoming.ClientId.Should().Be(ClientId);
        incoming.MessageId.Should().Be(boteMessage.Headers[Headers.MessageId]);
        incoming.Is<Ping>().Should().BeTrue();
        incoming.Is<Envelope<Ping>>().Should().BeFalse();
        var ping = incoming.Deserialize<Ping>();
        ping.Text.Should().Be("from the edge");
        ping.Count.Should().Be(7);
        incoming.Attachment.Should().BeNull();
    }

    [Fact]
    public async Task Reply_goes_to_the_sending_client_in_its_conversation()
    {
        var request = BoteIncomingMessage.From(Received(new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(new Ping()))
        {
            MessageId = "request-1",
            ContentType = RebusWireFormat.JsonContentType,
            ApplicationProperties = { [BoteHeaders.TenantId] = TenantId, [BoteHeaders.ClientId] = ClientId },
        }));
        var sender = new CapturingSender();

        await new BoteCloudSender(sender).ReplyAsync(request, new Ping { Text = "pong" });

        var reply = sender.Messages.Should().ContainSingle().Subject;
        reply.CorrelationId.Should().Be("request-1");
        reply.ApplicationProperties[Headers.CorrelationSequence].Should().Be("1");
        reply.ApplicationProperties[BoteHeaders.TenantId].Should().Be(TenantId);
        reply.ApplicationProperties[BoteHeaders.ClientId].Should().Be(ClientId);
    }

    [Fact]
    public async Task Broadcast_names_the_topic_and_no_client()
    {
        var sender = new CapturingSender();

        await new BoteCloudSender(sender).BroadcastAsync(TenantId, "chat", new Ping());

        var message = sender.Messages.Should().ContainSingle().Subject;
        message.ApplicationProperties[BoteHeaders.Topic].Should().Be("chat");
        message.ApplicationProperties.Should().NotContainKey(BoteHeaders.ClientId);
    }

    [Fact]
    public async Task Sender_refuses_to_set_bote_headers()
    {
        var send = () => new BoteCloudSender(new CapturingSender()).SendToClientAsync(TenantId, ClientId, new Ping(),
            new BoteSendOptions { Headers = new Dictionary<string, string> { [BoteHeaders.TenantId] = "other" } });

        await send.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Attachment_is_in_the_outbox_before_its_message_is_sent()
    {
        var storage = new BlobServiceClient(_azurite.ConnectionString);
        var outbox = storage.GetBlobContainerClient(BoteStorageConstants.Outbox);
        await outbox.CreateIfNotExistsAsync();

        var sender = new CapturingSender(onSend: async message =>
        {
            var attachmentId = (string)message.ApplicationProperties[BoteHeaders.AttachmentId];
            var blob = outbox.GetBlobClient($"{TenantId}/{attachmentId}");
            (await blob.ExistsAsync()).Value.Should().BeTrue("the worker must find the attachment when the message arrives");
            var properties = await blob.GetPropertiesAsync();
            properties.Value.Metadata[BoteStorageConstants.Metadata.TenantId].Should().Be(TenantId);
            properties.Value.Metadata["filename"].Should().Be("report.txt");
        });

        var attachment = BoteOutgoingAttachment.Create(new MemoryStream("attached"u8.ToArray()),
            new Dictionary<string, string> { ["filename"] = "report.txt" });

        await new BoteCloudSender(sender, outbox: outbox)
            .SendToClientAsync(TenantId, ClientId, new Ping(), new BoteSendOptions { Attachment = attachment });

        sender.Messages.Should().ContainSingle()
            .Which.ApplicationProperties[BoteHeaders.AttachmentId].Should().Be(attachment.Id);
    }

    [Fact]
    public async Task Attachment_from_a_client_is_read_from_the_inbox()
    {
        var storage = new BlobServiceClient(_azurite.ConnectionString);
        var inbox = storage.GetBlobContainerClient(BoteStorageConstants.Inbox);
        await inbox.CreateIfNotExistsAsync();
        var blob = inbox.GetBlobClient($"{TenantId}/attachment-1");
        await blob.UploadAsync(new MemoryStream("from the edge"u8.ToArray()), new BlobUploadOptions
        {
            Metadata = new Dictionary<string, string>
            {
                [BoteStorageConstants.Metadata.TenantId] = TenantId,
                ["filename"] = "edge.txt",
            },
        });
        var sas = blob.GenerateSasUri(BlobSasPermissions.Read | BlobSasPermissions.Delete, DateTimeOffset.UtcNow.AddHours(1));

        var incoming = BoteIncomingMessage.From(Received(AttachmentMessage(sas)), storage);

        var attachment = incoming.Attachment.Should().NotBeNull().And.Subject.As<BoteIncomingAttachment>();
        attachment.Id.Should().Be("attachment-1");
        await using (var content = await attachment.OpenReadAsync())
            (await new StreamReader(content, Encoding.UTF8).ReadToEndAsync()).Should().Be("from the edge");
        (await attachment.GetMetadataAsync()).Should().BeEquivalentTo(new Dictionary<string, string> { ["filename"] = "edge.txt" });

        await attachment.DeleteAsync();
        (await blob.ExistsAsync()).Value.Should().BeFalse();
    }

    [Fact]
    public void Attachment_on_another_host_is_refused()
    {
        var storage = new BlobServiceClient(_azurite.ConnectionString);
        var foreign = new Uri("https://attacker.example.com/bote-inbox/tenant-a/attachment-1?sig=x");

        var read = () => BoteIncomingMessage.From(Received(AttachmentMessage(foreign)), storage);

        read.Should().Throw<InvalidOperationException>().WithMessage("*untrusted host*");
    }

    [Fact]
    public void Message_without_tenant_is_refused()
    {
        var read = () => BoteIncomingMessage.From(Received(new ServiceBusMessage("{}") { MessageId = "m" }));

        read.Should().Throw<InvalidOperationException>().WithMessage("*no tenant*");
    }

    [Fact]
    public void Attachment_id_cannot_be_a_path()
    {
        var create = () => new BoteOutgoingAttachment("../other-tenant/x", new MemoryStream());

        create.Should().Throw<ArgumentException>();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(200);
    }

    private static ServiceBusMessage AttachmentMessage(Uri sas) =>
        new(JsonSerializer.SerializeToUtf8Bytes(new Ping()))
        {
            MessageId = "with-attachment",
            ContentType = RebusWireFormat.JsonContentType,
            ApplicationProperties =
            {
                [BoteHeaders.TenantId] = TenantId,
                [BoteHeaders.ClientId] = ClientId,
                [BoteHeaders.AttachmentId] = "attachment-1",
                [BoteHeaders.DataBusInboxSas] = sas.ToString(),
            },
        };

    private async Task<QueueClient> CreateQueueAsync(string prefix)
    {
        var queue = new QueueClient(_azurite.ConnectionString, $"{prefix}-{Guid.NewGuid():N}"[..40]);
        await queue.CreateAsync();
        return queue;
    }

    private static global::Rebus.Bus.IBus StartClient(BuiltinHandlerActivator activator, QueueClient inbound,
        ISignalRClient signalR) =>
        Configure.With(activator)
            .Logging(l => l.None())
            .Transport(t => t.UseBote(
                inbound.Name,
                signalR,
                new BoteTransportOptions { PendingMessagesReconcileInterval = TimeSpan.FromSeconds(1) }))
            .Routing(r => r.TypeBased().Map<Ping>("cloud"))
            .Serialization(s => s.UseSystemTextJson())
            .Start();

    /// <summary>A sent message as a Service Bus trigger hands it to the receiver.</summary>
    private static ServiceBusReceivedMessage Received(ServiceBusMessage message) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: message.Body,
            messageId: message.MessageId,
            correlationId: message.CorrelationId,
            contentType: message.ContentType,
            timeToLive: message.TimeToLive,
            properties: new Dictionary<string, object>(message.ApplicationProperties));

    public class Ping
    {
        public string Text { get; set; } = "";
        public int Count { get; set; }
    }

    public class Envelope<T>
    {
        public T? Content { get; set; }
    }

    private sealed class CapturingSender(Func<ServiceBusMessage, Task>? onSend = null) : ServiceBusSender
    {
        public ConcurrentQueue<ServiceBusMessage> Messages { get; } = new();

        public override async Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            if (onSend is not null)
                await onSend(message);
            Messages.Enqueue(message);
        }
    }

    /// <summary>
    /// Stands in for the worker's SignalR hub: hands out the client's Azurite queue and keeps
    /// what the client sends.
    /// </summary>
    private sealed class CapturingSignalRClient(QueueClient inbound) : ISignalRClient
    {
        public ConcurrentQueue<(string Queue, BoteMessage Message)> Sent { get; } = new();

        public Task<string> GetQueueConnection() =>
            Task.FromResult(inbound.GenerateSasUri(
                QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
                DateTimeOffset.UtcNow.AddHours(1)).ToString());

        public Task SendMessage(string queue, BoteMessage message)
        {
            Sent.Enqueue((queue, message));
            return Task.CompletedTask;
        }

        public Task<string> GetErrorQueueConnection() => throw new NotSupportedException();
        public Task SubscribeToTopic(string topicName) => throw new NotSupportedException();
        public Task UnsubscribeFromTopic(string topicName) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUri(string attachmentId) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUploadUri(string attachmentId) => throw new NotSupportedException();
        public Task NotifyAttachmentUploaded(string attachmentId) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetAttachmentMetadata(string attachmentId) => throw new NotSupportedException();
    }
}
