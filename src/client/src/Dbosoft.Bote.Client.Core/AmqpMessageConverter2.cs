// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Data;
using System.Runtime.Serialization;
using Azure.Core.Amqp;
using Azure.Core.Shared;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Amqp;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Encoding;
using Microsoft.Azure.Amqp.Framing;
using static Dbosoft.Bote.Client.Core.AmqpAnnotatedMessageConverter;

namespace Dbosoft.Bote.Client.Core
{
    internal class AmqpMessageConverter
    {
        /// <summary>The size, in bytes, to use as a buffer for stream operations.</summary>
        private const int StreamBufferSizeInBytes = 512;

        public static AmqpMessageConverter Default = new AmqpMessageConverter();

        public virtual AmqpMessage BatchSBMessagesAsAmqpMessage(BoteMessage source, bool forceBatch = false)
        {
            //Argument.AssertNotNull(source, nameof(source));
            var batchMessages = new List<AmqpMessage>(1) { SBMessageToAmqpMessage(source) };
            return BuildAmqpBatchFromMessages(batchMessages, forceBatch);
        }

        public virtual AmqpMessage BatchSBMessagesAsAmqpMessage(IReadOnlyCollection<BoteMessage> source, bool forceBatch = false)
        {
            //Argument.AssertNotNull(source, nameof(source));
            return BuildAmqpBatchFromMessage(source, forceBatch);
        }

        /// <summary>
        ///   Builds a batch of <see cref="AmqpMessage" /> from a set of <see cref="ServiceBusMessage" />
        ///   optionally propagating the custom properties.
        /// </summary>
        ///
        /// <param name="source">The set of messages to use as the body of the batch message.</param>
        /// <param name="forceBatch">Set to true to force creating as a batch even when only one message.</param>
        ///
        /// <returns>The batch <see cref="AmqpMessage" /> containing the source messages.</returns>
        ///
        private AmqpMessage BuildAmqpBatchFromMessage(IReadOnlyCollection<BoteMessage> source, bool forceBatch)
        {
            var batchMessages = new List<AmqpMessage>(source.Count);
            foreach (BoteMessage sbMessage in source)
            {
                batchMessages.Add(SBMessageToAmqpMessage(sbMessage));
            }
            return BuildAmqpBatchFromMessages(batchMessages, forceBatch);
        }

        /// <summary>
        ///   Builds a batch <see cref="AmqpMessage" /> from a set of <see cref="AmqpMessage" />.
        /// </summary>
        ///
        /// <param name="source">The set of messages to use as the body of the batch message.</param>
        /// <param name="forceBatch">Set to true to force creating as a batch even when only one message.</param>
        ///
        /// <returns>The batch <see cref="AmqpMessage" /> containing the source messages.</returns>
        ///
        public virtual AmqpMessage BuildAmqpBatchFromMessages(
            IReadOnlyCollection<AmqpMessage> source,
            bool forceBatch)
        {
            AmqpMessage batchEnvelope;
            AmqpMessage firstMessage = null;

            var batchMessages = source.GetEnumerator();
            batchMessages.MoveNext();

            if (source.Count > 0)
            {
                firstMessage = batchMessages.Current;
            }

            if (source.Count == 1 && !forceBatch)
            {
                batchEnvelope = batchMessages.Current.Clone();
            }
            else
            {
                var data = new List<Data>(source.Count);

                foreach (var message in source)
                {
                    message.Batchable = true;
                    using var messageStream = message.ToStream();
                    data.Add(new Data { Value = ReadStreamToArraySegment(messageStream) });
                }
                batchEnvelope = AmqpMessage.Create(data);
                batchEnvelope.MessageFormat = AmqpConstants.AmqpBatchedMessageFormat;
            }

            if ((firstMessage?.Sections & SectionFlag.Properties) > 0)
            {
                if (firstMessage?.Properties.MessageId != null)
                {
                    batchEnvelope.Properties.MessageId = firstMessage.Properties.MessageId;
                }

                if (firstMessage?.Properties.GroupId != null)
                {
                    batchEnvelope.Properties.GroupId = firstMessage.Properties.GroupId;
                }
            }

            //if ((firstMessage?.Sections & SectionFlag.MessageAnnotations) > 0)
            //{
            //    if (firstMessage?.MessageAnnotations.Map[AmqpMessageConstants.PartitionKeyName] != null)
            //    {
            //        batchEnvelope.MessageAnnotations.Map[AmqpMessageConstants.PartitionKeyName] =
            //            firstMessage.MessageAnnotations.Map[AmqpMessageConstants.PartitionKeyName];
            //    }

            //    if (firstMessage?.MessageAnnotations.Map[AmqpMessageConstants.ViaPartitionKeyName] != null)
            //    {
            //        batchEnvelope.MessageAnnotations.Map[AmqpMessageConstants.ViaPartitionKeyName] =
            //            firstMessage.MessageAnnotations.Map[AmqpMessageConstants.ViaPartitionKeyName];
            //    }
            //}

            batchEnvelope.Batchable = true;
            return batchEnvelope;
        }

