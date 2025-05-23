using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Samples.Simple.Tenant;

public class SuperBusOptions
{
    public string Endpoint { get; set; }

    public string AgentId { get; set; }

    public string TenantId { get; set; }

    public string SigningKey { get; set; }

    public string QueuePrefix { get; set; }
}
