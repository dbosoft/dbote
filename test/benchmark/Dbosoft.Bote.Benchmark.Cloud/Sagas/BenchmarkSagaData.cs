using Rebus.Sagas;

namespace Dbosoft.Bote.Benchmark.Cloud.Sagas;

public class BenchmarkSagaData : SagaData
{
    public Guid RequestId { get; set; }

    public string RequestSenderAddress { get; set; }

    public string RequestMessageId { get; set; }
}
