// Based on https://github.com/rebus-org/Rebus.AzureQueues
// Copyright (c) 2019 Mogens Heller Grabe
// Licensed under MIT license https://github.com/rebus-org/Rebus.AzureQueues/blob/master/LICENSE.md

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Dbosoft.Bote.Rebus.Transport;

internal class MessageLockRenewer
{
    private DateTimeOffset _nextRenewal;
    private DateTimeOffset? _nextVisibleOn;

    public MessageLockRenewer(QueueMessage message)
    {
        MessageId = message.MessageId;
        PopReceipt = message.PopReceipt;
        _nextVisibleOn = message.NextVisibleOn;

        _nextRenewal = GetTimeOfNextRenewal();
    }

    public string MessageId { get; }

    public string PopReceipt { get; private set; }

    public bool IsDue => DateTimeOffset.Now >= _nextRenewal;

    public async Task<Azure.Response<UpdateReceipt>> Renew(QueueClient queueClient)
    {
        // intentionally let exceptions bubble out here, so the caller can log it as a warning
        var response = await queueClient.UpdateMessageAsync(MessageId, PopReceipt, visibilityTimeout: TimeSpan.FromMinutes(5));

        PopReceipt = response.Value.PopReceipt;
        _nextVisibleOn = response.Value.NextVisibleOn;

        _nextRenewal = GetTimeOfNextRenewal();

        return response;
    }

    DateTimeOffset GetTimeOfNextRenewal()
    {
        var remainingTime = LockedUntil - DateTimeOffset.Now;
        var halfOfRemainingTime = TimeSpan.FromMinutes(0.5 * remainingTime.TotalMinutes);

        return DateTimeOffset.Now + halfOfRemainingTime;
    }

    DateTimeOffset LockedUntil => _nextVisibleOn ?? DateTimeOffset.MinValue;
}