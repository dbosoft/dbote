using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Dbosoft.Bote.Abstractions.SignalR;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.BoteWorker.Services;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Transport.Abstractions;
using JetBrains.Annotations;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
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
    ITokenValidationService tokenValidationService,
    IOptions<OpenIdOptions> openIdOptions, 
    IOptions<ServiceBusOptions> serviceBusOptions,
    ServiceBusClient serviceBusClient,
    ITenantStorageResolver tenantStorageResolver,
    BlobServiceClient blobServiceClient,
    TableServiceClient tableServiceClient,
    DataBusCopyProcessor copyProcessor)
    : ServerlessHub<IMessages>(serviceProvider)
{
    private const string HubName = nameof(BoteHub);


    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        if (!req.Headers.TryGetValues(HeaderNames.Authorization, out var authHeaders))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var authHeader = authHeaders.FirstOrDefault();
        if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var jwt = authHeader.Substring("Bearer ".Length);

        var validationResult = await tokenValidationService.ValidateAccessToken(jwt, req.FunctionContext.CancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogInformation(validationResult.Exception, "Token validation failed");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var token = (JsonWebToken)validationResult.SecurityToken;

        // Extract client identity from standard JWT claims
        if (!ClientIdentity.TryExtract(token, out var identity) || identity is null)
        {
            logger.LogWarning("Failed to extract client identity from token");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // Validate required scope (no fallback - must be configured)
        if (string.IsNullOrEmpty(openIdOptions.Value.RequiredScope))
        {
            logger.LogError("RequiredScope is not configured in OpenIdOptions");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        if (!token.TryGetValue("scope", out string scope) || scope != openIdOptions.Value.RequiredScope)
        {
            logger.LogWarning("Invalid or missing scope. Expected: {ExpectedScope}, Actual: {ActualScope}",
                openIdOptions.Value.RequiredScope, scope);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = $"{identity.TenantId}-{identity.ClientId}",
            Claims =
            [
                new Claim(ClaimNames.TenantId, identity.TenantId),
                new Claim(ClaimNames.ClientId, identity.ClientId),
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
            nameof(this.GetQueueMetadata),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);

        var queueClient = await tenantStorageResolver.ResolveQueueClient(tenantId, clientId);
        await queueClient.CreateAsync();
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
    /// (<c>{prefix}-{tenant}-{client}-error</c>). Clients move poison messages here
    /// directly, so the edge keeps its own failures instead of routing them through
    /// the <see cref="SendMessage"/> whitelist (which only accepts the cloud/clients
    /// queues and would reject the error queue).
    /// </summary>
    [Function(nameof(GetErrorQueueMetadata))]
    public async Task<BoteQueueMetadata> GetErrorQueueMetadata(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetErrorQueueMetadata),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);

        var queueClient = await tenantStorageResolver.ResolveQueueClient(tenantId, $"{clientId}-error");
        await queueClient.CreateIfNotExistsAsync();
        var uri = queueClient.GenerateSasUri(
            QueueSasPermissions.Add,
            DateTimeOffset.UtcNow.AddHours(1));

        return new BoteQueueMetadata
        {
            Connection = uri.ToString(),
        };
    }


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
