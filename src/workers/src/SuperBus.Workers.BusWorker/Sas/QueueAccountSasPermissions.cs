using System;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// <see cref="QueueAccountSasPermissions"/> contains the list of
/// permissions that can be set for a file's access policy.  Use
/// <see cref="QueueSasBuilder.SetPermissions(QueueAccountSasPermissions)"/>
/// to set the permissions on the <see cref="QueueSasBuilder"/>.
/// </summary>
[Flags]
public enum QueueAccountSasPermissions
{
    /// <summary>
    /// Indicates that Read is permitted.
    /// </summary>
    Read = 1,

    /// <summary>
    /// Indicates that Write is permitted.
    /// </summary>
    Write = 2,

    /// <summary>
    /// Indicates that Delete is permitted.
    /// </summary>
    Delete = 4,

    /// <summary>
    /// Indicates that List is permitted.
    /// </summary>
    List = 8,

    /// <summary>
    /// Indicates that Add is permitted.
    /// </summary>
    Add = 16,

    /// <summary>
    /// Indicates that Update is permitted.
    /// </summary>
    Update = 32,

    /// <summary>
    /// Indicates that Delete is permitted.
    /// </summary>
    Process = 64,

    /// <summary>
    /// Indicates that all permissions are set.
    /// </summary>
    All = ~0

}