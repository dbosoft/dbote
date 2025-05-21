using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Transport.Abstractions;

public class SuperBusMessage
{
    public IDictionary<string, string> Headers { get; set; }

    public string Body { get; set; }
}
