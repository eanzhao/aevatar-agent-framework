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
        Console.WriteLine("✅ Using Local runtime (single-machine in-memory mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Local")
            .WithExternalHttpEndpoints();
        break;

    case "orleans":
        Console.WriteLine("✅ Using Orleans runtime (distributed mode)");
        tradingApi = builder.AddProject<Projects.Aevatar_Trade_Api>("trading-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Orleans")
            .WithExternalHttpEndpoints();
        break;

    default:
        throw new InvalidOperationException($"Unsupported runtime type: {runtimeType}");
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
Console.WriteLine("   POST /api/trading/initialize  - Initialize system");
Console.WriteLine("   POST /api/trading/start       - Start trading");
Console.WriteLine("   POST /api/trading/stop        - Stop trading");
Console.WriteLine("   GET  /api/trading/status      - System status");
Console.WriteLine("   GET  /api/agents              - Agent status");
Console.WriteLine();

// ============ Build & Run ============

var app = builder.Build();
await app.RunAsync();
