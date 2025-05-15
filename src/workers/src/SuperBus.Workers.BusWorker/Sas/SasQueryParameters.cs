using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace SuperBus.Workers.BusWorker.Sas;
/// <summary>
/// A <see cref="SasQueryParameters"/> object represents the components
/// that make up an Azure Storage Shared Access Signature's query
/// parameters.  It includes components used by all Azure Storage resources
/// (Blob Containers, Blobs, Files, and Queues).  You can construct a new instance
/// using the service specific SAS builder types.
/// For more information,
/// <see href="https://docs.microsoft.com/rest/api/storageservices/create-service-sas">
/// Create a service SAS</see>.
/// </summary>
public partial class SasQueryParameters
{
    /// <summary>
    /// The default service version to use for Shared Access Signatures.
    /// </summary>
    public const string DefaultSasVersion = Constants.DefaultSasVersion;

    // sv
    private readonly string? _version;

    // st

    // st as a string

    // se

    // se as a string


    // si
    private readonly string? _identifier;

    // sr
    private readonly string? _resource;

    // sp
    private readonly string? _permissions;

    // sig
    private readonly string? _signature;

    /// <summary>
    /// Gets the storage service version to use to authenticate requests
    /// made with this shared access signature, and the service version to
    /// use when handling requests made with this shared access signature.
    /// </summary>
    public string? Version => _version ?? SasQueryParametersInternals.DefaultSasVersionInternal;


    /// <summary>
    /// Gets the optional time at which the shared access signature becomes
    /// valid.  If omitted, start time for this call is assumed to be the
    /// time when the storage service receives the request.
    /// <see cref="DateTimeOffset.MinValue"/> means not set.
    /// </summary>
    public DateTimeOffset StartsOn { get; }

    internal string? StartsOnString { get; }

    /// <summary>
    /// Gets the time at which the shared access signature becomes invalid.
    /// <see cref="DateTimeOffset.MinValue"/> means not set.
    /// </summary>
    public DateTimeOffset ExpiresOn { get; }

    internal string? ExpiresOnString { get; }

    /// <summary>
    /// Gets the optional unique value up to 64 characters in length that
    /// correlates to an access policy specified for the blob container, queue,
    /// or share.
    /// </summary>
    public string Identifier => _identifier ?? string.Empty;

    /// <summary>
    /// Gets the resources are accessible via the shared access signature.
    /// </summary>
    public string Resource => _resource ?? string.Empty;

    /// <summary>
    /// Gets the permissions associated with the shared access signature.
    /// The user is restricted to operations allowed by the permissions.
    /// This field must be omitted if it has been specified in an
    /// associated stored access policy.
    /// </summary>
    public string Permissions => _permissions ?? string.Empty;

 
    /// <summary>
    /// Gets the string-to-sign, a unique string constructed from the
    /// fields that must be verified in order to authenticate the request.
    /// The signature is an HMAC computed over the string-to-sign and key
    /// using the SHA256 algorithm, and then encoded using Base64 encoding.
    /// </summary>
    public string Signature => _signature ?? string.Empty;

    /// <summary>
    /// Gets empty shared access signature query parameters.
    /// </summary>
    public static SasQueryParameters Empty => new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SasQueryParameters"/> class.
    /// </summary>
    protected SasQueryParameters() { }

    /// <summary>
    /// Creates a new instance of the <see cref="SasQueryParameters"/> type
    /// based on the supplied query parameters <paramref name="values"/>.
    /// All SAS-related query parameters will be removed from
    /// <paramref name="values"/>.
    /// </summary>
    /// <param name="values">URI query parameters</param>
    protected SasQueryParameters(IDictionary<string, string> values)
    {
        // make copy, otherwise we'll get an exception when we remove
        IEnumerable<KeyValuePair<string, string>> kvPairs = values.ToArray();
        foreach (var kv in kvPairs)
        {
            // these are already decoded
            var isSasKey = true;
            switch (kv.Key.ToUpperInvariant())
            {
                case Constants.Sas.Parameters.VersionUpper:
                    _version = kv.Value;
                    break;
                case Constants.Sas.Parameters.StartTimeUpper:
                    StartsOnString = kv.Value;
                    StartsOn = ParseSasTime(kv.Value);
                    break;
                case Constants.Sas.Parameters.ExpiryTimeUpper:
                    ExpiresOnString = kv.Value;
                    ExpiresOn = ParseSasTime(kv.Value);
                    break;
                case Constants.Sas.Parameters.IdentifierUpper:
                    _identifier = kv.Value;
                    break;
                case Constants.Sas.Parameters.ResourceUpper:
                    _resource = kv.Value;
                    break;
                case Constants.Sas.Parameters.PermissionsUpper:
                    _permissions = kv.Value;
                    break;
                case Constants.Sas.Parameters.SignatureUpper:
                    _signature = kv.Value;
                    break;
                // We didn't recognize the query parameter
                default:
                    isSasKey = false;
                    break;
            }

            // Remove the query parameter if it's part of the SAS
            if (isSasKey)
            {
                values.Remove(kv.Key);
            }
        }
    }

