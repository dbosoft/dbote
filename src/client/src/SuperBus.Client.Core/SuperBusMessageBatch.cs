
using Azure.Messaging.ServiceBus;

namespace SuperBus.Client.Core;

/// <summary>
///   A set of <see cref="SuperBusMessage" /> with size constraints known up-front,
///   intended to be sent to the Queue/Topic as a single batch.
///   A <see cref="SuperBusMessageBatch"/> can be created using
///   <see cref="SuperBusSender.CreateMessageBatchAsync(System.Threading.CancellationToken)"/>.
///   Messages can be added to the batch using the <see cref="TryAddMessage"/> method on the batch.
/// </summary>
///
public sealed class SuperBusMessageBatch : IDisposable
{
    /// <summary>
    ///   The maximum size allowed for the batch, in bytes.  This includes the messages in the batch as
    ///   well as any overhead for the batch itself when sent to the Queue/Topic.
    /// </summary>
    ///
    public long MaxSizeInBytes => _innerBatch.MaxSizeInBytes;

    /// <summary>
    ///   The size of the batch, in bytes, as it will be sent to the Queue/Topic.
    /// </summary>
    ///
    public long SizeInBytes => _innerBatch.SizeInBytes;

    /// <summary>
    ///   The count of messages contained in the batch.
    /// </summary>
    ///
    public int Count => _innerBatch.Count;


    private readonly ServiceBusMessageBatch _innerBatch;


    /// <summary>
    ///   Initializes a new instance of the <see cref="SuperBusMessageBatch"/> class.
    /// </summary>
    ///
    /// <param name="transportBatch">The  transport-specific batch responsible for performing the batch operations.</param>
    /// <param name="clientDiagnostics">The entity scope used for instrumentation.</param>
    ///
    /// <remarks>
    ///   As an internal type, this class performs only basic sanity checks against its arguments.  It
    ///   is assumed that callers are trusted and have performed deep validation.
    ///
    ///   Any parameters passed are assumed to be owned by this instance and safe to mutate or dispose;
    ///   creation of clones or otherwise protecting the parameters is assumed to be the purview of the
    ///   caller.
    /// </remarks>
    ///
    internal SuperBusMessageBatch(ServiceBusMessageBatch servicebusBatch)
    {
        _innerBatch = servicebusBatch;
    }

    /// <summary>
    ///   Attempts to add a message to the batch, ensuring that the size
    ///   of the batch does not exceed its maximum.
    /// </summary>
    ///
    /// <param name="message">The message to attempt to add to the batch.</param>
    ///
    /// <returns><c>true</c> if the message was added; otherwise, <c>false</c>.</returns>
    ///
    /// <remarks>
    ///   When a message is accepted into the batch, changes made to its properties
    ///   will not be reflected in the batch nor will any state transitions be reflected
    ///   to the original instance.
    ///
    ///   Note: Any <see cref="ReadOnlyMemory{T}" />, byte array, or <see cref="BinaryData" />
    ///   instance associated with the event is referenced by the batch and must remain valid and
    ///   unchanged until the batch is disposed.
    /// </remarks>
    ///
    /// <exception cref="InvalidOperationException">
    ///   When a batch is sent, it will be locked for the duration of that operation.  During this time,
    ///   no messages may be added to the batch.  Calling <c>TryAdd</c> while the batch is being sent will
    ///   result in an <see cref="InvalidOperationException" /> until the send has completed.
    /// </exception>
    ///
    /// <exception cref="System.Runtime.Serialization.SerializationException">
    ///   Occurs when the <paramref name="message"/> has a member in its <see cref="SuperBusMessage.ApplicationProperties"/> collection that is an
    ///   unsupported type for serialization.  See the <see cref="SuperBusMessage.ApplicationProperties"/> remarks for details.
    /// </exception>
    ///
    //public bool TryAddMessage(SuperBusMessage message)
    //{
    //   return _innerBatch.TryAddMessage(message.GetServiceBusMessage());
    //}

    /// <summary>
    ///   Performs the task needed to clean up resources used by the <see cref="SuperBusMessageBatch" />.
    /// </summary>
    ///
    public void Dispose()
    {
        _innerBatch.Dispose();
    }

}