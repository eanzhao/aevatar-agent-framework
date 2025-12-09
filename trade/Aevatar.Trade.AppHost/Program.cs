var builder = DistributedApplication.CreateBuilder(args);

// ============ Configuration ============

var runtimeType = builder.Configuration["AgentRuntime:RuntimeType"] ?? "Local";

// ============ Banner ============

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║     🚀 WEEX AI Trading System - Aspire AppHost                ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Runtime: {runtimeType,-20}                         ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ============ Services ============

IResourceBuilder<ProjectResource> tradingApi;

switch (runtimeType.ToLower())
{
    case "local":
        Console.WriteLine("✅ 使用 Local 运行时（单机内存模式）");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Local")
            .WithExternalHttpEndpoints();
        break;

    case "orleans":
        Console.WriteLine("✅ 使用 Orleans 运行时（分布式模式）");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Orleans")
            .WithExternalHttpEndpoints();
        break;

    default:
        throw new InvalidOperationException($"不支持的运行时类型: {runtimeType}");
}

// ============ Dashboard Info ============

Console.WriteLine();
Console.WriteLine("📊 Aspire Dashboard: http://localhost:15888");
Console.WriteLine();
Console.WriteLine("🌐 Trading API Endpoints:");
Console.WriteLine("   📖 Swagger:    https://localhost:7100/swagger");
Console.WriteLine("   📈 Metrics:    https://localhost:7100/metrics");
Console.WriteLine("   💓 Health:     https://localhost:7100/health");
Console.WriteLine();
Console.WriteLine("🎮 Trading API Operations:");
Console.WriteLine("   POST /api/trading/initialize  - 初始化系统");
Console.WriteLine("   POST /api/trading/start       - 启动交易");
Console.WriteLine("   POST /api/trading/stop        - 停止交易");
Console.WriteLine("   GET  /api/trading/status      - 系统状态");
Console.WriteLine("   GET  /api/agents              - Agent 状态");
Console.WriteLine();

// ============ Build & Run ============

var app = builder.Build();
await app.RunAsync();
