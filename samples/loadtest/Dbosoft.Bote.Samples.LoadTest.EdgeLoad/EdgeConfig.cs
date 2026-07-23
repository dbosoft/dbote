namespace Dbosoft.Bote.Samples.LoadTest.EdgeLoad;

public sealed record EdgeConfig(
    int EdgeCount,
    string Tenant,
    string Endpoint,
    string IdpUrl,
    string ControllerUrl,
    string Authority,
    string Scope,
    string CloudQueue,
    string ClientsQueue,
    string ErrorQueue,
    int SmallIntervalMs,
    int SmallBytes,
    int LargeEveryN,
    int LargeBytes,
    int StartupStaggerMs,
    // Noisy-neighbour scenario: the first NoisyEdges clients send at NoisyIntervalMs (flooding);
    // the rest are well-behaved at SmallIntervalMs. Reported separately so the dashboard can show
    // whether the noisy client hurts the quiet ones' latency.
    int NoisyEdges,
    int NoisyIntervalMs);
