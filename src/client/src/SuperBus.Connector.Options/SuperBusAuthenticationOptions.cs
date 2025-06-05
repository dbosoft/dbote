using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Connector.Options;

public class SuperBusAuthenticationOptions
{

    public SuperBusAuthenticationType AuthenticationType { get; set; }

    public string? SigningKey { get; set; }
}
