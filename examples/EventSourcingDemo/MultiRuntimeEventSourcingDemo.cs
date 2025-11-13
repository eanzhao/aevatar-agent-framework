using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventSourcingDemo;

/// <summary>
/// 多运行时 EventSourcing 演示（V2）
/// 使用新的 WithEventSourcingAsync 扩展方法
/// </summary>
public static class MultiRuntimeEventSourcingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n\n");
        Console.WriteLine("🌌 ═══════════════════════════════════════════════════");
        Console.WriteLine("   Multi-Runtime EventSourcing Demo V2");
        Console.WriteLine("   展示 EventSourcing 在不同运行时下的统一API");
        Console.WriteLine("═══════════════════════════════════════════════════\n");
        
        // 配置依赖注入
        var services = ConfigureServices();
        var serviceProvider = services.BuildServiceProvider();
        
        // 创建共享的 EventStore（所有运行时共享同一个存储）
        var sharedEventStore = serviceProvider.GetRequiredService<InMemoryEventStore>();
        
        // 1. Local 运行时演示
        await DemoLocalRuntime(sharedEventStore, serviceProvider);
        
        // 2. Orleans 运行时说明
        ShowOrleansInstructions();
        
        Console.WriteLine("\n✅ Multi-Runtime EventSourcing Demo V2 完成！");
        Console.WriteLine("🌟 所有运行时都使用统一的 EventSourcing API！");
    }
    
    /// <summary>
    /// Local 运行时演示（使用新 API）
    /// </summary>
    private static async Task DemoLocalRuntime(InMemoryEventStore eventStore, IServiceProvider serviceProvider)
    {
        Console.WriteLine("📍 Local Runtime EventSourcing (V2)");
        Console.WriteLine("════════════════════════════════════════");
        
        var agentId = Guid.NewGuid();
        Console.WriteLine($"Agent ID: {agentId:N}");
        
        // 创建工厂
        var logger = serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>();
        var factory = new LocalGAgentActorFactory(serviceProvider, logger);
        
        // ✅ 场景1：使用新的 WithEventSourcingAsync 扩展方法
        Console.WriteLine("\n⚡ 场景1：使用 WithEventSourcingAsync 扩展方法");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 创建 Actor 并启用 EventSourcing（一行搞定！）
        var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId)
            .WithEventSourcingAsync(eventStore, serviceProvider);  // ✅ 新API
        
        var agent = actor.GetAgent() as BankAccountAgent;
        if (agent == null)
        {
            Console.WriteLine("  ❌ 无法获取 Agent 实例");
            return;
        }
        
        Console.WriteLine("  ✓ Actor 创建成功");
        Console.WriteLine("  ✓ EventSourcing 自动启用");
        
        // 执行交易
        await agent.CreateAccountAsync("Local User V2", 1000);
        await agent.DepositAsync(500, "Salary");
        await agent.WithdrawAsync(200, "Shopping");
        
        Console.WriteLine($"\n  💵 Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 Version: v{agent.GetCurrentVersion()}");
        Console.WriteLine($"  🔢 Transactions: {agent.GetState().TransactionCount}");
        
        // ✅ 场景2：批量交易演示
        Console.WriteLine("\n⚡ 场景2：批量交易（展示批量提交优势）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        var batchTransactions = new[]
        {
            ("deposit", 300m, "Bonus"),
            ("deposit", 100m, "Refund"),
            ("withdraw", 50m, "Coffee")
        };
        
        await agent.BatchTransactionsAsync(batchTransactions);
        
        Console.WriteLine($"  ✓ Batch completed (3 transactions in 1 commit)");
        Console.WriteLine($"  💵 New Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 New Version: v{agent.GetCurrentVersion()}");
        
        // ✅ 场景3：崩溃恢复
        Console.WriteLine("\n⚡ 场景3：崩溃恢复（自动事件重放）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 停止原 Actor
        await actor.DeactivateAsync();
        Console.WriteLine("  ✓ 原 Actor 已停止");
        
        // 检查事件
        var events = await eventStore.GetEventsAsync(agentId);
        Console.WriteLine($"  📝 EventStore 中的事件数: {events.Count}");
        
        // 创建新 Actor 并自动恢复
        var newActor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId)
            .WithEventSourcingAsync(eventStore, serviceProvider);  // ✅ 自动重放
        
        var recoveredAgent = newActor.GetAgent() as BankAccountAgent;
        if (recoveredAgent != null)
        {
            Console.WriteLine($"\n  ✅ 状态完美恢复:");
            Console.WriteLine($"     Balance: ${recoveredAgent.GetState().Balance:F2}");
            Console.WriteLine($"     Version: v{recoveredAgent.GetCurrentVersion()}");
            Console.WriteLine($"     Holder: {recoveredAgent.GetState().AccountHolder}");
            Console.WriteLine($"     Transactions: {recoveredAgent.GetState().TransactionCount}");
            
            // 验证
            if (recoveredAgent.GetState().Balance == 1650.0 && 
                recoveredAgent.GetCurrentVersion() == 7)  // 1 create + 2 individual + 3 batch + 0 (batch is 1 commit)
            {
                Console.WriteLine($"\n  🎉 Local Runtime EventSourcing V2 验证成功!");
            }
        }
        
        // ✅ 场景4：展示事件元数据
        Console.WriteLine("\n⚡ 场景4：事件元数据（用于审计和调试）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 获取最近的几个事件
        var recentEvents = await eventStore.GetEventsAsync(agentId, fromVersion: 1, maxCount: 5);
        Console.WriteLine($"  📝 最近 {recentEvents.Count} 个事件:");
        
        foreach (var evt in recentEvents)
        {
            var eventName = evt.EventType.Split('.').Last();
            var metadataStr = evt.Metadata.Any() 
                ? $" | Metadata: {string.Join(", ", evt.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}"
                : "";
            Console.WriteLine($"     v{evt.Version}: {eventName,-20}{metadataStr}");
        }
        
        Console.WriteLine($"\n  ✅ Local Runtime 演示完成!");
    }
    
    /// <summary>
    /// Orleans 运行时说明（V2 更新）
    /// </summary>
    private static void ShowOrleansInstructions()
    {
        Console.WriteLine("\n\n📍 Orleans Runtime EventSourcing (V2)");
        Console.WriteLine("════════════════════════════════════════════");
        Console.WriteLine("✅ Orleans 现在使用统一的 IEventStore 接口！\n");
        
        Console.WriteLine("🔧 配置方式 (Silo):");
        Console.WriteLine("───────────────────────────────────────────────");
        Console.WriteLine("```csharp");
        Console.WriteLine("siloBuilder.AddAgentEventSourcing(options =>");
        Console.WriteLine("{");
        Console.WriteLine("    options.UseInMemoryStore = false;  // 使用 OrleansEventStore");
        Console.WriteLine("    options.StorageProvider = \"EventStoreStorage\";");
        Console.WriteLine("});");
        Console.WriteLine("");
        Console.WriteLine("// 配置 GrainStorage");
        Console.WriteLine("siloBuilder.AddMemoryGrainStorage(\"EventStoreStorage\");");
        Console.WriteLine("// 或使用其他存储:");
        Console.WriteLine("// siloBuilder.AddAzureTableGrainStorage(\"EventStoreStorage\", ...);");
        Console.WriteLine("```\n");
        
        Console.WriteLine("💡 使用方式 (Client):");
        Console.WriteLine("───────────────────────────────────────────────");
        Console.WriteLine("```csharp");
        Console.WriteLine("// 方式1: 通过工厂创建");
        Console.WriteLine("var factory = new OrleansGAgentActorFactory(grainFactory, serviceProvider, logger);");
        Console.WriteLine("var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId)");
        Console.WriteLine("    .WithEventSourcingAsync(eventStore);  // ✅ 统一API");
        Console.WriteLine("");
        Console.WriteLine("// 方式2: 直接使用 Grain");
        Console.WriteLine("var grain = grainFactory.GetGrain<IStandardGAgentGrain>(agentId.ToString());");
        Console.WriteLine("await grain.ActivateAsync();");
        Console.WriteLine("```\n");
        
        Console.WriteLine("🌟 统一的 EventSourcing 特性:");
        Console.WriteLine("  ✓ 批量事件提交 (RaiseEvent + ConfirmEventsAsync)");
        Console.WriteLine("  ✓ 纯函数式状态转换 (TransitionState)");
        Console.WriteLine("  ✓ 自动事件重放 (OnActivateAsync)");
        Console.WriteLine("  ✓ 快照支持 (Snapshot Strategy)");
        Console.WriteLine("  ✓ 乐观并发控制 (Optimistic Concurrency)");
        Console.WriteLine("  ✓ 元数据支持 (Metadata)");
        Console.WriteLine("  ✓ GrainStorage 持久化 (支持多种存储提供者)");
        
        Console.WriteLine("\n📝 存储提供者支持:");
        Console.WriteLine("  • MemoryGrainStorage (开发/测试)");
        Console.WriteLine("  • AzureTableGrainStorage (生产)");
        Console.WriteLine("  • AdoNetGrainStorage (SQL数据库)");
        Console.WriteLine("  • 自定义存储提供者");
        
        Console.WriteLine("\n💡 提示: Orleans 需要运行完整的 Silo 服务器");
        Console.WriteLine("        详见: examples/Demo.AppHost/Program.cs");
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        
        // 日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        // EventStore - 注册为单例（所有运行时共享）
        services.AddSingleton<InMemoryEventStore>();
        
        return services;
    }
}
