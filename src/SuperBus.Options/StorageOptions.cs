using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Options;

public class StorageOptions
{
    public string Connection { get; set; } = null!;

    public string Prefix { get; set; } = "superbus";
}
