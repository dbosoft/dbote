// Based on https://github.com/rebus-org/Rebus.AzureServiceBus/blob/master/Rebus.AzureServiceBus/AzureServiceBus/Messages/DefaultMessageConverter.cs
// Copyright (c) 2012-2016 Mogens Heller Grabe
// Licensed under MIT license https://github.com/rebus-org/Rebus.AzureServiceBus/blob/master/LICENSE.md

using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Extensions;
using Rebus.Messages;

namespace Dbosoft.Bote.BoteWorker.Converters;

internal class MessageConverter : IMessageConverter
{
    public BoteMessage ToBote(ServiceBusReceivedMessage serviceBusMessage)
    {
        var applicationProperties = serviceBusMessage.ApplicationProperties;
        // TODO filter headers
        var headers = applicationProperties
            .Where(kvp => kvp.Key != BoteHeaders.TenantId && kvp.Key != BoteHeaders.ClientId)
            .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, Convert.ToString(kvp.Value)))
            .ToDictionary();
            
        headers[Headers.TimeToBeReceived] = serviceBusMessage.TimeToLive.ToString();
        headers[Headers.ContentType] = serviceBusMessage.ContentType;
        headers[Headers.CorrelationId] = serviceBusMessage.CorrelationId;
        headers[Headers.MessageId] = serviceBusMessage.MessageId;


        return new BoteMessage
        {
            Headers = headers,
            // TODO Fix performance https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter#write-raw-json
            // TODO or do we need to support binary data?
            // TODO handle binary data properly
            Body = serviceBusMessage.Body.ToString(),
        };
    }

    public ServiceBusMessage ToServiceBus(BoteMessage boteMessage)
    {
        var message = new ServiceBusMessage(boteMessage.Body);
        var headers = new Dictionary<string, string?>(boteMessage.Headers);

        if (headers.TryGetValue(Headers.TimeToBeReceived, out var timeToBeReceivedStr))
        {
            var timeToBeReceived = TimeSpan.Parse(timeToBeReceivedStr!);
            message.TimeToLive = timeToBeReceived;
            headers.Remove(Headers.TimeToBeReceived);
        }

        if (headers.TryGetValue(Headers.DeferredUntil, out var deferUntilTime))
        {
            var deferUntilDateTimeOffset = deferUntilTime.ToDateTimeOffset();
            message.ScheduledEnqueueTime = deferUntilDateTimeOffset;
            headers.Remove(Headers.DeferredUntil);
        }

        if (headers.TryGetValue(Headers.ContentType, out var contentType))
        {
            message.ContentType = contentType;
        }
        if (headers.TryGetValue(Headers.CorrelationId, out var correlationId))
        {
            message.CorrelationId = correlationId;
        }

        if (headers.TryGetValue(Headers.MessageId, out var messageId))
        {
            message.MessageId = messageId;
        }

        foreach (var header in headers)
        {
            message.ApplicationProperties[header.Key] = header.Value;
        }

        return message;
    }
}
