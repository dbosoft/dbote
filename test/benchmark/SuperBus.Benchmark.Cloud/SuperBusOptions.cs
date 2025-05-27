namespace SuperBus.Benchmark.Cloud;

internal class SuperBusOptions
{
    public string Connection { get; set; } = null!;

    public string QueuePrefix { get; set; } = "superbus";
}
