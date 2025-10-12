namespace SuperBus.Benchmark.Messages;

public class BenchmarkResponse
{
    public Guid RequestId { get; set; }

    public string Message { get; set; } = "";
}
