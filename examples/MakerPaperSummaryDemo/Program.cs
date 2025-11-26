using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Runtime.Local;
using MakerPaperSummaryDemo;
using System.Diagnostics;
using MakerBaziDemo;

var builder = WebApplication.CreateBuilder(args);

ConfigureConfiguration(builder.Configuration);

var timelineStore = new MakerTimelineStore();
builder.Services.AddSingleton(timelineStore);
builder.Services.AddSingleton<PaperSummaryOrchestrator>();

// Reuse generic Maker configuration or define new one? 
// We use the same "LLMProviders" section.
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());
builder.Services.AddMEAI();

// Register custom agents
builder.Services.AddTransient<PaperSummaryTaskAgent>();
builder.Services.AddTransient<PaperSummaryWorkerAgent>();

builder.Services.AddSingleton<IMakerRunRecorder, MakerFileRecorder>();
// Register custom child linker
builder.Services.AddSingleton<IMakerChildLinker, PaperSummaryChildLinker>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new TimelineLoggerProvider(timelineStore));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/run", async (PaperSummaryOrchestrator orchestrator, CancellationToken ct) =>
{
    var response = await orchestrator.StartRunAsync(ct);
    return Results.Json(response);
});

app.MapGet("/api/status", (PaperSummaryOrchestrator orchestrator) => Results.Json(orchestrator.GetStatus()));
app.MapGet("/api/snapshot", (PaperSummaryOrchestrator orchestrator) => Results.Json(orchestrator.GetSnapshot()));
app.MapGet("/api/timeline", (MakerTimelineStore store) => Results.Json(store.GetEvents()));

app.Run();

// Open browser automatically (Fire and forget)
_ = Task.Run(async () =>
{
    await Task.Delay(1500); // Wait for server to warm up
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "http://localhost:5001",
            UseShellExecute = true
        });
    }
    catch (Exception)
    {
        // Ignore if fails (e.g. no GUI)
    }
});

static void ConfigureConfiguration(ConfigurationManager config)
{
    config
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
}

