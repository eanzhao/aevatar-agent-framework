using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Maker.V2;
using MakerProjectsDemoV2.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

// Add MEAI LLM infrastructure
builder.Services.AddMEAI();

// Add MAKER V2 - one line, no adapters needed
builder.Services.AddMakerV2(poolSize: 3, temperatureVariance: 0.1f);

// Project service
builder.Services.AddSingleton<MakerProjectService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Static files
app.UseDefaultFiles();
app.UseStaticFiles();

// API endpoints
app.MapGet("/api/projects", (MakerProjectService svc) =>
    Results.Json(svc.GetProjects()));

app.MapPost("/api/projects/{projectId}/run", async (string projectId, MakerProjectService svc, CancellationToken ct) =>
    Results.Json(await svc.StartRunAsync(projectId, ct)));

app.MapGet("/api/projects/{projectId}/status", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetStatus(projectId)));

app.MapGet("/api/projects/{projectId}/snapshot", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetSnapshot(projectId)));

app.MapGet("/api/projects/{projectId}/timeline", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetTimeline(projectId)));

// Files endpoint - list all generated files
app.MapGet("/api/projects/{projectId}/files", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetFiles(projectId)));

// Get file content
app.MapGet("/api/projects/{projectId}/files/{category}/{name}", (string projectId, string category, string name, MakerProjectService svc) =>
{
    var content = svc.GetFileContent(projectId, category, name);
    if (content == null) return Results.NotFound();
    return Results.Text(content, "text/markdown");
});

// SSE endpoint for real-time streaming
app.MapGet("/api/projects/{projectId}/events", async (
    string projectId, 
    MakerProjectService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    
    await foreach (var evt in svc.GetEventStreamAsync(projectId, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

app.Run();
