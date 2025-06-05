using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.AppConfiguration;

public static class ConfigurationBuilderExtensions
{
    public static void AddSuperBusAzureAppConfiguration<T>(
        this T configuration) where T : IConfigurationBuilder, IConfiguration
    {
        var appConfigEndpoint = configuration["SuperBus:AppConfiguration:Endpoint"];
        var appConfigConnection = configuration["SuperBus:AppConfiguration:Connection"];
        if (string.IsNullOrEmpty(appConfigEndpoint) && string.IsNullOrEmpty(appConfigConnection))
            return;

        var appConfigEnvironment = configuration["SuperBus:AppConfiguration:Environment"];
        var appConfigPrefix = configuration["SuperBus:AppConfiguration:Prefix"];

        if (!string.IsNullOrEmpty(appConfigEndpoint) && !string.IsNullOrEmpty(appConfigConnection))
            throw new InvalidOperationException("Either endpoint or connection must be specified for app config");

        if (string.IsNullOrEmpty(appConfigEnvironment))
            throw new InvalidOperationException("The app config environment is missing");
        
        if (string.IsNullOrEmpty(appConfigPrefix))
            throw new InvalidOperationException("The app config prefix is missing");

        if (!string.IsNullOrEmpty(appConfigEndpoint))
        {
            configuration.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                    .Select($"{appConfigPrefix}:*", appConfigEnvironment);
            });
        }
        else if(!string.IsNullOrEmpty(appConfigConnection))
        {
            configuration.AddAzureAppConfiguration(options =>
            {
                options.Connect(appConfigConnection)
                    .Select($"{appConfigPrefix}:*", appConfigEnvironment);
            });
        }
    }
}
