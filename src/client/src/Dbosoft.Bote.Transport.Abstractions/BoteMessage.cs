namespace Dbosoft.Bote.Transport.Abstractions;

public class BoteMessage
{
    public IDictionary<string, string?> Headers { get; set; } = new Dictionary<string, string?>();

    public string? Body { get; set; }
}
