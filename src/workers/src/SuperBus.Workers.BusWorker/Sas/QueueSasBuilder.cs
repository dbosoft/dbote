using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// <see cref="QueueSasBuilder"/> is used to generate a Shared Access
/// Signature (SAS) for an Azure Storage queue.
///
/// For more information, see
/// <see href="https://docs.microsoft.com/en-us/rest/api/storageservices/constructing-a-service-sas">
/// Create  a Service SAS</see>.
/// </summary>
public class QueueSasBuilder
{
    /// <summary>
    /// The storage service version to use to authenticate requests made
    /// with this shared access signature, and the service version to use
    /// when handling requests made with this shared access signature.
    /// </summary>
    /// <remarks>
    /// This property has been deprecated and we will always use the latest
    /// storage SAS version of the Storage service supported. This change
    /// does not have any impact on how your application generates or makes
    /// use of SAS tokens.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? Version { get; set; }

    /// <summary>
    /// Optionally specify the time at which the shared access signature
    /// becomes valid.  If omitted when DateTimeOffset.MinValue is used,
    /// start time for this call is assumed to be the time when the
    /// storage service receives the request.
    /// </summary>
    public DateTimeOffset StartsOn { get; set; }

    /// <summary>
    /// The time at which the shared access signature becomes invalid.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </summary>
    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>
    /// The permissions associated with the shared access signature. The
    /// user is restricted to operations allowed by the permissions.
    /// </summary>
    public string? Permissions { get; private set; }

    /// <summary>
    /// An optional unique value up to 64 characters in length that
    /// correlates to an account sas identifier.
    /// </summary>
    public string? Identifier { get; set; }

    /// <summary>
    /// The optional name of the blob being made accessible.
    /// </summary>
    public string? QueueName { get; set; }



