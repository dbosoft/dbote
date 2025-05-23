using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Rebus.Integration;

public static class SuperBusHeaders
{
    public static readonly string TenantId = "superbus-tenant-id";

    public static readonly string ConnectorId = "superbus-connector-id";

    public static readonly string Signature = "superbus-signature";
}
