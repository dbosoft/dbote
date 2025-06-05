using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
}

// TODO handle reconnects

internal class SignalRClient(
    Uri endpointUri,
    SuperBusCredentials credentials,
    IPendingMessagesIndicators pendingMessagesIndicator)
    : ISignalRClient, IInitializable, IDisposable
{
    private HubConnection? _connection;
    private IDisposable? _listener;

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

    // TODO review
    private async Task<string?> GetAccessToken()
    {
        // TODO test reauthentication
        // TODO add logging
        var clientId = $"{credentials.TenantId}-{credentials.ConnectorId}";

        var signingCredentials = new SigningCredentials(credentials.SigningKey, SecurityAlgorithms.EcdsaSha256);

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(ClaimNames.TenantId, credentials.TenantId),
            new Claim(ClaimNames.ConnectorId, credentials.ConnectorId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            // TODO add nbf and exp
        ]);

        var handler = new JsonWebTokenHandler();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = signingCredentials,
            Audience = endpointUri.ToString(),
            Issuer = clientId,
        };

        var jwt = handler.CreateToken(tokenDescriptor);
        
        var formContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [OpenIdConnectParameterNames.GrantType] = OpenIdConnectGrantTypes.ClientCredentials,
                [OpenIdConnectParameterNames.ClientId] = $"{credentials.TenantId}-{credentials.ConnectorId}",
                [OpenIdConnectParameterNames.ClientAssertionType] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                [OpenIdConnectParameterNames.ClientAssertion] = jwt,
                ["scope"] = "superbus"
            });
        
        using var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(endpointUri + "/token", formContent);
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