    private QueueSasBuilder()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueSasBuilder"/>
    /// class.
    /// </summary>
    /// <param name="permissions">
    /// The permissions associated with the shared access signature.
    /// The user is restricted to operations allowed by the permissions.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </param>
    /// <param name="expiresOn">
    /// The time at which the shared access signature becomes invalid.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </param>
    /// <param name="queueName">Name of the queue.</param>
    public QueueSasBuilder(QueueSasPermissions permissions, DateTimeOffset expiresOn, string queueName)
    {
        ExpiresOn = expiresOn;
        QueueName = queueName;
        SetPermissions(permissions);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueSasBuilder"/>
    /// class.
    /// </summary>
    /// <param name="permissions">
    /// The permissions associated with the shared access signature.
    /// The user is restricted to operations allowed by the permissions.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </param>
    /// <param name="expiresOn">
    /// The time at which the shared access signature becomes invalid.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </param>
    public QueueSasBuilder(QueueAccountSasPermissions permissions, DateTimeOffset expiresOn, string identifier)
    {
        ExpiresOn = expiresOn;
        Identifier = identifier;
        SetPermissions(permissions);
    }

    /// <summary>
    /// Sets the permissions for a queue SAS.
    /// </summary>
    /// <param name="permissions">
    /// <see cref="QueueSasPermissions"/> containing the allowed permissions.
    /// </param>
    public void SetPermissions(QueueSasPermissions permissions)
    {
        Permissions = permissions.ToPermissionsString();
    }

    /// <summary>
    /// Sets the permissions for a queue account level SAS.
    /// </summary>
    /// <param name="permissions">
    /// <see cref="QueueAccountSasPermissions"/> containing the allowed permissions.
    /// </param>
    public void SetPermissions(QueueAccountSasPermissions permissions)
    {
        Permissions = permissions.ToPermissionsString();
    }

    /// <summary>
    /// Sets the permissions for the SAS using a raw permissions string.
    /// </summary>
    /// <param name="rawPermissions">
    /// Raw permissions string for the SAS.
    /// </param>
    /// <param name="normalize">
    /// If the permissions should be validated and correctly ordered.
    /// </param>
    public void SetPermissions(
        string? rawPermissions,
        bool normalize = default)
    {
        if (normalize)
        {
            rawPermissions = SasExtensions.ValidateAndSanitizeRawPermissions(
                permissions: rawPermissions,
                validPermissionsInOrder: ValidPermissionsInOrder);
        }

        Permissions = rawPermissions;
    }

    private static readonly List<char> ValidPermissionsInOrder = new()
    {
        Constants.Sas.Permissions.Process,
    };


    /// <summary>
    /// Use an account's <see cref="SharedKeyBusCredential"/> to sign this
    /// shared access signature values to produce the proper SAS query
    /// parameters for authenticating requests.
    /// </summary>
    /// <param name="sharedKeyCredential">
    /// The bus account's <see cref="SharedKeyBusCredential"/>.
    /// </param>
    /// <returns>
    /// The <see cref="SasQueryParameters"/> used for authenticating
    /// requests.
    /// </returns>
    public SasQueryParameters ToSasQueryParameters(SharedKeyBusCredential sharedKeyCredential)
    {
        sharedKeyCredential = sharedKeyCredential ?? throw Errors.ArgumentNull(nameof(sharedKeyCredential));

        EnsureState();

        var startTime = SasExtensions.FormatTimesForSasSigning(StartsOn);
        var expiryTime = SasExtensions.FormatTimesForSasSigning(ExpiresOn);
        var signature = GenerateSignature(sharedKeyCredential, startTime, expiryTime);

        // String to sign;
        var p = SasQueryParametersInternals.Create(
            version: Version,
            startsOn: StartsOn,
            expiresOn: ExpiresOn,
            identifier: Identifier,
            resource: QueueName,
            permissions: Permissions,
            signature: signature);
        return p;
    }

    private string? GenerateSignature(SharedKeyBusCredential sharedKeyCredential, string startTime, string expiryTime)
    {
        var stringToSign = string.Join("\n",
            Permissions,
            startTime,
            expiryTime,
            GetCanonicalName(sharedKeyCredential.AccountName, QueueName ?? string.Empty),
            Identifier,
            Version);
        return SharedKeyBusCredentialInternals.ComputeSasSignature(sharedKeyCredential, stringToSign);

    }

    public static QueueSasBuilder FromSasQueryParameters(SasQueryParameters parameters)
    {
        return new QueueSasBuilder()
        {
            Version = parameters.Version,
            StartsOn = parameters.StartsOn,
            ExpiresOn = parameters.ExpiresOn,
            Permissions = parameters.Permissions,
            QueueName = parameters.Resource,
            Identifier = parameters.Identifier
        };
    }

    internal bool ValidateSignature(string? signature, params SharedKeyBusCredential[] sharedKeyCredentials)
    {
        if (signature == null)
            return false;

        var startTime = SasExtensions.FormatTimesForSasSigning(StartsOn);
        var expiryTime = SasExtensions.FormatTimesForSasSigning(ExpiresOn);

        return sharedKeyCredentials.Select(credential => 
            GenerateSignature(credential, startTime, expiryTime))
            .Any(calculatedSignature => calculatedSignature != null && calculatedSignature == signature);
    }

    /// <summary>
    /// Computes the canonical name for a queue resource for SAS signing.
    /// </summary>
    /// <param name="account">
    /// Account.
    /// </param>
    /// <param name="queueName">
    /// Name of queue.
    /// </param>
    /// <returns>
    /// Canonical name as a string.
    /// </returns>
    private static string GetCanonicalName(string account, string queueName) =>
        // Queue: "/queue/account/queueName"
        string.Join("", new[] { "/queue/", account, "/", queueName });


    /// <summary>
    /// Ensure the <see cref="QueueSasBuilder"/>'s properties are in a
    /// consistent state.
    /// </summary>
    private void EnsureState()
    {
        if (Identifier == default)
        {
            if (ExpiresOn == default)
            {
                throw Errors.SasMissingData(nameof(ExpiresOn));
            }
            if (string.IsNullOrEmpty(Permissions))
            {
                throw Errors.SasMissingData(nameof(Permissions));
            }
        }

        Version = SasQueryParametersInternals.DefaultSasVersionInternal;
    }

    internal static QueueSasBuilder DeepCopy(QueueSasBuilder originalQueueSasBuilder)
        => new()
        {
            Version = originalQueueSasBuilder.Version,
            StartsOn = originalQueueSasBuilder.StartsOn,
            ExpiresOn = originalQueueSasBuilder.ExpiresOn,
            Permissions = originalQueueSasBuilder.Permissions,
            QueueName = originalQueueSasBuilder.QueueName
        };
}

// These error messages are only used by client libraries and not by the common