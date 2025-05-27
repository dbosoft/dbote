using Rebus.Config;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Threading;
using Rebus.Time;
using Rebus.Timeouts;
using Rebus.Transport;
using SuperBus.Rebus.Transport;
using SuperBus.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.Pipeline.Receive;

namespace SuperBus.Rebus.Config;

public static class SuperBusConfigurationExtensions
{
    private const string TimeoutManagerText =
        """
        A disabled timeout manager was installed as part of the SuperBus configuration, because the transport has native support for deferred messages.

        The SuperBus transport requires the use of the native functionality for deferred messages.
        """;

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

        configurer.OtherService<Options>().Decorate(c =>
        {
            var rebusOptions = c.Get<Options>();
            rebusOptions.ExternalTimeoutManagerAddressOrNull = SuperBusTransport.MagicDeferredMessagesAddress;
            return rebusOptions;
        });

        configurer.OtherService<ITimeoutManager>().Register(_ => new DisabledTimeoutManager(), description: TimeoutManagerText);

        configurer.OtherService<IPipeline>().Decorate(c =>
        {
            var pipeline = c.Get<IPipeline>();

            return new PipelineStepRemover(pipeline)
                .RemoveIncomingStep(s => s.GetType() == typeof(HandleDeferredMessagesStep));
        });


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
