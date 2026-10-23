using System.Diagnostics;
using AwesomeAssertions;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class BoteTracingTests
{
    private const string ClientTrace = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void A_client_message_continues_the_clients_trace_on_Service_Bus()
    {
        var properties = new Dictionary<string, object>();

        using (BoteTracing.ContinueClientTrace(new Dictionary<string, string?> { ["traceparent"] = ClientTrace }, "SendMessage"))
            BoteTracing.Stamp(properties);

        var id = properties["Diagnostic-Id"].Should().BeOfType<string>().Subject;
        ActivityContext.Parse(id, null).TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
        Activity.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a trace")]
    public void Without_a_trace_there_is_nothing_to_continue(string? traceParent)
    {
        var headers = new Dictionary<string, string?>();
        if (traceParent is not null) headers["traceparent"] = traceParent;

        BoteTracing.ContinueClientTrace(headers, "SendMessage").Should().BeNull();
    }

    [Fact]
    public void A_message_to_a_client_carries_the_current_trace_unless_it_has_one()
    {
        using var current = new Activity("trigger").SetIdFormat(ActivityIdFormat.W3C).Start();
        var fresh = new Dictionary<string, string?>();
        var traced = new Dictionary<string, string?> { ["traceparent"] = ClientTrace };

        BoteTracing.Stamp(fresh);
        BoteTracing.Stamp(traced);

        fresh["traceparent"].Should().Be(current.Id);
        traced["traceparent"].Should().Be(ClientTrace);
    }
}
