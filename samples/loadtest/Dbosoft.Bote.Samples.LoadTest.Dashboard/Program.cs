using Dbosoft.Bote.Samples.LoadTest.Dashboard;
using Dbosoft.Bote.Samples.LoadTest.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(c => c.AddSimpleConsole(o => o.SingleLine = true));
builder.Services.AddSingleton<MetricsHub>();
builder.Services.AddHostedService<StorageMetricsCollector>();

var app = builder.Build();

app.MapGet("/", () => Results.Content(DashboardPage.Html, "text/html"));
app.MapGet("/metrics", (MetricsHub hub) => Results.Json(hub.MetricsSnapshot()));

// Edge load generators push here (one report per generator, keyed by ReporterId).
app.MapPost("/report", (EdgeReport report, MetricsHub hub) =>
{
    hub.StoreEdgeReport(report);
    return Results.Ok();
});

// Controller replicas push here (one report per replica, keyed by ReporterId).
app.MapPost("/controller-report", (ControllerReport report, MetricsHub hub) =>
{
    hub.StoreControllerReport(report);
    return Results.Ok();
});

app.Run();
