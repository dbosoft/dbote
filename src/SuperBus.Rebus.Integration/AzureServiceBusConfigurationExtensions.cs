using Azure.Core;
using Rebus.Config;
using Rebus.Transport;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Core.Extensions;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace SuperBus.Rebus.Integration;

/// <summary>
/// 
/// </summary>
public static class AzureServiceBusConfigurationExtensions
{
    public static AzureServiceBusTransportClientSettings UseAzureServiceBusAsOneWayClient(
        this StandardConfigurer<ITransport> configurer,
        IConfiguration clientConfiguration)
    {
        if (clientConfiguration is IConfigurationSection section && !string.IsNullOrEmpty(section.Value))
            return configurer.UseAzureServiceBusAsOneWayClient(section.Value);

        var connection = GetFullyQualifiedNamespace(clientConfiguration);
        return configurer.UseAzureServiceBusAsOneWayClient(
            connection,
            GetCredential(clientConfiguration));
    }

    public static AzureServiceBusTransportSettings UseAzureServiceBus(
        this StandardConfigurer<ITransport> configurer,
        IConfiguration clientConfiguration,
        string inputQueueAddress)
    {
        if (clientConfiguration is IConfigurationSection section && !string.IsNullOrEmpty(section.Value))
            return configurer.UseAzureServiceBus(section.Value, inputQueueAddress);

        var connection = GetFullyQualifiedNamespace(clientConfiguration);
        return configurer.UseAzureServiceBus(
            connection,
            inputQueueAddress,
            GetCredential(clientConfiguration));
    }

    private static string GetFullyQualifiedNamespace(
        IConfiguration configuration)
    {
        var fullyQualifiedNamespace = configuration["fullyQualifiedNamespace"];
        if (string.IsNullOrEmpty(fullyQualifiedNamespace))
            throw new ArgumentException(
                "The 'fullyQualifiedNamespace' setting is missing.",
                nameof(configuration));

        var connectionString = $"Endpoint=sb://{fullyQualifiedNamespace};";
        
        var transportType = configuration["transportType"];
        if (!string.IsNullOrEmpty(transportType))
        {
            connectionString += $"TransportType={transportType};";
        }

        return connectionString;
    }

    private static TokenCredential GetCredential(
        IConfiguration configuration)
    {
        if (string.Equals(configuration["credential"], "managedidentity", StringComparison.OrdinalIgnoreCase))
            return new ManagedIdentityCredential();

        var tenantId = configuration["tenantId"];
        if (string.IsNullOrEmpty(tenantId))
            throw new ArgumentException(
                "The configuration must contain either the 'tenantId' or set 'credential' to 'managedidentity'.",
                nameof(configuration));

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = tenantId,
        });
    }
}
