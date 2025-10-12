namespace SuperBus.Transport.Abstractions;

public class SuperBusMessage
{
    public IDictionary<string, string?> Headers { get; set; } = new Dictionary<string, string?>();

    public string? Body { get; set; }
}
