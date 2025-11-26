using System.Diagnostics;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Runtime.Local;
using MakerProjectsDemo.Infrastructure;
using MakerProjectsDemo.Projects;

var builder = WebApplication.CreateBuilder(args);

ConfigureConfiguration(builder.Configuration);

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.AddAevatarAgentSystem(options => options.UseLocalRuntime());
builder.Services.AddMEAI();

builder.Services.AddSingleton<IMakerRunRecorder, MakerFileRecorder>();
builder.Services.AddSingleton<IMakerProjectContextAccessor, MakerProjectContextAccessor>();
builder.Services.AddSingleton<MakerTimelineHub>();
builder.Services.AddSingleton<IMakerChildLinker, SelfTypeMakerChildLinker>();
builder.Services.AddSingleton<IMakerProjectRunner>(sp =>
    new MakerProjectRunner(
        MakerProjectDefinitions.CreateBaziSpec(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<MakerTimelineHub>(),
        sp.GetRequiredService<IMakerProjectContextAccessor>(),
        sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<IMakerProjectRunner>(sp =>
    new MakerProjectRunner(
        MakerProjectDefinitions.CreatePaperSpec(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<MakerTimelineHub>(),
        sp.GetRequiredService<IMakerProjectContextAccessor>(),
        sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<MakerProjectsService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.Services.AddSingleton<ILoggerProvider, TimelineLoggerProvider>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/projects", (MakerProjectsService service) => Results.Json(service.GetProjects()));
app.MapPost("/api/projects/{projectId}/run", async (string projectId, MakerProjectsService service, CancellationToken ct) =>
{
    var response = await service.StartRunAsync(projectId, ct);
    return Results.Json(response);
});
app.MapGet("/api/projects/{projectId}/status", (string projectId, MakerProjectsService service) =>
    Results.Json(service.GetStatus(projectId)));
app.MapGet("/api/projects/{projectId}/snapshot", (string projectId, MakerProjectsService service) =>
    Results.Json(service.GetSnapshot(projectId)));
app.MapGet("/api/projects/{projectId}/timeline", (string projectId, MakerProjectsService service) =>
    Results.Json(service.GetTimeline(projectId)));

app.Run();

_ = Task.Run(async () =>
{
    await Task.Delay(1500);
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "http://localhost:5001",
            UseShellExecute = true
        });
    }
    catch
    {
    }
});

static void ConfigureConfiguration(ConfigurationManager config)
{
    config
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();
}

