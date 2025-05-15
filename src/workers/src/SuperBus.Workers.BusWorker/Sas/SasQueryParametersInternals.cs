using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// Helper to access protected static members of SasQueryParameters.
/// </summary>
internal class SasQueryParametersInternals : SasQueryParameters
{
    /// <summary>
    /// Settable internal property to allow different versions in test.
    /// </summary>
    internal static string? DefaultSasVersionInternal { get; set; } = DefaultSasVersion;

    internal new static SasQueryParameters Create(IDictionary<string, string> values) =>
        SasQueryParameters.Create(values);

    internal new static SasQueryParameters Create(
        string? version,
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        string? identifier,
        string? resource,
        string? permissions,
        string? signature) =>
        SasQueryParameters.Create(
            version: version,
            startsOn: startsOn,
            expiresOn: expiresOn,
            identifier: identifier,
            resource: resource,
            permissions: permissions,
            signature: signature);

    public static SasQueryParameters Parse(string queryString)
    {
        var nameValueDictionary = HttpUtility.ParseQueryString(queryString);

        return SasQueryParameters.Create(nameValueDictionary.AllKeys.Where(x=>!string.IsNullOrWhiteSpace(x))
            .ToDictionary(k => k ?? "", k => nameValueDictionary[k] ?? ""));
    }
}