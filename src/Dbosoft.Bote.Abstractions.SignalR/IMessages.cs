namespace Dbosoft.Bote.Abstractions.SignalR;

public interface IMessages
{
    public Task NewMessage(string message);
}
