namespace ServiceBusApiEmulator.Configuration
{
    public class QueueOptions
    {
        public string Name { get; set; } = string.Empty;

        public QueuePropertiesOptions? Properties { get; set; }
    }
}
