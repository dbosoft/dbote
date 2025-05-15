namespace SuperBus.Workers.BusWorker;

/// <summary>
/// This class is added to access protected static methods off of the base class
/// that should not be exposed directly to customers.
/// </summary>
internal class SharedKeyBusCredentialInternals : SharedKeyBusCredential
{
#pragma warning disable IDE0051 // Remove unused private members
    private SharedKeyBusCredentialInternals(string accountName, string accountKey) :
#pragma warning restore IDE0051 // Remove unused private members
        base(accountName, accountKey)
    {
    }

    internal new static string? ComputeSasSignature(SharedKeyBusCredential credential, string message) =>
        SharedKeyBusCredential.ComputeSasSignature(credential, message);
}