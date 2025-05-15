using System.Text;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// Queue enum extensions.
/// </summary>
internal static class QueueExtensions
{
    /// <summary>
    /// Create a permissions string to provide
    /// <see cref="QueueSasBuilder.Permissions"/>.
    /// </summary>
    /// <returns>A permissions string.</returns>
    internal static string? ToPermissionsString(this QueueSasPermissions permissions)
    {
        var sb = new StringBuilder();
        if ((permissions & QueueSasPermissions.Process) == QueueSasPermissions.Process)
        {
            sb.Append(Constants.Sas.Permissions.Process);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Create a permissions string to provide
    /// <see cref="QueueSasBuilder.Permissions"/>.
    /// </summary>
    /// <returns>A permissions string.</returns>
    internal static string ToPermissionsString(this QueueAccountSasPermissions permissions)
    {
        var sb = new StringBuilder();
        //if ((permissions & QueueAccountSasPermissions.Read) == QueueAccountSasPermissions.Read)
        //{
        //    sb.Append(Constants.Sas.Permissions.Read);
        //}
        //if ((permissions & QueueAccountSasPermissions.Write) == QueueAccountSasPermissions.Write)
        //{
        //    sb.Append(Constants.Sas.Permissions.Write);
        //}
        //if ((permissions & QueueAccountSasPermissions.Delete) == QueueAccountSasPermissions.Delete)
        //{
        //    sb.Append(Constants.Sas.Permissions.Delete);
        //}
        //if ((permissions & QueueAccountSasPermissions.List) == QueueAccountSasPermissions.List)
        //{
        //    sb.Append(Constants.Sas.Permissions.List);
        //}
        //if ((permissions & QueueAccountSasPermissions.Add) == QueueAccountSasPermissions.Add)
        //{
        //    sb.Append(Constants.Sas.Permissions.Add);
        //}
        //if ((permissions & QueueAccountSasPermissions.Update) == QueueAccountSasPermissions.Update)
        //{
        //    sb.Append(Constants.Sas.Permissions.Update);
        //}
        if ((permissions & QueueAccountSasPermissions.Process) == QueueAccountSasPermissions.Process)
        {
            sb.Append(Constants.Sas.Permissions.Process);
        }
        return sb.ToString();
    }
}