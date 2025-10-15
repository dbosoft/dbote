namespace Dbosoft.Bote.Transport.Abstractions;

public interface IIncoming
{
    public Task NewMessage(string id);
}
