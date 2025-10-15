using Azure.Storage.Blobs;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Integration.Pipeline;
using Rebus.AzureServiceBus.NameFormat;
using Rebus.Config;
using Rebus.DataBus;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace Dbosoft.Bote.Rebus.Integration;

public static class BoteConfigurationExtensions
{
    /// <summary>
    /// Enables the Bote multi-tenant support. All messages are required to
    /// have tenant information in the headers.
    /// </summary>
    public static void EnableBote(
        this OptionsConfigurer configurer,
        string clientsQueue)
    {
        configurer.Decorate<INameFormatter>(context =>
        {
            var nameFormatter = context.Get<INameFormatter>();
            return new BoteNameFormatter(nameFormatter, clientsQueue);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepConcatenator(pipeline)
                .OnReceive(new BoteIncomingTenantValidationStep(), PipelineAbsolutePosition.Front)
                .OnSend(new BoteOutgoingTenantContextStep(), PipelineAbsolutePosition.Front);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepInjector(pipeline)
                .OnSend(
                    new BoteOutgoingValidationsStep(clientsQueue),
                    PipelineRelativePosition.Before,
                    typeof(SendOutgoingMessageStep));
        });
    }

    /// <summary>
    /// Enables Bote DataBus support for transferring large attachments between cloud and clients.
    /// Uses inbox/outbox pattern for bidirectional async file transfers with geo-distribution support.
    /// </summary>
    /// <param name="configurer">The Rebus options configurer</param>
    /// <param name="connectionString">Azure Storage connection string</param>
    /// <param name="outboxContainerName">Name of the cloud outbox container (where cloud writes files to send to clients)</param>
    public static void EnableBoteDataBus(
        this OptionsConfigurer configurer,
        string connectionString,
        string outboxContainerName = BoteStorageConstants.Outbox)
    {
        // Register DataBusIncomingStep to import attachments from cloud inbox
        // Flow: Client uploads to client-outbox → BoteWorker copies to cloud-inbox → Cloud reads from cloud-inbox
        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();
            var dataBusStorage = context.Get<IDataBusStorage>();
            var loggerFactory = context.Get<IRebusLoggerFactory>();

            return new PipelineStepConcatenator(pipeline)
                .OnReceive(new DataBusIncomingStep(dataBusStorage, loggerFactory), 
                    PipelineAbsolutePosition.Front);
        });
        
        // Register DataBusOutgoingStep to copy attachments from cloud storage to cloud outbox
        // Flow: Cloud writes to cloud-outbox → BoteWorker copies to client-inbox → Client reads from client-inbox
        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();
            var dataBusStorage = context.Get<IDataBusStorage>();
            var loggerFactory = context.Get<IRebusLoggerFactory>();
            var blobServiceClient = new BlobServiceClient(connectionString);

            return new PipelineStepInjector(pipeline)
                .OnSend(new DataBusOutgoingStep(dataBusStorage, loggerFactory, 
                        blobServiceClient, outboxContainerName)
                    , PipelineRelativePosition.Before, 
                    typeof(SendOutgoingMessageStep)
                    );
        });
    }
}
