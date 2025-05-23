using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Rebus.Config;

public class SuperBusCredentials
{
    public string TenantId { get; set; }

    public string ConnectorId { get; set; }

    public string SigningKey { get; set; }
}
