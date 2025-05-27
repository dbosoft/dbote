using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Benchmark.Messages;

public class BenchmarkResponse
{
    public Guid RequestId { get; set; }

    public string Message { get; set; } = "";
}
