using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SuperBus.Workers.BusWorker;

/// <summary>
/// A <see cref="SharedKeyBusCredential"/> is a credential backed by
/// a Bus Account name and one of its access keys.
/// </summary>
public class SharedKeyBusCredential
{
    /// <summary>
    /// Gets the name of the Bus Account.
    /// </summary>
    public string AccountName { get; }

    /// <summary>
    /// The value of a Storage Account access key.
    /// </summary>
    private byte[] _accountKeyValue;

    /// <summary>
    /// Gets the value of a Bus Account access key.
    /// </summary>
    private byte[] AccountKeyValue
    {
        get => Volatile.Read(ref _accountKeyValue);
        set => Volatile.Write(ref _accountKeyValue, value);
    }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="SharedKeyBusCredential"/> class.
    /// </summary>
    /// <param name="accountName">The name of the Bus Account.</param>
    /// <param name="accountKey">A Bus Account access key.</param>
    public SharedKeyBusCredential(
        string accountName,
        string accountKey)
    {
        AccountName = accountName;
        SetAccountKey(accountKey);
    }

    /// <summary>
    /// Update the Bus Account's access key.  This intended to be used
    /// when you've regenerated your Bus Account's access keys and want
    /// to update long lived clients.
    /// </summary>
    /// <param name="accountKey">A Storage Account access key.</param>
    public void SetAccountKey(string accountKey) =>
        AccountKeyValue = Convert.FromBase64String(accountKey);

    /// <summary>
    /// Generates a base-64 hash signature string for an HTTP request or
    /// for a SAS.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <returns>The signed message.</returns>
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once IdentifierTypo
    internal string? ComputeHMACSHA256(string message)
    {
        return Convert.ToBase64String(HMACSHA256.HashData(AccountKeyValue, Encoding.UTF8.GetBytes(message)));

    }

    /// <summary>
    /// Generates a base-64 hash signature string for an HTTP request or
    /// for a SAS.
    /// </summary>
    /// <param name="credential">The credential.</param>
    /// <param name="message">The message to sign.</param>
    /// <returns>The signed message.</returns>
    protected static string? ComputeSasSignature(SharedKeyBusCredential credential, string message) =>
        credential.ComputeHMACSHA256(message);
}