using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DFrame;
using Rebus;
using Rebus.Bus;
using SuperBus.Benchmark.Messages;

namespace SuperBus.Benchmark.Runner;

public class SimpleWorkload(IBus bus) : WorkloadBase(BenchmarkType.Simple, bus);

public class ServiceWorkload(IBus bus) : WorkloadBase(BenchmarkType.Service, bus);

public class ConnectorWorkload(IBus bus) : WorkloadBase(BenchmarkType.Connector, bus);

public abstract class WorkloadBase(BenchmarkType benchmarkType, IBus bus) : Workload
{
    public override async Task ExecuteAsync(WorkloadContext context)
    {
        var response = await bus.SendRequest<BenchmarkResponse>(
            new BenchmarkRequest()
            {
                RequestId = Guid.NewGuid(),
                Type = benchmarkType,
            },
            new Dictionary<string, string>()
            {
                ["superbus-tenant-id"] = "tenant-a",
            });
    }
}
