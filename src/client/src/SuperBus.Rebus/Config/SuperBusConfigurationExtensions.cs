using Rebus.Config;
using Rebus.Logging;
using Rebus.Threading;
using Rebus.Time;
using Rebus.Transport;
using SuperBus.Rebus.Transport;
using SuperBus.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Rebus.Config;

public static class SuperBusConfigurationExtensions
{
    public static void UseSuperBus(
        this StandardConfigurer<ITransport> configurer,
        Uri endpointUri,
        string queueName,
        SuperBusCredentials credentials,
        SuperBusTransportOptions? options = null)
    {
        Register(configurer, queueName, endpointUri, credentials, options);
    }

    private static void Register(
        StandardConfigurer<ITransport> configurer,
        string queueName,
        Uri endpointUri,
        SuperBusCredentials credentials,
        SuperBusTransportOptions? options)
    {
        options ??= new SuperBusTransportOptions();

        configurer.OtherService<IPendingMessagesIndicators>()
            .Register(_ => new PendingMessagesIndicators());

        configurer.OtherService<ISignalRClient>()
            .Register(c =>
            {
                var pendingMessagesIndicator = c.Get<IPendingMessagesIndicators>();
                return new SignalRClient(endpointUri, credentials, pendingMessagesIndicator);
            });

        configurer.OtherService<SuperBusTransport>().Register(c =>
        {
            var pendingMessagesIndicator = c.Get<IPendingMessagesIndicators>();
            var signalRClient = c.Get<ISignalRClient>();
            var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
            var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
            return new SuperBusTransport(
                queueName,
                options,
                signalRClient,
                pendingMessagesIndicator,
                rebusLoggerFactory,
                asyncTaskFactory);
        });

        configurer.Register(c => c.Get<SuperBusTransport>());
    }
}
