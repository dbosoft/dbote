namespace Dbosoft.Bote.Rebus.Config;

/// <summary>
/// Whether other clients of this client's tenant hold a connection to the gateway now. Only
/// about the clients this client may send to (and itself); the worker refuses the others.
/// </summary>
/// <remarks>
/// A connected client takes its messages within moments; one that is not gets them when it
/// connects again, until they expire. Presence is a hint for the user, not a delivery guarantee.
/// </remarks>
public sealed class BotePresence
{
    private ISignalRClient? _signalRClient;

    internal void Attach(ISignalRClient signalRClient) => _signalRClient = signalRClient;

    public Task<bool> IsConnected(string clientId) =>
        (_signalRClient ?? throw new InvalidOperationException("The bus using these options has not been started."))
        .IsClientConnected(clientId);
}