        /// <summary>
        ///   Converts a stream to an <see cref="ArraySegment{T}" /> representation.
        /// </summary>
        ///
        /// <param name="stream">The stream to read and capture in memory.</param>
        ///
        /// <returns>The <see cref="ArraySegment{T}" /> containing the stream data.</returns>
        ///
        private static ArraySegment<byte> ReadStreamToArraySegment(Stream stream)
        {
            switch (stream)
            {
                case { Length: < 1 }:
                    return default;

                case BufferListStream bufferListStream:
                    return bufferListStream.ReadBytes((int)stream.Length);

                case MemoryStream memStreamSource:
                    {
                        using var memStreamCopy = new MemoryStream((int)(memStreamSource.Length - memStreamSource.Position));
                        memStreamSource.CopyTo(memStreamCopy, StreamBufferSizeInBytes);
                        if (!memStreamCopy.TryGetBuffer(out ArraySegment<byte> segment))
                        {
                            segment = new ArraySegment<byte>(memStreamCopy.ToArray());
                        }
                        return segment;
                    }

                default:
                    {
                        using var memStreamCopy = new MemoryStream(StreamBufferSizeInBytes);
                        stream.CopyTo(memStreamCopy, StreamBufferSizeInBytes);
                        if (!memStreamCopy.TryGetBuffer(out ArraySegment<byte> segment))
                        {
                            segment = new ArraySegment<byte>(memStreamCopy.ToArray());
                        }
                        return segment;
                    }
            }
        }

        public virtual AmqpMessage SBMessageToAmqpMessage(BoteMessage sbMessage)
        {
            var annotatedMessage = sbMessage.AmqpMessage;
            var message = AmqpAnnotatedMessageConverter.ToAmqpMessage(annotatedMessage);

            // Reset the resultant TTL as there is special handling for Service Bus
            if ((message.Sections & SectionFlag.Header) > 0)
                message.Header.Ttl = null;

            // If TTL is set, it is used to calculate AbsoluteExpiryTime and CreationTime
            //TimeSpan ttl = annotatedMessage.GetTimeToLive();
            //if (ttl != TimeSpan.MaxValue)
            //{
            //    message.Header.Ttl = (uint)ttl.TotalMilliseconds;
            //    message.Properties.CreationTime = DateTime.UtcNow;

            //    if (AmqpConstants.MaxAbsoluteExpiryTime - message.Properties.CreationTime.Value > ttl)
            //    {
            //        message.Properties.AbsoluteExpiryTime = message.Properties.CreationTime.Value + ttl;
            //    }
            //    else
            //    {
            //        message.Properties.AbsoluteExpiryTime = AmqpConstants.MaxAbsoluteExpiryTime;
            //    }
            //}
            //else
            //{
            //    if (annotatedMessage.Properties.CreationTime.HasValue)
            //    {
            //        message.Properties.CreationTime = annotatedMessage.Properties.CreationTime.Value.UtcDateTime;
            //    }
            //    if (annotatedMessage.Properties.AbsoluteExpiryTime.HasValue)
            //    {
            //        message.Properties.AbsoluteExpiryTime = annotatedMessage.Properties.AbsoluteExpiryTime.Value.UtcDateTime;
            //    }
            //}

            return message;
        }

        //public virtual ServiceBusReceivedMessage AmqpMessageToSBReceivedMessage(AmqpMessage amqpMessage, bool isPeeked = false)
        //{
        //    AmqpAnnotatedMessage annotatedMessage = AmqpAnnotatedMessageConverter.FromAmqpMessage(amqpMessage);

        //    if (annotatedMessage.HasSection(AmqpMessageSection.Header) && annotatedMessage.Header.DeliveryCount != null)
        //    {
        //        annotatedMessage.Header.DeliveryCount = isPeeked ? annotatedMessage.Header.DeliveryCount : annotatedMessage.Header.DeliveryCount + 1;
        //    }

        //    //if (annotatedMessage.HasSection(AmqpMessageSection.MessageAnnotations) &&
        //    //    annotatedMessage.MessageAnnotations.TryGetValue(AmqpMessageConstants.LockedUntilName, out object lockedUntil))
        //    //{
        //    //    if (lockedUntil is DateTime lockedUntilDateTime && lockedUntilDateTime >= DateTimeOffset.MaxValue.UtcDateTime)
        //    //    {
        //    //        annotatedMessage.MessageAnnotations[AmqpMessageConstants.LockedUntilName] = DateTimeOffset.MaxValue.UtcDateTime;
        //    //    }
        //    //}

        //    ServiceBusReceivedMessage sbMessage = new ServiceBusReceivedMessage(annotatedMessage);

        //    //if (GuidUtilities.TryParseGuidBytes(amqpMessage.DeliveryTag, out Guid lockToken))
        //    //{
        //    //    // lock token
        //    //    sbMessage.LockTokenGuid = lockToken;
        //    //};

        //    amqpMessage.Dispose();

        //    return sbMessage;
        //}


    }
}