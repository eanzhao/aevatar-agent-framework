using System.Text;
using AevatarKit;
using AevatarKit.Core.Serialization;
using AevatarKit.Runtime.DependencyInjection;
using AevatarKit.Runtime.Run;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAevatarKitRuntime();

var app = builder.Build();

// ------------------------------
// Static frontend (no build step)
// ------------------------------
// Prefer serving from aevatar-kit/frontend (source) so `dotnet run` works immediately.
// Fallback to wwwroot if present (e.g., in publish scenarios).
var frontendPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../frontend"));
if (Directory.Exists(frontendPath))
{
    var frontendProvider = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendProvider });
}

app.UseDefaultFiles();
app.UseStaticFiles();

// ------------------------------
// API: Start run
// ------------------------------
app.MapPost("/api/runs", async (HttpRequest request, IRunRegistry registry, IRunEngine engine) =>
{
    var body = await ReadBodyAsStringAsync(request).ConfigureAwait(false);
    var start = ProtobufJson.Parse<RunStartRequest>(body);

    var run = registry.Create(
        sessionId: string.IsNullOrWhiteSpace(start.SessionId) ? null : start.SessionId,
        graphSource: string.IsNullOrWhiteSpace(start.GraphSource) ? null : start.GraphSource,
        input: string.IsNullOrWhiteSpace(start.Input) ? null : start.Input);

    await engine.StartAsync(run).ConfigureAwait(false);

    var resp = new RunStartResponse { RunId = run.RunId };
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

// ------------------------------
// API: SSE event stream
// ------------------------------
app.MapGet("/api/runs/{runId}/events", async (HttpContext ctx, string runId, IRunRegistry registry) =>
{
    if (!registry.TryGet(runId, out var run))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("run not found").ConfigureAwait(false);
        return;
    }

    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.ContentType = "text/event-stream";

    // Initial hello event (helps UI show connected state)
    await WriteSseAsync(ctx, "hello", ProtobufJson.ToJson(new WorkflowStepEvent
    {
        RunId = run.RunId,
        StepId = "sse",
        StepName = "SSE Connected",
        Status = WorkflowStepStatus.Running,
        OutputChunk = "",
        Error = "",
        Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
    })).ConfigureAwait(false);

    var reader = run.EventChannel.Reader;
    while (await reader.WaitToReadAsync(ctx.RequestAborted).ConfigureAwait(false))
    {
        while (reader.TryRead(out var evt))
        {
            await WriteSseAsync(ctx, "step", ProtobufJson.ToJson(evt)).ConfigureAwait(false);
        }
    }
});

// ------------------------------
// API: Memory entries (MVP)
// ------------------------------
app.MapGet("/api/runs/{runId}/memory", (string runId, int? limit, IRunRegistry registry) =>
{
    if (!registry.TryGet(runId, out var run))
    {
        return Results.NotFound("run not found");
    }

    var take = limit is > 0 ? Math.Clamp(limit.Value, 1, 2000) : 200;
    var entries = run.MemoryEntries.ToArray();
    if (entries.Length > take)
    {
        entries = entries[^take..];
    }

    var resp = new ListMemoryEntriesResponse();
    resp.Entries.AddRange(entries);
    return Results.Text(ProtobufJson.ToJson(resp), "application/json; charset=utf-8");
});

app.Run();

static async Task<string> ReadBodyAsStringAsync(HttpRequest request)
{
    request.EnableBuffering();
    request.Body.Position = 0;
    using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
    request.Body.Position = 0;
    return body;
}

static async Task WriteSseAsync(HttpContext ctx, string eventName, string dataJson)
{
    // SSE format:
    // event: <name>
    // data: <payload>
    // \n
    await ctx.Response.WriteAsync($"event: {eventName}\n").ConfigureAwait(false);
    await ctx.Response.WriteAsync($"data: {dataJson}\n\n").ConfigureAwait(false);
    await ctx.Response.Body.FlushAsync().ConfigureAwait(false);
}


