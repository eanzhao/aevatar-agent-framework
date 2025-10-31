using Demo.Api;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 读取运行时配置
var runtimeOptions = builder.Configuration
    .GetSection(AgentRuntimeOptions.SectionName)
    .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

// 如果使用Orleans运行时，配置Orleans Host
if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
{
    builder.Host.UseOrleans((context, siloBuilder) =>
    {
        var orleansOptions = runtimeOptions.Orleans;
        
        if (orleansOptions.UseLocalhostClustering)
        {
            // 开发环境：本地集群
            siloBuilder.UseLocalhostClustering(
                siloPort: orleansOptions.SiloPort,
                gatewayPort: orleansOptions.GatewayPort);
        }
        else
        {
            // 生产环境：需要配置实际的集群
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });
        }
        
        // 添加内存存储（开发环境）
        siloBuilder.AddMemoryGrainStorage("AgentStore");
        
        Console.WriteLine($"🌐 Orleans Silo 配置完成");
        Console.WriteLine($"   ClusterId: {orleansOptions.ClusterId}");
        Console.WriteLine($"   ServiceId: {orleansOptions.ServiceId}");
        Console.WriteLine($"   SiloPort: {orleansOptions.SiloPort}");
        Console.WriteLine($"   GatewayPort: {orleansOptions.GatewayPort}");
    });
}

// 添加服务
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 添加Agent运行时（基于配置自动选择）
builder.Services.AddAgentRuntime(builder.Configuration);

var app = builder.Build();

// 配置HTTP管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// 显示当前使用的运行时
Console.WriteLine($"🚀 Agent Framework Demo API");
Console.WriteLine($"📦 运行时类型: {runtimeOptions.RuntimeType}");
Console.WriteLine($"🌐 Swagger UI: https://localhost:7001/swagger");

app.Run();

