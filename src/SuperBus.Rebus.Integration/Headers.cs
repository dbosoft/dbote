using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Rebus.Integration;

public static class Headers
{
    public static readonly string TenantId = "superbus-tenant-id";

    public static readonly string AgentId = "superbus-agent-id";

    public static readonly string Signature = "superbus-signature";
}
