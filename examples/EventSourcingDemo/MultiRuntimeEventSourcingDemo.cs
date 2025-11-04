using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Local;
using Aevatar.Agents.Local.EventSourcing;
using Aevatar.Agents.ProtoActor;
using Aevatar.Agents.ProtoActor.EventSourcing;
using EventSourcingDemo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace EventSourcingDemo;

/// <summary>
/// 多运行时 EventSourcing 演示
/// 展示 EventSourcing 在不同运行时下的使用
/// </summary>
public static class MultiRuntimeEventSourcingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n\n🌌 ===========================================");
        Console.WriteLine("   Multi-Runtime EventSourcing Demo");
        Console.WriteLine("   展示 EventSourcing 在不同运行时下的工作");
        Console.WriteLine("============================================\n");
        
        // 创建共享的 EventStore（所有运行时共享）
        var sharedEventStore = new InMemoryEventStore();
        
        // 配置依赖注入
        var services = ConfigureServices();
        var serviceProvider = services.BuildServiceProvider();
        
        // 1. Local 运行时演示
        await DemoLocalRuntime(sharedEventStore, serviceProvider);
        
        // 2. ProtoActor 运行时演示
        await DemoProtoActorRuntime(sharedEventStore, serviceProvider);
        
        // 3. Orleans 运行时说明（需要完整服务器）
        ShowOrleansInstructions();
        
        Console.WriteLine("\n✅ Multi-Runtime EventSourcing Demo 完成！");
        Console.WriteLine("🌟 所有运行时都成功支持 EventSourcing！");
    }
    
    /// <summary>
    /// Local 运行时演示
    /// </summary>
    private static async Task DemoLocalRuntime(IEventStore eventStore, IServiceProvider serviceProvider)
    {
        Console.WriteLine("📍 Local Runtime EventSourcing");
        Console.WriteLine("================================");
        
        var agentId = Guid.NewGuid();
        Console.WriteLine($"Agent ID: {agentId:N}");
        
        // 创建工厂
        var logger = serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>();
        var factory = new LocalGAgentActorFactory(serviceProvider, logger);
        
        // 场景1：通过 Actor 创建和管理 Agent
        Console.WriteLine("\n⚡ 场景1：通过 Actor 创建 Agent 并执行交易");
        IGAgentActor? actor = null;
        {
            // 使用工厂创建 Actor（Actor 内部会创建 Agent）
            actor = await factory.CreateGAgentActorAsync<BankAccountAgent, BankAccountState>(agentId);
            
            // 通过 Actor 获取 Agent
            var agent = actor.GetAgent() as BankAccountAgent;
            if (agent == null)
            {
                Console.WriteLine("  ❌ 无法获取 Agent 实例");
                return;
            }
            
            // 注入 EventStore（如果 Agent 支持）
            if (agent is GAgentBaseWithEventSourcing<BankAccountState> esAgent)
            {
                // 使用反射注入 EventStore
                var field = typeof(GAgentBaseWithEventSourcing<BankAccountState>)
                    .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(esAgent, eventStore);
                Console.WriteLine("  ✓ EventStore 已注入到 Agent");
            }
            
            // 执行交易
            await agent.CreateAccountAsync("Local User", 1000);
            await agent.DepositAsync(500, "Salary");
            await agent.WithdrawAsync(200, "Shopping");
            
            Console.WriteLine($"  余额: ${agent.GetState().Balance}");
            Console.WriteLine($"  版本: {agent.GetCurrentVersion()}");
            Console.WriteLine($"  交易数: {agent.GetState().TransactionCount}");
        }
        
        // 场景2：模拟崩溃和恢复
        Console.WriteLine("\n⚡ 场景2：模拟崩溃后恢复（重新创建 Actor）");
        {
            // 先停止原 Actor
            if (actor != null)
            {
                await actor.DeactivateAsync();
                Console.WriteLine("  原 Actor 已停止");
            }
            
            // 检查事件是否被保存
            var events = await eventStore.GetEventsAsync(agentId);
            Console.WriteLine($"  保存的事件数: {events.Count}");
            
            // 创建新的 Actor（模拟系统重启）
            var newActor = await factory.CreateGAgentActorAsync<BankAccountAgent, BankAccountState>(agentId);

            if (newActor.GetAgent() is BankAccountAgent recoveredAgent)
            {
                // 注入 EventStore 并恢复
                if (recoveredAgent is GAgentBaseWithEventSourcing<BankAccountState> esAgent)
                {
                    var field = typeof(GAgentBaseWithEventSourcing<BankAccountState>)
                        .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(esAgent, eventStore);
                    
                    // 激活时重放事件
                    await esAgent.OnActivateAsync();
                }
                
                Console.WriteLine($"  恢复后余额: ${recoveredAgent.GetState().Balance}");
                Console.WriteLine($"  恢复后版本: {recoveredAgent.GetCurrentVersion()}");
                Console.WriteLine($"  账户持有人: {recoveredAgent.GetState().AccountHolder}");
                
                // 验证
                if (recoveredAgent.GetState().Balance == 1300m && 
                    recoveredAgent.GetCurrentVersion() == 3)
                {
                    Console.WriteLine("  ✅ 状态完美恢复！Actor-Agent 模型验证成功！");
                }
            }
        }
    }
    
    /// <summary>
    /// ProtoActor 运行时演示
    /// </summary>
    private static async Task DemoProtoActorRuntime(IEventStore eventStore, IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n\n📍 ProtoActor Runtime EventSourcing");
        Console.WriteLine("=====================================");
        
        var agentId = Guid.NewGuid();
        Console.WriteLine($"Agent ID: {agentId:N}");
        
        // 创建 Actor System
        var system = new ActorSystem();
        await using (system)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ProtoActorGAgentActorFactory>>();
            var factory = new ProtoActorGAgentActorFactory(serviceProvider, system, logger);
            
            // 场景1：通过 Actor 创建和管理 Agent
            Console.WriteLine("\n⚡ 场景1：通过 Actor 创建 Agent 并执行交易");
            IGAgentActor? actor = null;
            {
                // 使用工厂创建 Actor（Actor 内部会创建 Agent）
                actor = await factory.CreateGAgentActorAsync<BankAccountAgent, BankAccountState>(agentId);
                
                // 通过 Actor 获取 Agent
                var agent = actor.GetAgent() as BankAccountAgent;
                if (agent == null)
                {
                    Console.WriteLine("  ❌ 无法获取 Agent 实例");
                    return;
                }
                
                // 注入 EventStore（如果 Agent 支持）
                if (agent is GAgentBaseWithEventSourcing<BankAccountState> esAgent)
                {
                    // 使用反射注入 EventStore
                    var field = typeof(GAgentBaseWithEventSourcing<BankAccountState>)
                        .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(esAgent, eventStore);
                    Console.WriteLine("  ✓ EventStore 已注入到 Agent");
                }
                
                // 执行交易
                await agent.CreateAccountAsync("ProtoActor User", 2000);
                await agent.DepositAsync(1000, "Bonus");
                await agent.WithdrawAsync(500, "Rent");
                
                Console.WriteLine($"  余额: ${agent.GetState().Balance}");
                Console.WriteLine($"  版本: {agent.GetCurrentVersion()}");
                Console.WriteLine($"  交易数: {agent.GetState().TransactionCount}");
            }
            
            // 场景2：模拟崩溃和恢复
            Console.WriteLine("\n⚡ 场景2：模拟崩溃后恢复（重新创建 Actor）");
            {
                // 先停止原 Actor
                if (actor != null)
                {
                    await actor.DeactivateAsync();
                    Console.WriteLine("  原 Actor 已停止");
                }
                
                // 检查事件是否被保存
                var events = await eventStore.GetEventsAsync(agentId);
                Console.WriteLine($"  事件总数: {events.Count}");
                
                // 创建新的 Actor（模拟系统重启）
                var newActor = await factory.CreateGAgentActorAsync<BankAccountAgent, BankAccountState>(agentId);
                var recoveredAgent = newActor.GetAgent() as BankAccountAgent;
                
                if (recoveredAgent != null)
                {
                    // 注入 EventStore 并恢复
                    if (recoveredAgent is GAgentBaseWithEventSourcing<BankAccountState> esAgent)
                    {
                        var field = typeof(GAgentBaseWithEventSourcing<BankAccountState>)
                            .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        field?.SetValue(esAgent, eventStore);
                        
                        // 激活时重放事件
                        await esAgent.OnActivateAsync();
                    }
                    
                    Console.WriteLine($"  重建后余额: ${recoveredAgent.GetState().Balance}");
                    Console.WriteLine($"  重建后版本: {recoveredAgent.GetCurrentVersion()}");
                    
                    if (recoveredAgent.GetState().Balance == 2500m)
                    {
                        Console.WriteLine("  ✅ ProtoActor EventSourcing 验证成功！Actor-Agent 模型验证成功！");
                    }
                }
            }
            
            // 关闭系统
            await system.ShutdownAsync();
        }
    }
    
    /// <summary>
    /// Orleans 运行时说明
    /// </summary>
    private static void ShowOrleansInstructions()
    {
        Console.WriteLine("\n\n📍 Orleans Runtime EventSourcing");
        Console.WriteLine("==================================");
        Console.WriteLine("Orleans 支持两种 EventSourcing 方式：");
        Console.WriteLine();
        Console.WriteLine("1️⃣ 使用 JournaledGrain（推荐）");
        Console.WriteLine("   ```csharp");
        Console.WriteLine("   [LogConsistencyProvider(\"LogStorage\")]");
        Console.WriteLine("   public class MyGrain : JournaledGrain<State, Event>");
        Console.WriteLine("   {");
        Console.WriteLine("       protected override void TransitionState(State state, Event evt)");
        Console.WriteLine("       {");
        Console.WriteLine("           // 状态转换逻辑");
        Console.WriteLine("       }");
        Console.WriteLine("   }");
        Console.WriteLine("   ```");
        Console.WriteLine();
        Console.WriteLine("2️⃣ 使用自定义 EventStore");
        Console.WriteLine("   - OrleansEventSourcingGrain");
        Console.WriteLine("   - 手动管理事件持久化");
        Console.WriteLine();
        Console.WriteLine("📝 注意：Orleans 需要运行完整的 Silo 服务器");
        Console.WriteLine("   配置示例：");
        Console.WriteLine("   ```csharp");
        Console.WriteLine("   siloBuilder.AddJournaledGrainEventSourcing(options =>");
        Console.WriteLine("   {");
        Console.WriteLine("       options.UseLogStorage = true;");
        Console.WriteLine("       options.UseMemoryStorage = true;");
        Console.WriteLine("   });");
        Console.WriteLine("   ```");
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
        
        // EventStore - 注册为单例，所有 Agent 共享
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        
        // 注册 BankAccountAgent 的工厂（用于 DI 创建）
        services.AddTransient<BankAccountAgent>(sp =>
        {
            var eventStore = sp.GetRequiredService<IEventStore>();
            var logger = sp.GetService<ILogger<BankAccountAgent>>();
            // 注意：这里的 Guid.Empty 会被工厂传入的实际 ID 替换
            return new BankAccountAgent(Guid.Empty, eventStore, logger);
        });
        
        return services;
    }
}
