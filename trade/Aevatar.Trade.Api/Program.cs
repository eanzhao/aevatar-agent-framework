using Aevatar.Agents.Core.Extensions;
using Aevatar.Trade;
using Aevatar.Trade.Api;
using Aevatar.Trade.Api.Extensions;
using Aevatar.Trade.Infrastructure.WeexApi;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ============ Secrets ============
// Load local secrets file (gitignored) for quick hackathon setup.
// NOTE: Environment variables can still be used, but this file enables "drop-in" setup on a new machine.
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

// ============ Aspire + Observability ============

builder.AddServiceDefaults();
builder.AddAevatarObservability();

// ============ Export WEEX credentials to env (for dotnet file skills) ============
// DotNetFileSkillTool executes a separate process (dotnet run --file) which reads WEEX_* from env.
// We bridge configuration -> env here so users only need to set appsettings.secrets.json.
var weexEnv = builder.Configuration.GetSection("Weex").Get<WeexApiConfig>();
if (weexEnv != null)
{
    if (!string.IsNullOrWhiteSpace(weexEnv.BaseUrl))
        Environment.SetEnvironmentVariable("WEEX_BASE_URL", weexEnv.BaseUrl);
    if (!string.IsNullOrWhiteSpace(weexEnv.ApiKey))
        Environment.SetEnvironmentVariable("WEEX_API_KEY", weexEnv.ApiKey);
    if (!string.IsNullOrWhiteSpace(weexEnv.ApiSecret))
        Environment.SetEnvironmentVariable("WEEX_API_SECRET", weexEnv.ApiSecret);
    if (!string.IsNullOrWhiteSpace(weexEnv.Passphrase))
        Environment.SetEnvironmentVariable("WEEX_PASSPHRASE", weexEnv.Passphrase);
}

// ============ Runtime Configuration ============

var runtimeOptions = builder.Configuration
    .GetSection(AgentRuntimeOptions.SectionName)
    .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

// Orleans configuration
if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
{
    builder.Host.UseOrleans((context, siloBuilder) =>
    {
        var orleansOptions = runtimeOptions.Orleans;

        if (orleansOptions.UseLocalhostClustering)
        {
            siloBuilder.UseLocalhostClustering(
                siloPort: orleansOptions.SiloPort,
                gatewayPort: orleansOptions.GatewayPort);
        }
        else
        {
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });
        }

        siloBuilder.AddMemoryGrainStorage("AgentStore");

        Console.WriteLine($"🌐 Orleans Silo configuration completed");
        Console.WriteLine($"   ClusterId: {orleansOptions.ClusterId}");
        Console.WriteLine($"   ServiceId: {orleansOptions.ServiceId}");
    });
}

// ============ Services ============

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// NOTE:
// - Avoid depending on Microsoft.OpenApi.Models.OpenApiInfo explicitly here to keep the sample lightweight.
// - Default swagger doc is good enough for early integration/debugging.
builder.Services.AddSwaggerGen();

// Agent runtime
builder.Services.AddAgentRuntime(builder.Configuration);
builder.Services.AddGAgentActorFactoryProvider();

// WEEX trading services
builder.Services.AddWeexTradingServices(builder.Configuration);

// LLM Provider (using MEAI)
builder.Services.AddMEAILLMProvider(builder.Configuration);

// Trading system
builder.Services.AddSingleton<TradingSystem>();

// ============ Build & Configure ============

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WEEX AI Trading API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UsePrometheusMetrics();

app.MapControllers();
app.MapDefaultEndpoints();

// ============ Startup Banner ============

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║        🤖 WEEX AI Trading System - Multi-Agent Trading        ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Runtime: {runtimeOptions.RuntimeType,-20}                         ║");
Console.WriteLine("║                                                               ║");
Console.WriteLine("║  Endpoints:                                                   ║");
Console.WriteLine("║    📊 Swagger:  https://localhost:7100/swagger                ║");
Console.WriteLine("║    📈 Metrics:  https://localhost:7100/metrics                ║");
Console.WriteLine("║    💓 Health:   https://localhost:7100/health                 ║");
Console.WriteLine("║                                                               ║");
Console.WriteLine("║  Agents:                                                      ║");
Console.WriteLine("║    🔌 DataCollector    - Data Collection                      ║");
Console.WriteLine("║    😱 SentimentAgent   - Sentiment Analysis (AI)             ║");
Console.WriteLine("║    📊 TechnicalAgent   - Technical Analysis (AI)            ║");
Console.WriteLine("║    🎯 Coordinator      - Decision Coordination (AI)          ║");
Console.WriteLine("║    🛡️ RiskManager      - Risk Management (AI)                 ║");
Console.WriteLine("║    ⚡ Executor         - Trade Execution                     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

app.Run();
