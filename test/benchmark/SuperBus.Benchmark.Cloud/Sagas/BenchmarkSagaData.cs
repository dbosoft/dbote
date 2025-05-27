using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.Sagas;

namespace SuperBus.Benchmark.Cloud.Sagas;

public class BenchmarkSagaData : SagaData
{
    public Guid RequestId { get; set; }

    public string RequestSenderAddress { get; set; }

    public string RequestMessageId { get; set; }
}
