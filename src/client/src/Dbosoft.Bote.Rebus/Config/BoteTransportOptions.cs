// Based on https://github.com/rebus-org/Rebus.AzureQueues
// Copyright (c) 2019 Mogens Heller Grabe
// Licensed under MIT license https://github.com/rebus-org/Rebus.AzureQueues/blob/master/LICENSE.md

using Rebus.Bus;

namespace Dbosoft.Bote.Rebus.Config;

/// <summary>
/// Options to configure the behavior of the Bote transport for Rebus
/// </summary>
public class BoteTransportOptions
{
    /// <summary>
    /// Configures whether Bote uses the builtin support for deferred messages which is provided
    /// by Azure Storage Queues.
    /// Defaults to <code>true</code>. When set to <code>false</code>, please remember to register
    /// a timeout manager, or configure another endpoint as a timeout manager, if you intend to
    /// <see cref="IBus.Defer"/> or <see cref="IBus.DeferLocal"/> messages.
    /// </summary>
    public bool UseNativeDeferredMessages { get; set; } = true;

    /// <summary>
    /// Configures how many messages to prefetch. Valid values are null, 0, ... 32
    /// </summary>
    // TODO Not supported. Do we want prefetch?
    public int? Prefetch { get; set; } = null;

    /// <summary>
    /// Optional. Specifies the new visibility timeout value, in seconds, relative to server time. The default value is 5 minutes.
    /// A specified value must be larger than or equal to 1 second, and cannot be larger than 7 days, or larger than 2 hours on REST protocol versions prior to version 2011-08-18. The
    /// visibility timeout of a message can be set to a value later than the expiry time.
    /// </summary>
    /// <remarks>
    /// See https://docs.microsoft.com/en-us/rest/api/storageservices/get-messages for more
    /// </remarks>
    public TimeSpan InitialVisibilityDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enables automatic peek lock renewal. Only enable this if you intend on handling messages for a long, long time, and
    /// DON'T intend on handling messages quickly - it will have an impact on message receive, so only enable it if you
    /// need it. You should usually strive after keeping message processing times low, much lower than the 5-minute lease
    /// you get with Azure Queue. Will not work with prefetch of messages.
    /// </summary>
    public bool AutomaticPeekLockRenewalEnabled { get; set; } = false;
}
