using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.LoadTest.Messages;

// Edge -> Cloud

/// <summary>An edge system announces itself to the controller.</summary>
public class RegisterEdge
{
    public required string EdgeId { get; set; }
}

/// <summary>A small request (a few KB) exchanged frequently.</summary>
public class SmallRequest
{
    public required Guid Id { get; set; }
    public required string EdgeId { get; set; }
    public required string Payload { get; set; }
}

/// <summary>A large request whose body travels as a DataBus attachment (KB..MB).</summary>
public class LargeRequest
{
    public required Guid Id { get; set; }
    public required string EdgeId { get; set; }
    public required string FileName { get; set; }
    public required DataBusAttachment Data { get; set; }
}

// Cloud -> Edge (targeted)

public class EdgeRegistered
{
    public required string EdgeId { get; set; }
}

public class SmallResponse
{
    public required Guid Id { get; set; }
    public required string Payload { get; set; }
}

public class LargeResponse
{
    public required Guid Id { get; set; }
    public required string FileName { get; set; }
    public required DataBusAttachment Data { get; set; }
}

// Cloud -> Edge (broadcast on a topic, used only for short notifications)

public class Notification
{
    public required long Sequence { get; set; }
    public required string Text { get; set; }
}
