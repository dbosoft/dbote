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

    private Task<string?> GetAccessToken()
    {
        // TODO review
        // Create a new ECDsa instance and import the private key
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(credentials.SigningKey), out _);
        
        var handler = new JsonWebTokenHandler();

        var claims = new[]
        {
            new Claim("tenant_id", credentials.TenantId),
            new Claim("agent_id", credentials.ConnectorId)
        };

        var securityKey = new ECDsaSecurityKey(ecdsa);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        // TODO define authority and audience
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = signingCredentials,
            Audience = "http://localhost",
            Issuer = "http://localhost",
        };
        
        var jwt = handler.CreateToken(tokenDescriptor);

        return Task.FromResult<string?>(jwt);
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
