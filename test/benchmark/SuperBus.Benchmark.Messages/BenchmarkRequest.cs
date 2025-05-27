using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Benchmark.Messages;

public class BenchmarkRequest
{
    public Guid RequestId { get; set; }

    public BenchmarkType Type { get; set; }
}

public enum BenchmarkType
{
    Simple,
    Connector,
    Service,
}