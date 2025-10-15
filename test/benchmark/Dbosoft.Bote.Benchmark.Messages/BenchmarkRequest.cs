namespace Dbosoft.Bote.Benchmark.Messages;

public class BenchmarkRequest
{
    public Guid RequestId { get; set; }

    public BenchmarkType Type { get; set; }
}

public enum BenchmarkType
{
    Simple,
    Client,
    Service,
}