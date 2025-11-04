using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using EventSourcingDemo;
using Microsoft.Extensions.Logging;

Console.WriteLine("🌌 Aevatar Agent Framework - EventSourcing Demo");
Console.WriteLine("==============================================\n");

// 创建 EventStore
var eventStore = new InMemoryEventStore();

// 创建支持 EventSourcing 的 Agent
var agentId = Guid.NewGuid();
var agent = new BankAccountAgent(agentId, eventStore);

// 创建账户
await agent.CreateAccountAsync("Alice Smith", 100);

Console.WriteLine($"📊 Bank Account Agent Created");
Console.WriteLine($"   Agent ID: {agentId}");
Console.WriteLine($"   Account Holder: {agent.GetState().AccountHolder}");
Console.WriteLine($"   Initial Balance: ${agent.GetState().Balance}\n");

// 执行一系列操作
Console.WriteLine("💰 Performing transactions:");
Console.WriteLine("──────────────────────────");

await agent.DepositAsync(1000, "Salary");
Console.WriteLine($"  ✅ Deposited $1000 (Salary)");

await agent.DepositAsync(500, "Bonus");
Console.WriteLine($"  ✅ Deposited $500 (Bonus)");

await agent.WithdrawAsync(300, "Rent");
Console.WriteLine($"  ✅ Withdrew $300 (Rent)");

await agent.DepositAsync(200, "Freelance");
Console.WriteLine($"  ✅ Deposited $200 (Freelance)");

Console.WriteLine($"\n💵 Current Balance: ${agent.GetState().Balance}");
Console.WriteLine($"📈 Current Version: {agent.GetCurrentVersion()}");

// 查看事件历史
var events = await eventStore.GetEventsAsync(agentId);
Console.WriteLine($"\n📝 Event History ({events.Count} events):");
Console.WriteLine("──────────────────────────");
foreach (var evt in events)
{
    Console.WriteLine($"  v{evt.Version}: {evt.EventType.Split('.').Last()} at {evt.TimestampUtc:HH:mm:ss}");
}

// 显示交易历史
Console.WriteLine($"\n📜 Transaction History:");
Console.WriteLine("──────────────────────────");
foreach (var history in agent.GetState().History)
{
    Console.WriteLine($"  {history}");
}

// 模拟崩溃和恢复
Console.WriteLine($"\n\n💥 Simulating crash and recovery...");
Console.WriteLine("──────────────────────────");

// 创建新的 Agent 实例（模拟重启）
var recoveredAgent = new BankAccountAgent(agentId, eventStore);

Console.WriteLine($"   Initial state: Balance = ${recoveredAgent.GetState().Balance}");
Console.WriteLine($"   Initial version: {recoveredAgent.GetCurrentVersion()}");

// 激活 Agent（会自动重放事件）
await recoveredAgent.OnActivateAsync();

Console.WriteLine($"\n✅ State recovered from events!");
Console.WriteLine($"   Recovered Balance: ${recoveredAgent.GetState().Balance}");
Console.WriteLine($"   Recovered Version: {recoveredAgent.GetCurrentVersion()}");
Console.WriteLine($"   Transaction Count: {recoveredAgent.GetState().TransactionCount}");
Console.WriteLine($"   Account Holder: {recoveredAgent.GetState().AccountHolder}");

// 验证
if (recoveredAgent.GetState().Balance == agent.GetState().Balance &&
    recoveredAgent.GetCurrentVersion() == agent.GetCurrentVersion() &&
    recoveredAgent.GetState().AccountHolder == agent.GetState().AccountHolder)
{
    Console.WriteLine($"\n🎉 EventSourcing verified! State perfectly recovered!");
    
    // 继续操作恢复的 Agent
    Console.WriteLine($"\n💳 Continuing with recovered agent:");
    Console.WriteLine("──────────────────────────");
    
    await recoveredAgent.WithdrawAsync(100, "Coffee");
    Console.WriteLine($"  ✅ Withdrew $100 (Coffee)");
    
    Console.WriteLine($"\n💵 Final Balance: ${recoveredAgent.GetState().Balance}");
    Console.WriteLine($"📈 Final Version: {recoveredAgent.GetCurrentVersion()}");
}

Console.WriteLine($"\n✅ EventSourcing Demo completed successfully!");

// 运行多运行时演示
await MultiRuntimeEventSourcingDemo.RunAsync();