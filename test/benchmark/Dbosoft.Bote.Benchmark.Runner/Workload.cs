using Dbosoft.Bote.Benchmark.Messages;
using DFrame;
using Rebus;
using Rebus.Bus;

namespace Dbosoft.Bote.Benchmark.Runner;

public class SimpleWorkload(IBus bus) : WorkloadBase(BenchmarkType.Simple, bus);

public class ServiceWorkload(IBus bus) : WorkloadBase(BenchmarkType.Service, bus);

public class ConnectorWorkload(IBus bus) : WorkloadBase(BenchmarkType.Connector, bus);

public abstract class WorkloadBase(BenchmarkType benchmarkType, IBus bus) : Workload
{
    public override async Task ExecuteAsync(WorkloadContext context)
    {
        // TODO add validation of response by passing GUID
        var response = await bus.SendRequest<BenchmarkResponse>(
            new BenchmarkRequest()
            {
                RequestId = Guid.NewGuid(),
                Type = benchmarkType,
            },
            new Dictionary<string, string>()
            {
                ["bote-tenant-id"] = "tenant-a",
            },
            TimeSpan.FromMinutes(1));
    }
}
