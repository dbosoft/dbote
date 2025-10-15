namespace Dbosoft.Bote.Models
{
    public record InboxConnection(Uri Endpoint, string Path, DateTimeOffset ExpiresOn, string Token);
}