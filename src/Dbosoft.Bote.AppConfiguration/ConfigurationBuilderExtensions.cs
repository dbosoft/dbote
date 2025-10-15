using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Dbosoft.Bote.AppConfiguration;

public static class ConfigurationBuilderExtensions
{
    public static void AddDboteAzureAppConfiguration<T>(
        this T configuration) where T : IConfigurationBuilder, IConfigurationRoot
    {
        var appConfigEndpoint = configuration["dbote:AppConfiguration:Endpoint"];
        var appConfigConnection = configuration["dbote:AppConfiguration:Connection"];
        if (string.IsNullOrEmpty(appConfigEndpoint) && string.IsNullOrEmpty(appConfigConnection))
            return;

        var appConfigEnvironment = configuration["dbote:AppConfiguration:Environment"];
        var appConfigPrefix = configuration["dbote:AppConfiguration:Prefix"];

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

        configuration.Reload();
    }
}
