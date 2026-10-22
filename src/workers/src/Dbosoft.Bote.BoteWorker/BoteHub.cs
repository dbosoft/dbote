using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Dbosoft.Bote.Abstractions.SignalR;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.BoteWorker.Services;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using JetBrains.Annotations;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Security.Claims;

namespace Dbosoft.Bote.BoteWorker;

[PublicAPI]
[SignalRConnection("dbote:Worker:SignalR:Connection")]
internal partial class BoteHub(
    ILogger<BoteHub> logger,
    IMessageConverter messageConverter,
    IServiceProvider serviceProvider,
    NegotiateAuthorizer negotiateAuthorizer,
    IOptions<ServiceBusOptions> serviceBusOptions,
    ITenantStorageResolver tenantStorageResolver,
    BlobServiceClient blobServiceClient,
    TableServiceClient tableServiceClient,
    DataBusCopyProcessor copyProcessor,
    WorkerMetrics workerMetrics,
    ServiceBusSenderCache senderCache)
    : ServerlessHub<IMessages>(serviceProvider)
{
    private const string HubName = nameof(BoteHub);

    /// <summary>Diagnostic: per-phase worker timings (inbound/outbound hot path) as JSON.
    /// Admin-level so it is protected by the host master key (not publicly anonymous).</summary>
    [Function("Metrics")]
    public async Task<HttpResponseData> Metrics(
        [HttpTrigger(AuthorizationLevel.Admin, "get")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(workerMetrics.Snapshot());
        return response;
    }


    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        var authorizationHeader = req.Headers.TryGetValues(HeaderNames.Authorization, out var authHeaders)
            ? authHeaders.FirstOrDefault()
            : null;

        var decision = await negotiateAuthorizer.Authorize(authorizationHeader, req.FunctionContext.CancellationToken);
        if (decision.Identity is not { } identity)
            return req.CreateResponse(decision.Status);

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = BoteClientNames.SignalRUserId(identity.TenantId, identity.ClientId),
            Claims =
            [
                new Claim(ClaimNames.TenantId, identity.TenantId),
                new Claim(ClaimNames.ClientId, identity.ClientId),
                // The hub calls see only these claims, not the token.
                ..(decision.MayAddressPeers ? [new Claim(ClaimNames.MayAddressPeers, "true")] : Array.Empty<Claim>()),
            ],
            CloseOnAuthenticationExpiration = true,
            TokenLifetime = TimeSpan.FromMinutes(15),
        });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }


    [Function(nameof(GetQueueMetadata))]
    public async Task<BoteQueueMetadata> GetQueueMetadata(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(GetQueueMetadata),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);

        var queueClient = await tenantStorageResolver.ResolveQueueClient(tenantId, clientId);
        // CreateIfNotExists (not Create): this is called again whenever a client refreshes an
        // expired queue SAS, and Create would throw 409 on the already-existing queue.
        await queueClient.CreateIfNotExistsAsync();
        var uri = queueClient.GenerateSasUri(
            QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
            DateTimeOffset.UtcNow.AddHours(1));

        var metaData = new BoteQueueMetadata()
        {
            Connection = uri.ToString(),
        };

        return metaData;
    }


    /// <summary>
    /// Issues a write-only SAS for the client's local dead-letter queue
    /// (<see cref="BoteClientNames.ClientErrorQueueName"/>). Clients move poison messages here
    /// directly, so the edge keeps its own failures instead of routing them through
    /// the <see cref="SendMessage"/> whitelist (which only accepts the cloud/clients
    /// queues and would reject the error queue).
    /// </summary>
    [Function(nameof(GetErrorQueueMetadata))]
    public async Task<BoteQueueMetadata> GetErrorQueueMetadata(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(GetErrorQueueMetadata),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);

        var queueClient = await tenantStorageResolver.ResolveErrorQueueClient(tenantId, clientId);
        await queueClient.CreateIfNotExistsAsync();
        var uri = queueClient.GenerateSasUri(
            QueueSasPermissions.Add,
            DateTimeOffset.UtcNow.AddHours(1));

        return new BoteQueueMetadata
        {
            Connection = uri.ToString(),
        };
    }


    private static bool MayAddressPeers(SignalRInvocationContext context) =>
        context.Claims.TryGetValue(ClaimNames.MayAddressPeers, out var value) && value == "true";

    private static string ExtractTenantId(SignalRInvocationContext context)
    {
        if (!context.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId) ||
            string.IsNullOrEmpty(tenantId))
            throw new UnauthorizedAccessException("Missing tenant claim");
        return tenantId.ToString() ?? throw new UnauthorizedAccessException("Missing tenant claim");
    }

    private static string ExtractClientId(SignalRInvocationContext context)
    {
        if (!context.Claims.TryGetValue(ClaimNames.ClientId, out var clientId) ||
            string.IsNullOrEmpty(clientId))
            throw new UnauthorizedAccessException("Missing client claim");
        return clientId.ToString() ?? throw new UnauthorizedAccessException("Missing client claim");
    }


}
