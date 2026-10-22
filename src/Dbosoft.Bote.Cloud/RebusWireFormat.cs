using System.Text.Json;

namespace Dbosoft.Bote.Cloud;

/// <summary>
/// The parts of the Rebus wire format the edge clients depend on. The clients run Rebus over
/// the bote transport, so a cloud side without Rebus has to read and write messages the way
/// Rebus does: the names below are Rebus' own header names and values.
/// </summary>
public static class RebusWireFormat
{
    public const string MessageIdHeader = "rbs2-msg-id";
    public const string TypeHeader = "rbs2-msg-type";
    public const string ContentTypeHeader = "rbs2-content-type";
    public const string CorrelationIdHeader = "rbs2-corr-id";
    public const string CorrelationSequenceHeader = "rbs2-corr-seq";
    public const string SentTimeHeader = "rbs2-senttime";
    public const string IntentHeader = "rbs2-intent";
    public const string ReturnAddressHeader = "rbs2-return-address";

    public const string PointToPointIntent = "p2p";

    /// <summary>The content type of Rebus' System.Text.Json serializer.</summary>
    public const string JsonContentType = "application/json;charset=utf-8";

    /// <summary>
    /// The serializer settings of Rebus' System.Text.Json serializer when it is configured
    /// without options (<c>s.UseSystemTextJson()</c>), which is what the bote clients use.
    /// </summary>
    public static JsonSerializerOptions DefaultSerializerOptions { get; } = new();

    /// <summary>
    /// The type name Rebus writes into <see cref="TypeHeader"/>: the full type name with the
    /// simple assembly name, generic arguments in the same form.
    /// </summary>
    public static string TypeName(Type type)
    {
        if (!type.IsGenericType || type.IsGenericTypeDefinition)
            return $"{type.FullName}, {type.Assembly.GetName().Name}";

        var arguments = string.Join(",", type.GetGenericArguments().Select(a => $"[{TypeName(a)}]"));
        return $"{type.GetGenericTypeDefinition().FullName}[{arguments}], {type.Assembly.GetName().Name}";
    }
}
