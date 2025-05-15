using System;

namespace SuperBus.Workers.BusWorker.Sas;

/// <summary>
/// <see cref="QueueSasPermissions"/> contains the list of
/// permissions that can be set for a queue's access policy.  Use
/// <see cref="QueueSasBuilder.SetPermissions(QueueSasPermissions)"/>
/// to set the permissions on the <see cref="QueueSasBuilder"/>.
/// </summary>
[Flags]
public enum QueueSasPermissions
{

    /// <summary>
    /// Indicates that Processing is permitted.
    /// </summary>
    Process = 8,

    /// <summary>
    /// Indicates that all permissions are set.
    /// </summary>
    All = ~0
}