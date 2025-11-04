var builder = DistributedApplication.CreateBuilder(args);

// 获取运行时配置
var runtimeType = builder.Configuration["AgentRuntime:RuntimeType"] ?? "Local";

Console.WriteLine($"🚀 Aspire AppHost - Agent Framework Demo");
Console.WriteLine($"📦 配置的运行时类型: {runtimeType}");

IResourceBuilder<ProjectResource> apiService;

switch (runtimeType.ToLower())
{
    case "local":
        // Local运行时 - 直接启动API
        Console.WriteLine("✅ 使用 Local 运行时（单机内存模式）");
        apiService = builder.AddProject<Projects.Demo_Api>("demo-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Local");
        break;

    case "orleans":
        // Orleans运行时 - API内部启动Orleans Silo
        Console.WriteLine("✅ 使用 Orleans 运行时（分布式模式）");
        Console.WriteLine("   注意: Orleans Silo 将在 API 内部启动");
        
        // 添加API服务，Orleans在API内部配置
        apiService = builder.AddProject<Projects.Demo_Api>("demo-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "Orleans");
        break;

    case "protoactor":
        // Proto.Actor运行时
        Console.WriteLine("✅ 使用 Proto.Actor 运行时");
        apiService = builder.AddProject<Projects.Demo_Api>("demo-api")
            .WithEnvironment("AgentRuntime__RuntimeType", "ProtoActor");
        break;

    default:
        throw new InvalidOperationException($"不支持的运行时类型: {runtimeType}");
}

Console.WriteLine("🌐 服务配置完成");
Console.WriteLine("");
Console.WriteLine("📊 访问 Aspire Dashboard: http://localhost:20888");
Console.WriteLine("   - 在 Dashboard 中查看所有服务的运行状态");
Console.WriteLine("");
Console.WriteLine("📈 Prometheus Metrics: http://localhost:7001/metrics");
Console.WriteLine("🔗 API Swagger: https://localhost:7001/swagger");
Console.WriteLine("");
Console.WriteLine("💡 Metrics 包含:");
Console.WriteLine("   - aevatar.agents.events.* (事件发布、处理、丢弃指标)");
Console.WriteLine("   - aevatar.agents.active.count (活跃 Actor 数)");
Console.WriteLine("   - aevatar.agents.exceptions (异常统计)");
Console.WriteLine("   - aevatar.agents.queue.length (队列长度)");

var app = builder.Build();
await app.RunAsync();
