using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace SuperBus.Workers.BusWorker;

public static class Constants
{

    /// <summary>
    /// Gets the default service version to use when building shared access
    /// signatures.
    /// </summary>
    public const string DefaultSasVersion = "2022-11-02";
    public const string PercentSign = "%";
    public const string EncodedPercentSign = "%25";
    public const string QueryDelimiter = "?";
    public const string PathBackSlashDelimiter = "/";


    // SASTimeFormat represents the format of a SAS start or expiry time. Use it when formatting/parsing a time.Time.
    // ISO 8601 uses "yyyy'-'MM'-'dd'T'HH':'mm':'ss"
    public const string SasTimeFormatSeconds = "yyyy-MM-ddTHH:mm:ssZ";
    public const string SasTimeFormatSubSeconds = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    public const string SasTimeFormatMinutes = "yyyy-MM-ddTHH:mmZ";
    public const string SasTimeFormatDays = "yyyy-MM-dd";


    /// <summary>
    /// Sas constant values.
    /// </summary>
    internal static class Sas
    {


        internal static class Permissions
        {
            public const char Process = 'p';
        }

        internal static class Parameters
        {
            public const string Version = "sv";
            public const string VersionUpper = "SV";
            public const string Services = "ss";
            public const string ServicesUpper = "SS";
            public const string ResourceTypes = "srt";
            public const string ResourceTypesUpper = "SRT";
            public const string Protocol = "spr";
            public const string ProtocolUpper = "SPR";
            public const string StartTime = "st";
            public const string StartTimeUpper = "ST";
            public const string ExpiryTime = "se";
            public const string ExpiryTimeUpper = "SE";
            public const string Identifier = "si";
            public const string IdentifierUpper = "SI";
            public const string Resource = "sr";
            public const string ResourceUpper = "SR";
            public const string Permissions = "sp";
            public const string PermissionsUpper = "SP";
            public const string Signature = "sig";
            public const string SignatureUpper = "SIG";
        }


        public static readonly List<char> ValidPermissionsInOrder = new List<char>
        {
            Sas.Permissions.Process,
        };

        /// <summary>
        /// List of ports used for path style addressing.
        /// Copied from Microsoft.Azure.Storage.Core.Util
        /// </summary>
        internal static readonly int[] PathStylePorts = { 10000, 10001, 10002, 10003, 10004, 10100, 10101, 10102, 10103, 10104, 11000, 11001, 11002, 11003, 11004, 11100, 11101, 11102, 11103, 11104 };
    }

}

/// <summary>
/// Extension methods used to manipulate URIs.
/// </summary>
internal static class UriExtensions
{
    /// <summary>
    /// Append a segment to a URIs path.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <param name="segment">The relative segment to append.</param>
    /// <returns>The combined URI.</returns>
    public static Uri AppendToPath(this Uri uri, string segment)
    {
        var builder = new UriBuilder(uri);
        var path = builder.Path;
        var seperator = (path.Length == 0 || path[path.Length - 1] != '/') ? "/" : "";
        // In URLs, the percent sign is used to encode special characters, so if the segment
        // has a percent sign in their URL path, we have to encode it before adding it to the path
        segment = segment.Replace(Constants.PercentSign, Constants.EncodedPercentSign);
        builder.Path += seperator + segment;
        return builder.Uri;
    }

    /// <summary>
    /// Get the (already encoded) query parameters on a URI.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <returns>Dictionary mapping query parameters to values.</returns>
    public static IDictionary<string, string> GetQueryParameters(this Uri uri)
    {
        var parameters = new Dictionary<string, string>();
        var query = uri.Query ?? "";
        if (!string.IsNullOrEmpty(query))
        {
            if (query.StartsWith("?", true, CultureInfo.InvariantCulture))
            {
                query = query.Substring(1);
            }
            foreach (var param in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = param.Split(new[] { '=' }, 2);
                var name = WebUtility.UrlDecode(parts[0]);
                if (parts.Length == 1)
                {
                    parameters.Add(name, default);
                }
                else
                {
                    parameters.Add(name, WebUtility.UrlDecode(parts[1]));
                }
            }
        }
        return parameters;
    }

    /// <summary>
    /// Get the account name from the domain portion of a Uri.
    /// </summary>
    /// <param name="uri">The Uri.</param>
    /// <param name="serviceSubDomain">The service subdomain used to validate that the
    /// domain is in the expected format. This should be "blob" for blobs, "file" for files,
    /// "queue" for queues, "blob" and "dfs" for datalake.</param>
    /// <returns>Account name or null if not able to be parsed.</returns>
    public static string GetAccountNameFromDomain(this Uri uri, string serviceSubDomain) =>
        GetAccountNameFromDomain(uri.Host, serviceSubDomain);

    /// <summary>
    /// Get the account name from the host.
    /// </summary>
    /// <param name="host">Host.</param>
    /// <param name="serviceSubDomain">The service subdomain used to validate that the
    /// domain is in the expected format. This should be "blob" for blobs, "file" for files,
    /// "queue" for queues, "blob" and "dfs" for datalake.</param>
    /// <returns>Account name or null if not able to be parsed.</returns>
    public static string GetAccountNameFromDomain(string host, string serviceSubDomain)
    {
        var accountEndIndex = host.IndexOf(".", StringComparison.InvariantCulture);
        if (accountEndIndex >= 0)
        {
            var serviceStartIndex = host.IndexOf(serviceSubDomain, accountEndIndex, StringComparison.InvariantCulture);
            return serviceStartIndex > -1 ? host[..accountEndIndex] : null;
        }
        return null;
    }

    /// <summary>
    /// If path starts with a slash, remove it
    /// </summary>
    /// <param name="uri">The Uri.</param>
    /// <returns>Sanitized Uri.</returns>
    public static string GetPath(this Uri uri) =>
        (uri.AbsolutePath[0] == '/') ?
            uri.AbsolutePath.Substring(1) :
            uri.AbsolutePath;

    // See remarks at https://docs.microsoft.com/en-us/dotnet/api/system.net.ipaddress.tryparse?view=netframework-4.7.2
    /// <summary>
    /// Check to see if Uri is using IP Endpoint style.
    /// </summary>
    /// <param name="uri">The Uri.</param>
    /// <returns>True if using IP Endpoint style.</returns>
    public static bool IsHostIPEndPointStyle(this Uri uri) =>
        (!string.IsNullOrEmpty(uri.Host) &&
        uri.Host.IndexOf(".", StringComparison.InvariantCulture) >= 0 &&
        IPAddress.TryParse(uri.Host, out _)) ||
        Constants.Sas.PathStylePorts.Contains(uri.Port);

    /// <summary>
    /// Appends a query parameter to the string builder.
    /// </summary>
    /// <param name="sb">string builder instance.</param>
    /// <param name="key">query parameter key.</param>
    /// <param name="value">query parameter value.</param>
    internal static void AppendQueryParameter(this StringBuilder sb, string key, string value) =>
        sb
        .Append(sb.Length > 0 ? "&" : "")
        .Append(key)
        .Append('=')
        .Append(value);
}