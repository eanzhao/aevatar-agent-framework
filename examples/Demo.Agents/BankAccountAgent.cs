using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Abstractions.Attributes;
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
    public async Task HandleDeposit(DepositEvent deposit)
    {
        Logger?.LogInformation("BankAccount {Id} processing deposit of {Amount}", Id, deposit.Amount);
        
        // 创建状态变更事件
        var stateChange = new BankAccountStateChange
        {
            EventType = "Deposit",
            Amount = deposit.Amount,
            Description = deposit.Description
        };
        
        // ✅ V2 API: Raise and Confirm
        RaiseEvent(stateChange);
        await ConfirmEventsAsync();
    }
    
    [EventHandler]
    public async Task HandleWithdraw(WithdrawEvent withdraw)
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
            
            // ✅ V2 API: Raise and Confirm
            RaiseEvent(stateChange);
            await ConfirmEventsAsync();
        }
        else
        {
            Logger?.LogWarning("BankAccount {Id} insufficient balance for withdrawal of {Amount}", Id, withdraw.Amount);
        }
    }
    
    /// <summary>
    /// Pure functional state transition (V2 API)
    /// Framework automatically clones state, just modify it directly
    /// </summary>
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        if (evt is BankAccountStateChange change)
        {
            switch (change.EventType)
            {
                case "Deposit":
                    state.Balance += change.Amount;
                    state.TransactionCount++;
                    state.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
                    break;
                case "Withdraw":
                    state.Balance -= change.Amount;
                    state.TransactionCount++;
                    state.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
                    break;
            }
        }
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"BankAccount {Id}: Balance={State.Balance:C}, Transactions={State.TransactionCount}");
    }
}