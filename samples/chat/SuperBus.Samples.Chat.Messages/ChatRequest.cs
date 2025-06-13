using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Samples.Chat.Messages;

public class ChatRequest
{
    public required Guid Id { get; set; }

    public required string Message { get; set; }
}
