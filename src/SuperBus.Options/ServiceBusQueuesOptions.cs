using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Options;

public class ServiceBusQueuesOptions
{
    public string Cloud { get; set; }

    public string Error { get; set; }

    public string Connectors { get; set; }
}
