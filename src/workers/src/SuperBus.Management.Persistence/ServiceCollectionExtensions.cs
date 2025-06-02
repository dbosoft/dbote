using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dbosoft.Azure.TableStorage;
using Microsoft.Extensions.DependencyInjection;
using SuperBus.Management.Persistence.Repositories;
using SuperBus.Management.Persistence.Services;

namespace SuperBus.Management.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTableStorage(
        this IServiceCollection services,
        string? storagePrefix = null)
    {
        services.AddSingleton<ITableNameFormatter>(new TableNameFormatter(storagePrefix ?? "superbus"));

        services.AddSingleton<ICloudTableProvider, CloudTableProvider>();
        services.AddSingleton<ICloudTableClientProvider, CloudTableClientProvider>();
        services.AddSingleton<IConnectorRepository, ConnectorRepository>();

        return services;
    }
}
