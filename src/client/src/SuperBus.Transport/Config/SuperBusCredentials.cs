using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Transport.Config;

public class SuperBusCredentials
{
    public string TenantId { get; set; }

    public string AgentId { get; set; }

    public string SigningKey { get; set; }
}
