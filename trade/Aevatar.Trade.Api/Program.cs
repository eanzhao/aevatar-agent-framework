using Aevatar.Agents.Core.Extensions;
using Aevatar.Trade;
using Aevatar.Trade.Api;
using Aevatar.Trade.Api.Extensions;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ============ Aspire + Observability ============

builder.AddServiceDefaults();
builder.AddAevatarObservability();

// ============ Runtime Configuration ============

var runtimeOptions = builder.Configuration
    .GetSection(AgentRuntimeOptions.SectionName)
    .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

// Orleans 配置
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

        Console.WriteLine($"🌐 Orleans Silo 配置完成");
        Console.WriteLine($"   ClusterId: {orleansOptions.ClusterId}");
        Console.WriteLine($"   ServiceId: {orleansOptions.ServiceId}");
    });
}

// ============ Services ============

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "WEEX AI Trading API",
        Version = "v1",
        Description = "基于多智能体协作的 AI 交易系统"
    });
});

// Agent 运行时
builder.Services.AddAgentRuntime(builder.Configuration);
builder.Services.AddGAgentActorFactoryProvider();

// WEEX 交易服务
builder.Services.AddWeexTradingServices(builder.Configuration);

// LLM Provider (使用 MEAI)
builder.Services.AddMEAILLMProvider(builder.Configuration);

// 交易系统
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
Console.WriteLine("║    🔌 DataCollector    - 数据采集                             ║");
Console.WriteLine("║    😱 SentimentAgent   - 情绪分析 (AI)                        ║");
Console.WriteLine("║    📊 TechnicalAgent   - 技术分析 (AI)                        ║");
Console.WriteLine("║    🎯 Coordinator      - 决策协调 (AI)                        ║");
Console.WriteLine("║    🛡️ RiskManager      - 风控管理 (AI)                        ║");
Console.WriteLine("║    ⚡ Executor         - 交易执行                             ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

app.Run();
