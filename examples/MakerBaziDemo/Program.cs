using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using MakerBaziDemo;

var builder = WebApplication.CreateBuilder(args);

ConfigureConfiguration(builder.Configuration);

var timelineStore = new MakerTimelineStore();
builder.Services.AddSingleton(timelineStore);
builder.Services.AddSingleton<MakerDemoOrchestrator>();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());
builder.Services.AddMEAI();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new TimelineLoggerProvider(timelineStore));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/run", async (MakerDemoOrchestrator orchestrator, CancellationToken ct) =>
{
    var response = await orchestrator.StartRunAsync(ct);
    return Results.Json(response);
});

app.MapGet("/api/status", (MakerDemoOrchestrator orchestrator) => Results.Json(orchestrator.GetStatus()));
app.MapGet("/api/snapshot", (MakerDemoOrchestrator orchestrator) => Results.Json(orchestrator.GetSnapshot()));
app.MapGet("/api/timeline", (MakerTimelineStore store) => Results.Json(store.GetEvents()));

app.Run();

static void ConfigureConfiguration(ConfigurationManager config)
{
    config
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
}

