using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// Extension methods for Sas.
/// </summary>
internal static class SasExtensions
{

    /// <summary>
    /// FormatTimesForSASSigning converts a time.Time to a snapshotTimeFormat string suitable for a
    /// SASField's StartTime or ExpiryTime fields. Returns "" if value.IsZero().
    /// </summary>
    /// <param name="time"></param>
    /// <returns></returns>
    internal static string FormatTimesForSasSigning(DateTimeOffset time) =>
        // "yyyy-MM-ddTHH:mm:ssZ"
        time == new DateTimeOffset() ? "" : time.ToString(Constants.SasTimeFormatSeconds, CultureInfo.InvariantCulture);

    /// <summary>
    /// Helper method to add query param key value pairs to StringBuilder
    /// </summary>
    /// <param name="sb">StringBuilder instance</param>
    /// <param name="key">query key</param>
    /// <param name="value">query value</param>
    internal static void AddToBuilder(StringBuilder sb, string key, string value) =>
        sb
            .Append(sb.Length > 0 ? "&" : "")
            .Append(key)
            .Append('=')
            .Append(value);

    internal static string? ValidateAndSanitizeRawPermissions(string? permissions,
        List<char> validPermissionsInOrder)
    {
        if (permissions == null)
        {
            return null;
        }

        // Convert permissions string to lower case.
        permissions = permissions.ToLowerInvariant();

        var validPermissionsSet = new HashSet<char>(validPermissionsInOrder);
        var permissionsSet = new HashSet<char>();

        foreach (var permission in permissions)
        {
            // Check that each permission is a real SAS permission.
            if (!validPermissionsSet.Contains(permission))
            {
                throw new ArgumentException($"{permission} is not a valid SAS permission");
            }

            // Add permission to permissionsSet for re-ordering.
            permissionsSet.Add(permission);
        }

        var stringBuilder = new StringBuilder();

        foreach (var permission in validPermissionsInOrder)
        {
            if (permissionsSet.Contains(permission))
            {
                stringBuilder.Append(permission);
            }
        }

        return stringBuilder.ToString();
    }
}