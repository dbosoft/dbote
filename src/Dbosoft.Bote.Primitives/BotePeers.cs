namespace Dbosoft.Bote.Primitives;

/// <summary>
/// The clients of its own tenant a client may send to. The identity provider grants them in the
/// access token (<see cref="TokenClaim"/>), next to the worker's peer scope: a list of client ids,
/// or <see cref="Everyone"/> for every client of the tenant.
/// </summary>
public sealed class BotePeers
{
    /// <summary>The access token claim listing the clients: a string or an array of strings.</summary>
    public const string TokenClaim = "bote_peers";

    /// <summary>In the list, stands for every client of the tenant.</summary>
    public const string Everyone = "*";

    public static readonly BotePeers None = new(false, []);
    public static readonly BotePeers All = new(true, []);

    private readonly bool _everyone;
    private readonly HashSet<string> _clientIds;

    private BotePeers(bool everyone, HashSet<string> clientIds)
    {
        _everyone = everyone;
        _clientIds = clientIds;
    }

    /// <summary>
    /// The clients a list names. An entry that cannot be a client id is left out: it names no
    /// client the worker would deliver to.
    /// </summary>
    public static BotePeers Of(IEnumerable<string> clientIds)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in clientIds)
        {
            if (id == Everyone)
                return All;
            if (BoteIds.IsPathSegment(id))
                ids.Add(id);
        }

        return ids.Count == 0 ? None : new BotePeers(false, ids);
    }

    public bool IsNone => !_everyone && _clientIds.Count == 0;

    public bool Allows(string clientId) => _everyone || _clientIds.Contains(clientId);

    /// <summary>
    /// One string for a claim of the worker's own SignalR token; the ids are separated by
    /// <c>/</c>, which no client id contains. SignalR hands the claims to the worker as HTTP
    /// headers, so the value must not contain line breaks.
    /// </summary>
    public string Format() => _everyone ? Everyone : string.Join('/', _clientIds.Order(StringComparer.Ordinal));

    /// <summary>Reads what <see cref="Format"/> wrote; nothing is none.</summary>
    public static BotePeers Parse(string? value) =>
        string.IsNullOrEmpty(value) ? None : Of(value.Split('/'));
}
