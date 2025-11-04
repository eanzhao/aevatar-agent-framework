using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Demo.Agents;

namespace Demo.Agents;

/// <summary>
/// 银行账户Agent - 支持Event Sourcing
/// </summary>
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    public BankAccountAgent(Guid id, IEventStore eventStore, ILogger<BankAccountAgent>? logger = null) 
        : base(id, eventStore, logger)
    {
    }
    
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // 如果State还没有初始化（没有事件可重放），设置初始值
        if (string.IsNullOrEmpty(State.AccountId))
        {
            State.AccountId = Id.ToString();
            State.LastTransaction = DateTimeOffset.UtcNow.ToTimestamp();
        }
    }
    
    [EventHandler]
    public async Task HandleDeposit(EventEnvelope envelope)
    {
        if (envelope.Payload != null && envelope.Payload.TryUnpack<DepositEvent>(out var deposit))
        {
            Logger?.LogInformation("BankAccount {Id} processing deposit of {Amount}", Id, deposit.Amount);
            
            // 创建状态变更事件
            var stateChange = new BankAccountStateChange
            {
                EventType = "Deposit",
                Amount = deposit.Amount,
                Description = deposit.Description
            };
            
            // 持久化状态变更
            await RaiseStateChangeEventAsync(Any.Pack(stateChange));
            
            // 应用状态变更
            await ApplyDepositAsync(deposit.Amount);
        }
    }
    
    [EventHandler]
    public async Task HandleWithdraw(EventEnvelope envelope)
    {
        if (envelope.Payload != null && envelope.Payload.TryUnpack<WithdrawEvent>(out var withdraw))
        {
            if (State.Balance >= withdraw.Amount)
            {
                Logger?.LogInformation("BankAccount {Id} processing withdrawal of {Amount}", Id, withdraw.Amount);
                
                // 创建状态变更事件
                var stateChange = new BankAccountStateChange
                {
                    EventType = "Withdraw",
                    Amount = withdraw.Amount,
                    Description = withdraw.Description
                };
                
                // 持久化状态变更
                await RaiseStateChangeEventAsync(Any.Pack(stateChange));
                
                // 应用状态变更
                await ApplyWithdrawAsync(withdraw.Amount);
            }
            else
            {
                Logger?.LogWarning("BankAccount {Id} insufficient balance for withdrawal of {Amount}", Id, withdraw.Amount);
            }
        }
    }
    
    protected override async Task ApplyStateChangeEventAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        if (evt is BankAccountStateChange change)
        {
            switch (change.EventType)
            {
                case "Deposit":
                    await ApplyDepositAsync(change.Amount);
                    break;
                case "Withdraw":
                    await ApplyWithdrawAsync(change.Amount);
                    break;
            }
        }
    }
    
    private Task ApplyDepositAsync(double amount)
    {
        State.Balance += amount;
        State.TransactionCount++;
        State.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        Logger?.LogInformation("BankAccount {Id} balance after deposit: {Balance}", Id, State.Balance);
        return Task.CompletedTask;
    }
    
    private Task ApplyWithdrawAsync(double amount)
    {
        State.Balance -= amount;
        State.TransactionCount++;
        State.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        Logger?.LogInformation("BankAccount {Id} balance after withdrawal: {Balance}", Id, State.Balance);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"BankAccount {Id}: Balance={State.Balance:C}, Transactions={State.TransactionCount}");
    }
}