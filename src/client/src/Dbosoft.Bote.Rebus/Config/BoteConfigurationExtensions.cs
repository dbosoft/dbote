using Dbosoft.Bote.Rebus.Subscriptions;
using Dbosoft.Bote.Rebus.Transport;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Subscriptions;
using Rebus.Threading;
using Rebus.Timeouts;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Config;

public static class BoteConfigurationExtensions
{
    private const string TimeoutManagerText =
        """
        A disabled timeout manager was installed as part of the Bote configuration, because the transport has native support for deferred messages.

        The Bote transport requires the use of the native functionality for deferred messages.
        """;

    public static void UseBote(
        this StandardConfigurer<ITransport> configurer,
        Uri endpointUri,
        string queueName,
        BoteCredentials credentials,
        IHttpClientFactory httpClientFactory,
        BoteTransportOptions? options = null)
    {
        Register(configurer, queueName, endpointUri, credentials, httpClientFactory, options);
    }

    private static void Register(
        StandardConfigurer<ITransport> configurer,
        string queueName,
        Uri endpointUri,
        BoteCredentials credentials,
        IHttpClientFactory httpClientFactory,
        BoteTransportOptions? options)
    {
        options ??= new BoteTransportOptions();

        configurer.OtherService<Options>().Decorate(c =>
        {
            var rebusOptions = c.Get<Options>();
            rebusOptions.ExternalTimeoutManagerAddressOrNull = BoteTransport.MagicDeferredMessagesAddress;
            return rebusOptions;
        });

        configurer.OtherService<ITimeoutManager>()
            .Register(_ => new DisabledTimeoutManager(), description: TimeoutManagerText);

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
                var loggerFactory = c.Get<IRebusLoggerFactory>();
                var pendingMessagesIndicator = c.Get<IPendingMessagesIndicators>();
                return new SignalRClient(endpointUri, credentials, pendingMessagesIndicator,
                    loggerFactory,
                    httpClientFactory);
            });

        configurer.OtherService<BoteTransport>().Register(c =>
        {
            var pendingMessagesIndicator = c.Get<IPendingMessagesIndicators>();
            var signalRClient = c.Get<ISignalRClient>();
            var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
            var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
            return new BoteTransport(
                queueName,
                options,
                signalRClient,
                pendingMessagesIndicator,
                rebusLoggerFactory,
                asyncTaskFactory);
        });

        configurer.OtherService<ISubscriptionStorage>().Register(c =>
        {
            var signalRClient = c.Get<ISignalRClient>();
            return new BoteSubscriptionStorage(signalRClient);
        });

        configurer.Register(c => c.Get<BoteTransport>());

    }
}