using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Samples.Simple.Cloud;

internal class SuperBusOptions
{
    public string Connection { get; set; } = null!;

    public string? QueuePrefix { get; set; } = "superbus";
}
