using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Abstractions.SignalR;

public interface IMessages
{
    public Task NewMessage(string message);
}
