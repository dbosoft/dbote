using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace SuperBus.Rebus.Config;

public class SuperBusCredentials
{
    public string TenantId { get; set; }

    public string ConnectorId { get; set; }

    public SecurityKey SigningKey { get; set; }

    public string Authority { get; set; }

    public string TokenEndpoint { get; set; }

    public string Scope { get; set; }
}
