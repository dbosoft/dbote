using System.Security.Claims;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Transport.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualBasic;
using Rebus.Bus;
using Rebus.Logging;

namespace Dbosoft.Bote.Rebus;
    
internal interface ISignalRClient
{
    Task<string> GetQueueConnection();

    Task SendMessage(string queue, BoteMessage message);

    Task SubscribeToTopic(string topicName);

    Task UnsubscribeFromTopic(string topicName);

    // DataBus - Read access (inbox)
    Task<string> GetDataBusAttachmentUri(string attachmentId);

    // DataBus - Write access (outbox)
    Task<string> GetDataBusAttachmentUploadUri(string attachmentId);

    // DataBus - Notification after upload
    Task NotifyAttachmentUploaded(string attachmentId);

    // DataBus - Read metadata (filtered, without internal metadata)
    Task<Dictionary<string, string>> GetAttachmentMetadata(string attachmentId);
}

// TODO handle reconnects

internal class SignalRClient(
    Uri endpointUri,
    BoteCredentials credentials,
    IPendingMessagesIndicators pendingMessagesIndicator,
    IRebusLoggerFactory loggerFactory,
    IHttpClientFactory httpClientFactory)
    : ISignalRClient, IInitializable, IDisposable
{
    private HubConnection? _connection;
    private IDisposable? _listener;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Bote.TokenClient");
    private readonly ILog _log = loggerFactory.GetLogger<SignalRClient>();

    public void Initialize()
    {
        _log.Debug("Initializing dbote SignalR client...");
        //TODO Make initial connection more reliable in case of network issues
        _connection = new HubConnectionBuilder()
            // TODO this must be URL of the Azure function with the negotiate endpoint
            .WithUrl(endpointUri, options =>
            {
                options.AccessTokenProvider = GetAccessToken;
            })
            .Build();

        _listener = _connection.On(nameof(IIncoming.NewMessage), async (string messageId) =>
        {
            await pendingMessagesIndicator.SetAsync();
        });

        AsyncHelpers.RunSync(() => _connection.StartAsync());
    }

    public async Task<string> GetQueueConnection()
    {
        var queueMetadata = await _connection!.InvokeAsync<BoteQueueMetadata>("GetQueueMetadata");
        return queueMetadata.Connection;
    }

    public async Task SendMessage(string queue, BoteMessage message)
    {
        await _connection!.SendAsync("SendMessage", queue, message);
    }

    public async Task SubscribeToTopic(string topicName)
    {
        await _connection!.InvokeAsync("SubscribeToTopic", topicName);
    }

    public async Task UnsubscribeFromTopic(string topicName)
    {
        await _connection!.InvokeAsync("UnsubscribeFromTopic", topicName);
    }

    public async Task<string> GetDataBusAttachmentUri(string attachmentId)
    {
        return await _connection!.InvokeAsync<string>("GetDataBusAttachmentUri", attachmentId);
    }

    public async Task<string> GetDataBusAttachmentUploadUri(string attachmentId)
    {
        return await _connection!.InvokeAsync<string>("GetDataBusAttachmentUploadUri", attachmentId);
    }

    public async Task NotifyAttachmentUploaded(string attachmentId)
    {
        await _connection!.InvokeAsync("AttachmentUploaded", attachmentId);
    }

    public async Task<Dictionary<string, string>> GetAttachmentMetadata(string attachmentId)
    {
        return await _connection!.InvokeAsync<Dictionary<string, string>>("GetAttachmentMetadata", attachmentId);
    }

    // TODO review
    private async Task<string?> GetAccessToken()
    {
        // TODO test reauthentication
        // TODO add logging
        var signingCredentials = new SigningCredentials(credentials.SigningKey, SecurityAlgorithms.EcdsaSha256);

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, credentials.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ]);

        var handler = new JsonWebTokenHandler();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = signingCredentials,
            Audience = credentials.Authority,
            Issuer = credentials.ClientId,
            IncludeKeyIdInHeader = true,
        };

        var jwt = handler.CreateToken(tokenDescriptor);

        var formContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [OpenIdConnectParameterNames.GrantType] = OpenIdConnectGrantTypes.ClientCredentials,
                [OpenIdConnectParameterNames.ClientId] = credentials.ClientId,
                [OpenIdConnectParameterNames.ClientAssertionType] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                [OpenIdConnectParameterNames.ClientAssertion] = jwt,
                ["scope"] = credentials.Scope
            });

        try
        {
            var response = await _httpClient.PostAsync(credentials.TokenEndpoint, formContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseMessage = new OpenIdConnectMessage(responseJson);

            return responseMessage.AccessToken;
        }
        catch (Exception ex)
        {
            _log.Error(ex,
                "Failed to retrieve access token for dbote SignalR client");
            throw new InvalidOperationException("Error acquiring access token", ex);
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
