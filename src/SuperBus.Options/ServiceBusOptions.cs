using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Options;

public class ServiceBusOptions
{
    public string Connection { get; set; } = null!;

    public ServiceBusQueuesOptions Queues { get; set; } = new();
}
