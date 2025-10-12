using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.AzureServiceBus.NameFormat;
using SuperBus.Rebus.Integration.Pipeline;

namespace SuperBus.Rebus.Integration;

public static class SuperBusConfigurationExtensions
{
    /// <summary>
    /// Enables the SuperBus multi-tenant support. All messages are required to
    /// have tenant information in the headers.
    /// </summary>
    public static void EnableSuperBus(
        this OptionsConfigurer configurer,
        string connectorsQueue)
    {
        configurer.Decorate<INameFormatter>(context =>
        {
            var nameFormatter = context.Get<INameFormatter>();
            return new SuperBusNameFormatter(nameFormatter, connectorsQueue);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepConcatenator(pipeline)
                .OnReceive(new SuperBusIncomingStep(), PipelineAbsolutePosition.Front)
                .OnSend(new SuperBusOutgoingStep(), PipelineAbsolutePosition.Front);
        });

        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepInjector(pipeline)
                .OnSend(
                    new SuperBusOutgoingConnectorStep(connectorsQueue),
                    PipelineRelativePosition.Before,
                    typeof(SendOutgoingMessageStep));
        });
    }
}
