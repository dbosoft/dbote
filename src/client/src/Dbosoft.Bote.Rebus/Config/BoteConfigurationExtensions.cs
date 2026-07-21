using Dbosoft.Bote.Rebus.Subscriptions;
using Dbosoft.Bote.Rebus.Transport;
using Rebus.Config;
using Rebus.Injection;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Retry;
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
        Register(configurer, queueName,
            c => new SignalRClient(endpointUri, credentials,
                c.Get<IPendingMessagesIndicators>(),
                c.Get<IRebusLoggerFactory>(),
                httpClientFactory),
            options);
    }

    /// <summary>
    /// Test seam: configure the Bote transport against a supplied <see cref="ISignalRClient"/>
    /// (e.g. one that hands out storage-queue SAS URIs directly), bypassing the real SignalR
    /// connection. Keeps the full transport/error-handler wiring intact.
    /// </summary>
    internal static void UseBote(
        this StandardConfigurer<ITransport> configurer,
        string queueName,
        ISignalRClient signalRClient,
        BoteTransportOptions? options = null)
    {
        Register(configurer, queueName, _ => signalRClient, options);
    }

    private static void Register(
        StandardConfigurer<ITransport> configurer,
        string queueName,
        Func<IResolutionContext, ISignalRClient> signalRClientFactory,
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
            .Register(signalRClientFactory);

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

        // Replace Rebus' default deadletter handler: poison messages must not be routed
        // back through the SignalR SendMessage whitelist (which would reject the error
        // queue). Instead they are written to the client's local error Storage Queue.
        configurer.OtherService<IErrorQueueWriter>()
            .Register(c => new BoteErrorQueueWriter(c.Get<ISignalRClient>()));

        configurer.OtherService<IErrorHandler>()
            .Register(c => new BoteErrorHandler(
                c.Get<IErrorQueueWriter>(),
                c.Get<IRebusLoggerFactory>()));

        configurer.Register(c => c.Get<BoteTransport>());

    }
}