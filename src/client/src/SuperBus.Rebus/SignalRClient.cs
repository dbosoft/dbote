using System.Security.Claims;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Rebus.Bus;
using SuperBus.Transport.Abstractions;
using SuperBus.Rebus.Config;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SuperBus.Rebus;
    
internal interface ISignalRClient
{
    Task<string> GetQueueConnection();

    Task SendMessage(string queue, SuperBusMessage message);

    Task SubscribeToTopic(string topicName);

    Task UnsubscribeFromTopic(string topicName);
}

// TODO handle reconnects

internal class SignalRClient(
    Uri endpointUri,
    SuperBusCredentials credentials,
    IPendingMessagesIndicators pendingMessagesIndicator,
    IHttpClientFactory httpClientFactory)
    : ISignalRClient, IInitializable, IDisposable
{
    private HubConnection? _connection;
    private IDisposable? _listener;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("SuperBus.TokenClient");

    public void Initialize()
    {
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
        var queueMetadata = await _connection!.InvokeAsync<SuperBusQueueMetadata>("GetQueueMetadata");
        return queueMetadata.Connection;
    }

    public async Task SendMessage(string queue, SuperBusMessage message)
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

    // TODO review
    private async Task<string?> GetAccessToken()
    {
        // TODO test reauthentication
        // TODO add logging
        var signingCredentials = new SigningCredentials(credentials.SigningKey, SecurityAlgorithms.EcdsaSha256);

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, credentials.ConnectorId),
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
            Issuer = credentials.ConnectorId,
            IncludeKeyIdInHeader = true,
        };

        var jwt = handler.CreateToken(tokenDescriptor);

        var formContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [OpenIdConnectParameterNames.GrantType] = OpenIdConnectGrantTypes.ClientCredentials,
                [OpenIdConnectParameterNames.ClientId] = credentials.ConnectorId,
                [OpenIdConnectParameterNames.ClientAssertionType] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                [OpenIdConnectParameterNames.ClientAssertion] = jwt,
                ["scope"] = credentials.Scope
            });

        var response = await _httpClient.PostAsync(credentials.TokenEndpoint, formContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseMessage = new OpenIdConnectMessage(responseJson);

        return responseMessage.AccessToken;
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
