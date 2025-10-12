using Dbosoft.Bote.Rebus.Integration.Pipeline;
using Rebus.AzureServiceBus.NameFormat;
using Rebus.Config;
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
        string connectorsQueue)
    {
        configurer.Decorate<INameFormatter>(context =>
        {
            var nameFormatter = context.Get<INameFormatter>();
            return new BoteNameFormatter(nameFormatter, connectorsQueue);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepConcatenator(pipeline)
                .OnReceive(new BoteIncomingStep(), PipelineAbsolutePosition.Front)
                .OnSend(new BoteOutgoingStep(), PipelineAbsolutePosition.Front);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepInjector(pipeline)
                .OnSend(
                    new BoteOutgoingConnectorStep(connectorsQueue),
                    PipelineRelativePosition.Before,
                    typeof(SendOutgoingMessageStep));
        });
    }
}
