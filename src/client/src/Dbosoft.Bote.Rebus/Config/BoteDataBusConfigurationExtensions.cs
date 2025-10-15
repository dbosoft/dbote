using Rebus.Config;
using Rebus.DataBus;

namespace Dbosoft.Bote.Rebus.Config;

public static class BoteDataBusConfigurationExtensions
{
    public static void UseBoteDataBus(
        this StandardConfigurer<IDataBusStorage> configurer)
    {
        configurer.Register(c =>
        {
            var signalRClient = c.Get<ISignalRClient>();
            return new BoteDataBus(signalRClient);
        });
    }
}