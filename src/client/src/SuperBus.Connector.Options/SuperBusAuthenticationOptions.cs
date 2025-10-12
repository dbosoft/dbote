using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Connector.Options;

public class SuperBusAuthenticationOptions
{
    public SuperBusAuthenticationType AuthenticationType { get; set; }

    public string KeyId { get; set; }

    public string? SigningKey { get; set; }

    public string? Authority { get; set; }

    public string? TokenEndpoint { get; set; }

    public string? Scope { get; set; }
}
