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
Console.WriteLine("📊 Dashboard: https://localhost:15888");
Console.WriteLine("🔗 API: https://localhost:7001 (从 launchSettings.json 读取)");

var app = builder.Build();
await app.RunAsync();
