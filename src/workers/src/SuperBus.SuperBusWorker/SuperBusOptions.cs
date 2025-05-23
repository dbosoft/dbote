using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.SuperBusWorker;

public class SuperBusOptions
{
    public IList<SuperBusTenantOptions> Tenants { get; set; } = [];

    public string QueuePrefix { get; set; } = null!;
}

public record SuperBusTenantOptions
{
    public string AgentId { get; set; }

    public string TenantId { get; set; }

    public string SigningKey { get; set; }
}
