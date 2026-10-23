using System.Diagnostics;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Keeps a message in its sender's trace across the worker. Clients carry the trace in a W3C
/// <c>traceparent</c> header; Service Bus carries it in <c>Diagnostic-Id</c>, which the Functions
/// trigger on the other side continues.
/// </summary>
internal static class BoteTracing
{
    public const string TraceParent = "traceparent";
    public const string DiagnosticId = "Diagnostic-Id";

    /// <summary>
    /// Starts the worker's part of a client's trace, so that its logs and the Service Bus send
    /// belong to it; <c>null</c> when the message carries no trace. Dispose to end it.
    /// </summary>
    public static Activity? ContinueClientTrace(IDictionary<string, string?> headers, string operation)
    {
        if (!headers.TryGetValue(TraceParent, out var traceParent)
            || !ActivityContext.TryParse(traceParent, null, out var parent))
            return null;

        var activity = new Activity(operation);
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
        return activity.Start();
    }

    /// <summary>Names the current trace for the receiver, unless the sender already did.</summary>
    public static void Stamp(IDictionary<string, object> serviceBusProperties)
    {
        if (CurrentId() is { } id)
            serviceBusProperties.TryAdd(DiagnosticId, id);
    }

    /// <inheritdoc cref="Stamp(IDictionary{string, object})"/>
    public static void Stamp(IDictionary<string, string?> clientHeaders)
    {
        if (CurrentId() is { } id)
            clientHeaders.TryAdd(TraceParent, id);
    }

    private static string? CurrentId() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;
}
