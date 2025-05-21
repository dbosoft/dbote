using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Transport.Abstractions;

public interface IIncoming
{
    public Task NewMessage(string id);
}