    /// <summary>
    /// Creates a new SasQueryParameters instance.
    /// </summary>
    protected SasQueryParameters(
        string? version,
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        string? identifier,
        string? resource,
        string? permissions,
        string? signature)
    {
        _version = version;
        StartsOn = startsOn;
        StartsOnString = startsOn.ToString(Constants.SasTimeFormatSeconds, CultureInfo.InvariantCulture);
        ExpiresOn = expiresOn;
        ExpiresOnString = expiresOn.ToString(Constants.SasTimeFormatSeconds, CultureInfo.InvariantCulture);
        _identifier = identifier;
        _resource = resource;
        _permissions = permissions;
        _signature = signature;
    }



    /// <summary>
    /// Creates a new instance of the <see cref="SasQueryParameters"/> type
    /// based on the supplied query parameters <paramref name="values"/>.
    /// All SAS-related query parameters will be removed from
    /// <paramref name="values"/>.
    /// </summary>
    /// <param name="values">URI query parameters</param>
    protected static SasQueryParameters Create(IDictionary<string, string> values) =>
        new(values);

    /// <summary>
    /// Creates a new SasQueryParameters instance.
    /// </summary>
    protected static SasQueryParameters Create(
        string? version,
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        string? identifier,
        string? resource,
        string? permissions,
        string? signature) =>
        new(
            version: version,
            startsOn: startsOn,
            expiresOn: expiresOn,
            identifier: identifier,
            resource: resource,
            permissions: permissions,
            signature: signature);

    /// <summary>
    /// Convert the SAS query parameters into a URL encoded query string.
    /// </summary>
    /// <returns>
    /// A URL encoded query string representing the SAS.
    /// </returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        AppendProperties(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Builds the query parameter string for the SasQueryParameters instance.
    /// </summary>
    /// <param name="stringBuilder">
    /// StringBuilder instance to add the query params to
    /// </param>
    protected internal void AppendProperties(StringBuilder stringBuilder)
    {
        if (!string.IsNullOrWhiteSpace(Version))
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.Version, Version);
        }


        if (StartsOn != DateTimeOffset.MinValue && StartsOnString != null)
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.StartTime, WebUtility.UrlEncode(StartsOnString));
        }

        if (ExpiresOn != DateTimeOffset.MinValue && ExpiresOnString!= null)
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.ExpiryTime, WebUtility.UrlEncode(ExpiresOnString));
        }


        if (!string.IsNullOrWhiteSpace(Identifier))
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.Identifier, Identifier);
        }

        if (!string.IsNullOrWhiteSpace(Resource))
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.Resource, Resource);
        }

        if (!string.IsNullOrWhiteSpace(Permissions))
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.Permissions, Permissions);
        }

        if (!string.IsNullOrWhiteSpace(Signature))
        {
            stringBuilder.AppendQueryParameter(Constants.Sas.Parameters.Signature, WebUtility.UrlEncode(Signature));
        }
    }

    private static DateTimeOffset ParseSasTime(string? dateTimeString)
    {
        if (string.IsNullOrEmpty(dateTimeString))
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.ParseExact(dateTimeString, SasTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static readonly string[] SasTimeFormats = {
            Constants.SasTimeFormatSeconds,
            Constants.SasTimeFormatSubSeconds,
            Constants.SasTimeFormatMinutes,
            Constants.SasTimeFormatDays
        };

}